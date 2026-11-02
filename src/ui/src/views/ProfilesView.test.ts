import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ProfilesView from './ProfilesView.vue'
import { api, type ProfileVersion } from '@/lib/api'
import en from '@/i18n/locales/en.json'

const standard: ProfileVersion = {
  id: 'standard', name: 'DE-Standard', version: 1, rationale: 'Baseline',
  createdAt: '2026-11-01T12:00:00Z', supersededAt: null,
  conditions: { userAgent: 'Test browser', viewportWidth: 1920, viewportHeight: 1080,
    cookieBanner: 'LeftAsIs', javaScriptSettleMs: 5000, authenticatedSession: false,
    adBlockerActive: false, chromiumVersion: 'test', crawlerVersion: 'test' },
}

function render() {
  return mount(ProfilesView, { global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })] } })
}

describe('capture profiles', () => {
  afterEach(() => vi.restoreAllMocks())

  it('creates a separate profile with complete browser conditions and keeps the original', async () => {
    vi.spyOn(api, 'get').mockResolvedValueOnce([standard])
      .mockResolvedValue([standard, { ...standard, id: 'mobile', name: 'Mobile' }])
    const post = vi.spyOn(api, 'post').mockResolvedValue({ ...standard, id: 'mobile', name: 'Mobile' })
    const wrapper = render()
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.get('#profile-name').setValue(' Mobile ')
      await wrapper.get('#profile-rationale').setValue(' Mobile layout ')
      await wrapper.get('#profile-width').setValue(390)
      await wrapper.get('#profile-height').setValue(844)
      await wrapper.get('#profile-cookies').setValue('Dismissed')
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      expect(post).toHaveBeenCalledWith('/api/capture-profiles', {
        name: 'Mobile', rationale: 'Mobile layout', conditions: {
          ...standard.conditions, viewportWidth: 390, viewportHeight: 844, cookieBanner: 'Dismissed',
          chromiumVersion: 'unknown-until-first-capture', crawlerVersion: 'unknown-until-first-capture',
        },
      })
      expect(wrapper.find('form').exists()).toBe(false)
      expect(wrapper.text()).toContain('Mobile v1')
      expect(wrapper.text()).toContain('DE-Standard v1')
      expect(wrapper.text()).not.toContain('Superseded at')
    } finally { wrapper.unmount() }
  })

  it('warns about the next version and retains the superseded version', async () => {
    vi.spyOn(api, 'get').mockResolvedValueOnce([standard]).mockResolvedValue([
      { ...standard, supersededAt: '2026-11-02T12:00:01Z' }, { ...standard, id: 'v2', version: 2 },
    ])
    vi.spyOn(api, 'post').mockResolvedValue({ ...standard, id: 'v2', version: 2 })
    const wrapper = render()
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.get('#profile-name').setValue('DE-Standard')
      await wrapper.get('#profile-rationale').setValue('Longer wait')
      await wrapper.get('#profile-settle').setValue(8000)
      expect(wrapper.text()).toContain('This will create DE-Standard v2')
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      expect(wrapper.text()).toContain('DE-Standard v2')
      expect(wrapper.text()).toContain('DE-Standard v1')
      expect(wrapper.text()).toContain('Superseded at')
    } finally { wrapper.unmount() }
  })

  it('keeps entered values when saving fails and permits retry', async () => {
    vi.spyOn(api, 'get').mockResolvedValue([])
    const post = vi.spyOn(api, 'post').mockRejectedValue(new Error('Save failed'))
    const wrapper = render()
    try {
      await flushPromises()
      await wrapper.get('button').trigger('click')
      await wrapper.get('#profile-name').setValue('Mobile')
      await wrapper.get('#profile-rationale').setValue('   ')
      await wrapper.get('form').trigger('submit')
      expect(post).not.toHaveBeenCalled()
      await wrapper.get('#profile-rationale').setValue('Mobile layout')
      await wrapper.get('form').trigger('submit')
      await flushPromises()
      expect(wrapper.text()).toContain('Save failed')
      expect((wrapper.get('#profile-name').element as HTMLInputElement).value).toBe('Mobile')
      expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeUndefined()
      const buttons = wrapper.findAll('form button')
      await buttons[1]!.trigger('click')
      expect(wrapper.find('form').exists()).toBe(false)
    } finally { wrapper.unmount() }
  })

  it('shows a loading error', async () => {
    vi.spyOn(api, 'get').mockRejectedValue(new Error('Profiles unavailable'))
    const wrapper = render()
    try {
      await flushPromises()
      expect(wrapper.text()).toContain('Profiles unavailable')
    } finally { wrapper.unmount() }
  })
})
