import { ShieldAlert } from 'lucide-react'
import { useEffect, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Route, Routes } from 'react-router-dom'
import { hasRole, useCurrentUser, type AppRole, type CurrentUser } from './auth/useCurrentUser'
import { AppShell } from './components/layout/AppShell'
import { Alert, Card, EmptyState, Spinner } from './components/ui/primitives'
import { AdminPage } from './features/admin/AdminPage'
import { ApprovalsPage } from './features/approvals/ApprovalsPage'
import { AuditPage } from './features/audit/AuditPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { DelegationDetailPage } from './features/delegations/DelegationDetailPage'
import { DelegationDrawerProvider } from './features/delegations/DelegationDrawer'
import { DelegationsPage } from './features/delegations/DelegationsPage'

const viewer: AppRole[] = ['Admin', 'Approver']

function RequireRole({ user, roles, children }: { user: CurrentUser; roles: AppRole[]; children: ReactNode }) {
  const { t } = useTranslation()
  if (roles.some((r) => hasRole(user, r))) return children
  return (
    <Card>
      <EmptyState icon={<ShieldAlert size={28} />} title={t('common.forbidden')} text={t('common.forbiddenHint')} />
    </Card>
  )
}

export default function App() {
  const { data: user, isLoading, isError } = useCurrentUser()
  const { t, i18n } = useTranslation()

  // Limba salvată pe server are prioritate față de cea detectată în browser.
  useEffect(() => {
    if (user && user.preferredLanguage !== i18n.resolvedLanguage) void i18n.changeLanguage(user.preferredLanguage)
  }, [user, i18n])

  if (isLoading) return <Spinner label={t('common.loading')} />
  if (isError || !user) {
    return (
      <div className="p-6">
        <Alert tone="danger">{t('common.authError')}</Alert>
      </div>
    )
  }

  return (
    <DelegationDrawerProvider>
      <Routes>
        <Route element={<AppShell user={user} />}>
          <Route index element={<RequireRole user={user} roles={viewer}><DashboardPage /></RequireRole>} />
          <Route path="delegations" element={<RequireRole user={user} roles={viewer}><DelegationsPage /></RequireRole>} />
          <Route path="delegations/:id" element={<RequireRole user={user} roles={viewer}><DelegationDetailPage /></RequireRole>} />
          <Route path="approvals" element={<RequireRole user={user} roles={['Approver']}><ApprovalsPage /></RequireRole>} />
          <Route path="audit" element={<RequireRole user={user} roles={['SystemAdmin']}><AuditPage /></RequireRole>} />
          <Route path="admin" element={<RequireRole user={user} roles={['SystemAdmin']}><AdminPage /></RequireRole>} />
          <Route path="*" element={<Alert>{t('common.notFound')}</Alert>} />
        </Route>
      </Routes>
    </DelegationDrawerProvider>
  )
}
