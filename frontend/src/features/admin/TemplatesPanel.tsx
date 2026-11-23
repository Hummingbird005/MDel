import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Mail, RotateCcw } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { adminApi, type SettingView, type TemplateKind, type TemplatesMeta } from '../../api/admin'
import { ApiError } from '../../api/client'
import { Alert, Button, Card, CardHeader, Field, inputClass, Spinner } from '../../components/ui/primitives'
import { useToast } from '../../components/ui/toast'
import { cx } from '../../lib/cx'

/**
 * Setări → Șabloane email: subiectul și textul fiecărui tip de mesaj, în română.
 * Tabelul cu detaliile delegării și butonul cu link se adaugă automat de server.
 */
export function TemplatesPanel({ settings }: { settings: SettingView[] }) {
  const { t } = useTranslation()
  const meta = useQuery({ queryKey: ['admin', 'templates'], queryFn: adminApi.templates, staleTime: Infinity })
  const [kind, setKind] = useState<TemplateKind>('Request')

  if (!meta.data) return meta.isError ? <Alert tone="danger">{(meta.error as Error).message}</Alert> : <Spinner />

  const value = (key: string) => settings.find((s) => s.key === key)?.value ?? ''
  const current = meta.data.templates.find((x) => x.kind === kind)!

  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-[280px_minmax(0,1fr)]">
      <Card className="h-fit">
        <CardHeader title={t('templates.types')} />
        <ul className="flex flex-col p-2" role="listbox" aria-label={t('templates.types')}>
          {meta.data.templates.map((x) => {
            const customized =
              value(x.subjectKey) !== x.defaultSubject && value(x.subjectKey) !== ''
                ? true
                : value(x.bodyKey) !== x.defaultBody && value(x.bodyKey) !== ''
            return (
              <li key={x.kind}>
                <button
                  role="option"
                  aria-selected={kind === x.kind}
                  onClick={() => setKind(x.kind)}
                  className={cx(
                    'flex w-full flex-col items-start gap-0.5 rounded-lg px-3 py-2 text-left',
                    kind === x.kind ? 'bg-accent/10' : 'hover:bg-subtle',
                  )}
                >
                  <span className={cx('flex w-full items-center gap-2 text-sm font-medium', kind === x.kind && 'text-accent-text')}>
                    {t(`templates.kinds.${x.kind}.name`)}
                    {customized && <span className="ml-auto rounded bg-accent/10 px-1.5 text-[10px] font-semibold uppercase text-accent-text">{t('templates.custom')}</span>}
                  </span>
                  <span className="text-xs text-muted">{t(`templates.kinds.${x.kind}.when`)}</span>
                </button>
              </li>
            )
          })}
        </ul>
      </Card>
      <TemplateEditor
        key={kind}
        meta={meta.data}
        kind={kind}
        initialSubject={value(current.subjectKey) || current.defaultSubject}
        initialBody={value(current.bodyKey) || current.defaultBody}
      />
    </div>
  )
}

