// Edge API facade + browser session manager.
//
// Session model: the access token lives only in memory; the refresh token is never visible to
// JS — Edge stores it in an HttpOnly cookie. On reload (no in-memory token) we call /api/auth/refresh
// (Edge reads the cookie) to restore the session. On a 401 the generated transport single-flight
// refreshes once and retries; if refresh fails the session ends and the app returns to login.
import { useRef } from 'react'
import * as G from './generated/edge'
import { configureClient } from './generated/edge'

const idempotencyHeader = (key?: string): RequestInit | undefined =>
  key ? { headers: { 'Idempotency-Key': key } } : undefined

// One idempotency key per in-flight submission: lazily minted, held across retries of the same
// attempt, and cleared on success or when the draft changes — so a network retry reuses the key
// (returns the same resource) while a fresh submission mints a new one.
export function useIdempotencyKey() {
  const ref = useRef<string | null>(null)
  return {
    key: () => { if (!ref.current) ref.current = crypto.randomUUID(); return ref.current },
    reset: () => { ref.current = null },
  }
}

const BASE_URL = import.meta.env.VITE_API_URL ?? ''
let accessToken: string | null = null
let onSessionEnded: (() => void) | null = null

function applyToken(token: string | null) {
  accessToken = token
  configureClient({ baseUrl: BASE_URL, token, refresh: doRefresh, onUnauthorized: endSession })
}

// Refresh once; the generated transport collapses concurrent refreshes into this single promise.
// Returns the new access token, or null when the cookie is absent/invalid (session over).
async function doRefresh(): Promise<string | null> {
  try { const tokens = await G.postApiAuthRefresh(); applyToken(tokens.accessToken); return tokens.accessToken }
  catch { return null }
}

function endSession() { applyToken(null); onSessionEnded?.() }

applyToken(null) // configure transport at module load (no Authorization header until login)

// --- session lifecycle ------------------------------------------------------
export function configureSession(ended: () => void) { onSessionEnded = ended }
export const hasAccessToken = () => accessToken !== null
export async function restoreSession(): Promise<boolean> { return (await doRefresh()) !== null }
export async function login(email: string, password: string) {
  const tokens = await G.postApiAuthLogin({ email, password })
  applyToken(tokens.accessToken)
}
export async function logout() {
  try { await G.postApiAuthLogout() } catch { /* best-effort: clear locally regardless */ }
  endSession()
}

// --- re-exported types (UI import names; all backed by generated schemas) --
export type Diary = G.DiaryResponse
export type Transaction = G.TransactionResponse
export type Discipline = G.DisciplineResponse
export type Alert = G.DiaryAlertResponse
export type PriceAlert = G.PriceAlertResponse
export type WatchlistItem = G.WatchlistResponse
export type Stock = G.StockResponse
export type ResearchNote = G.NoteResponse
export type TimelineEvent = G.TimelineResponse
export type RotationItem = G.EtfSnapshotResponse
export type Partner = G.Link
export type Article = G.PostResponse
export type Dashboard = G.Dashboard
export type Calendar = G.Calendar
export type CalendarDay = G.Calendar['days'][number]
export type Capability = 'available' | 'unavailable' | 'empty'

// --- data operations (paths/bodies/types from the generated client) ---------
export const getDashboard = (): Promise<Dashboard> => G.getApiAppDashboard()
export const saveQuickNote = (localDate: string, content: string, idempotencyKey?: string) => G.postApiAppQuickNote({ localDate, content, targetDiaryId: null }, idempotencyHeader(idempotencyKey))
export const getDiaries = () => G.getApiAppDiaries()
export const createDiary = (localDate: string, title: string, content: string, idempotencyKey?: string) => G.postApiAppDiaries({ localDate, title, content }, idempotencyHeader(idempotencyKey))
export const updateDiary = (id: string, localDate: string, title: string, content: string) => G.putApiAppDiariesId(id, { localDate, title, content })
export const deleteDiary = (id: string) => G.deleteApiAppDiariesId(id)
export const getTransactions = (diaryId: string) => G.getApiAppDiariesDiaryIdTransactions(diaryId)
export const createTransaction = (diaryId: string, body: G.TransactionWrite, idempotencyKey?: string) => G.postApiAppDiariesDiaryIdTransactions(diaryId, body, idempotencyHeader(idempotencyKey))
export const deleteTransaction = (diaryId: string, id: string) => G.deleteApiAppDiariesDiaryIdTransactionsId(diaryId, id)
export const getCalendar = (year: number, month: number) => G.getApiAppCalendar({ year, month })
export const savePerformance = (date: string, pnlAmount: number, capitalBase: number | null, note: string) =>
  G.putApiAppDailyPerformanceDate(date, { pnlAmount, capitalBase, note })
