# Configuration

Configuration comes from `appsettings.json`, overridden by environment variables. In the bundled
compose file everything is set from `deploy/.env`.

Environment variable names use the ASP.NET Core convention: section and key joined by a double
underscore, e.g. `Storage:ServiceUrl` becomes `STORAGE__SERVICEURL`.

## Storage

| Key | Default | Notes |
|---|---|---|
| `Storage:ServiceUrl` | `http://garage:3900` | Any S3-compatible endpoint. |
| `Storage:Region` | `garage` | |
| `Storage:Bucket` | `priorstate` | |
| `Storage:AccessKey` | — | Required. |
| `Storage:SecretKey` | — | Required. |
| `Storage:ForcePathStyle` | `true` | Required by Garage, MinIO, Ceph RGW. |
| `Storage:UseObjectLock` | `true` | Attempt Object Lock retention where supported. |
| `Storage:ProbeWormEnforcement` | `true` | Write-then-delete probe at startup. |

Turning `ProbeWormEnforcement` off downgrades a backend that would report `Enforced` to
`ApiPresentUnverified`, because an unverified claim of immutability is not one PriorState will
print on a protocol. See [Storage and WORM](/operations/storage).

## Timestamp authority

| Key | Default | Notes |
|---|---|---|
| `Ledger:Tsa:Url` | `https://freetsa.org/tsr` | **Demonstration only.** |
| `Ledger:Tsa:Qualified` | `false` | Your assertion that this is a qualified eIDAS provider. |
| `Ledger:Tsa:DisplayName` | `FreeTSA (demonstration only)` | Reproduced in evidence packages. |
| `Ledger:Tsa:RequestedPolicyOid` | — | Optional TSA policy to request. |
| `Ledger:Tsa:RequestSignerCertificate` | `true` | Embed the signing certificate in the token. |
| `Ledger:Tsa:Timeout` | `00:00:30` | |

Read [Timestamp authority](/operations/timestamping) before changing these. The choice cannot be
applied retroactively.

## Crawler

| Key | Default | Notes |
|---|---|---|
| `Crawler:Image` | `webrecorder/browsertrix-crawler:1.7.1` | Pin it. Versions are recorded on every snapshot. |
| `Crawler:WorkDirectory` | `/var/lib/priorstate/crawls` | Path inside the worker container. |
| `Crawler:HostWorkDirectory` | `/var/lib/priorstate/crawls` | The same directory as the **host** sees it — the crawl container is started by the host daemon. |
| `Crawler:Workers` | `2` | Browser workers per crawl. |
| `Crawler:PageLimit` | `500` | |
| `Crawler:DelayBetweenPagesSeconds` | `1` | Be polite. |
| `Crawler:Timeout` | `02:00:00` | |
| `Crawler:DockerEndpoint` | `unix:///var/run/docker.sock` | |

`HostWorkDirectory` is the setting people get wrong. The worker asks the host's Docker daemon to
start a crawl container with a bind mount, so the path in that request has to make sense to the
host, not to the worker.

## Evidence

| Key | Default | Notes |
|---|---|---|
| `Evidence:ToolVersion` | `0.1.0-dev` | Printed on every protocol. |
| `Evidence:CaChainPemPath` | — | TSA certificate chain, shipped in every package for offline verification. |
| `Evidence:ProtocolTemplatePath` | — | Override the built-in German template. |
| `Evidence:RendererImage` | the crawler image | Chromium used for HTML → PDF. |

## Capture plugins

See [Capture plugins](/operations/plugins).

| Key | Default | Notes |
|---|---|---|
| `Plugins:HttpJson:AllowedHosts` | empty | Hosts a binding may call. **Empty means any host.** Set it: the worker holds the Docker socket, so an unrestricted fetch is an escalation path, not a convenience. |
| `Plugins:HttpJson:MaxPayloadBytes` | `33554432` | Largest response archived. Payloads are buffered in memory to be hashed. |
| `Plugins:HttpJson:Timeout` | `00:00:30` | Per request. |

Credentials are not configured here. A binding names an environment variable, which must be called
`PS_SECRET_<NAME>`; only the name is stored and printed, never the value.

## Authentication

| Key | Default |
|---|---|
| `Authentication:Oidc:Enabled` | `false` |
| `Authentication:Oidc:Authority` | — |
| `Authentication:Oidc:ClientId` | — |
| `Authentication:Oidc:ClientSecret` | — |

Local accounts work with no configuration. Enable OIDC alongside them if you already run an
identity provider; both paths produce a real identity for the audit log.

## Webhooks

| Key | Default | Notes |
|---|---|---|
| `Webhooks:DeploymentToken` | — | Shared secret for the deployment webhook. Empty disables it. |

```yaml
# .github/workflows/deploy.yml, after a successful deploy
- name: Record deployment in PriorState
  run: |
    curl -fsS -X POST "$PRIORSTATE_URL/api/webhooks/deployment" \
      -H "X-PriorState-Token: ${{ secrets.PRIORSTATE_TOKEN }}" \
      -H 'Content-Type: application/json' \
      -d '{
            "projectId": "${{ vars.PRIORSTATE_PROJECT_ID }}",
            "commitSha": "${{ github.sha }}",
            "environment": "production",
            "deployedAtUtc": "'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'",
            "source": "github-actions"
          }'
```

This queues a capture and writes the deployment ledger entry linking the commit to the snapshot it
produced.

## Database

`ConnectionStrings:Postgres`. Migrations are applied automatically at API startup, along with the
seeded `DE-Standard v1` capture profile.
