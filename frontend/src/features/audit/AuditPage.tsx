import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Download, FileDiff, History, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { auditApi, type AuditDetail, type AuditFilters, type AuditItem, type AuditResult } from '../../api/audit'
import { PageHeader } from '../../components/layout/AppShell'
import { Alert, Card, EmptyState, IconButton, inputClass, Spinner } from '../../components/ui/primitives'
import { cx } from '../../lib/cx'
import { formatDateTime } from '../../lib/format'
import { Pager } from '../delegations/DelegationTable'

const PAGE_SIZE = 50
const filterKeys = ['from', 'to', 'user', 'action', 'entityType', 'entityId', 'result', 'search'] as const

/** Eticheta acțiunii în limba curentă; codul tehnic rămâne vizibil ca tooltip. */
// eslint-disable-next-line react-refresh/only-export-components
export function useActionLabel() {
  const { t } = useTranslation()
  return (action: string) => {
    const key = `audit.actions.${action.replace(/\./g, '_')}`
    const label = t(key, { defaultValue: '' })
    if (label) return label
    // Testele de conexiune: Admin.Test.Exchange etc.
    if (action.startsWith('Admin.Test.')) return t('audit.actions.Admin_Test', { target: action.slice('Admin.Test.'.length) })
    return action
  }
}

const resultStyle: Record<AuditResult, string> = {
  Success: 'bg-success/10 text-success ring-success/25',
  Failure: 'bg-danger/10 text-danger ring-danger/25',
  Denied: 'bg-warning/10 text-warning ring-warning/25',
}

