# Additional sources

An API source archives an HTTP response alongside a project's website capture. For example,
archive a shop page together with the prices returned by its pricing API.

## Set up a source

1. Open **Projects → your project → Additional sources → Add source** and select **HTTP endpoint**.
2. Enter a descriptive name and the API's absolute HTTP(S) URL.
3. Select **GET**, or **POST** for an API that uses a request body to read data.
4. Choose no authentication, a Bearer token, or a custom authentication header.
5. Explain why this source belongs in the archive, then save.
6. Select **Test connection** on the saved source. This uses the worker's network, host allowlist,
   credentials and response-size limit. Success shows the response type and byte count.
7. Use **Capture now** on the project to archive the website and its API sources.

The **Sources** navigation page provides the same controls with a project selector.
Use **Change settings** to create a new version. The source name identifies its version history;
retiring it stops future captures without deleting earlier settings or evidence.

Connection tests make a real request, including POST bodies. Use API operations intended to read
data. Requests are not automatically retried by the HTTP client. Tests store only operational
metadata, not the response body, object storage content or a ledger entry. A test waiting for over
two minutes usually means the worker is unavailable; restore it and test again.

## Authentication

Credentials are referenced by name and resolved only by the worker. For a new source:

```bash
# deploy/secrets.env (copy secrets.env.example first)
PS_SECRET_PRICES_TOKEN=your-token
PS_SECRET_INVENTORY_KEY=your-other-key
```

In the source form select **Bearer token** and enter `PS_SECRET_PRICES_TOKEN` as the credential
name. For an API key, select **API key / custom header**, enter a header such as `X-API-Key`,
and use `PS_SECRET_INVENTORY_KEY`. The optional prefix includes any required trailing space.

Compose loads `secrets.env` only into the worker. Apply changes with
`docker compose up -d --force-recreate worker` from `deploy/`; restarting an existing container
does not reload its environment. Names must match `PS_SECRET_[A-Z0-9_]+`.

The existing `PS_SECRET_ERP_TOKEN` setting remains in `deploy/.env` for compatibility and takes
precedence over the same name in `secrets.env`. Use distinct names for additional credentials.

Never put credentials in the source URL, body or non-secret headers. These configuration values
are stored permanently, committed by the ledger hash and included in evidence packages. The
secret's name is recorded; its value is not added to the configuration or request diagnostics.
An upstream API can itself return sensitive data, so check what its response contains.

## Request settings and host access

**Additional request settings** contains the Accept header, request Content-Type and non-secret
headers. Headers must be a JSON object with string values, for example:

```json
{ "X-Tenant": "shop-a" }
```

Configure allowed hosts on the worker in `deploy/.env`:

```bash
PLUGIN_HTTP_ALLOWED_HOST_0=api.example.com
PLUGIN_HTTP_ALLOWED_HOST_1=inventory.internal
```

Blank entries are ignored. With no nonblank entries, all HTTP(S) hosts reachable from the worker
are allowed. Restrict this list for your deployment, especially if it can reach internal services.
Redirects are not followed: configure the final endpoint explicitly so a redirect cannot bypass
the host list or forward an authentication header elsewhere.

For example, to capture `https://jsonplaceholder.typicode.com/posts/1`, add
`PLUGIN_HTTP_ALLOWED_HOST_0=jsonplaceholder.typicode.com` to `deploy/.env` (or use another
unused index, preserving existing hosts), then recreate the worker with
`docker compose up -d --force-recreate worker`. Enter only the hostname in the allowlist,
without `https://` or a path. Save the source as GET without authentication and test it.

If an older worker rejects every host despite empty allowlist settings, rebuild and migrate
the updated deployment as described below. Older versions interpreted Compose's blank entries
as a restrictive list. A log saying that a run completed with one snapshot does not establish
that its API source succeeded: that snapshot can be only the website archive. The updated Runs
page exposes source errors and distinguishes partial success.

The default maximum response size is 32 MiB, configurable with `PLUGIN_MAX_PAYLOAD_BYTES`.
The default request timeout is 30 seconds and covers receiving the body as well as headers.
Non-success HTTP responses, empty bodies and invalid JSON announced as JSON are reported as
failures rather than stored as successful captures.

## Capture timing and results

