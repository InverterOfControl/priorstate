import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, setUnauthorizedHandler } from './api'

function mockFetch(response: Response) {
  const spy = vi.fn().mockResolvedValue(response)
  vi.stubGlobal('fetch', spy)
  return spy
}

afterEach(() => {
  vi.unstubAllGlobals()
  setUnauthorizedHandler(() => {})
})

describe('request', () => {
  it('accepts a 200 with an empty body', async () => {
    // Regression: ASP.NET Core Identity's /register and /login?useCookies=true both answer 200
    // with no body. Calling response.json() on that throws "Unexpected end of JSON input", so a
    // registration that had actually succeeded was reported to the user as a failure.
    mockFetch(new Response('', { status: 200 }))

    await expect(api.post('/api/auth/register', { email: 'a@b.c', password: 'x' })).resolves.toBeUndefined()
  })

  it('accepts a 204 with no content', async () => {
    mockFetch(new Response(null, { status: 204 }))

    await expect(api.post('/api/auth/logout')).resolves.toBeUndefined()
  })

  it('parses a body when there is one', async () => {
    mockFetch(new Response(JSON.stringify({ chainLength: 7 }), { status: 200 }))

    await expect(api.get<{ chainLength: number }>('/api/ledger/status')).resolves.toEqual({ chainLength: 7 })
  })

  it('surfaces the problem-details message on failure', async () => {
    mockFetch(
      new Response(JSON.stringify({ title: 'Conflict', detail: 'Not timestamped yet.' }), { status: 409 }),
    )

    await expect(api.get('/api/snapshots/x/evidence')).rejects.toMatchObject({
      status: 409,
      message: 'Not timestamped yet.',
    })
  })

  it('falls back to the status text when the error body is not problem details', async () => {
    mockFetch(new Response('<html>gateway error</html>', { status: 502, statusText: 'Bad Gateway' }))

    await expect(api.get('/api/projects')).rejects.toBeInstanceOf(ApiError)
  })

  it('notifies the unauthorized handler on 401 so the UI can return to sign-in', async () => {
    mockFetch(new Response('', { status: 401 }))
    const handler = vi.fn()
    setUnauthorizedHandler(handler)

    await expect(api.get('/api/projects')).rejects.toBeInstanceOf(ApiError)
    expect(handler).toHaveBeenCalledOnce()
  })

  it('sends credentials, so the identity cookie is attached', async () => {
    const spy = mockFetch(new Response('{}', { status: 200 }))

    await api.get('/api/projects')

    expect(spy).toHaveBeenCalledWith('/api/projects', expect.objectContaining({ credentials: 'include' }))
  })
})