export const getDisciplines = () => G.getApiAppDisciplines()
export const createDiscipline = (content: string) => G.postApiAppDisciplines({ content })
export const deleteDiscipline = (id: string) => G.deleteApiAppDisciplinesId(id)
export const getAlerts = () => G.getApiAppDiaryAlerts()
export const createAlert = (body: G.DiaryAlertWrite) => G.postApiAppDiaryAlerts(body)
export const dismissAlert = (id: string) => G.postApiAppDiaryAlertsIdDismiss(id)
export const deleteAlert = (id: string) => G.deleteApiAppDiaryAlertsId(id)
export const getWatchlist = () => G.getApiAppWatchlist()
export async function addWatchlist(symbol: string) {
  const stock = await G.getApiAppStocksSymbol(symbol)
  return G.postApiAppWatchlistStockId(stock.id)
}
export const removeWatchlist = (id: string) => G.deleteApiAppWatchlistStockId(id)
export async function getResearchNote(id: string): Promise<ResearchNote | null> {
  try { return await G.getApiAppStocksStockIdNote(id) } catch (error) { if (String(error).includes('request_failed_404')) return null; throw error }
}
export const saveResearchNote = (id: string, content: string) => G.putApiAppStocksStockIdNote(id, { content })
export const getResearchTimeline = (id: string) => G.getApiAppStocksStockIdTimeline(id)
export const getPriceAlerts = () => G.getApiAppPriceAlerts()
export const addPriceAlert = (symbol: string, threshold: number, conditionType: string) =>
  G.postApiAppPriceAlerts({ symbol, threshold, conditionType, lookbackDays: null, direction: null })
export const deletePriceAlert = (id: string) => G.deleteApiAppPriceAlertsId(id)
export async function getMarketRotation(): Promise<{ snapshotDate: string | null; etfs: RotationItem[] }> {
  const universes = await G.getApiAppRotationUniverses()
  if (!universes.items.length) return { snapshotDate: null, etfs: [] }
  const monitor = await G.getApiAppRotationMonitor({ universe: universes.items[0].code })
  return { snapshotDate: monitor.snapshotDate, etfs: monitor.etfs }
}
export const getPartners = () => G.getApiAppPartners()
export const getArticles = () => G.getApiContentPosts()
export function calculate(tool: string, values: Record<string, unknown>): Promise<Record<string, number>> {
  const dispatch: Record<string, () => Promise<Record<string, number>>> = {
    'position-sizing': () => G.postApiAppToolsPositionSizing(values as unknown as G.PositionSizing) as Promise<Record<string, number>>,
    'risk-reward': () => G.postApiAppToolsRiskReward(values as unknown as G.RiskReward) as Promise<Record<string, number>>,
    'fire': () => G.postApiAppToolsFire(values as unknown as G.Fire) as Promise<Record<string, number>>,
    'relative-value': () => G.postApiAppToolsRelativeValue(values as unknown as G.RelativeValue) as Promise<Record<string, number>>,
    'seasonality': () => G.postApiAppToolsSeasonality(values as unknown as G.Seasonality) as Promise<Record<string, number>>,
  }
  const op = dispatch[tool]
  if (!op) throw new Error('unknown_tool')
  return op()
}
export async function createAgent(name: string) {
  return G.postApiAppAgents({ name, displayName: name, timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', baseCurrency: 'USD', scopes: ['research:read'], expiresAt: null })
}
