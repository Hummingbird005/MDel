import { Card, CardContent, Typography } from '@mui/material'
import Grid from '@mui/material/Grid2'
import { useTranslation } from 'react-i18next'

const sections = ['active', 'scheduled', 'pending', 'expired'] as const

export function DashboardPage() {
  const { t } = useTranslation()

  return (
    <>
      <Typography variant="h5" gutterBottom>
        {t('nav.dashboard')}
      </Typography>
      <Grid container spacing={2}>
        {sections.map((section) => (
          <Grid key={section} size={{ xs: 12, sm: 6, md: 3 }}>
            <Card>
              <CardContent>
                <Typography color="text.secondary">{t(`dashboard.${section}`)}</Typography>
                <Typography variant="h4">–</Typography>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>
      <Typography sx={{ mt: 3 }} color="text.secondary">
        {t('dashboard.placeholder')}
      </Typography>
    </>
  )
}
