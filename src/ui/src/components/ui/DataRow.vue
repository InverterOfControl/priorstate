<script setup lang="ts">
defineProps<{ label: string; mono?: boolean }>()
</script>

<template>
  <div class="grid grid-cols-[minmax(9rem,14rem)_1fr] gap-4 border-b border-rule py-2 last:border-b-0">
    <dt class="text-xs font-medium text-ink-muted">{{ label }}</dt>
    <dd class="text-sm" :class="mono ? 'hash' : ''"><slot /></dd>
  </div>
</template>
