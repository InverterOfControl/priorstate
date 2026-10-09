# Das Beweispaket

Ein Beweispaket enthält die Unterlagen, die ein Empfänger unabhängig prüfen kann.
Du exportierst es auf der Seite eines Snapshots als ZIP-Datei:

```
protocol.pdf             Human-readable record, in German. Start here.
verify.sh                Checks hashes and the timestamp signature.
snapshot.wacz            The web archive. Opens at replayweb.page, offline.
canonical/entry.txt      The exact bytes that were hashed into the ledger.
manifest.txt             The same facts, machine-readable.
merkle/audit-path.txt    Proof that this entry belongs to the timestamped root.
timestamp/token.tsr      RFC-3161 token from an independent authority.
timestamp/root.txt       The value that token attests to.
timestamp/tsa-chain.pem  Operator-supplied certificates; untrusted chain material.
README.txt               What is in here and how to check it.
```

Beginne mit `protocol.pdf`, dem deutschsprachigen Protokoll. `verify.sh` prüft Hashes und
Zeitstempelsignatur; `snapshot.wacz` lässt sich offline mit replayweb.page öffnen.
`canonical/entry.txt` enthält die exakt gehashten Bytes, `manifest.txt` die Angaben in
maschinenlesbarer Form. Der Merkle-Prüfpfad, das RFC-3161-Token und der bescheinigte Wurzelhash
liegen unter `merkle/` und `timestamp/`. `timestamp/tsa-chain.pem` enthält vom Betreiber
bereitgestellte Zertifikate, die als nicht vertrauenswürdiges Kettenmaterial behandelt werden.
`README.txt` beschreibt Inhalt und Prüfung.

## Prüfung durch den Empfänger {#verification-from-the-recipient-s-side}

```bash
unzip priorstate-evidence-*.zip
cd priorstate-evidence-*
sh verify.sh --ca-file /path/to/independently-trusted-ca.pem
```

Benötigt werden eine POSIX-Shell, `openssl`, `xxd` und `sha256sum`. Das Skript lädt nichts
herunter und kontaktiert keinen Server. Besorge die CA-Wurzelzertifikate über einen unabhängig
authentifizierten Kanal vom Zeitstempeldienst. Prüfe ihre Identität beziehungsweise ihren
Fingerabdruck anhand einer vertrauenswürdigen Quelle.

Die Zertifikatsdatei im Paket stammt vom Betreiber und dient ausschließlich als nicht
vertrauenswürdiges Kettenmaterial. Wer sie ohne unabhängige Authentifizierung als `--ca-file`
übergibt, hebt diese Vertrauensgrenze auf. Relative Pfade werden vom aktuellen Arbeitsverzeichnis
aus aufgelöst, bevor das Skript in das Paketverzeichnis wechselt.

Exit-Code 0 bedeutet, dass die kryptografischen Prüfungen mit den übergebenen Wurzelzertifikaten
erfolgreich waren. Code 1 meldet eine fehlgeschlagene Prüfung, Code 2 fehlende oder ungültige
Argumente oder ein unbrauchbares Paket. `--help` zeigt die Aufrufsyntax. Die Offline-Prüfung
fragt keine Zertifikatssperren ab und prüft nicht den qualifizierten Status des Anbieters.

Das Skript führt vier Prüfungen durch:

1. `sha256sum snapshot.wacz` muss mit dem Archiv-Hash im kanonischen Eintrag übereinstimmen.
2. `sha256sum canonical/entry.txt` muss den festgehaltenen Eintrags-Hash ergeben.
3. Der Merkle-Prüfpfad wird vom Blatt bis zum Wurzelhash der Verankerungscharge nachgerechnet.
4. `openssl ts -verify` prüft das Token gegen die vom Empfänger bereitgestellten
   CA-Wurzelzertifikate. Mitgelieferte Zertifikate dienen nur als nicht vertrauenswürdige
   Zwischenzertifikate.

Das Skript ist ausführlich kommentiert, damit Empfänger es vor der Ausführung lesen können.
Auch deshalb ist das in Schritt 2 gehashte Format [zeilenorientiert](/de/reference/canonical-form).

## Inhalt des Protokolls {#what-the-protocol-says}

Das PDF-Protokoll ist auf Deutsch, weil es für die Weitergabe an deutsche Anwälte oder Gerichte
gedacht ist. Für Browserarchive nennt es die erste Start-URL und den Crawl-Beginn, nicht die
URL und Erfassungszeit jeder Seite. Es dokumentiert außerdem Erfassungsbedingungen, sämtliche
Hashes des Nachweises, Zeitstempeldetails und den beobachteten Status der Speicher-Unveränderbarkeit.

Zwei Hinweise erscheinen automatisch, wenn sie zutreffen, und lassen sich nicht abschalten:

- Der Zeitstempeldienst ist kein qualifizierter eIDAS-Anbieter.
- WORM auf Speicherebene wurde angefordert, aber nicht nachgewiesen, oder ist nicht verfügbar.

Diese Angaben helfen dem Empfänger, den Nachweis einzuordnen.
Siehe [Grenzen des Nachweises](/de/guide/limits).

## Noch nicht exportierbare Snapshots {#snapshots-that-cannot-be-exported-yet}

Der Worker prüft stündlich und verankert ausstehende Einträge aus Tagen vor dem aktuellen
UTC-Tag. Heutige Einträge warten normalerweise bis morgen; eine manuelle Verankerung auf der
Ledger-Seite schließt sie sofort ein. Ohne Verankerung liefert der Export HTTP 409.
Ist der Zeitstempeldienst nicht erreichbar, bleiben die Einträge ausstehend, bis eine Anfrage gelingt.

## Prüfung ohne Beweispaket {#verifying-without-a-package}

Über **Ledger → re-derive the whole chain** lässt sich das gesamte Archiv jederzeit neu
nachrechnen. Die Funktion berechnet alle Eintrags-Hashes aus den aufgezeichneten Metadaten
und prüft jede Verknüpfung. Sie liest die gesamte Kette, denn eine Stichprobe kann nur die
geprüften Einträge bestätigen. Das Ergebnis wird unabhängig vom Ausgang im Audit-Log festgehalten.

## Vertrauen und Aussagekraft {#trust-and-scope}

Eine gültige Signatur bestätigt, dass die Nutzdaten und aufgezeichneten Metadaten vor dem
signierten Zeitpunkt existierten, sofern den gewählten CA-Wurzelzertifikaten und dem Dienst
vertraut wird. Sie belegt weder die Herkunft der Bytes von der angegebenen URL noch den
genauen Crawl-Zeitpunkt oder die Wahrheit der Inhalte und Bedingungen. Die Manifest-Felder
`tsa_url` und `tsa_qualified` sind ungeprüfte Angaben des Betreibers. Die Signatur authentifiziert
diese Felder nicht und begründet keinen qualifizierten Status.
