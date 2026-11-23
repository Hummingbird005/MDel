import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { delegationsApi } from '../../api/delegations'
import { useGlobalSearch } from '../../components/layout/AppShell'
import { sortOptions, toListParams, type ListFilters, type ModeFilter, type SortOption, type StatusFilter } from './filters'

/** Starea listei: filtre în URL (link-uri partajabile), căutarea globală din header, paginare. */
export function useDelegationList(pageSize: number) {
  const [params, setParams] = useSearchParams()
  const search = useGlobalSearch()
  const [debounced, setDebounced] = useState(search)
  const [page, setPage] = useState(1)

  const filters: ListFilters = {
    status: (params.get('status') as StatusFilter) || 'all',
    mode: (params.get('mode') as ModeFilter) || 'all',
    sort: (sortOptions as readonly string[]).includes(params.get('sort') ?? '') ? (params.get('sort') as SortOption) : 'Default',
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
  }

  useEffect(() => {
    const h = setTimeout(() => {
      setDebounced(search.trim())
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [search])

  // Pornim de la URL-ul curent (actualizat sincron la fiecare navigare), nu de la cel din ultima randare:
  // două schimbări rapide (ex. „de la” și „până la”) nu se suprascriu una pe alta.
  const setFilter = (key: keyof ListFilters, value: string) => {
    const next = new URLSearchParams(window.location.search)
    if (!value || value === 'all' || value === 'Default') next.delete(key)
    else next.set(key, value)
    setParams(next, { replace: true })
    setPage(1)
  }

  const listParams = toListParams(filters, debounced)
  const query = useQuery({
    queryKey: ['delegations', 'list', listParams, page, pageSize],
    queryFn: () => delegationsApi.list({ ...listParams, page, pageSize }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })

  return {
    query,
    filters,
    setFilter,
    page,
    setPage,
    exportUrl: delegationsApi.exportUrl(listParams),
    hasFilters: filters.status !== 'all' || filters.mode !== 'all' || !!filters.from || !!filters.to || !!debounced,
    clear: () => {
      setParams(new URLSearchParams(), { replace: true })
      setPage(1)
    },
  }
}
