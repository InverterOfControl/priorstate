using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PriorState.Plugins.Abstractions;
using PriorState.Plugins.Git;

namespace PriorState.Plugins.Git.Tests;

public sealed class GitCapturePluginTests
{
    [Theory]
    [InlineData("http://example.com/repo", "HEAD")]
    [InlineData("https://token@example.com/repo", "HEAD")]
    [InlineData("https://example.com/repo?token=secret", "HEAD")]
    [InlineData("https://example.com/repo", "--upload-pack=bad")]
    [InlineData("https://example.com/repo", "main:refs/heads/other")]
    [InlineData("https://example.com/repo", "HEAD~1")]
    public void RejectsUnsafeConfiguration(string url, string reference)
    {
        var plugin = new GitCapturePlugin(Options.Create(new GitOptions()));
        Assert.Throws<PluginException>(() => plugin.ValidateConfiguration(JsonSerializer.Serialize(new { url, reference }), "test", null));
    }

    [Fact]
    public async Task ArchivesOriginalBytesAndStableCommitTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "priorstate-git-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Git(root, "init");
            var bytes = new byte[] { 0, 13, 10, 255, 42 };
            await File.WriteAllBytesAsync(Path.Combine(root, "binary.bin"), bytes);
            await File.WriteAllTextAsync(Path.Combine(root, "lfs.txt"), "version https://git-lfs.github.com/spec/v1\noid sha256:abc\nsize 100\n");
            await Git(root, "add", ".");
            await Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-m", "fixture");
            var commit = (await Git(root, "rev-parse", "HEAD")).Trim();
            await Git(root, "update-index", "--add", "--cacheinfo", "160000," + commit + ",submodule");
            await Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-m", "gitlink fixture");
            await Git(root, "update-ref", "FETCH_HEAD", "HEAD");
            var plugin = new GitCapturePlugin(Options.Create(new GitOptions()));
            var config = new GitConfiguration { Url = "https://example.com/repo" };
            var first = await plugin.ArchiveAsync(root, root, "unused", config, null, CancellationToken.None);
            var second = await plugin.ArchiveAsync(root, root, "unused", config, null, CancellationToken.None);
            Assert.Equal(first, second);
            using var zip = new ZipArchive(new MemoryStream(first));
            using var stream = zip.GetEntry("files/binary.bin")!.Open();
            using var actual = new MemoryStream();
            await stream.CopyToAsync(actual);
            Assert.Equal(bytes, actual.ToArray());
            using var manifestStream = zip.GetEntry("manifest.json")!.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            Assert.Equal(40, manifest.RootElement.GetProperty("commit").GetString()!.Length);
            Assert.Equal(3, manifest.RootElement.GetProperty("entries").GetArrayLength());
            Assert.Null(zip.GetEntry("files/submodule"));
            Assert.Contains(manifest.RootElement.GetProperty("entries").EnumerateArray(), e => e.GetProperty("type").GetString() == "commit" && e.GetProperty("objectId").GetString() == commit);
            var limited = new GitCapturePlugin(Options.Create(new GitOptions { MaxPayloadBytes = 2 }));
            await Assert.ThrowsAsync<PluginException>(() => limited.ArchiveAsync(root, root, "unused", config, null, CancellationToken.None));
            var fileLimited = new GitCapturePlugin(Options.Create(new GitOptions { MaxFiles = 1 }));
            await Assert.ThrowsAsync<PluginException>(() => fileLimited.ArchiveAsync(root, root, "unused", config, null, CancellationToken.None));
            var storageLimited = new GitCapturePlugin(Options.Create(new GitOptions { MaxTemporaryBytes = 1 }));
            await Assert.ThrowsAsync<PluginException>(() => storageLimited.ArchiveAsync(root, root, "unused", config, null, CancellationToken.None));
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ArchiveAsync(root, root, "unused", config, null, cancelled.Token));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static async Task<string> Git(string root, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
        return output;
    }
}
