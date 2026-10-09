# Konfiguration

Die Konfiguration stammt aus `appsettings.json`. Umgebungsvariablen überschreiben diese Werte.
In der mitgelieferten Compose-Datei werden die Einstellungen aus `deploy/.env` übernommen.

Namen von Umgebungsvariablen folgen der ASP.NET-Core-Konvention: Abschnitt und Schlüssel
werden durch zwei Unterstriche verbunden. Aus `Storage:ServiceUrl` wird beispielsweise
`STORAGE__SERVICEURL`.

## Speicher {#storage}

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Storage:ServiceUrl` | `http://garage:3900` | Beliebiger S3-kompatibler Endpunkt. |
| `Storage:Region` | `garage` | |
| `Storage:Bucket` | `priorstate` | |
| `Storage:AccessKey` | Nicht gesetzt | Erforderlich. |
| `Storage:SecretKey` | Nicht gesetzt | Erforderlich. |
| `Storage:ForcePathStyle` | `true` | Für Garage, MinIO und Ceph RGW erforderlich. |
| `Storage:UseObjectLock` | `true` | Object-Lock-Aufbewahrung anfordern, sofern unterstützt. |
| `Storage:ProbeWormEnforcement` | `true` | Schreib- und Löschprüfung beim Start. |

Wird `ProbeWormEnforcement` deaktiviert, meldet ein ansonsten als `Enforced` erkannter
Speicher nur `ApiPresentUnverified`. Eine ungeprüfte Zusage der Unveränderbarkeit wird
nicht als bestätigter Schutz im Protokoll ausgegeben. Siehe [Speicher und WORM](/de/operations/storage).

## Zeitstempeldienst {#timestamp-authority}

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Ledger:Tsa:Url` | `https://freetsa.org/tsr` | Nur zur Demonstration. |
| `Ledger:Tsa:Qualified` | `false` | Angabe des Betreibers, dass der Dienst ein qualifizierter eIDAS-Anbieter ist. |
| `Ledger:Tsa:DisplayName` | `FreeTSA (demonstration only)` | Wird in Beweispaketen ausgegeben. |
| `Ledger:Tsa:RequestedPolicyOid` | Nicht gesetzt | Optional angeforderte TSA-Richtlinie. |
| `Ledger:Tsa:RequestSignerCertificate` | `true` | Signaturzertifikat in das Token aufnehmen. |
| `Ledger:Tsa:Timeout` | `00:00:30` | |

Lies vor einer Änderung [Zeitstempeldienst](/de/operations/timestamping).
Die Wahl lässt sich nicht rückwirkend anwenden.

## Crawler {#crawler}

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Crawler:Image` | `webrecorder/browsertrix-crawler:1.7.1` | Version festlegen. Sie wird an jedem Snapshot aufgezeichnet. |
| `Crawler:WorkDirectory` | `/var/lib/priorstate/crawls` | Pfad im Worker-Container. |
| `Crawler:HostWorkDirectory` | `/var/lib/priorstate/crawls` | Derselbe Ordner aus Sicht des Hosts. Dessen Docker-Daemon startet den Crawl-Container. |
| `Crawler:Workers` | `2` | Browser-Worker pro Crawl. |
| `Crawler:PageLimit` | `500` | |
| `Crawler:DelayBetweenPagesSeconds` | `1` | Rücksicht auf den Zielserver nehmen. |
| `Crawler:Timeout` | `02:00:00` | |
| `Crawler:DockerEndpoint` | `unix:///var/run/docker.sock` | |

Bei `HostWorkDirectory` muss der Pfad auf dem Host gelten. Der Worker beauftragt dessen
Docker-Daemon, einen Crawl-Container mit Bind-Mount zu starten. Ein nur im Worker gültiger
Pfad genügt dafür nicht.

## Beweispaket {#evidence}

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Evidence:ToolVersion` | `0.1.0-dev` | Wird in jedem Protokoll ausgegeben. |
| `Evidence:CaChainPemPath` | Nicht gesetzt | TSA-Zertifikatskette, die jedem Paket zur Offline-Prüfung beiliegt. |
| `Evidence:ProtocolTemplatePath` | Nicht gesetzt | Ersetzt die eingebaute deutsche Vorlage. |
| `Evidence:RendererImage` | Das Crawler-Image | Chromium für HTML → PDF. |

## Erfassungsplugins {#capture-plugins}

Siehe [Zusätzliche API-Quellen](/de/operations/plugins).

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Plugins:HttpJson:AllowedHosts` | Leer | Hosts, die eine Bindung abrufen darf. Leer erlaubt jeden Host. Begrenze die Liste: Der Worker hat Zugriff auf den Docker-Socket; uneingeschränkte Abrufe können Rechteausweitung ermöglichen. |
| `Plugins:HttpJson:MaxPayloadBytes` | `33554432` | Größte archivierte Antwort. Nutzdaten werden zum Hashen im Arbeitsspeicher gepuffert. |
| `Plugins:HttpJson:Timeout` | `00:00:30` | Pro Anfrage. |

Zugangsdaten werden hier nicht gesetzt. Eine Bindung nennt eine Umgebungsvariable mit
dem Muster `PS_SECRET_<NAME>`. Nur deren Name wird gespeichert und ausgegeben, nie ihr Wert.

## Authentifizierung {#authentication}

| Schlüssel | Standard |
|---|---|
| `Authentication:Oidc:Enabled` | `false` |
| `Authentication:Oidc:Authority` | Nicht gesetzt |
| `Authentication:Oidc:ClientId` | Nicht gesetzt |
| `Authentication:Oidc:ClientSecret` | Nicht gesetzt |

Lokale Konten funktionieren ohne weitere Konfiguration. Ist bereits ein Identitätsanbieter
vorhanden, lässt sich zusätzlich OIDC aktivieren. Beide Wege liefern eine Benutzeridentität
für das Audit-Log.

## Webhooks {#webhooks}

| Schlüssel | Standard | Hinweis |
|---|---|---|
| `Webhooks:DeploymentToken` | Nicht gesetzt | Gemeinsames Secret für den Deployment-Webhook. Leer deaktiviert ihn. |

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

Der Aufruf reiht eine Erfassung ein und schreibt einen Deployment-Ledger-Eintrag,
der den Commit mit dem erzeugten Snapshot verknüpft.

## Datenbank {#database}

`ConnectionStrings:Postgres` legt die Datenbankverbindung fest. Migrationen und das
Ausgangsprofil `DE-Standard v1` werden durch den einmaligen Start mit `--migrate` eingerichtet.
Der normale API-Start führt keine Migrationen aus. Siehe [Datenbankkonten und Updates](/de/operations/database).
