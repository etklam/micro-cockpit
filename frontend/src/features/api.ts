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
export type PriceAlertTrigger = G.TriggerResponse
export type PriceAlertEvaluationPrice = G.EvaluationPrice
export type WatchlistItem = G.WatchlistResponse
export type ResearchNote = G.NoteResponse
export type RotationItem = G.EtfSnapshotResponse
export type DiaryReview = G.DiaryReviewResponse
export type DiaryReviewWrite = G.DiaryReviewWrite
export type DiaryReviewSummary = G.DiaryReviewSummaryResponse

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
export async function getDiaryReview(diaryId: string): Promise<DiaryReview | null> {
  try { return await G.getApiAppDiariesDiaryIdReview(diaryId) } catch (error) { if (String(error).includes('request_failed_404')) return null; throw error }
}
export const saveDiaryReview = (diaryId: string, body: DiaryReviewWrite) => G.putApiAppDiariesDiaryIdReview(diaryId, body)
export const deleteDiaryReview = (diaryId: string) => G.deleteApiAppDiariesDiaryIdReview(diaryId)
export const getDiaryReviewSummary = (from: string, to: string) => G.getApiAppDiaryReviewSummary({ from, to })
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
export const addPriceAlert = (symbol: string, threshold: number, conditionType: string, evaluationPrice: PriceAlertEvaluationPrice) =>
  G.postApiAppPriceAlerts({ symbol, threshold, conditionType, evaluationPrice, lookbackDays: null, direction: null })
export const deletePriceAlert = (id: string) => G.deleteApiAppPriceAlertsId(id)
export const dismissPriceAlert = (id: string) => G.postApiAppPriceAlertsIdDismiss(id)
export const reactivatePriceAlert = (id: string) => G.postApiAppPriceAlertsIdReactivate(id)
export const getPriceAlertTriggers = (id: string) => G.getApiAppPriceAlertsIdTriggers(id)
export type PriceAlertCreateErrorKind = 'no_published_price' | 'invalid_request' | 'unavailable' | 'timeout' | 'unknown'
export function priceAlertCreateErrorKind(error: unknown): PriceAlertCreateErrorKind {
  if (!(error instanceof G.ApiError)) return 'unknown'
  if (error.responseBody.includes('symbol_has_no_published_price')) return 'no_published_price'
  if (error.status === 503) return 'unavailable'
  if (error.status === 504) return 'timeout'
  if (error.status === 400 || error.status === 422) return 'invalid_request'
  return 'unknown'
}
export const getRotationUniverses = () => G.getApiAppRotationUniverses()
export const getMarketRotation = (universe: string) => G.getApiAppRotationMonitor({ universe })
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
