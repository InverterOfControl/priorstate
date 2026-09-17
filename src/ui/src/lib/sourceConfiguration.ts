export interface HttpSourceConfiguration {
  url: string
  method: string
  headers?: Record<string, string>
  body?: string | null
  accept?: string
  contentType?: string
  authHeaderName?: string | null
  authValuePrefix?: string | null
  [key: string]: unknown
}

export function readSourceConfiguration(json: string): HttpSourceConfiguration {
  const config = JSON.parse(json)
  if (!config || Array.isArray(config) || typeof config !== 'object') throw new Error('Expected a JSON object')
  return { ...config, url: config.url ?? '', method: config.method ?? 'GET' }
}

export function sourceConfigurationJson(config: HttpSourceConfiguration, headersJson: string): string {
  const headers: unknown = JSON.parse(headersJson)
  if (!headers || Array.isArray(headers) || typeof headers !== 'object'
    || Object.values(headers).some(value => typeof value !== 'string')) {
    throw new Error('Headers must be a JSON object with text values.')
  }
  // Preserve unknown fields when versioning existing configurations.
  return JSON.stringify({ ...config, headers }, null, 2)
}
