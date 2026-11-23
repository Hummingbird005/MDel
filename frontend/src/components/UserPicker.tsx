import { Autocomplete, Box, CircularProgress, TextField, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { delegationsApi } from '../api/delegations'
import type { PickedUser } from './pickedUser'

interface Props {
  label: string
  value: PickedUser | null
  onChange: (user: PickedUser | null) => void
  error?: string
  disabled?: boolean
}

/** Autocomplete din Active Directory (doar utilizatori cu căsuță de email). */
export function UserPicker({ label, value, onChange, error, disabled }: Props) {
  const { t } = useTranslation()
  const [input, setInput] = useState('')
  const [term, setTerm] = useState('')

  // Debounce 300 ms.
  useEffect(() => {
    const handle = setTimeout(() => setTerm(input.trim()), 300)
    return () => clearTimeout(handle)
  }, [input])

  const search = useQuery({
    queryKey: ['directory', term],
    queryFn: () => delegationsApi.searchUsers(term),
    enabled: term.length >= 2,
    staleTime: 60_000,
  })

  const options: PickedUser[] = search.data ?? []

  return (
    <Autocomplete<PickedUser>
      disabled={disabled}
      value={value}
      options={options}
      filterOptions={(x) => x}
      isOptionEqualToValue={(a, b) => a.samAccountName === b.samAccountName}
      getOptionLabel={(u) => (u.email ? `${u.displayName} <${u.email}>` : u.displayName)}
      onChange={(_, u) => onChange(u)}
      onInputChange={(_, v, reason) => reason === 'input' && setInput(v)}
      loading={search.isFetching}
      noOptionsText={term.length < 2 ? t('form.typeToSearch') : search.isError ? t('form.adError') : t('form.noUsers')}
      renderOption={(props, u) => {
        const { key, ...rest } = props as typeof props & { key: string }
        return (
          <Box component="li" key={key} {...rest}>
            <Box>
              <Typography>{u.displayName}</Typography>
              <Typography variant="body2" color="text.secondary">
                {u.samAccountName}
                {u.email ? ` · ${u.email}` : ''}
              </Typography>
            </Box>
          </Box>
        )
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          error={!!error}
          helperText={error}
          slotProps={{
            input: {
              ...params.InputProps,
              endAdornment: (
                <>
                  {search.isFetching && <CircularProgress size={18} />}
                  {params.InputProps.endAdornment}
                </>
              ),
            },
          }}
        />
      )}
    />
  )
}
