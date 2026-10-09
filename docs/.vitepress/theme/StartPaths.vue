<script setup lang="ts">
import { withBase } from 'vitepress'

const paths = [
  {
    title: 'Run PriorState',
    description: 'Set up an instance, configure captures, and maintain the archive.',
    links: [
      ['Quickstart', '/guide/quickstart'],
      ['Capture profiles', '/operations/capture-profiles'],
      ['Storage and WORM', '/operations/storage'],
      ['Backup and retention', '/operations/backup'],
    ],
  },
  {
    title: 'Review the evidence',
    description: 'Understand the recorded state, inspect the proof, and check its limits.',
    links: [
      ['The evidence package', '/guide/evidence-package'],
      ['What it does not claim', '/guide/limits'],
      ['Timestamp authority', '/operations/timestamping'],
      ['Canonical form', '/reference/canonical-form'],
    ],
  },
]
</script>

<template>
  <div class="docs-paths">
    <section v-for="path in paths" :key="path.title" :aria-label="path.title">
      <h2>{{ path.title }}</h2>
      <p>{{ path.description }}</p>
      <ul>
        <li v-for="[label, url] in path.links" :key="url">
          <a :href="withBase(url)">{{ label }}</a>
        </li>
      </ul>
    </section>
  </div>
</template>
