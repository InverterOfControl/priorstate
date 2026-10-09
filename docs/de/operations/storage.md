# Speicher und WORM

## Überblick {#the-short-version}

PriorState verwendet einen konfigurierbaren S3-kompatiblen Speicher. Docker Compose liefert
Garage für lokale Entwicklung, Tests und Evaluation mit, weil es leichtgewichtig und einfach
zu betreiben ist. Im produktiven Einsatz lassen sich AWS S3 oder andere kompatible Anbieter
ohne Änderungen am Anwendungscode verwenden.

Garage implementiert kein S3 Object Lock. Die mitgelieferte Konfiguration hat deshalb kein
WORM auf Speicherebene. Sie wird unterstützt; vor dem Einsatz solltest du ihre Auswirkungen
auf den Nachweis kennen.

## Warum der ursprüngliche Entwurf geändert wurde {#why-the-original-design-changed}

Ein Beweisarchiv könnte WACZ-Dateien mit Object Lock im COMPLIANCE-Modus in einen S3-Bucket
schreiben. Bis zum gesetzten Aufbewahrungsende könnte dann niemand die Dateien löschen,
auch der Betreiber nicht.

Das setzt eine selbst betreibbare S3-Implementierung voraus, die Object Lock durchsetzt.
Mit Stand September 2026 ergibt sich folgendes Bild:

