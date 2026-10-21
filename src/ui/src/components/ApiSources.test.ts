import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ApiSources from './ApiSources.vue'
import { api } from '@/lib/api'
import en from '@/i18n/locales/en.json'

const httpPlugin = { id: 'http-json', displayName: 'HTTP endpoint', version: '1', packageSha256: 'hash',
  definition: { description: 'HTTP', fields: [], secretHelp: '', secretRequired: false } }

describe('API source setup', () => {
  afterEach(() => vi.restoreAllMocks())
  it('saves structured fields and a secret reference, then offers a worker test', async () => {
    vi.spyOn(api, 'get').mockImplementation(async path => path === '/api/plugins' ? [httpPlugin] : [])
    const post = vi.spyOn(api, 'post').mockResolvedValue({})
    const wrapper = mount(ApiSources, { props: { projectId: 'project-1' }, global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.get('input[maxlength="120"]').setValue('Prices')
      await wrapper.get('input[type="url"]').setValue('https://api.example.test/prices')
      await wrapper.findAll('select')[2]!.setValue('bearer')
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
    vi.spyOn(api, 'get').mockImplementation(async path => path === '/api/plugins' ? [httpPlugin] : [{ id: 'source-1', name: 'Prices', designation: 'Prices v1', pluginId: 'http-json', configurationJson: '{"url":"https://api.example.test"}', supersededAt: null }])
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

  it('configures a dynamically described plugin with typed fields and a worker secret', async () => {
    const plugin = { id: 'custom', displayName: 'Custom source', definition: { description: 'Custom', secretRequired: true, secretHelp: 'Worker secret', fields: [
      { name: 'endpoint', label: 'Endpoint', type: 'string', required: true, defaultValue: 'https://objects.example.test' },
      { name: 'port', label: 'Port', type: 'number', required: true, defaultValue: '443' },
      { name: 'enabled', label: 'Enabled', type: 'boolean', required: false, defaultValue: 'true' },
    ] } }
    vi.spyOn(api, 'get').mockImplementation(async path => path === '/api/plugins' ? [httpPlugin, plugin] : [])
    const post = vi.spyOn(api, 'post').mockResolvedValue({})
    const wrapper = mount(ApiSources, { props: { projectId: 'project-1' }, global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.findAll('select')[0]!.setValue('custom')
      await wrapper.get('input[maxlength="120"]').setValue('Objects')
      await wrapper.get('input[type="number"]').setValue('8443')
      await wrapper.get('input[pattern]').setValue('PS_SECRET_OBJECTS')
      await wrapper.get('textarea[maxlength="2000"]').setValue('Archive source')
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      const body = post.mock.calls[0]![1] as { pluginId: string; secretRef: string; configurationJson: string }
      expect(body.pluginId).toBe('custom')
      expect(body.secretRef).toBe('PS_SECRET_OBJECTS')
      expect(JSON.parse(body.configurationJson)).toEqual({ endpoint: 'https://objects.example.test', port: 8443, enabled: true })
    } finally { wrapper.unmount() }
  })
})
