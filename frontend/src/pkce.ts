const verifierKey = 'northstar.pkce.verifier'
const stateKey = 'northstar.pkce.state'
const clientId = 'northstar-spa'
const redirectUri = `${window.location.origin}/auth/callback`

export function clearAuthSession() {
  sessionStorage.removeItem(verifierKey)
  sessionStorage.removeItem(stateKey)
  sessionStorage.removeItem('northstar.access_token')
  localStorage.removeItem('northstar.tenant')
}

function base64Url(bytes: Uint8Array) {
  let value = ''
  bytes.forEach(byte => { value += String.fromCharCode(byte) })
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

export async function beginPkceLogin(tenant = localStorage.getItem('northstar.tenant') ?? 'acme-corp') {
  const verifier = base64Url(crypto.getRandomValues(new Uint8Array(32)))
  const state = base64Url(crypto.getRandomValues(new Uint8Array(24)))
  const challenge = base64Url(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))))
  sessionStorage.setItem(verifierKey, verifier)
  sessionStorage.setItem(stateKey, state)
  localStorage.setItem('northstar.tenant', tenant)
  const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'
  const params = new URLSearchParams({ client_id: clientId, response_type: 'code', redirect_uri: redirectUri, scope: 'openid profile email saas-api', code_challenge: challenge, code_challenge_method: 'S256', state, tenant })
  window.location.assign(`${apiUrl}/connect/authorize?${params}`)
}

export async function completePkceLogin(): Promise<string> {
  const query = new URLSearchParams(window.location.search)
  const code = query.get('code')
  const state = query.get('state')
  if (!code || !state || state !== sessionStorage.getItem(stateKey)) throw new Error('The authorization response is invalid.')
  const verifier = sessionStorage.getItem(verifierKey)
  if (!verifier) throw new Error('The authorization session has expired.')
  const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'
  const response = await fetch(`${apiUrl}/connect/token`, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, credentials: 'include', body: new URLSearchParams({ grant_type: 'authorization_code', client_id: clientId, redirect_uri: redirectUri, code, code_verifier: verifier }) })
  if (!response.ok) throw new Error('The token exchange failed.')
  const token = (await response.json()) as { access_token: string }
  sessionStorage.removeItem(verifierKey)
  sessionStorage.removeItem(stateKey)
  sessionStorage.setItem('northstar.access_token', token.access_token)
  window.history.replaceState({}, document.title, '/')
  return token.access_token
}
