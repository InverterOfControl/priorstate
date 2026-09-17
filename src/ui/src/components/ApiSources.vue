<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type PluginBindingSummary } from '@/lib/api'
import { readSourceConfiguration, sourceConfigurationJson, type HttpSourceConfiguration } from '@/lib/sourceConfiguration'
import { formatUtc } from '@/lib/format'
import Button from '@/components/ui/Button.vue'

const props = defineProps<{ projectId: string }>()
const { t } = useI18n()
const bindings = ref<PluginBindingSummary[]>([])
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
  bindings.value = await api.get<PluginBindingSummary[]>(`/api/plugin-bindings?projectId=${props.projectId}`)
}
function summary(binding: PluginBindingSummary) {
  try {
    const value = readSourceConfiguration(binding.configurationJson)
    return `${value.method} ${value.url}`
  } catch { return binding.pluginId }
}
function begin(binding?: PluginBindingSummary) {
  error.value = ''
  message.value = ''
  try {
    config.value = binding ? readSourceConfiguration(binding.configurationJson) : { url: '', method: 'GET' }
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
    await api.post('/api/plugin-bindings', {
      projectId: props.projectId, pluginId: 'http-json', name: name.value.trim(), rationale: rationale.value.trim(),
      required: required.value, secretRef: auth.value === 'none' ? null : secretRef.value.trim(),
      configurationJson: sourceConfigurationJson(configured, headersJson.value),
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
      <h2 class="text-sm font-semibold">{{ t('sources.title') }}</h2>
      <Button v-if="!editing" @click="begin()">{{ t('sources.add') }}</Button>
    </div>
    <p class="text-sm text-ink-muted">{{ t('sources.intro') }}</p>
    <p v-if="error" role="alert" class="text-sm text-broken">{{ error }}</p>
    <p v-if="message" role="status" class="text-sm text-verified">{{ message }}</p>
    <form v-if="editing" class="space-y-3 rounded border border-rule p-4" @submit.prevent="save">
      <p class="text-xs text-ink-muted">{{ t('plugins.newBindingHint') }}</p>
      <label class="block text-sm">{{ t('plugins.name') }}
        <input v-model="name" :readonly="existing" required maxlength="120" class="mt-1 w-full rounded border border-rule bg-paper p-2" />
      </label>
      <label class="block text-sm">{{ t('sources.url') }}
        <input v-model="config.url" type="url" required placeholder="https://api.example.com/prices" class="mt-1 w-full rounded border border-rule bg-paper p-2" />
      </label>
      <label class="block text-sm">{{ t('sources.method') }}
        <select v-model="config.method" class="ml-3 rounded border border-rule bg-paper p-2"><option>GET</option><option>POST</option></select>
      </label>
      <p v-if="config.method === 'POST'" class="text-sm text-ink-muted">{{ t('sources.postNotice') }}</p>
      <label v-if="config.method === 'POST'" class="block text-sm">{{ t('sources.body') }}
        <textarea v-model="config.body" rows="3" class="hash mt-1 w-full rounded border border-rule bg-paper p-2"></textarea>
      </label>
      <label class="block text-sm">{{ t('sources.authentication') }}
        <select v-model="auth" class="ml-3 rounded border border-rule bg-paper p-2">
          <option value="none">{{ t('sources.noAuthentication') }}</option>
          <option value="bearer">Bearer token</option>
          <option value="header">{{ t('sources.customHeader') }}</option>
        </select>
      </label>
      <template v-if="auth !== 'none'">
        <label v-if="auth === 'header'" class="block text-sm">{{ t('sources.headerName') }}
          <input v-model="config.authHeaderName" required placeholder="X-API-Key" class="mt-1 w-full rounded border border-rule bg-paper p-2" />
        </label>
        <label v-if="auth === 'header'" class="block text-sm">{{ t('sources.prefix') }}
          <input v-model="config.authValuePrefix" class="mt-1 w-full rounded border border-rule bg-paper p-2" />
        </label>
        <label class="block text-sm">{{ t('sources.secretName') }}
          <input v-model="secretRef" required pattern="PS_SECRET_[A-Z0-9_]+" placeholder="PS_SECRET_PRICES_TOKEN" class="hash mt-1 w-full rounded border border-rule bg-paper p-2" />
        </label>
        <p class="text-xs text-ink-muted">{{ t('sources.secretHelp') }}</p>
      </template>
      <details>
        <summary class="cursor-pointer text-sm">{{ t('sources.advanced') }}</summary>
        <label class="mt-2 block text-sm">Accept <input v-model="config.accept" placeholder="application/json" class="w-full rounded border border-rule bg-paper p-2" /></label>
        <label class="mt-2 block text-sm">Content-Type <input v-model="config.contentType" placeholder="application/json" class="w-full rounded border border-rule bg-paper p-2" /></label>
        <label class="mt-2 block text-sm">{{ t('sources.headers') }}
          <textarea v-model="headersJson" rows="3" class="hash w-full rounded border border-rule bg-paper p-2"></textarea>
        </label>
      </details>
      <p class="text-xs text-ink-muted">{{ t('sources.noSecrets') }}</p>
      <label class="block text-sm">{{ t('plugins.rationale') }}
        <textarea v-model="rationale" required maxlength="2000" rows="2" class="mt-1 w-full rounded border border-rule bg-paper p-2"></textarea>
      </label>
      <label class="flex gap-2 text-sm"><input v-model="required" type="checkbox" />{{ t('sources.required') }}</label>
      <div class="flex gap-2"><Button type="submit" variant="primary" :disabled="busy">{{ t('sources.save') }}</Button>
        <Button :disabled="busy" @click="editing = false">{{ t('plugins.cancel') }}</Button></div>
    </form>
    <p v-if="!live.length" class="text-sm text-ink-muted">{{ t('sources.empty') }}</p>
    <article v-for="binding in live" :key="binding.id" class="space-y-2 rounded border border-rule p-3">
      <h3 class="text-sm font-medium">{{ binding.designation }}</h3>
      <p class="break-all text-sm text-ink-muted">{{ summary(binding) }}</p>
      <p v-if="binding.secretRef" class="text-xs text-ink-muted">{{ t('sources.secretName') }}: {{ binding.secretRef }}</p>
      <p v-if="binding.required" class="text-xs text-ink-muted">{{ t('sources.requiredShort') }}</p>
      <details><summary class="cursor-pointer text-xs text-ink-muted">{{ t('sources.advanced') }}</summary>
        <pre class="mt-2 overflow-x-auto whitespace-pre-wrap break-all text-xs text-ink-muted">{{ binding.configurationJson }}</pre>
      </details>
      <div class="flex flex-wrap gap-2">
        <Button :disabled="busy || tests[binding.id]?.state === 'Queued' || tests[binding.id]?.state === 'Running'" @click="test(binding)">{{ t('sources.test') }}</Button>
        <Button v-if="binding.pluginId === 'http-json'" :disabled="busy" @click="begin(binding)">{{ t('sources.edit') }}</Button>
        <Button :disabled="busy" @click="retire(binding)">{{ t('plugins.retire') }}</Button>
      </div>
      <p class="text-xs text-ink-muted">{{ t('sources.testHint') }}</p>
      <div v-if="tests[binding.id]" role="status" class="text-sm">
        {{ t(`sources.state.${tests[binding.id]!.state}`) }}
        <span v-if="tests[binding.id]!.sizeBytes !== null"> · {{ tests[binding.id]!.sizeBytes }} bytes · {{ tests[binding.id]!.mediaType }}</span>
        <p v-if="tests[binding.id]!.error" class="text-broken">{{ tests[binding.id]!.error }}</p>
      </div>
    </article>
    <details v-if="history.length"><summary class="cursor-pointer text-sm">{{ t('plugins.history') }}</summary>
      <p v-for="binding in history" :key="binding.id" class="mt-2 text-xs text-ink-muted">{{ binding.designation }} · {{ formatUtc(binding.supersededAt!) }}</p>
    </details>
  </section>
</template>