| Backend | Object Lock | Status |
|---|---|---|
| MinIO | Ja | Community-Ausgabe nicht mehr gepflegt; Repository im Februar 2026 archiviert. Keine geeignete Grundlage für einen Neuaufbau. |
| Garage | Nein | [Issue #1127](https://git.deuxfleurs.fr/Deuxfleurs/garage/issues/1127) wartet auf Versionierungsunterstützung. Der Branch `feat/s3-versioning-object-lock` ist nicht integriert; ein Ziel-Release fehlt. |
| SeaweedFS | API vorhanden | Version 3.94 ergänzte Versionierung, Object Lock mit GOVERNANCE/COMPLIANCE und Legal Hold. Offene Berichte ([#8350](https://github.com/seaweedfs/seaweedfs/issues/8350), [#7194](https://github.com/seaweedfs/seaweedfs/issues/7194)) melden, dass COMPLIANCE Löschungen nicht zuverlässig verhindert. |
| RustFS | Nicht angegeben | Alpha; verteilter Betrieb noch nicht veröffentlicht. |
| Ceph RGW | Ja | Tatsächlich durchgesetzt, aber erheblicher Betriebsaufwand für ein Archiv einer einzelnen Website. |
| AWS S3, Backblaze B2, Wasabi, Scaleway | Ja | Durchgesetzt, gehostet. |

Eine angekündigte WORM-Funktion ohne tatsächliche Durchsetzung beschädigt die Glaubwürdigkeit
des Archivs, wenn sie im Streitfall auffällt.

## Wie PriorState die Unverändertheit nachweist {#what-priorstate-does-instead}

Der Nachweis beruht auf der Hash-Kette und den externen RFC-3161-Zeitstempeln. Beide sind
unabhängig vom Speicher. Das Token belegt, dass ein bestimmter Eintrags-Hash vor dem
bescheinigten Zeitpunkt existierte. Kanonische Darstellung und Kette lassen Änderungen am
Eintrag erkennen. Wird der gesamte Bucket gelöscht, sind die Archive verloren; aufgezeichnete
Inhalte lassen sich aber nicht unbemerkt umschreiben.

WORM auf Speicherebene ist ein zusätzlicher Schutz, den PriorState beim Start prüft:

1. Es liest die Object-Lock-Konfiguration des Buckets. Fehlt sie, lautet das Ergebnis `Unsupported`.
2. Es schreibt ein kleines Testobjekt mit COMPLIANCE-Aufbewahrung bis eine Minute in die Zukunft.
3. Es erfasst die Versions-ID und liest die COMPLIANCE-Aufbewahrung genau dieser Version zurück.
4. Es lädt ein ungeschütztes Kontrollobjekt hoch und löscht dessen genaue Version, um die
   Löschberechtigungen zu prüfen.
5. Es versucht, die geschützte Version über ihre ID zu löschen.
   - Nach erfolgreichen Vorprüfungen ergibt `AccessDenied` (HTTP 403) den Status `Enforced`.
   - Erfolgreiches Löschen, fehlende Versions-IDs, abweichende Aufbewahrungsangaben oder andere
     Prüffehler ergeben `ApiPresentUnverified`.

Ein allgemeiner Berechtigungsfehler beweist keine durchgesetzte Aufbewahrung. Das Kontrollobjekt
prüft gewöhnliches Löschen einer Version, kann aber nicht jede objektspezifische IAM-Regel
ausschließen. Verwende für beide Testobjekte gleichwertige Berechtigungen. Auch eine
standardmäßige Bucket-Aufbewahrung kann die Kontrollversion schützen; das Ergebnis bleibt dann
`ApiPresentUnverified`. Geschützte Testobjekte müssen nach Fristablauf gesondert aufgeräumt
werden. Der Fristablauf löscht sie nicht automatisch.

Das Ergebnis wird an jedem Snapshot gespeichert, in der Oberfläche und unter `/health`
angezeigt sowie in jedem Beweisprotokoll ausgegeben. Das Protokoll behauptet keinen Schutz,
der nicht angewandt wurde.

## Ein Backend wählen {#choosing-a-backend}

Für die Evaluation oder ein kleines internes Archiv reicht das mitgelieferte Garage aus.
Die Konfiguration mit einem Knoten hat allerdings keine Redundanz. Ein Festplattenausfall
kann alle WACZ-Dateien vernichten. Ledger und Zeitstempel belegen dann weiterhin, was existierte,
die Dateien selbst lassen sich aber nicht mehr vorlegen.

### Produktiver Betrieb mit AWS S3 {#production-with-aws-s3}

API und Worker verwenden denselben konfigurierbaren S3-Client hinter `IObjectStore`.
Für einen bestehenden AWS-S3-Bucket in Frankfurt konfigurierst du beide Dienste in `deploy/.env`:

```bash
STORAGE_SERVICE_URL=https://s3.eu-central-1.amazonaws.com
STORAGE_REGION=eu-central-1
STORAGE_BUCKET=your-archive-bucket
STORAGE_ACCESS_KEY=...
STORAGE_SECRET_KEY=...
```

Wähle den zum Bucket passenden [regionalen S3-Endpunkt](https://docs.aws.amazon.com/general/latest/gr/s3.html).
Außerhalb von Compose setzt du die entsprechenden `Storage`-Optionen für beide Dienste;
siehe [Konfiguration](/de/reference/configuration). Der Standard `Storage:ForcePathStyle=true`
funktioniert mit gewöhnlichen AWS-S3-Buckets. Der aktuelle Client benötigt Access Key und
Secret Key. Er nutzt weder die AWS-Standardkette für Zugangsdaten noch IAM-Rollen oder Session-Tokens.

Die mitgelieferte Compose-Datei startet weiterhin `garage` und `garage-init`; die API hängt
weiterhin von `garage-init` ab. Für Produktion ohne Garage benötigst du eine separate
Compose-Datei ohne diese Dienste und Abhängigkeit. Ein neuer Endpunkt leitet den Speicherzugriff
um, migriert aber keine vorhandenen Objekte. Halte den bisherigen Speicher erreichbar oder
migriere die Daten gesondert.

Die WORM-Prüfung löscht die genaue hochgeladene Version. AWS-Löschmarker verursachen dadurch
kein falsch negatives Ergebnis. Siehe [AWS Object Lock](https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-lock.html).
Die Zugangsdaten benötigen `s3:GetBucketObjectLockConfiguration`, `s3:GetObjectRetention`,
`s3:PutObject`, `s3:PutObjectRetention` und `s3:DeleteObjectVersion`. Beim Start wird auch der
Bucket-Standort gelesen. Bleibt das Ergebnis `ApiPresentUnverified`, fordert PriorState für
Archiv-Uploads trotzdem COMPLIANCE-Aufbewahrung an, sofern Object Lock angeboten wird.
Bucket-Standardaufbewahrung oder unzureichende Prüfrechte können den Nachweis verhindern.

Für ein Archiv, auf dessen Nachweis du angewiesen bist, verwende einen Speicher mit
durchgesetztem Object Lock und aktiviere es beim Anlegen des Buckets. Bei den meisten
Implementierungen ist das nur zu diesem Zeitpunkt möglich.

```bash
# In deploy/.env
STORAGE_SERVICE_URL=https://s3.eu-central-003.backblazeb2.com
STORAGE_REGION=eu-central-003
STORAGE_BUCKET=your-archive-bucket
STORAGE_ACCESS_KEY=...
STORAGE_SECRET_KEY=...
```

Prüfe anschließend das Ergebnis:

```bash
curl -s localhost:8080/health | grep -i worm
```

Meldet die Prüfung etwas anderes als `Enforced`, kontrolliere neben der Konfiguration auch,
ob das Backend die Aufbewahrung tatsächlich erzwingt.

## Speicherbedarf der Aufbewahrung {#retention-arithmetic}

Mit Object Lock kann auch der Betreiber vor Fristablauf nichts löschen. Berechne den
Speicherbedarf, bevor du sechs oder zehn Jahre Aufbewahrung aktivierst, und halte ihn fest.
Ein WACZ einer mittelgroßen Website benötigt meist 5 bis 50 MB. Zehn täglich erfasste Seiten
über sechs Jahre ergeben einige Hundert Gigabyte, die vorher nicht entfernt werden können.

Die API erlaubt keine nachträgliche Verkürzung der Aufbewahrungsfrist. Der Betreiber soll
unbequeme Snapshots nicht vorzeitig verfallen lassen können.

## Wenn Garage Object Lock ergänzt {#when-garage-adds-object-lock}

PriorState benötigt dafür keine Änderung. Bei tatsächlich durchgesetztem Schutz meldet die
Prüfung `Enforced`, und neue Snapshots erhalten diesen Status. Frühere Snapshots behalten ihren
bisherigen Status, weil sie beim Schreiben keinen Schutz auf Speicherebene hatten.
