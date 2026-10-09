<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type RunSummary, type RunSources } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'
import Timestamp from '@/components/ui/Timestamp.vue'

const { t } = useI18n()
const runs = ref<RunSummary[]>([])
const loading = ref(true)
const error = ref('')
const open = ref<Record<string, boolean>>({})
const results = ref<Record<string, RunSources>>({})
let timer: ReturnType<typeof setInterval> | undefined
let refreshing = false
let disposed = false
async function load() {
  if (refreshing) return
  refreshing = true
  try {
    runs.value = await api.get<RunSummary[]>('/api/runs')
    for (const id of Object.keys(open.value).filter(id => open.value[id]))
      results.value[id] = await api.get<RunSources>(`/api/runs/${id}/sources`)
    error.value = ''
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { loading.value = false; refreshing = false }
}
async function toggle(id: string) {
  open.value[id] = !open.value[id]
  if (open.value[id]) {
    try { results.value[id] = await api.get<RunSources>(`/api/runs/${id}/sources`) }
    catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  }
}
onMounted(async () => { await load(); if (!disposed) timer = setInterval(() => void load(), 5000) })
onUnmounted(() => { disposed = true; if (timer) clearInterval(timer) })
</script>

<template>
  <div class="space-y-6">
    <h1 class="font-serif text-title">{{ t('runs.title') }}</h1>
    <Notice v-if="error" tone="alert" role="alert">{{ error }}</Notice>
    <p v-if="loading" class="text-body-sm text-ink-muted">{{ t('common.loading') }}</p>
    <Card v-for="run in runs" :key="run.id">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p class="flex flex-wrap items-baseline gap-x-2 text-body font-medium"><Timestamp :value="run.queuedAt" /> <span>{{ t(`sources.state.${run.status}`) }}</span></p>
          <p class="text-body-sm text-ink-muted">{{ run.trigger }} · {{ run.captureProfile }} · {{ run.snapshotCount }} {{ t('runs.snapshots') }}</p>
        </div>
        <Button @click="toggle(run.id)">{{ t('sources.results') }}</Button>
      </div>
      <p v-if="run.failureReason" class="mt-2 text-body-sm text-alert">{{ run.failureReason }}</p>
      <ul v-if="run.pluginFailures?.length" class="mt-2 list-inside list-disc text-body-sm text-alert">
        <li v-for="failure in run.pluginFailures" :key="failure">{{ failure }}</li>
      </ul>
      <div v-if="open[run.id] && results[run.id]" class="mt-4 space-y-4 border-t border-rule pt-3">
        <section>
          <h2 class="text-body font-semibold">{{ t('sources.website') }}</h2>
          <p v-if="!results[run.id]!.snapshots.some(s => !s.bindingId)" class="text-body-sm text-ink-muted">{{ t('sources.noWebsiteYet') }}</p>
          <RouterLink v-for="snapshot in results[run.id]!.snapshots.filter(s => !s.bindingId)" :key="snapshot.id"
            :to="`/snapshots/${snapshot.id}`" class="mt-1 block break-all text-body-sm text-ledger underline-offset-3 hover:underline">
            #{{ snapshot.chainSequence }} · {{ snapshot.url }} · {{ formatUtc(snapshot.capturedAtUtc) }}
          </RouterLink>
        </section>
        <section>
          <h2 class="text-body font-semibold">{{ t('sources.title') }}</h2>
          <p v-if="!results[run.id]!.sources.length" class="text-body-sm text-ink-muted">{{ t('sources.noResultsYet') }}</p>
          <article v-for="source in results[run.id]!.sources" :key="source.id" class="mt-2 rounded-sm border border-rule p-3 text-body-sm">
            <p>{{ source.name }} · {{ t(`sources.state.${source.state}`) }} <span v-if="source.required">· {{ t('sources.requiredShort') }}</span></p>
            <p v-if="source.startedAt" class="text-body-sm text-ink-muted">{{ t('sources.started') }}: {{ formatUtc(source.startedAt) }}</p>
            <p v-if="source.finishedAt" class="text-body-sm text-ink-muted">{{ t('sources.finished') }}: {{ formatUtc(source.finishedAt) }}<span v-if="source.sizeBytes !== null"> · {{ source.sizeBytes }} bytes</span></p>
            <p v-if="source.error" class="text-alert">{{ source.error }}</p>
            <RouterLink v-if="source.snapshotId" :to="`/snapshots/${source.snapshotId}`" class="text-ledger underline-offset-3 hover:underline">{{ t('sources.response') }}</RouterLink>
          </article>
          <!-- Older runs predate structured source results; their ledger snapshots remain accessible. -->
          <RouterLink v-for="snapshot in results[run.id]!.snapshots.filter(s => s.bindingId && !results[run.id]!.sources.some(e => e.snapshotId === s.id))"
            :key="snapshot.id" :to="`/snapshots/${snapshot.id}`" class="mt-2 block text-body-sm text-ledger underline-offset-3 hover:underline">{{ snapshot.url }} · {{ formatUtc(snapshot.capturedAtUtc) }}</RouterLink>
        </section>
      </div>
    </Card>
  </div>
</template>
