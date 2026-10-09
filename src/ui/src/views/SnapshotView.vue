<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '@/lib/api'
import Card from '@/components/ui/Card.vue'
import DataRow from '@/components/ui/DataRow.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'
import Hash from '@/components/ui/Hash.vue'
import Notice from '@/components/ui/Notice.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import Timestamp from '@/components/ui/Timestamp.vue'

// Registers <replay-web-page>. Vue is told in vite.config.ts to pass replay-* tags through to the
// browser rather than resolve them as components.
import 'replaywebpage'

/**
 * Lets ReplayWeb.page get as far as registering its service worker.
 *
 * It registers through register-service-worker, which decides once, when that module is first
 * evaluated, whether the page has finished loading: it captures a promise of the window "load"
 * event at import time and waits on it before touching navigator.serviceWorker. This view is a
 * lazily loaded route chunk, so whenever it is reached after the page has settled — following any
 * link inside the application, which is the normal way to get here — that event is already in the
 * past, nothing ever resolves the promise, and registration is not attempted at all.
 *
 * The component gives no sign of it. It waits on its own registration before rendering the replay
 * iframe, so what is left is an empty box with no error in it and nothing in the console.
 *
 * Re-firing the event lets the listener it just added run. Nothing else in this application
 * listens for load, so the only code this reaches is the one waiting for it.
 */
function releaseServiceWorkerRegistration(): void {
  if (document.readyState === 'complete') {
    window.dispatchEvent(new Event('load'))
  }
}

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
  releaseServiceWorkerRegistration()

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
  <Notice v-if="error" tone="alert">{{ error }}</Notice>

  <div v-else-if="snapshot" class="space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="min-w-0">
        <h1 class="font-serif text-title">
          {{ isPageCapture ? t('snapshot.title') : t('snapshot.pluginTitle') }}
        </h1>
        <p class="mt-1 font-mono text-mono break-all text-ink-muted">{{ snapshot.url }}</p>
        <p v-if="isPageCapture" class="mt-2 text-body-sm text-ink-muted">{{ t('snapshot.pageMetadataHint') }}</p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <StatusBadge :status="snapshot.timestampAnchorId ? 'sealed' : 'unsealed'">
          {{ snapshot.timestampAnchorId ? t('timestamp.anchored') : t('timestamp.pending') }}
        </StatusBadge>
        <IntegrityBadge :worm="snapshot.storageWorm" />
      </div>
    </div>

    <Card v-if="isPageCapture" :title="t('snapshot.replay')">
      <!--
        Replay is served straight from the stored WACZ over a range-request endpoint, so the
        component fetches only the parts it needs. What is shown here is the same file that goes
        into the evidence package, byte for byte.
      -->
      <div class="h-[36rem] overflow-hidden rounded-xs border border-rule bg-paper-sunken">
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
      <Notice v-if="payloadError" tone="alert">{{ payloadError }}</Notice>

      <pre
        v-else-if="shownPayload !== null"
        class="hash max-h-[32rem] overflow-auto rounded-xs border border-rule bg-paper-sunken p-3"
      >{{ shownPayload }}</pre>

      <p v-else-if="!isReadable" class="text-body-sm text-ink-muted">
        {{ t('snapshot.payloadNotShown', { type: snapshot.payloadMediaType }) }}
      </p>

      <p v-else class="text-body-sm text-ink-muted">{{ t('common.loading') }}</p>

      <div class="mt-4">
        <a
          :href="`/api/snapshots/${snapshot.id}/archive`"
          class="inline-flex h-9 items-center rounded-sm border border-rule-strong bg-paper-raised px-4 text-sm font-medium text-ink transition-colors hover:bg-paper-sunken"
        >
          {{ t('snapshot.payloadDownload') }}
        </a>
      </div>
    </Card>

    <Card :title="t('snapshot.title')">
      <dl>
        <DataRow :label="t(isPageCapture ? 'snapshot.seedUrl' : 'snapshot.url')">{{ snapshot.url }}</DataRow>
        <DataRow :label="t(isPageCapture ? 'snapshot.crawlStarted' : 'snapshot.captured')"><Timestamp :value="snapshot.capturedAtUtc" /></DataRow>
        <DataRow :label="t('snapshot.profile')">{{ snapshot.captureProfileVersion?.designation ?? '—' }}</DataRow>
        <DataRow :label="t('snapshot.sequence')"><span class="font-mono text-mono tabular-nums">#{{ snapshot.chainSequence }}</span></DataRow>
        <DataRow :label="t('snapshot.entryHash')"><Hash :value="snapshot.entryHash" full /></DataRow>
        <DataRow :label="t('snapshot.previousHash')"><Hash :value="snapshot.previousHash" full /></DataRow>
        <DataRow :label="t('snapshot.payloadHash')"><Hash :value="snapshot.payloadSha256" full /></DataRow>
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
      <p class="mb-4 text-body-sm text-ink-muted">{{ snapshot.pluginBindingVersion.rationale }}</p>
      <dl>
        <DataRow :label="t('plugins.plugin')">{{ snapshot.pluginBindingVersion.pluginId }}</DataRow>
        <DataRow :label="t('snapshot.pluginVersion')">{{ snapshot.pluginVersion ?? '—' }}</DataRow>
        <DataRow :label="t('plugins.configuration')">{{ snapshot.pluginBindingVersion.designation }}</DataRow>
        <DataRow :label="t('plugins.secretRef')" mono>
          {{ snapshot.pluginBindingVersion.secretRef ?? '—' }}
        </DataRow>
      </dl>
      <pre class="hash mt-3 overflow-auto rounded-xs border border-rule bg-paper-sunken p-3">{{
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
      <Notice v-if="!snapshot.timestampAnchorId" tone="brass">
        {{ t('snapshot.notTimestamped') }}
      </Notice>
      <!--
        A plain link rather than a scripted download: the browser streams the ZIP straight from
        the API, and the export is recorded in the audit log server-side.
      -->
      <a
        v-else
        :href="`/api/snapshots/${snapshot.id}/evidence`"
        class="inline-flex h-9 items-center rounded-sm border border-ledger bg-ledger px-4 text-sm font-medium text-on-ledger transition-[filter] hover:brightness-108"
      >
        {{ t('snapshot.evidence') }}
      </a>
    </Card>
  </div>

  <p v-else class="text-body-sm text-ink-muted">{{ t('common.loading') }}</p>
</template>
