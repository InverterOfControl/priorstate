# Zusätzliche API-Quellen

Eine API-Quelle archiviert eine HTTP-Antwort neben der Website-Erfassung eines Projekts.
So lässt sich etwa eine Shopseite zusammen mit den Preisen aus ihrer Preis-API aufzeichnen.

## Eine Quelle einrichten {#set-up-a-source}

1. Öffne **Projects → dein Projekt → Additional API sources → Add API source**.
2. Gib einen aussagekräftigen Namen und die absolute HTTP(S)-URL der API ein.
3. Wähle **GET** oder **POST**, wenn die API einen Request-Body zum Lesen von Daten benötigt.
4. Wähle keine Authentifizierung, ein Bearer-Token oder einen eigenen Authentifizierungsheader.
5. Begründe, warum diese Quelle zum Archiv gehört, und speichere sie.
6. Wähle **Test connection** an der gespeicherten Quelle. Der Test verwendet Netzwerk,
   Host-Freigabeliste, Zugangsdaten und Größenlimit des Workers. Bei Erfolg zeigt er
   Antworttyp und Bytezahl.
7. Starte **Capture now** am Projekt, um Website und API-Quellen zu archivieren.

Die Navigationsseite **API sources** bietet dieselben Funktionen mit einer Projektauswahl.
**Change settings** legt eine neue Version an. Der Quellname bezeichnet die Versionshistorie.
Das Stilllegen einer Quelle beendet künftige Erfassungen, ohne frühere Einstellungen oder
Nachweise zu löschen.

Verbindungstests senden echte Anfragen einschließlich POST-Bodies. Verwende API-Operationen,
die zum Lesen von Daten gedacht sind. Der HTTP-Client wiederholt Anfragen nicht automatisch.
Tests speichern nur Betriebsmetadaten. Antwortinhalt, Objektspeicher und Ledger bleiben dabei
unberührt. Wartet ein Test länger als zwei Minuten, ist meist der Worker nicht verfügbar.
Stelle ihn wieder her und teste erneut.

## Authentifizierung {#authentication}

Zugangsdaten werden über ihren Namen referenziert und ausschließlich vom Worker aufgelöst.
Für eine neue Quelle:

```bash
# deploy/secrets.env (copy secrets.env.example first)
PS_SECRET_PRICES_TOKEN=your-token
PS_SECRET_INVENTORY_KEY=your-other-key
```

Wähle im Formular **Bearer token** und gib `PS_SECRET_PRICES_TOKEN` als Namen der Zugangsdaten
an. Für einen API-Schlüssel wähle **API key / custom header**, setze beispielsweise `X-API-Key`
als Header und verwende `PS_SECRET_INVENTORY_KEY`. Ein optionales Präfix muss nötige
abschließende Leerzeichen enthalten.

Compose lädt `secrets.env` nur in den Worker. Übernimm Änderungen aus `deploy/` mit
`docker compose up -d --force-recreate worker`. Ein bloßer Neustart lädt die Umgebung eines
bestehenden Containers nicht neu. Die Namen müssen `PS_SECRET_[A-Z0-9_]+` entsprechen.

Die bisherige Einstellung `PS_SECRET_ERP_TOKEN` bleibt zur Kompatibilität in `deploy/.env`
und hat Vorrang vor demselben Namen in `secrets.env`. Verwende für weitere Zugangsdaten
eigene Namen.

Trage niemals Zugangsdaten in die Quell-URL, den Body oder nicht geheime Header ein.
Diese Konfigurationswerte werden dauerhaft gespeichert, durch den Ledger-Hash festgehalten
und in Beweispakete aufgenommen. Der Name eines Secrets wird aufgezeichnet; sein Wert
gelangt weder in die Konfiguration noch in die Anfrage-Diagnose. Die API selbst kann
vertrauliche Daten zurückliefern. Prüfe deshalb auch den Inhalt ihrer Antworten.

## Anfrageeinstellungen und Host-Zugriff {#request-settings-and-host-access}

Unter **Additional request settings** stehen Accept-Header, Content-Type der Anfrage und
nicht geheime Header. Header müssen ein JSON-Objekt mit Zeichenketten als Werten sein:

```json
{ "X-Tenant": "shop-a" }
```

Konfiguriere die erlaubten Hosts des Workers in `deploy/.env`:

```bash
PLUGIN_HTTP_ALLOWED_HOST_0=api.example.com
PLUGIN_HTTP_ALLOWED_HOST_1=inventory.internal
```

Leere Einträge werden ignoriert. Ohne ausgefüllte Einträge sind alle vom Worker erreichbaren
HTTP(S)-Hosts erlaubt. Begrenze diese Liste für deine Installation, insbesondere bei Zugriff
auf interne Dienste. Weiterleitungen werden nicht verfolgt. Gib den endgültigen Endpunkt an,
damit ein Redirect weder die Host-Liste umgehen noch einen Authentifizierungsheader an einen
anderen Host weiterreichen kann.

Für `https://jsonplaceholder.typicode.com/posts/1` ergänze beispielsweise
`PLUGIN_HTTP_ALLOWED_HOST_0=jsonplaceholder.typicode.com` in `deploy/.env`. Ist der Index
bereits belegt, verwende einen freien und behalte vorhandene Hosts bei. Erzeuge den Worker mit
`docker compose up -d --force-recreate worker` neu. Die Freigabeliste enthält nur den Hostnamen,
ohne `https://` und Pfad. Speichere die Quelle als GET ohne Authentifizierung und teste sie.

