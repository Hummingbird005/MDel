import { Box, CircularProgress, Container } from '@mui/material'
import { useEffect, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Route, Routes } from 'react-router-dom'
import { hasRole, useCurrentUser, type AppRole, type CurrentUser } from './auth/useCurrentUser'
import { AppLayout } from './layout/AppLayout'
import { DashboardPage } from './pages/DashboardPage'
import { MessagePage, PlaceholderPage } from './pages/PlaceholderPage'

function RequireRole({ user, role, children }: { user: CurrentUser; role: AppRole; children: ReactNode }) {
  return hasRole(user, role) ? children : <MessagePage messageKey="common.forbidden" />
}

export default function App() {
  const { data: user, isLoading, isError } = useCurrentUser()
  const { i18n } = useTranslation()

  // Limba salvată pe server are prioritate față de cea detectată din browser.
  useEffect(() => {
    if (user && user.preferredLanguage !== i18n.resolvedLanguage) {
      void i18n.changeLanguage(user.preferredLanguage)
    }
  }, [user, i18n])

  if (isLoading) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  if (isError || !user) {
    return (
      <Container sx={{ py: 4 }}>
        <MessagePage messageKey="common.authError" severity="error" />
      </Container>
    )
  }

  return (
    <Routes>
      <Route element={<AppLayout user={user} />}>
        <Route index element={<DashboardPage />} />
        <Route
          path="delegations/new"
          element={
            <RequireRole user={user} role="Admin">
              <PlaceholderPage titleKey="nav.newDelegation" />
            </RequireRole>
          }
        />
        <Route
          path="approvals"
          element={
            <RequireRole user={user} role="Approver">
              <PlaceholderPage titleKey="nav.approvals" />
            </RequireRole>
          }
        />
        <Route
          path="admin"
          element={
            <RequireRole user={user} role="Admin">
              <PlaceholderPage titleKey="nav.admin" />
            </RequireRole>
          }
        />
        <Route
          path="audit"
          element={
            <RequireRole user={user} role="Admin">
              <PlaceholderPage titleKey="nav.audit" />
            </RequireRole>
          }
        />
        <Route path="*" element={<MessagePage messageKey="common.notFound" />} />
      </Route>
    </Routes>
  )
}
