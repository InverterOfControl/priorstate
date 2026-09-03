<script setup lang="ts">
withDefaults(defineProps<{ variant?: 'primary' | 'secondary'; disabled?: boolean }>(), {
  variant: 'secondary',
  disabled: false,
})
</script>

<template>
  <button
    type="button"
    :disabled="disabled"
    class="inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50"
    :class="
      variant === 'primary'
        ? 'bg-accent text-paper-raised hover:opacity-90'
        : 'border border-rule bg-paper-raised hover:bg-paper'
    "
  >
    <slot />
  </button>
</template>
