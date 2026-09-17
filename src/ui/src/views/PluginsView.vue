<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProjectSummary } from '@/lib/api'
import ApiSources from '@/components/ApiSources.vue'
import Card from '@/components/ui/Card.vue'

const { t } = useI18n()
const projects = ref<ProjectSummary[]>([])
const selected = ref('')
const error = ref('')
onMounted(async () => {
  try {
    projects.value = await api.get<ProjectSummary[]>('/api/projects')
    selected.value = projects.value[0]?.id ?? ''
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
})
</script>
<template>
  <div class="space-y-5">
    <h1 class="text-lg font-semibold">{{ t('sources.title') }}</h1>
    <p v-if="error" role="alert" class="text-broken">{{ error }}</p>
    <label class="block text-sm">{{ t('plugins.project') }}
      <select v-model="selected" class="ml-3 rounded border border-rule bg-paper p-2">
        <option value="" disabled>{{ t('plugins.selectProject') }}</option>
        <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
      </select>
    </label>
    <p v-if="!projects.length" class="text-sm text-ink-muted">{{ t('projects.none') }}</p>
    <Card v-if="selected"><ApiSources :key="selected" :project-id="selected" /></Card>
  </div>
</template>
