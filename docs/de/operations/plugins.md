# Zusätzliche Quellen

Eine API-Quelle archiviert eine HTTP-Antwort neben der Website-Erfassung eines Projekts.
So lässt sich etwa eine Shopseite zusammen mit den Preisen aus ihrer Preis-API aufzeichnen.

## Eine Quelle einrichten {#set-up-a-source}

1. Öffne **Projects → dein Projekt → Additional sources → Add source** und wähle **HTTP endpoint**.
2. Gib einen aussagekräftigen Namen und die absolute HTTP(S)-URL der API ein.
3. Wähle **GET** oder **POST**, wenn die API einen Request-Body zum Lesen von Daten benötigt.
4. Wähle keine Authentifizierung, ein Bearer-Token oder einen eigenen Authentifizierungsheader.
5. Begründe, warum diese Quelle zum Archiv gehört, und speichere sie.
6. Wähle **Test connection** an der gespeicherten Quelle. Der Test verwendet Netzwerk,
   Host-Freigabeliste, Zugangsdaten und Größenlimit des Workers. Bei Erfolg zeigt er
   Antworttyp und Bytezahl.
7. Starte **Capture now** am Projekt, um Website und API-Quellen zu archivieren.

Die Navigationsseite **Sources** bietet dieselben Funktionen mit einer Projektauswahl.
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
`PriorState.Plugins.Abstractions`. Plugins werden als `.psplugin`-Pakete installiert und beim
Start von API und Worker geladen. Sie erhalten Konfiguration und liefern Bytes zurück;
der Host übernimmt Speicherung und Ledger-Einträge. Eigene Ladekontexte trennen Abhängigkeiten,
sind aber keine Sandbox für nicht vertrauenswürdigen Code.

Das mitgelieferte Plugin behält seine ID `http-json`, damit bestehende Bindungen erkannt
werden. Vorhandene Einstellungen müssen die Validierungsregeln erfüllen: GET oder POST,
keine Zugangsdaten in der URL und Authentifizierung über eine Secret-Referenz statt statischer
Authorization- oder Cookie-Header.

## Plugin-Pakete installieren

Die Seite **Zusätzliche Quellen** zeigt die geladenen Paket-Hashes und deine Betreiberidentität.
Übernimm diese exakte Identität in `PLUGIN_OPERATOR_ID` in `deploy/.env` und erstelle den
API-Container neu. Der Wert ist der authentifizierte NameIdentifier-Claim, keine E-Mail-Adresse.
Ohne Freigabe ist der Upload gesperrt. Andere Installationen verwenden `Plugins:OperatorIds`.

Lade ein vertrauenswürdiges `.psplugin`-Paket hoch und starte beide Prozesse neu:

```bash
docker compose restart api worker
```

Bei geänderten Umgebungsvariablen stattdessen `docker compose up -d --force-recreate api worker`
verwenden. Compose bindet das gemeinsame Volume `priorstate-plugins` ein; außerhalb von Compose
muss `Plugins:Directory` auf dasselbe gemeinsame Dateisystem zeigen.

Der Upload prüft Manifest, Vertragsversion, Größen und sichere ZIP-Pfade, ohne Plugin-Code
auszuführen. Erst beim Neustart werden Registrierung und Konfigurationsprüfung in der API sowie
Erfassung im Worker verfügbar. Plugins laufen mit Anwendungsberechtigungen. Die Prüfung ist
keine Sicherheitszertifizierung; inkompatible Assemblies können den Start verhindern.
Es gibt weder Hot-Reload noch einen Signatur-Vertrauensspeicher.

`packages/<sha256>.psplugin` enthält unveränderliche Pakete; `active.json` wählt eines je ID aus.
Ein Paket mit vorhandener ID ist ein Update. Alte Pakete bleiben erhalten, laufende Prozesse
behalten ihren geladenen Stand. Sichere das Volume mit. Für eine Wiederherstellung beide
Prozesse stoppen und die vorherige Paketidentität aus einem Backup in `active.json` einsetzen.
Die Installation wird vor Aktivierung auditiert; ein Aktivierungsfehler kann deshalb einen
Autorisierungseintrag ohne abgeschlossene Installation hinterlassen.

Die Oberfläche meldet ausstehende Neustarts, abweichende API-/Worker-Hashes und fehlende aktuelle
Worker-Meldungen. Der Status unterstützt eine API und einen Worker je Installation.
Neue Plugin-Snapshots verwenden `priorstate-snapshot-v3` mit tatsächlicher Paket-ID und SHA-256.
Bestehende v1-/v2-Einträge bleiben unverändert prüfbar. Zuerst die neue Datenbankmigration ausführen.

