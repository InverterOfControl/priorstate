#!/bin/sh
#
# PriorState evidence package verification
# ========================================
#
# This script re-derives every claim the accompanying protocol makes, from the files in this
# package alone. It contacts no server and trusts nothing about the system that produced the
# package. If it prints OK at the end, then:
#
#   1. The payload file is byte-for-byte the file that was recorded.
#   2. The recorded metadata (URL, capture time, capture profile, and either the browser
#      conditions or the plugin that fetched it) hashes to the entry hash committed to the ledger.
#   3. That entry hash is provably part of the Merkle root for its day.
#   4. That Merkle root was submitted to an independent timestamp authority, which signed it at
#      the stated time — so the snapshot existed, in exactly this form, before that moment.
#   5. For a package produced by a capture plugin: the configuration shipped alongside is exactly
#      the one the ledger entry commits to.
#
# There are two kinds of package. A page capture ships snapshot.wacz and records the browser
# conditions it ran under; a plugin capture ships what an endpoint returned, plus the configuration
# it was fetched under. Which one this is follows from the first line of canonical/entry.txt.
#
# It is intentionally short and dependency-free so that it can be read in full before it is run.
# You are not expected to trust it; you are expected to read it.
#
# Requirements: a POSIX shell, openssl, xxd, and sha256sum (or shasum on macOS).
#
# Usage:   sh verify.sh              (from inside the unpacked package)
# Exit:    0 = every check passed, 1 = a check failed, 2 = the package is unusable
#
# Licence: AGPL-3.0-only, part of PriorState. https://github.com/InverterOfControl/priorstate

set -eu

PACKAGE_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$PACKAGE_DIR"

FAILURES=0

say()  { printf '%s\n' "$*"; }
pass() { printf '  [ OK ]  %s\n' "$*"; }
fail() { printf '  [FAIL]  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
die()  { printf 'ERROR: %s\n' "$*" >&2; exit 2; }

# --- Tool discovery -----------------------------------------------------------------------

command -v openssl >/dev/null 2>&1 || die "openssl is required but was not found."
command -v xxd     >/dev/null 2>&1 || die "xxd is required but was not found."

if command -v sha256sum >/dev/null 2>&1; then
  sha256_file() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum >/dev/null 2>&1; then
  sha256_file() { shasum -a 256 "$1" | cut -d' ' -f1; }
else
  die "Neither sha256sum nor shasum was found."
fi

# SHA-256 of raw bytes arriving on stdin, printed as lowercase hex.
sha256_stdin() { openssl dgst -sha256 -binary | xxd -p -c 256 | tr -d '\n'; }

# Hex string on stdin -> raw bytes on stdout.
hex_to_bin() { xxd -r -p; }

# Reads a single "key=value" field out of the canonical entry file.
canonical_field() {
  sed -n "s/^$1=//p" canonical/entry.txt | head -n 1
}

# Which canonical form this entry was hashed under. It decides which payload file the package
# ships and which field names the entry uses. An unknown form means this script is older than the
# package: stop rather than guess.
FORM="$(head -n 1 canonical/entry.txt | tr -d '\r')"

case "$FORM" in
  priorstate-snapshot-v1)
    PAYLOAD_FILE="snapshot.wacz"
    PAYLOAD_HASH_FIELD="wacz_sha256"
    EXTRA_REQUIRED=""
    ;;
  priorstate-snapshot-v2)
    PAYLOAD_FILE="$(sed -n 's/^payload_file=//p' manifest.txt | head -n 1)"
    PAYLOAD_HASH_FIELD="payload_sha256"
    EXTRA_REQUIRED="plugin/binding.txt plugin/configuration.json"
    [ -n "$PAYLOAD_FILE" ] || die "This package declares no payload_file in manifest.txt."
    ;;
  *)
    die "Unknown canonical form '$FORM'. This package was written by a newer PriorState than this script."
    ;;
esac

for required in canonical/entry.txt manifest.txt "$PAYLOAD_FILE" timestamp/token.tsr timestamp/root.txt $EXTRA_REQUIRED; do
  [ -f "$required" ] || die "This package is incomplete: $required is missing."
done

