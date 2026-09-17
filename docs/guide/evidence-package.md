# The evidence package

This is the point at which PriorState becomes useful to a lawyer rather than to an engineer.

Export it from a snapshot's page. It is a ZIP:

```
protocol.pdf             Human-readable record, in German. Start here.
verify.sh                Checks hashes and the timestamp signature.
snapshot.wacz            The web archive. Opens at replayweb.page, offline.
canonical/entry.txt      The exact bytes that were hashed into the ledger.
manifest.txt             The same facts, machine-readable.
merkle/audit-path.txt    Proof that this entry belongs to the timestamped root.
timestamp/token.tsr      RFC-3161 token from an independent authority.
timestamp/root.txt       The value that token attests to.
timestamp/tsa-chain.pem  Operator-supplied certificates; untrusted chain material.
README.txt               What is in here and how to check it.
```

## Verification, from the recipient's side

```bash
unzip priorstate-evidence-*.zip
cd priorstate-evidence-*
sh verify.sh --ca-file /path/to/independently-trusted-ca.pem
```

Requirements: a POSIX shell, `openssl`, `xxd`, `sha256sum`. Nothing is downloaded, no server is
contacted. Obtain the CA roots from the authority through an independently authenticated channel,
and check their identity/fingerprint against a trusted source. The package's certificate file is
operator-supplied and is used only as untrusted chain material. Passing it as `--ca-file` without
independently authenticating it defeats this trust boundary. Relative paths are resolved from the
current working directory before the script enters the package directory.

Exit code 0 means the cryptographic checks passed under the supplied roots; 1 means verification
failed; 2 means missing/invalid arguments or an unusable package. `--help` prints usage.
Verification is offline: it does not perform revocation lookups or validate qualified-provider status.

The script performs four checks:

1. **Archive integrity.** `sha256sum snapshot.wacz` against the hash in the canonical entry.
2. **Ledger entry.** `sha256sum canonical/entry.txt` against the committed entry hash.
3. **Merkle inclusion.** Replays the audit path from the leaf to its anchor's batch root.
4. **Timestamp.** `openssl ts -verify` of the token against the recipient's supplied CA roots, using bundled certificates only as untrusted intermediates.

It is heavily commented, on purpose: the recipient is not expected to trust
it, they are expected to read it. That is also why the format hashed in step 2 is
[line-oriented rather than JSON](/reference/canonical-form).

## What the protocol says

The protocol PDF is in German — it is the document handed to a German lawyer or court, and it is
one of two deliberate exceptions to this project being in English. For browser archives it records
the first seed URL and crawl start, rather than each page's URL and capture time. It also records
the capture conditions, every hash in the chain of reasoning, the timestamp details,
and the storage immutability status **as observed**.

Two warnings print automatically when they apply, and cannot be switched off:

- The timestamp authority is not a qualified eIDAS provider.
- Storage-level WORM was requested but not verified, or is unavailable.

A reader handed one of these documents needs to know what it is worth before relying on it. See
[what it does not claim](/guide/limits).

## Snapshots that cannot be exported yet

The scheduled worker checks hourly and anchors pending entries dated before today in UTC.
Today's entries normally wait until tomorrow; manual anchoring through the Ledger page includes
them immediately. Export returns 409 while a snapshot has no anchor. If the authority is
unreachable, entries stay pending until a request succeeds.

## Verifying without a package

The whole archive can be re-derived at any time from the interface: **Ledger → re-derive the whole
chain** recomputes every entry hash from its recorded metadata and checks each link. It reads the
entire chain rather than a sample — a verification that checks only some entries proves only that
those entries are intact. The result goes to the audit log either way.

## Trust and scope

A valid signature commits the payload and recorded metadata before the signed timestamp, assuming
the chosen CA roots and authority are trusted. It does not prove that the bytes came from the
recorded URL, when exactly a crawl took place, or whether the content and recorded conditions are
true. The `tsa_url` and `tsa_qualified` manifest fields are unverified operator assertions; the
signature does not authenticate those fields or establish qualified status.
