import { Ban, CalendarRange, ChevronDown, ChevronLeft, ChevronRight, Filter, Inbox, Paperclip, Pencil, X } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { delegationsApi, type DelegationDetail, type DelegationListItem, type UserRef } from '../../api/delegations'
import { ForwardModeBadge, StatusBadge } from '../../components/ui/badges'
import { Avatar, EmptyState, IconButton } from '../../components/ui/primitives'
import { cx } from '../../lib/cx'
import { DAY_MS, formatDate, formatTime } from '../../lib/format'
import { DecisionDialog } from './DecisionDialogs'
import { useDelegationDrawer } from './DelegationDrawer'
import { sortOptions, statusFilterMap, type ListFilters, type ModeFilter, type StatusFilter } from './filters'


interface Props {
  rows: DelegationListItem[]
  loading: boolean
  filters: ListFilters
  onFilter: (key: keyof ListFilters, value: string) => void
  onClear?: () => void
  hasFilters?: boolean
  /** Afișează și sortarea și intervalul de date (lista completă). */
  advanced?: boolean
  canManage: boolean
  footer?: React.ReactNode
}

function UserCell({ user }: { user: UserRef }) {
  return (
    <div className="flex min-w-[150px] max-w-[220px] items-center gap-3">
      <Avatar name={user.displayName} />
      <div className="min-w-0 leading-tight">
        <p className="truncate text-sm font-medium">{user.displayName}</p>
        <p className="truncate text-xs text-muted">{user.email ?? user.samAccountName}</p>
      </div>
    </div>
  )
}

function PeriodCell({ d }: { d: DelegationListItem }) {
  const { t } = useTranslation()
  const start = Date.parse(d.startUtc)
  const end = Date.parse(d.endUtc)
  const now = Date.now()
  const progress = d.status === 'Active' ? Math.min(100, Math.max(0, ((now - start) / (end - start)) * 100)) : null
  const daysLeft = Math.max(0, Math.ceil((end - now) / DAY_MS))
  return (
    <div className="flex min-w-[140px] flex-col gap-0.5">
      <span className="tabular whitespace-nowrap text-sm">
        {formatDate(d.startUtc)} <span className="text-muted">{formatTime(d.startUtc)}</span>
      </span>
      <span className="tabular whitespace-nowrap text-sm">
        <span className="text-muted">→</span> {formatDate(d.endUtc)} <span className="text-muted">{formatTime(d.endUtc)}</span>
      </span>
      {progress !== null && (
        <div className="flex items-center gap-2">
          <div className="h-1 flex-1 overflow-hidden rounded-full bg-subtle" aria-hidden="true">
            <div className={cx('h-full rounded-full', daysLeft <= 2 ? 'bg-warning' : 'bg-accent')} style={{ width: `${progress}%` }} />
          </div>
          <span className={cx('tabular text-[11px]', daysLeft <= 2 ? 'text-warning' : 'text-muted')}>{t('table.daysLeft', { count: daysLeft })}</span>
        </div>
      )}
    </div>
  )
}

