import { useRef } from 'react'
import * as G from '../generated/edge'

const idempotencyHeader = (key?: string): RequestInit | undefined =>
  key ? { headers: { 'Idempotency-Key': key } } : undefined

export function useIdempotencyKey() {
  const ref = useRef<string | null>(null)
  return {
    key: () => { if (!ref.current) ref.current = crypto.randomUUID(); return ref.current },
    reset: () => { ref.current = null },
  }
}

export type Diary = G.DiaryResponse
export type Transaction = G.TransactionResponse
export type Discipline = G.DisciplineResponse
export type Alert = G.DiaryAlertResponse
export type PriceAlert = G.PriceAlertResponse
export type WatchlistItem = G.WatchlistResponse
export type ResearchNote = G.NoteResponse
export type RotationItem = G.EtfSnapshotResponse

export const getBootstrap = () => G.getApiAppBootstrap()
export const getDashboard = () => G.getApiAppDashboard()
export const saveQuickNote = (localDate: string, content: string, key?: string) =>
  G.postApiAppQuickNote({ localDate, content, targetDiaryId: null }, idempotencyHeader(key))
export const getDiaries = () => G.getApiAppDiaries()
export const getDiary = (id: string) => G.getApiAppDiariesId(id)
export const createDiary = (localDate: string, title: string, content: string, key?: string) =>
  G.postApiAppDiaries({ localDate, title, content }, idempotencyHeader(key))
export const updateDiary = (id: string, localDate: string, title: string, content: string) =>
  G.putApiAppDiariesId(id, { localDate, title, content })
export const deleteDiary = (id: string) => G.deleteApiAppDiariesId(id)
export const getTransactions = (diaryId: string) => G.getApiAppDiariesDiaryIdTransactions(diaryId)
export const createTransaction = (diaryId: string, body: G.TransactionWrite, key?: string) =>
  G.postApiAppDiariesDiaryIdTransactions(diaryId, body, idempotencyHeader(key))
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
export const getArticle = (slug: string) => G.getApiContentPostsSlug(slug)
export function calculate(tool: string, values: Record<string, unknown>): Promise<Record<string, number>> {
  const dispatch: Record<string, () => Promise<Record<string, number>>> = {
    'position-sizing': () => G.postApiAppToolsPositionSizing(values as unknown as G.PositionSizing) as Promise<Record<string, number>>,
    'risk-reward': () => G.postApiAppToolsRiskReward(values as unknown as G.RiskReward) as Promise<Record<string, number>>,
    'fire': () => G.postApiAppToolsFire(values as unknown as G.Fire) as Promise<Record<string, number>>,
    'relative-value': () => G.postApiAppToolsRelativeValue(values as unknown as G.RelativeValue) as Promise<Record<string, number>>,
    'seasonality': () => G.postApiAppToolsSeasonality(values as unknown as G.Seasonality) as Promise<Record<string, number>>,
  }
  const operation = dispatch[tool]
  if (!operation) throw new Error('unknown_tool')
  return operation()
}
export const createAgent = (name: string) => G.postApiAppAgents({
  name,
  displayName: name,
  timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
  baseCurrency: 'USD',
  scopes: ['research:read'],
  expiresAt: null,
})
