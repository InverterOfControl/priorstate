using System.Text.Json.Serialization;

namespace PriorState.Plugins.HttpJson;

/// <summary>
/// Deployment-wide settings for the HTTP JSON plugin. Bound from "Plugins:HttpJson".
///
/// Distinct from a binding's configuration: this is what the operator of the deployment decides,
/// and it constrains what any binding is allowed to do.
/// </summary>
public sealed class HttpJsonOptions
{
    public const string SectionName = "Plugins:HttpJson";

    /// <summary>
    /// Hosts a binding may call. Empty means any host, which is logged as a warning at startup.
    ///
    /// This matters more than it looks. The worker container mounts the Docker socket in the
    /// shipped compose file, which the security documentation already describes as equivalent to
    /// host root. A binding is operator-configured, so an unrestricted fetch turns "can edit
    /// project settings" into "can make a host-root-equivalent container issue arbitrary requests
    /// inside your network" — including to cloud metadata endpoints. Set this.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>How long to wait for the endpoint before giving up.</summary>
    [JsonIgnore]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Largest response that will be archived. A plugin payload is buffered in memory to be
    /// hashed, so an endpoint that streams forever must not be able to take the worker down.
    /// </summary>
    public long MaxPayloadBytes { get; set; } = 32 * 1024 * 1024;
}

/// <summary>
/// One binding's configuration, stored as JSON in the binding row.
///
/// Property names are part of the record: the binding's configuration bytes are hashed into the
/// snapshot's entry hash and shipped in the evidence package, so renaming one changes the digest
/// of every future binding and makes an old configuration harder to read, though existing entries
/// stay verifiable against the bytes they were written with.
/// </summary>
public sealed class HttpJsonBindingConfiguration
{
    /// <summary>The endpoint to read. Recorded in the canonical form, so never put a secret in it.</summary>
    public string Url { get; set; } = string.Empty;

    public string Method { get; set; } = "GET";

    /// <summary>Static request headers. Values are recorded in the configuration; keep secrets out.</summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Request body, for a POST. Sent as-is with <see cref="ContentType"/>.</summary>
    public string? Body { get; set; }

    public string ContentType { get; set; } = "application/json";

    /// <summary>Sent as the Accept header.</summary>
    public string Accept { get; set; } = "application/json";

    /// <summary>
    /// Header the binding's secret is sent in, e.g. "Authorization". The secret's value is taken
    /// from the environment variable named by the binding and never appears here.
    /// </summary>
    public string? AuthHeaderName { get; set; }

    /// <summary>
    /// Optional prefix for the secret, e.g. "Bearer ". Separate from the secret so the shape of
    /// the credential is legible in the archived configuration without the credential being in it.
    /// </summary>
    public string? AuthValuePrefix { get; set; }
}
