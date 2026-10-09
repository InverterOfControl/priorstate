<script setup lang="ts">
/**
 * An inline message. The tint sits only behind its own hue's text; alert is kept for integrity
 * failures and errors, brass for anything still waiting on a timestamp.
 */
withDefaults(defineProps<{ tone?: 'ledger' | 'brass' | 'alert' | 'neutral' }>(), { tone: 'neutral' })
</script>

<template>
  <p
    class="rounded-sm px-4 py-2 text-body-sm"
    :class="{
      'bg-ledger-soft text-ledger': tone === 'ledger',
      'bg-brass-soft text-brass': tone === 'brass',
      'bg-alert-soft text-alert': tone === 'alert',
      'bg-paper-sunken text-ink': tone === 'neutral',
    }"
  >
    <slot />
  </p>
</template>
