import { Alert, Typography } from '@mui/material'
import { useTranslation } from 'react-i18next'

export function PlaceholderPage({ titleKey }: { titleKey: string }) {
  const { t } = useTranslation()
  return (
    <>
      <Typography variant="h5" gutterBottom>
        {t(titleKey)}
      </Typography>
      <Alert severity="info">{t('common.comingSoon')}</Alert>
    </>
  )
}

export function MessagePage({ messageKey, severity = 'warning' }: { messageKey: string; severity?: 'warning' | 'error' }) {
  const { t } = useTranslation()
  return <Alert severity={severity}>{t(messageKey)}</Alert>
}
