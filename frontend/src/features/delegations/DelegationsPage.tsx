import { Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { hasRole, useCurrentUser } from '../../auth/useCurrentUser'
import { PageHeader } from '../../components/layout/AppShell'
import { Button } from '../../components/ui/primitives'
import { useDelegationDrawer } from './DelegationDrawer'
import { DelegationTable, Pager } from './DelegationTable'
import { useDelegationList } from './useDelegationList'

const PAGE_SIZE = 25

export function DelegationsPage() {
  const { t } = useTranslation()
  const { data: user } = useCurrentUser()
  const drawer = useDelegationDrawer()
  const isAdmin = hasRole(user, 'Admin')
  const list = useDelegationList(PAGE_SIZE)

  return (
    <>
      <PageHeader
        title={t('nav.delegations')}
        subtitle={t('delegations.subtitle')}
        actions={
          isAdmin && (
            <Button onClick={drawer.openCreate}>
              <Plus size={16} /> {t('nav.newDelegation')}
            </Button>
          )
        }
      />
      <DelegationTable
        rows={list.query.data?.items ?? []}
        loading={list.query.isFetching}
        status={list.status}
        onStatus={list.setStatus}
        mode={list.mode}
        onMode={list.setMode}
        canManage={isAdmin}
        footer={<Pager page={list.page} pageSize={PAGE_SIZE} total={list.query.data?.total ?? 0} onPage={list.setPage} />}
      />
    </>
  )
}
