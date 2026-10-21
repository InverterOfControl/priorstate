# S3 capture plugin

Archives one object's original bytes per binding. Works with AWS S3 and compatible stores. It does not list buckets, create objects, or share the host's evidence-store client.

Publish this project, create a `.psplugin` with PriorState.PluginPackager, upload it on the sources page, and restart API and worker. Host builds package this included plugin automatically.

Deployment configuration:

```json
{"Plugins":{"S3":{"AllowedHosts":["objects.example.com"],"AllowInsecureHttp":false,"MaxPayloadBytes":33554432,"Timeout":"00:00:30"}}}
```

The host allowlist defaults to deny (blank entries do not grant access). HTTP needs explicit operator permission. Configure the correct regional endpoint directly; HTTP redirects are disabled and every SDK transport request is checked against the configured origin, including SDK regional redirect attempts. Implicit system/environment proxies and cookies are disabled. Endpoint URLs cannot contain credentials, paths, query strings or fragments.

Binding configuration:

```json
{"endpoint":"https://objects.example.com","region":"us-east-1","bucket":"documents","key":"prices/current.pdf","forcePathStyle":true}
```

Optional `versionId` pins an object version. Otherwise each run captures the current object. Endpoint and object identity are public evidence. For virtual-host addressing set `forcePathStyle` to false and allow the configured endpoint host.

Set the binding's secret reference to an environment variable such as `PS_SECRET_SOURCE_S3`. Its value on the worker must be JSON:

```json
{"accessKeyId":"example","secretAccessKey":"example","sessionToken":"optional"}
```

Use read-only source credentials. Session token is optional; credentials are explicit and never obtained from the AWS default credential chain or host storage configuration. Do not put credentials in binding JSON. SDK errors are replaced with sanitized messages.

Reads are bounded in memory by deployment policy and the deadline covers the response body. Truncated responses fail. The host currently rejects zero-byte captures. Original bytes use the source Content-Type (or application/octet-stream) and a credential-free s3 URI; object version, when configured, is in that URI. No metadata envelope is generated.
