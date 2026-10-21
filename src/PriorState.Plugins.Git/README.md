# Git capture plugin

Plugin ID `git`. Host builds package it automatically; custom builds can be published and
packaged with PriorState.PluginPackager. Install `.psplugin` through the sources page and
restart API and worker. The worker needs a trusted `git` executable (included in the image).

Binding example:

```json
{"url":"https://git.example.com/team/repo.git","reference":"main","username":"git"}
```

An optional `PS_SECRET_...` worker reference supplies an HTTPS personal access token. Configure
the username required by your provider; it is public configuration, not the token. No SSH,
credential helpers, interactive prompts, redirects, local Git configuration or hooks are used.
The token is supplied through a temporary Askpass helper's process environment, never command
arguments or archive metadata. Process output is bounded and failure messages are sanitized.

The generated ZIP preserves original blob bytes under `files/` and records the resolved full
commit ID, reference, paths, modes and object IDs in `manifest.json` (formatVersion 1). Entries
are sorted and timestamps fixed. Symlinks are their link text; submodule gitlinks are manifest
references. Neither submodule nor LFS contents are fetched; original LFS pointers remain.
There is no repository history or checkout. Filenames must be valid UTF-8 and safe ZIP paths.

`Plugins:Git:AllowedHosts` must allow the HTTPS endpoint; blank entries grant no access.
Defaults: `Timeout` 60 seconds, `MaxPayloadBytes` 32 MiB, `MaxTemporaryBytes` 128 MiB,
`MaxFiles` 10,000, `GitExecutable` `git`. The temporary directory size is checked every 25 ms
during subprocess execution and after each exit; this is a monitored limit, not an OS quota,
so short-lived overshoots are possible. Cancellation kills the process tree. Cleanup failures
are logged without replacing the captured result; operators should monitor worker temporary storage.
