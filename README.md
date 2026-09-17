# PriorState

**Tamper-evident archiving of your own websites.**

PriorState records browser visits in replayable web archives, hashes them into a chain, and
submits batches to an external timestamp authority. Evidence packages let recipients check the
recorded bytes against a timestamp using an independently trusted certificate.

It helps investigate questions such as:

- Was statement X present in a recorded visit?
- Between which recorded visits did a contested claim disappear?
- Which price or advertising claim did this browser receive?

Periodic visits can bracket an observed change; they cannot establish the exact removal time or
continuous availability between visits. Personalisation and incomplete crawls also limit what a
visit says about other visitors.

## How verification works

PriorState drives [browsertrix-crawler](https://github.com/webrecorder/browsertrix-crawler) with
Chromium and stores [WACZ](https://specs.webrecorder.net/wacz/) archives, replayed through
[ReplayWeb.page](https://replayweb.page).

1. Every snapshot is hashed over a **fixed, documented canonical form** containing the payload
   digest and recorded metadata. For browser archives, the recorded URL is the project's **first
   seed URL** and the recorded time is the **crawl start**. These are archive-level labels;
   PriorState does not extract each page's URL, redirect or capture time into snapshot metadata.
   Inspect the WACZ for its individual page records.
2. Each entry links to its predecessor in a **hash chain**. The default API and worker database
   role can insert ledger entries and fill specified fields once; it cannot rewrite history or
   remove the protecting triggers. A separate one-shot process applies migrations. Database
   administrators and anyone controlling the Docker host can still remove protections; see
   [database accounts and upgrades](docs/operations/database.md).
3. The worker checks hourly for **pending entries dated before the current UTC day**, and sends
   their Merkle root to an **RFC-3161 timestamp authority** in one batch. A batch can span several
   days; manual anchoring also includes today's pending entries. Until anchoring succeeds, entries
   have no external timestamp. The authority attests that the committed bytes existed by its
   signing time; it does **not** independently confirm the operator-recorded crawl or plugin time,
   source URL, or truth of the content.
4. An **evidence package** includes the payload, protocol PDF, timestamp token, Merkle proof and
   `verify.sh`. Verification requires a POSIX shell, `openssl`, `sha256sum`, `xxd`, and standard Unix
   text utilities. Supply a TSA
   root certificate obtained and authenticated independently of the package using
   `sh verify.sh --ca-file /path/to/independently-trusted-ca.pem`; a certificate
   supplied by the archive operator alone does not establish that trust. See
   [evidence packages](docs/guide/evidence-package.md).

## Limits

- **Storage immutability is reported, not assumed.** The bundled Garage store does not enforce
  S3 Object Lock. PriorState records the result of its storage probe per snapshot. Tamper-evidence
  does not restore deleted content; keep backups of archives and evidence packages. See
  [storage and WORM](docs/operations/storage.md).
- **FreeTSA is the demonstration default.** Choose and assess an appropriate timestamp authority
  for your use case, configure its certificate chain, and independently establish recipient
  trust. See [timestamping](docs/operations/timestamping.md).
- **A valid hash and timestamp do not certify a complete or truthful capture.** The operator's
  process, browser conditions and access controls matter. The timestamp also does not authenticate
  every statement in the generated protocol or unsigned manifest. See [limits](docs/guide/limits.md).
- PriorState does not determine whether evidence will be accepted in a dispute. A German
  *Verfahrensdokumentation* template is included in `docs/`.

## Deliberately missing features

- Deleting an individual snapshot through the application
- Shortening a retention period after the fact
- Switching the timestamp source for already anchored entries
- Editing capture profiles in place: profiles are named and versioned; changes apply to future runs

## Quickstart

```bash
git clone https://github.com/InverterOfControl/priorstate.git
cd priorstate/deploy
cp .env.example .env
# Set different administrator and application database passwords in .env.
# Review storage/TSA settings and certificate setup in the timestamping documentation.
docker compose up -d
```

Then open <http://localhost:8080> and create the first account. Existing installations should
follow the [database upgrade steps](docs/operations/database.md) before starting the new services.

## Documentation

<https://inverterofcontrol.github.io/priorstate>

## Licence

[AGPL-3.0-only](LICENSE), © 2026 Sascha Laabs. Self-hosting is free and unrestricted; the licence
keeps a hosted variant viable and means recipients can inspect how a snapshot was produced.

Contributions require a [Developer Certificate of Origin](CONTRIBUTING.md) sign-off.
