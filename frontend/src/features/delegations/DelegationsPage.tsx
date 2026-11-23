import { Download, Plus } from 'lucide-react'
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
          <>
            <a
              href={list.exportUrl}
              className="inline-flex h-9 items-center gap-2 rounded-lg bg-card px-3.5 text-sm font-medium text-fg ring-1 ring-inset ring-line hover:bg-subtle"
            >
              <Download size={16} /> {t('delegations.export')}
            </a>
            {isAdmin && (
              <Button onClick={drawer.openCreate}>
                <Plus size={16} /> {t('nav.newDelegation')}
              </Button>
            )}
          </>
        }
      />
      <DelegationTable
        rows={list.query.data?.items ?? []}
        loading={list.query.isFetching}
        filters={list.filters}
        onFilter={list.setFilter}
        onClear={list.clear}
        hasFilters={list.hasFilters}
        advanced
        canManage={isAdmin}
        footer={<Pager page={list.page} pageSize={PAGE_SIZE} total={list.query.data?.total ?? 0} onPage={list.setPage} />}
      />
    </>
  )
}
