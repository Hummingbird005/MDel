import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { Paperclip, X } from 'lucide-react'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { delegationsApi, uploadAttachment, type DelegationDetail, type FormConfig } from '../../api/delegations'
import { useCurrentUser } from '../../auth/useCurrentUser'
import { toPicked, type PickedUser } from '../../components/pickedUser'
import { ForwardModeBadge } from '../../components/ui/badges'
import { Alert, Button, Drawer, Field, inputClass, Spinner } from '../../components/ui/primitives'
import { useToast } from '../../components/ui/toast'
import { UserPicker } from '../../components/UserPicker'
import { formatDateTime, formatSize, fromLocalInput, toLocalInput } from '../../lib/format'
import { ReadOnlyParties } from './ReadOnlyParties'

interface DrawerApi {
  openCreate: () => void
  openEdit: (delegation: DelegationDetail) => void
}

const DrawerContext = createContext<DrawerApi>({ openCreate: () => {}, openEdit: () => {} })
// eslint-disable-next-line react-refresh/only-export-components
export const useDelegationDrawer = () => useContext(DrawerContext)

/** Drawer-ul „Adaugă delegare nouă” / „Editare”, deschis din orice pagină. */
export function DelegationDrawerProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ editing: DelegationDetail | null; key: number } | null>(null)
  const openCreate = useCallback(() => setState({ editing: null, key: Date.now() }), [])
  const openEdit = useCallback((d: DelegationDetail) => setState({ editing: d, key: Date.now() }), [])
  const close = useCallback(() => setState(null), [])

  return (
    <DrawerContext.Provider value={{ openCreate, openEdit }}>
      {children}
      {state && <DrawerLoader key={state.key} editing={state.editing} onClose={close} />}
    </DrawerContext.Provider>
  )
}

function DrawerLoader({ editing, onClose }: { editing: DelegationDetail | null; onClose: () => void }) {
  const config = useQuery({ queryKey: ['delegations', 'form-config'], queryFn: delegationsApi.formConfig })
  if (!config.data) {
    return (
      <Drawer title="…" onClose={onClose} onSubmit={() => {}} footer={null}>
        {config.isError ? <Alert tone="danger">{(config.error as Error).message}</Alert> : <Spinner />}
      </Drawer>
    )
  }
  return <DelegationForm config={config.data} editing={editing} onClose={onClose} />
}

function validateFile(file: File, config: FormConfig, t: TFunction): string | null {
  const ext = file.name.includes('.') ? `.${file.name.split('.').pop()!.toLowerCase()}` : ''
  if (config.allowedExtensions.length > 0 && !config.allowedExtensions.includes(ext))
    return t('form.fileExtension', { name: file.name, list: config.allowedExtensions.join(', ') })
  if (file.size > config.maxAttachmentMb * 1024 * 1024) return t('form.fileTooLarge', { name: file.name, max: config.maxAttachmentMb })
  if (file.size === 0) return t('form.fileEmpty', { name: file.name })
  return null
}

