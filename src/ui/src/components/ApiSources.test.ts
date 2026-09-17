import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ApiSources from './ApiSources.vue'
import { api } from '@/lib/api'
import en from '@/i18n/locales/en.json'

describe('API source setup', () => {
  afterEach(() => vi.restoreAllMocks())
  it('saves structured fields and a secret reference, then offers a worker test', async () => {
    vi.spyOn(api, 'get').mockResolvedValue([])
    const post = vi.spyOn(api, 'post').mockResolvedValue({})
    const wrapper = mount(ApiSources, { props: { projectId: 'project-1' }, global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.get('input[maxlength="120"]').setValue('Prices')
      await wrapper.get('input[type="url"]').setValue('https://api.example.test/prices')
      await wrapper.findAll('select')[1]!.setValue('bearer')
      await wrapper.get('input[pattern]').setValue('PS_SECRET_PRICES_TOKEN')
      await wrapper.get('textarea[maxlength="2000"]').setValue('Archive current prices')
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      expect(post).toHaveBeenCalledWith('/api/plugin-bindings', expect.objectContaining({
        projectId: 'project-1', pluginId: 'http-json', name: 'Prices', secretRef: 'PS_SECRET_PRICES_TOKEN',
      }))
      const body = post.mock.calls[0]![1] as { configurationJson: string }
      expect(JSON.parse(body.configurationJson)).toMatchObject({ url: 'https://api.example.test/prices', method: 'GET', authHeaderName: 'Authorization', authValuePrefix: 'Bearer ' })
      expect(wrapper.text()).toContain('You can now test it from the worker')
    } finally { wrapper.unmount() }
  })

  it('queues connection tests instead of fetching APIs from the browser', async () => {
    vi.spyOn(api, 'get').mockResolvedValue([{ id: 'source-1', name: 'Prices', designation: 'Prices v1', pluginId: 'http-json', configurationJson: '{"url":"https://api.example.test"}', supersededAt: null }])
    const post = vi.spyOn(api, 'post').mockResolvedValue({ id: 'test-1', state: 'Queued', queuedAt: new Date().toISOString(), sizeBytes: null, error: null })
    const wrapper = mount(ApiSources, { props: { projectId: 'project-1' }, global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      await wrapper.findAll('button').find(button => button.text() === 'Test connection')!.trigger('click')
      await flushPromises()
      expect(post).toHaveBeenCalledWith('/api/plugin-bindings/source-1/test')
      expect(wrapper.text()).toContain('Queued')
    } finally { wrapper.unmount() }
  })
})
