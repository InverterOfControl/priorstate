# Timestamp authority

The most consequential setting in PriorState, and the one that cannot be corrected afterwards.

## What the timestamp does

Once a day, PriorState computes a Merkle root over that day's ledger entries and sends it to an
RFC-3161 timestamp authority. The authority returns a signed token asserting that it saw that
exact value at that time.

This is the only part of the system that does not depend on you. The hash chain shows internal
consistency, which is worth something — but an operator who controls the database controls the
chain. The token is signed by a third party with its own key, and it makes altering any entry in
that day contradict a signature you cannot forge.

## The default is not good enough for a dispute

PriorState ships with FreeTSA configured, so the system can be tried end to end without an
account. FreeTSA is real, its tokens verify correctly, and it is **not a qualified trust service
provider under eIDAS**.

The interface displays a banner while an unqualified authority is in use, the protocol prints a
warning on every affected package, and the logs say so on every anchor. None of these can be
switched off, because the alternative is someone discovering it when a package is challenged.

::: danger This cannot be fixed later
Snapshots cannot be re-anchored to a different authority. A day anchored to FreeTSA keeps that
anchor for ever — re-stamping it later would only prove the entry existed at the later date, which
is precisely the fact in dispute. Configure a qualified provider **before** capturing anything you
may need to rely on.
:::

## Configuring a qualified provider

Qualified providers under eIDAS charge per timestamp. PriorState anchors once per day rather than
once per snapshot specifically to keep that cost bounded — a busy archive and a quiet one cost the
same.

```bash
# deploy/.env
TSA_URL=https://tsa.example-qtsp.eu/tsr
TSA_QUALIFIED=true
TSA_DISPLAY_NAME=Example QTSP qualified timestamp service
```

`TSA_QUALIFIED` is an assertion by you, recorded on every anchor and printed on every protocol.
PriorState cannot verify a provider's qualified status; setting it to `true` for a provider that
is not on the EU trusted list would put a false statement into a legal document.

Then place the provider's certificate chain in `deploy/tsa-chain.pem`. It is copied into every
evidence package so the recipient can verify the token **offline, years later**, without the
authority still being reachable — which for a ten-year retention is not a hypothetical.

For the FreeTSA default:

```bash
curl -o deploy/tsa-chain.pem https://freetsa.org/files/cacert.pem
```

## If the authority is unreachable

Anchoring runs hourly and retries any day that is still unanchored, with standard resilience
handling on the HTTP call. A day that could not be stamped stays pending and is picked up later;
the entries are already in the chain and nothing is lost.

Anchoring runs hourly rather than at a fixed time of day so that an installation switched off
overnight still anchors. Unanchored entries are the one backlog this system must not accumulate:
`/api/ledger/status` reports the count, and the Ledger page shows it.

## Verifying a token by hand

```bash
openssl ts -reply -in timestamp/token.tsr -token_in -text
openssl ts -verify -digest <merkle-root-hex> -in timestamp/token.tsr -CAfile tsa-chain.pem
```

This is exactly what `verify.sh` does in step 4, and what an opposing expert will run.
