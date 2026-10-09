import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'PriorState',
  description: 'Tamper-evident website archiving for use as evidence',
  lang: 'en-GB',
  locales: {
    root: { label: 'English', lang: 'en-GB' },
    de: {
      label: 'Deutsch',
      lang: 'de-DE',
      description: 'Webseiten manipulationsnachweisbar archivieren und als Beweismittel prüfen',
      themeConfig: {
        nav: [
          { text: 'Einführung', link: '/de/guide/what-it-does' },
          { text: 'Betrieb', link: '/de/operations/storage' },
          { text: 'Referenz', link: '/de/reference/canonical-form' },
          { text: 'Rechtliches', link: '/de/legal/verfahrensdokumentation' },
        ],
        sidebar: [
          {
            text: 'Einführung', collapsed: false,
            items: [
              { text: 'Was PriorState leistet', link: '/de/guide/what-it-does' },
              { text: 'Grenzen des Nachweises', link: '/de/guide/limits' },
              { text: 'Schnellstart', link: '/de/guide/quickstart' },
              { text: 'Architektur', link: '/de/guide/architecture' },
              { text: 'Das Beweispaket', link: '/de/guide/evidence-package' },
            ],
          },
          {
            text: 'Betrieb', collapsed: false,
            items: [
              { text: 'Datenbankkonten und Updates', link: '/de/operations/database' },
              { text: 'Speicher und WORM', link: '/de/operations/storage' },
              { text: 'Zeitstempeldienst', link: '/de/operations/timestamping' },
              { text: 'Erfassungsprofile', link: '/de/operations/capture-profiles' },
              { text: 'Zusätzliche API-Quellen', link: '/de/operations/plugins' },
              { text: 'Datensicherung und Aufbewahrung', link: '/de/operations/backup' },
              { text: 'Anforderungen vor dem Start', link: '/de/operations/phase-0-requirements' },
            ],
          },
          {
            text: 'Referenz', collapsed: false,
            items: [
              { text: 'Kanonische Darstellung', link: '/de/reference/canonical-form' },
              { text: 'Konfiguration', link: '/de/reference/configuration' },
            ],
          },
          {
            text: 'Rechtliches', collapsed: false,
            items: [{ text: 'Verfahrensdokumentation', link: '/de/legal/verfahrensdokumentation' }],
          },
        ],
        outline: { level: [2, 3], label: 'Auf dieser Seite' },
        docFooter: { prev: 'Vorherige Seite', next: 'Nächste Seite' },
        lastUpdated: { text: 'Zuletzt aktualisiert', formatOptions: { dateStyle: 'medium' } },
        editLink: {
          pattern: 'https://github.com/InverterOfControl/priorstate/edit/main/docs/:path',
          text: 'Diese Seite auf GitHub bearbeiten',
        },
        footer: {
          message: 'AGPL-3.0-only. Keine Rechtsberatung.',
          copyright: 'Copyright © 2026 Sascha Laabs',
        },
        langMenuLabel: 'Sprache ändern',
        returnToTopLabel: 'Nach oben',
        sidebarMenuLabel: 'Menü',
        darkModeSwitchLabel: 'Darstellung',
        lightModeSwitchTitle: 'Helle Darstellung verwenden',
        darkModeSwitchTitle: 'Dunkle Darstellung verwenden',
        skipToContentLabel: 'Zum Inhalt springen',
      },
    },
  },
  cleanUrls: true,
  lastUpdated: true,

  // Published to GitHub Pages under /priorstate/.
  base: '/priorstate/',

  head: [['meta', { name: 'robots', content: 'index, follow' }]],

  // localhost URLs appear throughout the quickstart because that is where the software runs.
  // They are instructions, not links, and the dead-link check cannot tell the difference.
  ignoreDeadLinks: [/^https?:\/\/localhost/],

  themeConfig: {
    nav: [
      { text: 'Guide', link: '/guide/what-it-does' },
      { text: 'Operations', link: '/operations/storage' },
      { text: 'Reference', link: '/reference/canonical-form' },
      { text: 'Rechtliches (DE)', link: '/legal/verfahrensdokumentation' },
    ],

    sidebar: [
      {
        text: 'Guide',
        collapsed: false,
        items: [
          { text: 'What it does', link: '/guide/what-it-does' },
          { text: 'What it does not claim', link: '/guide/limits' },
          { text: 'Quickstart', link: '/guide/quickstart' },
          { text: 'Architecture', link: '/guide/architecture' },
          { text: 'The evidence package', link: '/guide/evidence-package' },
        ],
      },
      {
        text: 'Operations',
        collapsed: false,
        items: [
          { text: 'Database accounts and upgrades', link: '/operations/database' },
          { text: 'Storage and WORM', link: '/operations/storage' },
          { text: 'Timestamp authority', link: '/operations/timestamping' },
          { text: 'Capture profiles', link: '/operations/capture-profiles' },
          { text: 'Capture plugins', link: '/operations/plugins' },
          { text: 'Backup and retention', link: '/operations/backup' },
          { text: 'Phase 0 requirements', link: '/operations/phase-0-requirements' },
        ],
      },
      {
        text: 'Reference',
        collapsed: false,
        items: [
          { text: 'Canonical form', link: '/reference/canonical-form' },
          { text: 'Configuration', link: '/reference/configuration' },
        ],
      },
      {
        text: 'Rechtliches (Deutsch)',
        collapsed: false,
        items: [
          { text: 'Verfahrensdokumentation', link: '/legal/verfahrensdokumentation' },
        ],
      },
    ],

    socialLinks: [{ icon: 'github', link: 'https://github.com/InverterOfControl/priorstate' }],

    footer: {
      message: 'AGPL-3.0-only. Not legal advice.',
      copyright: 'Copyright © 2026 Sascha Laabs',
    },

    search: {
      provider: 'local',
      options: {
        locales: {
          de: {
            translations: {
              button: { buttonText: 'Suchen', buttonAriaLabel: 'Dokumentation durchsuchen' },
              modal: {
                displayDetails: 'Details anzeigen',
                resetButtonTitle: 'Suche zurücksetzen',
                backButtonTitle: 'Suche schließen',
                noResultsText: 'Keine Ergebnisse für',
                footer: {
                  selectText: 'Auswählen', selectKeyAriaLabel: 'Eingabetaste',
                  navigateText: 'Navigieren', navigateUpKeyAriaLabel: 'Pfeil nach oben',
                  navigateDownKeyAriaLabel: 'Pfeil nach unten',
                  closeText: 'Schließen', closeKeyAriaLabel: 'Escape',
                },
              },
            },
          },
        },
      },
    },
    outline: { level: [2, 3], label: 'On this page' },

    editLink: {
      pattern: 'https://github.com/InverterOfControl/priorstate/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
  },
})
