<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import DataRow from '@/components/ui/DataRow.vue'

interface ProfileVersion {
  id: string
  name: string
  version: number
  rationale: string
  createdAt: string
  supersededAt: string | null
  conditions: {
    userAgent: string
    viewportWidth: number
    viewportHeight: number
    cookieBanner: string
    javaScriptSettleMs: number
  }
}

const { t } = useI18n()
const profiles = ref<ProfileVersion[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    profiles.value = await api.get<ProfileVersion[]>('/api/capture-profiles')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-lg font-semibold tracking-tight">{{ t('nav.profiles') }}</h1>

    <!--
      Profiles are read-only here on purpose. Editing capture settings in place would change what
      an already-issued protocol claims; a change creates a new version instead, and existing
      snapshots keep the version they were captured under.
    -->
    <p v-if="loading" class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

    <Card
      v-for="profile in profiles"
      :key="profile.id"
      :title="`${profile.name} v${profile.version}`"
      :subtitle="profile.supersededAt ? `superseded ${formatUtc(profile.supersededAt)}` : undefined"
    >
      <p class="mb-4 text-sm text-ink-muted">{{ profile.rationale }}</p>
      <dl>
        <DataRow :label="t('conditions.userAgent')" mono>{{ profile.conditions.userAgent }}</DataRow>
        <DataRow :label="t('conditions.viewport')">
          {{ profile.conditions.viewportWidth }} × {{ profile.conditions.viewportHeight }}
        </DataRow>
        <DataRow :label="t('conditions.cookieBanner')">{{ profile.conditions.cookieBanner }}</DataRow>
        <DataRow :label="t('conditions.settle')">{{ profile.conditions.javaScriptSettleMs }} ms</DataRow>
      </dl>
    </Card>
  </div>
</template>
