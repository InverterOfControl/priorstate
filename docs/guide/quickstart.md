# Quickstart

Ten minutes, one command. If it takes longer than that, please
[open an issue](https://github.com/InverterOfControl/priorstate/issues) — the setup being fast is a
feature, not a nicety.

## Requirements

Docker with Compose. Nothing else.

## Start it

```bash
git clone https://github.com/InverterOfControl/priorstate.git
cd priorstate/deploy
cp .env.example .env
```

Open `.env` and change `POSTGRES_PASSWORD`. Then find the group id of your Docker socket, so the
worker can start crawl containers without running as root:

```bash
stat -c '%g' /var/run/docker.sock    # put the number in DOCKER_GID
```

Then:

```bash
docker compose up -d
```

Open <http://localhost:8080> and create the first account.

## Check what you actually got

```bash
curl -s localhost:8080/health
```

The storage entry reports what your object store was *observed* to do about immutability, not what
it advertises. With the bundled Garage it will say there is no Object Lock — that is expected and
correct; see [Storage and WORM](/operations/storage).

## Take a first capture

1. **Projects → create** a project with one seed URL and a retention period.
2. **Capture now.** The worker pulls `browsertrix-crawler` on first use, so the first run takes a
   few minutes longer than later ones.
3. **Timeline** shows the snapshot with its chain position and entry hash.
4. **Ledger → re-derive the whole chain** recomputes every entry hash from its recorded metadata.

## Before you rely on it

Two settings in `.env` are fine for evaluation and not fine for real use. Both are called out in
the file itself:

- **`TSA_URL`** defaults to FreeTSA, a demonstration service. Its tokens verify correctly but it
  is not a qualified eIDAS provider, and **snapshots cannot be re-anchored to a different
  authority later**. See [Timestamp authority](/operations/timestamping).
- **`STORAGE_SERVICE_URL`** defaults to the bundled single-node Garage, which has neither Object
  Lock nor redundancy. See [Storage and WORM](/operations/storage).

Also work through [Phase 0 requirements](/operations/phase-0-requirements) before capturing in
earnest. Deciding scope, frequency and retention afterwards is possible; deciding them first is
much cheaper.

## Development

```bash
cd deploy && docker compose up -d postgres garage garage-init   # dependencies only
dotnet run --project src/PriorState.Api                          # API on :8080
dotnet run --project src/PriorState.Worker                       # worker
cd src/ui && npm ci && npm run dev                               # UI on :5173, proxying to :8080
cd docs && npm ci && npm run docs:dev                            # these docs
```

`dotnet test` runs everything, including integration tests that start a real PostgreSQL through
Testcontainers, so Docker needs to be running.
