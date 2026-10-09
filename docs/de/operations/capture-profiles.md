# Erfassungsprofile

Ein Erfassungsprofil ist ein benannter, versionierter Satz von Browsereinstellungen:
User-Agent, Darstellungsfläche, Wartezeit für JavaScript und Behandlung von Cookie-Bannern.
Beim ersten Start wird `DE-Standard v1` angelegt.

## Warum die Einstellungen versioniert sind {#why-they-are-not-sliders}

Frei veränderbare Einstellungen erschweren den Nachweis, wie eine Erfassung zustande kam.
Die Gegenseite kann einwenden, die Konfiguration sei für ein gewünschtes Ergebnis gewählt
worden. Der Betreiber müsste dann die damaligen Einstellungen aus einem System belegen,
das er selbst kontrolliert.

Im Protokoll bezeichnet `DE-Standard v1` dagegen einen bestimmten unveränderbaren
Datenbankeintrag, der vor der Erfassung angelegt und nie bearbeitet wurde.

## Änderungen an einem Profil {#how-changes-work}

Eine geänderte Einstellung erzeugt eine neue Version. Die bestehende Version bleibt erhalten.

- Bestehende Snapshots behalten ihre verwendete Version. Alte Protokolle beschreiben so
  weiterhin die tatsächlichen Erfassungsbedingungen.
- Die neue Version gilt nur für nachfolgende Erfassungen.
- Die vorherige Version erhält einen Ablösezeitpunkt und wird nicht gelöscht.
- Die Änderung wird im Audit-Log festgehalten.

Die Datenbank erzwingt diese Regeln. Ein durch die Migration angelegter Trigger erlaubt,
`SupersededAt` genau einmal zu setzen, und weist alle anderen Änderungen und Löschungen
auf der Tabelle ab. Siehe [Architektur](/de/guide/architecture).

## Angaben des Ausgangsprofils {#what-the-baseline-profile-records}

`DE-Standard v1` beschreibt einen neutralen Desktop-Besuch ohne Anmeldung:

| Einstellung | Wert | Begründung |
|---|---|---|
| Angemeldete Sitzung | Nein | Eine angemeldete Ansicht entspricht nicht der Ansicht eines gewöhnlichen Besuchers. |
| Werbeblocker | Nein | Blockierte Inhalte verändern die dargestellte Seite. |
| Darstellungsfläche | 1920 × 1080 | Eine verbreitete Desktop-Größe, die ausdrücklich dokumentiert wird. |
| Cookie-Banner | Wie ausgeliefert belassen | Geringster Eingriff und leicht nachvollziehbar. |
| Wartezeit | 5000 ms | Zeit zum Anzeigen von clientseitig gerenderten Inhalten. |

Chromium- und Crawler-Versionen werden aus dem tatsächlich ausgeführten Container ausgelesen
und am Snapshot gespeichert. Sie gehören nicht zur Vorgabe des Profils. Vorgabe und
tatsächliche Ausführung werden getrennt aufgezeichnet.

## Eine Version anlegen {#creating-a-version}

```
POST /api/capture-profiles
{
  "name": "DE-Standard",
  "rationale": "Increased settle time to 8s: the pricing page renders its table client-side and 5s was occasionally too short.",
  "conditions": { ... }
}
```

Die Begründung ist Pflicht und wird zusammen mit der Version angezeigt. Beschreibe darin,
warum die Einstellungen geändert wurden. Im Beispiel wird die Wartezeit auf acht Sekunden
erhöht, weil fünf Sekunden für die clientseitige Preistabelle gelegentlich nicht ausreichten.
