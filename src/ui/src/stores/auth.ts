import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, ApiError } from '@/lib/api'

export interface AuthStatus {
  authenticated: boolean
  userName: string | null
  hasUsers: boolean
}

/**
 * Session state for the interface.
 *
 * Authentication is a cookie issued by ASP.NET Core Identity, so there is no token to hold here —
 * the browser sends it and the server decides. What this store tracks is only what the interface
 * needs in order to render: whether we are signed in, who as, and whether the instance has been
 * set up yet.
 */
export const useAuthStore = defineStore('auth', () => {
  const authenticated = ref(false)
  const userName = ref<string | null>(null)
  /** False only on a brand-new instance, which is what turns the sign-in form into first-run setup. */
  const hasUsers = ref(true)
  const resolved = ref(false)

  async function refresh(): Promise<void> {
    try {
      const status = await api.get<AuthStatus>('/api/auth/status')
      authenticated.value = status.authenticated
      userName.value = status.userName
      hasUsers.value = status.hasUsers
    } catch {
      // The API is unreachable or still starting. Treat it as signed out; the router guard sends
      // the user to the sign-in page, which is the right place to see the failure.
      authenticated.value = false
      userName.value = null
    } finally {
      resolved.value = true
    }
  }

  async function signIn(email: string, password: string): Promise<void> {
    // useCookies=true asks Identity for a cookie rather than a bearer token. A cookie is the right
    // choice here: it is HttpOnly, so a script cannot read it, and the browser attaches it to the
    // archive and evidence-package downloads that happen outside fetch().
    await api.post('/api/auth/login?useCookies=true', { email, password })
    await refresh()
  }

  async function register(email: string, password: string): Promise<void> {
    await api.post('/api/auth/register', { email, password })
    await signIn(email, password)
  }

  async function signOut(): Promise<void> {
    try {
      await api.post('/api/auth/logout')
    } catch (error) {
      // A session that is already gone is not a failure worth blocking sign-out over.
      if (!(error instanceof ApiError) || error.status !== 401) {
        throw error
      }
    }
    authenticated.value = false
    userName.value = null
  }

  /** Called by the API client when any request comes back 401, so the UI cannot show stale state. */
  function markSignedOut(): void {
    authenticated.value = false
    userName.value = null
  }

  return { authenticated, userName, hasUsers, resolved, refresh, signIn, register, signOut, markSignedOut }
})
