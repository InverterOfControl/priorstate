# Database accounts and upgrades

Compose runs `migrate` once with `POSTGRES_USER` / `POSTGRES_PASSWORD`, applies EF migrations,
seeds the baseline profile, and configures `priorstate_app`. API and worker start only after
that job succeeds and receive only `POSTGRES_APP_PASSWORD`. Choose different random passwords
(for example 32 random hexadecimal bytes, which need no connection-string escaping).

The runtime role can create accounts and update operational state. Ledger tables permit inserts,
reads and the documented set-once fields; triggers enforce the latter. It cannot own tables,
disable triggers, replace protection functions, or truncate history. The schema administrator
and anyone controlling the Docker host can still remove protections. These permissions protect
against application mistakes and direct misuse of runtime database credentials, not host compromise.

## Existing Compose deployments

1. Back up PostgreSQL and stop API and worker: `docker compose stop api worker`.
2. Retain the existing `POSTGRES_USER`, `POSTGRES_PASSWORD` and database volume. Set a distinct
   `POSTGRES_APP_PASSWORD` in `.env`; never set `POSTGRES_USER=priorstate_app`.
3. Build the updated image and run the idempotent job:
   `docker compose build migrate` then `docker compose run --rm migrate`.
4. If it succeeds, run `docker compose up -d --build`. If it fails, fix the reported database
   configuration first. Do not start the old administrator-connected application again.

Repeat the migration job when upgrading or rotating the runtime password, with API and worker
stopped. Changing the administrator password in `.env` alone does not change PostgreSQL's stored
password on an existing volume. The bootstrap refuses runtime role ownership or membership in
other roles: an administrator must first audit and remove these grants or transfer ownership.

## Running outside Compose

Run the API executable with `--migrate`, an administrator `ConnectionStrings__Postgres`, and
`Database__RuntimePassword`. Then launch API and worker without `--migrate`, using a connection
string whose username is `priorstate_app`. Normal API startup no longer applies migrations.
Future migrations adding runtime tables must also update the explicit grants in
`RuntimeDatabaseRole`; new tables are intentionally inaccessible by default.
