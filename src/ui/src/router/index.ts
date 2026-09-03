import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/ledger' },
  { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
  { path: '/ledger', name: 'ledger', component: () => import('@/views/LedgerView.vue') },
  { path: '/projects', name: 'projects', component: () => import('@/views/ProjectsView.vue') },
  { path: '/timeline', name: 'timeline', component: () => import('@/views/TimelineView.vue') },
  { path: '/snapshots/:id', name: 'snapshot', component: () => import('@/views/SnapshotView.vue'), props: true },
  { path: '/runs', name: 'runs', component: () => import('@/views/RunsView.vue') },
  { path: '/profiles', name: 'profiles', component: () => import('@/views/ProfilesView.vue') },
  { path: '/audit', name: 'audit', component: () => import('@/views/AuditView.vue') },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

/**
 * Every route except the sign-in page requires a session. The API enforces this independently —
 * the guard exists so a signed-out visitor sees a sign-in form instead of a page full of failed
 * requests, not as the access control itself.
 */
router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // One status call per page load, not per navigation.
  if (!auth.resolved) {
    await auth.refresh()
  }

  if (to.meta.public) {
    return auth.authenticated && to.name === 'login' ? '/ledger' : true
  }

  if (!auth.authenticated) {
    // Remember where they were headed so sign-in can put them back there.
    return { name: 'login', query: to.fullPath === '/ledger' ? {} : { next: to.fullPath } }
  }

  return true
})

export default router
