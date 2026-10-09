<script setup lang="ts">
defineProps<{ title?: string | undefined; subtitle?: string | undefined }>()
</script>

<template>
  <section class="rounded-md border border-rule bg-paper-raised">
    <header v-if="title" class="border-b border-rule px-4 py-3">
      <h2 class="text-heading">{{ title }}</h2>
      <p v-if="subtitle" class="mt-0.5 text-body-sm text-ink-muted">{{ subtitle }}</p>
    </header>
    <div class="p-4">
      <slot />
    </div>
  </section>
</template>
