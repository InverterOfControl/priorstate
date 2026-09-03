# Architecture

```
Deploy webhook (GitHub Actions) ──┐
                                  ├──> crawl job (Postgres queue)
Cron schedule ────────────────────┘         │
                                            v
                              browsertrix-crawler container
                                            │
                                    WACZ ──> S3-compatible storage
                                            │    (WORM capability probed,
                                            │     recorded per snapshot)
                                            v
                        SHA-256 ──> hash chain (Postgres, append-only)
                                            │
                                  daily Merkle root
                                            │
                                            v
                              RFC-3161 timestamp authority
```

## Components

| Project | Responsibility |
|---|---|
| `PriorState.Domain` | Entities and value objects. No dependencies at all, by design. |
| `PriorState.Ledger` | Canonical form, hash chain, Merkle tree, RFC-3161 client. |
| `PriorState.Storage` | `IObjectStore` over S3, plus the WORM capability probe. |
| `PriorState.Crawler` | Turns a capture profile into browsertrix arguments; runs the container. |
| `PriorState.Evidence` | Evidence package assembly, protocol rendering, `verify.sh`. |
| `PriorState.Data` | EF Core model, migrations, the append-only enforcement. |
| `PriorState.Api` | Minimal APIs, Identity, and serving the built Vue app. |
| `PriorState.Worker` | Queue consumer, cron scheduler, daily timestamp anchoring. |

## Data model

`Project → CaptureProfileVersion → Run → Snapshot → TimestampAnchor`, plus `DeploymentLedgerEntry`
linking commits to snapshots and `AuditLogEntry` recording access.

`Snapshot`, `TimestampAnchor`, `AuditLogEntry`, `CaptureProfileVersion` and
`DeploymentLedgerEntry` are ledger tables: append-only, enforced in SQL. `Project`, `Run` and
`CrawlJob` are operational state and remain mutable — a retry has to be recordable.

Three narrow set-once exceptions exist, none of which feed a hash:
`snapshots.TimestampAnchorId`, `capture_profile_versions.SupersededAt`, and
`deployment_ledger_entries.RunId`. Each can go from `NULL` to a value exactly once; the trigger
rejects everything else, including a second attempt on the same column.

## Choices worth knowing about

**No message broker.** The queue is a Postgres table claimed with `FOR UPDATE SKIP LOCKED`. Several
workers can run, the queue and the ledger share a transaction, and the compose file stays small.
A broker would buy scale that a single-domain archive does not need.

**An advisory lock, not optimistic retry, for chain appends.** Two concurrent appends reading the
same tail would both claim the same predecessor and silently fork the chain.
`pg_advisory_xact_lock` serialises the append; the lock is held for one insert. A unique index on
the chain sequence is the backstop.

**Daily anchoring, not per-snapshot.** Tokens from a qualified authority cost money per request.
One token covers a whole day, and any single entry is still provable against it with a short audit
path — which is what the Merkle tree is for.

**PDF via the crawler's own Chromium.** The protocol is HTML rendered by the same browser that
performed the capture. No PDF library, no additional licence to explain to self-hosters, and a
reviewer can open the same HTML in their own browser to compare.

**The Vue app is built into the API image.** One container serves both, so the production compose
file needs no Node runtime and one fewer thing can go wrong during a first-run evaluation.
