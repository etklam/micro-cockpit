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
export async function login(email: string, password: string) {
  const tokens = await G.postApiAuthLogin({ email, password })
  applyToken(tokens.accessToken)
}
export async function logout() {
  try { await G.postApiAuthLogout() } catch { /* Local session clearing is authoritative. */ }
  endSession()
}
