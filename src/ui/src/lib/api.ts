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
    throw new ApiError(response.status, detail)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
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
  lastAnchoredDay: string | null
  lastAnchorQualified: boolean
  storageWorm: WormSupport
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
