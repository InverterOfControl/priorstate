<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { api, type LedgerStatus } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'

const { t, locale, availableLocales } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const status = ref<LedgerStatus | null>(null)

const nav = [
  { name: 'ledger', to: '/ledger' },
  { name: 'projects', to: '/projects' },
  { name: 'timeline', to: '/timeline' },
  { name: 'runs', to: '/runs' },
  { name: 'profiles', to: '/profiles' },
  { name: 'plugins', to: '/plugins' },
  { name: 'audit', to: '/audit' },
] as const

function switchLocale(value: string) {
  locale.value = value
  localStorage.setItem('priorstate.locale', value)
}

async function signOut() {
  await auth.signOut()
  await router.replace({ name: 'login' })
}

async function loadStatus() {
  if (!auth.authenticated) {
    status.value = null
    return
  }

  try {
    status.value = await api.get<LedgerStatus>('/api/ledger/status')
  } catch {
    // The banner simply does not appear; the page itself will surface the error.
  }
}

watch(() => auth.authenticated, loadStatus)
onMounted(loadStatus)
</script>

<template>
  <div class="min-h-screen">
    <header class="app-masthead border-b">
      <div class="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3 sm:px-8">
        <RouterLink to="/" class="inline-flex shrink-0 items-center gap-2.5 font-serif text-[22px] leading-7 font-medium tracking-[-0.01em]">
          <svg class="h-8 w-8 shrink-0 text-masthead-accent" viewBox="0 0 64 64" fill="none" aria-hidden="true">
            <path d="M23 43H7V7h36v16M27 27h30v30H27z" stroke="currentColor" stroke-width="4" stroke-linecap="square" stroke-linejoin="miter" />
          </svg>
          <span>{{ t('app.name') }}</span>
        </RouterLink>

        <nav v-if="auth.authenticated" :aria-label="t('app.name')" class="order-last -mx-2 flex w-full gap-1 overflow-x-auto py-1 text-sm xl:order-none xl:mx-0 xl:w-auto xl:flex-1">
          <RouterLink
            v-for="item in nav"
            :key="item.name"
            :to="item.to"
            class="inline-flex min-h-11 items-center rounded-sm px-2.5 py-2 whitespace-nowrap text-masthead-muted transition-colors hover:bg-masthead-active hover:text-masthead-ink"
            active-class="bg-masthead-active !text-masthead-accent font-medium"
          >
            {{ t(`nav.${item.name}`) }}
          </RouterLink>
        </nav>
        <span v-else class="flex-1" />

        <div class="ml-auto flex min-w-0 flex-wrap items-center justify-end gap-x-3 gap-y-1 text-body-sm">
          <template v-if="auth.authenticated">
            <span class="max-w-48 truncate text-masthead-muted" :title="auth.userName ?? undefined">{{ auth.userName }}</span>
            <button type="button" class="min-h-11 text-masthead-muted transition-colors hover:text-masthead-ink" @click="signOut">
              {{ t('auth.signOut') }}
            </button>
          </template>

          <div class="flex gap-1">
            <button
              v-for="value in availableLocales"
              :key="value"
              type="button"
              :aria-pressed="locale === value"
              class="min-h-11 min-w-11 rounded-xs px-2 py-2 type-label transition-colors"
              :class="locale === value ? 'bg-masthead-active text-masthead-accent' : 'text-masthead-muted hover:bg-masthead-active hover:text-masthead-ink'"
              @click="switchLocale(value)"
            >
              {{ value }}
            </button>
          </div>
        </div>
      </div>
    </header>

    <!--
      The single most consequential thing an operator can get wrong is leaving the default
      demonstration timestamp authority in place and only discovering it when a package is
      challenged. So it is stated on every page, not tucked into a settings screen.
    -->
    <div
      v-if="status && status.timestampAnchors > 0 && !status.lastAnchorQualified"
      role="alert"
      class="border-b border-rule bg-brass-soft text-body-sm text-brass"
    >
      <div class="mx-auto max-w-6xl px-4 py-3 sm:px-8">
        {{ t('timestamp.unqualifiedWarning') }}
      </div>
    </div>

    <main class="mx-auto max-w-6xl px-4 py-8 sm:px-8">
      <RouterView />
    </main>

    <footer class="mx-auto max-w-6xl px-4 pb-10 text-body-sm text-ink-muted sm:px-8">
      <div class="border-t border-rule pt-5">
        {{ t('app.name') }} — {{ t('app.tagline') }} · AGPL-3.0-only
      </div>
    </footer>
  </div>
</template>
