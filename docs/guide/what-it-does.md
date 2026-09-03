# What it does

PriorState records what a visitor actually saw on a page at a point in time, and makes that
record defensible later to someone who has every reason to doubt it.

## What a snapshot captures

A page as a visitor receives it is assembled at request time, out of:

- CMS content from the database
- feature flags, A/B tests, personalisation
- embedded third-party content
- prices and data from backend systems

PriorState records the rendered result — what was actually served and displayed — rather than the
parts it was assembled from. Once a page changes, that state exists nowhere else.

Captures can also be tied to a release. The **deployment ledger** records a commit SHA against the
snapshot taken after that deployment went live, so a change in the rendered page can be traced
back to the release that produced it.

## The four layers

**Capture** is not home-grown. [browsertrix-crawler](https://github.com/webrecorder/browsertrix-crawler)
drives a real Chromium and writes [WACZ](https://specs.webrecorder.net/wacz/) archives that replay
interactively through [ReplayWeb.page](https://replayweb.page). Reimplementing that would mean
defending a home-made capture mechanism in a dispute instead of pointing at an established one.

**The ledger** hashes each snapshot over a [fixed canonical form](/reference/canonical-form) and
links it to its predecessor. The database refuses `UPDATE`, `DELETE` and `TRUNCATE` on ledger
tables through triggers installed by a migration — so the guarantee is readable in SQL rather than
promised in application code.

**Timestamping** takes a Merkle root over each day's entries to an RFC-3161 authority. The
returned token proves those entries existed, unaltered, before an attested moment — independently
of PriorState, its operator, and its storage.

**The evidence package** exports the archive, a protocol PDF, the timestamp token, the Merkle
audit path and a short `verify.sh` that recomputes everything with `openssl` and `sha256sum`. The
opposing party checks the claim themselves, offline, without trusting you.

## Deliberately missing

These are absent by design and will not be added. An archive whose operator can remove entries is
worthless as evidence:

- Deleting an individual snapshot
- Shortening a retention period after the fact
- Switching the timestamp source for existing entries
- Free-form capture settings — profiles are [named and versioned](/operations/capture-profiles),
  changes apply only going forward, and every change is written to the audit log

That last one is subtler than the others. Freely adjustable viewport, user agent or wait times
hand the opposing side an easy argument: that the capture was configured to produce the desired
result. A named, versioned profile referenced in the protocol removes that argument.
