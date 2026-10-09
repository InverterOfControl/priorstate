<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { WormSupport } from '@/lib/api'
import Glyph, { type GlyphName } from './Glyph.vue'

/**
 * Reports storage immutability as it was observed, never as it was hoped.
 *
 * "Unsupported" is shown plainly rather than hidden, because it is a correct and supported
 * configuration: the hash chain and the external timestamp are what make a snapshot provable, and
 * both survive the object store being wiped. Dressing an unverified backend up as protected would
 * be the one failure that could actually mislead someone relying on this.
 */
const props = defineProps<{ worm: WormSupport }>()
const { t } = useI18n()

const look = computed((): { glyph: GlyphName; tone: string } => {
  switch (props.worm) {
    case 'Enforced':
      return { glyph: 'shield', tone: 'bg-ledger-soft text-ledger' }
    case 'ApiPresentUnverified':
      return { glyph: 'clock', tone: 'bg-brass-soft text-brass' }
    default:
      return { glyph: 'dash', tone: 'bg-paper-sunken text-ink-muted inset-ring inset-ring-rule' }
  }
})

const label = computed(() => t(`storage.${props.worm}`))
</script>

<template>
  <span
    class="inline-flex h-[22px] items-center gap-1 rounded-xs pr-2 pl-1.5 whitespace-nowrap type-label [&_svg]:size-3.5"
    :class="look.tone"
    :title="label"
  >
    <Glyph :name="look.glyph" />
    <span>{{ label }}</span>
  </span>
</template>
