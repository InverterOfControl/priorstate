<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type RunSummary } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'

const { t } = useI18n()
const runs = ref<RunSummary[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    runs.value = await api.get<RunSummary[]>('/api/runs')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-lg font-semibold tracking-tight">{{ t('runs.title') }}</h1>
    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-else>
      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="text-left text-xs text-ink-muted">
            <tr class="border-b border-rule">
              <th class="pb-2 font-medium">{{ t('runs.queued') }}</th>
              <th class="pb-2 font-medium">{{ t('runs.trigger') }}</th>
              <th class="pb-2 font-medium">{{ t('runs.status') }}</th>
              <th class="pb-2 font-medium">{{ t('snapshot.profile') }}</th>
              <th class="pb-2 text-right font-medium">{{ t('runs.snapshots') }}</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-rule">
            <tr v-for="run in runs" :key="run.id">
              <td class="py-2 whitespace-nowrap tabular-nums text-ink-muted">{{ formatUtc(run.queuedAt) }}</td>
              <td class="py-2">{{ run.trigger }}</td>
              <td class="py-2" :class="run.status === 'Failed' ? 'text-broken' : ''">
                {{ run.status }}
                <span v-if="run.failureReason" class="block text-xs text-ink-muted">{{ run.failureReason }}</span>
              </td>
              <td class="py-2 text-ink-muted">{{ run.captureProfile }}</td>
              <td class="py-2 text-right tabular-nums">{{ run.snapshotCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </Card>
  </div>
</template>
