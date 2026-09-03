<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary'
    disabled?: boolean
    // Defaults to "button" so a button inside a form does not submit it by accident; the sign-in
    // form passes "submit" explicitly.
    type?: 'button' | 'submit'
  }>(),
  {
    variant: 'secondary',
    disabled: false,
    type: 'button',
  },
)
</script>

<template>
  <button
    :type="type"
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
