<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import Glyph from './Glyph.vue'

/**
 * A digest in monospace. Truncation only ever happens in the middle, and the full value is always
 * in the tooltip and on the clipboard — a shortened hash cannot be compared against anything.
 */
const props = withDefaults(
  defineProps<{
    value: string | null | undefined
    /** Prefix shown before the digest, e.g. "sha256". Empty hides it. */
    algorithm?: string
    head?: number
    tail?: number
    /** Show the whole digest, wrapping, instead of truncating it. */
    full?: boolean
    copyable?: boolean
  }>(),
  { algorithm: '', head: 8, tail: 8, full: false, copyable: true },
)

const { t } = useI18n()
const copied = ref(false)
let reset: ReturnType<typeof setTimeout> | undefined

const qualified = computed(() => (props.algorithm ? `${props.algorithm}:` : '') + (props.value ?? ''))
const shown = computed(() => {
  const value = props.value ?? ''
  return props.full || value.length <= props.head + props.tail + 1
    ? value
    : `${value.slice(0, props.head)}…${value.slice(-props.tail)}`
})

async function copy() {
  try {
    await navigator.clipboard.writeText(qualified.value)
    copied.value = true
    clearTimeout(reset)
    reset = setTimeout(() => (copied.value = false), 1600)
  } catch {
    // Clipboard unavailable (insecure context); the value is still selectable and in the tooltip.
  }
}

onUnmounted(() => clearTimeout(reset))
</script>

<template>
  <span v-if="!value" class="text-ink-muted">—</span>
  <span
    v-else
    class="inline-flex max-w-full items-start gap-1 rounded-xs bg-paper-sunken py-px pr-0.5 pl-2"
    :title="qualified"
  >
    <code
      class="min-w-0 font-mono text-mono text-ink"
      :class="full ? 'break-all' : 'overflow-hidden text-ellipsis whitespace-nowrap'"
    ><span v-if="algorithm" class="text-ink-muted">{{ algorithm }}:</span>{{ shown }}</code>
    <button
      v-if="copyable"
      type="button"
      class="grid size-[22px] shrink-0 cursor-pointer place-items-center rounded-xs text-ink-muted transition-colors hover:text-ink"
      :aria-label="copied ? t('common.copied') : t('common.copyHash')"
      :title="copied ? t('common.copied') : t('common.copyHash')"
      @click.stop.prevent="copy"
    >
      <Glyph :name="copied ? 'check' : 'copy'" />
    </button>
  </span>
</template>
