# Zeitstempeldienst

Die Wahl des Zeitstempeldiensts lässt sich für bereits verankerte Snapshots nicht
nachträglich korrigieren.

## Was der Zeitstempel belegt {#what-the-timestamp-does}

PriorState sammelt stündlich unverankerte Einträge aus Tagen vor dem aktuellen UTC-Tag und
übermittelt einen Merkle-Wurzelhash für diese Charge an einen RFC-3161-Zeitstempeldienst.
Nach einem Ausfall kann eine Charge mehrere Tage umfassen. Manuelle Verankerung berücksichtigt
auch heutige Einträge. Der Dienst liefert ein signiertes Token, das den Empfang dieses Werts
zu seinem Signaturzeitpunkt bescheinigt.

Dieser Nachweis stammt von einem Dritten mit einem eigenen Schlüssel. Die Hash-Kette zeigt
interne Konsistenz, steht aber unter der Kontrolle des Datenbankbetreibers. Eine Änderung
eines verankerten Eintrags widerspricht der externen Signatur, die der Betreiber nicht fälschen
kann. Voraussetzung ist, dass der Empfänger dem Zertifikat des Diensts unabhängig vertraut.

## Der Standarddienst reicht für einen Streitfall nicht aus {#the-default-is-not-good-enough-for-a-dispute}

FreeTSA ist vorkonfiguriert, damit sich das gesamte System ohne Benutzerkonto ausprobieren
lässt. Der Dienst funktioniert und seine Tokens sind korrekt prüfbar. Er ist jedoch kein
qualifizierter Vertrauensdiensteanbieter nach eIDAS.

Bei Verwendung eines nicht qualifizierten Diensts zeigt die Oberfläche einen Hinweis,
jedes betroffene Protokoll enthält eine Warnung und jede Verankerung wird entsprechend
protokolliert. Diese Hinweise lassen sich nicht abschalten, damit Empfänger den Status
vor einer möglichen Anfechtung kennen.

::: danger Nachträglicher Wechsel für bestehende Snapshots nicht möglich
Snapshots können nicht bei einem anderen Dienst neu verankert werden. Ein mit FreeTSA
verankerter Tag behält diese Verankerung. Ein späterer Zeitstempel würde nur belegen, dass
der Eintrag zum späteren Zeitpunkt existierte. Richte einen qualifizierten Anbieter ein,
bevor du Inhalte erfasst, auf deren Nachweis du dich verlassen möchtest.
:::

## Einen qualifizierten Anbieter einrichten {#configuring-a-qualified-provider}

Bei der Bündelung abgeschlossener Tage wird normalerweise ein Zeitstempel pro aktivem Tag
benötigt. Manuelle Verankerungen können zusätzliche Anfragen auslösen.

```bash
# deploy/.env
TSA_URL=https://tsa.example-qtsp.eu/tsr
TSA_QUALIFIED=true
TSA_DISPLAY_NAME=Example QTSP qualified timestamp service
```

`TSA_QUALIFIED` ist eine Angabe des Betreibers. Sie wird an jeder Verankerung gespeichert
und in jedem Protokoll ausgegeben. PriorState kann den qualifizierten Status nicht prüfen.
Für einen Anbieter außerhalb der EU-Vertrauensliste `true` zu setzen, würde eine falsche
Aussage in ein rechtlich verwendetes Dokument aufnehmen.

Lege die Zertifikatskette des Anbieters in `deploy/tsa-chain.pem` ab. Sie wird jedem
Beweispaket beigelegt, damit Empfänger das Token auch Jahre später offline prüfen können,
selbst wenn der Dienst nicht mehr erreichbar ist. Bei zehn Jahren Aufbewahrung muss das
berücksichtigt werden.

Für die Standardkonfiguration liegt die offizielle FreeTSA-CA bei. Herkunft, Download-Hash,
Zertifikatsfingerabdruck und Austauschverfahren stehen in `deploy/tsa-certificates.md`.
Die API prüft den konfigurierten PEM-Inhalt beim Start und vor dem Export. Diese Prüfung
betrifft das Zertifikatsmaterial, nicht Anbieteridentität oder Sperrstatus.
Eine neue Kopie lässt sich beim Anbieter herunterladen:

```bash
curl -o deploy/tsa-chain.pem https://freetsa.org/files/cacert.pem
```

## Wenn der Dienst nicht erreichbar ist {#if-the-authority-is-unreachable}

Die Verankerung läuft stündlich und versucht weiterhin alle unverankerten Tage zu bearbeiten.
Der HTTP-Aufruf verwendet die üblichen Mechanismen zur Behandlung vorübergehender Fehler.
Ein nicht bestätigter Tag bleibt ausstehend und wird später erneut berücksichtigt.
Die Einträge stehen bereits in der Kette und gehen dadurch nicht verloren.

Der stündliche Ablauf ermöglicht auch Installationen, die nachts ausgeschaltet sind,
ihre Einträge zu verankern. Beobachte diesen Rückstand: `/api/ledger/status` meldet die
Anzahl ausstehender Einträge, und die Ledger-Seite zeigt sie an.

## Ein Token von Hand prüfen {#verifying-a-token-by-hand}

```bash
openssl ts -reply -in timestamp/token.tsr -token_in -text
openssl ts -verify -digest <merkle-root-hex> -in timestamp/token.tsr -token_in \
  -CAfile /path/to/independently-trusted-ca.pem \
  -untrusted timestamp/tsa-chain.pem
```

Das entspricht der Signaturprüfung in Schritt 4 von `sh verify.sh --ca-file PATH`.
Lasse `-untrusted` weg, wenn keine Kette beiliegt und das Token seine Signaturkette bereits
enthält. Dienst-URL und Qualifizierungsstatus im Manifest sind Angaben des Betreibers.
Diese Signatur authentifiziert sie nicht. Sie bestätigt die Existenz der Bytes vor dem
signierten Zeitpunkt, nicht deren Empfang von einer bestimmten Quell-URL oder die genaue
Erfassungszeit.