MANIFEST_ENTRY_HASH="$(sed -n 's/^entry_hash=//p' manifest.txt | head -n 1)"
MERKLE_ROOT="$(tr -d '\n\r ' < timestamp/root.txt)"

say "PriorState evidence package verification"
say "========================================"
say ""
say "  URL           $(canonical_field url)"
say "  Captured      $(canonical_field captured_at)"
say "  Profile       $(canonical_field profile)"
say "  Chain entry   $(canonical_field sequence)"
say ""

# --- 1. The payload is the payload that was recorded -----------------------------------------
#
# The canonical entry names a SHA-256 for the payload. Recompute it from the file on disk. If this
# fails, the payload has been altered or replaced since it was recorded.

say "1. Payload integrity"
EXPECTED_PAYLOAD="$(canonical_field "$PAYLOAD_HASH_FIELD")"
ACTUAL_PAYLOAD="$(sha256_file "$PAYLOAD_FILE")"

if [ "$EXPECTED_PAYLOAD" = "$ACTUAL_PAYLOAD" ]; then
  pass "$PAYLOAD_FILE matches the recorded hash ($ACTUAL_PAYLOAD)"
else
  fail "$PAYLOAD_FILE does NOT match."
  say  "          recorded: $EXPECTED_PAYLOAD"
  say  "          actual:   $ACTUAL_PAYLOAD"
fi
say ""

# --- 2. The recorded metadata hashes to the committed entry hash ----------------------------
#
# canonical/entry.txt holds the exact bytes that were hashed into the ledger: a fixed field
# order, LF line endings, UTF-8, one trailing newline. Read it — it is plain text, and it is the
# complete set of facts being asserted about this capture.

say "2. Ledger entry"
ACTUAL_ENTRY_HASH="$(sha256_stdin < canonical/entry.txt)"

if [ "$MANIFEST_ENTRY_HASH" = "$ACTUAL_ENTRY_HASH" ]; then
  pass "the recorded metadata hashes to $ACTUAL_ENTRY_HASH"
else
  fail "the metadata does NOT hash to the committed entry hash."
  say  "          committed: $MANIFEST_ENTRY_HASH"
  say  "          actual:    $ACTUAL_ENTRY_HASH"
fi
say ""

# --- 3. The entry belongs to the timestamped Merkle root -------------------------------------
#
# Every entry from one UTC day is a leaf in a binary Merkle tree, and one root per day is
# timestamped. The audit path is the list of sibling hashes from this leaf up to the root;
# replaying it reconstructs the root without needing any other snapshot from that day.
#
# Domain separation follows RFC 6962: leaves are hashed with a 0x00 prefix, internal nodes with
# 0x01, so a leaf can never be substituted for a node.

say "3. Merkle inclusion"
NODE="$(printf '00%s' "$ACTUAL_ENTRY_HASH" | hex_to_bin | sha256_stdin)"

if [ -f merkle/audit-path.txt ]; then
  while IFS=' ' read -r SIDE SIBLING; do
    [ -n "${SIDE:-}" ] || continue
    case "$SIDE" in
      L) PAIR="$SIBLING$NODE" ;;   # sibling on the left, this node on the right
      R) PAIR="$NODE$SIBLING" ;;   # this node on the left, sibling on the right
      *) die "Malformed audit path entry: '$SIDE $SIBLING'" ;;
    esac
    NODE="$(printf '01%s' "$PAIR" | hex_to_bin | sha256_stdin)"
  done < merkle/audit-path.txt
fi

if [ "$NODE" = "$MERKLE_ROOT" ]; then
  pass "this entry is part of the day's root ($MERKLE_ROOT)"
else
  fail "this entry does NOT reconstruct the timestamped root."
  say  "          timestamped root: $MERKLE_ROOT"
  say  "          reconstructed:    $NODE"
fi
say ""

# --- 4. An independent authority attested to that root ---------------------------------------
#
# This is the check that does not depend on the archive operator at all. The RFC-3161 token was
# issued by the timestamp authority named in the protocol, over the Merkle root above. openssl
# verifies the authority's signature against its certificate chain.
#
# If tsa-chain.pem is absent the operator did not ship the authority's certificates; obtain them
# from the authority named in the protocol and re-run with -CAfile pointing at them.

