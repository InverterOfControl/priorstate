using PriorState.Api.Endpoints;
using PriorState.Domain.ValueObjects;

namespace PriorState.Api.Tests;

public sealed class ProfileValidationTests
{
    private static readonly CaptureConditions Valid = new()
    {
        UserAgent = "Test browser", ViewportWidth = 1920, ViewportHeight = 1080,
        AuthenticatedSession = false, AdBlockerActive = false,
        CookieBanner = CookieBannerHandling.LeftAsIs, JavaScriptSettleMs = 5000,
        ChromiumVersion = "unknown-until-first-capture", CrawlerVersion = "unknown-until-first-capture",
    };

    [Fact]
    public void AcceptsValidProfile() =>
        Assert.Null(new CreateProfileVersionRequest("Mobile", "Mobile layout", Valid).Validate());

    [Theory]
    [InlineData("", "Reason")]
    [InlineData("   ", "Reason")]
    [InlineData("Mobile", " ")]
    public void RequiresNameAndRationale(string name, string rationale) =>
        Assert.NotNull(new CreateProfileVersionRequest(name, rationale, Valid).Validate());

    [Fact]
    public void RejectsInvalidConditions()
    {
        CaptureConditions[] invalid = [
            Valid with { ViewportWidth = 0 }, Valid with { ViewportHeight = 7681 },
            Valid with { JavaScriptSettleMs = -1000 }, Valid with { JavaScriptSettleMs = 300001 },
            Valid with { JavaScriptSettleMs = 1500 }, Valid with { UserAgent = " " },
            Valid with { AuthenticatedSession = true }, Valid with { AdBlockerActive = true },
            Valid with { CookieBanner = (CookieBannerHandling)99 },
            Valid with { CrawlerVersion = new string('x', 65) },
        ];
        foreach (var conditions in invalid)
        {
            Assert.NotNull(new CreateProfileVersionRequest("Mobile", "Reason", conditions).Validate());
        }
    }
}
