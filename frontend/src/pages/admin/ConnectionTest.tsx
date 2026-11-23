import { Alert, Button, Card, CardContent, Stack, TextField, Typography } from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { adminApi, type MailboxForwarding, type TestTarget } from '../../api/admin'

/** Buton „Testează conexiunea” (folosește setările salvate) + extra pentru SMTP și Exchange. */
export function ConnectionTest({ target }: { target: TestTarget }) {
  const { t } = useTranslation()
  const [sendTo, setSendTo] = useState('')

  const test = useMutation({
    mutationFn: () => adminApi.test(target, target === 'smtp' ? { sendTo: sendTo || null } : undefined),
  })

  return (
    <Card>
      <CardContent>
        <Typography variant="subtitle1" gutterBottom>
          {t('admin.testTitle')}
        </Typography>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          {t('admin.testHint')}
        </Typography>
        <Stack spacing={2} sx={{ maxWidth: 720 }}>
          {target === 'smtp' && (
            <TextField
              label={t('admin.smtpSendTo')}
              value={sendTo}
              onChange={(e) => setSendTo(e.target.value)}
              size="small"
            />
          )}
          <Stack direction="row" spacing={2} alignItems="center">
            <Button variant="outlined" disabled={test.isPending} onClick={() => test.mutate()}>
              {test.isPending ? t('admin.testing') : t('admin.test')}
            </Button>
          </Stack>
          {test.data && <Alert severity={test.data.success ? 'success' : 'error'}>{test.data.message}</Alert>}
          {test.error && <Alert severity="error">{(test.error as Error).message}</Alert>}
          {target === 'exchange' && <ForwardingLookup />}
        </Stack>
      </CardContent>
    </Card>
  )
}

/** Diagnostic read-only: forwardul curent al unei căsuțe. */
function ForwardingLookup() {
  const { t } = useTranslation()
  const [mailbox, setMailbox] = useState('')
  const lookup = useMutation({ mutationFn: (m: string) => adminApi.getForwarding(m) })

  const show = (f: MailboxForwarding) =>
    [
      `${t('admin.forwardingAddress')}: ${f.forwardingAddress ?? '–'}`,
      `${t('admin.keepCopy')}: ${f.deliverToMailboxAndForward ? t('admin.yes') : t('admin.no')}`,
      f.forwardingSmtpAddress ? `${t('admin.forwardingSmtp')}: ${f.forwardingSmtpAddress}` : null,
    ]
      .filter(Boolean)
      .join(' · ')

  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2">{t('admin.forwardingLookup')}</Typography>
      <Stack direction="row" spacing={1}>
        <TextField
          size="small"
          fullWidth
          label={t('admin.mailbox')}
          placeholder="ion.popescu@contoso.local"
          value={mailbox}
          onChange={(e) => setMailbox(e.target.value)}
        />
        <Button disabled={!mailbox.trim() || lookup.isPending} onClick={() => lookup.mutate(mailbox.trim())}>
          {t('admin.check')}
        </Button>
      </Stack>
      {lookup.data && <Alert severity="info">{show(lookup.data)}</Alert>}
      {lookup.error && <Alert severity="error">{(lookup.error as Error).message}</Alert>}
    </Stack>
  )
}
