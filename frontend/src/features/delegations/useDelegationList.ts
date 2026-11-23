import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { delegationsApi } from '../../api/delegations'
import { useGlobalSearch } from '../../components/layout/AppShell'
import { statusFilterMap, type ModeFilter, type StatusFilter } from './DelegationTable'

/** Starea listei (filtre în URL, căutarea globală din header, paginare). */
export function useDelegationList(pageSize: number) {
  const [params, setParams] = useSearchParams()
  const search = useGlobalSearch()
  const [debounced, setDebounced] = useState(search)
  const [page, setPage] = useState(1)

  const status = (params.get('status') as StatusFilter) || 'all'
  const mode = (params.get('mode') as ModeFilter) || 'all'

  useEffect(() => {
    const h = setTimeout(() => {
      setDebounced(search.trim())
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [search])

  const setFilter = (key: 'status' | 'mode', value: string) => {
    const next = new URLSearchParams(params)
    if (value === 'all') next.delete(key)
    else next.set(key, value)
    setParams(next, { replace: true })
    setPage(1)
  }

  const query = useQuery({
    queryKey: ['delegations', 'list', status, mode, debounced, page, pageSize],
    queryFn: () =>
      delegationsApi.list({
        status: statusFilterMap[status] ?? undefined,
        keepCopy: mode === 'all' ? undefined : mode === 'copy',
        search: debounced,
        page,
        pageSize,
      }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })

  return {
    query,
    status,
    mode,
    page,
    setPage,
    setStatus: (s: StatusFilter) => setFilter('status', s),
    setMode: (m: ModeFilter) => setFilter('mode', m),
  }
}
