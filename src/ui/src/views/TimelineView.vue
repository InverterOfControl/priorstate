<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type SnapshotSummary } from '@/lib/api'
import { formatUtc, shortHash } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'

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
    <h1 class="text-lg font-semibold tracking-tight">{{ t('nav.timeline') }}</h1>

    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-else>
      <ol class="divide-y divide-rule">
        <li v-for="snapshot in snapshots" :key="snapshot.id" class="py-3 first:pt-0 last:pb-0">
          <RouterLink :to="`/snapshots/${snapshot.id}`" class="group flex items-baseline gap-4">
            <span class="w-14 shrink-0 text-xs tabular-nums text-ink-muted">#{{ snapshot.chainSequence }}</span>
            <span class="w-48 shrink-0 text-xs tabular-nums text-ink-muted">
              {{ formatUtc(snapshot.capturedAtUtc) }}
            </span>
            <span class="flex-1 truncate text-sm group-hover:underline">{{ snapshot.url }}</span>
            <!-- A plugin entry is not a page capture, and the timeline should not imply it is. -->
            <span
              v-if="snapshot.plugin"
              class="shrink-0 rounded border border-rule px-1.5 py-0.5 text-xs text-ink-muted"
            >
              {{ snapshot.plugin }}
            </span>
            <span class="hash hidden shrink-0 text-ink-muted md:inline">{{ shortHash(snapshot.entryHash) }}</span>
            <span v-if="!snapshot.timestamped" class="shrink-0 text-xs text-caution">
              {{ t('timestamp.pending') }}
            </span>
            <IntegrityBadge :worm="snapshot.storageWorm" />
          </RouterLink>
        </li>
      </ol>
      <p v-if="snapshots.length === 0" class="text-sm text-ink-muted">{{ t('projects.none') }}</p>
    </Card>
  </div>
</template>
