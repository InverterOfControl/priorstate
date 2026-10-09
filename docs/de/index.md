---
layout: home

hero:
  text: PriorState-Dokumentation
  tagline: Webseiten als Beweismittel archivieren. Mit Hash-Kette und externen Zeitstempeln, unabhängig vom Betreiber nachprüfbar.

features:
  - title: Interaktiv wiedergebbare Archive
    details: browsertrix-crawler erfasst Webseiten mit einem vollständigen Chromium-Browser und speichert sie als WACZ. PriorState verwendet die ausgereifte Erfassungstechnik von Webrecorder.
  - title: Nur anfügbares Ledger
    details: Jeder Snapshot wird über eine festgelegte kanonische Darstellung gehasht und mit seinem Vorgänger verknüpft. SQL-Trigger weisen UPDATE, DELETE und TRUNCATE auf den Ledger-Tabellen ab. Prüfer können die Regeln direkt einsehen.
  - title: Unabhängige Zeitstempel
    details: Ein täglicher Merkle-Wurzelhash wird einem RFC-3161-Zeitstempeldienst vorgelegt. Sein Token belegt, dass die Einträge vor dem bescheinigten Zeitpunkt in dieser Form existierten. Der Nachweis hängt weder von PriorState noch vom Betreiber oder Speicher ab.
  - title: Durch die Gegenseite prüfbar
    details: Das Beweispaket enthält das Archiv, ein Protokoll, den Zeitstempel-Token und ein lesbares Shellskript mit etwa 150 Zeilen. Empfänger können die Nachweise offline mit openssl und sha256sum nachrechnen.
---

## Wofür das Archiv gedacht ist {#the-problem-this-solves}

Eine Webseite entsteht beim Abruf aus CMS-Inhalten, Feature Flags, A/B-Tests, eingebetteten
Inhalten Dritter und Preisen aus Backend-Systemen. Nach einer Änderung ist der vorherige
Zustand verloren. Ihn nachträglich aus diesen Bestandteilen zusammenzusetzen, bleibt unsicher.

PriorState hilft bei Fragen, die in einem Streitfall auftreten:

- Stand Aussage X am Tag Y auf der Seite?
- Wann wurde eine beanstandete Aussage nach einer Abmahnung entfernt?
- Welcher Preis oder welche Werbeaussage war an einem bestimmten Tag online?

## Worauf der Nachweis beruht {#where-the-guarantee-actually-lives}

Mit Stand 2026 setzt keine einfach selbst betreibbare S3-Implementierung Object Lock
zuverlässig durch. PriorState stützt seinen Nachweis deshalb
auf die Hash-Kette und den externen Zeitstempel. Es [prüft den Speicher](/de/operations/storage)
und hält das Ergebnis für jeden Snapshot im Beweispaket fest.

Der Zeitstempel belegt, dass ein bestimmter Eintrags-Hash vor einem bescheinigten Zeitpunkt
existierte; die Kette macht spätere Änderungen am Eintrag erkennbar. Diese Nachweise bleiben
auch bei einem gelöschten Bucket gültig. Die verlorenen Archivdateien können sie allerdings
nicht wiederherstellen. Der zugesicherte Nachweis ist damit enger als die Aussage, der gesamte
Speicher sei unveränderbar.
