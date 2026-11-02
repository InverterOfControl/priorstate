<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ProfileVersion } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import DataRow from '@/components/ui/DataRow.vue'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'
import ProfileForm from '@/components/ProfileForm.vue'

const { t } = useI18n()
const profiles = ref<ProfileVersion[]>([])
const loading = ref(true)
const creating = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)

async function onCreated(profile: ProfileVersion) {
  profiles.value.push(profile)
  profiles.value.sort((a, b) => a.name.localeCompare(b.name) || b.version - a.version)
  creating.value = false
  message.value = t('profiles.created', { name: profile.name, version: profile.version })
  try {
    profiles.value = await api.get<ProfileVersion[]>('/api/capture-profiles')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

onMounted(async () => {
  try {
    profiles.value = await api.get<ProfileVersion[]>('/api/capture-profiles')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-wrap items-baseline justify-between gap-4">
      <h1 class="font-serif text-title">{{ t('nav.profiles') }}</h1>
      <Button v-if="!creating" variant="primary" :disabled="loading || !!error" @click="creating = true; message = null">
        {{ t('profiles.new') }}
      </Button>
    </div>
    <p class="max-w-prose text-body-sm text-ink-muted">{{ t('profiles.explain') }}</p>
    <Notice v-if="error" tone="alert">{{ error }}</Notice>
    <Notice v-if="message" tone="ledger">{{ message }}</Notice>
    <Card v-if="creating" :title="t('profiles.new')">
      <ProfileForm :profiles="profiles" @created="onCreated" @cancel="creating = false" />
    </Card>

    <!--
      Stored versions remain read-only; the form only creates new immutable versions.
    -->
    <p v-if="loading" class="text-body-sm text-ink-muted">{{ t('common.loading') }}</p>
    <p v-else-if="!error && profiles.length === 0" class="text-body-sm text-ink-muted">{{ t('profiles.none') }}</p>

    <Card
      v-for="profile in profiles"
      :key="profile.id"
      :title="`${profile.name} v${profile.version}`"
      :subtitle="profile.supersededAt ? t('profiles.superseded', { time: formatUtc(profile.supersededAt) }) : undefined"
    >
      <p class="mb-4 text-body-sm text-ink-muted">{{ profile.rationale }}</p>
      <dl>
        <DataRow :label="t('conditions.userAgent')" mono>{{ profile.conditions.userAgent }}</DataRow>
        <DataRow :label="t('conditions.viewport')">
          {{ profile.conditions.viewportWidth }} × {{ profile.conditions.viewportHeight }}
        </DataRow>
        <DataRow :label="t('conditions.cookieBanner')">{{ t(`profiles.cookies.${profile.conditions.cookieBanner}`) }}</DataRow>
        <DataRow :label="t('conditions.settle')">{{ profile.conditions.javaScriptSettleMs }} ms</DataRow>
      </dl>
    </Card>
  </div>
</template>
