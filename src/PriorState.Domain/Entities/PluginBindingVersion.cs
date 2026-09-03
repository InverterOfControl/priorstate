namespace PriorState.Domain.Entities;

/// <summary>
/// A named, versioned binding of one capture plugin to one project, e.g. "erp-prices" v3.
///
/// This is the plugin equivalent of <see cref="CaptureProfileVersion"/>, and for the same reason.
/// A plugin that fetched whatever the operator happened to have configured at the time, with no
/// record of what that was, would hand the opposing side the argument that the endpoint was
/// changed to produce a desired result. So a binding is not edited: a change creates a new
/// version, it applies only to runs after it, and snapshots keep the version they ran under.
/// Rows are never updated or deleted; a trigger raises on either.
///
/// Retiring a binding means superseding it without a successor. There is no enabled flag and no
/// delete, so "this plugin stopped running on 12 March" stays visible rather than disappearing.
/// </summary>
public sealed class PluginBindingVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>Which plugin this binds, e.g. "http-json". Matches ICapturePlugin.Id.</summary>
    public required string PluginId { get; set; }

    /// <summary>Stable name across versions, chosen by the operator, e.g. "erp-prices".</summary>
    public required string Name { get; set; }

    public required int Version { get; set; }

    /// <summary>
    /// The plugin's configuration, stored as the exact bytes that were submitted.
    ///
    /// Deliberately text and not jsonb: jsonb reorders keys and drops insignificant whitespace, so
    /// the bytes read back would not be the bytes that were hashed. The canonical binding form
    /// commits to a SHA-256 of this string, and the evidence package ships it verbatim, so a
    /// recipient can read the configuration and confirm it is the one that ran.
    ///
    /// Never contains credentials. Secrets are referenced by environment variable name through
    /// <see cref="SecretRef"/> and resolved at execution time.
    /// </summary>
    public required string ConfigurationJson { get; set; }

    /// <summary>
    /// Name of the environment variable holding this binding's secret, e.g. "PS_SECRET_ERP_TOKEN".
    /// The name is recorded; the value never is.
    /// </summary>
    public string? SecretRef { get; set; }

    /// <summary>Why this version exists. Shown in the evidence package.</summary>
    public required string Rationale { get; set; }

    /// <summary>
    /// Whether a failure of this plugin fails the whole run.
    ///
    /// False by default: an ERP being briefly unreachable should not cost you the page capture,
    /// which is the thing that cannot be recaptured later. Either way the failure is recorded on
    /// the run and in the audit log — a plugin that failed never produces a snapshot, so there is
    /// no such thing as a partial or placeholder entry in the chain.
    /// </summary>
    public required bool Required { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when a newer version exists, or when the binding was retired outright.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public string Designation => $"{Name} v{Version}";
}
