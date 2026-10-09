<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type AnchorResult, type ChainVerificationResult, type LedgerStatus } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import Notice from '@/components/ui/Notice.vue'
import Button from '@/components/ui/Button.vue'
import DataRow from '@/components/ui/DataRow.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'
import Hash from '@/components/ui/Hash.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import Timestamp from '@/components/ui/Timestamp.vue'

const { t } = useI18n()

const status = ref<LedgerStatus | null>(null)
const verification = ref<ChainVerificationResult | null>(null)
const verifying = ref(false)
const anchoring = ref(false)
const anchorResult = ref<AnchorResult | null>(null)
const error = ref<string | null>(null)
const loading = ref(true)

async function load() {
  loading.value = true
  error.value = null
  try {
    status.value = await api.get<LedgerStatus>('/api/ledger/status')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

async function verify() {
  verifying.value = true
  verification.value = null
  error.value = null
  try {
    verification.value = await api.post<ChainVerificationResult>('/api/ledger/verify')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    verifying.value = false
  }
}

async function anchorNow() {
  anchoring.value = true
  anchorResult.value = null
  error.value = null

  try {
    anchorResult.value = await api.post<AnchorResult>('/api/ledger/anchor')
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    anchoring.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="ledger-page space-y-6 sm:space-y-8">
    <h1 class="font-serif text-title sm:text-display">{{ t('ledger.title') }}</h1>

    <div v-if="error" role="alert" class="space-y-3">
      <Notice tone="alert">{{ error }}</Notice>
      <Button v-if="!status" :disabled="loading" @click="load">{{ t('ledger.retry') }}</Button>
    </div>

    <p v-if="loading && !status" role="status" class="border-y border-rule py-8 text-body text-ink-muted">
      {{ t('common.loading') }}
    </p>

    <div class="grid items-start gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(0,5fr)]">

      <Card v-if="status">
        <dl>
          <DataRow :label="t('ledger.chainLength')"><span class="font-mono tabular-nums">{{ status.chainLength }}</span></DataRow>
          <DataRow :label="t('ledger.head')"><Hash :value="status.headHash" :head="12" :tail="8" /></DataRow>
          <DataRow :label="t('ledger.lastCapture')"><Timestamp :value="status.lastCapture" /></DataRow>
          <DataRow :label="t('ledger.anchors')"><span class="font-mono tabular-nums">{{ status.timestampAnchors }}</span></DataRow>
          <DataRow :label="t('ledger.lastAnchoredAt')"><Timestamp :value="status.lastAnchoredAt" /></DataRow>
          <DataRow :label="t('ledger.awaitingTimestamp')">
            <span class="inline-flex flex-wrap items-center gap-2">
              <span class="font-mono tabular-nums">{{ status.snapshotsAwaitingTimestamp }}</span>
              <StatusBadge v-if="status.snapshotsAwaitingTimestamp > 0" status="pending">
                {{ t('timestamp.pending') }}
              </StatusBadge>
            </span>
          </DataRow>
          <DataRow :label="t('storage.label')">
            <IntegrityBadge :worm="status.storageWorm" />
            <p class="mt-2 max-w-prose text-body-sm text-ink-muted">{{ t('storage.note') }}</p>
          </DataRow>
        </dl>
      </Card>

      <div class="space-y-6">
        <Card :title="t('ledger.anchorTitle')" :subtitle="t('ledger.anchorExplain')">
          <Button
            class="min-h-11 w-full justify-center whitespace-normal text-center"
            :aria-busy="anchoring"
            :disabled="anchoring || (status?.snapshotsAwaitingTimestamp ?? 0) === 0"
            @click="anchorNow"
          >
            {{ anchoring ? t('ledger.anchoring') : t('ledger.anchorNow') }}
          </Button>

          <Notice v-if="anchorResult?.didAnchor" tone="ledger" class="mt-4" role="status">
            {{
              t('ledger.anchored', {
                count: anchorResult.entriesAnchored,
                time: formatUtc(anchorResult.attestedAt),
              })
            }}
          </Notice>
          <p v-else-if="anchorResult" class="mt-4 text-body-sm text-ink-muted">{{ t('ledger.nothingPending') }}</p>

          <p v-if="anchorResult?.didAnchor && !anchorResult.qualified" class="mt-2 text-body-sm text-brass">
            {{ t('timestamp.unqualifiedWarning') }}
          </p>
        </Card>

        <Card :title="t('ledger.verify')" :subtitle="t('ledger.explain')">
          <Button variant="primary" class="min-h-11 w-full justify-center whitespace-normal text-center" :aria-busy="verifying" :disabled="verifying || !status || loading" @click="verify">
            {{ verifying ? t('ledger.verifying') : t('ledger.verify') }}
          </Button>

          <Notice v-if="verification?.isIntact" tone="ledger" class="mt-4" role="status">
            {{ t('ledger.intact', { count: verification.entriesChecked }) }}
          </Notice>

          <Notice v-else-if="verification" tone="alert" class="mt-4" role="alert">
            {{
              t('ledger.broken', {
                sequence: verification.failedChainSequence ?? '?',
                explanation: verification.explanation ?? '',
              })
            }}
          </Notice>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ledger-page :deep(section > header) {
  padding: 1.25rem;
}

.ledger-page :deep(section > header p) {
  margin-top: 0.5rem;
  line-height: 1.65;
}

.ledger-page :deep(section > div) {
  padding: 1.25rem;
}

.ledger-page :deep(dl > div) {
  padding-block: 0.875rem;
}

.ledger-page :deep(dl > div:first-child) {
  padding-top: 0;
}

.ledger-page :deep(dl > div:last-child) {
  padding-bottom: 0;
}

@media (min-width: 640px) {
  .ledger-page :deep(dl > div) {
    grid-template-columns: minmax(9rem, 0.8fr) minmax(0, 1fr);
  }
}
</style>
