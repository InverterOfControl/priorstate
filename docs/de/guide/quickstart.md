# Schnellstart

Die Einrichtung soll in etwa zehn Minuten erledigt sein. Dauert sie länger, erstelle bitte
ein [Issue](https://github.com/InverterOfControl/priorstate/issues), damit wir den Einstieg
verbessern können.

## Voraussetzungen {#requirements}

Du brauchst Docker mit Compose.

## Starten {#start-it}

```bash
git clone https://github.com/InverterOfControl/priorstate.git
cd priorstate/deploy
cp .env.example .env
```

Öffne `.env` und setze unterschiedliche Werte für `POSTGRES_PASSWORD` und
`POSTGRES_APP_PASSWORD`. Compose führt Migrationen einmalig mit einem Administratorkonto aus.
API und Worker verwenden das eingeschränkte Laufzeitkonto. Bei einer bestehenden Installation
folge [Datenbankkonten und Updates](/de/operations/database).

Ermittle anschließend die Gruppen-ID des Docker-Sockets. Damit kann der Worker Crawl-Container
starten, ohne als root zu laufen:

```bash
stat -c '%g' /var/run/docker.sock    # put the number in DOCKER_GID
```

Starte die Dienste:

```bash
docker compose up -d
```

Öffne <http://localhost:8080> und lege das erste Benutzerkonto an.

## Den Speicherstatus prüfen {#check-what-you-actually-got}

```bash
curl -s localhost:8080/health
```

Der Speichereintrag meldet das tatsächlich beobachtete Verhalten des Objektspeichers hinsichtlich
Unveränderbarkeit. Beim mitgelieferten Garage meldet er fehlendes Object Lock. Das ist zu erwarten;
siehe [Speicher und WORM](/de/operations/storage).

## Die erste Erfassung {#take-a-first-capture}

1. Lege unter **Projects → create** ein Projekt mit einer Start-URL und einer Aufbewahrungsfrist an.
2. Wähle **Capture now**. Beim ersten Aufruf lädt der Worker `browsertrix-crawler` herunter.
   Dieser Lauf dauert deshalb einige Minuten länger.
3. Die **Timeline** zeigt den Snapshot mit seiner Position in der Kette und seinem Eintrags-Hash.
4. **Ledger → re-derive the whole chain** berechnet jeden Eintrags-Hash aus den aufgezeichneten
   Metadaten neu.

## Vor dem produktiven Einsatz {#before-you-rely-on-it}

Zwei Einstellungen in `.env` sind zum Ausprobieren geeignet, müssen für einen ernsthaften Einsatz
aber geprüft werden. Die Datei weist selbst darauf hin:

- `TSA_URL` verweist standardmäßig auf FreeTSA, einen Demonstrationsdienst. Seine Tokens lassen
  sich korrekt prüfen, er ist aber kein qualifizierter eIDAS-Anbieter. Bestehende Snapshots
  können später nicht bei einem anderen Dienst neu verankert werden.
  Siehe [Zeitstempeldienst](/de/operations/timestamping).
- `STORAGE_SERVICE_URL` verweist standardmäßig auf Garage mit einem einzelnen Knoten. Diese
  Konfiguration bietet weder Object Lock noch Redundanz. Siehe [Speicher und WORM](/de/operations/storage).

Gehe außerdem die [Anforderungen vor dem Start](/de/operations/phase-0-requirements) durch.
Umfang, Häufigkeit und Aufbewahrung lassen sich später festlegen; vor der ersten Erfassung
vermeidet das unnötigen Aufwand.

## Entwicklung {#development}

Führe vor einem lokalen Start von API oder Worker die API mit `--migrate`, administrativen
Datenbankzugangsdaten und `Database__RuntimePassword` aus. Verwende für den normalen Start die
Verbindung als `priorstate_app`. Die Einstellungen stehen unter
[Betrieb außerhalb von Compose](/de/operations/database#running-outside-compose).
Der normale API-Start führt keine Migrationen aus.

```bash
cd deploy && docker compose up -d postgres garage garage-init   # dependencies only
dotnet run --project src/PriorState.Api                          # API on :8080
dotnet run --project src/PriorState.Worker                       # worker
cd src/ui && npm ci && npm run dev                               # UI on :5173, proxying to :8080
cd docs && npm ci && npm run docs:dev                            # these docs
```

`dotnet test` führt alle Tests aus, einschließlich Integrationstests mit einem echten PostgreSQL
über Testcontainers. Docker muss dafür laufen.
