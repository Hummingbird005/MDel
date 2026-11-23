import { useTranslation } from 'react-i18next'
import type { Notification } from '../../api/delegations'
import { cx } from '../../lib/cx'
import { formatDateTime } from '../../lib/format'

interface Props {
  createdBy: string
  createdUtc?: string | null
  approvedBy?: string | null
  approvedUtc?: string | null
  notifications?: Notification[]
  compact?: boolean
}

/** Câmpurile read-only cerute: cine a înregistrat, cine a aprobat, cine a fost informat. */
export function ReadOnlyParties({ createdBy, createdUtc, approvedBy, approvedUtc, notifications = [], compact }: Props) {
  const { t } = useTranslation()

  // Un destinatar o singură dată (pe adresă), cu ultima stare de trimitere.
  const informed = Array.from(new Map(notifications.map((n) => [n.recipientEmail, n])).values())

  return (
    <div className={cx('grid gap-4 rounded-lg bg-subtle p-3 text-sm', compact ? 'grid-cols-1 sm:grid-cols-3' : 'grid-cols-1')}>
      <div>
        <p className="text-xs text-muted">{t('form.createdBy')}</p>
        <p className="mt-0.5 font-medium">{createdBy || '–'}</p>
        {createdUtc && <p className="tabular text-xs text-muted">{formatDateTime(createdUtc)}</p>}
      </div>
      <div>
        <p className="text-xs text-muted">{t('form.approvedBy')}</p>
        <p className="mt-0.5 font-medium">{approvedBy ?? '–'}</p>
        {approvedUtc && <p className="tabular text-xs text-muted">{formatDateTime(approvedUtc)}</p>}
      </div>
      <div className="min-w-0">
        <p className="text-xs text-muted">{t('form.informed')}</p>
        {informed.length === 0 ? (
          <p className="mt-0.5 font-medium">–</p>
        ) : (
          <ul className="mt-1 flex flex-col gap-1">
            {informed.map((n) => (
              <li key={n.recipientEmail} className="flex min-w-0 items-center gap-1.5" title={n.error ?? n.recipientEmail}>
                <span
                  className={cx('h-1.5 w-1.5 shrink-0 rounded-full', n.sentUtc ? 'bg-success' : n.error ? 'bg-danger' : 'bg-warning')}
                  aria-label={n.sentUtc ? t('notify.sent') : n.error ? t('notify.failed') : t('notify.pending')}
                />
                <span className="truncate">{n.recipientName ?? n.recipientEmail}</span>
                <span className="shrink-0 text-xs text-muted">({t(`recipient.${n.recipientType}`)})</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
