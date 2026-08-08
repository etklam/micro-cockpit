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

export type Discipline = G.DisciplinePrincipleResponse
export type DisciplinePrincipleStatus = G.DisciplinePrincipleStatus
export type PatternReview = G.PatternReviewResponse
export type PatternLabel = G.PatternLabelResponse
export type ConfirmedPattern = G.ConfirmedPatternResponse
export type OwnerComparison = G.OwnerComparisonResponse
export type WatchlistItem = G.WatchlistItemResponse
export type MarketObservation = G.MarketObservationResponse
export type ObservationUpdate = G.ObservationUpdateResponse
export type Expectation = G.ExpectationResponse
export type ExpectationWrite = G.ExpectationWrite
export type ExpectationConfidence = G.ExpectationConfidence
export type ExpectationDeadlinePreset = NonNullable<G.ExpectationDeadlinePreset>
export type ExpectationReview = G.ExpectationReviewResponse
export type ExpectationReviewWrite = G.ExpectationReviewWrite
export type ExpectationReviewContext = G.ExpectationReviewContextResponse
export type ReasoningLabel = G.ReasoningLabelResponse
export type ReasoningLabelKind = G.ReasoningLabelKind
export type ActionDecision = G.ActionDecisionResponse
export type ActionDecisionWrite = G.ActionDecisionWrite
export type ActionDecisionIntent = G.ActionDecisionIntent
export type ExecutionReview = G.ExecutionReview
export type TradeEvidence = G.TradeEvidenceResponse
export type TradeEvidenceWrite = G.TradeEvidenceWrite
export type ObservationUpdateWrite = G.ObservationUpdateWrite
export type ObservationSubjectWrite = G.ObservationSubjectWrite
export type ObservationSearchItem = G.ObservationSearchItemResponse
export type ObservationSearchPage = G.ObservationSearchPage
export type InstrumentDirectoryItem = G.PublishedSymbolResponse
export type ObservationSearchFilters = {
  query?: string
  from?: string
  to?: string
  subjectType?: G.ObservationSubjectType
  subject?: string
  instrumentId?: string
  market?: string
  symbol?: string
  tag?: string
  author?: string
}

export const getBootstrap = () => G.getApiAppBootstrap()
export type Bootstrap = Awaited<ReturnType<typeof getBootstrap>>
export type UserSettings = G.UserSettingsResponse
export type UserSettingsWrite = G.UserSettingsWrite
export const getSettings = () => G.getApiAppSettings()
export const putSettings = (body: UserSettingsWrite) => G.putApiAppSettings(body)
export const getAccountExport = () => G.getApiAppAccountExport()
export const deleteAccount = () => G.deleteApiAppAccount({ confirmation: 'DELETE' })

export async function getTodayMarketObservation(): Promise<MarketObservation | null> {
  try {
    return await G.getApiAppMarketObservationsToday()
  } catch (error) {
    if (error instanceof G.ApiError && error.status === 404) return null
    throw error
  }
}
export const saveQuickObservation = (content: string, key?: string, sourceLabel?: string, journalDay?: string) =>
  G.postApiAppQuickObservations({ content, sourceLabel: sourceLabel || null, ...(journalDay ? { journalDay } : {}) }, idempotencyHeader(key))
export const updateObservation = (id: string, body: ObservationUpdateWrite) =>
  G.putApiAppObservationUpdatesId(id, body)
export const getObservationHistory = (filters: ObservationSearchFilters, cursor?: string) =>
  G.getApiAppMarketObservations({ ...filters, cursor, limit: 20 })

export const getExpectations = async () => (await G.getApiAppExpectations({})).items
export const createExpectation = (updateId: string, body: ExpectationWrite, key?: string) =>
  G.postApiAppObservationUpdatesUpdateIdExpectations(updateId, body, idempotencyHeader(key))
export const updateExpectation = (id: string, body: ExpectationWrite) => G.putApiAppExpectationsId(id, body)
export const invalidateExpectation = (id: string) => G.postApiAppExpectationsIdInvalidate(id)
export async function getExpectationReview(id: string): Promise<ExpectationReview | null> {
  try {
    return await G.getApiAppExpectationsIdReview(id)
  } catch (error) {
    if (error instanceof G.ApiError && error.status === 404) return null
    throw error
  }
}
export const saveExpectationReview = (id: string, body: ExpectationReviewWrite) =>
  G.putApiAppExpectationsIdReview(id, body)
