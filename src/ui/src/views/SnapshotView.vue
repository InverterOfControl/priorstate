<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import DataRow from '@/components/ui/DataRow.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'

// Registers <replay-web-page>. Vue is told in vite.config.ts to pass replay-* tags through to the
// browser rather than resolve them as components.
import 'replaywebpage'

const props = defineProps<{ id: string }>()
const { t } = useI18n()

interface SnapshotDetail {
  id: string
  url: string
  finalUrl: string | null
  capturedAtUtc: string
  chainSequence: number
  entryHash: string
  previousHash: string
  waczSha256: string
  storageWorm: 'Unsupported' | 'ApiPresentUnverified' | 'Enforced'
  timestampAnchorId: string | null
  captureProfileVersion: { designation: string } | null
  conditions: {
    userAgent: string
    viewportWidth: number
    viewportHeight: number
    authenticatedSession: boolean
    adBlockerActive: boolean
    cookieBanner: string
    javaScriptSettleMs: number
    chromiumVersion: string
    crawlerVersion: string
  }
}

const snapshot = ref<SnapshotDetail | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    snapshot.value = await api.get<SnapshotDetail>(`/api/snapshots/${props.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
})
</script>

<template>
  <div v-if="error" class="rounded-md border border-broken/40 bg-broken/10 px-4 py-2 text-sm text-broken">
    {{ error }}
  </div>

  <div v-else-if="snapshot" class="space-y-6">
    <div class="flex items-baseline justify-between gap-4">
      <div>
        <h1 class="text-lg font-semibold tracking-tight">{{ t('snapshot.title') }}</h1>
        <p class="mt-0.5 text-sm text-ink-muted">{{ snapshot.url }}</p>
      </div>
      <IntegrityBadge :worm="snapshot.storageWorm" />
    </div>

    <Card :title="t('snapshot.replay')">
      <!--
        Replay is served straight from the stored WACZ over a range-request endpoint, so the
        component fetches only the parts it needs. What is shown here is the same file that goes
        into the evidence package, byte for byte.
      -->
      <div class="h-[36rem] overflow-hidden rounded border border-rule">
        <replay-web-page
          :source="`/api/snapshots/${snapshot.id}/archive`"
          :url="snapshot.finalUrl ?? snapshot.url"
          replayBase="/replay/"
          embed="replayonly"
          style="width: 100%; height: 100%"
        />
      </div>
    </Card>

    <Card :title="t('snapshot.title')">
      <dl>
        <DataRow :label="t('snapshot.url')">{{ snapshot.url }}</DataRow>
        <DataRow :label="t('snapshot.captured')">{{ formatUtc(snapshot.capturedAtUtc) }}</DataRow>
        <DataRow :label="t('snapshot.profile')">{{ snapshot.captureProfileVersion?.designation ?? '—' }}</DataRow>
        <DataRow :label="t('snapshot.sequence')">{{ snapshot.chainSequence }}</DataRow>
        <DataRow :label="t('snapshot.entryHash')" mono>{{ snapshot.entryHash }}</DataRow>
        <DataRow :label="t('snapshot.previousHash')" mono>{{ snapshot.previousHash }}</DataRow>
        <DataRow :label="t('snapshot.waczHash')" mono>{{ snapshot.waczSha256 }}</DataRow>
      </dl>
    </Card>

    <Card :title="t('snapshot.conditions')">
      <dl>
        <DataRow :label="t('conditions.userAgent')" mono>{{ snapshot.conditions.userAgent }}</DataRow>
        <DataRow :label="t('conditions.viewport')">
          {{ snapshot.conditions.viewportWidth }} × {{ snapshot.conditions.viewportHeight }}
        </DataRow>
        <DataRow :label="t('conditions.authenticated')">
          {{ snapshot.conditions.authenticatedSession ? t('common.yes') : t('common.no') }}
        </DataRow>
        <DataRow :label="t('conditions.adBlocker')">
          {{ snapshot.conditions.adBlockerActive ? t('common.yes') : t('common.no') }}
        </DataRow>
        <DataRow :label="t('conditions.cookieBanner')">{{ snapshot.conditions.cookieBanner }}</DataRow>
        <DataRow :label="t('conditions.settle')">{{ snapshot.conditions.javaScriptSettleMs }} ms</DataRow>
        <DataRow :label="t('conditions.chromium')">{{ snapshot.conditions.chromiumVersion }}</DataRow>
        <DataRow :label="t('conditions.crawler')">{{ snapshot.conditions.crawlerVersion }}</DataRow>
      </dl>
    </Card>

    <Card :title="t('snapshot.evidence')" :subtitle="t('snapshot.evidenceHint')">
      <p
        v-if="!snapshot.timestampAnchorId"
        class="rounded-md border border-caution/40 bg-caution/10 px-4 py-2 text-sm text-caution"
      >
        {{ t('snapshot.notTimestamped') }}
      </p>
      <!--
        A plain link rather than a scripted download: the browser streams the ZIP straight from
        the API, and the export is recorded in the audit log server-side.
      -->
      <a
        v-else
        :href="`/api/snapshots/${snapshot.id}/evidence`"
        class="inline-flex items-center rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-paper-raised hover:opacity-90"
      >
        {{ t('snapshot.evidence') }}
      </a>
    </Card>
  </div>

  <p v-else class="text-sm text-ink-muted">{{ t('common.loading') }}</p>
</template>
