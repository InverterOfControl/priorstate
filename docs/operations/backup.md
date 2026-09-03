# Backup and retention

## What needs backing up, and why

Two things, for different reasons.

**The Postgres database** holds the ledger: the hash chain, the timestamp tokens, the audit log.
Losing it loses the proof. The WACZ files would still exist but nothing would attest to when they
were made or that they are unaltered.

**The object store** holds the WACZ archives. Losing it loses the evidence itself. The chain and
timestamps would still prove exactly what existed and when — you simply could not produce it.

Neither substitutes for the other. Back up both.

## Database

```bash
docker compose exec -T postgres \
  pg_dump -U priorstate -Fc priorstate > priorstate-$(date -u +%Y%m%dT%H%M%SZ).dump
```

The dump includes the triggers that enforce append-only, so a restore restores the guarantee along
with the data. Verify that after any restore:

```sql
-- Must fail with "append-only".
UPDATE snapshots SET "Url" = 'x' WHERE "ChainSequence" = 1;
```

If it succeeds, the restore did not bring the triggers with it and the archive is not protected.

## Object store

For the bundled Garage, back up the `garage-data` and `garage-meta` volumes together — the
metadata alone is not restorable. For a hosted backend, use its own replication or lifecycle
mechanism, and be aware that a bucket with Object Lock enabled cannot be cleaned up on the usual
schedule.

## Restoring, and what to check afterwards

A restore is not finished until the chain re-derives. From the interface:
**Ledger → re-derive the whole chain**. Or:

```bash
curl -sX POST localhost:8080/api/ledger/verify --cookie-jar - | jq
```

This recomputes every entry hash from its recorded metadata and checks every link. A restore that
brought back a partial or stale database shows up here as a sequence gap or a broken link, not as
silent corruption discovered months later.

Then spot-check that an archive still downloads and replays — the chain being intact says nothing
about whether the object store came back.

## Retention arithmetic

Retention can be extended and never shortened, so the number chosen at
[Phase 0](/operations/phase-0-requirements) is a commitment.

Rough sizing: a WACZ for a medium page is typically 5–50 MB. Ten pages captured daily for six
years is somewhere between 100 GB and 1 TB. With Object Lock enabled, none of it can be pruned
before its retention expires — including by you.

If that number is uncomfortable, the levers are capture frequency and scope, decided before you
start. Reducing them later leaves the earlier volume in place.

## Retiring an installation

The archives are more durable than the software. Before decommissioning:

1. Export an evidence package for every snapshot that may still matter. Each one is
   self-contained and verifiable with `openssl` alone, indefinitely, with no PriorState involved.
2. Keep the database dump alongside them.
3. Keep the timestamp authority's certificate chain — evidence packages embed it, but a copy
   costs nothing.

An evidence package outlives this project, which is the intended property. See
[the evidence package](/guide/evidence-package).
