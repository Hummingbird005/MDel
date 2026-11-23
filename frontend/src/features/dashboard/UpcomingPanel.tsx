import { useQuery } from '@tanstack/react-query'
import { CalendarCheck2, CalendarClock, CalendarX2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { delegationsApi, type UpcomingEvent } from '../../api/delegations'
import { StatusBadge } from '../../components/ui/badges'
import { Card, CardHeader, EmptyState, Spinner } from '../../components/ui/primitives'
import i18n from '../../i18n'
import { cx } from '../../lib/cx'
import { formatTime } from '../../lib/format'

/** Grupare pe zile: „Azi”, „Mâine”, apoi data. */
function dayLabel(utc: string, t: (k: string) => string) {
  const d = new Date(utc)
  const today = new Date()
  const startOf = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime()
  const diff = Math.round((startOf(d) - startOf(today)) / 86_400_000)
  if (diff === 0) return t('upcoming.today')
  if (diff === 1) return t('upcoming.tomorrow')
  return d.toLocaleDateString(i18n.resolvedLanguage === 'en' ? 'en-GB' : 'ro-RO', { weekday: 'long', day: 'numeric', month: 'long' })
}

export function UpcomingPanel() {
  const { t } = useTranslation()
  const query = useQuery({ queryKey: ['delegations', 'upcoming'], queryFn: () => delegationsApi.upcoming(7), refetchInterval: 60_000 })
  const events = query.data ?? []

  const groups = events.reduce<{ label: string; items: UpcomingEvent[] }[]>((acc, e) => {
    const label = dayLabel(e.atUtc, t)
    const last = acc[acc.length - 1]
    if (last?.label === label) last.items.push(e)
    else acc.push({ label, items: [e] })
    return acc
  }, [])

  return (
    <Card>
      <CardHeader
        title={
          <span className="inline-flex items-center gap-2">
            <CalendarClock size={16} className="text-muted" aria-hidden="true" /> {t('upcoming.title')}
          </span>
        }
        actions={<span className="tabular text-xs text-muted">{t('upcoming.count', { count: events.length })}</span>}
      />
      {query.isLoading ? (
        <Spinner />
      ) : events.length === 0 ? (
        <EmptyState icon={<CalendarCheck2 size={24} />} title={t('upcoming.empty')} />
      ) : (
        <div className="grid max-h-80 grid-cols-1 gap-x-6 overflow-y-auto p-4 pt-2 lg:grid-cols-2">
          {groups.map((g) => (
            <section key={g.label} className="flex flex-col">
              <h3 className="pb-1 pt-2 text-xs font-medium uppercase tracking-wider text-muted first-letter:uppercase">{g.label}</h3>
              <ul className="flex flex-col">
                {g.items.map((e) => {
                  const activation = e.type === 'Activation'
                  const Icon = activation ? CalendarCheck2 : CalendarX2
                  return (
                    <li key={`${e.type}-${e.delegation.id}`}>
                      <Link
                        to={`/delegations/${e.delegation.id}`}
                        className="flex items-center gap-3 rounded-lg px-2 py-1.5 hover:bg-subtle"
                      >
                        <span className="tabular w-11 shrink-0 text-sm font-medium">{formatTime(e.atUtc)}</span>
                        <span
                          className={cx(
                            'grid h-7 w-7 shrink-0 place-items-center rounded-md',
                            activation ? 'bg-success/10 text-success' : 'bg-warning/10 text-warning',
                          )}
                          title={activation ? t('upcoming.activation') : t('upcoming.expiration')}
                        >
                          <Icon size={14} aria-hidden="true" />
                        </span>
                        <span className="min-w-0 flex-1 truncate text-sm">
                          <span className="font-medium">{e.delegation.from.displayName}</span>
                          <span className="text-muted"> → </span>
                          {e.delegation.to.displayName}
                        </span>
                        <span className="hidden shrink-0 text-xs text-muted sm:inline">
                          {activation ? t('upcoming.activation') : t('upcoming.expiration')}
                        </span>
                        {e.delegation.status === 'PendingApproval' && <StatusBadge status="PendingApproval" />}
                      </Link>
                    </li>
                  )
                })}
              </ul>
            </section>
          ))}
        </div>
      )}
    </Card>
  )
}
