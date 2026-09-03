import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'PriorState',
  description: 'Tamper-evident website archiving for use as evidence',
  lang: 'en-GB',
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
      { text: 'GitHub', link: 'https://github.com/InverterOfControl/priorstate' },
    ],

    sidebar: [
      {
        text: 'Guide',
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
        items: [
          { text: 'Storage and WORM', link: '/operations/storage' },
          { text: 'Timestamp authority', link: '/operations/timestamping' },
          { text: 'Capture profiles', link: '/operations/capture-profiles' },
          { text: 'Backup and retention', link: '/operations/backup' },
          { text: 'Phase 0 requirements', link: '/operations/phase-0-requirements' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Canonical form', link: '/reference/canonical-form' },
          { text: 'Configuration', link: '/reference/configuration' },
        ],
      },
      {
        text: 'Rechtliches (Deutsch)',
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

    search: { provider: 'local' },

    editLink: {
      pattern: 'https://github.com/InverterOfControl/priorstate/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
  },
})
