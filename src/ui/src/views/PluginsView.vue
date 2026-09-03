<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  api,
  type PluginBindingSummary,
  type PluginSummary,
  type ProjectSummary,
} from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import Button from '@/components/ui/Button.vue'
import DataRow from '@/components/ui/DataRow.vue'

const { t } = useI18n()

const plugins = ref<PluginSummary[]>([])
const bindings = ref<PluginBindingSummary[]>([])
const projects = ref<ProjectSummary[]>([])
const loading = ref(true)
const creating = ref(false)
const saving = ref(false)
const message = ref<string | null>(null)
const error = ref<string | null>(null)

const form = ref({
  projectId: '',
  pluginId: '',
  name: '',
  configurationJson: '{\n  "url": "https://example.internal/api/prices",\n  "method": "GET"\n}',
  secretRef: '',
  rationale: '',
  required: false,
})

// Live bindings first, superseded ones after: the history stays visible, but the question people
// actually arrive with is what is running now.
const live = computed(() => bindings.value.filter((b) => b.supersededAt === null))
const superseded = computed(() => bindings.value.filter((b) => b.supersededAt !== null))

function projectName(id: string) {
  return projects.value.find((p) => p.id === id)?.name ?? id
}

async function load() {
  const [p, b, pr] = await Promise.all([
    api.get<PluginSummary[]>('/api/plugins'),
    api.get<PluginBindingSummary[]>('/api/plugin-bindings'),
    api.get<ProjectSummary[]>('/api/projects'),
  ])
  plugins.value = p
  bindings.value = b
  projects.value = pr
}

async function create() {
  saving.value = true
  message.value = null
  error.value = null

  try {
    const created = await api.post<PluginBindingSummary>('/api/plugin-bindings', {
      ...form.value,
      secretRef: form.value.secretRef.trim() === '' ? null : form.value.secretRef.trim(),
    })
    creating.value = false
    message.value = t('plugins.saved', { designation: created.designation })
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    saving.value = false
  }
}

