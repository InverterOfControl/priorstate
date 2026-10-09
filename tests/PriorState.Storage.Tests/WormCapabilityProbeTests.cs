using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using PriorState.Domain.Entities;
using PriorState.Storage;

namespace PriorState.Storage.Tests;

public sealed class WormCapabilityProbeTests
{
    [Theory]
    [InlineData("enforced", WormSupport.Enforced)]
    [InlineData("delete-succeeds", WormSupport.ApiPresentUnverified)]
    [InlineData("no-version", WormSupport.ApiPresentUnverified)]
    [InlineData("wrong-retention", WormSupport.ApiPresentUnverified)]
    [InlineData("expired", WormSupport.ApiPresentUnverified)]
    [InlineData("control-denied", WormSupport.ApiPresentUnverified)]
    [InlineData("default-retention", WormSupport.ApiPresentUnverified)]
    [InlineData("server-error", WormSupport.ApiPresentUnverified)]
    [InlineData("write-denied", WormSupport.ApiPresentUnverified)]
    [InlineData("retention-denied", WormSupport.ApiPresentUnverified)]
    public async Task Probe_OnlyConfirmsRetentionAfterVersionAndPermissionChecks(string scenario, WormSupport expected)
    {
        using var client = new ProbeClient(scenario);
        var probe = new WormCapabilityProbe(client, NullLogger<WormCapabilityProbe>.Instance);
        var result = await probe.ProbeAsync(new ObjectStoreOptions { AccessKey = "test", SecretKey = "test" });
        Assert.Equal(expected, result);
        if (scenario is "enforced" or "delete-succeeds" or "server-error")
            Assert.Equal(new[] { "control-version", "protected-version" }, client.DeletedVersions);
        else
            Assert.DoesNotContain("protected-version", client.DeletedVersions);
    }

    private sealed class ProbeClient(string scenario) : AmazonS3Client(
        new BasicAWSCredentials("test", "test"),
        new AmazonS3Config { ServiceURL = "http://localhost:3900", AuthenticationRegion = "test" })
    {
        private DateTime? _retainUntil;
        public List<string> DeletedVersions { get; } = [];

        public override Task<GetObjectLockConfigurationResponse> GetObjectLockConfigurationAsync(
            GetObjectLockConfigurationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetObjectLockConfigurationResponse
            {
                ObjectLockConfiguration = new ObjectLockConfiguration { ObjectLockEnabled = ObjectLockEnabled.Enabled },
            });

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            if (scenario == "write-denied") throw Denied();
            if (request.ObjectLockMode == ObjectLockMode.Compliance)
            {
                _retainUntil = request.ObjectLockRetainUntilDate;
                return Task.FromResult(new PutObjectResponse { VersionId = scenario == "no-version" ? null : "protected-version" });
            }
            return Task.FromResult(new PutObjectResponse { VersionId = "control-version" });
        }

        public override Task<GetObjectRetentionResponse> GetObjectRetentionAsync(
            GetObjectRetentionRequest request, CancellationToken cancellationToken = default)
        {
            if (scenario == "retention-denied") throw Denied();
            if (request.VersionId == "control-version")
                return Task.FromResult(new GetObjectRetentionResponse
                {
                    Retention = scenario == "default-retention"
                        ? new ObjectLockRetention { Mode = ObjectLockRetentionMode.Compliance, RetainUntilDate = _retainUntil }
                        : null,
                });
            Assert.Equal("protected-version", request.VersionId);
            return Task.FromResult(new GetObjectRetentionResponse
            {
                Retention = new ObjectLockRetention
                {
                    Mode = scenario == "wrong-retention" ? ObjectLockRetentionMode.Governance : ObjectLockRetentionMode.Compliance,
                    RetainUntilDate = scenario == "expired" ? DateTime.UtcNow.AddMinutes(-1) : _retainUntil,
                },
            });
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
        {
            Assert.False(string.IsNullOrWhiteSpace(request.VersionId));
            DeletedVersions.Add(request.VersionId);
            if (request.VersionId == "control-version")
            {
                if (scenario == "control-denied") throw Denied();
                return Task.FromResult(new DeleteObjectResponse());
            }
            if (scenario == "server-error")
                throw new AmazonS3Exception("Server error") { StatusCode = HttpStatusCode.InternalServerError };
            if (scenario == "delete-succeeds") return Task.FromResult(new DeleteObjectResponse());
            throw Denied();
        }

        private static AmazonS3Exception Denied() => new("Access denied")
        {
            StatusCode = HttpStatusCode.Forbidden, ErrorCode = "AccessDenied",
        };
    }
}
