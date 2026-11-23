import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { PlugZap, Search, Trash2, UserPlus, Users } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { adminApi, type ConnectionTestResult, type MailboxForwarding, type SettingView, type TestTarget } from '../../api/admin'
import { ApiError } from '../../api/client'
import { PageHeader } from '../../components/layout/AppShell'
import type { PickedUser } from '../../components/pickedUser'
import { Alert, Avatar, Button, Card, CardHeader, EmptyState, Field, IconButton, inputClass, Spinner } from '../../components/ui/primitives'
import { useToast } from '../../components/ui/toast'
import { UserPicker } from '../../components/UserPicker'
import { cx } from '../../lib/cx'
import { formatDateTime } from '../../lib/format'

type FieldKind = 'text' | 'secret' | 'bool' | 'number' | 'language'

/** Tab-urile: categoriile de setări + grupul de informare. */
const tabs: { id: string; test?: TestTarget }[] = [
  { id: 'ActiveDirectory', test: 'ad' },
  { id: 'Groups' },
  { id: 'InformGroup' },
  { id: 'Exchange', test: 'exchange' },
  { id: 'Smtp', test: 'smtp' },
  { id: 'Attachments', test: 'storage' },
  { id: 'Retention' },
  { id: 'General' },
]

const kinds: Record<string, FieldKind> = {
  'Exchange.KeepCopyInSource': 'bool',
  'Smtp.UseTls': 'bool',
  'General.PreventSelfApproval': 'bool',
  'Smtp.Port': 'number',
  'Attachments.MaxSizeMb': 'number',
  'Retention.AttachmentsDays': 'number',
  'Retention.AuditDays': 'number',
  'General.DefaultLanguage': 'language',
}

const labelKey = (key: string) => `admin.fields.${key.replace(/\./g, '_')}`
const helpKey = (key: string) => `admin.help.${key.replace(/\./g, '_')}`

export function AdminPage() {
  const { t } = useTranslation()
  const [tab, setTab] = useState(tabs[0].id)
  const query = useQuery({ queryKey: ['admin', 'settings'], queryFn: adminApi.getSettings })
  const current = tabs.find((x) => x.id === tab)!

  return (
    <>
      <PageHeader title={t('nav.admin')} subtitle={t('admin.subtitle')} />
      <div className="flex gap-1 overflow-x-auto border-b border-line" role="tablist">
        {tabs.map((x) => (
          <button
            key={x.id}
            role="tab"
            aria-selected={tab === x.id}
            onClick={() => setTab(x.id)}
            className={cx(
              '-mb-px whitespace-nowrap border-b-2 px-3 py-2 text-sm font-medium transition-colors',
              tab === x.id ? 'border-accent text-accent-text' : 'border-transparent text-muted hover:text-fg',
            )}
          >
            {t(`admin.categories.${x.id}`)}
          </button>
        ))}
      </div>

      {tab === 'InformGroup' ? (
        <InformGroupPanel />
      ) : query.isLoading ? (
        <Spinner />
      ) : query.isError || !query.data ? (
        <Alert tone="danger">{t('admin.loadError')}</Alert>
      ) : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-5">
          <div className="xl:col-span-3">
            <SettingsForm key={tab} settings={query.data.settings.filter((s) => s.category === tab)} maxAttachmentMb={query.data.maxAttachmentMb} />
          </div>
          {current.test && (
            <div className="xl:col-span-2">
              <ConnectionTest target={current.test} />
            </div>
          )}
        </div>
      )}
    </>
  )
}

