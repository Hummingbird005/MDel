import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, Ban, CheckCircle2, Paperclip, Pencil, Trash2, Upload, XCircle } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { delegationsApi, uploadAttachment, type DelegationDetail } from '../../api/delegations'
import { hasRole, useCurrentUser } from '../../auth/useCurrentUser'
import { ForwardModeBadge, StatusBadge } from '../../components/ui/badges'
import { Alert, Avatar, Button, Card, CardHeader, Dialog, EmptyState, IconButton, Spinner } from '../../components/ui/primitives'
import { formatDateTime, formatSize } from '../../lib/format'
import { DecisionDialog } from './DecisionDialogs'
import { useDelegationDrawer } from './DelegationDrawer'
import { ReadOnlyParties } from './ReadOnlyParties'

export function DelegationDetailPage() {
  const { id } = useParams()
  const { t } = useTranslation()
  const drawer = useDelegationDrawer()
  const { data: me } = useCurrentUser()
  const canSeeAudit = hasRole(me, 'SystemAdmin')
  const [dialog, setDialog] = useState<'stop' | 'approve' | 'reject' | null>(null)
  const query = useQuery({ queryKey: ['delegations', Number(id)], queryFn: () => delegationsApi.get(Number(id)) })

  if (query.isLoading) return <Spinner />
  if (query.isError || !query.data) return <Alert tone="danger">{t('common.notFound')}</Alert>

  const d = query.data
  const a = d.actions

  return (
    <>
      <div className="flex flex-col gap-3">
        <Link to="/delegations" className="inline-flex w-fit items-center gap-1.5 text-sm text-muted hover:text-fg">
          <ArrowLeft size={14} /> {t('nav.delegations')}
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-semibold tracking-tight">{t('detail.title', { id: d.id })}</h1>
          <StatusBadge status={d.status} />
          <ForwardModeBadge keepCopy={d.keepCopyInSource} />
          <div className="flex flex-wrap gap-2 sm:ml-auto">
            {a.canApprove && (
              <Button variant="success" onClick={() => setDialog('approve')}>
                <CheckCircle2 size={16} /> {t('detail.approve')}
              </Button>
            )}
            {a.canReject && (
              <Button variant="secondary" onClick={() => setDialog('reject')}>
                <XCircle size={16} /> {t('detail.reject')}
              </Button>
            )}
            {(a.canEdit || a.canEditEndOnly) && (
              <Button variant="secondary" onClick={() => drawer.openEdit(d)}>
                <Pencil size={15} /> {a.canEditEndOnly ? t('detail.changeEnd') : t('detail.edit')}
              </Button>
            )}
            {a.canStop && (
              <Button variant="danger" onClick={() => setDialog('stop')}>
                <Ban size={15} /> {t('detail.stop')}
              </Button>
            )}
          </div>
        </div>
      </div>

      {d.lastError && <Alert tone={d.status === 'Failed' ? 'danger' : 'warning'}>{d.lastError}</Alert>}

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="flex min-w-0 flex-col gap-4 xl:col-span-2">
          <Card>
            <div className="flex flex-col gap-4 p-4 sm:flex-row sm:items-center">
              <Party label={t('table.source')} name={d.from.displayName} email={d.from.email ?? d.from.samAccountName} />
              <ArrowRight size={18} className="hidden shrink-0 text-muted sm:block" aria-hidden="true" />
              <Party label={t('table.target')} name={d.to.displayName} email={d.to.email ?? d.to.samAccountName} />
            </div>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-4 border-t border-line p-4 sm:grid-cols-2">
              <Item label={t('form.start')}>{formatDateTime(d.startUtc)}</Item>
              <Item label={t('form.end')}>{formatDateTime(d.endUtc)}</Item>
              <Item label={t('detail.approval')}>{d.requiresApproval ? t('detail.withApproval') : t('detail.withoutApproval')}</Item>
              <Item label={t('form.forwardMode')}>{d.keepCopyInSource ? t('mode.keepCopyHelp') : t('mode.forwardOnlyHelp')}</Item>
              {d.previousForwardingAddress && <Item label={t('detail.previousForward')}>{d.previousForwardingAddress}</Item>}
              {d.exchangeAppliedUtc && <Item label={t('detail.applied')}>{formatDateTime(d.exchangeAppliedUtc)}</Item>}
              {d.exchangeRemovedUtc && <Item label={t('detail.removed')}>{formatDateTime(d.exchangeRemovedUtc)}</Item>}
              {d.rejectReason && (
                <Item label={t('detail.rejectReason')} wide>
                  {d.rejectReason}
                </Item>
              )}
              {d.stopReason && (
                <Item label={t('detail.stoppedBy')} wide>
                  {d.stoppedBy?.displayName} · {formatDateTime(d.stoppedUtc)} – {d.stopReason}
                </Item>
              )}
              {d.notes && (
                <Item label={t('form.notes')} wide>
                  {d.notes}
                </Item>
              )}
            </dl>
          </Card>

          <Attachments delegation={d} />

          <Card>
            <CardHeader
            title={t('detail.history')}
            actions={
              canSeeAudit && (
                <Link to={`/audit?entityType=Delegation&entityId=${d.id}`} className="text-xs font-medium text-accent-text hover:underline">
                  {t('detail.auditTrail')} →
                </Link>
              )
            }
          />
            <ol className="flex flex-col gap-0 p-4">
              {d.history.map((h, i) => (
                <li key={i} className="relative flex gap-3 pb-4 last:pb-0">
                  {i < d.history.length - 1 && <span className="absolute left-[7px] top-4 h-full w-px bg-line" aria-hidden="true" />}
                  <span className="relative mt-1 h-3.5 w-3.5 shrink-0 rounded-full bg-card ring-2 ring-accent/50" aria-hidden="true" />
                  <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1">
                    <StatusBadge status={h.toStatus} />
                    <span className="text-sm">{h.changedBy === 'System' ? t('detail.system') : h.changedBy}</span>
                    <span className="tabular text-xs text-muted">{formatDateTime(h.changedUtc)}</span>
                    {h.reason && <span className="w-full text-sm text-muted">{h.reason}</span>}
                  </div>
                </li>
              ))}
            </ol>
          </Card>
        </div>

        <Card className="h-fit">
          <CardHeader title={t('form.registration')} />
          <div className="p-4">
            <ReadOnlyParties
              createdBy={d.createdBy.displayName}
              createdUtc={d.createdUtc}
              approvedBy={d.approvedBy?.displayName}
              approvedUtc={d.approvedUtc}
              notifications={d.notifications}
            />
          </div>
        </Card>
      </div>

      {dialog && <DecisionDialog kind={dialog} delegation={d} onClose={() => setDialog(null)} />}
    </>
  )
}

