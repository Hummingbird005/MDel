import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  CircularProgress,
  FormControlLabel,
  IconButton,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import Grid from '@mui/material/Grid2'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { delegationsApi, uploadAttachment, type DelegationDetail, type FormConfig } from '../../api/delegations'
import { useCurrentUser } from '../../auth/useCurrentUser'
import { formatDateTime, formatSize, fromLocalInput, toLocalInput } from '../../components/format'
import { ReadOnlyParties } from '../../components/ReadOnlyParties'
import { StatusChip } from '../../components/StatusChip'
import { toPicked, type PickedUser } from '../../components/pickedUser'
import { UserPicker } from '../../components/UserPicker'

export function DelegationFormPage() {
  const { id } = useParams()
  const delegationId = id ? Number(id) : undefined
  const config = useQuery({ queryKey: ['delegations', 'form-config'], queryFn: delegationsApi.formConfig })
  const existing = useQuery({
    queryKey: ['delegations', delegationId],
    queryFn: () => delegationsApi.get(delegationId!),
    enabled: delegationId !== undefined,
  })

  if (config.isLoading || existing.isLoading) return <CircularProgress />
  if (config.isError || existing.isError || !config.data) {
    return <Alert severity="error">{((config.error ?? existing.error) as Error)?.message}</Alert>
  }

  return <DelegationForm config={config.data} existing={existing.data} />
}

/** Validare locală (aceleași reguli sunt verificate și pe server). */
function validateFile(file: File, config: FormConfig, t: TFunction): string | null {
  const ext = file.name.includes('.') ? `.${file.name.split('.').pop()!.toLowerCase()}` : ''
  if (config.allowedExtensions.length > 0 && !config.allowedExtensions.includes(ext)) {
    return t('form.fileExtension', { name: file.name, list: config.allowedExtensions.join(', ') })
  }
  if (file.size > config.maxAttachmentMb * 1024 * 1024) {
    return t('form.fileTooLarge', { name: file.name, max: config.maxAttachmentMb })
  }
  if (file.size === 0) return t('form.fileEmpty', { name: file.name })
  return null
}

