# Security Policy

## Reporting a vulnerability

Report privately via [GitHub Security Advisories](https://github.com/saschalaabs/priorstate/security/advisories/new).
Please do not open a public issue for a vulnerability.

Expect an acknowledgement within a few days. This is a spare-time project — see the support
expectations below — but security reports are handled ahead of everything else.

Vulnerabilities in the **hash chain, canonical serialisation, timestamp handling, evidence package
generation or the append-only database constraints** are the most serious class this project has.
A flaw that lets recorded history be altered undetectably defeats the entire purpose. Please flag
such reports as critical.

## Scope

In scope: the PriorState API, worker, ledger, evidence generation, verification script, UI, and the
shipped container/compose configuration.

Out of scope: vulnerabilities in upstream projects (report those to
[browsertrix-crawler](https://github.com/webrecorder/browsertrix-crawler),
[Garage](https://git.deuxfleurs.fr/Deuxfleurs/garage), PostgreSQL, .NET) — though a heads-up is
welcome if PriorState's use of them makes an upstream issue exploitable here.

## Support expectations

This is maintained on a best-effort basis, with no service-level commitment, no guaranteed
response time and no backports to old versions. Security fixes land on `main` and in the next
release. Self-hosters are responsible for updating.

## Evidentiary responsibility

**PriorState produces records intended to be used as evidence. Responsibility for their
evidentiary value lies with the operator, not with this project or its author.** Whether a court
accepts an archive depends on the operator's process, timestamp authority, retention and access
controls — not only on the software. See the `Verfahrensdokumentation` template in `docs/`.

The licence's warranty disclaimer (AGPL-3.0 §15–16) applies in full.
