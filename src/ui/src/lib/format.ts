/**
 * Timestamps are always displayed in UTC, matching what is written into the hash chain and
 * printed on the protocol. Showing a local time here would mean a person reads one time in the
 * interface and a different one in the evidence package for the same snapshot.
 */
export function formatUtc(value: string | null | undefined): string {
  if (!value) return '—'
  return `${new Date(value).toISOString().replace('T', ' ').slice(0, 19)} UTC`
}

export function formatUtcDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toISOString().slice(0, 10)
}

export function shortHash(hash: string | null | undefined): string {
  if (!hash) return '—'
  return `${hash.slice(0, 12)}…${hash.slice(-8)}`
}
