<script setup lang="ts">
defineProps<{ title?: string | undefined; subtitle?: string | undefined }>()
</script>

<template>
  <section class="rounded-lg border border-rule bg-paper-raised">
    <header v-if="title" class="border-b border-rule px-5 py-3">
      <h2 class="text-sm font-semibold tracking-tight">{{ title }}</h2>
      <p v-if="subtitle" class="mt-0.5 text-xs text-ink-muted">{{ subtitle }}</p>
    </header>
    <div class="p-5">
      <slot />
    </div>
  </section>
</template>
