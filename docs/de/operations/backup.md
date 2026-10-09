# Datensicherung und Aufbewahrung

## Was gesichert werden muss {#what-needs-backing-up-and-why}

Sichere sowohl die Postgres-Datenbank als auch den Objektspeicher.

Die Datenbank enthält das Ledger mit Hash-Kette, Zeitstempel-Tokens und Audit-Log.
Geht sie verloren, fehlen die Nachweise. Vorhandene WACZ-Dateien allein belegen weder ihren
Erstellungszeitpunkt noch ihre Unverändertheit.

Der Objektspeicher enthält die WACZ-Archive. Geht er verloren, fehlen die Beweisinhalte.
Kette und Zeitstempel belegen weiterhin, was wann existierte, aber die Dateien lassen sich
nicht mehr vorlegen.

## Datenbank {#database}

```bash
docker compose exec -T postgres \
  pg_dump -U priorstate -Fc priorstate > priorstate-$(date -u +%Y%m%dT%H%M%SZ).dump
```

Der Dump enthält die Trigger, die nur das Anfügen von Einträgen erlauben. Bei einer
Wiederherstellung müssen diese zusammen mit den Daten zurückkehren. Prüfe anschließend:

```sql
-- Must fail with "append-only".
UPDATE snapshots SET "Url" = 'x' WHERE "ChainSequence" = 1;
```

Die Anweisung muss mit "append-only" scheitern. Gelingt sie, wurden die Trigger nicht
wiederhergestellt und das Archiv ist nicht geschützt.

## Objektspeicher {#object-store}

Sichere bei Garage die Volumes `garage-data` und `garage-meta` gemeinsam. Metadaten allein
reichen für eine Wiederherstellung nicht aus. Nutze bei einem gehosteten Backend dessen
Replikations- oder Lifecycle-Verfahren. Ein Bucket mit Object Lock lässt sich nicht nach
einem gewöhnlichen Löschzeitplan aufräumen.

## Wiederherstellung prüfen {#restoring-and-what-to-check-afterwards}

Eine Wiederherstellung ist erst abgeschlossen, wenn die Kette erfolgreich neu berechnet
wurde. Nutze **Ledger → re-derive the whole chain** in der Oberfläche oder:

```bash
curl -sX POST localhost:8080/api/ledger/verify --cookie-jar - | jq
```

Dabei werden alle Eintrags-Hashes aus den aufgezeichneten Metadaten berechnet und sämtliche
Verknüpfungen geprüft. Eine unvollständige oder veraltete Datenbank fällt durch Lücken in
der Sequenz oder unterbrochene Verknüpfungen auf.

Prüfe anschließend stichprobenartig, ob sich ein Archiv herunterladen und wiedergeben lässt.
Eine intakte Kette bestätigt nicht, dass auch der Objektspeicher wiederhergestellt wurde.

## Speicherbedarf der Aufbewahrung {#retention-arithmetic}

Aufbewahrungsfristen lassen sich verlängern, aber nie verkürzen. Die vor dem Start in
[Phase 0](/de/operations/phase-0-requirements) gewählte Frist ist deshalb verbindlich.

Als grobe Planung benötigt ein WACZ einer mittelgroßen Seite meist 5 bis 50 MB.
Zehn täglich erfasste Seiten über sechs Jahre ergeben ungefähr 100 GB bis 1 TB.
Mit Object Lock kann niemand diese Daten vor Ablauf ihrer Frist entfernen, auch der
Betreiber nicht.

Ist das zu viel, lege vor dem Start einen kleineren Erfassungsumfang oder längere Intervalle
fest. Spätere Änderungen reduzieren die bereits vorhandene Datenmenge nicht.

## Eine Installation außer Betrieb nehmen {#retiring-an-installation}

Vor der Abschaltung:

1. Exportiere für jeden weiterhin relevanten Snapshot ein Beweispaket. Es ist eigenständig
   und ohne PriorState langfristig mit den Prüfwerkzeugen nachprüfbar.
2. Bewahre den Datenbank-Dump zusammen mit den Paketen auf.
3. Sichere die Zertifikatskette des Zeitstempeldiensts. Sie liegt bereits in den
   Beweispaketen, eine zusätzliche Kopie ist trotzdem sinnvoll.

Das [Beweispaket](/de/guide/evidence-package) ist so angelegt, dass es das Projekt überdauern kann.
