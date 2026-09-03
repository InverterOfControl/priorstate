# The evidence package

This is the point at which PriorState becomes useful to a lawyer rather than to an engineer.

Export it from a snapshot's page. It is a ZIP:

```
protocol.pdf             Human-readable record, in German. Start here.
verify.sh                Re-derives every claim in the protocol.
snapshot.wacz            The web archive. Opens at replayweb.page, offline.
canonical/entry.txt      The exact bytes that were hashed into the ledger.
manifest.txt             The same facts, machine-readable.
merkle/audit-path.txt    Proof that this entry belongs to the timestamped root.
timestamp/token.tsr      RFC-3161 token from an independent authority.
timestamp/root.txt       The value that token attests to.
timestamp/tsa-chain.pem  The authority's certificates, for offline verification.
README.txt               What is in here and how to check it.
```

## Verification, from the recipient's side

```bash
unzip priorstate-evidence-*.zip
cd priorstate-evidence-*
sh verify.sh
```

Requirements: a POSIX shell, `openssl`, `xxd`, `sha256sum`. Nothing is downloaded, no server is
contacted, and nothing about the system that produced the package is trusted. Exit code 0 means
every check passed.

The script performs four checks:

1. **Archive integrity.** `sha256sum snapshot.wacz` against the hash in the canonical entry.
2. **Ledger entry.** `sha256sum canonical/entry.txt` against the committed entry hash.
3. **Merkle inclusion.** Replays the audit path from the leaf to the day's root.
4. **Timestamp.** `openssl ts -verify` of the token against the authority's certificate chain.

It is about 150 lines and heavily commented, on purpose: the recipient is not expected to trust
it, they are expected to read it. That is also why the format hashed in step 2 is
[line-oriented rather than JSON](/reference/canonical-form).

## What the protocol says

The protocol PDF is in German — it is the document handed to a German lawyer or court, and it is
one of two deliberate exceptions to this project being in English. It records the URL, capture
time, the full capture conditions, every hash in the chain of reasoning, the timestamp details,
and the storage immutability status **as observed**.

Two warnings print automatically when they apply, and cannot be switched off:

- The timestamp authority is not a qualified eIDAS provider.
- Storage-level WORM was requested but not verified, or is unavailable.

A reader handed one of these documents needs to know what it is worth before relying on it. See
[what it does not claim](/guide/limits).

## Snapshots that cannot be exported yet

Anchoring runs once a day, over complete days. A snapshot captured today has no timestamp anchor
until tomorrow, and the export refuses with a 409 rather than producing a package that omits the
one check the recipient cannot perform for themselves.

## Verifying without a package

The whole archive can be re-derived at any time from the interface: **Ledger → re-derive the whole
chain** recomputes every entry hash from its recorded metadata and checks each link. It reads the
entire chain rather than a sample — a verification that checks only some entries proves only that
those entries are intact. The result goes to the audit log either way.