export const getExpectationReviewContext = (id: string) =>
  G.getApiAppExpectationsIdReviewContext(id)

export const getReasoningLabels = async () => (await G.getApiAppReasoningLabels()).items
export const createReasoningLabel = (kind: ReasoningLabelKind, name: string) =>
  G.postApiAppReasoningLabels({ kind, name })
export const getActionDecisions = async (updateId: string) =>
  (await G.getApiAppObservationUpdatesUpdateIdActionDecisions(updateId)).items
export const createActionDecision = (updateId: string, body: ActionDecisionWrite) =>
  G.postApiAppObservationUpdatesUpdateIdActionDecisions(updateId, body)
export const updateActionDecision = (id: string, body: ActionDecisionWrite) =>
  G.putApiAppActionDecisionsId(id, body)
export const deleteActionDecision = (id: string) => G.deleteApiAppActionDecisionsId(id)
export const getTradeEvidence = async (decisionId: string) =>
  (await G.getApiAppActionDecisionsDecisionIdTrades(decisionId)).items
export const createTradeEvidence = (decisionId: string, body: TradeEvidenceWrite) =>
  G.postApiAppActionDecisionsDecisionIdTrades(decisionId, body)

export const getInstrumentDirectory = async () => (await G.getApiAppMarketSymbols()).items
export const getCalendar = (year: number, month: number) => G.getApiAppCalendar({ year, month })
export const getDisciplines = () => G.getApiAppDisciplinePrinciples()
export const createDiscipline = (content: string, confirmedPatternId?: string) =>
  G.postApiAppDisciplinePrinciples({ content, confirmedPatternId: confirmedPatternId ?? null })
export const updateDiscipline = (id: string, content: string, status: DisciplinePrincipleStatus) =>
  G.putApiAppDisciplinePrinciplesId(id, { content, status })
export const selectDiscipline = (id: string) => G.postApiAppDisciplinePrinciplesIdSelect(id)
export async function getTodayDiscipline(): Promise<Discipline | null> {
  try {
    return await G.getApiAppDisciplinePrinciplesToday()
  } catch (error) {
    if (error instanceof G.ApiError && error.status === 404) return null
    throw error
  }
}
export const getPatternReview = (range: 'weekly' | 'monthly' | 'custom', from?: string, to?: string) =>
  G.getApiAppPatternReview({ range, from, to })
export const confirmPattern = (kind: G.ReasoningLabelKind, key: string) =>
  G.postApiAppConfirmedPatterns({ kind, key })
export const unconfirmPattern = (id: string) =>
  G.deleteApiAppConfirmedPatternsId(id)
export type ComparisonQuery = Parameters<typeof G.getApiAppComparison>[0]
export const getComparison = (query: ComparisonQuery) => G.getApiAppComparison(query)

export const getWatchlist = async () => (await G.getApiAppWatchlist()).items
export const addWatchlist = (instrumentId: string, note: string) =>
  G.postApiAppWatchlistInstrumentId(instrumentId, { note: note.trim() })
export const removeWatchlist = (instrumentId: string) => G.deleteApiAppWatchlistInstrumentId(instrumentId)
export const saveWatchlistNote = (instrumentId: string, note: string) =>
  G.putApiAppWatchlistInstrumentIdNote(instrumentId, { note: note || null })

const agentScopes = ['journal:read', 'journal:write', 'agent:read']
export const getAgents = () => G.getApiAppAgents()
export const createAgent = (name: string) => G.postApiAppAgents({
  name,
  displayName: name,
  timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
  baseCurrency: 'USD',
  scopes: agentScopes,
  expiresAt: null,
})
export const rotateAgentToken = (id: string) => G.postApiAppAgentsIdToken(id, { scopes: agentScopes })
export const revokeAgentToken = (id: string) => G.deleteApiAppAgentsIdToken(id)
export type AgentManagement = G.AgentManagementResponse
export type AccessGrant = G.AccessGrantResponse
export const getAccessGrants = () => G.getApiAppAccessGrants()
export const createAccessGrant = (body: G.AccessGrantWrite) => G.postApiAppAccessGrants(body)
export const revokeAccessGrant = (id: string) => G.deleteApiAppAccessGrantsId(id)
export type AccessGrantWrite = G.AccessGrantWrite
