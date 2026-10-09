<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type SnapshotSummary } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'
import Hash from '@/components/ui/Hash.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'

const { t } = useI18n()
const snapshots = ref<SnapshotSummary[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    snapshots.value = await api.get<SnapshotSummary[]>('/api/snapshots?take=200')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="font-serif text-title">{{ t('nav.timeline') }}</h1>

    <p v-if="loading" class="text-body-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-else>
      <ol class="divide-y divide-rule">
        <li v-for="snapshot in snapshots" :key="snapshot.id" class="py-3 first:pt-0 last:pb-0">
          <RouterLink :to="`/snapshots/${snapshot.id}`" class="group flex flex-wrap items-center gap-x-4 gap-y-1 md:flex-nowrap">
            <span class="w-14 shrink-0 font-mono text-mono-sm text-ink-muted tabular-nums">#{{ snapshot.chainSequence }}</span>
            <span class="shrink-0 text-body-sm text-ink-muted md:w-72">
              {{ t(snapshot.plugin ? 'snapshot.captured' : 'snapshot.crawlStarted') }}: <span class="font-mono text-mono-sm text-brass tabular-nums">{{ formatUtc(snapshot.capturedAtUtc) }}</span>
            </span>
            <span class="min-w-0 flex-1 basis-full truncate text-body-sm underline-offset-3 group-hover:text-ledger group-hover:underline md:basis-auto">{{ t(snapshot.plugin ? 'snapshot.url' : 'snapshot.seedUrl') }}: {{ snapshot.url }}</span>
            <!-- A plugin entry is not a page capture, and the timeline should not imply it is. -->
            <span
              v-if="snapshot.plugin"
              class="shrink-0 rounded-xs bg-paper-sunken px-1.5 py-0.5 font-mono text-mono-sm text-ink-muted"
            >
              {{ snapshot.plugin }}
            </span>
            <Hash :value="snapshot.entryHash" :head="12" :tail="8" :copyable="false" class="hidden shrink-0 md:inline-flex" />
            <StatusBadge v-if="!snapshot.timestamped" status="unsealed" class="shrink-0">
              {{ t('timestamp.pending') }}
            </StatusBadge>
            <IntegrityBadge :worm="snapshot.storageWorm" />
          </RouterLink>
        </li>
      </ol>
      <p v-if="snapshots.length === 0" class="text-body-sm text-ink-muted">{{ t('projects.none') }}</p>
    </Card>
  </div>
</template>
