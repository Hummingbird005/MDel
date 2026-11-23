import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeftRight,
  CheckCheck,
  History,
  LayoutDashboard,
  Mail,
  Menu,
  Moon,
  Search,
  Settings,
  Sun,
  X,
  type LucideIcon,
} from 'lucide-react'
import { createContext, useContext, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { adminApi } from '../../api/admin'
import { api } from '../../api/client'
import { delegationsApi } from '../../api/delegations'
import { hasRole, type AppRole, type CurrentUser } from '../../auth/useCurrentUser'
import { useTheme } from '../../hooks/useTheme'
import { supportedLanguages, type Language } from '../../i18n'
import { cx } from '../../lib/cx'
import { Avatar } from '../ui/primitives'

/** Textul din căutarea globală din header (filtrează listele de delegări). */
const SearchContext = createContext('')
// eslint-disable-next-line react-refresh/only-export-components
export const useGlobalSearch = () => useContext(SearchContext)

interface NavItem {
  to: string
  labelKey: string
  icon: LucideIcon
  roles: AppRole[]
}

const navItems: NavItem[] = [
  { to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard, roles: ['Admin', 'Approver'] },
  { to: '/delegations', labelKey: 'nav.delegations', icon: ArrowLeftRight, roles: ['Admin', 'Approver'] },
  { to: '/approvals', labelKey: 'nav.approvals', icon: CheckCheck, roles: ['Approver'] },
  { to: '/audit', labelKey: 'nav.audit', icon: History, roles: ['Admin'] },
  { to: '/admin', labelKey: 'nav.admin', icon: Settings, roles: ['Admin'] },
]

export function AppShell({ user }: { user: CurrentUser }) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [search, setSearch] = useState('')
  const location = useLocation()

  // Meniul mobil se închide la navigare.
  useEffect(() => setMenuOpen(false), [location.pathname])

  return (
    <SearchContext.Provider value={search}>
      <div className="min-h-full">
        <Sidebar user={user} open={menuOpen} onClose={() => setMenuOpen(false)} />
        <div className="flex min-h-full min-w-0 flex-col lg:pl-64">
          <Header user={user} search={search} onSearch={setSearch} onMenu={() => setMenuOpen(true)} />
          <main className="flex min-w-0 flex-col gap-6 px-4 py-6 sm:px-6">
            <Outlet />
          </main>
        </div>
      </div>
    </SearchContext.Provider>
  )
}

