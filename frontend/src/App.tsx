import { Box, CircularProgress, Container } from '@mui/material'
import { useEffect, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Route, Routes } from 'react-router-dom'
import { hasRole, useCurrentUser, type AppRole, type CurrentUser } from './auth/useCurrentUser'
import { AppLayout } from './layout/AppLayout'
import { AdminPage } from './pages/admin/AdminPage'
import { DashboardPage } from './pages/DashboardPage'
import { DelegationDetailPage } from './pages/delegations/DelegationDetailPage'
import { DelegationFormPage } from './pages/delegations/DelegationFormPage'
import { MessagePage, PlaceholderPage } from './pages/PlaceholderPage'

function RequireRole({ user, role, children }: { user: CurrentUser; role: AppRole | AppRole[]; children: ReactNode }) {
  const roles = Array.isArray(role) ? role : [role]
  return roles.some((r) => hasRole(user, r)) ? children : <MessagePage messageKey="common.forbidden" />
}

const viewer: AppRole[] = ['Admin', 'Approver']

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
        <Route
          index
          element={
            <RequireRole user={user} role={viewer}>
              <DashboardPage />
            </RequireRole>
          }
        />
        <Route
          path="delegations/new"
          element={
            <RequireRole user={user} role="Admin">
              <DelegationFormPage />
            </RequireRole>
          }
        />
        <Route
          path="delegations/:id"
          element={
            <RequireRole user={user} role={viewer}>
              <DelegationDetailPage />
            </RequireRole>
          }
        />
        <Route
          path="delegations/:id/edit"
          element={
            <RequireRole user={user} role="Admin">
              <DelegationFormPage />
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
              <AdminPage />
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
