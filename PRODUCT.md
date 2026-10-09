# PriorState

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Companies that need to verify the state of their own website at a point in time in the past. This audience and job are confirmed by the product owner. Specific company roles, sectors, and accessibility needs remain open.

## Product Purpose

Preserve website visits so a company can later inspect, replay, and verify the recorded state. Success means finding the relevant recorded visit and producing evidence whose captured bytes can be checked independently.

## Positioning

The repository describes tamper-evident website archiving: replayable browser captures combined with a hash chain, external timestamps, and exportable evidence packages. This is the implemented mechanism, not a promise of legal admissibility or complete historical coverage.

## Operating Context

The existing application is self-hosted. Operators configure projects and named, versioned capture profiles; inspect capture runs, the ledger, and timelines; replay snapshots; and export evidence. A deployment ledger can associate captures with release commit SHAs. The interface currently supports English and German.

## Capabilities and Constraints

The following constraints are documented in the repository and must remain accurate in product copy:

- Browsertrix with Chromium produces WACZ archives; ReplayWeb.page provides replay.
- Snapshot metadata labels a browser archive with the project's first seed URL and crawl start. Individual page records must be inspected inside the archive.
- Periodic captures bracket observed changes; they do not establish exact change times or continuous availability. Personalisation and incomplete crawls limit conclusions about other visitors.
- A hash chain protects recorded history against detectable alteration. Runtime database protections do not prevent administrators or host operators from removing protections.
- External RFC-3161 timestamps attest that committed bytes existed by signing time. They do not independently certify the recorded crawl time, source URL, content truth, or capture completeness. Pending entries have no external timestamp until anchoring succeeds.
- Evidence verification requires the exported payload, proof and timestamp, supported Unix tools, and a TSA root certificate authenticated independently of the evidence package.
- Storage immutability is measured and reported. The bundled Garage store does not enforce S3 Object Lock; tamper-evidence cannot recover deleted archives.
- FreeTSA is a demonstration default. Do not represent it as a qualified timestamp service or promise acceptance in a dispute.
- Individual snapshot deletion, retrospective retention shortening, switching timestamp sources for anchored entries, and editing capture profiles in place are deliberately unavailable.

The existing stack is Vue, TypeScript, Vite, and Tailwind for the interface, with a .NET API and worker. Existing application routes cover login, ledger, projects, timeline, snapshot details, runs, profiles, plugins, and audit history.

## Brand Commitments

The existing product name is PriorState. Keep product language precise about recorded observations, verification, and evidence limitations. No additional binding brand commitments were supplied during initialization.

## Evidence on Hand

- `README.md`: overview, verification procedure, limitations, self-hosting, and AGPL-3.0-only licensing.
- `docs/guide/what-it-does.md`: capture mechanism, deployment ledger, and deliberately missing operations.
- `docs/guide/evidence-package.md` and `docs/guide/limits.md`: evidence verification and its limits.
- `docs/operations/`: storage, database, timestamping, backups, plugins, and capture profiles.
- `docs/legal/verfahrensdokumentation.md`: German process-documentation template.
- `src/ui/`: implemented interface and English/German copy.

No customer testimonials, independently validated business outcomes, or legal-admissibility guarantees were established in this initialization. Do not invent them.

## Product Principles

- Make past recorded website states easy to locate, inspect, and verify.
- Explain what the evidence establishes with the same care as what it cannot establish.
- Preserve traceability from captured bytes through ledger entries, timestamps, and evidence exports.
- Keep capture configuration and historical records accountable; changes affect future runs rather than rewriting history.

## Open Decisions

Specific purchasing and operating roles, target sectors, required accessibility standards, and additional business constraints are not yet confirmed. Repository-derived capabilities above describe the current implementation and documentation; the owner explicitly confirmed the audience and historical-verification job.
