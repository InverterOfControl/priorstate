# Architektur

```
Deploy webhook (GitHub Actions) ──┐
                                  ├──> crawl job (Postgres queue)
Cron schedule ────────────────────┘         │
                                            v
                              browsertrix-crawler container
                                            │
                                    WACZ ──> S3-compatible storage
                                            │    (WORM capability probed,
                                            │     recorded per snapshot)
                                            v
                        SHA-256 ──> hash chain (Postgres, append-only)
                                            │
                                  daily Merkle root
                                            │
                                            v
                              RFC-3161 timestamp authority
```

## Komponenten {#components}

| Projekt | Aufgabe |
|---|---|
| `PriorState.Domain` | Entitäten und Wertobjekte. Hat bewusst keinerlei Abhängigkeiten. |
| `PriorState.Ledger` | Kanonische Darstellung, Hash-Kette, Merkle-Baum und RFC-3161-Client. |
| `PriorState.Storage` | `IObjectStore` über S3 sowie die Prüfung der WORM-Fähigkeiten. |
| `PriorState.Crawler` | Übersetzt ein Erfassungsprofil in browsertrix-Argumente und führt den Container aus. |
| `PriorState.Evidence` | Stellt Beweispakete zusammen, rendert Protokolle und liefert `verify.sh`. |
| `PriorState.Data` | EF-Core-Modell, Migrationen und Durchsetzung der nur anfügbaren Tabellen. |
| `PriorState.Api` | Minimal APIs, Identity und Auslieferung der gebauten Vue-Anwendung. |
| `PriorState.Worker` | Verarbeitung der Warteschlange, Cron-Zeitplanung und tägliche Zeitstempel-Verankerung. |

## Datenmodell {#data-model}

`Project → CaptureProfileVersion → Run → Snapshot → TimestampAnchor` bildet den Erfassungsablauf ab.
`DeploymentLedgerEntry` verknüpft Commits mit Snapshots, `AuditLogEntry` zeichnet Zugriffe auf.

`Snapshot`, `TimestampAnchor`, `AuditLogEntry`, `CaptureProfileVersion` und
`DeploymentLedgerEntry` sind Ledger-Tabellen. SQL-Regeln erlauben dort nur das Anfügen von
Einträgen. `Project`, `Run` und `CrawlJob` bleiben als Betriebsdaten veränderbar, damit sich
beispielsweise Wiederholungsversuche erfassen lassen.

Drei eng begrenzte Ausnahmen erlauben das einmalige Setzen eines Werts. Keine davon geht in
einen Hash ein: `snapshots.TimestampAnchorId`, `capture_profile_versions.SupersededAt` und
`deployment_ledger_entries.RunId`. Jedes Feld darf genau einmal von `NULL` auf einen Wert
wechseln. Der Trigger weist alle anderen Änderungen ab, auch einen zweiten Versuch am selben Feld.

## Entscheidungen für den Betrieb {#choices-worth-knowing-about}

Die Warteschlange liegt in einer Postgres-Tabelle. Worker beanspruchen Jobs mit
`FOR UPDATE SKIP LOCKED`; mehrere Worker können parallel laufen. Warteschlange und Ledger
teilen eine Transaktion. So bleibt die Compose-Datei klein, ohne einen Message Broker einzuführen,
dessen Skalierung für das Archiv einer einzelnen Domain nicht benötigt wird.

Beim Anfügen an die Kette serialisiert `pg_advisory_xact_lock` die Zugriffe. Zwei gleichzeitige
Vorgänge könnten sonst denselben letzten Eintrag lesen, denselben Vorgänger beanspruchen und
die Kette unbemerkt verzweigen. Die Sperre gilt für eine Einfügung. Ein eindeutiger Index auf
die Kettensequenz sichert zusätzlich ab.

Die Verankerung erfolgt täglich. Ein qualifizierter Zeitstempeldienst berechnet Kosten pro
Anfrage; ein Token kann einen ganzen Tag abdecken. Der Merkle-Baum erlaubt für jeden einzelnen
Eintrag einen kurzen Prüfpfad zu diesem Token.

Das PDF-Protokoll entsteht aus HTML mit dem Chromium des Crawlers. Damit ist keine zusätzliche
PDF-Bibliothek oder Lizenz nötig. Prüfer können dasselbe HTML im eigenen Browser öffnen und
mit dem PDF vergleichen.

Die Vue-Anwendung wird in das API-Image eingebaut. Ein Container liefert beide aus.
Die produktive Compose-Konfiguration benötigt dadurch keine Node-Laufzeit, und beim ersten
Start muss ein Dienst weniger eingerichtet werden.
