<script setup lang="ts">
export type GlyphName = 'check' | 'clock' | 'alert' | 'dash' | 'copy' | 'shield'

defineProps<{ name: GlyphName }>()
</script>

<!-- 16px outline glyphs in currentColor, drawn with the same 1.5px stroke as the rest of the set. -->
<template>
  <svg
    class="block shrink-0"
    viewBox="0 0 16 16"
    width="16"
    height="16"
    fill="none"
    stroke="currentColor"
    stroke-width="1.5"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path v-if="name === 'check'" d="M3.5 8.5l3 3 6-7" />
    <template v-else-if="name === 'clock'">
      <circle cx="8" cy="8" r="5.5" />
      <path d="M8 5v3.2l2 1.3" />
    </template>
    <template v-else-if="name === 'alert'">
      <path d="M8 2.5l6 11H2z" />
      <path d="M8 6.5v3" />
      <path d="M8 11.6v.1" />
    </template>
    <template v-else-if="name === 'copy'">
      <rect x="5.5" y="5.5" width="8" height="8" rx="1" />
      <path d="M10.5 5.5v-2a1 1 0 0 0-1-1h-6a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2" />
    </template>
    <path v-else-if="name === 'shield'" d="M8 2l5 2v4c0 3-2.2 5.2-5 6-2.8-.8-5-3-5-6V4z" />
    <path v-else d="M4 8h8" />
  </svg>
</template>
