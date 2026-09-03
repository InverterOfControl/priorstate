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

**Storage WORM status and the timestamp anchor.** Both are recorded on the snapshot and printed on
the protocol, but neither is hashed. The anchor is assigned after the entry is written — hashing it
would be circular — and the WORM status is an observation about the storage layer, not a claim
about the captured content.