## Mitgelieferte Plugins

- **HTTP endpoint** (`http-json`): bestehende GET-/POST-Konfiguration bleibt kompatibel.
- **S3 object** (`s3`): ein Objekt je Quelle, unveränderte Bytes und optional eine feste
  `versionId`. Endpoint, Region, Bucket und Key konfigurieren. `PLUGIN_S3_ALLOWED_HOST` freigeben;
  HTTP erfordert zusätzlich `PLUGIN_S3_ALLOW_HTTP=true`. Das Worker-Secret enthält JSON mit
  `accessKeyId`, `secretAccessKey` und optional `sessionToken`. Archivspeicher-Zugangsdaten
  werden nicht wiederverwendet. Keine lokalen Dateien und keine Bucket-Auflistung.
- **DNS and TLS** (`dns-tls`): A, AAAA, MX, TXT, NS, SOA und CAA samt TTL sowie originale
  DNS-Nachrichten über TCP und Zertifikatsbytes als DER. `PLUGIN_DNS_TLS_ALLOWED_HOST` freigeben.
  Ein expliziter Resolver benötigt `PLUGIN_DNS_ALLOWED_RESOLVER`; sonst wird der Worker-Resolver
  verwendet. Ungültige Zertifikate sind Befunde. DNSSEC und Zertifikatswiderruf werden nicht
  geprüft. Die Zertifikatskette wird vom Worker aufgebaut und kann lokale Zertifikate enthalten.
  TLS verwendet die normale Worker-Namensauflösung unabhängig vom DNS-Beobachtungsresolver.
- **Git repository** (`git`): HTTPS-Branch, -Tag oder -Commit wird zu einer vollständigen Commit-ID
  aufgelöst. Reproduzierbares ZIP mit `manifest.json` und Dateistand unter `files/`.
  `PLUGIN_GIT_ALLOWED_HOST` freigeben. Private Repositories verwenden optional ein PAT-Secret
  und einen nicht geheimen Benutzernamen. Keine Historie, `.git`, Submodul- oder LFS-Downloads.
  Gitlinks, LFS-Zeiger und Symlink-Texte bleiben erhalten; Links werden nicht verfolgt.

Neue Plugins verweigern nicht freigegebene Zielhosts. Weitere Hosts lassen sich über indizierte
Variablen wie `PLUGINS__GIT__ALLOWEDHOSTS__1` ergänzen. Secrets gehören ausschließlich in die
Worker-Umgebung, etwa `deploy/secrets.env`; Quellkonfigurationen werden dauerhaft archiviert.
Verbindungstests führen dieselben Erfassungen aus, speichern aber weder Inhalt noch Ledger-Eintrag.

## Eigene Plugins paketieren

`ICapturePluginModule.Register(IServiceCollection, IConfiguration)` registriert genau ein
Singleton-`ICapturePlugin` aus der Einstiegspunkt-Assembly. `Definition` beschreibt Felder
(`string`, `number`, `boolean`, `multiline`) und Secret-Hinweise; `ValidateConfiguration` prüft
öffentliche Einstellungen und meldet Fehler über `PluginException`. Worker-Secrets dürfen
bei der API-Konfigurationsprüfung nicht aufgelöst werden.

Das Output-Verzeichnis enthält Assemblies, Laufzeitabhängigkeiten und `plugin.json`:

```json
{
  "formatVersion": 1,
  "contractVersion": 1,
  "id": "my-source",
  "entryAssembly": "MySource.dll",
  "entryType": "MySource.PluginModule"
}
```

Manifest nach Output/Publish kopieren und `CopyLocalLockFileAssemblies` aktivieren. Die stabile
kleingeschriebene ID muss `ICapturePlugin.Id` entsprechen. Abstraktions-, Domain- und
Microsoft.Extensions-Verträge werden mit dem Host geteilt; andere Abhängigkeiten erhalten
einen eigenen Ladekontext. Der Host verwendet .NET 10 und Vertragsversion 1.

```bash
dotnet publish MySource.csproj -c Release -o ./plugin-output
dotnet run --project src/PriorState.PluginPackager -- ./packages ./plugin-output
```

Der Packager erzeugt deterministische ZIPs; API-/Worker-Builds paketieren die vier mitgelieferten
Plugins automatisch. Beobachtungsformate und Archive müssen ihre Formatversion dokumentieren
und Originalbytes von berechneten Metadaten unterscheiden. Weitere Details stehen in der
[englischen Betriebsanleitung](/operations/plugins#building-a-plugin-package).
