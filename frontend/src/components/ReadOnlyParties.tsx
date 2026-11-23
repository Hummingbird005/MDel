import { Box, Chip, Stack, TextField, Typography } from '@mui/material'
import { useTranslation } from 'react-i18next'
import type { Notification } from '../api/delegations'
import { formatDateTime } from './format'

interface Props {
  createdBy: string
  createdUtc?: string | null
  approvedBy?: string | null
  approvedUtc?: string | null
  notifications?: Notification[]
}

/** Câmpurile read-only cerute în formular: cine a înregistrat, cine a aprobat, cine a fost informat. */
export function ReadOnlyParties({ createdBy, createdUtc, approvedBy, approvedUtc, notifications = [] }: Props) {
  const { t } = useTranslation()

  // Un destinatar o singură dată, cu tipul lui.
  const informed = Array.from(
    new Map(notifications.map((n) => [`${n.recipientType}:${n.recipientEmail}`, n])).values(),
  )

  const withDate = (name: string, utc?: string | null) => (utc ? `${name} · ${formatDateTime(utc)}` : name)

  return (
    <Stack spacing={2}>
      <TextField
        label={t('form.createdBy')}
        value={withDate(createdBy, createdUtc)}
        slotProps={{ input: { readOnly: true } }}
        variant="filled"
      />
      <TextField
        label={t('form.approvedBy')}
        value={approvedBy ? withDate(approvedBy, approvedUtc) : '–'}
        slotProps={{ input: { readOnly: true } }}
        variant="filled"
      />
      <Box>
        <Typography variant="caption" color="text.secondary">
          {t('form.informed')}
        </Typography>
        {informed.length === 0 ? (
          <Typography variant="body2">–</Typography>
        ) : (
          <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap" sx={{ mt: 0.5 }}>
            {informed.map((n) => (
              <Chip
                key={`${n.recipientType}:${n.recipientEmail}`}
                size="small"
                variant="outlined"
                color={n.error ? 'error' : 'default'}
                label={`${n.recipientName ?? n.recipientEmail} (${t(`recipient.${n.recipientType}`)})`}
                title={n.error ?? n.recipientEmail}
              />
            ))}
          </Stack>
        )}
      </Box>
    </Stack>
  )
}
