# Additional API sources

An API source archives an HTTP response alongside a project's website capture. For example,
archive a shop page together with the prices returned by its pricing API.

## Set up a source

1. Open **Projects → your project → Additional API sources → Add API source**.
2. Enter a descriptive name and the API's absolute HTTP(S) URL.
3. Select **GET**, or **POST** for an API that uses a request body to read data.
4. Choose no authentication, a Bearer token, or a custom authentication header.
5. Explain why this source belongs in the archive, then save.
6. Select **Test connection** on the saved source. This uses the worker's network, host allowlist,
   credentials and response-size limit. Success shows the response type and byte count.
7. Use **Capture now** on the project to archive the website and its API sources.

The **API sources** navigation page provides the same controls with a project selector.
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
Plugins are compiled in and registered explicitly using `AddCapturePlugin<T>()`. They receive
configuration and return bytes; the host handles storage and ledger writes. This interface is
not a sandbox for untrusted code running in the worker process.

The shipped plugin retains its stable `http-json` ID, so existing bindings remain recognised.
Existing settings must meet the request validation rules: GET or POST, no URL credentials,
and authentication through a secret reference rather than static Authorization/Cookie headers.