Lehnt ein älterer Worker trotz leerer Freigabeliste alle Hosts ab, baue und migriere die
aktualisierte Installation wie unten beschrieben. Ältere Versionen interpretierten leere
Compose-Einträge als einschränkende Liste. Eine Logmeldung über einen abgeschlossenen Lauf
mit einem Snapshot belegt keinen erfolgreichen API-Abruf: Der Snapshot kann allein das
Website-Archiv sein. Die aktualisierte Runs-Seite zeigt Quellfehler und unterscheidet Teilerfolge.

Die maximale Antwortgröße beträgt standardmäßig 32 MiB und lässt sich über
`PLUGIN_MAX_PAYLOAD_BYTES` ändern. Das Anfrage-Timeout beträgt standardmäßig 30 Sekunden
und umfasst Header und Body. HTTP-Antworten ohne Erfolgsstatus, leere Bodies und ungültiges,
als JSON angekündigtes JSON werden als Fehler gemeldet und nicht als erfolgreiche Erfassung
gespeichert.

## Erfassungszeitpunkt und Ergebnisse {#capture-timing-and-results}

Die Verarbeitung der API-Quellen startet parallel zum Website-Crawler. Innerhalb dieser
Verarbeitung werden die Quellen nacheinander abgerufen. Website und APIs bilden deshalb
nicht zwingend denselben Zeitpunkt ab. Jeder API-Snapshot hält fest, wann die Antwort
vollständig empfangen wurde, noch vor dem Schreiben in den Objektspeicher. Die verwendeten
Quellversionen werden beim ersten Start des Laufs ausgewählt und gelten auch für Crawl-Wiederholungen.

Unter **Runs → Website and API results** zeigt jede Quelle ihre Version, ihren Status,
Anfragebeginn, gegebenenfalls einen Fehler und den Link zur archivierten Antwort.
Der Antwort-Snapshot enthält Erfassungszeit, Hashes und Beweisexport. Erfolgreiche
Website-Archive sind in derselben Übersicht verlinkt.

- Scheitert eine optionale API bei erfolgreicher Website-Erfassung, lautet der Status
  **Partially succeeded**.
- Scheitert eine erforderliche API, wird der Lauf als **Failed** markiert. Erfolgreiche
  Erfassungen bleiben erhalten. Allein dieser API-Fehler löst keinen erneuten Website-Crawl
  aus. Starte für einen neuen Versuch einen neuen Lauf.
- Ein Browserfehler bricht die API-Verarbeitung nicht ab. Erfolgreiche API-Antworten bleiben
  archiviert. Die bisherigen Regeln für Browser-Wiederholungen gelten weiter; bereits
  archivierte Quellen werden im selben Lauf nicht erneut abgerufen. Website-Wiederholungen
  verwenden eigene Speicherschlüssel.
- Ältere Läufe enthalten unter Umständen nur die bisherige Fehlerliste und Snapshots,
  ohne strukturierte Quellergebnisse. Beide bleiben lesbar. Bestehende Ledger-Einträge
  und kanonische Formate bleiben unverändert.

Quellausführungen und Testergebnisse sind veränderbare Betriebsdaten. Der Snapshot einer
erfolgreichen Antwort ist dagegen der unveränderbare, mit der Hash-Kette verknüpfte Eintrag.
Testergebnisse sind keine Beweismittel.

## Speicher und Beweispaket {#storage-and-evidence}

Jede erfolgreiche Antwort wird Byte für Byte als eigener Snapshot im selben Lauf und globalen
Ledger wie die Website gespeichert. Ihre Konfiguration wird intern als benannte, versionierte
Plugin-Bindung geführt. Der Eintrag hält sowohl den Nutzdaten-Hash als auch den Bindungs-Hash fest.

Das [Beweispaket](/de/guide/evidence-package) enthält die Originalantwort, bei JSON als
`payload.json`, außerdem `plugin/binding.txt`, `plugin/configuration.json`, Zeitstempel und
Merkle-Nachweis. Die Prüfung kontrolliert auch die Hashes von Bindung und Konfiguration.
Die externe Verankerung erfolgt in Chargen; sie macht API- und Website-Erfassung nicht gleichzeitig.

Bei unabhängig vertrauenswürdigen TSA-Wurzelzertifikaten belegt die Prüfung, dass die
gehashten Bytes und Metadaten spätestens zum signierten Zeitpunkt existierten. Quell-URL,
inhaltliche Wahrheit der Antwort und Genauigkeit der aufgezeichneten Erfassungszeit werden
nicht unabhängig bestätigt.

## Updates und weitere Plugins {#upgrading-and-adding-plugins}

Das Update ergänzt die Betriebstabelle `source_executions`. Führe den einmaligen
Migrationsdienst vor dem Start der aktualisierten API und des Workers aus;
siehe [Datenbank-Updates](/de/operations/database). Die optionale Datei `secrets.env` benötigt
Docker Compose ab Version 2.24. Siehe die
[Docker-Dokumentation zu Umgebungsdateien](https://docs.docker.com/compose/how-tos/environment-variables/set-environment-variables/#additional-information-1).

Die zugrunde liegende Schnittstelle ist weiterhin `ICapturePlugin` aus
`PriorState.Plugins.Abstractions`. Plugins werden einkompiliert und explizit über
`AddCapturePlugin<T>()` registriert. Sie erhalten Konfiguration und liefern Bytes zurück;
der Host übernimmt Speicherung und Ledger-Einträge. Diese Schnittstelle ist keine Sandbox
für nicht vertrauenswürdigen Code im Worker-Prozess.

Das mitgelieferte Plugin behält seine ID `http-json`, damit bestehende Bindungen erkannt
werden. Vorhandene Einstellungen müssen die Validierungsregeln erfüllen: GET oder POST,
keine Zugangsdaten in der URL und Authentifizierung über eine Secret-Referenz statt statischer
Authorization- oder Cookie-Header.
