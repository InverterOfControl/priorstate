<script setup lang="ts">
import { computed } from 'vue'
import Glyph, { type GlyphName } from './Glyph.vue'

export type SealStatus = 'sealed' | 'pending' | 'tampered' | 'unsealed'

/**
 * The integrity state of a record as a glyph plus a word — never colour alone. The word comes from
 * the caller so it stays translated and specific ("Not yet timestamped", "Chain intact").
 */
const props = defineProps<{ status: SealStatus }>()

const look: Record<SealStatus, { glyph: GlyphName; tone: string }> = {
  sealed: { glyph: 'check', tone: 'bg-ledger-soft text-ledger' },
  pending: { glyph: 'clock', tone: 'bg-brass-soft text-brass' },
  tampered: { glyph: 'alert', tone: 'bg-alert-soft text-alert' },
  unsealed: { glyph: 'dash', tone: 'bg-paper-sunken text-ink-muted inset-ring inset-ring-rule' },
}

const current = computed(() => look[props.status])
</script>

<template>
  <span
    class="inline-flex h-[22px] items-center gap-1 rounded-xs pr-2 pl-1.5 whitespace-nowrap type-label [&_svg]:size-3.5"
    :class="current.tone"
  >
    <Glyph :name="current.glyph" />
    <span><slot /></span>
  </span>
</template>