API-source processing starts alongside the website crawler. Sources run sequentially within that
processing task, so the website and every API cannot be promised to represent the same instant.
Each API snapshot records when its response was completely received, before object storage work.
The source versions are selected as of the run's first start, including on crawl retries.

In **Runs → Website and API results**, each source shows its version, status, request start time,
error if any, and a link to its archived response. The response snapshot contains its capture time,
hashes and evidence export. Successful website archives are linked in the same overview.

- An optional API failure yields **Partially succeeded** when the website succeeds.
- A required API failure marks the run **Failed**, but keeps every successful capture. It does not
  automatically re-crawl the website solely because that API failed; start a new run to retry it.
- A browser failure does not cancel the API-source processing. Successful API responses remain
  archived. Existing browser retry rules apply, but successfully archived sources are not fetched
  again for the same run. Website retries use distinct storage keys.
- Old runs may have only the existing failure list and snapshots, without structured source
  results. Both remain readable. Old ledger entries and canonical formats are unchanged.

Source execution and test results are mutable operational records. The successful response's
snapshot is the immutable, hash-chained record. Test results are not evidence.

## Storage and evidence

Each successful response is stored byte for byte as a separate snapshot under the same run and
global ledger as the website. Its configuration is named and versioned internally as a plugin
binding. The entry commits to both the payload hash and the binding digest.

The [evidence package](/guide/evidence-package) includes the original response (`payload.json`
for JSON), `plugin/binding.txt` and `plugin/configuration.json`, plus the timestamp and Merkle proof.
Verification also checks the binding and configuration digests. External anchoring happens in
batches; it does not make the API and website captures simultaneous.

With independently trusted TSA roots, verification establishes that the committed bytes and
metadata existed by the signed timestamp. It does not independently establish their source URL,
the truth of the response, or the accuracy of the operator-recorded capture time.

## Upgrading and adding plugins