function TemplateEditor({
  meta,
  kind,
  initialSubject,
  initialBody,
}: {
  meta: TemplatesMeta
  kind: TemplateKind
  initialSubject: string
  initialBody: string
}) {
  const { t } = useTranslation()
  const toast = useToast()
  const queryClient = useQueryClient()
  const template = meta.templates.find((x) => x.kind === kind)!
  const [subject, setSubject] = useState(initialSubject)
  const [body, setBody] = useState(initialBody)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [preview, setPreview] = useState<{ subject: string; html: string } | null>(null)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const lastFocused = useRef<'subject' | 'body'>('body')
  const subjectRef = useRef<HTMLInputElement>(null)
  const bodyRef = useRef<HTMLTextAreaElement>(null)

  const dirty = subject !== initialSubject || body !== initialBody
  const isDefault = subject === template.defaultSubject && body === template.defaultBody

  // Previzualizare cu întârziere de 400 ms după tastare.
  useEffect(() => {
    const h = setTimeout(() => {
      adminApi
        .previewTemplate(kind, subject, body)
        .then((p) => {
          setPreview(p)
          setPreviewError(null)
        })
        .catch((e) => setPreviewError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : (e as Error).message))
    }, 400)
    return () => clearTimeout(h)
  }, [kind, subject, body])

  const save = useMutation({
    mutationFn: () => adminApi.updateSettings({ [template.subjectKey]: subject, [template.bodyKey]: body }),
    onSuccess: async () => {
      setErrors({})
      toast(t('templates.saved'))
      await queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] })
    },
    onError: (e) => {
      if (e instanceof ApiError && e.errors) {
        setErrors({
          subject: e.errors[template.subjectKey]?.join(' ') ?? '',
          body: e.errors[template.bodyKey]?.join(' ') ?? '',
        })
      } else toast((e as Error).message, 'danger')
    },
  })

  /** Inserează {câmp} la poziția cursorului, în ultimul câmp folosit. */
  const insert = (name: string) => {
    const token = `{${name}}`
    const target = lastFocused.current === 'subject' ? subjectRef.current : bodyRef.current
    const current = lastFocused.current === 'subject' ? subject : body
    const setter = lastFocused.current === 'subject' ? setSubject : setBody
    const start = target?.selectionStart ?? current.length
    const end = target?.selectionEnd ?? current.length
    setter(current.slice(0, start) + token + current.slice(end))
    requestAnimationFrame(() => {
      target?.focus()
      target?.setSelectionRange(start + token.length, start + token.length)
    })
  }

  return (
    <div className="grid min-w-0 grid-cols-1 gap-4 2xl:grid-cols-2">
      <Card className="min-w-0">
        <CardHeader title={t(`templates.kinds.${kind}.name`)} />
        <form
          className="flex flex-col gap-4 p-5"
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <p className="text-sm text-muted">
            {t(`templates.kinds.${kind}.when`)} · {t('templates.recipients')}: {t(`templates.kinds.${kind}.to`)}
          </p>
          <Field label={t('templates.subject')} htmlFor="template-subject" error={errors.subject || undefined}>
            <input
              id="template-subject"
              ref={subjectRef}
              className={inputClass(errors.subject)}
              value={subject}
              maxLength={300}
              onFocus={() => (lastFocused.current = 'subject')}
              onChange={(e) => setSubject(e.target.value)}
            />
          </Field>
          <Field label={t('templates.body')} htmlFor="template-body" error={errors.body || undefined} hint={t('templates.bodyHint')}>
            <textarea
              id="template-body"
              ref={bodyRef}
              rows={7}
              maxLength={4000}
              className={inputClass(errors.body, 'h-auto py-2 font-mono text-[13px] leading-relaxed')}
              value={body}
              onFocus={() => (lastFocused.current = 'body')}
              onChange={(e) => setBody(e.target.value)}
            />
          </Field>
          <div className="flex flex-col gap-2">
            <p className="text-xs font-medium text-muted">{t('templates.fields')}</p>
            <div className="flex flex-wrap gap-1.5">
              {meta.placeholders.map((p) => (
                <button
                  key={p}
                  type="button"
                  onClick={() => insert(p)}
                  title={t(`templates.placeholders.${p}`)}
                  className="rounded-md bg-subtle px-2 py-1 font-mono text-xs text-fg ring-1 ring-inset ring-line hover:ring-accent"
                >
                  {`{${p}}`}
                </button>
              ))}
            </div>
            <p className="text-xs text-muted">{t('templates.fieldsHint')}</p>
          </div>
          <Alert>{t('templates.languageNote')}</Alert>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={!dirty} loading={save.isPending}>
              {t('admin.save')}
            </Button>
            <Button
              type="button"
              variant="ghost"
              disabled={isDefault}
              onClick={() => {
                setSubject(template.defaultSubject)
                setBody(template.defaultBody)
              }}
            >
              <RotateCcw size={14} /> {t('templates.reset')}
            </Button>
          </div>
        </form>
      </Card>

      <Card className="min-w-0">
        <CardHeader
          title={
            <span className="inline-flex items-center gap-2">
              <Mail size={15} className="text-muted" aria-hidden="true" /> {t('templates.preview')}
            </span>
          }
        />
        <div className="flex flex-col gap-3 p-5">
          {previewError ? (
            <Alert tone="danger">{previewError}</Alert>
          ) : !preview ? (
            <Spinner />
          ) : (
            <>
              <div className="rounded-lg bg-subtle px-3 py-2 text-sm">
                <span className="text-muted">{t('templates.subject')}: </span>
                <span className="font-medium">{preview.subject}</span>
              </div>
              {/* Previzualizare izolată: fără scripturi, cu fundal alb ca în clientul de email. */}
              <iframe
                title={t('templates.preview')}
                sandbox=""
                srcDoc={`<!doctype html><html><body style="margin:16px;background:#ffffff">${preview.html}</body></html>`}
                className="h-[420px] w-full rounded-lg bg-white ring-1 ring-line"
              />
              <p className="text-xs text-muted">{t('templates.previewHint')}</p>
            </>
          )}
        </div>
      </Card>
    </div>
  )
}
