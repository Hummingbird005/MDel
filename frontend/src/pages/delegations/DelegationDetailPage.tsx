import {
  Alert,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  LinearProgress,
  Link,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import Grid from '@mui/material/Grid2'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink, useLocation, useParams } from 'react-router-dom'
import { delegationsApi, uploadAttachment, type DelegationDetail } from '../../api/delegations'
import { formatDateTime, formatSize } from '../../components/format'
import { ReadOnlyParties } from '../../components/ReadOnlyParties'
import { StatusChip } from '../../components/StatusChip'

export function DelegationDetailPage() {
  const { id } = useParams()
  const { t } = useTranslation()
  const location = useLocation()
  const uploadError = (location.state as { uploadError?: string } | null)?.uploadError
  const query = useQuery({ queryKey: ['delegations', Number(id)], queryFn: () => delegationsApi.get(Number(id)) })

  if (query.isLoading) return <CircularProgress />
  if (query.isError || !query.data) return <Alert severity="error">{t('common.notFound')}</Alert>

  const d = query.data
  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2 }} flexWrap="wrap" useFlexGap>
        <Typography variant="h5">{t('detail.title', { id: d.id })}</Typography>
        <StatusChip status={d.status} size="medium" />
        <Stack direction="row" spacing={1} sx={{ ml: 'auto' }}>
          {(d.actions.canEdit || d.actions.canEditEndOnly) && (
            <Button variant="outlined" component={RouterLink} to={`/delegations/${d.id}/edit`}>
              {d.actions.canEditEndOnly ? t('detail.changeEnd') : t('detail.edit')}
            </Button>
          )}
          {d.actions.canStop && <StopButton delegation={d} />}
        </Stack>
      </Stack>

      {uploadError && <Alert severity="error" sx={{ mb: 2 }}>{t('detail.uploadFailed', { error: uploadError })}</Alert>}
      {d.lastError && <Alert severity={d.status === 'Failed' ? 'error' : 'warning'} sx={{ mb: 2 }}>{d.lastError}</Alert>}

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 8 }}>
          <Stack spacing={2}>
            <Card>
              <CardContent>
                <Grid container spacing={2}>
                  <Field label={t('form.from')}>{d.from.displayName} <Muted>{d.from.email ?? d.from.samAccountName}</Muted></Field>
                  <Field label={t('form.to')}>{d.to.displayName} <Muted>{d.to.email ?? d.to.samAccountName}</Muted></Field>
                  <Field label={t('form.start')}>{formatDateTime(d.startUtc)}</Field>
                  <Field label={t('form.end')}>{formatDateTime(d.endUtc)}</Field>
                  <Field label={t('detail.approval')}>
                    {d.requiresApproval ? t('detail.withApproval') : t('detail.withoutApproval')}
                  </Field>
                  <Field label={t('detail.keepCopy')}>{d.keepCopyInSource ? t('admin.yes') : t('admin.no')}</Field>
                  {d.previousForwardingAddress && (
                    <Field label={t('detail.previousForward')}>{d.previousForwardingAddress}</Field>
                  )}
                  {d.exchangeAppliedUtc && <Field label={t('detail.applied')}>{formatDateTime(d.exchangeAppliedUtc)}</Field>}
                  {d.exchangeRemovedUtc && <Field label={t('detail.removed')}>{formatDateTime(d.exchangeRemovedUtc)}</Field>}
                  {d.rejectReason && <Field label={t('detail.rejectReason')} full>{d.rejectReason}</Field>}
                  {d.stopReason && (
                    <Field label={t('detail.stoppedBy')} full>
                      {d.stoppedBy?.displayName} · {formatDateTime(d.stoppedUtc)} – {d.stopReason}
                    </Field>
                  )}
                  {d.notes && <Field label={t('form.notes')} full>{d.notes}</Field>}
                </Grid>
              </CardContent>
            </Card>

            <Attachments delegation={d} />

            <Card>
              <CardContent>
                <Typography variant="subtitle1" gutterBottom>
                  {t('detail.history')}
                </Typography>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>{t('detail.when')}</TableCell>
                      <TableCell>{t('detail.status')}</TableCell>
                      <TableCell>{t('detail.by')}</TableCell>
                      <TableCell>{t('detail.reason')}</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {d.history.map((h, i) => (
                      <TableRow key={i}>
                        <TableCell>{formatDateTime(h.changedUtc)}</TableCell>
                        <TableCell>
                          <StatusChip status={h.toStatus} />
                        </TableCell>
                        <TableCell>{h.changedBy === 'System' ? t('detail.system') : h.changedBy}</TableCell>
                        <TableCell>{h.reason ?? ''}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </Stack>
        </Grid>

        <Grid size={{ xs: 12, md: 4 }}>
          <Card>
            <CardContent>
              <Typography variant="subtitle1" gutterBottom>
                {t('form.registration')}
              </Typography>
              <ReadOnlyParties
                createdBy={d.createdBy.displayName}
                createdUtc={d.createdUtc}
                approvedBy={d.approvedBy?.displayName}
                approvedUtc={d.approvedUtc}
                notifications={d.notifications}
              />
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </>
  )
}

function Field({ label, children, full }: { label: string; children: ReactNode; full?: boolean }) {
  return (
    <Grid size={{ xs: 12, sm: full ? 12 : 6 }}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography component="div">{children}</Typography>
    </Grid>
  )
}

const Muted = ({ children }: { children: ReactNode }) => (
  <Typography component="span" variant="body2" color="text.secondary">
    {children}
  </Typography>
)

function StopButton({ delegation }: { delegation: DelegationDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')

  const stop = useMutation({
    mutationFn: () => delegationsApi.stop(delegation.id, reason.trim(), delegation.rowVersion),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['delegations'] }),
    onSuccess: () => setOpen(false),
  })

  return (
    <>
      <Button color="error" variant="contained" onClick={() => setOpen(true)}>
        {t('detail.stop')}
      </Button>
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('detail.stopTitle', { id: delegation.id })}</DialogTitle>
        <DialogContent>
          <Typography gutterBottom>
            {delegation.status === 'Active' ? t('detail.stopActiveHelp') : t('detail.stopHelp')}
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={2}
            label={t('detail.reason')}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            sx={{ mt: 1 }}
          />
          {stop.error && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {(stop.error as Error).message}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t('form.cancel')}</Button>
          <Button color="error" variant="contained" disabled={!reason.trim() || stop.isPending} onClick={() => stop.mutate()}>
            {t('detail.stopConfirm')}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}