function Sidebar({ user, open, onClose }: { user: CurrentUser; open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const isApprover = hasRole(user, 'Approver')
  const stats = useQuery({ queryKey: ['delegations', 'stats'], queryFn: delegationsApi.stats, refetchInterval: 60_000 })
  const pending = stats.data?.byStatus.PendingApproval ?? 0
  const isAdmin = hasRole(user, 'Admin')
  const failedMails = useQuery({
    queryKey: ['admin', 'notifications', 'failed-count'],
    queryFn: adminApi.failedNotifications,
    enabled: isAdmin,
    refetchInterval: 120_000,
  })

  const content = (
    <div className="flex h-full flex-col gap-6 border-r border-line bg-card px-3 pb-4 pt-4">
      <div className="flex items-center gap-2.5 px-2">
        <span className="grid h-8 w-8 place-items-center rounded-lg bg-accent text-accent-fg shadow-sm">
          <Mail size={16} aria-hidden="true" />
        </span>
        <div className="leading-tight">
          <p className="text-sm font-semibold">MDel</p>
          <p className="text-[11px] text-muted">{t('app.subtitle')}</p>
        </div>
        <button className="ml-auto grid h-8 w-8 place-items-center rounded-lg text-muted hover:bg-subtle lg:hidden" onClick={onClose} aria-label={t('common.close')}>
          <X size={16} />
        </button>
      </div>
      <nav className="flex flex-col gap-0.5" aria-label={t('nav.main')}>
        {navItems
          .filter((item) => item.roles.some((r) => hasRole(user, r)))
          .map(({ to, labelKey, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                cx(
                  'flex h-9 items-center gap-2.5 rounded-lg px-2.5 text-sm font-medium transition-colors',
                  isActive ? 'bg-accent/10 text-accent-text' : 'text-muted hover:bg-subtle hover:text-fg',
                )
              }
            >
              <Icon size={16} aria-hidden="true" />
              <span className="flex-1">{t(labelKey)}</span>
              {to === '/approvals' && isApprover && pending > 0 && (
                <span className="tabular rounded-full bg-warning/15 px-1.5 text-[11px] font-semibold text-warning">{pending}</span>
              )}
              {to === '/admin' && (failedMails.data ?? 0) > 0 && (
                <span className="tabular rounded-full bg-danger/15 px-1.5 text-[11px] font-semibold text-danger" title={t('admin.failedMails')}>
                  {failedMails.data}
                </span>
              )}
            </NavLink>
          ))}
      </nav>
      <div className="mt-auto rounded-xl bg-subtle p-3 text-xs text-muted">
        <p className="font-medium text-fg">{t('app.title')}</p>
        <p className="mt-1">{t('app.footer')}</p>
      </div>
    </div>
  )

  return (
    <>
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 lg:block">{content}</aside>
      {open && (
        <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true" aria-label={t('nav.main')}>
          <div className="absolute inset-0 animate-fade-in bg-overlay/50" onClick={onClose} />
          <div className="absolute inset-y-0 left-0 w-72 max-w-[85vw] animate-slide-in">{content}</div>
        </div>
      )}
    </>
  )
}

function Header({
  user,
  search,
  onSearch,
  onMenu,
}: {
  user: CurrentUser
  search: string
  onSearch: (v: string) => void
  onMenu: () => void
}) {
  const { t, i18n } = useTranslation()
  const { theme, toggle } = useTheme()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const inputRef = useRef<HTMLInputElement>(null)

  const saveLanguage = useMutation({
    mutationFn: (lang: Language) => api.put('/api/me/language', { language: lang }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  })

  // „/” sau Ctrl+K focalizează căutarea.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName ?? '')
      if (!typing && (e.key === '/' || (e.key === 'k' && (e.ctrlKey || e.metaKey)))) {
        e.preventDefault()
        inputRef.current?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const onSearchChange = (value: string) => {
    onSearch(value)
    // Căutarea se aplică pe liste; din alte pagini ducem utilizatorul la lista de delegări.
    if (value && !['/', '/delegations', '/approvals'].includes(location.pathname)) navigate('/delegations')
  }

  return (
    <header className="sticky top-0 z-20 border-b border-line bg-card/85 backdrop-blur">
      <div className="flex h-14 items-center gap-3 px-4 sm:px-6">
        <button className="grid h-9 w-9 place-items-center rounded-lg text-muted hover:bg-subtle hover:text-fg lg:hidden" onClick={onMenu} aria-label={t('nav.openMenu')}>
          <Menu size={18} />
        </button>
        <div className="relative min-w-0 flex-1 sm:max-w-md">
          <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" aria-hidden="true" />
          <input
            id="global-search"
            ref={inputRef}
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder={t('header.search')}
            aria-label={t('header.search')}
            className="h-9 w-full rounded-lg bg-subtle pl-9 pr-10 text-sm text-fg ring-1 ring-inset ring-transparent placeholder:text-muted/80 focus:bg-card focus:outline-none focus:ring-accent"
          />
          <kbd className="pointer-events-none absolute right-2.5 top-1/2 hidden -translate-y-1/2 rounded border border-line px-1.5 font-mono text-[10px] text-muted sm:block">
            /
          </kbd>
        </div>
        <div className="ml-auto flex items-center gap-1">
          <select
            id="language"
            aria-label={t('language.label')}
            value={(i18n.resolvedLanguage ?? 'ro') as Language}
            onChange={(e) => {
              const lang = e.target.value as Language
              void i18n.changeLanguage(lang)
              saveLanguage.mutate(lang)
            }}
            className="h-9 rounded-lg bg-transparent px-2 text-sm font-medium uppercase text-muted hover:bg-subtle hover:text-fg focus:outline-none"
          >
            {supportedLanguages.map((l) => (
              <option key={l} value={l}>
                {l.toUpperCase()}
              </option>
            ))}
          </select>
          <button
            onClick={toggle}
            className="grid h-9 w-9 place-items-center rounded-lg text-muted hover:bg-subtle hover:text-fg"
            aria-label={theme === 'dark' ? t('header.lightTheme') : t('header.darkTheme')}
            title={theme === 'dark' ? t('header.lightTheme') : t('header.darkTheme')}
          >
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          <div className="ml-2 hidden items-center gap-2.5 border-l border-line pl-3 sm:flex">
            <Avatar name={user.displayName} />
            <div className="leading-tight">
              <p className="text-sm font-medium">{user.displayName}</p>
              <p className="text-[11px] text-muted">{user.roles.map((r) => t(`roles.${r}`)).join(' · ') || t('roles.none')}</p>
            </div>
          </div>
        </div>
      </div>
    </header>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {subtitle && <p className="mt-0.5 text-sm text-muted">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