# token.tsr holds a bare TimeStampToken (the signed CMS structure), not a full TimeStampResp,
# which is why -token_in is needed below. Without it openssl looks for a response wrapper that
# is not there and fails with an ASN.1 tag error rather than anything about the signature —
# reporting a perfectly valid timestamp as invalid.
say "4. Timestamp"
if [ -f timestamp/tsa-chain.pem ]; then
  if openssl ts -verify \
        -digest "$MERKLE_ROOT" \
        -token_in \
        -in timestamp/token.tsr \
        -CAfile timestamp/tsa-chain.pem >/dev/null 2>&1; then
    pass "the timestamp token is valid and covers the root"
  else
    fail "the timestamp token did NOT verify against timestamp/tsa-chain.pem."
    say  "          re-run manually for the full reason:"
    say  "          openssl ts -verify -digest $MERKLE_ROOT \\"
    say  "            -in timestamp/token.tsr -token_in -CAfile timestamp/tsa-chain.pem"
  fi
else
  fail "timestamp/tsa-chain.pem is missing, so the signature cannot be checked offline."
  say  "          obtain the authority's certificate chain and re-run:"
  say  "          openssl ts -verify -digest $MERKLE_ROOT \\"
  say  "            -in timestamp/token.tsr -token_in -CAfile <chain.pem>"
fi

say ""

# --- 5. The plugin ran under the configuration shipped here ----------------------------------
#
# Only for a plugin capture. The entry commits to a digest of the binding, and the binding commits
# to a digest of the configuration. Recomputing both is what answers the obvious objection: that
# the endpoint was pointed somewhere else and the result presented as this one.

if [ "$FORM" = "priorstate-snapshot-v2" ]; then
  say "5. Plugin configuration"

  EXPECTED_BINDING="$(canonical_field binding_digest)"
  ACTUAL_BINDING="$(sha256_file plugin/binding.txt)"

  if [ "$EXPECTED_BINDING" = "$ACTUAL_BINDING" ]; then
    pass "plugin/binding.txt is the configuration the ledger entry names"
  else
    fail "plugin/binding.txt does NOT match the ledger entry."
    say  "          recorded: $EXPECTED_BINDING"
    say  "          actual:   $ACTUAL_BINDING"
  fi

  EXPECTED_CONFIG="$(sed -n 's/^config_sha256=//p' plugin/binding.txt | head -n 1)"
  ACTUAL_CONFIG="$(sha256_file plugin/configuration.json)"

  if [ "$EXPECTED_CONFIG" = "$ACTUAL_CONFIG" ]; then
    pass "plugin/configuration.json is the configuration that was used"
  else
    fail "plugin/configuration.json does NOT match."
    say  "          recorded: $EXPECTED_CONFIG"
    say  "          actual:   $ACTUAL_CONFIG"
  fi

  say ""
  say "  Plugin:         $(canonical_field plugin) $(canonical_field plugin_version)"
  say "  Configuration:  $(canonical_field binding)"
fi

say "  Asserted time:  $(openssl ts -reply -in timestamp/token.tsr -token_in -text 2>/dev/null \
                          | sed -n 's/^ *Time stamp: *//p' | head -n 1)"
say "  Authority:      $(sed -n 's/^tsa_url=//p' manifest.txt | head -n 1)"
say "  Qualified:      $(sed -n 's/^tsa_qualified=//p' manifest.txt | head -n 1)"
say ""

# --- Result ----------------------------------------------------------------------------------

if [ "$FAILURES" -eq 0 ]; then
  say "RESULT: OK — every check passed."
  say ""
  say "Note on scope: these checks prove that this payload is unaltered since it was recorded and"
  say "that it existed before the attested time."
  if [ "$FORM" = "priorstate-snapshot-v2" ]; then
    say "They say nothing about whether what the endpoint returned was correct. What is attested is"
    say "receipt, not truth: that these bytes came back from the URL in canonical/entry.txt, under"
    say "the configuration in plugin/configuration.json, before the attested time."
  else
    say "They say nothing about whether the capture was complete or representative; for that,"
    say "inspect $PAYLOAD_FILE itself and the capture profile and conditions listed in"
    say "canonical/entry.txt."
  fi
  exit 0
fi

say "RESULT: FAILED — $FAILURES check(s) did not pass. This package should not be relied upon."
exit 1
