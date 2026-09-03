import { createI18n } from 'vue-i18n'
import en from './locales/en.json'
import de from './locales/de.json'

/**
 * English is the default and the fallback; German is the second locale because the legal
 * material this tool is built around — Verfahrensdokumentation, eIDAS, GoBD — is German. Adding
 * a locale means adding a JSON file here and nothing else.
 */
export default createI18n({
  legacy: false,
  locale: localStorage.getItem('priorstate.locale') ?? 'en',
  fallbackLocale: 'en',
  messages: { en, de },
})
