import {
  Card,
  CardActionArea,
  CardContent,
  Chip,
  LinearProgress,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import AttachFileIcon from '@mui/icons-material/AttachFile'
import Grid from '@mui/material/Grid2'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { delegationsApi, type DelegationStatus } from '../api/delegations'
import { formatDateTime } from '../components/format'
import { StatusChip } from '../components/StatusChip'

type TabId = 'active' | 'scheduled' | 'pending' | 'expired' | 'other'

const tabs: { id: TabId; statuses: DelegationStatus[] }[] = [
  { id: 'active', statuses: ['Active'] },
  { id: 'scheduled', statuses: ['Scheduled'] },
  { id: 'pending', statuses: ['PendingApproval'] },
  { id: 'expired', statuses: ['Expired'] },
  { id: 'other', statuses: ['Stopped', 'Rejected', 'Failed'] },
]

export function DashboardPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as TabId) ?? 'active'
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)

  useEffect(() => {
    const h = setTimeout(() => {
      setDebounced(search.trim())
      setPage(0)
    }, 300)
    return () => clearTimeout(h)
  }, [search])

  const statuses = tabs.find((x) => x.id === tab)?.statuses ?? tabs[0].statuses
  const summary = useQuery({ queryKey: ['delegations', 'summary'], queryFn: delegationsApi.summary, refetchInterval: 60_000 })
  const list = useQuery({
    queryKey: ['delegations', 'list', tab, debounced, page, pageSize],
    queryFn: () => delegationsApi.list({ status: statuses, search: debounced, page: page + 1, pageSize }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })

  const count = (s: DelegationStatus[]) => s.reduce((sum, x) => sum + (summary.data?.[x] ?? 0), 0)
  const selectTab = (id: TabId) => {
    setParams({ tab: id })
    setPage(0)
  }

  return (
    <>
      <Typography variant="h5" gutterBottom>
        {t('nav.dashboard')}
      </Typography>

      <Grid container spacing={2} sx={{ mb: 2 }}>
        {tabs.slice(0, 4).map((x) => (
          <Grid key={x.id} size={{ xs: 6, md: 3 }}>
            <Card variant={tab === x.id ? 'elevation' : 'outlined'} raised={tab === x.id}>
              <CardActionArea onClick={() => selectTab(x.id)}>
                <CardContent>
                  <Typography color="text.secondary">{t(`dashboard.${x.id}`)}</Typography>
                  <Typography variant="h4">{summary.data ? count(x.statuses) : '–'}</Typography>
                </CardContent>
              </CardActionArea>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Card>
        <Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'center' }} sx={{ px: 2 }}>
          <Tabs value={tab} onChange={(_, v) => selectTab(v)} variant="scrollable" sx={{ flexGrow: 1 }}>
            {tabs.map((x) => (
              <Tab
                key={x.id}
                value={x.id}
                label={
                  <Stack direction="row" spacing={1} alignItems="center">
                    <span>{t(`dashboard.${x.id}`)}</span>
                    {summary.data && <Chip size="small" label={count(x.statuses)} />}
                  </Stack>
                }
              />
            ))}
          </Tabs>
          <TextField
            size="small"
            placeholder={t('dashboard.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ minWidth: 260, my: 1 }}
          />
        </Stack>
        {list.isFetching && <LinearProgress />}
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>#</TableCell>
              <TableCell>{t('form.from')}</TableCell>
              <TableCell>{t('form.to')}</TableCell>
              <TableCell>{t('form.start')}</TableCell>
              <TableCell>{t('form.end')}</TableCell>
              <TableCell>{t('detail.status')}</TableCell>
              <TableCell>{t('form.createdBy')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((d) => (
              <TableRow key={d.id} hover sx={{ cursor: 'pointer' }} onClick={() => navigate(`/delegations/${d.id}`)}>
                <TableCell>{d.id}</TableCell>
                <TableCell>{d.from.displayName}</TableCell>
                <TableCell>{d.to.displayName}</TableCell>
                <TableCell>{formatDateTime(d.startUtc)}</TableCell>
                <TableCell>{formatDateTime(d.endUtc)}</TableCell>
                <TableCell>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <StatusChip status={d.status} />
                    {!d.requiresApproval && <Chip size="small" variant="outlined" label={t('dashboard.noApproval')} />}
                    {d.attachmentCount > 0 && <AttachFileIcon fontSize="small" color="action" />}
                  </Stack>
                </TableCell>
                <TableCell>{d.createdBy.displayName}</TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>
                  <Typography color="text.secondary">{t('dashboard.empty')}</Typography>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        <TablePagination
          component="div"
          count={list.data?.total ?? 0}
          page={page}
          rowsPerPage={pageSize}
          rowsPerPageOptions={[10, 25, 50, 100]}
          onPageChange={(_, p) => setPage(p)}
          onRowsPerPageChange={(e) => {
            setPageSize(Number(e.target.value))
            setPage(0)
          }}
          labelRowsPerPage={t('dashboard.rowsPerPage')}
          labelDisplayedRows={({ from, to, count }) => t('dashboard.displayedRows', { from, to, count })}
        />
      </Card>
    </>
  )
}
