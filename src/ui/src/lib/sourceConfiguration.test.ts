import { describe, expect, it } from 'vitest'
import { readSourceConfiguration, sourceConfigurationJson } from './sourceConfiguration'

describe('API source configuration', () => {
  it('preserves existing configuration fields when changing an API URL', () => {
    const config = readSourceConfiguration('{"url":"https://old.test","method":"POST","body":"{}","accept":"application/vnd.test+json","extension":42}')
    config.url = 'https://new.test'
    const saved = JSON.parse(sourceConfigurationJson(config, '{"X-Tenant":"shop"}'))
    expect(saved).toEqual({ url: 'https://new.test', method: 'POST', body: '{}', accept: 'application/vnd.test+json', extension: 42, headers: { 'X-Tenant': 'shop' } })
  })
  it('rejects arrays and non-string header values', () => {
    expect(() => sourceConfigurationJson({ url: '', method: 'GET' }, '[]')).toThrow()
    expect(() => sourceConfigurationJson({ url: '', method: 'GET' }, '{"X-Tenant":123}')).toThrow()
  })
})
