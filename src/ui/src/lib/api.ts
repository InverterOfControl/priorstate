/**
 * Thin wrapper around fetch for the PriorState API.
 *
 * Identity comes from the ASP.NET Core Identity cookie, so every request carries credentials and
 * the server can attribute it in the audit log. There is no anonymous read path: an access log
 * that cannot name who looked at a snapshot would undermine the process documentation.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

let onUnauthorized: (() => void) | undefined

/**
 * Registered once at start-up. Kept as a callback rather than importing the auth store here,
 * because the store imports this module and the cycle would be resolved at an unpredictable time.
 */
export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  })

  if (!response.ok) {
    let detail = response.statusText
    try {
      const problem = await response.json()
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // Not a problem-details response; the status text will have to do.
    }

    // A 401 means the cookie expired or the session ended elsewhere. Tell whoever is listening so
    // the interface stops showing a signed-in shell over requests that are all failing.
    if (response.status === 401) {
      onUnauthorized?.()
    }

    throw new ApiError(response.status, detail)
  }

  // Not every success carries a body. Identity's /register and /login return 200 with nothing in
  // it, and calling response.json() on an empty body throws "Unexpected end of JSON input" — which
  // surfaces as a failure for a request that actually succeeded. Read as text and only parse when
  // there is something to parse.
  const text = await response.text()
  return (text.length > 0 ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, body === undefined ? { method: 'POST' } : { method: 'POST', body: JSON.stringify(body) }),
}

export type WormSupport = 'Unsupported' | 'ApiPresentUnverified' | 'Enforced'

export interface ProjectSummary {
  id: string
  name: string
  seedUrls: string[]
  schedule: string | null
  retentionYears: number
  captureProfile: string
  archived: boolean
}

export interface SnapshotSummary {
  id: string
  url: string
  capturedAtUtc: string
  chainSequence: number
  entryHash: string
  captureProfile: string
  storageWorm: WormSupport
  timestamped: boolean
}

export interface LedgerStatus {
  chainLength: number
  headHash: string | null
  lastCapture: string | null
  snapshotsAwaitingTimestamp: number
  timestampAnchors: number
  lastAnchoredAt: string | null
  lastAnchorQualified: boolean
  storageWorm: WormSupport
}

export interface AnchorResult {
  didAnchor: boolean
  entriesAnchored: number
  anchorId: string | null
  merkleRoot: string | null
  attestedAt: string | null
  qualified: boolean
}

export interface ChainVerificationResult {
  isIntact: boolean
  entriesChecked: number
  defect: string | null
  failedChainSequence: number | null
  explanation: string | null
}

export interface RunSummary {
  id: string
  projectId: string
  trigger: string
  status: string
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  captureProfile: string
  snapshotCount: number
  failureReason: string | null
}

export interface AuditEntry {
  id: string
  occurredAtUtc: string
  userName: string | null
  action: string
  subjectType: string
  subjectId: string | null
  detail: string | null
  remoteAddress: string | null
}
