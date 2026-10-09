<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProjectSummary } from '@/lib/api'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'

interface ProfileOption {
  id: string
  name: string
  version: number
  supersededAt: string | null
}

const emit = defineEmits<{ created: [ProjectSummary]; cancel: [] }>()
const { t } = useI18n()

const name = ref('')
const seedUrls = ref('')
const scopeIncludes = ref('')
const scopeExcludes = ref('')
const schedule = ref('')
const retentionYears = ref(6)
const captureProfileVersionId = ref<string>('')

const profiles = ref<ProfileOption[]>([])
const busy = ref(false)
const error = ref<string | null>(null)

/** One entry per non-empty line, trimmed. Lets people paste a list without fighting a tag widget. */
function lines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
}

const seeds = computed(() => lines(seedUrls.value))

async function submit() {
  error.value = null

  if (seeds.value.length === 0) {
    error.value = t('projects.form.needSeed')
    return
  }

  busy.value = true
  try {
    const created = await api.post<ProjectSummary>('/api/projects', {
      name: name.value.trim(),
      seedUrls: seeds.value,
      scopeIncludes: lines(scopeIncludes.value),
      scopeExcludes: lines(scopeExcludes.value),
      schedule: schedule.value.trim() === '' ? null : schedule.value.trim(),
      retentionYears: retentionYears.value,
      captureProfileVersionId: captureProfileVersionId.value === '' ? null : captureProfileVersionId.value,
    })

    emit('created', created)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  try {
    const all = await api.get<ProfileOption[]>('/api/capture-profiles')
    profiles.value = all
    // Default to the newest profile still in force. A superseded version can still be selected
    // deliberately, but it should never be the accidental choice.
    captureProfileVersionId.value = all.find((p) => p.supersededAt === null)?.id ?? all[0]?.id ?? ''
  } catch {
    // The select falls back to "instance default", which the API resolves to DE-Standard v1.
  }
})
</script>

<template>
  <form class="space-y-5" @submit.prevent="submit">
    <div>
      <label for="p-name" class="field-label">
        {{ t('projects.form.name') }}
      </label>
      <input
        id="p-name"
        v-model="name"
        required
        maxlength="200"
        class="field-input"
      />
    </div>

    <div>
      <label for="p-seeds" class="field-label">
        {{ t('projects.form.seedUrls') }}
      </label>
      <textarea
        id="p-seeds"
        v-model="seedUrls"
        rows="3"
        placeholder="https://example.com/&#10;https://example.com/prices"
        class="field-input font-mono text-mono"
      />
      <p class="field-hint">{{ t('projects.form.seedUrlsHint') }}</p>
    </div>

    <details class="rounded-sm border border-rule px-3 py-2">
      <summary class="cursor-pointer type-label text-ink-muted">
        {{ t('projects.form.scope') }}
      </summary>
      <div class="mt-3 space-y-3">
        <div>
          <label for="p-include" class="field-label">
            {{ t('projects.form.include') }}
          </label>
          <textarea
            id="p-include"
            v-model="scopeIncludes"
            rows="2"
            class="field-input font-mono text-mono"
          />
        </div>
        <div>
          <label for="p-exclude" class="field-label">
            {{ t('projects.form.exclude') }}
          </label>
          <textarea
            id="p-exclude"
            v-model="scopeExcludes"
            rows="2"
            class="field-input font-mono text-mono"
          />
        </div>
        <p class="field-hint">{{ t('projects.form.scopeHint') }}</p>
      </div>
    </details>

    <div class="grid gap-4 sm:grid-cols-2">
      <div>
        <label for="p-schedule" class="field-label">
          {{ t('projects.form.schedule') }}
        </label>
        <input
          id="p-schedule"
          v-model="schedule"
          placeholder="0 3 * * *"
          class="field-input font-mono text-mono"
        />
        <p class="field-hint">{{ t('projects.form.scheduleHint') }}</p>
      </div>

      <div>
        <label for="p-retention" class="field-label">
          {{ t('projects.form.retention') }}
        </label>
        <input
          id="p-retention"
          v-model.number="retentionYears"
          type="number"
          min="1"
          max="30"
          required
          class="field-input"
        />
        <!--
          Stated here rather than in a help page, because this is the moment the decision is made
          and the API will refuse to reverse it later.
        -->
        <p class="field-hint text-brass">{{ t('projects.form.retentionWarning') }}</p>
      </div>
    </div>

    <div>
      <label for="p-profile" class="field-label">
        {{ t('projects.form.profile') }}
      </label>
      <select
        id="p-profile"
        v-model="captureProfileVersionId"
        class="field-input"
      >
        <option value="">{{ t('projects.form.profileDefault') }}</option>
        <option v-for="profile in profiles" :key="profile.id" :value="profile.id">
          {{ profile.name }} v{{ profile.version }}{{ profile.supersededAt ? ' — superseded' : '' }}
        </option>
      </select>
      <p class="field-hint">{{ t('projects.form.profileHint') }}</p>
    </div>

    <Notice v-if="error" tone="alert">{{ error }}</Notice>

    <div class="flex gap-2">
      <Button type="submit" variant="primary" :disabled="busy">
        {{ busy ? t('auth.working') : t('projects.form.create') }}
      </Button>
      <Button type="button" @click="emit('cancel')">{{ t('projects.form.cancel') }}</Button>
    </div>
  </form>
</template>
