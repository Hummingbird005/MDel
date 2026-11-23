// Client HTTP minimal. Autentificarea AD (Negotiate/Kerberos) se face de browser,
// așa că trimitem întotdeauna credențialele.

export class ApiError extends Error {
  readonly status: number
  readonly detail?: string

  constructor(status: number, message: string, detail?: string) {
    super(message)
    this.status = status
    this.detail = detail
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      'Accept-Language': localStorage.getItem('mdel.lang') ?? 'ro',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    let detail: string | undefined
    try {
      const problem = await response.json()
      detail = problem.detail ?? problem.title
    } catch {
      // răspuns fără corp JSON
    }
    throw new ApiError(response.status, `HTTP ${response.status}`, detail)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body),
  delete: <T>(url: string) => request<T>('DELETE', url),
}
