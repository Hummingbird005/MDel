import type { DirectoryUser, UserRef } from '../api/delegations'

export interface PickedUser {
  samAccountName: string
  displayName: string
  email: string | null
}

export const toPicked = (u: DirectoryUser | UserRef): PickedUser => ({
  samAccountName: u.samAccountName,
  displayName: u.displayName,
  email: u.email,
})
