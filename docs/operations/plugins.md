# Capture plugins

Sometimes the page is not the whole record. A shop page quotes a price; the price also exists in
an ERP behind an API. Archiving only the page leaves the obvious question — what did your own
system say at that moment — unanswerable, and answering it later from a database backup is exactly
the kind of evidence PriorState exists to avoid producing.

A capture plugin closes that gap. It runs as part of a crawl, fetches something the browser cannot
reach, and its output becomes a snapshot in the same chain, under the same daily timestamp, with
its own evidence package.

One plugin ships with PriorState: `http-json`, which reads an HTTP endpoint.

## What a plugin can and cannot do

This is the part worth reading before enabling anything.

A plugin receives its configuration and returns bytes. That is the entire contract. It gets no
database handle, no object store, no filesystem path and no access to the ledger. PriorState hashes
the payload, stores it and appends it, through the same code that appends a page capture.

So a plugin can only ever cause a **new entry to be appended**. It cannot modify an entry, delete
one, or influence how one is hashed. That is not politeness about layering — the project's whole
claim is that recorded history cannot be altered, including by the operator, and a plugin API that
handed out a database connection would be a supported route around that claim.

Plugins are compiled in and registered explicitly. There is no plugin directory and nothing is
loaded from disk at startup, so the answer to "what code produced this evidence" stays "the AGPL
source you can read".

## Bindings are versioned, not edited

A binding is one plugin pointed at one endpoint for one project. Like a
[capture profile](/operations/capture-profiles), it is never edited in place: saving a change
creates a new version and supersedes the previous one, and a snapshot permanently names the version
it ran under. Retiring a binding supersedes it without a successor, so "this stopped running on the
12th" stays visible instead of disappearing.

The database enforces this rather than the application. `UPDATE` and `DELETE` on
`plugin_binding_versions` are rejected by a trigger; the single permitted change is setting
`SupersededAt` once.

This matters for a specific reason. A snapshot's entry hash commits to a digest of the binding
(`binding_digest` in the [canonical form](/reference/canonical-form)). If bindings were mutable,
the endpoint a snapshot claims to have been fetched from would be a row the archive operator could
quietly change, and the obvious objection — *you pointed it somewhere else and re-ran it* — would
have no answer. Because the digest is in the chain and the configuration ships inside the evidence
package, the recipient can check it offline.

## Secrets

A binding refers to a credential by the **name of an environment variable**, never by value:

```
PS_SECRET_ERP_TOKEN=...        # in deploy/.env, on the worker
```

```json
{ "url": "https://erp.internal/api/prices", "authHeaderName": "Authorization",
  "authValuePrefix": "Bearer " }
```

The name is recorded in the ledger, printed on the protocol and shipped in the package. The value
exists only in the worker's process environment. Dumping the database, exporting a package or
reading the ledger cannot leak it, and rotating it does not require a new binding version.

Names must match `PS_SECRET_<NAME>`. That restriction is enforced, not merely documented: without
it, "configure a plugin" would become "read any environment variable this container has", which
includes the database connection string.

The endpoint URL is a different matter. It **is** recorded in the canonical form and is therefore
permanently readable in every evidence package that snapshot produces, so a URL with credentials in
it is rejected. Put the credential in a header.

## Restrict which hosts a plugin may call

::: warning Set an allowlist
The worker container mounts the Docker socket, which the [security policy](https://github.com/InverterOfControl/priorstate/blob/main/SECURITY.md)
already describes as equivalent to host root. With no allowlist, anyone who can edit a project can
make that container issue requests anywhere it can reach — internal services, cloud metadata
endpoints, the lot.
:::

```
PLUGIN_HTTP_ALLOWED_HOST_0=erp.internal
PLUGIN_HTTP_ALLOWED_HOST_1=pim.internal
```

Empty means unrestricted, and the worker logs a warning at startup saying so.

## When a plugin fails

By default, a plugin failure does not fail the run. A page capture cannot be taken again later, and
an ERP being briefly unreachable should not cost you one. The failure is recorded on the run and in
the worker log either way.

Turn on **Failure stops the run** for a binding where a snapshot without the plugin's data would be
misleading. The run then fails and retries with the usual backoff.

Either way, a plugin that failed produces no snapshot. There is no partial or placeholder entry in
the chain, and a response that was not a success is never archived — a 500 page stored as though it
were the data would look like a genuine record of what the API returned.

## What the evidence package contains

A plugin snapshot's package carries the same files as a page capture's, with three differences:

| File | Contents |
|---|---|
| `payload.json` | The archived response, byte for byte as the endpoint sent it. |
| `plugin/binding.txt` | The binding form the entry hash commits to. |
| `plugin/configuration.json` | The configuration itself, verbatim, for reading. |

`verify.sh` gains a fifth check for these packages: it recomputes the binding digest against the
canonical entry, and the configuration digest against the binding. The other four checks are
unchanged.

## What this does not prove

The package proves that these bytes came back from that URL, under that configuration, before the
attested time, and are unaltered since.

It does not prove that what the endpoint returned was **correct**. PriorState attests receipt, not
truth. If your ERP was wrong, the archive faithfully records that it was wrong — which is often the
useful thing, but it is not the same claim, and the protocol says so in as many words.

## Writing another plugin

Implement `ICapturePlugin` from `PriorState.Plugins.Abstractions`, register it with
`AddCapturePlugin<T>()`, and return bytes. Return them exactly as received: reformatting or
re-serialising means the archived hash covers your rendering of the data rather than what the far
end actually sent, which is the thing being attested.

Note that a plugin id is recorded in the canonical form of every snapshot it produces. Renaming one
orphans existing evidence, so choose it once.
