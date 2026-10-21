# DNS/TLS capture plugin

Plugin ID: `dns-tls`. Package the published output with PriorState.PluginPackager, upload the `.psplugin` and restart both API and worker. Host builds package this included plugin automatically.

Example binding configuration:

```json
{
  "host": "example.com",
  "port": 443,
  "resolver": "192.0.2.53",
  "resolverPort": 53,
  "captureDns": true,
  "captureTls": true
}
```

An omitted resolver selects the first DNS address configured on an operational worker network interface. DNS queries use TCP and require the resolver to accept TCP connections. Seven queries capture A, AAAA, MX, TXT, NS, SOA and CAA; the envelope preserves complete request/response messages as Base64 plus decoded answer, authority and additional records, including TTL. Original message bytes are authoritative where rendered record strings lose encoding details. NXDOMAIN and successful empty answers are observations; other resolver errors fail the capture. DNSSEC authenticity is not validated.

TLS uses the worker's ordinary hostname resolution independently of the DNS observation resolver. The connected address is recorded. SNI uses the configured host. Invalid certificates are recorded as observations rather than rejected; no credentials or application data are sent. The leaf DER is preserved exactly. The chain is built by the worker and can contain local certificates; it is not represented as the exact chain sent on the wire. Revocation and certificate downloads are disabled. Validity, hostname and trust errors reflect the worker's trust store at observation time.

The payload is generated JSON with format `priorstate-dns-tls-observation`, version 1, rather than a verbatim remote JSON response. A capture fails as a whole if a selected DNS or TLS operation cannot complete. Cancellation and the deployment timeout cover all seven DNS queries and TLS together.

Deployment settings: `Plugins:DnsTls:AllowedHosts`, `Plugins:DnsTls:AllowedResolvers`, and `Plugins:DnsTls:Timeout` (30 seconds by default). The host allowlist must contain the configured hostname; an empty or whitespace-only list blocks capture. An empty resolver allowlist permits only the automatically selected worker resolver; explicitly configured resolvers must be allowlisted. Resolver policies apply to DNS observations; host policies apply to the target hostname. The plugin uses no secrets.


The generated JSON envelope is capped by Plugins:DnsTls:MaxPayloadBytes (2 MiB by default; valid range 1 byte–64 MiB). Oversized certificate material or a serialized envelope exceeding this limit fails capture without an archived payload. DNS packets are independently limited by the DNS TCP protocol to 65,535 bytes each.
