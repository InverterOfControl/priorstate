<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { WormSupport } from '@/lib/api'

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

const tone = computed(() => {
  switch (props.worm) {
    case 'Enforced':
      return 'border-verified/40 text-verified'
    case 'ApiPresentUnverified':
      return 'border-caution/40 text-caution'
    default:
      return 'border-rule text-ink-muted'
  }
})

const label = computed(() => t(`storage.${props.worm}`))
</script>

<template>
  <span
    class="inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium"
    :class="tone"
    :title="label"
  >
    {{ label }}
  </span>
</template>
