# Timestamp certificates

`tsa-chain.pem` is the public FreeTSA CA certificate downloaded on 2026-09-15 from
https://freetsa.org/files/cacert.pem over HTTPS. Its byte-for-byte SHA-256 digest is:

```
2151b61137ffa86bf664691ba67e7da0b19f98c758e3d228d5d8ebf27e044438
```

This matches the download digest published at https://www.freetsa.org/index_en.php.
Check the downloaded file with `sha256sum deploy/tsa-chain.pem`. This is a file digest,
not an X.509 DER certificate fingerprint. No private key is distributed.

Compose mounts this file and configures `Evidence:CaChainPemPath`. Timestamp requests
ask the authority to include its signing certificate, so the CA suffices for the default
FreeTSA service. FreeTSA is for evaluation; it is not a qualified timestamp provider.

For another provider, obtain its CA/intermediate certificates from an authenticated
provider source, replace this bundle, and configure the TSA URL accordingly. Preserve
older certificates needed for historical timestamps. Review certificate validity,
revocation, and provider changes as part of deployment maintenance.

The API refuses startup for an explicitly configured unreadable, malformed, empty,
private-key-containing, or CA-free bundle. It repeats this check before building an
export so replacing a file after startup cannot silently produce a broken bundle.
An unconfigured path is supported, with a startup warning: recipients must supply
certificates themselves. Content validation does not establish trust, check revocation,
or prove that the configured CA issued the timestamp signing certificate. Verification
of the actual token against an independently authenticated CA remains necessary.

For certificate-level comparison, the SHA-256 fingerprint of the DER certificate is
`A6379E7CECC05FAA3CBF076013D745E327BBBAA38C0B9AF22469D4701D18AABC`.
Its not-after date is 2041-03-07 UTC. Inspect it with
`openssl x509 -in deploy/tsa-chain.pem -noout -fingerprint -sha256 -dates`.