export function DelegationTable({ rows, loading, filters, onFilter, onClear, hasFilters, advanced, canManage, footer }: Props) {
  const { status, mode } = filters
  const { t } = useTranslation()
  const navigate = useNavigate()
  const drawer = useDelegationDrawer()
  const [stopping, setStopping] = useState<DelegationDetail | null>(null)
  const [busy, setBusy] = useState<number | null>(null)

  const editable = (d: DelegationListItem) => ['Active', 'Scheduled', 'PendingApproval'].includes(d.status)
  const stoppable = (d: DelegationListItem) => ['Active', 'Scheduled', 'PendingApproval', 'Failed'].includes(d.status)

  /** Acțiunile au nevoie de detaliu (RowVersion, permisiuni) – îl încărcăm la click. */
  const withDetail = async (id: number, action: (d: DelegationDetail) => void) => {
    setBusy(id)
    try {
      action(await delegationsApi.get(id))
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="min-w-0 rounded-xl bg-card ring-1 ring-line" aria-label={t('nav.delegations')}>
      <div className="flex flex-col gap-3 border-b border-line p-3">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
          <div className="flex gap-1 overflow-x-auto" role="tablist" aria-label={t('table.statusFilter')}>
            {(Object.keys(statusFilterMap) as StatusFilter[]).map((s) => (
              <button
                key={s}
                role="tab"
                aria-selected={status === s}
                onClick={() => onFilter('status', s)}
                className={cx(
                  'h-8 whitespace-nowrap rounded-lg px-3 text-sm font-medium transition-colors',
                  status === s ? 'bg-fg text-bg' : 'text-muted hover:bg-subtle hover:text-fg',
                )}
              >
                {t(`dashboard.${s}`)}
              </button>
            ))}
          </div>
          <div className="flex flex-wrap items-center gap-2 lg:ml-auto">
            <Filter size={14} className="text-muted" aria-hidden="true" />
            <Select id="mode-filter" label={t('table.modeFilter')} value={mode} onChange={(v) => onFilter('mode', v as ModeFilter)}>
              <option value="all">{t('table.allModes')}</option>
              <option value="copy">{t('mode.keepCopy')}</option>
              <option value="forwardOnly">{t('mode.forwardOnly')}</option>
            </Select>
            {advanced && (
              <Select id="sort" label={t('table.sort')} value={filters.sort} onChange={(v) => onFilter('sort', v)}>
                {sortOptions.map((o) => (
                  <option key={o} value={o}>
                    {t(`table.sortOptions.${o.replace(':', '_')}`)}
                  </option>
                ))}
              </Select>
            )}
          </div>
        </div>
        {advanced && (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <CalendarRange size={14} className="text-muted" aria-hidden="true" />
            <label htmlFor="range-from" className="text-muted">
              {t('table.periodFrom')}
            </label>
            <input
              id="range-from"
              type="date"
              value={filters.from}
              max={filters.to || undefined}
              onChange={(e) => onFilter('from', e.target.value)}
              className="h-8 rounded-lg bg-card px-2 text-sm text-fg ring-1 ring-inset ring-line focus:outline-none focus:ring-accent"
            />
            <label htmlFor="range-to" className="text-muted">
              {t('table.periodTo')}
            </label>
            <input
              id="range-to"
              type="date"
              value={filters.to}
              min={filters.from || undefined}
              onChange={(e) => onFilter('to', e.target.value)}
              className="h-8 rounded-lg bg-card px-2 text-sm text-fg ring-1 ring-inset ring-line focus:outline-none focus:ring-accent"
            />
            {hasFilters && onClear && (
              <button onClick={onClear} className="inline-flex h-8 items-center gap-1 rounded-lg px-2 text-sm text-muted hover:bg-subtle hover:text-fg">
                <X size={14} /> {t('table.clearFilters')}
              </button>
            )}
          </div>
        )}
      </div>

      <div className="relative overflow-x-auto">
        <table className="w-full text-left">
          <thead>
            <tr className="text-[11px] uppercase tracking-wider text-muted">
              <th className="px-3 py-2.5 font-medium">{t('table.source')}</th>
              <th className="px-3 py-2.5 font-medium">{t('table.target')}</th>
              <th className="px-3 py-2.5 font-medium">{t('table.mode')}</th>
              <th className="px-3 py-2.5 font-medium">{t('table.period')}</th>
              <th className="px-3 py-2.5 font-medium">{t('table.status')}</th>
              <th className="px-3 py-2.5 font-medium">
                <span className="sr-only">{t('table.actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody className={cx('divide-y divide-line border-t border-line transition-opacity', loading && 'opacity-60')}>
            {rows.map((d) => (
              <tr key={d.id} className="cursor-pointer hover:bg-subtle/60" onClick={() => navigate(`/delegations/${d.id}`)}>
                <td className="px-3 py-3">
                  <div className="flex items-center gap-2">
                    <Link
                      to={`/delegations/${d.id}`}
                      onClick={(e) => e.stopPropagation()}
                      className="tabular w-8 shrink-0 font-mono text-xs text-muted hover:text-accent-text"
                    >
                      #{d.id}
                    </Link>
                    <UserCell user={d.from} />
                  </div>
                </td>
                <td className="px-3 py-3">
                  <UserCell user={d.to} />
                </td>
                <td className="px-3 py-3">
                  <div className="flex items-center gap-2">
                    <ForwardModeBadge keepCopy={d.keepCopyInSource} />
                    {d.attachmentCount > 0 && (
                      <Paperclip size={13} className="text-muted" aria-label={t('table.attachments', { count: d.attachmentCount })} />
                    )}
                  </div>
                </td>
                <td className="px-3 py-3">
                  <PeriodCell d={d} />
                </td>
                <td className="px-3 py-3">
                  <div className="flex flex-col items-start gap-1">
                    <StatusBadge status={d.status} />
                    {!d.requiresApproval && <span className="text-[11px] text-muted">{t('dashboard.noApproval')}</span>}
                  </div>
                </td>
                <td className="px-3 py-3" onClick={(e) => e.stopPropagation()}>
                  {canManage && (
                    <div className="flex justify-end gap-1">
                      <IconButton
                        label={t('table.edit', { id: d.id })}
                        disabled={!editable(d) || busy === d.id}
                        onClick={() => withDetail(d.id, drawer.openEdit)}
                      >
                        <Pencil size={15} />
                      </IconButton>
                      <IconButton
                        tone="danger"
                        label={t('table.stop', { id: d.id })}
                        disabled={!stoppable(d) || busy === d.id}
                        onClick={() => withDetail(d.id, setStopping)}
                      >
                        <Ban size={15} />
                      </IconButton>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && rows.length === 0 && (
          <div className="border-t border-line">
            <EmptyState icon={<Inbox size={28} />} title={t('dashboard.empty')} text={t('table.emptyHint')} />
          </div>
        )}
      </div>
      {footer && <div className="flex items-center justify-between gap-3 border-t border-line px-4 py-2.5 text-xs text-muted">{footer}</div>}
      {stopping && <DecisionDialog kind="stop" delegation={stopping} onClose={() => setStopping(null)} />}
    </section>
  )
}

function Select({ id, label, value, onChange, children }: { id: string; label: string; value: string; onChange: (v: string) => void; children: React.ReactNode }) {
  return (
    <div className="relative">
      <select
        id={id}
        value={value}
        aria-label={label}
        title={label}
        onChange={(e) => onChange(e.target.value)}
        className="h-8 appearance-none rounded-lg bg-card pl-2.5 pr-8 text-sm text-fg ring-1 ring-inset ring-line focus:outline-none focus:ring-accent"
      >
        {children}
      </select>
      <ChevronDown size={14} className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-muted" aria-hidden="true" />
    </div>
  )
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const { t } = useTranslation()
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(total, page * pageSize)
  return (
    <>
      <span className="tabular">{t('dashboard.displayedRows', { from, to, count: total })}</span>
      <div className="flex gap-1">
        <IconButton label={t('table.previous')} disabled={page <= 1} onClick={() => onPage(page - 1)}>
          <ChevronLeft size={16} />
        </IconButton>
        <IconButton label={t('table.next')} disabled={to >= total} onClick={() => onPage(page + 1)}>
          <ChevronRight size={16} />
        </IconButton>
      </div>
    </>
  )
}
