# Anforderungen vor dem Start

Kläre diese Fragen vor den ersten ernsthaften Erfassungen. Ein halber Tag Planung vermeidet
spätere Probleme, insbesondere bei einer Aufbewahrungsfrist, die sich nicht verkürzen lässt.

Kopiere diese Seite in dein Repository oder Wiki und trage die Antworten ein. Sie fließen
auch in die [Verfahrensdokumentation](/de/legal/verfahrensdokumentation) ein.

## Umfang {#scope}

Welche URLs sollen erfasst werden? Liste die Start-URLs auf. Soll der Crawl Links folgen,
ergänze die einzuschließenden und auszuschließenden Präfixe.

> …

Wessen Seiten sollen erfasst werden? Nur eigene oder auch fremde Angebote? Technisch ist
die Erfassung einer Konkurrenzseite identisch, rechtlich muss sie gesondert beurteilt werden.
Crawle zurückhaltend, greife nicht auf geschützte Anmeldebereiche zu und hole vorher Rat ein.

> …

Wie tief soll der Crawl gehen? Ein zu niedriges Seitenlimit erzeugt unvollständige
Erfassungen, ohne dass das sofort auffällt. Ein zu hohes Limit erzeugt Archive, die niemand
mehr durchsehen kann. Beginne mit einem klar begrenzten Umfang.

> …

## Häufigkeit {#frequency}

Wie oft soll erfasst werden? Täglich ist eine übliche Wahl. Überlege, worauf ein Streitfall
hinauslaufen würde: Bei der Frage nach dem Änderungszeitpunkt bestimmt das Erfassungsintervall
die zeitliche Auflösung deiner Antwort.

> …

Auch bei Deployments? Der Deployment-Webhook verknüpft den Commit mit dem Snapshot nach
dem Release. So lassen sich Änderungen der Seite auf das auslösende Release zurückführen.

> …

## Aufbewahrung {#retention}

Wie viele Jahre? Sechs und zehn Jahre sind häufige Antworten im deutschen Geschäftsumfeld.
Lass die passende Frist rechtlich prüfen.

> …

Ist der Speicherbedarf berechnet? Ein WACZ einer mittelgroßen Seite benötigt meist 5 bis
50 MB. Multipliziere das mit Seitenzahl, Häufigkeit und Jahren. Halte das Ergebnis fest.

> …

::: warning Aufbewahrungsfristen lassen sich nur verlängern
Die API weist eine Verkürzung zurück. Der Betreiber soll unbequeme Snapshots nicht vorzeitig
verfallen lassen können. Wähle eine Frist, deren Speicherung du dauerhaft finanzieren kannst.
:::

## Zugriff {#access}

Wer darf das Archiv einsehen? Snapshot-Aufrufe stehen mit der tatsächlichen Benutzeridentität
im Audit-Log. Ein gemeinsam verwendetes Konto würde diese Zuordnung unbrauchbar machen.

> …

Lokale Konten oder ein vorhandener Identitätsanbieter? Beide Wege funktionieren.
Wenn bereits ein Anbieter vorhanden ist, lohnt sich die OIDC-Konfiguration.

> …

Wer darf Beweispakete exportieren? Durch den Export erhält eine dritte Person das Archiv.
Er wird wie jeder andere Zugriff protokolliert.

> …

## Zeitstempel {#timestamping}

Welcher Dienst? Siehe [Zeitstempeldienst](/de/operations/timestamping). Soll zunächst der
Standard verwendet werden, setze ein Datum für den Wechsel zu einem qualifizierten Anbieter.
Behandle vorherige Erfassungen nicht als für einen Streitfall geeignet.

> …

Liegt die Zertifikatskette vor? Sie gehört nach `deploy/tsa-chain.pem` und wird jedem
Beweispaket beigelegt.

> …

## Speicher {#storage}

Welches Backend? Siehe [Speicher und WORM](/de/operations/storage). Wenn du das mitgelieferte
Garage nutzt, halte fest, dass es weder WORM noch Redundanz durchsetzt, und begründe,
warum das für deinen Fall ausreicht.

> …

Wo liegen die Backups, und wurde eine Wiederherstellung getestet? Die Hash-Kette belegt,
was existierte. Verlorene WACZ-Dateien kann sie nicht zurückbringen.
Siehe [Datensicherung und Aufbewahrung](/de/operations/backup).

> …
