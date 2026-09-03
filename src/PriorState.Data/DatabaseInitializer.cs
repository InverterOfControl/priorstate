using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Data;

/// <summary>
/// Applies migrations and seeds the baseline capture profile, so that a first
/// <c>docker compose up</c> reaches a usable system with no manual steps.
/// </summary>
public sealed partial class DatabaseInitializer
{
    /// <summary>
    /// Fixed id for the seeded profile. Deterministic so that the same profile is recognisable
    /// across installations, which matters when two parties compare evidence packages.
    /// </summary>
    public static readonly Guid StandardProfileId = Guid.Parse("00000000-0000-7000-8000-0000de010001");

    private readonly PriorStateDbContext _db;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(PriorStateDbContext db, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.MigrateAsync(cancellationToken);
        LogMigrationsApplied();

        if (await _db.CaptureProfileVersions.AnyAsync(cancellationToken))
        {
            return;
        }

        _db.CaptureProfileVersions.Add(new CaptureProfileVersion
        {
            Id = StandardProfileId,
            Name = "DE-Standard",
            Version = 1,
            Rationale =
                "Baseline profile: a neutral, unauthenticated desktop visit with no content blocking and "
                + "the cookie banner left exactly as the site serves it. Chosen so that the capture "
                + "represents what an ordinary visitor sees and requires no explanation of why any "
                + "setting was changed.",
            Conditions = new CaptureConditions
            {
                UserAgent =
                    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) "
                    + "Chrome/140.0.0.0 Safari/537.36",
                ViewportWidth = 1920,
                ViewportHeight = 1080,
                AuthenticatedSession = false,
                AdBlockerActive = false,
                CookieBanner = CookieBannerHandling.LeftAsIs,
                JavaScriptSettleMs = 5000,
                // Filled in from the container at capture time; the seed records the versions the
                // profile was written against.
                ChromiumVersion = "unknown-until-first-capture",
                CrawlerVersion = "unknown-until-first-capture",
            },
        });

        await _db.SaveChangesAsync(cancellationToken);
        LogProfileSeeded();
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Database migrations applied.")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Seeded capture profile DE-Standard v1.")]
    private partial void LogProfileSeeded();
}
