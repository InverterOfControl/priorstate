<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type LedgerStatus } from '@/lib/api'

const { t, locale, availableLocales } = useI18n()
const status = ref<LedgerStatus | null>(null)

const nav = [
  { name: 'ledger', to: '/ledger' },
  { name: 'projects', to: '/projects' },
  { name: 'timeline', to: '/timeline' },
  { name: 'runs', to: '/runs' },
  { name: 'profiles', to: '/profiles' },
  { name: 'audit', to: '/audit' },
] as const

function switchLocale(value: string) {
  locale.value = value
  localStorage.setItem('priorstate.locale', value)
}

onMounted(async () => {
  try {
    status.value = await api.get<LedgerStatus>('/api/ledger/status')
  } catch {
    // Not signed in yet, or the API is still starting. The banner simply does not appear.
  }
})
</script>

<template>
  <div class="min-h-screen">
    <header class="border-b border-rule bg-paper-raised">
      <div class="mx-auto flex max-w-6xl items-baseline gap-6 px-6 py-3">
        <RouterLink to="/" class="font-semibold tracking-tight">{{ t('app.name') }}</RouterLink>
        <nav class="flex flex-1 gap-1 text-sm">
          <RouterLink
            v-for="item in nav"
            :key="item.name"
            :to="item.to"
            class="rounded px-2.5 py-1 text-ink-muted transition-colors hover:bg-paper hover:text-ink"
            active-class="bg-paper text-ink"
          >
            {{ t(`nav.${item.name}`) }}
          </RouterLink>
        </nav>
        <div class="flex gap-1 text-xs">
          <button
            v-for="value in availableLocales"
            :key="value"
            type="button"
            class="rounded px-1.5 py-0.5 uppercase transition-colors"
            :class="locale === value ? 'text-ink' : 'text-ink-muted hover:text-ink'"
            @click="switchLocale(value)"
          >
            {{ value }}
          </button>
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
      class="border-b border-caution/30 bg-caution/10 px-6 py-2 text-center text-xs text-caution"
    >
      {{ t('timestamp.unqualifiedWarning') }}
    </div>

    <main class="mx-auto max-w-6xl px-6 py-8">
      <RouterView />
    </main>

    <footer class="mx-auto max-w-6xl px-6 pb-10 text-xs text-ink-muted">
      {{ t('app.name') }} — {{ t('app.tagline') }} · AGPL-3.0-only
    </footer>
  </div>
</template>
