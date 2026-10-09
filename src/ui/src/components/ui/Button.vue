<script setup lang="ts">
withDefaults(
  defineProps<{
    /** primary is the one action a view is for — at most once per view. */
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'md' | 'sm'
    disabled?: boolean
    // Defaults to "button" so a button inside a form does not submit it by accident; the sign-in
    // form passes "submit" explicitly.
    type?: 'button' | 'submit'
  }>(),
  {
    variant: 'secondary',
    size: 'md',
    disabled: false,
    type: 'button',
  },
)
</script>

<template>
  <button
    :type="type"
    :disabled="disabled"
    class="inline-flex cursor-pointer items-center gap-2 rounded-sm border font-medium transition-[background-color,border-color,filter] disabled:cursor-not-allowed disabled:opacity-50 disabled:filter-none"
    :class="[
      size === 'sm' ? 'h-7 px-3 text-body-sm' : 'h-9 px-4 text-sm',
      {
        'border-ledger bg-ledger text-on-ledger hover:brightness-108': variant === 'primary',
        'border-rule-strong bg-paper-raised text-ink hover:bg-paper-sunken': variant === 'secondary',
        'border-transparent bg-transparent text-ink hover:bg-paper-sunken': variant === 'ghost',
        'border-alert bg-alert text-on-alert hover:brightness-108': variant === 'danger',
      },
    ]"
  >
    <slot />
  </button>
</template>
