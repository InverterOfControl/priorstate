<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type PluginBindingSummary, type PluginSummary } from '@/lib/api'
import { readSourceConfiguration, sourceConfigurationJson, type HttpSourceConfiguration } from '@/lib/sourceConfiguration'
import { formatUtc } from '@/lib/format'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'

const props = defineProps<{ projectId: string }>()
const { t } = useI18n()
const bindings = ref<PluginBindingSummary[]>([])
const plugins = ref<PluginSummary[]>([])
const pluginId = ref('http-json')
const definition = computed(() => plugins.value.find(p => p.id === pluginId.value)?.definition)
const genericConfig = ref<Record<string, string | number | boolean>>({})
const genericJson = ref('{}')
const live = computed(() => bindings.value.filter(b => !b.supersededAt))
const history = computed(() => bindings.value.filter(b => b.supersededAt))
const editing = ref(false)
const existing = ref(false)
const busy = ref(false)
const error = ref('')
const message = ref('')
const name = ref('')
const rationale = ref('')
const required = ref(false)
const secretRef = ref('')
const config = ref<HttpSourceConfiguration>({ url: '', method: 'GET' })
const headersJson = ref('{}')
const auth = ref('none')
interface SourceTest {
  id: string; state: string; queuedAt: string; finishedAt: string | null
  sizeBytes: number | null; mediaType: string | null; error: string | null
}
const tests = ref<Record<string, SourceTest>>({})
let timer: ReturnType<typeof setInterval> | undefined
let polling = false
let disposed = false

