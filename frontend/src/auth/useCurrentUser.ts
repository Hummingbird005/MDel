import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

export type AppRole = 'Admin' | 'Approver'

export interface CurrentUser {
  samAccountName: string
  displayName: string
  email: string | null
  roles: AppRole[]
  preferredLanguage: 'ro' | 'en'
}

export function useCurrentUser() {
  return useQuery({
    queryKey: ['me'],
    queryFn: () => api.get<CurrentUser>('/api/me'),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function hasRole(user: CurrentUser | undefined, role: AppRole) {
  return user?.roles.includes(role) ?? false
}
