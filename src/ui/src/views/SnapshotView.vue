<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
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
  payloadSha256: string
  payloadSizeBytes: number
  payloadMediaType: string
  canonicalFormVersion: string
  pluginVersion: string | null
  storageWorm: 'Unsupported' | 'ApiPresentUnverified' | 'Enforced'
  timestampAnchorId: string | null
  captureProfileVersion: { designation: string } | null
  pluginBindingVersion: {
    pluginId: string
    designation: string
    configurationJson: string
    secretRef: string | null
    rationale: string
  } | null
  // Null for a plugin snapshot: an API call has no viewport and no browser version, and the
  // record does not invent them.
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
  } | null
}

const snapshot = ref<SnapshotDetail | null>(null)
const payload = ref<string | null>(null)
const payloadError = ref<string | null>(null)
const error = ref<string | null>(null)

const isPageCapture = computed(() => snapshot.value?.canonicalFormVersion === 'priorstate-snapshot-v1')

// Only text-shaped payloads are worth putting on the page; anything else is offered as a download.
const isReadable = computed(() => {
  const type = snapshot.value?.payloadMediaType ?? ''
  return /^(text\/|application\/(json|xml|.*\+json|.*\+xml))/.test(type)
})

const shownPayload = computed(() => {
  if (payload.value === null) {
    return null
  }

  // Pretty-printing is a display convenience only. What was hashed is what the download and the
  // evidence package carry; this reformatting never reaches either.
  if (snapshot.value?.payloadMediaType.includes('json')) {
    try {
      return JSON.stringify(JSON.parse(payload.value), null, 2)
    } catch {
      return payload.value
    }
  }

  return payload.value
})

onMounted(async () => {
  try {
    snapshot.value = await api.get<SnapshotDetail>(`/api/snapshots/${props.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    return
  }

  if (!isPageCapture.value && isReadable.value) {
    try {
      payload.value = await api.getText(`/api/snapshots/${props.id}/archive`)
    } catch (e) {
      payloadError.value = e instanceof Error ? e.message : String(e)
    }
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
        <h1 class="text-lg font-semibold tracking-tight">
          {{ isPageCapture ? t('snapshot.title') : t('snapshot.pluginTitle') }}
        </h1>
        <p class="mt-0.5 text-sm text-ink-muted">{{ snapshot.url }}</p>
      </div>
      <IntegrityBadge :worm="snapshot.storageWorm" />
    </div>

    <Card v-if="isPageCapture" :title="t('snapshot.replay')">
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

    <!--
      The archived response itself. A stored payload nobody can look at is not much of an archive;
      the hash below it is what makes looking at it worth anything.
    -->
    <Card v-else :title="t('snapshot.payload')" :subtitle="t('snapshot.payloadHint')">
      <p v-if="payloadError" class="rounded-md border border-broken/40 bg-broken/10 px-4 py-2 text-sm">
        {{ payloadError }}
      </p>

      <pre
        v-else-if="shownPayload !== null"
        class="hash max-h-[32rem] overflow-auto rounded border border-rule bg-paper p-3 text-xs"
      >{{ shownPayload }}</pre>

      <p v-else-if="!isReadable" class="text-sm text-ink-muted">
        {{ t('snapshot.payloadNotShown', { type: snapshot.payloadMediaType }) }}
      </p>

      <p v-else class="text-sm text-ink-muted">{{ t('common.loading') }}</p>

      <div class="mt-4">
        <a
          :href="`/api/snapshots/${snapshot.id}/archive`"
          class="inline-flex items-center rounded-md border border-rule bg-paper-raised px-3 py-1.5 text-sm font-medium hover:bg-paper"
        >
          {{ t('snapshot.payloadDownload') }}
        </a>
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
        <DataRow :label="t('snapshot.payloadHash')" mono>{{ snapshot.payloadSha256 }}</DataRow>
        <DataRow :label="t('snapshot.payloadType')">
          {{ snapshot.payloadMediaType }} · {{ snapshot.payloadSizeBytes }} bytes
        </DataRow>
      </dl>
    </Card>

    <!--
      What produced a plugin snapshot, and under which configuration. The binding designation and
      a digest of that configuration are both part of the entry hash, so this is not a convenience
      section — it is the record.
    -->
    <Card
      v-if="snapshot.pluginBindingVersion"
      :title="t('snapshot.pluginSource')"
      :subtitle="t('snapshot.pluginSourceHint')"
    >
      <p class="mb-4 text-sm text-ink-muted">{{ snapshot.pluginBindingVersion.rationale }}</p>
      <dl>
        <DataRow :label="t('plugins.plugin')">{{ snapshot.pluginBindingVersion.pluginId }}</DataRow>
        <DataRow :label="t('snapshot.pluginVersion')">{{ snapshot.pluginVersion ?? '—' }}</DataRow>
        <DataRow :label="t('plugins.configuration')">{{ snapshot.pluginBindingVersion.designation }}</DataRow>
        <DataRow :label="t('plugins.secretRef')" mono>
          {{ snapshot.pluginBindingVersion.secretRef ?? '—' }}
        </DataRow>
      </dl>
      <pre class="hash mt-3 overflow-auto rounded border border-rule bg-paper p-3 text-xs">{{
        snapshot.pluginBindingVersion.configurationJson
      }}</pre>
    </Card>

    <Card v-if="snapshot.conditions" :title="t('snapshot.conditions')">
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
