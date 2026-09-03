using Microsoft.AspNetCore.Identity;

namespace PriorState.Data;

/// <summary>
/// A person with access to the archive.
///
/// Local accounts exist so the tool can be evaluated without standing up an identity provider;
/// OIDC can be configured alongside for organisations that already have one. Either way every
/// action reaches the audit log with a real identity attached — a shared password would make the
/// access log, and with it half the process documentation, worthless.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Set for accounts provisioned through an external identity provider.</summary>
    public string? ExternalProvider { get; set; }
}
