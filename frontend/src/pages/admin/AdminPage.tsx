import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { adminApi, type ConnectionTestResult, type SettingView } from '../../api/admin'
import { ApiError } from '../../api/client'
import { ConnectionTest } from './ConnectionTest'
import { categories, fieldKind, helpKey, labelKey } from './settingFields'

export function AdminPage() {
  const { t } = useTranslation()
  const [tab, setTab] = useState(categories[0].id)
  const query = useQuery({ queryKey: ['admin', 'settings'], queryFn: adminApi.getSettings })

  if (query.isLoading) return <CircularProgress />
  if (query.isError || !query.data) return <Alert severity="error">{t('admin.loadError')}</Alert>

  const category = categories.find((c) => c.id === tab)!
  const settings = query.data.settings.filter((s) => s.category === tab)

  return (
    <>
      <Typography variant="h5" gutterBottom>
        {t('nav.admin')}
      </Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" sx={{ mb: 2 }}>
        {categories.map((c) => (
          <Tab key={c.id} value={c.id} label={t(`admin.categories.${c.id}`)} />
        ))}
      </Tabs>
      {/* key: resetează formularul la schimbarea tab-ului */}
      <SettingsForm key={tab} settings={settings} maxAttachmentMb={query.data.maxAttachmentMb} />
      {category.test && <ConnectionTest target={category.test} />}
    </>
  )
}

function SettingsForm({ settings, maxAttachmentMb }: { settings: SettingView[]; maxAttachmentMb: number }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const initial = useMemo(
    () => Object.fromEntries(settings.map((s) => [s.key, s.isSecret ? null : (s.value ?? '')])),
    [settings],
  )
  const [values, setValues] = useState<Record<string, string | null>>(initial)
  const [result, setResult] = useState<ConnectionTestResult | null>(null)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  const changed = Object.fromEntries(Object.entries(values).filter(([k, v]) => v !== initial[k]))
  const dirty = Object.keys(changed).length > 0

  const save = useMutation({
    mutationFn: () => adminApi.updateSettings(changed),
    onSuccess: async () => {
      setErrors({})
      setResult({ success: true, message: t('admin.saved') })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] })
    },
    onError: (error) => {
      const validation = error instanceof ApiError ? error.errors : undefined
      setErrors(validation ?? {})
      setResult({ success: false, message: validation ? t('admin.validationError') : (error as Error).message })
    },
  })

  const set = (key: string, value: string | null) => setValues((v) => ({ ...v, [key]: value }))

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Stack spacing={2} sx={{ maxWidth: 720 }}>
          {settings.map((s) => {
            const kind = fieldKind(s.key, s.isSecret)
            const error = errors[s.key]?.join(' ')
            const help = t(helpKey(s.key), { defaultValue: '', max: maxAttachmentMb })
            const label = t(labelKey(s.key), { defaultValue: s.key })

            if (kind === 'bool') {
              return (
                <FormControlLabel
                  key={s.key}
                  control={
                    <Switch checked={values[s.key] === 'true'} onChange={(e) => set(s.key, String(e.target.checked))} />
                  }
                  label={label}
                />
              )
            }

            if (kind === 'secret') {
              return (
                <Stack key={s.key} direction="row" spacing={1} alignItems="flex-start">
                  <TextField
                    fullWidth
                    type="password"
                    autoComplete="new-password"
                    label={label}
                    value={values[s.key] ?? ''}
                    placeholder={s.hasValue ? t('admin.secretSet') : t('admin.secretNotSet')}
                    slotProps={{ inputLabel: { shrink: true } }}
                    onChange={(e) => set(s.key, e.target.value === '' ? null : e.target.value)}
                    error={!!error}
                    helperText={error ?? (values[s.key] === '' ? t('admin.secretWillClear') : help)}
                  />
                  {s.hasValue && (
                    <Button sx={{ mt: 1 }} onClick={() => set(s.key, '')}>
                      {t('admin.clear')}
                    </Button>
                  )}
                </Stack>
              )
            }

            return (
              <TextField
                key={s.key}
                select={kind === 'language'}
                type={kind === 'number' ? 'number' : 'text'}
                label={label}
                value={values[s.key] ?? ''}
                onChange={(e) => set(s.key, e.target.value)}
                error={!!error}
                helperText={error ?? help}
              >
                {kind === 'language' &&
                  ['ro', 'en'].map((l) => (
                    <MenuItem key={l} value={l}>
                      {t(`language.${l}`)}
                    </MenuItem>
                  ))}
              </TextField>
            )
          })}

          {result && (
            <Alert severity={result.success ? 'success' : 'error'} onClose={() => setResult(null)}>
              {result.message}
            </Alert>
          )}

          <Box>
            <Button variant="contained" disabled={!dirty || save.isPending} onClick={() => save.mutate()}>
              {t('admin.save')}
            </Button>
          </Box>
        </Stack>
      </CardContent>
    </Card>
  )
}