function DelegationForm({ config, existing }: { config: FormConfig; existing?: DelegationDetail }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: me } = useCurrentUser()

  const isEdit = !!existing
  const endOnly = existing?.status === 'Active'
  const defaultStart = new Date(Date.now() + 5 * 60_000)

  const [from, setFrom] = useState<PickedUser | null>(existing ? toPicked(existing.from) : null)
  const [to, setTo] = useState<PickedUser | null>(existing ? toPicked(existing.to) : null)
  const [start, setStart] = useState(toLocalInput(existing?.startUtc ?? defaultStart))
  const [end, setEnd] = useState(existing ? toLocalInput(existing.endUtc) : '')
  const [withoutApproval, setWithoutApproval] = useState(existing ? !existing.requiresApproval : false)
  const [notes, setNotes] = useState(existing?.notes ?? '')
  const [files, setFiles] = useState<File[]>([])
  const [fileErrors, setFileErrors] = useState<string[]>([])
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [progress, setProgress] = useState<{ name: string; percent: number } | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const sourceCheck = useQuery({
    queryKey: ['delegations', 'source-check', from?.samAccountName],
    queryFn: () => delegationsApi.sourceCheck(from!.samAccountName),
    enabled: !!from && !endOnly,
  })

  const addFiles = (list: FileList | null) => {
    if (!list) return
    const accepted: File[] = []
    const rejected: string[] = []
    Array.from(list).forEach((f) => {
      const error = validateFile(f, config, t)
      if (error) rejected.push(error)
      else accepted.push(f)
    })
    setFiles((prev) => [...prev, ...accepted])
    setFileErrors(rejected)
  }

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        fromSam: from?.samAccountName ?? '',
        toSam: to?.samAccountName ?? '',
        startUtc: start ? fromLocalInput(start) : '',
        endUtc: end ? fromLocalInput(end) : '',
        requiresApproval: !withoutApproval,
        notes: notes.trim() || null,
        rowVersion: existing?.rowVersion,
      }
      const saved = isEdit ? await delegationsApi.update(existing.id, body) : await delegationsApi.create(body)

      // Delegarea e deja salvată: o eroare la upload nu trebuie să ducă la o a doua creare.
      let uploadError: string | null = null
      for (const file of files) {
        try {
          setProgress({ name: file.name, percent: 0 })
          await uploadAttachment(saved.id, file, (percent) => setProgress({ name: file.name, percent }))
        } catch (e) {
          uploadError = `${file.name}: ${(e as Error).message}`
          break
        }
      }
      return { saved, uploadError }
    },
    onSuccess: async ({ saved, uploadError }) => {
      await queryClient.invalidateQueries({ queryKey: ['delegations'] })
      navigate(`/delegations/${saved.id}`, { state: { uploadError } })
    },
    onError: async (error) => {
      setProgress(null)
      if (error instanceof ApiError && error.errors) {
        setErrors(Object.fromEntries(Object.entries(error.errors).map(([k, v]) => [k, v.join(' ')])))
        setFormError(t('form.fixErrors'))
      } else {
        setFormError((error as Error).message)
      }
      await queryClient.invalidateQueries({ queryKey: ['delegations'] })
    },
  })

  const localErrors = () => {
    const e: Record<string, string> = {}
    if (!from) e.fromSam = t('form.required')
    if (!to) e.toSam = t('form.required')
    if (!start) e.startUtc = t('form.required')
    if (!end) e.endUtc = t('form.required')
    if (start && end && new Date(end) <= new Date(start)) e.endUtc = t('form.endAfterStart')
    return e
  }

  const submit = () => {
    const e = localErrors()
    setErrors(e)
    setFormError(null)
    if (Object.keys(e).length === 0) save.mutate()
  }

  const current = sourceCheck.data?.currentForwarding
  const otherOpen = sourceCheck.data?.openDelegations.filter((d) => d.id !== existing?.id) ?? []

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2 }}>
        <Typography variant="h5">
          {isEdit ? t('form.editTitle', { id: existing.id }) : t('nav.newDelegation')}
        </Typography>
        {existing && <StatusChip status={existing.status} />}
      </Stack>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 8 }}>
          <Card>
            <CardContent>
              <Stack spacing={2}>
                {endOnly && <Alert severity="info">{t('form.activeEndOnly')}</Alert>}
                {isEdit && existing.status === 'Scheduled' && existing.requiresApproval && (
                  <Alert severity="warning">{t('form.reapproval')}</Alert>
                )}

                <UserPicker label={t('form.from')} value={from} onChange={setFrom} error={errors.fromSam} disabled={endOnly} />
                {current?.forwardingAddress && (
                  <Alert severity="warning">{t('form.existingForward', { address: current.forwardingAddress })}</Alert>
                )}
                {current?.forwardingSmtpAddress && (
                  <Alert severity="warning">{t('form.existingSmtpForward', { address: current.forwardingSmtpAddress })}</Alert>
                )}
                {sourceCheck.data?.exchangeError && (
                  <Alert severity="info">{t('form.exchangeCheckFailed', { error: sourceCheck.data.exchangeError })}</Alert>
                )}
                {otherOpen.length > 0 && (
                  <Alert severity="info">
                    {t('form.otherOpen')}{' '}
                    {otherOpen
                      .map((d) => `#${d.id} → ${d.to.displayName} (${formatDateTime(d.startUtc)} – ${formatDateTime(d.endUtc)})`)
                      .join('; ')}
                  </Alert>
                )}

                <UserPicker label={t('form.to')} value={to} onChange={setTo} error={errors.toSam} disabled={endOnly} />

                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                  <TextField
                    fullWidth
                    type="datetime-local"
                    label={t('form.start')}
                    value={start}
                    onChange={(e) => setStart(e.target.value)}
                    disabled={endOnly}
                    error={!!errors.startUtc}
                    helperText={errors.startUtc}
                    slotProps={{ inputLabel: { shrink: true } }}
                  />
                  <TextField
                    fullWidth
                    type="datetime-local"
                    label={t('form.end')}
                    value={end}
                    onChange={(e) => setEnd(e.target.value)}
                    error={!!errors.endUtc}
                    helperText={errors.endUtc}
                    slotProps={{ inputLabel: { shrink: true } }}
                  />
                </Stack>

                <Box>
                  <FormControlLabel
                    control={
                      <Checkbox
                        checked={withoutApproval}
                        onChange={(e) => setWithoutApproval(e.target.checked)}
                        disabled={isEdit}
                      />
                    }
                    label={t('form.withoutApproval')}
                  />
                  <Typography variant="body2" color="text.secondary">
                    {withoutApproval ? t('form.withoutApprovalHelp') : t('form.withApprovalHelp')}
                  </Typography>
                </Box>

                <TextField
                  label={t('form.notes')}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  multiline
                  minRows={2}
                  slotProps={{ htmlInput: { maxLength: 2000 } }}
                />

                {!isEdit && (
                  <Box>
                    <Button component="label" variant="outlined" startIcon={<UploadFileIcon />}>
                      {t('form.addFiles')}
                      <input
                        hidden
                        multiple
                        type="file"
                        accept={config.allowedExtensions.join(',')}
                        onChange={(e) => {
                          addFiles(e.target.files)
                          e.target.value = ''
                        }}
                      />
                    </Button>
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                      {t('form.filesHelp', { max: config.maxAttachmentMb, list: config.allowedExtensions.join(', ') })}
                    </Typography>
                    {fileErrors.map((e) => (
                      <Alert key={e} severity="error" sx={{ mt: 1 }}>
                        {e}
                      </Alert>
                    ))}
                    {errors.files && <Alert severity="error">{errors.files}</Alert>}
                    <List dense>
                      {files.map((f, i) => (
                        <ListItem
                          key={`${f.name}-${i}`}
                          secondaryAction={
                            <IconButton edge="end" onClick={() => setFiles(files.filter((_, j) => j !== i))}>
                              <DeleteIcon />
                            </IconButton>
                          }
                        >
                          <ListItemText primary={f.name} secondary={formatSize(f.size)} />
                        </ListItem>
                      ))}
                    </List>
                  </Box>
                )}

                {progress && (
                  <Box>
                    <Typography variant="body2">
                      {t('form.uploading', { name: progress.name })} {progress.percent}%
                    </Typography>
                    <LinearProgress variant="determinate" value={progress.percent} />
                  </Box>
                )}
                {formError && <Alert severity="error">{formError}</Alert>}

                <Stack direction="row" spacing={2}>
                  <Button variant="contained" onClick={submit} disabled={save.isPending}>
                    {isEdit ? t('form.save') : t('form.create')}
                  </Button>
                  <Button onClick={() => navigate(-1)} disabled={save.isPending}>
                    {t('form.cancel')}
                  </Button>
                </Stack>
              </Stack>
            </CardContent>
          </Card>
        </Grid>

        <Grid size={{ xs: 12, md: 4 }}>
          <Card>
            <CardContent>
              <Typography variant="subtitle1" gutterBottom>
                {t('form.registration')}
              </Typography>
              <ReadOnlyParties
                createdBy={existing?.createdBy.displayName ?? me?.displayName ?? ''}
                createdUtc={existing?.createdUtc}
                approvedBy={existing?.approvedBy?.displayName}
                approvedUtc={existing?.approvedUtc}
                notifications={existing?.notifications}
              />
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </>
  )
}
