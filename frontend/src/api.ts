import * as G from './generated/edge'
import { configureClient } from './generated/edge'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''
let accessToken: string | null = null
let onSessionEnded: (() => void) | null = null

function applyToken(token: string | null) {
  accessToken = token
  configureClient({ baseUrl: BASE_URL, token, refresh: refreshAccessToken, onUnauthorized: endSession })
}

async function refreshAccessToken(): Promise<string | null> {
  try {
    const tokens = await G.postApiAuthRefresh()
    applyToken(tokens.accessToken)
    return tokens.accessToken
  } catch {
    return null
  }
}

function endSession() {
  applyToken(null)
  onSessionEnded?.()
}

applyToken(null)

export function configureSession(ended: () => void) { onSessionEnded = ended }
export const hasAccessToken = () => accessToken !== null
export async function restoreSession(): Promise<boolean> { return (await refreshAccessToken()) !== null }
export async function register(input: G.RegisterRequest): Promise<G.RegisterResponse> {
  return G.postApiAuthRegister(input)
}
export async function login(email: string, password: string) {
  const tokens = await G.postApiAuthLogin({ email, password })
  applyToken(tokens.accessToken)
}
export async function logout() {
  try { await G.postApiAuthLogout() } catch { /* Local session clearing is authoritative. */ }
  endSession()
}

/** Rotate refresh cookie and replace in-memory access token. Does not expose the refresh token. */
export async function refreshSession(): Promise<boolean> {
  const token = await refreshAccessToken()
  return token !== null
}

/** Drop the in-memory access token without invoking the session-ended listener. */
export function clearAccessToken() {
  applyToken(null)
}

/** Clear local access token and notify listeners (cookie cleared only via logout/refresh failure). */
export function terminateSession() {
  endSession()
}
