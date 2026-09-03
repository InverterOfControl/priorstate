---
layout: home

hero:
  name: PriorState
  text: Tamper-evident website archiving
  tagline: For use as evidence, not as documentation. Hash-chained, externally timestamped, and re-verifiable by someone who does not trust you.
  actions:
    - theme: brand
      text: Quickstart
      link: /guide/quickstart
    - theme: alt
      text: What it does not claim
      link: /guide/limits
    - theme: alt
      text: GitHub
      link: https://github.com/saschalaabs/priorstate

features:
  - title: Capture, not screenshots
    details: Drives browsertrix-crawler with a real Chromium and stores WACZ archives that replay interactively. The capture stack is Webrecorder's, mature and purpose-built; PriorState does not reinvent it.
  - title: An append-only ledger
    details: Every snapshot is hashed over a fixed canonical form and linked to its predecessor. The database refuses UPDATE, DELETE and TRUNCATE on ledger tables — enforced in SQL, so an auditor can read the guarantee rather than take it on faith.
  - title: Independent timestamps
    details: A daily Merkle root goes to an RFC-3161 authority. The token proves the entries existed unaltered before an attested moment, independently of PriorState, its operator, and its storage.
  - title: Verifiable by the other side
    details: The evidence package ships the archive, a protocol, the timestamp token and a readable 150-line shell script. The opposing party recomputes everything with openssl and sha256sum, offline.
---

## The problem this solves

Git records which *code* was deployed. Between that and what a visitor actually saw sit CMS
content, feature flags, A/B tests, third-party embeds and backend-driven prices. Git is also
rewritable by design — force-push, rebase, moved tags. A repository is a history, not a proof.

PriorState answers the questions a dispute actually turns on:

- Was statement X on the page on day Y?
- When exactly was a contested claim removed, after a cease-and-desist?
- Which price or advertising claim was online on a given date?

## Where the guarantee actually lives

Not in the storage layer. As of 2026 no self-hostable S3 implementation enforces Object Lock
reliably, so PriorState does not rest its claim there — it
[probes the backend and reports what it found](/operations/storage), per snapshot, in every
evidence package.

The guarantee lives in the hash chain and the external timestamp. Those survive the bucket being
wiped: the token proves a given entry hash existed before an attested moment, and the chain proves
that entry has not changed since. That is a narrower claim than "our storage is immutable", and it
is one that holds up.