function SettingsForm({ settings, maxAttachmentMb }: { settings: SettingView[]; maxAttachmentMb: number }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const toast = useToast()
  const initial = useMemo(() => Object.fromEntries(settings.map((s) => [s.key, s.isSecret ? null : (s.value ?? '')])), [settings])
  const [values, setValues] = useState<Record<string, string | null>>(initial)
  const [errors, setErrors] = useState<Record<string, string>>({})

  const changed = Object.fromEntries(Object.entries(values).filter(([k, v]) => v !== initial[k]))
  const dirty = Object.keys(changed).length > 0
  const set = (key: string, value: string | null) => setValues((v) => ({ ...v, [key]: value }))

  const save = useMutation({
    mutationFn: () => adminApi.updateSettings(changed),
    onSuccess: async () => {
      setErrors({})
      toast(t('admin.saved'))
      await queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] })
    },
    onError: (error) => {
      if (error instanceof ApiError && error.errors) {
        setErrors(Object.fromEntries(Object.entries(error.errors).map(([k, v]) => [k, v.join(' ')])))
      } else {
        toast((error as Error).message, 'danger')
      }
    },
  })

  return (
    <Card>
      <form
        className="flex flex-col gap-5 p-5"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        {settings.map((s) => {
          const kind: FieldKind = s.isSecret ? 'secret' : (kinds[s.key] ?? 'text')
          const label = t(labelKey(s.key), { defaultValue: s.key })
          const help = t(helpKey(s.key), { defaultValue: '', max: maxAttachmentMb })
          const id = `setting-${s.key.replace(/\./g, '-')}`
          const error = errors[s.key]

          if (kind === 'bool') {
            const on = values[s.key] === 'true'
            return (
              <div key={s.key} className="flex items-start justify-between gap-4">
                <div>
                  <label htmlFor={id} className="text-sm font-medium">
                    {label}
                  </label>
                  {help && <p className="text-xs text-muted">{help}</p>}
                </div>
                <button
                  id={id}
                  type="button"
                  role="switch"
                  aria-checked={on}
                  onClick={() => set(s.key, String(!on))}
                  className={cx('relative h-6 w-11 shrink-0 rounded-full transition-colors', on ? 'bg-accent' : 'bg-line')}
                >
                  <span className={cx('absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all', on ? 'left-[22px]' : 'left-0.5')} />
                </button>
              </div>
            )
          }

          if (kind === 'secret') {
            return (
              <Field key={s.key} label={label} htmlFor={id} error={error} hint={values[s.key] === '' ? t('admin.secretWillClear') : help}>
                <div className="flex gap-2">
                  <input
                    id={id}
                    type="password"
                    autoComplete="new-password"
                    className={inputClass(error)}
                    placeholder={s.hasValue ? t('admin.secretSet') : t('admin.secretNotSet')}
                    value={values[s.key] ?? ''}
                    onChange={(e) => set(s.key, e.target.value === '' ? null : e.target.value)}
                  />
                  {s.hasValue && (
                    <Button type="button" variant="ghost" onClick={() => set(s.key, '')}>
                      {t('admin.clear')}
                    </Button>
                  )}
                </div>
              </Field>
            )
          }

          return (
            <Field key={s.key} label={label} htmlFor={id} error={error} hint={help}>
              {kind === 'language' ? (
                <select id={id} className={inputClass(error)} value={values[s.key] ?? 'ro'} onChange={(e) => set(s.key, e.target.value)}>
                  <option value="ro">{t('language.ro')}</option>
                  <option value="en">{t('language.en')}</option>
                </select>
              ) : (
                <input
                  id={id}
                  type={kind === 'number' ? 'number' : 'text'}
                  className={inputClass(error)}
                  value={values[s.key] ?? ''}
                  onChange={(e) => set(s.key, e.target.value)}
                />
              )}
            </Field>
          )
        })}
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={!dirty} loading={save.isPending}>
            {t('admin.save')}
          </Button>
          {Object.keys(errors).length > 0 && <span className="text-sm text-danger">{t('admin.validationError')}</span>}
        </div>
      </form>
    </Card>
  )
}

