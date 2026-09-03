using PriorState.Domain.Entities;

namespace PriorState.Plugins.Abstractions;

/// <summary>
/// A source of archivable data that is not a web page.
///
/// Written for the case where the page alone is not the whole record: a shop page quotes a price,
/// and the price also exists in an ERP behind an API. Archiving only the page leaves the obvious
/// question — what did your own system say at that moment — unanswerable.
///
/// The contract is deliberately the narrowest one that can do that job. A plugin receives its
/// configuration and returns bytes. It gets no database, no object store, no filesystem path and
/// no way to reach the ledger: the host hashes the payload, stores it and appends it, through the
/// same code that appends a page capture. This is not politeness about layering. PriorState's
/// entire claim is that recorded history cannot be altered, including by the operator, and a
/// plugin API that handed out a DbContext would be a supported route around that claim.
///
/// So a plugin can only ever cause a new entry to be appended. It cannot modify one, delete one,
/// or influence how one is hashed.
///
/// Implementations must be thread-safe and are registered as singletons.
/// </summary>
public interface ICapturePlugin
{
    /// <summary>
    /// Stable identifier, e.g. "http-json". Recorded in the canonical form of every snapshot this
    /// plugin produces, so it is a compatibility contract: renaming it orphans existing evidence.
    /// </summary>
    string Id { get; }

    /// <summary>Human-readable name, shown in the UI. Free to change.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Fetches the data to archive.
    ///
    /// Return the bytes exactly as received. Reformatting, pretty-printing or re-serialising the
    /// response would mean the archived hash covers the plugin's rendering of the data rather than
    /// what the far end actually sent, which is the thing being attested.
    ///
    /// Throw to report failure. Whether that fails the whole run is the binding's decision, not
    /// the plugin's. A plugin must not return an empty or placeholder payload to signal a problem:
    /// there is no such thing as a partial entry in the chain.
    /// </summary>
    Task<PluginPayload> ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Everything a plugin is given. Note what is absent.</summary>
public sealed record PluginExecutionContext
{
    public required Guid RunId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The capture profile the run is executing under, for context. Read-only.</summary>
    public required CaptureProfileVersion Profile { get; init; }

    /// <summary>
    /// The versioned binding being executed. Its ConfigurationJson is the plugin's own
    /// configuration, in whatever shape the plugin defined.
    /// </summary>
    public required PluginBindingVersion Binding { get; init; }

    /// <summary>
    /// The resolved value of the binding's secret, if it declared one.
    ///
    /// Present here and nowhere else: not in the database, not in the canonical form, not in the
    /// evidence package, and not in any log line. Only the name of the environment variable it
    /// came from is recorded.
    /// </summary>
    public string? Secret { get; init; }
}

/// <summary>What a plugin returns. The host does everything else with it.</summary>
public sealed record PluginPayload
{
    /// <summary>
    /// The resource that was read, as requested. Enters the canonical form and is therefore
    /// permanently visible in every evidence package — it must not carry credentials.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>Where the request actually ended up, if it differs. A redirect is itself evidence.</summary>
    public string? FinalUrl { get; init; }

    /// <summary>The media type as reported by the source, e.g. "application/json".</summary>
    public required string MediaType { get; init; }

    /// <summary>The bytes to archive, verbatim.</summary>
    public required byte[] Content { get; init; }
}

/// <summary>Thrown by a plugin when it cannot produce a payload.</summary>
public sealed class PluginException : Exception
{
    public PluginException()
    {
    }

    public PluginException(string message)
        : base(message)
    {
    }

    public PluginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
