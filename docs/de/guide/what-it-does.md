# Was PriorState leistet

PriorState hält fest, was ein Besucher zu einem bestimmten Zeitpunkt auf einer Webseite
gesehen hat. Die Aufzeichnung lässt sich später auch von jemandem prüfen, der dem Betreiber
nicht vertraut.

## Was ein Snapshot erfasst {#what-a-snapshot-captures}

Beim Abruf setzt sich eine Seite aus mehreren Quellen zusammen:

- CMS-Inhalten aus der Datenbank
- Feature Flags, A/B-Tests und Personalisierung
- eingebetteten Inhalten Dritter
- Preisen und Daten aus Backend-Systemen

PriorState zeichnet das ausgelieferte und dargestellte Ergebnis auf. Nach einer Änderung
existiert dieser Zustand sonst nicht mehr.

Erfassungen können außerdem einem Release zugeordnet werden. Das Deployment-Ledger verknüpft
einen Commit-SHA mit dem Snapshot nach dem Deployment. So lässt sich eine Änderung der
dargestellten Seite auf das zugehörige Release zurückführen.

## Die vier Schichten {#the-four-layers}

Die Erfassung übernimmt [browsertrix-crawler](https://github.com/webrecorder/browsertrix-crawler).
Er steuert einen vollständigen Chromium-Browser und schreibt
[WACZ-Archive](https://specs.webrecorder.net/wacz/), die sich mit
[ReplayWeb.page](https://replayweb.page) interaktiv wiedergeben lassen. In einem Streitfall
kann damit auf ein etabliertes Erfassungswerkzeug verwiesen werden.

Die Snapshot-Metadaten jedes WACZ enthalten die erste Start-URL des Projekts und den Beginn des
Crawls. Ein WACZ kann mehrere Seiten enthalten; deren einzelne URLs, Weiterleitungen und
Abrufzeiten werden nicht in die Snapshot-Metadaten übernommen. Diese Angaben stehen im Archiv.
Regelmäßige Erfassungen grenzen eine beobachtete Änderung zeitlich ein. Ihren genauen Zeitpunkt
oder eine durchgehende Verfügbarkeit belegen sie nicht.

Das Ledger hasht jeden Snapshot über eine [festgelegte kanonische Darstellung](/de/reference/canonical-form)
und verknüpft ihn mit seinem Vorgänger. Das standardmäßige Laufzeitkonto darf Einträge anlegen
und bestimmte Felder einmalig ergänzen. Es kann weder die Historie umschreiben noch die
schützenden Trigger entfernen. Administratoren und Betreiber des Docker-Hosts können diese
Schutzmaßnahmen weiterhin aufheben; siehe [Datenbankkonten](/de/operations/database).

Für die Zeitstempel prüft der Worker stündlich, ob unbestätigte Einträge aus Tagen vor dem
aktuellen UTC-Tag vorliegen. Er fasst sie in einem Merkle-Baum zusammen; eine Charge kann mehrere
Tage umfassen. Eine manuelle Verankerung schließt auch heutige Einträge ein. Bis die Verankerung
gelingt, fehlt ein externer Nachweis. Ein gültiges RFC-3161-Token bestätigt, dass die gehashten
Bytes spätestens zum Signaturzeitpunkt existierten. Es prüft weder die vom Betreiber
aufgezeichnete Beobachtungszeit noch Quell-URL oder Inhalt unabhängig.

Das Beweispaket exportiert Archiv, PDF-Protokoll, Zeitstempel-Token, Merkle-Prüfpfad und
`verify.sh`. Zur Prüfung werden eine POSIX-Shell, OpenSSL, sha256sum, xxd und die üblichen
Unix-Werkzeuge sowie ein unabhängig vom Paket authentifiziertes TSA-Wurzelzertifikat benötigt.
Den vollständigen Aufruf und die Voraussetzungen beschreibt [Das Beweispaket](/de/guide/evidence-package).

## Bewusst ausgeschlossene Funktionen {#deliberately-missing}

Die Anwendung bietet folgende Möglichkeiten nicht an:

- einzelne Snapshots löschen
- eine Aufbewahrungsfrist nachträglich verkürzen
- den Zeitstempeldienst für bestehende Einträge wechseln
- Erfassungseinstellungen beliebig ändern: Profile sind [benannt und versioniert](/de/operations/capture-profiles),
  Änderungen gelten für künftige Erfassungen und werden im Audit-Log festgehalten

Frei veränderbare Einstellungen für Darstellungsfläche, User-Agent oder Wartezeiten erleichtern
den Einwand, eine Erfassung sei auf ein gewünschtes Ergebnis zugeschnitten worden. Ein benanntes,
versioniertes Profil im Protokoll macht die verwendeten Einstellungen nachvollziehbar.
