# Phase 0 requirements

Fill this in before capturing in earnest. Half a day of deciding these now is much cheaper than
discovering them later — particularly retention, which cannot be shortened afterwards.

Copy this page into your own repository or wiki and answer it. The answers also feed directly into
the [Verfahrensdokumentation](/legal/verfahrensdokumentation).

## Scope

**Which URLs?** List them. Seed URLs and, if the crawl should follow links, the include and
exclude prefixes.

> …

**Whose sites?** Your own only, or also third parties? Archiving your own sites is
straightforward. Archiving a competitor's is technically identical and legally a different
question — crawl politely, never touch login-protected areas, and take advice first.

> …

**How deep?** A page limit that is too low silently produces incomplete captures; one that is too
high produces archives nobody can review. Start narrow and specific.

> …

## Frequency

**How often?** Daily is a common answer. Consider what the dispute would turn on: if the question
is "when was this changed", the capture interval is the resolution of your answer.

> …

**On deployment as well?** The deployment webhook links a commit to the snapshot taken after it
went live, which is what closes the gap between code history and rendered state.

> …

## Retention

**How many years?** Six and ten are the common answers in a German commercial context. Take
advice; this is a legal question, not a technical one.

> …

**Have you done the storage arithmetic?** A WACZ for a medium page is typically 5–50 MB. Multiply
by pages, by frequency, by years. Write the number down.

> …

::: warning Retention can be extended, never shortened
The API refuses to shorten it. An archive whose operator can make inconvenient snapshots expire
early is not an archive. Choose a number you can live with and can afford to store.
:::

## Access

**Who may see the archive?** Snapshot views are recorded in the audit log with a real user
identity, so a shared account would make that log meaningless.

> …

**Local accounts or your identity provider?** Both work; OIDC is worth configuring if you already
run one.

> …

**Who may export evidence packages?** Exporting is what puts an archive in front of a third party.
It is audited like any other access.

> …

## Timestamping

**Which authority?** See [Timestamp authority](/operations/timestamping). If the answer is "the
default for now", set a date by which it will be a qualified provider, and do not treat anything
captured before then as usable in a dispute.

> …

**Do you have their certificate chain?** It goes in `deploy/tsa-chain.pem` and ships inside every
evidence package.

> …

## Storage

**Which backend?** See [Storage and WORM](/operations/storage). If the bundled Garage is the
answer, record that you know it enforces neither WORM nor redundancy, and why that is acceptable
for your case.

> …

**Where are the backups, and have you restored one?** The hash chain proves what existed; it does
not bring the WACZ files back. See [Backup and retention](/operations/backup).

> …
