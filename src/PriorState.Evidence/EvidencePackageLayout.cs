using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Evidence;

/// <summary>
/// Where things live inside an evidence package.
///
/// Shared between the builder, the protocol renderer and the tests so that the name the protocol
/// prints is necessarily the name the package contains. A protocol pointing at a file that is not
/// there is the kind of defect nobody notices until it is being read in front of an opponent.
/// </summary>
public static class EvidencePackageLayout
{
    public const string CanonicalEntry = "canonical/entry.txt";
    public const string Manifest = "manifest.txt";
    public const string PageCapturePayload = PayloadNaming.PageCaptureFileName;
    public const string PluginBinding = "plugin/binding.txt";
    public const string PluginConfiguration = "plugin/configuration.json";

    /// <summary>
    /// The name the payload is shipped under. Page captures keep snapshot.wacz, which existing
    /// packages and every published example already use; plugin payloads are named for their
    /// media type so the file opens in the obvious application.
    /// </summary>
    public static string PayloadFileName(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Equals(snapshot.CanonicalFormVersion, CanonicalSnapshotForm.Version2, StringComparison.Ordinal)
            ? PayloadNaming.FileNameFor(snapshot.PayloadMediaType)
            : PageCapturePayload;
    }

}