This change adds the operational `source_executions` table. Run the one-shot migration service
before starting the updated API and worker; see [database upgrades](/operations/database).
Docker Compose 2.24 or later is needed for the optional `secrets.env` file; see the
[Docker environment-file documentation](https://docs.docker.com/compose/how-tos/environment-variables/set-environment-variables/#additional-information-1).

The underlying plugin interface remains `ICapturePlugin` from `PriorState.Plugins.Abstractions`.
Plugins are installed as `.psplugin` ZIP packages and loaded once when API and worker start.
They receive configuration and return bytes; the host handles storage and ledger writes.
Assembly load contexts isolate dependencies, but are not a sandbox for untrusted code.

The shipped plugin retains its stable `http-json` ID, so existing bindings remain recognised.
Existing settings must meet the request validation rules: GET or POST, no URL credentials,
and authentication through a secret reference rather than static Authorization/Cookie headers.

## Installing packages

The **Additional sources** page lists loaded package hashes and offers an upload to authorised
operators. Copy your own identity from that page into `PLUGIN_OPERATOR_ID` in `deploy/.env`,
then recreate the API container. The value is the exact authenticated NameIdentifier claim,
not an email address. For non-Compose installations set `Plugins:OperatorIds` to an array of
these identities. An empty list disables installation; ordinary users can still configure sources.

Only install packages from trusted publishers. Their code runs with application permissions
in API (registration and validation) and worker (capture). Upload validation checks the manifest,
paths, contract version, bounded ZIP contents and SHA-256 without loading executable code.
It cannot prove that a plugin is safe or its entry point can run; incompatible binaries fail startup.
There is no package signature trust store or automatic restart.

Upload a `.psplugin` file, then run from `deploy/`:

```bash
docker compose restart api worker
```

Changes to environment configuration require `docker compose up -d --force-recreate api worker`
instead. Compose mounts the same `priorstate-plugins` volume in both processes. With other
deployments configure the same shared filesystem as `Plugins:Directory` in both processes.
The package page detects different loaded hashes, pending installations, and missing/stale worker
reports. It assumes one API and one worker reporter for this installation.

Packages live under `packages/<sha256>.psplugin`; `active.json` selects one installed package
per plugin ID. Replacing an ID installs an update. Earlier packages remain available and running
processes keep their original loaded code. For recovery from a package that prevents startup,
stop both processes and restore the previous identity in `active.json` from a backup. Keep the
package volume in your backups. Installation authorisation is audited before activation; failed
activation can therefore leave an authorisation entry without a completed install.

Run migrations before upgrading. New plugin snapshots use `priorstate-snapshot-v3`, committing
the actual package ID and package SHA-256 alongside the observed assembly version. Existing
v1 and v2 snapshots retain their exact canonical bytes and remain exportable/verifiable. A package
hash identifies captured code; it does not independently prove the plugin executed as claimed.

## Included source plugins

- **HTTP endpoint** (`http-json`): existing GET/POST configuration and secret references remain compatible.
- **S3 object** (`s3`): one object per binding, unchanged bytes, optional `versionId`. Configure
  endpoint, region, bucket and key. Set `PLUGIN_S3_ALLOWED_HOST` and, only for trusted HTTP
  endpoints, `PLUGIN_S3_ALLOW_HTTP=true`. Its worker secret is JSON containing `accessKeyId`,
  `secretAccessKey`, and optional `sessionToken`. Archive-storage credentials are never reused.
- **DNS and TLS** (`dns-tls`): A/AAAA/MX/TXT/NS/SOA/CAA records and TTLs, original DNS TCP
  messages, original certificate DER, worker-built chain and TLS validation observations.
  Set `PLUGIN_DNS_TLS_ALLOWED_HOST`; an explicit resolver additionally needs
  `PLUGIN_DNS_ALLOWED_RESOLVER`. Without one, the worker's network-interface DNS resolver is used.
  DNS over TCP must be reachable. DNSSEC and certificate revocation are not checked; invalid
  certificates are recorded as observations. TLS hostname resolution uses the worker resolver
  independently of the DNS observation resolver.
- **Git repository** (`git`): HTTPS branch/tag/commit resolved to its full commit ID; complete
  file tree as a reproducible ZIP with `manifest.json` and `files/`. Set `PLUGIN_GIT_ALLOWED_HOST`.
  Private repositories use an optional PAT worker secret and a non-secret username. History,
  `.git`, submodule downloads and LFS downloads are excluded; gitlinks and LFS pointers are
  preserved. Symlinks are stored as their original link text and mode rather than followed.

New plugins deny target hosts unless allowlisted. More hosts can be configured using indexed
environment variables such as `PLUGINS__GIT__ALLOWEDHOSTS__1`. Secrets belong only in the worker
environment (`deploy/secrets.env`); binding configuration is permanent evidence. S3 and Git
default to a 32 MiB payload limit. Git also limits file count and temporary storage; the latter
is monitored during subprocess execution and can briefly overshoot between checks.
DNS/TLS uses a bounded 2 MiB observation envelope. HTTP and S3 timeouts default to 30 seconds,
DNS/TLS to 30 seconds, Git to 60 seconds. Connection tests execute the same source operations
without retaining payloads or appending the ledger.

## Building a plugin package

Implement `ICapturePluginModule.Register(IServiceCollection, IConfiguration)` to register exactly
one singleton `ICapturePlugin` from the entry assembly. Supply `Definition` (public configuration
fields and secret help) and `ValidateConfiguration`; throw `PluginException` for configuration
errors. Configuration field types are `string`, `number`, `boolean`, and `multiline`.
Do not resolve worker secrets in API validation.

Place this manifest at the published output root:

```json
{
  "formatVersion": 1,
  "contractVersion": 1,
  "id": "my-source",
  "entryAssembly": "MySource.dll",
  "entryType": "MySource.PluginModule"
}
```

Copy `plugin.json` to output/publish and enable `CopyLocalLockFileAssemblies` so runtime dependencies
are included. IDs are stable lowercase identifiers and must match `ICapturePlugin.Id`.
The abstraction, domain and Microsoft.Extensions contracts are shared with the host; plugin-specific
dependencies are resolved inside their own load context. Match the host's .NET 10 contract version.

```bash
dotnet publish MySource.csproj -c Release -o ./plugin-output
dotnet run --project src/PriorState.PluginPackager -- ./packages ./plugin-output
```

The packager creates deterministic ZIPs with sorted entries and fixed timestamps. API and worker
builds automatically package all four included plugins; neither host hardcodes their capture types.
The source interface preserves original bytes; generated observation envelopes and archives must
document their format/version and distinguish original bytes from computed metadata.
