import { Copy, Forward } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { DelegationStatus } from '../../api/delegations'
import { cx } from '../../lib/cx'

const statusStyles: Record<DelegationStatus, { dot: string; cls: string }> = {
  Active: { dot: 'bg-success', cls: 'text-success bg-success/10 ring-success/25' },
  Scheduled: { dot: 'bg-info', cls: 'text-info bg-info/10 ring-info/25' },
  PendingApproval: { dot: 'bg-warning', cls: 'text-warning bg-warning/10 ring-warning/25' },
  Expired: { dot: 'bg-muted', cls: 'text-muted bg-subtle ring-line' },
  Stopped: { dot: 'bg-muted', cls: 'text-muted bg-subtle ring-line' },
  Rejected: { dot: 'bg-danger', cls: 'text-danger bg-danger/10 ring-danger/25' },
  Failed: { dot: 'bg-danger', cls: 'text-danger bg-danger/10 ring-danger/25' },
}

export function StatusBadge({ status }: { status: DelegationStatus }) {
  const { t } = useTranslation()
  const s = statusStyles[status]
  return (
    <span className={cx('inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset', s.cls)}>
      <span className={cx('h-1.5 w-1.5 rounded-full', s.dot)} aria-hidden="true" />
      {t(`status.${status}`)}
    </span>
  )
}

/** Modul forwardului: cu copie în căsuța sursă (DeliverToMailboxAndForward) sau doar redirecționare. */
export function ForwardModeBadge({ keepCopy }: { keepCopy: boolean }) {
  const { t } = useTranslation()
  const Icon = keepCopy ? Copy : Forward
  return (
    <span
      className={cx(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium ring-1 ring-inset',
        keepCopy ? 'bg-accent/10 text-accent-text ring-accent/20' : 'bg-warning/10 text-warning ring-warning/25',
      )}
      title={keepCopy ? t('mode.keepCopyHelp') : t('mode.forwardOnlyHelp')}
    >
      <Icon size={12} strokeWidth={2.25} aria-hidden="true" />
      {keepCopy ? t('mode.keepCopy') : t('mode.forwardOnly')}
    </span>
  )
}
