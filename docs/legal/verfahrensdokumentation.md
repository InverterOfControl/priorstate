# Verfahrensdokumentation — Vorlage

> **Zum Gebrauch dieser Vorlage.** Dieses Dokument ist eine ausfüllbare Vorlage, keine
> Rechtsberatung. Kopieren Sie es in Ihre eigene Ablage, füllen Sie die mit `…` markierten Stellen
> aus, streichen Sie, was nicht zutrifft, und lassen Sie es vor dem Ernstfall anwaltlich prüfen.
>
> Diese Vorlage ist bewusst auf Deutsch gehalten. Der übrige Quelltext und die Dokumentation von
> PriorState sind englisch; die Verfahrensdokumentation und das Erfassungsprotokoll sind die
> beiden Ausnahmen, weil sie für eine bestimmte Rechtsordnung geschrieben sind.

Die Verfahrensdokumentation beschreibt, **wie** ein Archiv entsteht und **warum** man sich darauf
verlassen kann. Sie fehlt in der Praxis fast immer — und sie entscheidet mit darüber, ob ein
Gericht einem Archiv folgt. Ein technisch einwandfreies Archiv ohne beschriebenes Verfahren ist
schwerer zu verteidigen als ein einfacheres Archiv mit sauberer Dokumentation.

---

## 1. Gegenstand und Zweck

**Betreiber:** …
(Firma, Anschrift, verantwortliche Person)

**Zweck des Archivs:** …
> Beispiel: Nachweis des Inhalts eigener Webseiten zu beliebigen Zeitpunkten, insbesondere zum
> Beleg von Werbeaussagen und Preisangaben sowie zum Nachweis der Entfernung beanstandeter
> Inhalte nach Abgabe einer Unterlassungserklärung.

**Erfasste Webangebote:** …
(Domains, Start-URLs, Umfang der Erfassung)

**Beginn des Archivbetriebs:** …

**Eingesetzte Software:** PriorState, Version …, Quelltext öffentlich einsehbar unter
<https://github.com/InverterOfControl/priorstate>, Lizenz AGPL-3.0-only.

---

## 2. Ablauf der Erfassung

Die Erfassung erfolgt vollautomatisch. Ein manueller Eingriff in den Ablauf ist nicht vorgesehen
und technisch nicht möglich.

**Auslösung:** …
(zeitgesteuert nach Zeitplan `…`; zusätzlich bei jedem Deployment über Webhook; zusätzlich manuell
durch berechtigte Personen — Zutreffendes angeben)

**Erfassungswerkzeug:** `browsertrix-crawler`, Version …, in einem Container mit einem
vollständigen Chromium-Browser. Die Erfassung entspricht damit dem Abruf durch einen gewöhnlichen
Besucher; es werden keine Rohdaten aus dem Content-Management-System übernommen.

**Archivformat:** WACZ nach der Spezifikation von Webrecorder. Das Format ist offen dokumentiert
und mit frei verfügbarer Software (ReplayWeb.page) auch ohne PriorState wiedergebbar.

**Erfassungsbedingungen:** Sämtliche Einstellungen des Browsers ergeben sich aus einem benannten
und versionierten Erfassungsprofil. Verwendet wird Profil …, Version ….

Das Profil legt fest:

| Merkmal | Wert |
|---|---|
| Angemeldete Sitzung | nein |
| Inhaltsblocker | nicht aktiv |
| User-Agent | … |
| Darstellungsfläche | … |
| Cookie-Banner | … |
| Wartezeit nach dem Laden | … ms |

Die tatsächlich verwendeten Browser- und Crawler-Versionen werden zum Zeitpunkt der Erfassung aus
dem ausgeführten Container ausgelesen und je Erfassung gespeichert; sie werden nicht aus der
Konfiguration übernommen.

**Änderungen an Erfassungsprofilen** erzeugen stets eine neue Profilversion. Bestehende Erfassungen
behalten die Version, unter der sie aufgenommen wurden. Eine nachträgliche Änderung eines Profils
ist technisch ausgeschlossen (Datenbank-Trigger); jede Änderung wird protokolliert.

