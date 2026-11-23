import { useQuery } from '@tanstack/react-query'
import { ArrowRight, CheckCheck, CheckCircle2, Paperclip, XCircle } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { delegationsApi, type DelegationDetail } from '../../api/delegations'
import { PageHeader, useGlobalSearch } from '../../components/layout/AppShell'
import { ForwardModeBadge } from '../../components/ui/badges'
import { Avatar, Button, Card, EmptyState, Spinner } from '../../components/ui/primitives'
import { formatDateTime } from '../../lib/format'
import { DecisionDialog } from '../delegations/DecisionDialogs'

/** Cererile în așteptare pentru grupul de aprobatori. */
export function ApprovalsPage() {
  const { t } = useTranslation()
  const search = useGlobalSearch()
  const [dialog, setDialog] = useState<{ kind: 'approve' | 'reject'; d: DelegationDetail } | null>(null)
  const [busy, setBusy] = useState<number | null>(null)

  const query = useQuery({
    queryKey: ['delegations', 'list', 'approvals', search],
    queryFn: () => delegationsApi.list({ status: ['PendingApproval'], search: search.trim(), sort: 'Start', page: 1, pageSize: 100 }),
    refetchInterval: 30_000,
  })

  const open = async (id: number, kind: 'approve' | 'reject') => {
    setBusy(id)
    try {
      setDialog({ kind, d: await delegationsApi.get(id) })
    } finally {
      setBusy(null)
    }
  }

  const items = query.data?.items ?? []

  return (
    <>
      <PageHeader title={t('nav.approvals')} subtitle={t('approvals.subtitle')} />
      {query.isLoading ? (
        <Spinner />
      ) : items.length === 0 ? (
        <Card>
          <EmptyState icon={<CheckCheck size={28} />} title={t('approvals.empty')} text={t('approvals.emptyHint')} />
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
          {items.map((d) => (
            <Card key={d.id} className="flex flex-col gap-4 p-4">
              <div className="flex items-center gap-2">
                <Link to={`/delegations/${d.id}`} className="font-mono text-xs text-muted hover:text-accent-text">
                  #{d.id}
                </Link>
                <ForwardModeBadge keepCopy={d.keepCopyInSource} />
                {d.attachmentCount > 0 && (
                  <span className="inline-flex items-center gap-1 text-xs text-muted">
                    <Paperclip size={12} /> {d.attachmentCount}
                  </span>
                )}
                <span className="tabular ml-auto text-xs text-muted">{t('approvals.requested', { when: formatDateTime(d.createdUtc), by: d.createdBy.displayName })}</span>
              </div>
              <div className="flex items-center gap-3">
                <div className="flex min-w-0 flex-1 items-center gap-2">
                  <Avatar name={d.from.displayName} />
                  <span className="truncate text-sm font-medium">{d.from.displayName}</span>
                </div>
                <ArrowRight size={16} className="shrink-0 text-muted" aria-hidden="true" />
                <div className="flex min-w-0 flex-1 items-center gap-2">
                  <Avatar name={d.to.displayName} />
                  <span className="truncate text-sm font-medium">{d.to.displayName}</span>
                </div>
              </div>
              <p className="tabular text-sm text-muted">
                {formatDateTime(d.startUtc)} → {formatDateTime(d.endUtc)}
              </p>
              <div className="flex gap-2">
                <Button variant="success" size="sm" disabled={busy === d.id} onClick={() => open(d.id, 'approve')}>
                  <CheckCircle2 size={14} /> {t('detail.approve')}
                </Button>
                <Button variant="secondary" size="sm" disabled={busy === d.id} onClick={() => open(d.id, 'reject')}>
                  <XCircle size={14} /> {t('detail.reject')}
                </Button>
                <Link to={`/delegations/${d.id}`} className="ml-auto self-center text-xs font-medium text-accent-text hover:underline">
                  {t('approvals.details')} →
                </Link>
              </div>
            </Card>
          ))}
        </div>
      )}
      {dialog && <DecisionDialog kind={dialog.kind} delegation={dialog.d} onClose={() => setDialog(null)} />}
    </>
  )
}
