<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProjectSummary } from '@/lib/api'
import Card from '@/components/ui/Card.vue'
import Button from '@/components/ui/Button.vue'
import DataRow from '@/components/ui/DataRow.vue'

const { t } = useI18n()
const projects = ref<ProjectSummary[]>([])
const loading = ref(true)
const triggering = ref<string | null>(null)

async function trigger(projectId: string) {
  triggering.value = projectId
  try {
    await api.post('/api/runs', { projectId })
  } finally {
    triggering.value = null
  }
}

onMounted(async () => {
  try {
    projects.value = await api.get<ProjectSummary[]>('/api/projects')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-lg font-semibold tracking-tight">{{ t('projects.title') }}</h1>

    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>
    <p v-else-if="projects.length === 0" class="text-sm text-ink-muted">{{ t('projects.none') }}</p>

    <Card v-for="project in projects" :key="project.id" :title="project.name">
      <dl>
        <DataRow :label="t('projects.seeds')">
          <span v-for="seed in project.seedUrls" :key="seed" class="block">{{ seed }}</span>
        </DataRow>
        <DataRow :label="t('projects.schedule')">{{ project.schedule ?? t('projects.onDemand') }}</DataRow>
        <DataRow :label="t('projects.retention')">
          {{ t('projects.years', { count: project.retentionYears }) }}
        </DataRow>
        <DataRow :label="t('snapshot.profile')">{{ project.captureProfile }}</DataRow>
      </dl>
      <div class="mt-4">
        <Button variant="primary" :disabled="triggering === project.id" @click="trigger(project.id)">
          {{ t('projects.trigger') }}
        </Button>
      </div>
    </Card>
  </div>
</template>
