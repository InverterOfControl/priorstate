import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/ledger' },
  { path: '/ledger', name: 'ledger', component: () => import('@/views/LedgerView.vue') },
  { path: '/projects', name: 'projects', component: () => import('@/views/ProjectsView.vue') },
  { path: '/timeline', name: 'timeline', component: () => import('@/views/TimelineView.vue') },
  { path: '/snapshots/:id', name: 'snapshot', component: () => import('@/views/SnapshotView.vue'), props: true },
  { path: '/runs', name: 'runs', component: () => import('@/views/RunsView.vue') },
  { path: '/profiles', name: 'profiles', component: () => import('@/views/ProfilesView.vue') },
  { path: '/audit', name: 'audit', component: () => import('@/views/AuditView.vue') },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