function Attachments({ delegation: d }: { delegation: DelegationDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [progress, setProgress] = useState<{ name: string; percent: number } | null>(null)
  const [error, setError] = useState<string | null>(null)

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
    onSuccess: refresh,
    onError: (e) => setError((e as Error).message),
  })

  return (
    <Card>
      <CardContent>
        <Stack direction="row" alignItems="center" sx={{ mb: 1 }}>
          <Typography variant="subtitle1">{t('detail.attachments')}</Typography>
          {d.actions.canManageAttachments && (
            <Button component="label" size="small" startIcon={<UploadFileIcon />} sx={{ ml: 'auto' }} disabled={!!progress}>
              {t('form.addFiles')}
              <input
                hidden
                multiple
                type="file"
                onChange={(e) => {
                  void upload(e.target.files)
                  e.target.value = ''
                }}
              />
            </Button>
          )}
        </Stack>
        {d.attachments.length === 0 && <Typography color="text.secondary">–</Typography>}
        <Table size="small">
          <TableBody>
            {d.attachments.map((a) => (
              <TableRow key={a.id}>
                <TableCell>
                  {a.purged ? (
                    <Typography color="text.secondary" title={t('detail.purged')}>
                      {a.fileName} ({t('detail.purged')})
                    </Typography>
                  ) : (
                    <Link href={delegationsApi.attachmentUrl(d.id, a.id)}>{a.fileName}</Link>
                  )}
                </TableCell>
                <TableCell>{formatSize(a.sizeBytes)}</TableCell>
                <TableCell>
                  {a.uploadedBy.displayName} · {formatDateTime(a.uploadedUtc)}
                </TableCell>
                <TableCell align="right">
                  {d.actions.canManageAttachments && (
                    <IconButton
                      size="small"
                      aria-label={t('detail.deleteAttachment')}
                      onClick={() => window.confirm(t('detail.confirmDelete', { name: a.fileName })) && remove.mutate(a.id)}
                    >
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {progress && (
          <>
            <Typography variant="body2" sx={{ mt: 1 }}>
              {t('form.uploading', { name: progress.name })} {progress.percent}%
            </Typography>
            <LinearProgress variant="determinate" value={progress.percent} />
          </>
        )}
        {error && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {error}
          </Alert>
        )}
      </CardContent>
    </Card>
  )
}
