# Kanonische Darstellung

Die kanonische Darstellung ist die genaue Bytefolge, die für das Ledger gehasht wird.
Sie legt die Kompatibilität zwischen PriorState, seinen Beweispaketen und dem von der
Gegenseite ausgeführten `verify.sh` fest.

::: danger Bestehende Formatversionen bleiben unverändert
Ein Feld in einer bestehenden Version zu ändern, umzuordnen oder zu entfernen, würde die
Prüfung bereits exportierter Beweispakete verhindern. Ein neues Feld benötigt eine neue
Versionskennung und einen eigenen Zweig im Renderer. Snapshots werden dauerhaft mit
der Version dargestellt, unter der sie geschrieben wurden.
:::

## Version 1 {#version-1}

Kennung: `priorstate-snapshot-v1`

Kodierung: UTF-8, LF-Zeilenenden, genau ein abschließender Zeilenumbruch, keine Byte Order Mark.

```
priorstate-snapshot-v1
sequence=1
prev=0000000000000000000000000000000000000000000000000000000000000000
url=https://example.com/prices
final_url=
captured_at=2026-09-03T14:30:00Z
wacz_sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
wacz_size=1048576
profile=DE-Standard v1
user_agent=Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36
viewport=1920x1080
authenticated=false
adblock=false
cookie_banner=left_as_is
js_settle_ms=5000
chromium=140.0.7259.68
crawler=1.7.1
```

Der Eintrags-Hash ist `SHA-256` über genau diese Bytes.

## Felder {#fields}

| Feld | Bedeutung |
|---|---|
| `sequence` | Position in der Kette, lückenlos ab 1. Eine Lücke bedeutet Manipulation. |
| `prev` | Eintrags-Hash des Vorgängers; beim ersten Eintrag 64 Nullen. |
| `url` | Bei Browserarchiven die erste Start-URL des Projekts, gemeinsam für alle WACZ-Dateien des Laufs. Keine aus einzelnen Seiten extrahierte URL. |
| `final_url` | Für die endgültige URL reserviert. Der aktuelle Browser-Worker lässt das Feld leer. Das belegt nicht, dass keine Weiterleitung stattfand. |
| `captured_at` | Bei Browserarchiven der vom Betreiber aufgezeichnete Crawl-Beginn, nicht die Abrufzeit jeder Seite. UTC mit Sekundengenauigkeit, immer mit Endung `Z`. |
| `wacz_sha256` | SHA-256 der gespeicherten Archivdatei als Hexadezimalwert in Kleinbuchstaben. |
| `wacz_size` | Größe in Bytes. |
| `profile` | Name und Version des Erfassungsprofils, etwa `DE-Standard v1`. |
| `user_agent` … `crawler` | Die tatsächlichen Bedingungen der Erfassung. |

Die bestehenden Feldnamen und Bytes bleiben aus Kompatibilitätsgründen unverändert.
Der Zeitstempeldienst bescheinigt, dass die festgehaltenen Daten zum Signaturzeitpunkt
existierten. Die Richtigkeit des aufgezeichneten Crawl-Beginns bestätigt er nicht.
Einzelne Seitenaufzeichnungen stehen im WACZ.

## Version 2 {#version-2}

Kennung: `priorstate-snapshot-v2`

Diese Version gilt für Snapshots aus einem [Erfassungsplugin](/de/operations/plugins).
Browsererfassungen verwenden weiterhin dauerhaft Version 1. Die Formatversion steht am
Snapshot; die Einführung von Version 2 hat deshalb keinen bestehenden Eintrags-Hash verändert.

```
priorstate-snapshot-v2
sequence=2
prev=a4c1...
url=https://erp.example.com/api/prices
final_url=
captured_at=2026-09-03T14:30:00Z
payload_sha256=9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08
payload_size=2048
payload_media_type=application/json
profile=DE-Standard v1
plugin=http-json
plugin_version=1.4.2
binding=erp-prices v3
binding_digest=56c946e0e9db65166f4eef0f32f714d0bfe94dd34f0d2e5addb65e7e4b6f41ca
```

