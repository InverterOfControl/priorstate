namespace PriorState.Ledger.Timestamping;

/// <summary>Configuration for the RFC-3161 timestamp authority. Bound from "Ledger:Tsa".</summary>
public sealed class TimestampAuthorityOptions
{
    public const string SectionName = "Ledger:Tsa";

    /// <summary>
    /// The default is FreeTSA, purely so the tool can be evaluated without an account. It is not
    /// a qualified provider and produces tokens that will not carry a dispute — see
    /// <see cref="Qualified"/>.
    /// </summary>
    public string Url { get; set; } = "https://freetsa.org/tsr";

    /// <summary>
    /// Whether the configured authority is a qualified trust service provider under eIDAS.
    /// This is an assertion by the operator, recorded on every anchor and printed on every
    /// protocol. It is left false by default and the UI warns while it stays false, because the
    /// difference between a demo timestamp and a qualified one is the difference between an
    /// archive that holds up and one that does not.
    /// </summary>
    public bool Qualified { get; set; }

    /// <summary>Human-readable name of the authority, reproduced in the evidence package.</summary>
    public string DisplayName { get; set; } = "FreeTSA (demonstration only)";

    /// <summary>Optional TSA policy OID to request.</summary>
    public string? RequestedPolicyOid { get; set; }

    /// <summary>
    /// Ask the authority to embed its signing certificate in the token. On by default: without it
    /// the verifying party has to source the certificate separately, which is exactly the kind of
    /// friction that makes an archive look unverifiable.
    /// </summary>
    public bool RequestSignerCertificate { get; set; } = true;

    /// <summary>
    /// PEM file holding the authority's CA chain, mounted by the operator. Shipped inside every
    /// evidence package so verify.sh can check the token offline, years later, without needing
    /// the authority to still be reachable.
    /// </summary>
    public string? CaChainPemPath { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
