import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import i18n from './i18n'
import { setUnauthorizedHandler } from './lib/api'
import { useAuthStore } from './stores/auth'
import './style.css'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia).use(router).use(i18n)

// Any request that comes back 401 — an expired cookie, a session ended elsewhere — drops the
// interface back to the sign-in page rather than leaving a signed-in shell over failing requests.
const auth = useAuthStore(pinia)
setUnauthorizedHandler(() => {
  auth.markSignedOut()
  if (router.currentRoute.value.name !== 'login') {
    const current = router.currentRoute.value.fullPath
    void router.replace({ name: 'login', query: current === '/ledger' ? {} : { next: current } })
  }
})

app.mount('#app')
