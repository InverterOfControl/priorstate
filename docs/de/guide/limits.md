# Grenzen des Nachweises

Wer sich auf ein Archiv verlässt, muss wissen, was es belegt. Zu weit gehende Zusagen können
gerade im Streitfall die Glaubwürdigkeit des Nachweises beschädigen.

## Was ein Beweispaket belegt {#what-an-evidence-package-proves}

1. Die Archivdatei entspricht Byte für Byte der aufgezeichneten Datei.
2. Die aufgezeichneten Metadaten ergeben den im Ledger festgehaltenen Eintrags-Hash:
   bei Browserarchiven Start-URL und Crawl-Beginn, bei Plugin-Antworten Beobachtungszeit sowie
   jeweils Profil und Bedingungen.
3. Dieser Eintrag gehört zum Merkle-Wurzelhash seiner Verankerungscharge.
4. Ein Zeitstempeldienst hat diesen Wurzelhash zum bescheinigten Zeitpunkt signiert,
   sofern seine Zertifikatskette gegen ein vom Empfänger unabhängig authentifiziertes
   Wurzelzertifikat geprüft werden kann.

Zusammen belegt das: Der Snapshot existierte vor dem bescheinigten Zeitpunkt in genau dieser
Form und wurde seitdem nicht verändert.

Bis zur erfolgreichen Verankerung hat ein Snapshot keinen externen Zeitstempel. Der Worker
prüft stündlich und fasst ausstehende Einträge aus Tagen vor dem aktuellen UTC-Tag zusammen.
Eine manuelle Verankerung kann auch heutige Einträge einschließen. Eine Charge kann mehrere
Tage abdecken. Nicht alle unsignierten Beschreibungen in Protokoll und Manifest werden vom
Prüfskript kryptografisch authentifiziert.

## Was es nicht belegt {#what-it-does-not-prove}

### Den genauen Zeitpunkt der Erfassung oder Entfernung

Browser-Snapshots speichern die erste Start-URL des Projekts und den Crawl-Beginn. Das sind
nicht die tatsächliche Abrufzeit und endgültige URL jeder einzelnen Seite. Genauere Angaben
stehen in den einzelnen Seitenaufzeichnungen des WACZ. Auch die Beobachtungszeiten von Plugins
stammen vom System des Betreibers. Ein Zeitstempeldienst bestätigt, dass die gehashten Bytes
zum Signaturzeitpunkt bereits existierten. Er überprüft diese aufgezeichneten Zeiten und die
Herkunft der Inhalte nicht. Vergleiche regelmäßiger Besuche können eine beobachtete Änderung
eingrenzen. Wann genau sie erfolgte und was zwischen den Besuchen ausgeliefert wurde, bleibt offen.

### Dass Administratoren die Datenbank nicht verändern können

Das eingeschränkte Laufzeitkonto kann die Ledger-Historie weder umschreiben noch ihre Trigger
entfernen. Schema-Administratoren und Personen mit Kontrolle über den Docker-Host können das
weiterhin. Eine unabhängige Zeitstempelprüfung kann Änderungen an verankerten Bytes aufdecken,
aber keine verlorenen Archive wiederherstellen. Siehe [Datenbankkonten](/de/operations/database).

### Dass die Erfassung vollständig war

Ein zu niedriges Seitenlimit, eine abweichende Darstellung für Rechenzentrums-IP-Adressen oder
Inhalte hinter einer nicht ausgeführten Interaktion führen zu einer echten, unveränderten
Aufzeichnung eines unvollständigen Besuchs. Die Vollständigkeit muss anhand des WACZ und der
aufgezeichneten Erfassungsbedingungen beurteilt werden.

### Dass die Erfassung repräsentativ war

Personalisierung, A/B-Tests und regionale Unterschiede machen jeden Besuch zu einer einzelnen
Beobachtung. Häufige Erfassungen mit dokumentierter Konfiguration verringern das Risiko,
begründen aber keinen Nachweis der Repräsentativität.

### Dass die Seite für alle erreichbar war

Das Archiv hält fest, was dieser Browser aus diesem Netzwerk zu diesem Zeitpunkt empfangen hat.

### Dass Daten eines Erfassungsplugins richtig waren

Ein [Erfassungsplugin](/de/operations/plugins) archiviert die Antwort eines Endpunkts. Das Paket
belegt, dass diese Bytes unverändert sind und vor dem Zeitstempel existierten. Es bestätigt
nicht die Richtigkeit der Antwort. Fehler im Quellsystem werden ebenfalls unverändert archiviert.

### Dass der Speicher unveränderbar war

Der beobachtete Status wird für jeden Snapshot ausgewiesen und lautet häufig "nein".
Siehe [Speicher und WORM](/de/operations/storage). Das berührt die Nachweise 1 bis 4 oben nicht.

### Dass ein Gericht das Archiv akzeptiert

Das hängt auch vom betriebenen Verfahren ab. Die [Verfahrensdokumentation](/de/legal/verfahrensdokumentation)
hilft, dieses Verfahren zu beschreiben.

## Verantwortung des Betreibers {#where-responsibility-sits}

PriorState ist Software unter AGPL-3.0, die du selbst betreibst. Die Lizenz schließt
Gewährleistung aus. Die Verantwortung für den Beweiswert der erzeugten Unterlagen liegt beim
Betreiber. Dafür sind der gewählte Zeitstempeldienst, die Aufbewahrungsfristen, Zugriffskontrollen
und das dokumentierte Verfahren maßgeblich.

Diese Dokumentation ist keine Rechtsberatung. Fragen zu Beweiswert, Lizenzwahl und
Arbeitsverträgen gehören in eine anwaltliche Beratung.
