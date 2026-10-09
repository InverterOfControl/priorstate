<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatUtc } from '@/lib/format'

/**
 * A timestamp of record: absolute UTC first, in brass, exactly as the hash chain and the protocol
 * print it. Relative time is only ever a secondary aid.
 */
const props = withDefaults(
  defineProps<{ value: string | null | undefined; relative?: boolean; now?: number }>(),
  { relative: true },
)

const { locale } = useI18n()

const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 31_536_000],
  ['month', 2_592_000],
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
]

const relativeText = computed(() => {
  if (!props.value) return ''
  const seconds = Math.round((new Date(props.value).getTime() - (props.now ?? Date.now())) / 1000)
  const format = new Intl.RelativeTimeFormat(locale.value, { numeric: 'auto' })
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return format.format(Math.trunc(seconds / size), unit)
  }
  return format.format(0, 'second')
})
</script>

<template>
  <span v-if="!value" class="text-ink-muted">—</span>
  <span v-else class="inline-flex flex-wrap items-baseline gap-x-2">
    <time :datetime="value" class="font-mono text-mono-sm text-brass tabular-nums">{{ formatUtc(value) }}</time>
    <span v-if="relative" class="text-body-sm text-ink-muted">{{ relativeText }}</span>
  </span>
</template>
