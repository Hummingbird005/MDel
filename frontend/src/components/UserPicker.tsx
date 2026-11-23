import { useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { delegationsApi } from '../api/delegations'
import { cx } from '../lib/cx'
import { Avatar, inputClass } from './ui/primitives'
import type { PickedUser } from './pickedUser'

interface Props {
  id: string
  value: PickedUser | null
  onChange: (user: PickedUser | null) => void
  error?: string
  disabled?: boolean
  exclude?: string
}

/** Combobox cu căutare în Active Directory (doar utilizatori cu căsuță de email). */
export function UserPicker({ id, value, onChange, error, disabled, exclude }: Props) {
  const { t } = useTranslation()
  const listId = useId()
  const [open, setOpen] = useState(false)
  const [input, setInput] = useState('')
  const [term, setTerm] = useState('')
  const [active, setActive] = useState(0)

  useEffect(() => {
    const h = setTimeout(() => setTerm(input.trim()), 250)
    return () => clearTimeout(h)
  }, [input])

  const search = useQuery({
    queryKey: ['directory', term],
    queryFn: () => delegationsApi.searchUsers(term),
    enabled: open && term.length >= 2,
    staleTime: 60_000,
  })
  const options = (search.data ?? []).filter((u) => u.samAccountName !== exclude)

  const select = (u: PickedUser) => {
    onChange({ samAccountName: u.samAccountName, displayName: u.displayName, email: u.email })
    setOpen(false)
    setInput('')
  }

  return (
    <div className="relative">
      <input
        id={id}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-invalid={!!error}
        autoComplete="off"
        disabled={disabled}
        className={inputClass(error, 'pr-8')}
        placeholder={t('form.searchAd')}
        value={open ? input : value ? `${value.displayName}${value.email ? ` · ${value.email}` : ''}` : ''}
        onFocus={() => {
          setOpen(true)
          setInput('')
          setActive(0)
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onChange={(e) => {
          setInput(e.target.value)
          setActive(0)
        }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') setActive((a) => Math.min(a + 1, options.length - 1))
          else if (e.key === 'ArrowUp') setActive((a) => Math.max(a - 1, 0))
          else if (e.key === 'Enter' && open && options[active]) {
            e.preventDefault()
            select(options[active])
          }
        }}
      />
      {search.isFetching && (
        <Loader2 size={14} className="absolute right-3 top-1/2 -translate-y-1/2 animate-spin text-muted" aria-hidden="true" />
      )}
      {open && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-10 mt-1 max-h-64 w-full animate-fade-in overflow-auto rounded-lg bg-card p-1 shadow-lg ring-1 ring-line"
        >
          {term.length < 2 ? (
            <li className="px-3 py-2 text-sm text-muted">{t('form.typeToSearch')}</li>
          ) : search.isError ? (
            <li className="px-3 py-2 text-sm text-danger">{t('form.adError')}</li>
          ) : options.length === 0 && !search.isFetching ? (
            <li className="px-3 py-2 text-sm text-muted">{t('form.noUsers')}</li>
          ) : (
            options.map((u, i) => (
              <li
                key={u.samAccountName}
                role="option"
                aria-selected={i === active}
                onMouseDown={(e) => {
                  e.preventDefault()
                  select(u)
                }}
                onMouseEnter={() => setActive(i)}
                className={cx('flex cursor-pointer items-center gap-3 rounded-md px-2 py-1.5', i === active && 'bg-subtle')}
              >
                <Avatar name={u.displayName} />
                <div className="min-w-0 leading-tight">
                  <p className="truncate text-sm font-medium">{u.displayName}</p>
                  <p className="truncate text-xs text-muted">
                    {u.samAccountName}
                    {u.email ? ` · ${u.email}` : ''}
                  </p>
                </div>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  )
}
