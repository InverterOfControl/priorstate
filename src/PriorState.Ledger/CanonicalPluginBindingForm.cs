using System.Security.Cryptography;
using System.Text;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger;

/// <summary>
/// Renders a plugin binding into the bytes whose digest is committed to the chain.
///
/// A plugin snapshot asserts "this JSON came from that endpoint". Without this form, the endpoint
/// would be a mutable row on the archive operator's own server, and the natural objection — you
/// pointed it somewhere else and re-ran it — would have no answer. The v2 snapshot form hashes
/// binding_digest, the evidence package ships these bytes plus the configuration verbatim, and the
/// recipient recomputes both offline.
///
/// The configuration is committed by digest rather than inline because its shape belongs to the
/// plugin, not to the ledger. This project must not learn what an HTTP header is in order to hash
/// a binding, and a plugin added later must not require a new canonical form version.
///
/// Same compatibility contract as CanonicalSnapshotForm: never change, reorder or remove a field
/// in an existing version. A new field means a new version constant and a new branch.
/// </summary>
public static class CanonicalPluginBindingForm
{
    public const string Version1 = "priorstate-plugin-binding-v1";

    public static byte[] Render(PluginBindingVersion binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var builder = new StringBuilder();

        builder.Append(Version1).Append('\n');
        CanonicalText.Field(builder, "plugin", binding.PluginId);
        CanonicalText.Field(builder, "name", binding.Name);
        CanonicalText.Field(builder, "version", CanonicalText.Number(binding.Version));
        CanonicalText.Field(builder, "secret_ref", binding.SecretRef ?? string.Empty);
        CanonicalText.Field(builder, "required", CanonicalText.Bool(binding.Required));
        CanonicalText.Field(builder, "created_at", CanonicalText.FormatTimestamp(binding.CreatedAt));
        CanonicalText.Field(builder, "config_sha256", ComputeConfigurationDigest(binding).Value);

        return CanonicalText.ToUtf8(builder);
    }

    /// <summary>SHA-256 over the canonical form. This is the value the chain commits to.</summary>
    public static Sha256Hash ComputeDigest(PluginBindingVersion binding) =>
        Sha256Hash.FromBytes(SHA256.HashData(Render(binding)));

    /// <summary>
    /// SHA-256 over the configuration bytes exactly as submitted. The evidence package ships the
    /// same bytes, so a recipient reads the configuration and confirms this digest themselves.
    /// </summary>
    public static Sha256Hash ComputeConfigurationDigest(PluginBindingVersion binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Sha256Hash.FromBytes(SHA256.HashData(ConfigurationBytes(binding)));
    }

    /// <summary>The configuration as stored, UTF-8, with no reformatting. Never re-serialised.</summary>
    public static byte[] ConfigurationBytes(PluginBindingVersion binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(binding.ConfigurationJson);
    }
}