async function retire(binding: PluginBindingSummary) {
  message.value = null
  error.value = null

  try {
    await api.post(`/api/plugin-bindings/${binding.id}/retire`)
    message.value = t('plugins.retired', { designation: binding.designation })
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
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
      <h1 class="text-lg font-semibold tracking-tight">{{ t('plugins.title') }}</h1>
      <Button v-if="!creating && plugins.length > 0" variant="primary" @click="creating = true">
        {{ t('plugins.add') }}
      </Button>
    </div>

    <p class="max-w-3xl text-sm text-ink-muted">{{ t('plugins.intro') }}</p>

    <p v-if="error" class="rounded-md border border-broken/40 bg-broken/10 p-3 text-sm">{{ error }}</p>
    <p v-if="message" class="rounded-md border border-rule bg-paper-raised p-3 text-sm">{{ message }}</p>
    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card v-if="!loading && plugins.length === 0" :title="t('plugins.noneInstalled')">
      <p class="text-sm text-ink-muted">{{ t('plugins.noneInstalledDetail') }}</p>
    </Card>

    <!--
      A binding is never edited. Saving creates a new version and supersedes the previous one, so
      a snapshot keeps naming the configuration it actually ran under. The form says so out loud
      rather than leaving it to be discovered.
    -->
    <Card v-if="creating" :title="t('plugins.newBinding')" :subtitle="t('plugins.newBindingHint')">
      <form class="space-y-4" @submit.prevent="create">
        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.project') }}</span>
          <select v-model="form.projectId" required class="w-full rounded-md border border-rule bg-paper p-2">
            <option value="" disabled>{{ t('plugins.selectProject') }}</option>
            <option v-for="p in projects" :key="p.id" :value="p.id">{{ p.name }}</option>
          </select>
        </label>

        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.plugin') }}</span>
          <select v-model="form.pluginId" required class="w-full rounded-md border border-rule bg-paper p-2">
            <option value="" disabled>{{ t('plugins.selectPlugin') }}</option>
            <option v-for="p in plugins" :key="p.id" :value="p.id">{{ p.displayName }} ({{ p.id }})</option>
          </select>
        </label>

        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.name') }}</span>
          <input v-model="form.name" required class="w-full rounded-md border border-rule bg-paper p-2" />
        </label>

        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.configuration') }}</span>
          <textarea
            v-model="form.configurationJson"
            required
            rows="6"
            class="hash w-full rounded-md border border-rule bg-paper p-2"
          ></textarea>
          <span class="mt-1 block text-xs text-ink-muted">{{ t('plugins.configurationHint') }}</span>
        </label>

        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.secretRef') }}</span>
          <input
            v-model="form.secretRef"
            placeholder="PS_SECRET_ERP_TOKEN"
            class="hash w-full rounded-md border border-rule bg-paper p-2"
          />
          <span class="mt-1 block text-xs text-ink-muted">{{ t('plugins.secretRefHint') }}</span>
        </label>

        <label class="block text-sm">
          <span class="mb-1 block font-medium">{{ t('plugins.rationale') }}</span>
          <textarea
            v-model="form.rationale"
            required
            rows="2"
            class="w-full rounded-md border border-rule bg-paper p-2"
          ></textarea>
          <span class="mt-1 block text-xs text-ink-muted">{{ t('plugins.rationaleHint') }}</span>
        </label>

        <label class="flex items-start gap-2 text-sm">
          <input v-model="form.required" type="checkbox" class="mt-1" />
          <span>
            <span class="block font-medium">{{ t('plugins.required') }}</span>
            <span class="block text-xs text-ink-muted">{{ t('plugins.requiredHint') }}</span>
          </span>
        </label>

        <div class="flex gap-2">
          <Button type="submit" variant="primary" :disabled="saving">
            {{ saving ? t('common.loading') : t('plugins.save') }}
          </Button>
          <Button :disabled="saving" @click="creating = false">{{ t('plugins.cancel') }}</Button>
        </div>
      </form>
    </Card>

    <section v-if="!loading" class="space-y-4">
      <h2 class="text-sm font-semibold tracking-tight">{{ t('plugins.live') }}</h2>
      <p v-if="live.length === 0" class="text-sm text-ink-muted">{{ t('plugins.noneLive') }}</p>

      <Card
        v-for="binding in live"
        :key="binding.id"
        :title="binding.designation"
        :subtitle="`${binding.pluginId} · ${projectName(binding.projectId)}`"
      >
        <p class="mb-4 text-sm text-ink-muted">{{ binding.rationale }}</p>
        <dl>
          <DataRow :label="t('plugins.configuration')" mono>{{ binding.configurationJson }}</DataRow>
          <DataRow :label="t('plugins.secretRef')" mono>{{ binding.secretRef ?? '—' }}</DataRow>
          <DataRow :label="t('plugins.required')">
            {{ binding.required ? t('common.yes') : t('common.no') }}
          </DataRow>
          <DataRow :label="t('plugins.created')">{{ formatUtc(binding.createdAt) }}</DataRow>
        </dl>
        <div class="mt-4">
          <Button @click="retire(binding)">{{ t('plugins.retire') }}</Button>
        </div>
      </Card>
    </section>

    <section v-if="!loading && superseded.length > 0" class="space-y-4">
      <h2 class="text-sm font-semibold tracking-tight">{{ t('plugins.history') }}</h2>
      <p class="max-w-3xl text-sm text-ink-muted">{{ t('plugins.historyHint') }}</p>

      <Card
        v-for="binding in superseded"
        :key="binding.id"
        :title="binding.designation"
        :subtitle="`${binding.pluginId} · ${t('plugins.supersededOn', { date: formatUtc(binding.supersededAt!) })}`"
      >
        <p class="mb-4 text-sm text-ink-muted">{{ binding.rationale }}</p>
        <dl>
          <DataRow :label="t('plugins.configuration')" mono>{{ binding.configurationJson }}</DataRow>
          <DataRow :label="t('plugins.created')">{{ formatUtc(binding.createdAt) }}</DataRow>
        </dl>
      </Card>
    </section>
  </div>
</template>