**Zusatzmodule (Erfassungsmodule):** …
(Zutreffendes angeben — falls keine Module eingesetzt werden: „Es werden keine Zusatzmodule
eingesetzt." Dieser Absatz kann dann entfallen.)

Neben der Seitenerfassung können Zusatzmodule Daten archivieren, die auf der Seite selbst nicht
enthalten sind — beispielsweise Preise, die von einer internen Schnittstelle abgerufen werden. Das
Ergebnis wird als eigener Stand in derselben Hash-Kette geführt und unterliegt denselben Regeln wie
eine Seitenerfassung.

| Modul | Konfiguration (Bezeichnung, Version) | Abgerufene Schnittstelle | Projekt |
|---|---|---|---|
| … | … | … | … |

Für Zusatzmodule gilt entsprechend:

- Die Module sind fest in die eingesetzte Programmversion eingebunden. Ein Nachladen beliebigen
  Programmcodes zur Laufzeit findet nicht statt; der ausgeführte Quelltext ist derselbe, der unter
  der AGPL-3.0 veröffentlicht ist.
- Ein Modul kann ausschließlich neue Einträge veranlassen. Ein Zugriff auf bereits gespeicherte
  Einträge, auf den Objektspeicher oder auf die Hash-Kette ist ihm technisch nicht möglich.
- **Änderungen an der Konfiguration eines Moduls** erzeugen stets eine neue Version und lösen die
  bisherige ab. Bestehende Erfassungen behalten die Version, unter der sie aufgenommen wurden; eine
  nachträgliche Änderung ist durch Datenbank-Trigger ausgeschlossen und wird protokolliert.
- Der Hash der jeweils verwendeten Konfiguration ist Bestandteil des Eintrags-Hashes. Die
  Konfiguration liegt jedem Beweispaket bei und ist damit durch Dritte nachrechenbar.
- Zugangsdaten sind nicht Bestandteil der Konfiguration. Aufgezeichnet wird ausschließlich der Name
  der Umgebungsvariablen, aus der sie zur Laufzeit gelesen werden.
- Die Version eines Moduls wird zum Zeitpunkt der Ausführung aus dem ausgeführten Programmstand
  ausgelesen; sie wird nicht aus der Konfiguration übernommen.

Bescheinigt wird für solche Stände der Empfang der Daten, nicht deren inhaltliche Richtigkeit.

---

## 3. Sicherung der Unveränderbarkeit

**Hash-Kette.** Zu jeder Erfassung wird eine festgelegte, dokumentierte kanonische Darstellung
gebildet (URL, Erfassungszeitpunkt in UTC, SHA-256 der Archivdatei, Profilversion,
Erfassungsbedingungen, Werkzeugversionen) und mit SHA-256 gehasht. Jeder Eintrag enthält den Hash
seines Vorgängers. Eine nachträgliche Änderung eines Eintrags ist damit rechnerisch feststellbar.

Die kanonische Darstellung ist unter
<https://inverterofcontrol.github.io/priorstate/reference/canonical-form> vollständig dokumentiert.

**Technische Absicherung gegen Änderungen.** Die Tabellen der Kette sind in der Datenbank als
ausschließlich anfügbar eingerichtet. `UPDATE`, `DELETE` und `TRUNCATE` werden durch Trigger
abgewiesen — auch gegenüber dem Betreiber und gegenüber administrativen Datenbankkonten. Zulässig
ist ausschließlich das einmalige Nachtragen der Zeitstempel-Zuordnung, die selbst nicht in die
Hashbildung eingeht.

Die entsprechende Datenbankmigration ist im Quelltext offen einsehbar und kann von der Gegenseite
geprüft werden.

**Externer Zeitstempel.** Einmal täglich wird über die Einträge des Tages ein Merkle-Wurzelhash
gebildet und einem Zeitstempeldienst nach RFC 3161 zur Signatur vorgelegt.

Verwendeter Dienst: …
Qualifizierter Vertrauensdiensteanbieter nach eIDAS: ja / nein — …

> Ist hier „nein" einzutragen, ist dies ausdrücklich zu vermerken und zu begründen. Die
> Zeitstempel sind dann technisch gültig und nachprüfbar, erfüllen aber nicht die Anforderungen an
> einen qualifizierten elektronischen Zeitstempel. Eine nachträgliche Umstellung bereits erfasster
> Stände auf einen anderen Zeitstempeldienst ist nicht möglich.

**Speicherung.** Die Archivdateien werden abgelegt bei …
(Speicherort, Anbieter, Standort der Daten)

Unveränderbarkeit des Speichers (Object Lock / WORM): …

> PriorState prüft beim Start, ob der eingesetzte Speicher eine Sperrfrist tatsächlich durchsetzt,
> und vermerkt das Ergebnis bei jeder einzelnen Erfassung sowie in jedem Beweispaket. Tragen Sie
> hier das tatsächliche Ergebnis ein, nicht die Absicht.
>
> Setzt der Speicher keine Sperrfrist durch, ist das zu vermerken. Der Nachweis beruht dann
> allein auf Hash-Kette und externem Zeitstempel — beide bleiben auch dann gültig, wenn der
> Speicher gelöscht oder verändert wird.

**Löschung einzelner Stände.** Technisch nicht vorgesehen. Die Software bietet keine Funktion zum
Löschen einzelner Erfassungen und keine Möglichkeit, eine Aufbewahrungsfrist nachträglich zu
verkürzen.

---

## 4. Aufbewahrung

**Aufbewahrungsdauer:** … Jahre

**Rechtsgrundlage bzw. Anlass der gewählten Dauer:** …

**Verlängerung:** möglich. **Verkürzung:** durch die Software ausgeschlossen.

**Datensicherung:** …
(Verfahren, Häufigkeit, Aufbewahrungsort der Sicherungen, Datum der letzten erfolgreichen
Wiederherstellungsprüfung)

---

## 5. Zugriff und Protokollierung

**Zugriffsberechtigte Personen:** …
(namentlich oder nach Rolle)

**Authentifizierung:** …
(lokale Benutzerkonten / Anmeldung über … — Zutreffendes angeben)

Gemeinsam genutzte Zugangsdaten werden nicht verwendet.

**Protokollierung.** Protokolliert werden nicht nur Änderungen, sondern auch Lesezugriffe:
Anzeigen einer Erfassung, Wiedergabe eines Archivs, Erzeugung eines Beweispakets, Auslösen einer
Erfassung, Anlegen und Ändern von Projekten und Erfassungsprofilen, An- und fehlgeschlagene
Anmeldungen.

Das Zugriffsprotokoll ist ebenfalls ausschließlich anfügbar; Einträge können weder geändert noch
gelöscht werden.

**Aufbewahrung der Protokolle:** … (in der Regel ebenso lange wie die Erfassungen selbst)

---

## 6. Nachprüfbarkeit durch Dritte

Zu jeder Erfassung kann ein Beweispaket erzeugt werden. Es enthält:

- die Archivdatei (WACZ),
- ein Erfassungsprotokoll als PDF,
- die kanonische Darstellung, aus der der Hash gebildet wurde,
- den Zeitstempel-Token nach RFC 3161 nebst Zertifikatskette des Anbieters,
- den Merkle-Nachweis der Zugehörigkeit zum Wurzelhash des Tages,
- ein Prüfskript (`verify.sh`).

Das Prüfskript rechnet sämtliche Angaben ohne Mitwirkung des Betreibers und ohne Netzzugriff nach.
Benötigt werden lediglich eine POSIX-Shell sowie `openssl`, `xxd` und `sha256sum`. Das Skript ist
kurz gehalten und kommentiert, damit es vor der Ausführung vollständig gelesen werden kann.

Der vollständige Quelltext von PriorState steht unter der AGPL-3.0 öffentlich zur Verfügung. Das
Erfassungs- und Prüfverfahren ist damit auch der Gegenseite in vollem Umfang zugänglich.

---

## 7. Zuständigkeiten und Änderungen an diesem Dokument

**Verantwortlich für den Betrieb:** …

**Verantwortlich für dieses Dokument:** …

**Turnus der Überprüfung:** … (empfohlen: jährlich sowie bei jeder Änderung an Erfassungsprofilen,
Zeitstempeldienst, Speicher oder Aufbewahrungsdauer)

### Änderungsverlauf

| Datum | Version | Änderung | Bearbeiter |
|---|---|---|---|
| … | 1.0 | Erstfassung | … |

---

*Dieses Dokument enthält keine Rechtsberatung. Fragen zu Beweiswert, Aufbewahrungsfristen und
Lizenzwahl gehören zum Anwalt.*
