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

**Snapshot metadata** for each WACZ stores the first project seed URL and crawl start. A WACZ may
contain multiple pages; their individual URLs, redirects and request times are not extracted into
snapshot metadata. Inspect the archive for those records. Periodic captures can bracket an
observed change, not establish its exact time or continuous availability.

**The ledger** hashes each snapshot over a [fixed canonical form](/reference/canonical-form) and
links it to its predecessor. The default runtime role can insert records and fill permitted
fields once, but cannot rewrite history or remove protecting triggers. Administrators and Docker
host operators can still remove those protections; see [database accounts](/operations/database).

**Timestamping** checks hourly for pending entries dated before today (UTC), combining them into
one Merkle tree. A batch can span several days. Manual anchoring includes today's pending entries.
Entries remain without an external attestation until anchoring succeeds. A valid RFC-3161 token
attests existence of committed bytes by its signing time; it does not independently verify the
operator-recorded observation time, source URL or content.

**The evidence package** exports the archive, protocol PDF, timestamp token, Merkle audit path and
`verify.sh`. Verification requires a POSIX shell, OpenSSL, sha256sum, xxd and standard Unix tools,
plus a TSA root certificate authenticated independently of the package. See
[evidence packages](/guide/evidence-package) for the complete command and dependencies.

## Deliberately missing

These operations are absent from the application by design:

- Deleting an individual snapshot through the application
- Shortening a retention period after the fact
- Switching the timestamp source for existing entries
- Free-form capture settings — profiles are [named and versioned](/operations/capture-profiles),
  changes apply only going forward, and every change is written to the audit log

That last one is subtler than the others. Freely adjustable viewport, user agent or wait times
hand the opposing side an easy argument: that the capture was configured to produce the desired
result. A named, versioned profile referenced in the protocol removes that argument.
