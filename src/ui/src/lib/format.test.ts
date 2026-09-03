import { describe, expect, it } from 'vitest'
import { formatUtc, formatUtcDate, shortHash } from './format'

/**
 * Time display is not cosmetic here. What the interface shows for a snapshot has to match what
 * the hash chain recorded and what the protocol prints, or two people comparing the same
 * snapshot will read two different capture times and reasonably conclude something is wrong.
 */
describe('formatUtc', () => {
  it('renders in UTC regardless of the offset in the input', () => {
    expect(formatUtc('2026-09-03T16:30:00+02:00')).toBe('2026-09-03 14:30:00 UTC')
  })

  it('renders a Z-suffixed timestamp unchanged', () => {
    expect(formatUtc('2026-09-03T14:30:00Z')).toBe('2026-09-03 14:30:00 UTC')
  })

  it('drops sub-second precision, matching the canonical form', () => {
    expect(formatUtc('2026-09-03T14:30:00.750Z')).toBe('2026-09-03 14:30:00 UTC')
  })

  it('renders an em dash for a missing value rather than "Invalid Date"', () => {
    expect(formatUtc(null)).toBe('—')
    expect(formatUtc(undefined)).toBe('—')
  })
})

describe('formatUtcDate', () => {
  it('returns the UTC calendar day, which is what anchors are keyed by', () => {
    expect(formatUtcDate('2026-09-03T23:30:00-04:00')).toBe('2026-09-04')
  })
})

describe('shortHash', () => {
  const hash = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'

  it('keeps both ends so two hashes can be told apart at a glance', () => {
    expect(shortHash(hash)).toBe('e3b0c44298fc…7852b855')
  })

  it('handles a missing hash', () => {
    expect(shortHash(null)).toBe('—')
  })
})
