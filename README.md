# PriorState

**Tamper-evident archiving of your own websites — for use as evidence, not documentation.**

PriorState captures what a visitor actually saw on a page at a point in time, and makes that
capture defensible later: hash-chained, externally timestamped, and re-verifiable by someone who
does not trust you.

It answers the questions a dispute actually turns on:

- Was statement X on the page on day Y?
- When exactly was a contested claim removed (proof of compliance after a cease-and-desist)?
- Which price or advertising claim was online on a given date?

A page as a visitor receives it is assembled at request time, out of CMS content, feature flags,
A/B tests, third-party embeds and prices from backend systems. Once it changes, the state it was
in is gone, and reconstructing it afterwards from the parts is guesswork. PriorState records the
rendered result itself, in a form that is still checkable years later.

## How the guarantee works

Capture is not home-grown: PriorState drives [browsertrix-crawler](https://github.com/webrecorder/browsertrix-crawler)
with a real Chromium and stores [WACZ](https://specs.webrecorder.net/wacz/) archives, replayed
through [ReplayWeb.page](https://replayweb.page). That part is a mature, purpose-built stack and
we do not reinvent it.

What PriorState adds is the evidentiary layer:

1. Every snapshot is hashed over a **fixed, documented canonical form** — URL, UTC capture time,
   WACZ SHA-256, capture profile version, tool versions, capture conditions.
2. Each entry links to the previous one, forming an append-only **hash chain**. The database role
   the application runs as cannot `UPDATE` or `DELETE` these rows; this is enforced in SQL, not in
   application code, so an auditor can verify it by reading the migration.
3. Once a day a **Merkle root** over that day's entries is sent to an **RFC-3161 timestamp
   authority**. The returned token proves the entries existed, unaltered, before that moment —
   independently of PriorState, its operator, and its storage.
4. An **evidence package** exports the WACZ, the protocol PDF, the timestamp token and a short,
   readable `verify.sh` so the opposing party can recompute everything themselves with nothing but
   `openssl` and `sha256sum`.

## What this does not claim

Honesty about limits is part of the argument, so it is stated up front:

- **Storage immutability is reported, not assumed.** S3 Object Lock (WORM) is used when the
  configured backend genuinely enforces it. As of 2026 no self-hostable S3 implementation does
  reliably — MinIO's community edition is archived, Garage has not merged Object Lock, SeaweedFS
  has open reports that COMPLIANCE mode does not block deletes. PriorState therefore probes the
  backend, records the result **per snapshot**, and prints it in the evidence package. The
  tamper-evidence rests on the hash chain and the external timestamp, which survive the bucket
  being wiped. See [docs: storage and WORM](docs/operations/storage.md).
- **The default timestamp authority is not good enough for a real dispute.** FreeTSA ships as the
  default so the tool can be tried in ten minutes. For anything that may be litigated, configure a
  qualified eIDAS provider. The tool will keep telling you this.
- **Evidentiary responsibility lies with the operator.** PriorState produces a defensible record;
  whether a court accepts it also depends on your process documentation. A fill-in
  *Verfahrensdokumentation* template (German, for German law) ships in `docs/`.
- This is not legal advice.

## Deliberately missing features

These are absent by design and will not be added. An archive whose operator can remove entries is
worthless as evidence:

- Deleting an individual snapshot
- Shortening a retention period after the fact
- Switching the timestamp source for existing entries
- Free-form capture settings — profiles are **named and versioned** (`DE-Standard v1`), changes
  apply only going forward, and every change is written to the audit log. Freely adjustable
  viewport, user agent or wait times would hand the opposing side the argument that the capture
  was configured to suit.

## Quickstart

```bash
git clone https://github.com/InverterOfControl/priorstate.git
cd priorstate/deploy
cp .env.example .env
docker compose up -d
```

Then open <http://localhost:8080> and create the first account. That is the whole setup — if it
takes longer than ten minutes, that is a bug worth reporting.

## Documentation

<https://inverterofcontrol.github.io/priorstate>

## Licence

[AGPL-3.0-only](LICENSE), © 2026 Sascha Laabs. Self-hosting is free and unrestricted; the licence
keeps a hosted variant viable and, more importantly, means the opposing party in a dispute can
inspect exactly how a snapshot was produced.

Contributions require a [Developer Certificate of Origin](CONTRIBUTING.md) sign-off.
