# Datenbankkonten und Updates

Compose führt `migrate` einmalig mit `POSTGRES_USER` und `POSTGRES_PASSWORD` aus. Der Prozess
wendet EF-Migrationen an, legt das Ausgangsprofil an und richtet `priorstate_app` ein.
API und Worker starten erst nach erfolgreichem Abschluss und erhalten ausschließlich
`POSTGRES_APP_PASSWORD`. Wähle unterschiedliche zufällige Passwörter, beispielsweise 32
zufällige Bytes in Hexadezimaldarstellung. Diese benötigen kein Escaping im Verbindungsstring.

Das Laufzeitkonto darf Benutzerkonten anlegen und Betriebsdaten aktualisieren. Ledger-Tabellen
erlauben Einfügungen, Lesezugriffe und die dokumentierten einmalig setzbaren Felder; Trigger
sichern diese Beschränkung ab. Das Konto darf weder Tabellen besitzen noch Trigger deaktivieren,
Schutzfunktionen ersetzen oder die Historie mit TRUNCATE leeren. Der Schema-Administrator und
Personen mit Kontrolle über den Docker-Host können die Schutzmaßnahmen weiterhin entfernen.
Die Berechtigungen schützen vor Anwendungsfehlern und direktem Missbrauch der Laufzeit-Zugangsdaten.
Ein kompromittierter Host fällt nicht unter diesen Schutz.

## Bestehende Compose-Installationen {#existing-compose-deployments}

1. Sichere PostgreSQL und stoppe API und Worker: `docker compose stop api worker`.
2. Behalte `POSTGRES_USER`, `POSTGRES_PASSWORD` und das Datenbank-Volume bei. Setze ein
   abweichendes `POSTGRES_APP_PASSWORD` in `.env`. Setze niemals `POSTGRES_USER=priorstate_app`.
3. Baue das aktualisierte Image und führe den idempotenten Migrationsjob aus:
   erst `docker compose build migrate`, dann `docker compose run --rm migrate`.
4. Nach erfolgreichem Abschluss starte mit `docker compose up -d --build`. Schlägt der Job
   fehl, korrigiere zuerst die gemeldete Datenbankkonfiguration. Starte nicht erneut die alte
   Anwendung mit administrativer Verbindung.

Wiederhole den Migrationsjob bei Updates oder einem Wechsel des Laufzeitpassworts, während API
und Worker gestoppt sind. Eine Änderung des Administratorpassworts in `.env` allein ändert
nicht das in PostgreSQL gespeicherte Passwort eines bestehenden Volumes. Die Initialisierung
weist Tabellenbesitz oder Mitgliedschaften in anderen Rollen für das Laufzeitkonto zurück.
Ein Administrator muss diese Berechtigungen zuerst prüfen und entfernen oder den Besitz übertragen.

## Betrieb außerhalb von Compose {#running-outside-compose}

Starte die API mit `--migrate`, einem administrativen `ConnectionStrings__Postgres` und
`Database__RuntimePassword`. Starte danach API und Worker ohne `--migrate` mit einem
Verbindungsstring für den Benutzer `priorstate_app`. Der normale API-Start führt keine
Migrationen mehr aus.

Künftige Migrationen mit neuen Tabellen für die Laufzeit müssen auch die expliziten
Berechtigungen in `RuntimeDatabaseRole` ergänzen. Neue Tabellen sind standardmäßig
absichtlich nicht zugänglich.
