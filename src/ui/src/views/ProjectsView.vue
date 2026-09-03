<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProjectSummary } from '@/lib/api'
import Card from '@/components/ui/Card.vue'
import Button from '@/components/ui/Button.vue'
import DataRow from '@/components/ui/DataRow.vue'
import ProjectForm from '@/components/ProjectForm.vue'

const { t } = useI18n()
const projects = ref<ProjectSummary[]>([])
const loading = ref(true)
const creating = ref(false)
const triggering = ref<string | null>(null)
const message = ref<string | null>(null)
const error = ref<string | null>(null)

async function load() {
  projects.value = await api.get<ProjectSummary[]>('/api/projects')
}

async function trigger(project: ProjectSummary) {
  triggering.value = project.id
  message.value = null
  error.value = null

  try {
    await api.post('/api/runs', { projectId: project.id })
    // The crawl is queued, not finished — the worker picks it up and it can take minutes. Saying
    // so avoids the impression that nothing happened.
    message.value = t('projects.queued', { name: project.name })
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    triggering.value = null
  }
}

function onCreated(project: ProjectSummary) {
  creating.value = false
  message.value = t('projects.created', { name: project.name })
  void load()
}

onMounted(async () => {
  try {
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-baseline justify-between gap-4">
      <h1 class="text-lg font-semibold tracking-tight">{{ t('projects.title') }}</h1>
      <Button v-if="!creating" variant="primary" @click="creating = true">
        {{ t('projects.new') }}
      </Button>
    </div>

    <p v-if="message" class="rounded-md border border-verified/40 bg-verified/10 px-4 py-2 text-sm text-verified">
      {{ message }}
    </p>
    <p v-if="error" class="rounded-md border border-broken/40 bg-broken/10 px-4 py-2 text-sm text-broken">
      {{ error }}
    </p>

    <Card v-if="creating" :title="t('projects.new')">
      <ProjectForm @created="onCreated" @cancel="creating = false" />
    </Card>

    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-else-if="projects.length === 0 && !creating">
      <p class="text-sm text-ink-muted">{{ t('projects.none') }}</p>
      <div class="mt-4">
        <Button variant="primary" @click="creating = true">{{ t('projects.new') }}</Button>
      </div>
    </Card>

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
        <Button variant="primary" :disabled="triggering === project.id" @click="trigger(project)">
          {{ triggering === project.id ? t('auth.working') : t('projects.trigger') }}
        </Button>
      </div>
    </Card>
  </div>
</template>