function Party({ label, name, email }: { label: string; name: string; email: string }) {
  return (
    <div className="flex min-w-0 flex-1 items-center gap-3">
      <Avatar name={name} />
      <div className="min-w-0 leading-tight">
        <p className="text-xs text-muted">{label}</p>
        <p className="truncate font-medium">{name}</p>
        <p className="truncate text-xs text-muted">{email}</p>
      </div>
    </div>
  )
}

function Item({ label, children, wide }: { label: string; children: ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? 'sm:col-span-2' : undefined}>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  )
}

function Attachments({ delegation: d }: { delegation: DelegationDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [progress, setProgress] = useState<{ name: string; percent: number } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<{ id: number; name: string } | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['delegations', d.id] })

  const upload = async (files: FileList | null) => {
    if (!files) return
    setError(null)
    for (const file of Array.from(files)) {
      try {
        setProgress({ name: file.name, percent: 0 })
        await uploadAttachment(d.id, file, (percent) => setProgress({ name: file.name, percent }))
      } catch (e) {
        setError(`${file.name}: ${(e as Error).message}`)
        break
      }
    }
    setProgress(null)
    await refresh()
  }

  const remove = useMutation({
    mutationFn: (attachmentId: number) => delegationsApi.deleteAttachment(d.id, attachmentId),
    onSuccess: () => {
      setDeleting(null)
      void refresh()
    },
    onError: (e) => setError((e as Error).message),
  })

  return (
    <Card>
      <CardHeader
        title={t('detail.attachments')}
        actions={
          d.actions.canManageAttachments && (
            <label className="inline-flex h-8 cursor-pointer items-center gap-2 rounded-lg px-2.5 text-xs font-medium text-accent-text hover:bg-subtle">
              <Upload size={14} /> {t('form.addFiles')}
              <input
                type="file"
                multiple
                className="sr-only"
                disabled={!!progress}
                onChange={(e) => {
                  void upload(e.target.files)
                  e.target.value = ''
                }}
              />
            </label>
          )
        }
      />
      {d.attachments.length === 0 ? (
        <EmptyState icon={<Paperclip size={22} />} title={t('detail.noAttachments')} />
      ) : (
        <ul className="divide-y divide-line">
          {d.attachments.map((a) => (
            <li key={a.id} className="flex items-center gap-3 px-4 py-2.5">
              <Paperclip size={15} className="shrink-0 text-muted" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                {a.purged ? (
                  <p className="truncate text-sm text-muted">
                    {a.fileName} ({t('detail.purged')})
                  </p>
                ) : (
                  <a href={delegationsApi.attachmentUrl(d.id, a.id)} className="truncate text-sm font-medium text-accent-text hover:underline">
                    {a.fileName}
                  </a>
                )}
                <p className="tabular text-xs text-muted">
                  {formatSize(a.sizeBytes)} · {a.uploadedBy.displayName} · {formatDateTime(a.uploadedUtc)}
                </p>
              </div>
              {d.actions.canManageAttachments && (
                <IconButton tone="danger" label={t('detail.deleteAttachment')} onClick={() => setDeleting({ id: a.id, name: a.fileName })}>
                  <Trash2 size={15} />
                </IconButton>
              )}
            </li>
          ))}
        </ul>
      )}
      {(progress || error) && (
        <div className="flex flex-col gap-2 border-t border-line p-4">
          {progress && (
            <>
              <p className="text-xs text-muted">
                {t('form.uploading', { name: progress.name })} {progress.percent}%
              </p>
              <div className="h-1.5 overflow-hidden rounded-full bg-subtle">
                <div className="h-full rounded-full bg-accent transition-all" style={{ width: `${progress.percent}%` }} />
              </div>
            </>
          )}
          {error && <Alert tone="danger">{error}</Alert>}
        </div>
      )}
      {deleting && (
        <Dialog
          title={t('detail.deleteAttachment')}
          onClose={() => setDeleting(null)}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDeleting(null)}>
                {t('form.cancel')}
              </Button>
              <Button variant="danger" loading={remove.isPending} onClick={() => remove.mutate(deleting.id)}>
                {t('common.delete')}
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">{t('detail.confirmDelete', { name: deleting.name })}</p>
        </Dialog>
      )}
    </Card>
  )
}
