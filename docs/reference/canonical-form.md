# Canonical form

The canonical form is the exact byte sequence that gets hashed into the ledger. It is the
compatibility contract between PriorState, every evidence package it has ever produced, and the
`verify.sh` script the opposing party runs.

::: danger This format never changes
Changing, reordering or removing a field in an existing version makes every evidence package ever
exported unverifiable. A new field means a new version marker and a new branch in the renderer;
snapshots keep being rendered under the version they were written with, for ever.
:::

## Version 1

Marker: `priorstate-snapshot-v1`

Encoding: UTF-8, LF line endings, one trailing newline, no byte order mark.

```
priorstate-snapshot-v1
sequence=1
prev=0000000000000000000000000000000000000000000000000000000000000000
url=https://example.com/prices
final_url=
captured_at=2026-09-03T14:30:00Z
wacz_sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
wacz_size=1048576
profile=DE-Standard v1
user_agent=Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36
viewport=1920x1080
authenticated=false
adblock=false
cookie_banner=left_as_is
js_settle_ms=5000
chromium=140.0.7259.68
crawler=1.7.1
```

The entry hash is `SHA-256` over exactly those bytes.

## Fields

| Field | Meaning |
|---|---|
| `sequence` | Position in the chain, contiguous from 1. A gap is tampering. |
| `prev` | Entry hash of the preceding entry, or 64 zeros for the first. |
| `url` | The URL as requested, before any redirect. |
| `final_url` | Where the browser ended up, empty if unchanged. A redirect is itself evidence. |
| `captured_at` | UTC, second precision, always `Z`-suffixed. |
| `wacz_sha256` | SHA-256 of the archive file as stored, lowercase hex. |
| `wacz_size` | Size in bytes. |
| `profile` | Capture profile name and version, e.g. `DE-Standard v1`. |
| `user_agent` … `crawler` | The conditions the capture actually ran under. |

## Version 2

Marker: `priorstate-snapshot-v2`

Used for a snapshot produced by a [capture plugin](/operations/plugins) rather than by a browser.
Version 1 is still used for every page capture and always will be; which one applies is recorded on
the snapshot, so adding this version did not change a single existing entry hash.

```
priorstate-snapshot-v2
sequence=2
prev=a4c1...
url=https://erp.example.com/api/prices
final_url=
captured_at=2026-09-03T14:30:00Z
payload_sha256=9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08
payload_size=2048
payload_media_type=application/json
profile=DE-Standard v1
plugin=http-json
plugin_version=1.4.2
binding=erp-prices v3
binding_digest=56c946e0e9db65166f4eef0f32f714d0bfe94dd34f0d2e5addb65e7e4b6f41ca
```

| Field | Meaning |
|---|---|
| `payload_sha256` | SHA-256 of the archived response as stored, lowercase hex. |
| `payload_size` | Size in bytes. |
| `payload_media_type` | The media type the source reported, e.g. `application/json`. |
| `profile` | The capture profile the run executed under. |
| `plugin` | Which plugin fetched it, e.g. `http-json`. |
| `plugin_version` | The plugin's version, read from the assembly that ran. |
| `binding` | Name and version of the configuration it ran under. |
| `binding_digest` | SHA-256 of the plugin binding form below. |

The browser fields are absent rather than empty. An API call has no viewport, no user agent and no
Chromium version, and writing one down would be a false statement in the one document that exists
to be read adversarially.

`binding_digest` is what makes the configuration provable. Without it, the endpoint a snapshot
claims to have been fetched from would be a mutable row on the archive operator's own server, and
the obvious objection — you pointed it somewhere else and re-ran it — would have no answer.

## Plugin binding form

Marker: `priorstate-plugin-binding-v1`

The bytes `binding_digest` is computed over. Shipped in the evidence package as
`plugin/binding.txt`, alongside the configuration itself as `plugin/configuration.json`.

```
priorstate-plugin-binding-v1
plugin=http-json
name=erp-prices
version=3
secret_ref=PS_SECRET_ERP_TOKEN
required=false
created_at=2026-09-01T08:00:00Z
config_sha256=fbaad759812738f6695a660fa632871778e05b1c95c1e03f2f0e375371e16a3a
```

The configuration is committed to by digest rather than inline, because its shape belongs to the
plugin and not to the ledger. A plugin added later must not require a new canonical form version,
and this file must not have to learn what an HTTP header is in order to hash a binding.

`secret_ref` is the *name* of an environment variable. The value it holds is never recorded, never
stored in the database and never shipped in a package.

## Escaping

Within a value: `\` becomes `\\`, LF becomes `\n`, CR becomes `\r`. Nothing else is escaped.

This exists so that a value cannot introduce a line break and forge a record. A URL containing a
newline followed by `url=https://evil.example/` would otherwise produce a canonical form with two
`url` lines, and a naive parser could be made to read the wrong one.

## Why line-oriented rather than JSON

Canonical JSON (RFC 8785) is a well-specified answer to the same problem, and it is painful to
reproduce in a shell script. `verify.sh` has to stay short enough that a court-appointed expert
will actually read it before running it, and rebuilding these lines with `printf` and `sed` is
something anyone can check by eye. Readability of the verifier is worth more here than elegance of
the format.

## Deliberately excluded

**Extracted page text.** It is derived from the WACZ and reproducible from it, and it feeds search
and the diff view. Including it would make the hash depend on the text-extraction implementation,
so an upgrade to browsertrix would break verification of old snapshots for no gain.

**The plugin configuration itself.** Committed to by digest, and shipped in full next to the
entry so a recipient can read it and recompute that digest. Inlining it would put an arbitrary
plugin's data model inside a format that has to stay stable for decades.

**Storage WORM status and the timestamp anchor.** Both are recorded on the snapshot and printed on
the protocol, but neither is hashed. The anchor is assigned after the entry is written — hashing it
would be circular — and the WORM status is an observation about the storage layer, not a claim
about the captured content.
