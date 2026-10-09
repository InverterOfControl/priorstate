import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import LedgerView from './LedgerView.vue'
import { api, type LedgerStatus } from '@/lib/api'
import en from '@/i18n/locales/en.json'

const status: LedgerStatus = {
  chainLength: 0, headHash: null, lastCapture: null,
  snapshotsAwaitingTimestamp: 0, timestampAnchors: 0,
  lastAnchoredAt: null, lastAnchorQualified: false, storageWorm: 'Unsupported',
}

function render() {
  return mount(LedgerView, {
    global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] },
  })
}

describe('ledger recovery', () => {
  afterEach(() => vi.restoreAllMocks())

  it('keeps verification unavailable until status is loaded, then permits checking an empty chain', async () => {
    let resolve!: (value: LedgerStatus) => void
    vi.spyOn(api, 'get').mockImplementation(() => new Promise<LedgerStatus>(done => { resolve = done }) as never)
    const wrapper = render()
    try {
      expect(wrapper.get('[role="status"]').text()).toBe('Loading…')
      expect(wrapper.findAll('button').find(button => button.text() === en.ledger.verify)!.attributes('disabled')).toBeDefined()
      resolve(status)
      await flushPromises()
      expect(wrapper.find('[role="status"]').exists()).toBe(false)
      expect(wrapper.findAll('button').find(button => button.text() === en.ledger.verify)!.attributes('disabled')).toBeUndefined()
    } finally { wrapper.unmount() }
  })

  it('recovers from a failed initial request without leaving a stale error', async () => {
    const get = vi.spyOn(api, 'get').mockRejectedValueOnce(new Error('Connection unavailable')).mockResolvedValue(status)
    const wrapper = render()
    try {
      await flushPromises()
      expect(wrapper.get('[role="alert"]').text()).toContain('Connection unavailable')
      await wrapper.findAll('button').find(button => button.text() === en.ledger.retry)!.trigger('click')
      await flushPromises()
      expect(get).toHaveBeenCalledTimes(2)
      expect(wrapper.find('[role="alert"]').exists()).toBe(false)
      expect(wrapper.text()).toContain(en.ledger.chainLength)
    } finally { wrapper.unmount() }
  })
})
