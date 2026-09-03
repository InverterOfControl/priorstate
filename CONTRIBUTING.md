# Contributing to PriorState

Thanks for considering it. Please read the two paragraphs about the DCO before you open a pull
request — that part is not negotiable.

## Developer Certificate of Origin

Every commit must be signed off:

```
Signed-off-by: Your Name <your.email@example.com>
```

`git commit -s` adds this line for you. CI rejects commits without it.

The sign-off is your statement that you wrote the contribution or otherwise have the right to
submit it under the project's licence — the full text is the [Developer Certificate of Origin
1.1](https://developercertificate.org/). This is required from the very first commit and cannot be
applied retroactively: merging third-party patches without provenance would permanently prevent
this project from ever being relicensed. That is why there are no exceptions.

## Scope — what will not be merged

PriorState is an evidence tool. Some features are absent on purpose and pull requests adding them
will be declined regardless of quality:

- Deleting individual snapshots, or any path that removes entries from the ledger
- Shortening retention after the fact
- Changing the timestamp source for existing entries
- Free-form capture settings that bypass named, versioned capture profiles
- Anything that lets the operator alter recorded history

If you think you have a case that these are wrong, open an issue and argue it before writing code.

## Ground rules

- **Code, comments, commit messages, issues and PRs in English.** The German-language material
  (`Verfahrensdokumentation` template, eIDAS/GoBD notes, evidence-package wording) stays German
  because it is written for a specific jurisdiction.
- The hash chain, canonical serialisation, and timestamp code are the load-bearing parts. Changes
  there need tests and a clear explanation in the PR description of why the guarantee still holds.
- **Never change the canonical serialisation format** without a versioned migration path. Existing
  evidence packages must remain verifiable forever; `verify.sh` must reproduce old hashes exactly.
- Keep `docker compose up` working. It is the first thing every evaluator does.

## Development setup

Requirements: .NET 10 SDK, Node 22+, Docker.

```bash
# dependencies only
cd deploy && docker compose up -d postgres garage

# backend
dotnet build
dotnet test

# frontend
cd src/ui && npm ci && npm run dev

# docs
cd docs && npm ci && npm run docs:dev
```

Integration tests use Testcontainers and start real Postgres and Garage instances, so Docker must
be running.

## Pull requests

1. Open an issue first for anything larger than a bug fix.
2. One logical change per PR.
3. `dotnet test` and `npm test` green.
4. Every commit signed off.