| Feld | Bedeutung |
|---|---|
| `url` | Die vom Plugin gemeldete Quell-URL. |
| `captured_at` | Die vom Plugin auf dem System des Betreibers gemeldete Beobachtungszeit. Keine unabhängig gemessene TSA-Zeit. |
| `payload_sha256` | SHA-256 der gespeicherten Antwort als Hexadezimalwert in Kleinbuchstaben. |
| `payload_size` | Größe in Bytes. |
| `payload_media_type` | Von der Quelle gemeldeter Medientyp, etwa `application/json`. |
| `profile` | Das Erfassungsprofil des Laufs. |
| `plugin` | Das abrufende Plugin, etwa `http-json`. |
| `plugin_version` | Version des Plugins, aus der tatsächlich ausgeführten Assembly gelesen. |
| `binding` | Name und Version der verwendeten Konfiguration. |
| `binding_digest` | SHA-256 der unten beschriebenen kanonischen Plugin-Bindung. |

Browserfelder fehlen vollständig. Ein API-Aufruf hat keine Darstellungsfläche, keinen
Browser-User-Agent und keine Chromium-Version; solche Angaben würden den Nachweis verfälschen.

`binding_digest` macht die Konfiguration nachprüfbar. Ohne diesen Hash wäre der behauptete
Endpunkt nur ein veränderbarer Datenbankeintrag auf dem Server des Betreibers. Ein späterer
Wechsel des Endpunkts ließe sich dann nicht anhand des Beweispakets erkennen.

## Darstellung der Plugin-Bindung {#plugin-binding-form}

Kennung: `priorstate-plugin-binding-v1`

Über diese Bytes wird `binding_digest` berechnet. Sie liegen im Beweispaket als
`plugin/binding.txt`, die vollständige Konfiguration als `plugin/configuration.json`.

```
priorstate-plugin-binding-v1
plugin=http-json
name=erp-prices
version=3
secret_ref=PS_SECRET_ERP_TOKEN
required=false
created_at=2026-09-01T08:00:00Z
config_sha256=fbaad759812738f6695a660fa632871778e05b1c95c1e03f2f0e375371e16a3a
```

Die Konfiguration wird über ihren Hash eingebunden, weil ihre Struktur zum Plugin gehört.
Ein später ergänztes Plugin soll keine neue kanonische Formatversion benötigen. Das
Ledger-Format muss dafür beispielsweise keine HTTP-Header abbilden können.

`secret_ref` ist der Name einer Umgebungsvariablen. Ihr Wert wird weder aufgezeichnet
noch in der Datenbank gespeichert oder einem Paket beigelegt.

## Escaping {#escaping}

Innerhalb eines Werts wird `\` zu `\\`, LF zu `\n` und CR zu `\r`. Weitere Zeichen
werden nicht maskiert.

Damit kann ein Wert keinen Zeilenumbruch einfügen und eine zusätzliche Zeile vortäuschen.
Eine URL mit einem Zeilenumbruch und anschließendem `url=https://evil.example/` würde sonst
zwei `url`-Zeilen erzeugen. Ein einfacher Parser könnte dadurch die falsche lesen.

## Warum das Format zeilenorientiert ist {#why-line-oriented-rather-than-json}

Kanonisches JSON nach RFC 8785 löst dasselbe Problem mit einer klaren Spezifikation,
lässt sich aber nur aufwendig in einem Shellskript nachbilden. `verify.sh` soll kurz genug
bleiben, damit ein gerichtlich bestellter Sachverständiger es vor der Ausführung tatsächlich
lesen kann. Die Zeilen lassen sich mit `printf` und `sed` rekonstruieren und leicht prüfen.
Die Lesbarkeit des Prüfskripts bestimmt hier die Formatwahl.

## Nicht enthaltene Angaben {#deliberately-excluded}

Extrahierter Seitentext lässt sich aus dem WACZ reproduzieren und dient Suche und
Änderungsvergleich. Würde er in den Hash eingehen, hinge dieser von der Textextraktion ab.
Ein browsertrix-Update könnte dann ohne Nutzen die Prüfung alter Snapshots beeinträchtigen.

Die Plugin-Konfiguration wird über ihren Hash festgehalten und vollständig neben dem
Eintrag mitgeliefert. Empfänger können sie lesen und den Hash neu berechnen. Eine direkte
Einbettung würde das Datenmodell beliebiger Plugins in ein jahrzehntelang stabiles Format aufnehmen.

WORM-Status und Zeitstempel-Verankerung stehen am Snapshot und im Protokoll, gehen aber
nicht in den Hash ein. Die Verankerung wird erst nach dem Eintrag zugewiesen; sie zu hashen
wäre zirkulär. Der WORM-Status beschreibt eine Beobachtung der Speicherschicht und keine
Eigenschaft der erfassten Inhalte.
