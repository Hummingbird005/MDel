import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  AppBar,
  Box,
  Button,
  Chip,
  Container,
  MenuItem,
  Select,
  Stack,
  Toolbar,
  Typography,
} from '@mui/material'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet } from 'react-router-dom'
import { api } from '../api/client'
import { hasRole, type AppRole, type CurrentUser } from '../auth/useCurrentUser'
import { supportedLanguages, type Language } from '../i18n'

interface NavItem {
  to: string
  labelKey: string
  role?: AppRole
}

const navItems: NavItem[] = [
  { to: '/', labelKey: 'nav.dashboard' },
  { to: '/delegations/new', labelKey: 'nav.newDelegation', role: 'Admin' },
  { to: '/approvals', labelKey: 'nav.approvals', role: 'Approver' },
  { to: '/admin', labelKey: 'nav.admin', role: 'Admin' },
  { to: '/audit', labelKey: 'nav.audit', role: 'Admin' },
]

export function AppLayout({ user }: { user: CurrentUser }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()

  const saveLanguage = useMutation({
    mutationFn: (lang: Language) => api.put('/api/me/language', { language: lang }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  })

  const changeLanguage = (lang: Language) => {
    void i18n.changeLanguage(lang)
    saveLanguage.mutate(lang)
  }

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="static">
        <Toolbar sx={{ gap: 2, flexWrap: 'wrap' }}>
          <Typography variant="h6" component="div" sx={{ mr: 2 }}>
            {t('app.title')}
          </Typography>
          <Stack direction="row" spacing={1} sx={{ flexGrow: 1, flexWrap: 'wrap' }}>
            {navItems
              .filter((item) => !item.role || hasRole(user, item.role))
              .map((item) => (
                <Button
                  key={item.to}
                  component={NavLink}
                  to={item.to}
                  end={item.to === '/'}
                  color="inherit"
                  sx={{ '&.active': { bgcolor: 'rgba(255,255,255,0.15)' } }}
                >
                  {t(item.labelKey)}
                </Button>
              ))}
          </Stack>
          <Select
            size="small"
            value={(i18n.resolvedLanguage ?? 'ro') as Language}
            onChange={(e) => changeLanguage(e.target.value as Language)}
            aria-label={t('language.label')}
            sx={{ color: 'inherit', '.MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' } }}
          >
            {supportedLanguages.map((lng) => (
              <MenuItem key={lng} value={lng}>
                {t(`language.${lng}`)}
              </MenuItem>
            ))}
          </Select>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="body2">{user.displayName}</Typography>
            {user.roles.map((role) => (
              <Chip key={role} size="small" label={t(`roles.${role}`)} color="secondary" />
            ))}
          </Stack>
        </Toolbar>
      </AppBar>
      <Container maxWidth="xl" sx={{ py: 3 }}>
        <Outlet />
      </Container>
    </Box>
  )
}
