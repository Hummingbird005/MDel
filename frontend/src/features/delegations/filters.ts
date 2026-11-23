import type { DelegationStatus, ListParams, SortKey } from '../../api/delegations'

export type StatusFilter = 'all' | 'active' | 'scheduled' | 'pending' | 'expired' | 'other'
export type ModeFilter = 'all' | 'copy' | 'forwardOnly'

export const statusFilterMap: Record<StatusFilter, DelegationStatus[] | undefined> = {
  all: undefined,
  active: ['Active'],
  scheduled: ['Scheduled'],
  pending: ['PendingApproval'],
  expired: ['Expired'],
  other: ['Stopped', 'Rejected', 'Failed'],
}

/** Sortările disponibile în listă (valoare = cheie + direcție). */
export const sortOptions = ['Default', 'Start:asc', 'Start:desc', 'End:asc', 'End:desc', 'Created:desc', 'Id:desc'] as const
export type SortOption = (typeof sortOptions)[number]

export interface ListFilters {
  status: StatusFilter
  mode: ModeFilter
  sort: SortOption
  from: string // yyyy-mm-dd, zi locală
  to: string
}

/** Zi locală (yyyy-mm-dd) → începutul zilei în UTC; „to” include toată ziua. */
const dayStartUtc = (day: string) => (day ? new Date(`${day}T00:00:00`).toISOString() : undefined)
const dayEndUtc = (day: string) => (day ? new Date(new Date(`${day}T00:00:00`).getTime() + 86_400_000).toISOString() : undefined)

export function toListParams(f: ListFilters, search: string): ListParams {
  const [sort, dir] = f.sort.split(':') as [SortKey, string | undefined]
  return {
    status: statusFilterMap[f.status],
    keepCopy: f.mode === 'all' ? undefined : f.mode === 'copy',
    search,
    sort,
    desc: dir === 'desc',
    fromUtc: dayStartUtc(f.from),
    toUtc: dayEndUtc(f.to),
  }
}

