## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## Does this touch the evidentiary path?

The hash chain, canonical serialisation, timestamp handling, append-only constraints, evidence
package or `verify.sh`.

- [ ] No — this change cannot affect recorded history or its verification.
- [ ] Yes — explained below, with tests.

<!-- If yes: why does the guarantee still hold? Can existing evidence packages still be verified? -->

## Checklist

- [ ] Every commit is signed off (`git commit -s`) — DCO, see CONTRIBUTING.md
- [ ] `dotnet test` passes
- [ ] `npm test` passes (if the UI changed)
- [ ] Docs updated (if behaviour changed)
- [ ] `docker compose up` still reaches a working UI
