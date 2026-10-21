import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import PluginsView from './PluginsView.vue'
import { api } from '@/lib/api'
import en from '@/i18n/locales/en.json'

describe('plugin package installation', () => {
  afterEach(() => vi.restoreAllMocks())
  const state = { canInstall: false, callerId: 'reader', restartRequired: false, mismatch: false,
    workerAvailable: true, installed: [], loaded: [{ id: 'git', sha256: 'original' }] }

  it('shows package state without an upload control to ordinary users', async () => {
    vi.spyOn(api, 'get').mockImplementation(async path => path === '/api/plugins/packages' ? state : [])
    const wrapper = mount(PluginsView, { global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      expect(wrapper.find('input[type="file"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('original')
      expect(wrapper.text()).toContain('restricted to operators')
    } finally { wrapper.unmount() }
  })

  it('uploads a package and shows pending restart while the loaded package is retained', async () => {
    let uploaded = false
    vi.spyOn(api, 'get').mockImplementation(async path => path === '/api/plugins/packages'
      ? { ...state, canInstall: true, restartRequired: uploaded, installed: uploaded ? [{ id: 'git', sha256: 'updated' }] : [] } : [])
    const upload = vi.spyOn(api, 'uploadPlugin').mockImplementation(async () => { uploaded = true; return {} })
    const wrapper = mount(PluginsView, { global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      const input = wrapper.get('input[type="file"]')
      const file = new File(['package'], 'git.psplugin')
      Object.defineProperty(input.element, 'files', { value: [file] })
      await input.trigger('change')
      await wrapper.get('button').trigger('click')
      await flushPromises()
      expect(upload).toHaveBeenCalledWith(file)
      expect(wrapper.text()).toContain('restart of both the API and worker')
      expect(wrapper.text()).toContain('original')
      expect(wrapper.text()).toContain('updated')
    } finally { wrapper.unmount() }
  })
})
