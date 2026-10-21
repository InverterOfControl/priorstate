<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProjectSummary } from '@/lib/api'
import ApiSources from '@/components/ApiSources.vue'
import Card from '@/components/ui/Card.vue'
import Notice from '@/components/ui/Notice.vue'
import Button from '@/components/ui/Button.vue'

const { t } = useI18n()
const projects = ref<ProjectSummary[]>([])
const selected = ref('')
const error = ref('')
interface PackageState {
  canInstall: boolean; callerId: string; restartRequired: boolean; mismatch: boolean; workerAvailable: boolean
  installed: Array<{ id: string; sha256: string }>; loaded: Array<{ id: string; sha256: string }>
}
const packages = ref<PackageState>()
const file = ref<File>()
const uploading = ref(false)
let timer: ReturnType<typeof setInterval> | undefined
async function refreshPackages() {
  try { packages.value = await api.get<PackageState>('/api/plugins/packages') }
  catch (e) { error.value = e instanceof Error ? e.message : String(e) }
}
async function install() {
  if (!file.value) return
  uploading.value = true
  error.value = ''
  try { await api.uploadPlugin(file.value); await refreshPackages() }
  catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { uploading.value = false }
}
function selectFile(event: Event) { file.value = (event.target as HTMLInputElement).files?.[0] }
onMounted(async () => {
  try {
    projects.value = await api.get<ProjectSummary[]>('/api/projects')
    selected.value = projects.value[0]?.id ?? ''
    await refreshPackages()
    timer = setInterval(() => void refreshPackages(), 30000)
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
})
onUnmounted(() => { if (timer) clearInterval(timer) })
</script>
<template>
  <div class="space-y-5">
    <h1 class="font-serif text-title">{{ t('sources.title') }}</h1>
    <Notice v-if="error" tone="alert" role="alert">{{ error }}</Notice>
    <Card v-if="packages" class="space-y-3">
      <h2 class="text-body font-semibold">{{ t('pluginSystem.packages') }}</h2>
      <Notice v-if="packages.restartRequired" role="status">{{ t('pluginSystem.restart') }}</Notice>
      <Notice v-if="packages.mismatch" tone="alert" role="alert">{{ t('pluginSystem.mismatch') }}</Notice>
      <p v-if="!packages.workerAvailable" class="text-body-sm text-ink-muted">{{ t('pluginSystem.workerUnknown') }}</p>
      <ul class="space-y-2">
        <li v-for="plugin in packages.loaded" :key="plugin.id" class="text-body-sm">
          <span class="font-medium">{{ plugin.id }}</span>
          <span class="hash block break-all text-ink-muted">{{ plugin.sha256 }}</span>
        </li>
      </ul>
      <div v-if="packages.installed.some(p => !packages?.loaded.some(l => l.id === p.id && l.sha256 === p.sha256))" class="text-body-sm">
        <p>{{ t('pluginSystem.pending') }}</p>
        <p v-for="plugin in packages.installed.filter(p => !packages?.loaded.some(l => l.id === p.id && l.sha256 === p.sha256))" :key="plugin.id" class="hash break-all">{{ plugin.id }} · {{ plugin.sha256 }}</p>
      </div>
      <div v-if="packages.canInstall" class="space-y-2">
        <p class="text-body-sm text-ink-muted">{{ t('pluginSystem.trust') }}</p>
        <label class="block text-body-sm">{{ t('pluginSystem.packageFile') }}
          <input type="file" accept=".psplugin" class="mt-1 block" @change="selectFile" />
        </label>
        <Button :disabled="!file || uploading" @click="install">{{ t('pluginSystem.install') }}</Button>
      </div>
      <p v-else class="text-body-sm text-ink-muted">{{ t('pluginSystem.operatorOnly') }}</p>
      <p class="text-body-sm text-ink-muted">{{ t('pluginSystem.identity') }}: <span class="hash">{{ packages.callerId }}</span></p>
    </Card>
    <label class="block text-body-sm">{{ t('plugins.project') }}
      <select v-model="selected" class="field-input ml-3 w-auto">
        <option value="" disabled>{{ t('plugins.selectProject') }}</option>
        <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
      </select>
    </label>
    <p v-if="!projects.length" class="text-body-sm text-ink-muted">{{ t('projects.none') }}</p>
    <Card v-if="selected"><ApiSources :key="selected" :project-id="selected" /></Card>
  </div>
</template>
