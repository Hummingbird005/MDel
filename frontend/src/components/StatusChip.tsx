import { Chip, type ChipProps } from '@mui/material'
import { useTranslation } from 'react-i18next'
import type { DelegationStatus } from '../api/delegations'

const colors: Record<DelegationStatus, ChipProps['color']> = {
  Active: 'success',
  Scheduled: 'info',
  PendingApproval: 'warning',
  Expired: 'default',
  Stopped: 'default',
  Rejected: 'error',
  Failed: 'error',
}

export function StatusChip({ status, size = 'small' }: { status: DelegationStatus; size?: ChipProps['size'] }) {
  const { t } = useTranslation()
  return <Chip size={size} color={colors[status]} label={t(`status.${status}`)} />
}