function ConnectionTest({ target }: { target: TestTarget }) {
  const { t } = useTranslation()
  const [sendTo, setSendTo] = useState('')
  const [mailbox, setMailbox] = useState('')
  const test = useMutation<ConnectionTestResult>({
    mutationFn: () => adminApi.test(target, target === 'smtp' ? { sendTo: sendTo || null } : undefined),
  })
  const lookup = useMutation<MailboxForwarding, Error, string>({ mutationFn: (m) => adminApi.getForwarding(m) })

  return (
    <Card>
      <CardHeader title={t('admin.testTitle')} />
      <div className="flex flex-col gap-4 p-5">
        <p className="text-sm text-muted">{t('admin.testHint')}</p>
        {target === 'smtp' && (
          <Field label={t('admin.smtpSendTo')} htmlFor="smtp-send-to">
            <input id="smtp-send-to" className={inputClass()} value={sendTo} onChange={(e) => setSendTo(e.target.value)} />
          </Field>
        )}
        <Button variant="secondary" className="w-fit" loading={test.isPending} onClick={() => test.mutate()}>
          <PlugZap size={15} /> {test.isPending ? t('admin.testing') : t('admin.test')}
        </Button>
        {test.data && <Alert tone={test.data.success ? 'success' : 'danger'}>{test.data.message}</Alert>}
        {test.error && <Alert tone="danger">{(test.error as Error).message}</Alert>}

        {target === 'exchange' && (
          <div className="flex flex-col gap-2 border-t border-line pt-4">
            <label htmlFor="lookup-mailbox" className="text-sm font-medium">
              {t('admin.forwardingLookup')}
            </label>
            <div className="flex gap-2">
              <input
                id="lookup-mailbox"
                className={inputClass()}
                placeholder="ion.popescu@contoso.local"
                value={mailbox}
                onChange={(e) => setMailbox(e.target.value)}
              />
              <Button variant="secondary" disabled={!mailbox.trim()} loading={lookup.isPending} onClick={() => lookup.mutate(mailbox.trim())}>
                <Search size={15} /> {t('admin.check')}
              </Button>
            </div>
            {lookup.data && (
              <Alert>
                {t('admin.forwardingAddress')}: {lookup.data.forwardingAddress ?? '–'} · {t('admin.keepCopy')}:{' '}
                {lookup.data.deliverToMailboxAndForward ? t('admin.yes') : t('admin.no')}
                {lookup.data.forwardingSmtpAddress && ` · ${t('admin.forwardingSmtp')}: ${lookup.data.forwardingSmtpAddress}`}
              </Alert>
            )}
            {lookup.error && <Alert tone="danger">{lookup.error.message}</Alert>}
          </div>
        )}
      </div>
    </Card>
  )
}

function InformGroupPanel() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const toast = useToast()
  const [picked, setPicked] = useState<PickedUser | null>(null)
  const [error, setError] = useState<string | null>(null)
  const members = useQuery({ queryKey: ['admin', 'inform-group'], queryFn: adminApi.informGroup })

  const add = useMutation({
    mutationFn: (sam: string) => adminApi.addInformMember(sam),
    onSuccess: () => {
      setPicked(null)
      setError(null)
      toast(t('admin.informAdded'))
      void queryClient.invalidateQueries({ queryKey: ['admin', 'inform-group'] })
    },
    onError: (e) => setError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : (e as Error).message),
  })
  const remove = useMutation({
    mutationFn: (id: number) => adminApi.removeInformMember(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['admin', 'inform-group'] }),
  })

  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-5">
      <Card className="xl:col-span-3">
        <CardHeader title={t('admin.informMembers', { count: members.data?.length ?? 0 })} />
        {members.isLoading ? (
          <Spinner />
        ) : (members.data ?? []).length === 0 ? (
          <EmptyState icon={<Users size={26} />} title={t('admin.informEmpty')} text={t('admin.informHelp')} />
        ) : (
          <ul className="divide-y divide-line">
            {members.data!.map((m) => (
              <li key={m.id} className="flex items-center gap-3 px-4 py-2.5">
                <Avatar name={m.user.displayName} />
                <div className="min-w-0 flex-1 leading-tight">
                  <p className="truncate text-sm font-medium">{m.user.displayName}</p>
                  <p className="truncate text-xs text-muted">{m.user.email}</p>
                </div>
                <span className="hidden text-xs text-muted sm:block">
                  {t('admin.informAddedBy', { by: m.addedBy, when: formatDateTime(m.addedUtc) })}
                </span>
                <IconButton tone="danger" label={t('admin.informRemove', { name: m.user.displayName })} onClick={() => remove.mutate(m.id)}>
                  <Trash2 size={15} />
                </IconButton>
              </li>
            ))}
          </ul>
        )}
      </Card>
      <Card className="h-fit xl:col-span-2">
        <CardHeader title={t('admin.informAdd')} />
        <div className="flex flex-col gap-4 p-5">
          <p className="text-sm text-muted">{t('admin.informHelp')}</p>
          <Field label={t('admin.informUser')} htmlFor="inform-user" error={error ?? undefined}>
            <UserPicker id="inform-user" value={picked} onChange={setPicked} />
          </Field>
          <Button className="w-fit" disabled={!picked} loading={add.isPending} onClick={() => picked && add.mutate(picked.samAccountName)}>
            <UserPlus size={15} /> {t('admin.informAddButton')}
          </Button>
        </div>
      </Card>
    </div>
  )
}
