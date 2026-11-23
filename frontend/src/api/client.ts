// Client HTTP minimal. Autentificarea AD (Negotiate/Kerberos) se face de browser,
// așa că trimitem întotdeauna credențialele.

export class ApiError extends Error {
  readonly status: number
  /** Erori de validare per câmp (ValidationProblemDetails.errors). */
  readonly errors?: Record<string, string[]>

  constructor(status: number, message: string, errors?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.errors = errors
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
    let message = `HTTP ${response.status}`
    let errors: Record<string, string[]> | undefined
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
      errors = problem.errors
    } catch {
      // răspuns fără corp JSON
    }
    throw new ApiError(response.status, message, errors)
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
