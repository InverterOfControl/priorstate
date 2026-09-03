<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type AuditEntry } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'

const { t } = useI18n()
const entries = ref<AuditEntry[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    entries.value = await api.get<AuditEntry[]>('/api/audit?take=300')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-lg font-semibold tracking-tight">{{ t('audit.title') }}</h1>
    <p class="text-sm text-ink-muted">{{ t('audit.note') }}</p>

    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-else>
      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="text-left text-xs text-ink-muted">
            <tr class="border-b border-rule">
              <th class="pb-2 font-medium">{{ t('audit.when') }}</th>
              <th class="pb-2 font-medium">{{ t('audit.who') }}</th>
              <th class="pb-2 font-medium">{{ t('audit.action') }}</th>
              <th class="pb-2 font-medium">{{ t('audit.subject') }}</th>
              <th class="pb-2 font-medium">{{ t('audit.detail') }}</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-rule">
            <tr v-for="entry in entries" :key="entry.id">
              <td class="py-2 whitespace-nowrap tabular-nums text-ink-muted">
                {{ formatUtc(entry.occurredAtUtc) }}
              </td>
              <td class="py-2">{{ entry.userName ?? 'system' }}</td>
              <td class="py-2">{{ entry.action }}</td>
              <td class="py-2 text-ink-muted">{{ entry.subjectType }}</td>
              <td class="py-2 text-ink-muted">{{ entry.detail }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </Card>
  </div>
</template>
