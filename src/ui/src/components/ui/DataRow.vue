<script setup lang="ts">
defineProps<{ label: string; mono?: boolean }>()
</script>

<template>
  <div
    class="grid grid-cols-1 gap-1 border-b border-rule py-2.5 last:border-b-0 sm:grid-cols-[minmax(9rem,14rem)_1fr] sm:gap-4"
  >
    <dt class="pt-0.5 type-label text-ink-muted">{{ label }}</dt>
    <dd class="min-w-0 text-body" :class="mono ? 'hash' : ''"><slot /></dd>
  </div>
</template>
