<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, type ChainVerificationResult, type LedgerStatus } from '@/lib/api'
import { formatUtc, formatUtcDate, shortHash } from '@/lib/format'
import Card from '@/components/ui/Card.vue'
import Button from '@/components/ui/Button.vue'
import DataRow from '@/components/ui/DataRow.vue'
import IntegrityBadge from '@/components/ui/IntegrityBadge.vue'

const { t } = useI18n()

const status = ref<LedgerStatus | null>(null)
const verification = ref<ChainVerificationResult | null>(null)
const verifying = ref(false)
const error = ref<string | null>(null)

async function load() {
  try {
    status.value = await api.get<LedgerStatus>('/api/ledger/status')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function verify() {
  verifying.value = true
  verification.value = null
  try {
    verification.value = await api.post<ChainVerificationResult>('/api/ledger/verify')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    verifying.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-lg font-semibold tracking-tight">{{ t('ledger.title') }}</h1>

    <p v-if="error" class="rounded-md border border-broken/40 bg-broken/10 px-4 py-2 text-sm text-broken">
      {{ error }}
    </p>

    <Card v-if="status">
      <dl>
        <DataRow :label="t('ledger.chainLength')">{{ status.chainLength }}</DataRow>
        <DataRow :label="t('ledger.head')" mono>{{ shortHash(status.headHash) }}</DataRow>
        <DataRow :label="t('ledger.lastCapture')">{{ formatUtc(status.lastCapture) }}</DataRow>
        <DataRow :label="t('ledger.anchors')">{{ status.timestampAnchors }}</DataRow>
        <DataRow :label="t('ledger.lastAnchoredDay')">{{ formatUtcDate(status.lastAnchoredDay) }}</DataRow>
        <DataRow :label="t('ledger.awaitingTimestamp')">
          {{ status.snapshotsAwaitingTimestamp }}
          <span v-if="status.snapshotsAwaitingTimestamp > 0" class="ml-2 text-xs text-ink-muted">
            {{ t('snapshot.notTimestamped') }}
          </span>
        </DataRow>
        <DataRow :label="t('storage.label')">
          <IntegrityBadge :worm="status.storageWorm" />
          <p class="mt-1.5 text-xs text-ink-muted">{{ t('storage.note') }}</p>
        </DataRow>
      </dl>
    </Card>

    <Card :title="t('ledger.verify')" :subtitle="t('ledger.explain')">
      <Button variant="primary" :disabled="verifying" @click="verify">
        {{ verifying ? t('ledger.verifying') : t('ledger.verify') }}
      </Button>

      <p
        v-if="verification?.isIntact"
        class="mt-4 rounded-md border border-verified/40 bg-verified/10 px-4 py-2 text-sm text-verified"
      >
        {{ t('ledger.intact', { count: verification.entriesChecked }) }}
      </p>

      <p
        v-else-if="verification"
        class="mt-4 rounded-md border border-broken/40 bg-broken/10 px-4 py-2 text-sm text-broken"
      >
        {{
          t('ledger.broken', {
            sequence: verification.failedChainSequence ?? '?',
            explanation: verification.explanation ?? '',
          })
        }}
      </p>
    </Card>
  </div>
</template>