function DelegationForm({ config, editing, onClose }: { config: FormConfig; editing: DelegationDetail | null; onClose: () => void }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const toast = useToast()
  const { data: me } = useCurrentUser()

  const endOnly = editing?.status === 'Active'
  const tomorrow = new Date(Date.now() + 86_400_000)
  tomorrow.setHours(9, 0, 0, 0)

  const [from, setFrom] = useState<PickedUser | null>(editing ? toPicked(editing.from) : null)
  const [to, setTo] = useState<PickedUser | null>(editing ? toPicked(editing.to) : null)
  const [start, setStart] = useState(toLocalInput(editing?.startUtc ?? tomorrow))
  const [end, setEnd] = useState(editing ? toLocalInput(editing.endUtc) : '')
  const [withoutApproval, setWithoutApproval] = useState(editing ? !editing.requiresApproval : false)
  const [notes, setNotes] = useState(editing?.notes ?? '')
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
  const current = sourceCheck.data?.currentForwarding
  const otherOpen = sourceCheck.data?.openDelegations.filter((d) => d.id !== editing?.id) ?? []

  const addFiles = (list: FileList | null) => {
    if (!list) return
    const accepted: File[] = []
    const rejected: string[] = []
    Array.from(list).forEach((f) => {
      const e = validateFile(f, config, t)
      if (e) rejected.push(e)
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
        startUtc: fromLocalInput(start),
        endUtc: fromLocalInput(end),
        requiresApproval: !withoutApproval,
        notes: notes.trim() || null,
        rowVersion: editing?.rowVersion,
      }
      const saved = editing ? await delegationsApi.update(editing.id, body) : await delegationsApi.create(body)

      // Delegarea e salvată: o eroare la upload nu trebuie să ducă la o a doua creare.
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
      onClose()
      toast(
        editing
          ? t('toast.updated', { id: saved.id })
          : saved.status === 'PendingApproval'
            ? t('toast.sentForApproval', { id: saved.id })
            : t('toast.created', { id: saved.id }),
      )
      if (uploadError) toast(t('detail.uploadFailed', { error: uploadError }), 'danger')
      navigate(`/delegations/${saved.id}`)
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

  const submit = () => {
    const e: Record<string, string> = {}
    if (!from) e.fromSam = t('form.required')
    if (!to) e.toSam = t('form.required')
    if (!start) e.startUtc = t('form.required')
    if (!end) e.endUtc = t('form.required')
    if (start && end && new Date(end) <= new Date(start)) e.endUtc = t('form.endAfterStart')
    setErrors(e)
    setFormError(null)
    if (Object.keys(e).length === 0) save.mutate()
  }

  const submitLabel = editing ? t('form.save') : withoutApproval ? t('form.create') : t('form.sendForApproval')

  return (
    <Drawer
      title={editing ? t('form.editTitle', { id: editing.id }) : t('nav.newDelegation')}
      subtitle={t('form.subtitle')}
      closeLabel={t('common.close')}
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose} disabled={save.isPending}>
            {t('form.cancel')}
          </Button>
          <Button type="submit" loading={save.isPending}>
            {submitLabel}
          </Button>
        </>
      }
    >
      {endOnly && <Alert>{t('form.activeEndOnly')}</Alert>}
      {editing?.status === 'Scheduled' && editing.requiresApproval && <Alert tone="warning">{t('form.reapproval')}</Alert>}

      <Field label={t('form.from')} htmlFor="from" error={errors.fromSam}>
        <UserPicker id="from" value={from} onChange={setFrom} error={errors.fromSam} disabled={endOnly} exclude={to?.samAccountName} />
      </Field>
      {current?.forwardingAddress && <Alert tone="warning">{t('form.existingForward', { address: current.forwardingAddress })}</Alert>}
      {current?.forwardingSmtpAddress && <Alert tone="warning">{t('form.existingSmtpForward', { address: current.forwardingSmtpAddress })}</Alert>}
      {sourceCheck.data?.exchangeError && <Alert>{t('form.exchangeCheckFailed', { error: sourceCheck.data.exchangeError })}</Alert>}
      {otherOpen.length > 0 && (
        <Alert>
          {t('form.otherOpen')}{' '}
          {otherOpen.map((d) => `#${d.id} → ${d.to.displayName} (${formatDateTime(d.startUtc)} – ${formatDateTime(d.endUtc)})`).join('; ')}
        </Alert>
      )}

      <Field label={t('form.to')} htmlFor="to" error={errors.toSam}>
        <UserPicker id="to" value={to} onChange={setTo} error={errors.toSam} disabled={endOnly} exclude={from?.samAccountName} />
      </Field>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label={t('form.start')} htmlFor="start" error={errors.startUtc}>
          <input id="start" type="datetime-local" className={inputClass(errors.startUtc)} value={start} disabled={endOnly} onChange={(e) => setStart(e.target.value)} />
        </Field>
        <Field label={t('form.end')} htmlFor="end" error={errors.endUtc}>
          <input id="end" type="datetime-local" className={inputClass(errors.endUtc)} value={end} onChange={(e) => setEnd(e.target.value)} />
        </Field>
      </div>

      <div className="flex items-center gap-2 text-sm text-muted">
        <span>{t('form.forwardMode')}:</span>
        <ForwardModeBadge keepCopy={editing?.keepCopyInSource ?? config.keepCopyInSource} />
        <span className="text-xs">{t('form.forwardModeHelp')}</span>
      </div>

      <label className="flex cursor-pointer items-start gap-3 rounded-lg bg-subtle p-3">
        <input
          id="without-approval"
          type="checkbox"
          checked={withoutApproval}
          disabled={!!editing}
          onChange={(e) => setWithoutApproval(e.target.checked)}
          className="mt-0.5 h-4 w-4 accent-[rgb(var(--accent))]"
        />
        <span className="flex flex-col gap-0.5">
          <span className="text-sm font-medium">{t('form.withoutApproval')}</span>
          <span className="text-xs text-muted">{withoutApproval ? t('form.withoutApprovalHelp') : t('form.withApprovalHelp')}</span>
        </span>
      </label>

      <Field label={t('form.notes')} htmlFor="notes">
        <textarea
          id="notes"
          rows={3}
          maxLength={2000}
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          placeholder={t('form.notesPlaceholder')}
          className={inputClass(false, 'h-auto py-2')}
        />
      </Field>

      {!editing && (
        <Field
          label={t('detail.attachments')}
          htmlFor="files"
          error={errors.files ?? fileErrors.join(' ')}
          hint={t('form.filesHelp', { max: config.maxAttachmentMb, list: config.allowedExtensions.join(', ') })}
        >
          <label
            htmlFor="files"
            className="flex cursor-pointer items-center gap-3 rounded-lg border border-dashed border-line px-3 py-3 text-sm text-muted hover:border-accent/60 hover:text-fg"
          >
            <Paperclip size={16} aria-hidden="true" /> {t('form.addFiles')}
            <input
              id="files"
              type="file"
              multiple
              className="sr-only"
              accept={config.allowedExtensions.join(',')}
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
          </label>
          {files.length > 0 && (
            <ul className="flex flex-col gap-1">
              {files.map((f, i) => (
                <li key={`${f.name}-${i}`} className="flex items-center gap-2 text-sm">
                  <Paperclip size={13} className="text-accent-text" aria-hidden="true" />
                  <span className="truncate">{f.name}</span>
                  <span className="text-xs text-muted">{formatSize(f.size)}</span>
                  <button
                    type="button"
                    className="ml-auto text-muted hover:text-danger"
                    aria-label={t('form.removeFile', { name: f.name })}
                    onClick={() => setFiles(files.filter((_, j) => j !== i))}
                  >
                    <X size={14} />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Field>
      )}

      {progress && (
        <div className="flex flex-col gap-1">
          <p className="text-xs text-muted">
            {t('form.uploading', { name: progress.name })} {progress.percent}%
          </p>
          <div className="h-1.5 overflow-hidden rounded-full bg-subtle">
            <div className="h-full rounded-full bg-accent transition-all" style={{ width: `${progress.percent}%` }} />
          </div>
        </div>
      )}

      <ReadOnlyParties
        compact
        createdBy={editing?.createdBy.displayName ?? me?.displayName ?? ''}
        createdUtc={editing?.createdUtc}
        approvedBy={editing?.approvedBy?.displayName}
        approvedUtc={editing?.approvedUtc}
        notifications={editing?.notifications}
      />

      {formError && <Alert tone="danger">{formError}</Alert>}
    </Drawer>
  )
}
