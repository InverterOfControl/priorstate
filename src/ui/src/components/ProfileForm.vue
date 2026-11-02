<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProfileVersion } from '@/lib/api'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'

const props = defineProps<{ profiles: ProfileVersion[] }>()
const emit = defineEmits<{ created: [ProfileVersion]; cancel: [] }>()
const { t } = useI18n()
const baseline = props.profiles.find(p => !p.supersededAt)?.conditions
const form = reactive({
  name: '', rationale: '',
  userAgent: baseline?.userAgent ?? 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36',
  viewportWidth: 1920, viewportHeight: 1080,
  cookieBanner: 'LeftAsIs', javaScriptSettleMs: 5000,
})
const busy = ref(false)
const error = ref<string | null>(null)
const existing = computed(() => props.profiles.find(p => p.name === form.name.trim()))
const nextVersion = computed(() => Math.max(0, ...props.profiles
  .filter(p => p.name === form.name.trim()).map(p => p.version)) + 1)

async function submit() {
  if (busy.value) return
  error.value = null
  if (!form.name.trim() || !form.rationale.trim() || !form.userAgent.trim()) {
    error.value = t('profiles.required')
    return
  }
  busy.value = true
  try {
    const created = await api.post<ProfileVersion>('/api/capture-profiles', {
      name: form.name.trim(), rationale: form.rationale.trim(),
      conditions: {
        userAgent: form.userAgent.trim(), viewportWidth: form.viewportWidth,
        viewportHeight: form.viewportHeight, cookieBanner: form.cookieBanner,
        javaScriptSettleMs: form.javaScriptSettleMs,
        authenticatedSession: false, adBlockerActive: false,
        chromiumVersion: 'unknown-until-first-capture', crawlerVersion: 'unknown-until-first-capture',
      },
    })
    emit('created', created)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="space-y-5" @submit.prevent="submit">
    <fieldset :disabled="busy" class="min-w-0 space-y-5">
      <div>
        <label for="profile-name" class="field-label">{{ t('profiles.name') }}</label>
        <input id="profile-name" v-model="form.name" required maxlength="120" class="field-input" aria-describedby="profile-name-hint" />
        <p id="profile-name-hint" class="field-hint">{{ t('profiles.nameHint') }}</p>
        <p v-if="existing" class="field-hint text-brass" role="status">{{ t('profiles.nextVersion', { name: existing.name, version: nextVersion }) }}</p>
      </div>
      <div>
        <label for="profile-rationale" class="field-label">{{ t('profiles.rationale') }}</label>
        <textarea id="profile-rationale" v-model="form.rationale" required maxlength="2000" rows="3" class="field-input" aria-describedby="profile-rationale-hint" />
        <p id="profile-rationale-hint" class="field-hint">{{ t('profiles.rationaleHint') }}</p>
      </div>
      <div>
        <label for="profile-agent" class="field-label">{{ t('conditions.userAgent') }}</label>
        <textarea id="profile-agent" v-model="form.userAgent" required maxlength="512" rows="3" class="field-input font-mono text-mono" />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label for="profile-width" class="field-label">{{ t('profiles.width') }}</label>
          <input id="profile-width" v-model.number="form.viewportWidth" type="number" min="1" max="7680" step="1" required class="field-input" />
        </div>
        <div>
          <label for="profile-height" class="field-label">{{ t('profiles.height') }}</label>
          <input id="profile-height" v-model.number="form.viewportHeight" type="number" min="1" max="7680" step="1" required class="field-input" />
        </div>
        <div>
          <label for="profile-cookies" class="field-label">{{ t('conditions.cookieBanner') }}</label>
          <select id="profile-cookies" v-model="form.cookieBanner" class="field-input">
            <option value="LeftAsIs">{{ t('profiles.cookies.LeftAsIs') }}</option>
            <option value="Dismissed">{{ t('profiles.cookies.Dismissed') }}</option>
          </select>
        </div>
        <div>
          <label for="profile-settle" class="field-label">{{ t('profiles.settle') }}</label>
          <input id="profile-settle" v-model.number="form.javaScriptSettleMs" type="number" min="0" max="300000" step="1000" required class="field-input" />
          <p class="field-hint">{{ t('profiles.settleHint') }}</p>
        </div>
      </div>
      <p class="field-hint">{{ t('profiles.fixedConditions') }}</p>
      <Notice v-if="error" tone="alert">{{ error }}</Notice>
      <div class="flex flex-wrap gap-2">
        <Button type="submit" variant="primary" :disabled="busy">{{ busy ? t('auth.working') : t('profiles.create') }}</Button>
        <Button :disabled="busy" @click="emit('cancel')">{{ t('projects.form.cancel') }}</Button>
      </div>
    </fieldset>
  </form>
</template>
