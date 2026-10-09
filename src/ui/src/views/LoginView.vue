<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { ApiError } from '@/lib/api'
import Button from '@/components/ui/Button.vue'
import Notice from '@/components/ui/Notice.vue'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

/**
 * On an instance with no accounts, this page is the initial setup rather than a sign-in form.
 * That is the whole of the setup: the README promises the first run ends at "create the first
 * account", and this is where that happens.
 */
const firstRun = computed(() => !auth.hasUsers)

async function submit() {
  error.value = null
  busy.value = true

  try {
    if (firstRun.value) {
      await auth.register(email.value, password.value)
    } else {
      await auth.signIn(email.value, password.value)
    }

    const target = typeof route.query.next === 'string' ? route.query.next : '/ledger'
    await router.replace(target)
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 401
        ? t('auth.badCredentials')
        : e instanceof Error
          ? e.message
          : String(e)
  } finally {
    busy.value = false
  }
}

onMounted(auth.refresh)
</script>

<template>
  <div class="mx-auto max-w-sm py-10">
    <h1 class="font-serif text-title">
      {{ firstRun ? t('auth.setupTitle') : t('auth.signInTitle') }}
    </h1>
    <p class="mt-1 mb-6 text-body-sm text-ink-muted">
      {{ firstRun ? t('auth.setupHint') : t('auth.signInHint') }}
    </p>

    <form class="space-y-4" @submit.prevent="submit">
      <div>
        <label for="email" class="field-label">{{ t('auth.email') }}</label>
        <input
          id="email"
          v-model="email"
          type="email"
          required
          autocomplete="username"
          class="field-input"
        />
      </div>

      <div>
        <label for="password" class="field-label">
          {{ t('auth.password') }}
        </label>
        <input
          id="password"
          v-model="password"
          type="password"
          required
          :minlength="12"
          :autocomplete="firstRun ? 'new-password' : 'current-password'"
          class="field-input"
        />
        <p v-if="firstRun" class="field-hint">{{ t('auth.passwordRule') }}</p>
      </div>

      <Notice v-if="error" tone="alert">{{ error }}</Notice>

      <Button type="submit" variant="primary" :disabled="busy" class="w-full justify-center">
        <span>{{ busy ? t('auth.working') : firstRun ? t('auth.createAccount') : t('auth.signIn') }}</span>
      </Button>
    </form>

    <p class="mt-6 text-body-sm text-ink-muted">{{ t('auth.accessLogged') }}</p>
  </div>
</template>
