import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Ban, CheckCircle2, TriangleAlert, XCircle } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { delegationsApi, type DelegationDetail } from '../../api/delegations'
import { Alert, Button, Dialog, Field, inputClass } from '../../components/ui/primitives'
import { useToast } from '../../components/ui/toast'

type Kind = 'stop' | 'approve' | 'reject'

/** Oprire (admin), aprobare și respingere (aprobator) – confirmare în pagină, cu motiv unde e cazul. */
export function DecisionDialog({ kind, delegation: d, onClose }: { kind: Kind; delegation: DelegationDetail; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const toast = useToast()
  const [reason, setReason] = useState('')
  const needsReason = kind !== 'approve'

  const run = useMutation({
    mutationFn: () =>
      kind === 'stop'
        ? delegationsApi.stop(d.id, reason.trim(), d.rowVersion)
        : kind === 'approve'
          ? delegationsApi.approve(d.id, d.rowVersion)
          : delegationsApi.reject(d.id, reason.trim(), d.rowVersion),
    onSuccess: (result) => {
      onClose()
      toast(t(`toast.${kind}`, { id: d.id }), kind === 'approve' ? 'success' : kind === 'reject' ? 'danger' : 'info')
      if (result.status === 'Failed') toast(t('toast.exchangeFailed'), 'danger')
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['delegations'] }),
  })

  const icon = {
    stop: <Ban size={18} />,
    approve: <CheckCircle2 size={18} />,
    reject: <XCircle size={18} />,
  }[kind]
  const tone = kind === 'approve' ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'

  const body =
    kind === 'stop'
      ? d.status === 'Active'
        ? t('dialog.stopActive', { to: d.to.displayName, from: d.from.displayName })
        : t('dialog.stopInactive')
      : kind === 'approve'
        ? new Date(d.startUtc) <= new Date()
          ? t('dialog.approveNow', { from: d.from.displayName, to: d.to.displayName })
          : t('dialog.approveLater', { from: d.from.displayName, to: d.to.displayName })
        : t('dialog.reject')

  return (
    <Dialog
      title={t(`dialog.${kind}Title`, { id: d.id })}
      onClose={onClose}
      icon={<span className={`grid h-10 w-10 shrink-0 place-items-center rounded-full ${tone}`}>{icon}</span>}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('form.cancel')}
          </Button>
          <Button
            variant={kind === 'approve' ? 'success' : 'danger'}
            disabled={needsReason && !reason.trim()}
            loading={run.isPending}
            onClick={() => run.mutate()}
          >
            {t(`dialog.${kind}Confirm`)}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted">{body}</p>
        {kind === 'approve' && d.actions.isOwnRequest && (
          <Alert tone="warning">
            <span className="flex items-center gap-2">
              <TriangleAlert size={14} /> {t('dialog.ownRequest')}
            </span>
          </Alert>
        )}
        {needsReason && (
          <Field label={t('detail.reason')} htmlFor="decision-reason">
            <textarea
              id="decision-reason"
              rows={2}
              autoFocus
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className={inputClass(false, 'h-auto py-2')}
            />
          </Field>
        )}
        {run.error && <Alert tone="danger">{(run.error as Error).message}</Alert>}
      </div>
    </Dialog>
  )
}
