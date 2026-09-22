import { clearAuthSession } from './pkce'

const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'

type Session = { authenticated: boolean; email?: string }
export type ApiUser = { id: string; email: string; displayName: string; isActive: boolean; role: string }
export type ApiAuditEntry = { id: string; occurredAt: string; actorId?: string; action: string; resource: string; ipAddress?: string }
export type TenantSummary = { id: string; slug: string; name: string; domain?: string | null; brandingColor: string; provisioningStatus: string }
export type TenantSettings = { id: string; name: string; slug: string; domain?: string; brandingColor: string }

export function getSessionRole(): string | null {
  const token = sessionStorage.getItem('northstar.access_token')
  if (!token) return null

  try {
    const [, payload] = token.split('.')
    if (!payload) return null
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
    const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), '=')
    const decoded = JSON.parse(atob(padded)) as { role?: string | string[] }
    const role = decoded.role
    if (Array.isArray(role)) return role[0] ?? null
    return typeof role === 'string' ? role : null
  } catch {
    return null
  }
}

function readErrorMessage(payload: unknown, fallback: string): string {
  if (!payload || typeof payload !== 'object') return fallback
  const record = payload as Record<string, unknown>
  return String(record.error ?? record.detail ?? record.title ?? fallback)
}

async function request<T>(path: string, options: RequestInit = {}, tenantScoped = true, allowUnauthorized = false): Promise<T> {
  const headers = new Headers(options.headers)
  headers.set('Content-Type', 'application/json')
  if (tenantScoped) headers.set('X-Tenant', localStorage.getItem('northstar.tenant') ?? 'acme-corp')
  const accessToken = sessionStorage.getItem('northstar.access_token')
  if (tenantScoped && accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  const response = await fetch(`${apiBaseUrl}${path}`, { ...options, headers, credentials: 'include' })
  if (!response.ok) {
    const payload = await response.json().catch(() => null)
    const message = readErrorMessage(payload, `Request failed (${response.status})`)
    if ((response.status === 401 || response.status === 403) && !allowUnauthorized) {
      clearAuthSession()
    }
    if (allowUnauthorized && (response.status === 401 || response.status === 403)) {
      return null as T
    }
    throw new Error(message)
  }
  return response.status === 204 ? (undefined as T) : response.json() as Promise<T>
}

export const api = {
  session: async (): Promise<Session> => {
    const session = await request<Session>('/api/session', {}, false, true)
    return session ?? { authenticated: false }
  },
  login: (email: string, password: string, rememberMe: boolean) => request<{ authenticated: boolean }>('/account/login', { method: 'POST', body: JSON.stringify({ email, password, rememberMe }) }, false),
  forgotPassword: (email: string) => request<void>('/account/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }, false),
  logout: () => request<void>('/account/logout', { method: 'POST' }, false),
  tenants: () => request<TenantSummary[]>('/api/tenants'),
  users: () => request<ApiUser[]>('/api/users'),
  createUser: (email: string, displayName: string, role: string) => request<ApiUser>('/api/users', { method: 'POST', body: JSON.stringify({ email, displayName, role }) }),
  auditLog: () => request<{ entries: ApiAuditEntry[] }>('/api/audit-log'),
  settings: () => request<TenantSettings>('/api/tenant-settings'),
  updateSettings: (settings: Pick<TenantSettings, 'name' | 'domain' | 'brandingColor'>) => request<TenantSettings>('/api/tenant-settings', { method: 'PUT', body: JSON.stringify(settings) }),
}