import { useQuery } from '@tanstack/react-query'
import { ArrowLeftRight, CalendarClock, CalendarX, Hourglass, Plus, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { delegationsApi } from '../../api/delegations'
import { hasRole, useCurrentUser } from '../../auth/useCurrentUser'
import { PageHeader } from '../../components/layout/AppShell'
import { Button } from '../../components/ui/primitives'
import { cx } from '../../lib/cx'
import { useDelegationDrawer } from '../delegations/DelegationDrawer'
import { DelegationTable } from '../delegations/DelegationTable'
import type { StatusFilter } from '../delegations/filters'
import { UpcomingPanel } from './UpcomingPanel'
import { useDelegationList } from '../delegations/useDelegationList'

interface Kpi {
  filter: StatusFilter
  labelKey: string
  value?: number
  foot: string
  icon: LucideIcon
  tone: string
}

export function DashboardPage() {
  const { t } = useTranslation()
  const { data: user } = useCurrentUser()
  const drawer = useDelegationDrawer()
  const isAdmin = hasRole(user, 'Admin')
  const stats = useQuery({ queryKey: ['delegations', 'stats'], queryFn: delegationsApi.stats, refetchInterval: 60_000 })
  const list = useDelegationList(8)
  const s = stats.data

  const kpis: Kpi[] = [
    {
      filter: 'active',
      labelKey: 'kpi.active',
      value: s?.byStatus.Active,
      foot: s ? t('kpi.expiringSoon', { count: s.expiringIn7Days }) : '',
      icon: ArrowLeftRight,
      tone: 'bg-success/10 text-success',
    },
    { filter: 'scheduled', labelKey: 'kpi.scheduled', value: s?.byStatus.Scheduled, foot: t('kpi.scheduledFoot'), icon: CalendarClock, tone: 'bg-info/10 text-info' },
    {
      filter: 'pending',
      labelKey: 'kpi.pending',
      value: s?.byStatus.PendingApproval,
      foot: t('kpi.pendingFoot'),
      icon: Hourglass,
      tone: 'bg-warning/10 text-warning',
    },
    {
      filter: 'expired',
      labelKey: 'kpi.expired',
      value: s?.expiredLast30Days,
      foot: t('kpi.expiredFoot', { total: s?.byStatus.Expired ?? 0 }),
      icon: CalendarX,
      tone: 'bg-subtle text-muted',
    },
  ]

  return (
    <>
      <PageHeader
        title={t('nav.dashboard')}
        subtitle={t('dashboard.subtitle')}
        actions={
          isAdmin && (
            <Button onClick={drawer.openCreate}>
              <Plus size={16} /> {t('nav.newDelegation')}
            </Button>
          )
        }
      />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {kpis.map(({ filter, labelKey, value, foot, icon: Icon, tone }) => (
          <button
            key={filter}
            onClick={() => list.setFilter('status', list.filters.status === filter ? 'all' : filter)}
            aria-pressed={list.filters.status === filter}
            className={cx(
              'flex flex-col gap-3 rounded-xl bg-card p-4 text-left ring-1 transition hover:shadow-sm',
              list.filters.status === filter ? 'ring-2 ring-accent' : 'ring-line hover:ring-accent/40',
            )}
          >
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted">{t(labelKey)}</span>
              <span className={cx('grid h-8 w-8 place-items-center rounded-lg', tone)}>
                <Icon size={16} aria-hidden="true" />
              </span>
            </div>
            <span className="tabular text-3xl font-semibold tracking-tight">{value ?? '–'}</span>
            <span className="text-xs text-muted">{foot}</span>
          </button>
        ))}
      </div>

      <UpcomingPanel />

      <DelegationTable
        rows={list.query.data?.items ?? []}
        loading={list.query.isFetching}
        filters={list.filters}
        onFilter={list.setFilter}
        canManage={isAdmin}
        footer={
          <>
            <span className="tabular">{t('table.latest', { shown: list.query.data?.items.length ?? 0, total: list.query.data?.total ?? 0 })}</span>
            <Link to={`/delegations${list.filters.status === 'all' ? '' : `?status=${list.filters.status}`}`} className="font-medium text-accent-text hover:underline">
              {t('table.viewAll')} →
            </Link>
          </>
        }
      />
    </>
  )
}