export function AuditPage() {
  const { t } = useTranslation()
  const label = useActionLabel()
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<number | null>(null)

  // Filtrele stau în URL (link-uri directe, ex. din pagina unei delegări).
  const filters: AuditFilters = Object.fromEntries(filterKeys.map((k) => [k, params.get(k) ?? ''])) as AuditFilters
  const [userInput, setUserInput] = useState(filters.user ?? '')
  const [searchInput, setSearchInput] = useState(filters.search ?? '')

  const setFilter = (key: (typeof filterKeys)[number], value: string) => {
    const next = new URLSearchParams(window.location.search)
    if (value) next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
    setPage(1)
  }

  // Câmpurile text se aplică după 300 ms de pauză la tastare.
  useEffect(() => {
    const h = setTimeout(() => {
      if ((filters.user ?? '') !== userInput) setFilter('user', userInput.trim())
      if ((filters.search ?? '') !== searchInput) setFilter('search', searchInput.trim())
    }, 300)
    return () => clearTimeout(h)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userInput, searchInput])

  // Datele din filtre sunt zile locale; serverul primește UTC (sfârșitul include toată ziua).
  const apiFilters: AuditFilters = {
    ...filters,
    from: filters.from ? new Date(`${filters.from}T00:00:00`).toISOString() : '',
    to: filters.to ? new Date(new Date(`${filters.to}T00:00:00`).getTime() + 86_400_000).toISOString() : '',
  }

  const list = useQuery({
    queryKey: ['audit', apiFilters, page],
    queryFn: () => auditApi.list(apiFilters, page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  })
  const actions = useQuery({ queryKey: ['audit', 'actions'], queryFn: auditApi.actions, staleTime: 60_000 })
  const hasFilters = filterKeys.some((k) => !!filters[k])

  // Grupare în select: „Delegări”, „Setări”…, după prefixul acțiunii.
  const groups = (actions.data ?? []).reduce<Record<string, string[]>>((acc, a) => {
    const prefix = a.split('.')[0]
    ;(acc[prefix] ??= []).push(a)
    return acc
  }, {})

  return (
    <>
      <PageHeader
        title={t('nav.audit')}
        subtitle={t('audit.subtitle')}
        actions={
          <a
            href={auditApi.exportUrl(apiFilters)}
            className="inline-flex h-9 items-center gap-2 rounded-lg bg-card px-3.5 text-sm font-medium text-fg ring-1 ring-inset ring-line hover:bg-subtle"
          >
            <Download size={16} /> {t('delegations.export')}
          </a>
        }
      />

      <Card className="min-w-0">
        <div className="grid grid-cols-1 gap-3 border-b border-line p-3 sm:grid-cols-2 xl:grid-cols-6">
          <FilterField id="audit-from" label={t('audit.from')}>
            <input id="audit-from" type="date" className={inputClass(false, 'h-9')} value={filters.from} max={filters.to || undefined} onChange={(e) => setFilter('from', e.target.value)} />
          </FilterField>
          <FilterField id="audit-to" label={t('audit.to')}>
            <input id="audit-to" type="date" className={inputClass(false, 'h-9')} value={filters.to} min={filters.from || undefined} onChange={(e) => setFilter('to', e.target.value)} />
          </FilterField>
          <FilterField id="audit-user" label={t('audit.user')}>
            <input id="audit-user" className={inputClass(false, 'h-9')} value={userInput} placeholder="CONTOSO\\…" onChange={(e) => setUserInput(e.target.value)} />
          </FilterField>
          <FilterField id="audit-action" label={t('audit.action')}>
            <select id="audit-action" className={inputClass(false, 'h-9')} value={filters.action} onChange={(e) => setFilter('action', e.target.value)}>
              <option value="">{t('audit.allActions')}</option>
              {Object.entries(groups).map(([prefix, items]) => (
                <optgroup key={prefix} label={t(`audit.groups.${prefix}`, { defaultValue: prefix })}>
                  <option value={`${prefix}.`}>{t('audit.allInGroup', { group: t(`audit.groups.${prefix}`, { defaultValue: prefix }) })}</option>
                  {items.map((a) => (
                    <option key={a} value={a}>
                      {label(a)}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
          </FilterField>
          <FilterField id="audit-result" label={t('audit.result')}>
            <select id="audit-result" className={inputClass(false, 'h-9')} value={filters.result} onChange={(e) => setFilter('result', e.target.value)}>
              <option value="">{t('audit.allResults')}</option>
              {(['Success', 'Failure', 'Denied'] as const).map((r) => (
                <option key={r} value={r}>
                  {t(`audit.results.${r}`)}
                </option>
              ))}
            </select>
          </FilterField>
          <FilterField id="audit-search" label={t('audit.search')}>
            <input id="audit-search" className={inputClass(false, 'h-9')} value={searchInput} placeholder={t('audit.searchPlaceholder')} onChange={(e) => setSearchInput(e.target.value)} />
          </FilterField>
          {(filters.entityType || hasFilters) && (
            <div className="flex flex-wrap items-center gap-2 text-sm sm:col-span-2 xl:col-span-6">
              {filters.entityType && (
                <span className="inline-flex items-center gap-1.5 rounded-md bg-accent/10 px-2 py-1 text-accent-text">
                  {t('audit.entityFilter', { type: filters.entityType, id: filters.entityId })}
                  <button
                    aria-label={t('table.clearFilters')}
                    onClick={() => {
                      setFilter('entityType', '')
                      setFilter('entityId', '')
                    }}
                  >
                    <X size={13} />
                  </button>
                </span>
              )}
              {hasFilters && (
                <button
                  onClick={() => {
                    setParams(new URLSearchParams(), { replace: true })
                    setUserInput('')
                    setSearchInput('')
                    setPage(1)
                  }}
                  className="inline-flex h-8 items-center gap-1 rounded-lg px-2 text-muted hover:bg-subtle hover:text-fg"
                >
                  <X size={14} /> {t('table.clearFilters')}
                </button>
              )}
            </div>
          )}
        </div>

        {list.isLoading ? (
          <Spinner />
        ) : list.isError ? (
          <div className="p-4">
            <Alert tone="danger">{(list.error as Error).message}</Alert>
          </div>
        ) : (list.data?.items.length ?? 0) === 0 ? (
          <EmptyState icon={<History size={26} />} title={t('audit.empty')} text={t('table.emptyHint')} />
        ) : (
          <div className="relative overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="text-[11px] uppercase tracking-wider text-muted">
                  <th className="px-3 py-2.5 font-medium">{t('audit.when')}</th>
                  <th className="px-3 py-2.5 font-medium">{t('audit.user')}</th>
                  <th className="px-3 py-2.5 font-medium">{t('audit.action')}</th>
                  <th className="px-3 py-2.5 font-medium">{t('audit.target')}</th>
                  <th className="px-3 py-2.5 font-medium">{t('audit.result')}</th>
                  <th className="px-3 py-2.5 font-medium">
                    <span className="sr-only">{t('audit.details')}</span>
                  </th>
                </tr>
              </thead>
              <tbody className={cx('divide-y divide-line border-t border-line', list.isFetching && 'opacity-60')}>
                {list.data!.items.map((a) => (
                  <Row key={a.id} item={a} onOpen={() => setSelected(a.id)} />
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="flex items-center justify-between gap-3 border-t border-line px-4 py-2.5 text-xs text-muted">
          <Pager page={page} pageSize={PAGE_SIZE} total={list.data?.total ?? 0} onPage={setPage} />
        </div>
      </Card>

      {selected !== null && <AuditDetailPanel id={selected} onClose={() => setSelected(null)} />}
    </>
  )
}

function FilterField({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <label htmlFor={id} className="text-xs font-medium text-muted">
        {label}
      </label>
      {children}
    </div>
  )
}

function Row({ item: a, onOpen }: { item: AuditItem; onOpen: () => void }) {
  const { t } = useTranslation()
  const label = useActionLabel()
  const target =
    a.entityType === 'Delegation' && a.entityId ? (
      <Link to={`/delegations/${a.entityId}`} onClick={(e) => e.stopPropagation()} className="font-medium text-accent-text hover:underline">
        #{a.entityId}
      </Link>
    ) : a.entityType ? (
      <span>
        {a.entityType} {a.entityId && <span className="font-mono text-xs text-muted">{a.entityId}</span>}
      </span>
    ) : a.path ? (
      <span className="font-mono text-xs text-muted">
        {a.httpMethod} {a.path}
      </span>
    ) : (
      '–'
    )

  return (
    <tr className="cursor-pointer hover:bg-subtle/60" onClick={onOpen}>
      <td className="tabular whitespace-nowrap px-3 py-2.5 text-muted">{formatDateTime(a.timestampUtc)}</td>
      <td className="px-3 py-2.5">
        <div className="min-w-[140px] leading-tight">
          <p className="font-medium">{a.userName === 'system' ? t('detail.system') : (a.userDisplayName ?? a.userName)}</p>
          {a.userName !== 'system' && <p className="font-mono text-[11px] text-muted">{a.userName}</p>}
        </div>
      </td>
      <td className="px-3 py-2.5" title={a.action}>
        {label(a.action)}
      </td>
      <td className="px-3 py-2.5">
        {target}
      </td>
      <td className="px-3 py-2.5">
        <span className={cx('whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium ring-1 ring-inset', resultStyle[a.result])}>
          {t(`audit.results.${a.result}`)}
        </span>
      </td>
      <td className="max-w-[320px] px-3 py-2.5">
        <div className="flex items-center gap-2">
          {a.hasChanges && <FileDiff size={14} className="shrink-0 text-accent-text" aria-label={t('audit.hasChanges')} />}
          <span className="truncate text-muted">{a.details}</span>
        </div>
      </td>
    </tr>
  )
}

/** Valori vechi / noi, câmp cu câmp; cele modificate sunt evidențiate. */
function parse(json: string | null): Record<string, unknown> | null {
  if (!json) return null
  try {
    const v = JSON.parse(json)
    return v && typeof v === 'object' && !Array.isArray(v) ? v : { value: v }
  } catch {
    return { value: json }
  }
}

const show = (v: unknown) => (v === undefined ? '' : v === null ? '–' : typeof v === 'object' ? JSON.stringify(v) : String(v))

function AuditDetailPanel({ id, onClose }: { id: number; onClose: () => void }) {
  const { t } = useTranslation()
  const label = useActionLabel()
  const query = useQuery({ queryKey: ['audit', 'detail', id], queryFn: () => auditApi.get(id) })

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const d: AuditDetail | undefined = query.data
  const oldV = parse(d?.oldValues ?? null)
  const newV = parse(d?.newValues ?? null)
  const keys = Array.from(new Set([...Object.keys(oldV ?? {}), ...Object.keys(newV ?? {})]))

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label={t('audit.details')}>
      <div className="absolute inset-0 animate-fade-in bg-overlay/50" onClick={onClose} />
      <aside className="absolute inset-y-0 right-0 flex w-full max-w-xl animate-slide-in flex-col bg-card shadow-2xl ring-1 ring-line">
        <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
          <div>
            <h2 className="text-base font-semibold">{d ? label(d.action) : t('audit.details')}</h2>
            {d && <p className="tabular mt-0.5 text-sm text-muted">{formatDateTime(d.timestampUtc)}</p>}
          </div>
          <IconButton label={t('common.close')} onClick={onClose}>
            <X size={18} />
          </IconButton>
        </div>
        {!d ? (
          <Spinner />
        ) : (
          <div className="flex flex-col gap-5 overflow-y-auto px-5 py-5 text-sm">
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
              <dt className="text-muted">{t('audit.user')}</dt>
              <dd>
                {d.userDisplayName ?? d.userName} <span className="font-mono text-xs text-muted">{d.userName}</span>
              </dd>
              <dt className="text-muted">{t('audit.actionCode')}</dt>
              <dd className="font-mono text-xs">{d.action}</dd>
              <dt className="text-muted">{t('audit.result')}</dt>
              <dd>{t(`audit.results.${d.result}`)}</dd>
              {d.entityType && (
                <>
                  <dt className="text-muted">{t('audit.target')}</dt>
                  <dd>
                    {d.entityType} {d.entityId}
                  </dd>
                </>
              )}
              {d.path && (
                <>
                  <dt className="text-muted">HTTP</dt>
                  <dd className="font-mono text-xs">
                    {d.httpMethod} {d.path} → {d.statusCode}
                  </dd>
                </>
              )}
              {d.ipAddress && (
                <>
                  <dt className="text-muted">IP</dt>
                  <dd className="font-mono text-xs">{d.ipAddress}</dd>
                </>
              )}
              {d.correlationId && (
                <>
                  <dt className="text-muted">{t('audit.correlation')}</dt>
                  <dd className="break-all font-mono text-xs">{d.correlationId}</dd>
                </>
              )}
              {d.details && (
                <>
                  <dt className="text-muted">{t('audit.details')}</dt>
                  <dd className="break-words">{d.details}</dd>
                </>
              )}
            </dl>

            {keys.length > 0 && (
              <div className="overflow-x-auto rounded-lg ring-1 ring-line">
                <table className="w-full text-left text-xs">
                  <thead className="bg-subtle text-muted">
                    <tr>
                      <th className="px-3 py-2 font-medium">{t('audit.field')}</th>
                      <th className="px-3 py-2 font-medium">{t('audit.oldValue')}</th>
                      <th className="px-3 py-2 font-medium">{t('audit.newValue')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {keys.map((k) => {
                      const before = show(oldV?.[k])
                      const after = show(newV?.[k])
                      const changed = oldV && newV && before !== after
                      return (
                        <tr key={k} className={changed ? 'bg-warning/5' : undefined}>
                          <td className="px-3 py-2 font-mono">{k}</td>
                          <td className={cx('break-all px-3 py-2', changed && 'text-danger line-through decoration-danger/40')}>{before}</td>
                          <td className={cx('break-all px-3 py-2', changed && 'font-medium text-success')}>{after}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </aside>
    </div>
  )
}