async function load() {
  const [sources, available] = await Promise.all([
    api.get<PluginBindingSummary[]>(`/api/plugin-bindings?projectId=${props.projectId}`),
    api.get<PluginSummary[]>('/api/plugins'),
  ])
  bindings.value = sources
  plugins.value = available
}
function changePlugin() {
  genericConfig.value = {}
  genericJson.value = '{}'
  for (const field of definition.value?.fields ?? []) {
    if (field.defaultValue === null) continue
    genericConfig.value[field.name] = field.type === 'boolean' ? field.defaultValue === 'true'
      : field.type === 'number' ? Number(field.defaultValue) : field.defaultValue
  }
  secretRef.value = ''
}
function summary(binding: PluginBindingSummary) {
  if (binding.pluginId !== 'http-json') return plugins.value.find(p => p.id === binding.pluginId)?.displayName ?? binding.pluginId
  try {
    const value = readSourceConfiguration(binding.configurationJson)
    return `${value.method} ${value.url}`
  } catch { return binding.pluginId }
}
function begin(binding?: PluginBindingSummary) {
  error.value = ''
  message.value = ''
  try {
    pluginId.value = binding?.pluginId ?? (plugins.value.some(p => p.id === 'http-json') ? 'http-json' : plugins.value[0]?.id ?? '')
    changePlugin()
    if (binding && pluginId.value !== 'http-json') {
      genericConfig.value = JSON.parse(binding.configurationJson)
      genericJson.value = binding.configurationJson
    }
    config.value = binding?.pluginId === 'http-json' ? readSourceConfiguration(binding.configurationJson) : { url: '', method: 'GET' }
    headersJson.value = JSON.stringify(config.value.headers ?? {}, null, 2)
    name.value = binding?.name ?? ''
    rationale.value = binding?.rationale ?? ''
    required.value = binding?.required ?? false
    secretRef.value = binding?.secretRef ?? ''
    auth.value = config.value.authHeaderName
      ? (config.value.authHeaderName === 'Authorization' && config.value.authValuePrefix === 'Bearer ' ? 'bearer' : 'header')
      : 'none'
    existing.value = !!binding
    editing.value = true
  } catch { error.value = t('sources.invalidConfiguration') }
}
async function save() {
  busy.value = true
  error.value = ''
  try {
    const configured = { ...config.value }
    if (configured.method === 'GET') configured.body = null
    if (auth.value === 'none') { configured.authHeaderName = null; configured.authValuePrefix = null }
    if (auth.value === 'bearer') { configured.authHeaderName = 'Authorization'; configured.authValuePrefix = 'Bearer ' }
    for (const field of definition.value?.fields ?? []) {
      const value = genericConfig.value[field.name]
      if (field.type === 'number' && value !== undefined && value !== '') genericConfig.value[field.name] = Number(value)
    }
    const sourceConfig = pluginId.value === 'http-json' ? sourceConfigurationJson(configured, headersJson.value)
      : definition.value?.fields.length ? JSON.stringify(Object.fromEntries(Object.entries(genericConfig.value).filter(([, v]) => v !== '')))
      : genericJson.value
    await api.post('/api/plugin-bindings', {
      projectId: props.projectId, pluginId: pluginId.value, name: name.value.trim(), rationale: rationale.value.trim(),
      required: required.value, secretRef: pluginId.value === 'http-json' && auth.value === 'none' ? null : secretRef.value.trim() || null,
      configurationJson: sourceConfig,
    })
    editing.value = false
    message.value = t('sources.saved')
    await load()
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { busy.value = false }
}
async function test(binding: PluginBindingSummary) {
  busy.value = true
  error.value = ''
  try {
    tests.value[binding.id] = await api.post<SourceTest>(`/api/plugin-bindings/${binding.id}/test`)
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { busy.value = false }
}
async function retire(binding: PluginBindingSummary) {
  busy.value = true
  error.value = ''
  try { await api.post(`/api/plugin-bindings/${binding.id}/retire`); await load() }
  catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { busy.value = false }
}
async function poll() {
  if (polling || disposed) return
  polling = true
  try {
    for (const [bindingId, test] of Object.entries(tests.value)) {
      if (test.state !== 'Queued' && test.state !== 'Running') continue
      if (Date.now() - new Date(test.queuedAt).getTime() > 125000) {
        error.value = t('sources.workerUnavailable')
        continue
      }
      tests.value[bindingId] = await api.get<SourceTest>(`/api/plugin-bindings/tests/${test.id}`)
    }
  } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  finally { polling = false }
}
onMounted(async () => {
  try { await load() } catch (e) { error.value = e instanceof Error ? e.message : String(e) }
  if (!disposed) timer = setInterval(() => void poll(), 1500)
})
onUnmounted(() => { disposed = true; if (timer) clearInterval(timer) })
</script>

<template>
  <section class="space-y-4 border-t border-rule pt-4">
    <div class="flex items-center justify-between gap-4">
      <h2 class="text-body font-semibold">{{ t('sources.title') }}</h2>
      <Button v-if="!editing" @click="begin()">{{ t('sources.add') }}</Button>
    </div>
    <p class="text-body-sm text-ink-muted">{{ t('sources.intro') }}</p>
    <Notice v-if="error" tone="alert" role="alert">{{ error }}</Notice>
    <p v-if="message" role="status" class="text-body-sm text-ledger">{{ message }}</p>
    <form v-if="editing" class="space-y-3 rounded-sm border border-rule p-4" @submit.prevent="save">
      <p class="text-body-sm text-ink-muted">{{ t('plugins.newBindingHint') }}</p>
      <label class="block text-body-sm">{{ t('plugins.name') }}
        <input v-model="name" :readonly="existing" required maxlength="120" class="field-input mt-1" />
      </label>
      <label class="block text-body-sm">{{ t('pluginSystem.plugin') }}
        <select v-model="pluginId" :disabled="existing" required class="field-input mt-1" @change="changePlugin">
          <option v-for="plugin in plugins" :key="plugin.id" :value="plugin.id">{{ plugin.displayName }}</option>
        </select>
      </label>
      <template v-if="pluginId === 'http-json'">
      <label class="block text-body-sm">{{ t('sources.url') }}
        <input v-model="config.url" type="url" required placeholder="https://api.example.com/prices" class="field-input mt-1" />
      </label>
      <label class="block text-body-sm">{{ t('sources.method') }}
        <select v-model="config.method" class="field-input ml-3 w-auto"><option>GET</option><option>POST</option></select>
      </label>
      <p v-if="config.method === 'POST'" class="text-body-sm text-ink-muted">{{ t('sources.postNotice') }}</p>
      <label v-if="config.method === 'POST'" class="block text-body-sm">{{ t('sources.body') }}
        <textarea v-model="config.body" rows="3" class="field-input hash mt-1"></textarea>
      </label>
      <label class="block text-body-sm">{{ t('sources.authentication') }}
        <select v-model="auth" class="field-input ml-3 w-auto">
          <option value="none">{{ t('sources.noAuthentication') }}</option>
          <option value="bearer">Bearer token</option>
          <option value="header">{{ t('sources.customHeader') }}</option>
        </select>
      </label>
      <template v-if="auth !== 'none'">
        <label v-if="auth === 'header'" class="block text-body-sm">{{ t('sources.headerName') }}
          <input v-model="config.authHeaderName" required placeholder="X-API-Key" class="field-input mt-1" />
        </label>
        <label v-if="auth === 'header'" class="block text-body-sm">{{ t('sources.prefix') }}
          <input v-model="config.authValuePrefix" class="field-input mt-1" />
        </label>
        <label class="block text-body-sm">{{ t('sources.secretName') }}
          <input v-model="secretRef" required pattern="PS_SECRET_[A-Z0-9_]+" placeholder="PS_SECRET_PRICES_TOKEN" class="field-input hash mt-1" />
        </label>
        <p class="text-body-sm text-ink-muted">{{ t('sources.secretHelp') }}</p>
      </template>
      <details>
        <summary class="cursor-pointer text-body-sm">{{ t('sources.advanced') }}</summary>
        <label class="mt-2 block text-body-sm">Accept <input v-model="config.accept" placeholder="application/json" class="field-input" /></label>
        <label class="mt-2 block text-body-sm">Content-Type <input v-model="config.contentType" placeholder="application/json" class="field-input" /></label>
        <label class="mt-2 block text-body-sm">{{ t('sources.headers') }}
          <textarea v-model="headersJson" rows="3" class="field-input hash"></textarea>
        </label>
      </details>
      <p class="text-body-sm text-ink-muted">{{ t('sources.noSecrets') }}</p>
      </template>
      <template v-else>
        <p class="text-body-sm text-ink-muted">{{ definition?.description }}</p>
        <label v-for="field in definition?.fields ?? []" :key="field.name" class="block text-body-sm">
          {{ field.label }}
          <input v-if="field.type === 'boolean'" v-model="genericConfig[field.name]" type="checkbox" class="ml-2" />
          <textarea v-else-if="field.type === 'multiline'" :value="String(genericConfig[field.name] ?? '')" :required="field.required" rows="3" class="field-input mt-1" @input="genericConfig[field.name] = ($event.target as HTMLTextAreaElement).value"></textarea>
          <input v-else v-model="genericConfig[field.name]" :type="field.type === 'number' ? 'number' : 'text'" :required="field.required" class="field-input mt-1" />
          <span v-if="field.helpText" class="mt-1 block text-ink-muted">{{ field.helpText }}</span>
        </label>
        <label v-if="!definition?.fields.length" class="block text-body-sm">{{ t('pluginSystem.configuration') }}
          <textarea v-model="genericJson" rows="6" required class="field-input hash mt-1"></textarea>
        </label>
        <label v-if="definition?.secretHelp || definition?.secretRequired" class="block text-body-sm">{{ t('sources.secretName') }}
          <input v-model="secretRef" :required="definition?.secretRequired" pattern="PS_SECRET_[A-Z0-9_]+" class="field-input hash mt-1" />
          <span class="mt-1 block text-ink-muted">{{ definition?.secretHelp }}</span>
        </label>
        <p class="text-body-sm text-ink-muted">{{ t('sources.noSecrets') }}</p>
      </template>
      <label class="block text-body-sm">{{ t('plugins.rationale') }}
        <textarea v-model="rationale" required maxlength="2000" rows="2" class="field-input mt-1"></textarea>
      </label>
      <label class="flex gap-2 text-body-sm"><input v-model="required" type="checkbox" />{{ t('sources.required') }}</label>
      <div class="flex gap-2"><Button type="submit" variant="primary" :disabled="busy">{{ t('sources.save') }}</Button>
        <Button :disabled="busy" @click="editing = false">{{ t('plugins.cancel') }}</Button></div>
    </form>
    <p v-if="!live.length" class="text-body-sm text-ink-muted">{{ t('sources.empty') }}</p>
    <article v-for="binding in live" :key="binding.id" class="space-y-2 rounded-sm border border-rule p-3">
      <h3 class="text-body font-medium">{{ binding.designation }}</h3>
      <p class="break-all text-body-sm text-ink-muted">{{ summary(binding) }}</p>
      <p v-if="binding.secretRef" class="text-body-sm text-ink-muted">{{ t('sources.secretName') }}: {{ binding.secretRef }}</p>
      <p v-if="binding.required" class="text-body-sm text-ink-muted">{{ t('sources.requiredShort') }}</p>
      <details><summary class="cursor-pointer text-body-sm text-ink-muted">{{ t('sources.advanced') }}</summary>
        <pre class="mt-2 overflow-x-auto whitespace-pre-wrap break-all text-body-sm text-ink-muted">{{ binding.configurationJson }}</pre>
      </details>
      <div class="flex flex-wrap gap-2">
        <Button :disabled="busy || tests[binding.id]?.state === 'Queued' || tests[binding.id]?.state === 'Running'" @click="test(binding)">{{ t('sources.test') }}</Button>
        <Button v-if="plugins.some(p => p.id === binding.pluginId)" :disabled="busy" @click="begin(binding)">{{ t('sources.edit') }}</Button>
        <Button :disabled="busy" @click="retire(binding)">{{ t('plugins.retire') }}</Button>
      </div>
      <p class="text-body-sm text-ink-muted">{{ t('sources.testHint') }}</p>
      <div v-if="tests[binding.id]" role="status" class="text-body-sm">
        {{ t(`sources.state.${tests[binding.id]!.state}`) }}
        <span v-if="tests[binding.id]!.sizeBytes !== null"> · {{ tests[binding.id]!.sizeBytes }} bytes · {{ tests[binding.id]!.mediaType }}</span>
        <p v-if="tests[binding.id]!.error" class="text-alert">{{ tests[binding.id]!.error }}</p>
      </div>
    </article>
    <details v-if="history.length"><summary class="cursor-pointer text-body-sm">{{ t('plugins.history') }}</summary>
      <p v-for="binding in history" :key="binding.id" class="mt-2 text-body-sm text-ink-muted">{{ binding.designation }} · {{ formatUtc(binding.supersededAt!) }}</p>
    </details>
  </section>
</template>
