// Generated from contracts/openapi/edge-api.openapi.json. Do not edit.

export type AccessGrantMode = "fixed" | "ongoing"
export type AccessGrantResponse = { "id": string; "agentUserId": string; "mode": AccessGrantMode; "from": string; "to": string; "subjectType": null | string; "subject": null | string; "instrumentId": null | string; "expiresAt": null | string; "revokedAt": null | string; "createdAt": string }
export type AccessGrantWrite = { "agentUserId": string; "mode": AccessGrantMode; "from": string; "to": string; "subjectType"?: null | string; "subject"?: null | string; "instrumentId"?: null | string; "expiresAt"?: null | string }
export type AccountDeletionWrite = { "confirmation": string }
export type AccountExportResponse = { "schemaVersion": number; "exportedAt": string; "identity": { [key: string]: unknown }; "journal": { [key: string]: unknown }; "tools": { [key: string]: unknown } }
export type ActionDecisionEditResponse = { "id": string; "observationUpdateId": string; "expectationId": null | string; "intent": ActionDecisionIntent; "reason": string; "recordedAt": string; "executionReview": null | ExecutionReview; "updatedAt": string; "honestyReminderRequired": boolean }
export type ActionDecisionIntent = "trade" | "continue_observing" | "avoid_trade"
export type ActionDecisionResponse = { "id": string; "observationUpdateId": string; "expectationId": null | string; "intent": ActionDecisionIntent; "reason": string; "recordedAt": string; "executionReview": null | ExecutionReview; "updatedAt": string }
export type ActionDecisionWrite = { "intent": ActionDecisionIntent; "reason": string; "expectationId"?: null | string; "executionReview"?: null | ExecutionReview }
export type AgentManagementResponse = { "userId": string; "displayName": string; "timezone": string; "baseCurrency": string; "keyId": null | string; "scopes": Array<string>; "tokenCreatedAt": null | string; "lastUsedAt": null | string; "lastSuccessfulRequestAt": null | string }
export type AgentProvisionResponse = { "userId": string; "displayName": string; "keyId": string; "apiToken": string; "scopes": Array<string>; "createdAt": string; "lastUsedAt": null | string; "lastSuccessfulRequestAt": null | string }
export type AgentRequest = { "name": string; "displayName": string; "timezone": string; "baseCurrency": string; "scopes": Array<string>; "expiresAt": null | string }
export type AgentTokenRequest = { "scopes": Array<string> }
export type AgentTokenResponse = { "keyId": string; "apiToken": string; "scopes": Array<string>; "createdAt": string }
export type ApiKeyTokenRequest = { "apiKey": string }
export type ApiKeyTokenResponse = { "accessToken": string; "expiresAt": string }
export type AppBootstrapResponse = { "currentUser": { "id": string; "email": string; "displayName": string }; "timezone": string; "journalDayRollover": string; "baseCurrency": string; "appearance": string; "locale": string; "accentTheme": string; "role": string; "accountType": string; "currentJournalDay": string; "availableProductAreas": Array<string> }
export type AverageCost = { "currentQuantity": number; "currentAverageCost": number; "addedQuantity": number; "addedPrice": number }
export type AverageCostResponse = { "averageCost": number; "totalQuantity": number; "totalCost": number; "averageCostChange": number }
export type BarsResponse = { "contractVersion": number | string; "symbol": string; "items": Array<PublishedBarResponse> }
export type CalendarResponse = { "year": number; "month": number; "days": Array<{ "date": string; "marketObservationId": string | null; "updateCount": number; "readyForReviewCount": number | null }> }
export type CollectionResponseOfAccessGrantResponse = { "items": Array<AccessGrantResponse> }
export type CollectionResponseOfActionDecisionResponse = { "items": Array<ActionDecisionResponse> }
export type CollectionResponseOfAgentManagementResponse = { "items": Array<AgentManagementResponse> }
export type CollectionResponseOfDisciplinePrincipleResponse = { "items": Array<DisciplinePrincipleResponse> }
export type CollectionResponseOfExpectationResponse = { "items": Array<ExpectationResponse> }
export type CollectionResponseOfReasoningLabelResponse = { "items": Array<ReasoningLabelResponse> }
export type CollectionResponseOfTradeEvidenceResponse = { "items": Array<TradeEvidenceResponse> }
export type CollectionResponseOfWatchlistItemResponse = { "items": Array<WatchlistItemResponse> }
export type ComparisonAvailability = "available" | "empty" | "unavailable"
export type ComparisonDifferenceResponse = { "outcomeConsistent": null | boolean; "confidenceDifference": null | number | string }
export type ComparisonExpectationResponse = { "id": string; "expectedBehavior": string; "deadline": string; "invalidationCondition": string; "confidence": ExpectationConfidence; "market": string; "outcome": null | ExpectationOutcome; "reasoningQuality": null | ReasoningQuality; "reviewExplanation": null | string }
export type ComparisonObservationResponse = { "journalDay": string; "update": ObservationUpdateResponse; "expectations": Array<ComparisonExpectationResponse> }
export type ComparisonOwnerResponse = { "ownerId": string; "ownerType": ComparisonOwnerType; "availability": ComparisonAvailability; "observations": Array<ComparisonObservationResponse> }
export type ComparisonOwnerType = "human" | "agent"
export type DailyCloseEvidenceResponse = { "tradingDate": string; "rawClose": number; "adjustedClose": number; "provider": string; "publishedAt": string }
export type DailyCloseStatus = "available" | "unavailable" | "unsupported"
export type DisciplinePrincipleCreate = { "content": string }
export type DisciplinePrincipleResponse = { "id": string; "content": string; "status": DisciplinePrincipleStatus; "selectedForToday": boolean; "createdAt": string; "updatedAt": string }
export type DisciplinePrincipleStatus = "active" | "disabled" | "archived"
export type DisciplinePrincipleUpdate = { "content": string; "status": DisciplinePrincipleStatus }
export type EdgeProblemDetails = { "code": string; "title": string; "status": number; "detail": string; "correlationId": string }
export type ExecutionReview = "followed" | "partially_followed" | "deviated" | null
export type ExpectationConfidence = "low" | "medium" | "high"
export type ExpectationDeadlinePreset = "next_trading_day" | "five_trading_days" | null
export type ExpectationEditResponse = { "id": string; "observationUpdateId": string; "marketObservationId": string; "journalDay": string; "expectedBehavior": string; "deadline": string; "invalidationCondition": string; "confidence": ExpectationConfidence; "market": string; "invalidatedAt": null | string; "readiness": ExpectationReadiness; "deadlineElapsed": boolean; "createdAt": string; "updatedAt": string; "honestyReminderRequired": boolean }
export type ExpectationOutcome = "confirmed" | "partially_confirmed" | "invalidated" | "indeterminate"
export type ExpectationReadiness = "active" | "ready_for_review" | "reviewed"
export type ExpectationResponse = { "id": string; "observationUpdateId": string; "marketObservationId": string; "journalDay": string; "expectedBehavior": string; "deadline": string; "invalidationCondition": string; "confidence": ExpectationConfidence; "market": string; "invalidatedAt": null | string; "readiness": ExpectationReadiness; "deadlineElapsed": boolean; "createdAt": string; "updatedAt": string }
export type ExpectationReviewResponse = { "id": string; "expectationId": string; "outcome": ExpectationOutcome; "reasoningQuality": ReasoningQuality; "explanation": null | string; "labels": Array<ReasoningLabelResponse>; "createdAt": string; "updatedAt": string }
export type ExpectationReviewWrite = { "outcome": ExpectationOutcome; "reasoningQuality": ReasoningQuality; "explanation": null | string; "systemIssueKeys"?: null | Array<string>; "systemStrengthKeys"?: null | Array<string>; "customLabelIds"?: null | Array<string> }
export type ExpectationWrite = { "expectedBehavior": string; "deadline": null | string; "invalidationCondition": string; "confidence": ExpectationConfidence; "market": string; "deadlinePreset"?: null | ExpectationDeadlinePreset }
export type GrantedChangePage = { "items": Array<unknown>; "nextCursor": string; "hasMore": boolean }
export type GrantedObservationResponse = { "marketObservationId": string; "ownerId": string; "journalDay": string; "records": Array<GrantedRecordResponse> }
export type GrantedRecordPage = { "items": Array<GrantedObservationResponse>; "nextCursor": null | string; "syncCursor": string }
export type GrantedRecordResponse = { "recordType": string; "id": string; "ownerId": string; "updatedAt": string; "content": JsonElement }
export type JsonElement = unknown
export type LoginRequest = { "email": string; "password": string }
export type MarketObservationResponse = { "id": string; "journalDay": string; "timezone": string; "rollover": string; "updates": Array<ObservationUpdateResponse> }
export type ObservationEvidenceResponse = { "url": string; "title": null | string; "quote": null | string }
export type ObservationEvidenceWrite = { "url": string; "title"?: null | string; "quote"?: null | string }
export type ObservationSearchItemResponse = { "marketObservationId": string; "journalDay": string; "authorId": string; "update": ObservationUpdateResponse }
export type ObservationSearchPage = { "items": Array<ObservationSearchItemResponse>; "nextCursor": null | string }
export type ObservationSubjectResponse = { "type": ObservationSubjectType; "name": null | string; "instrumentId": null | string; "market": null | string; "symbol": null | string; "displayName": null | string; "dailyCloseAvailable": boolean; "dailyCloseStatus"?: DailyCloseStatus; "dailyClose"?: null | DailyCloseEvidenceResponse }
export type ObservationSubjectType = "broad_market" | "sector" | "theme" | "instrument"
export type ObservationSubjectWrite = { "type": ObservationSubjectType; "name"?: null | string; "instrumentId"?: null | string; "market"?: null | string; "symbol"?: null | string; "displayName"?: null | string }
export type ObservationUpdateEditResponse = { "id": string; "content": string; "recordedAt": string; "updatedAt": string; "honestyReminderRequired": boolean; "signal": null | string; "interpretation": null | string; "mentalState": null | string; "tags": Array<string>; "primarySubject": null | ObservationSubjectResponse; "relatedSubjects": Array<ObservationSubjectResponse>; "evidence": null | ObservationEvidenceResponse }
export type ObservationUpdateResponse = { "id": string; "content": string; "recordedAt": string; "updatedAt": string; "signal": null | string; "interpretation": null | string; "mentalState": null | string; "tags": Array<string>; "primarySubject": null | ObservationSubjectResponse; "relatedSubjects": Array<ObservationSubjectResponse>; "evidence": null | ObservationEvidenceResponse }
export type ObservationUpdateWrite = { "content": string; "signal"?: null | string; "interpretation"?: null | string; "mentalState"?: null | string; "tags"?: null | Array<string>; "primarySubject"?: null | ObservationSubjectWrite; "relatedSubjects"?: null | Array<ObservationSubjectWrite>; "evidence"?: null | ObservationEvidenceWrite; "sourceLabel"?: null | string }
export type OwnerComparisonResponse = { "human": ComparisonOwnerResponse; "agent": ComparisonOwnerResponse; "difference": ComparisonDifferenceResponse }
export type PatternEvidenceResponse = { "expectationId": string; "url": string }
export type PatternLabelResponse = { "kind": ReasoningLabelKind; "key": string; "name": string; "system": boolean; "count": number | string; "denominator": number | string; "evidence": Array<PatternEvidenceResponse> }
export type PatternReviewResponse = { "from": string; "to": string; "reviewedExpectationCount": number | string; "labels": Array<PatternLabelResponse> }
export type PositionSizing = { "accountValue": number; "riskPercent": number; "entryPrice": number; "stopPrice": number }
export type PositionSizingResponse = { "quantity": number; "plannedLoss": number; "riskBudget": number; "positionValue": number; "perUnitRisk": number }
export type PresetWrite = { "name": string; "toolType": string; "inputs": JsonElement; "currency": null | string }
export type ProfitLoss = { "side": string; "entryPrice": number; "exitPrice": number; "quantity": number; "entryFee": number; "exitFee": number }
export type ProfitLossResponse = { "netPnl": number; "returnPercent": number; "grossPnl": number; "totalFees": number; "exitValue": number }
export type ProviderHealthResponse = { "provider": string; "lastSuccessAt": string; "healthy": boolean }
export type ProvidersHealthResponse = { "contractVersion": number | string; "healthy": boolean; "items": Array<ProviderHealthResponse> }
export type PublishedBarResponse = { "tradingDate": string; "open": number; "high": number; "low": number; "close": number; "rawClose": number; "adjustedClose": number; "volume": number; "provider": string; "publishedAt": string }
export type PublishedSymbolResponse = { "instrumentId": string; "symbol": string; "name": string; "exchange": string; "currency": string; "timezone": string }
export type QuickObservationResponse = { "marketObservationId": string; "observationUpdateId": string; "journalDay": string; "recordedAt": string; "appended": boolean }
export type QuickObservationWrite = { "content": string; "sourceLabel"?: null | string; "journalDay"?: null | string }
export type ReasoningLabelKind = "issue" | "strength"
export type ReasoningLabelResponse = { "id": null | string; "kind": ReasoningLabelKind; "key": string; "name": string; "isSystem": boolean }
export type ReasoningLabelWrite = { "kind": ReasoningLabelKind; "name": string }
export type ReasoningQuality = "sound" | "mixed" | "weak"
export type RegisterRequest = { "email": string; "password": string; "displayName": string; "timezone": string; "baseCurrency": string }
export type RegisterResponse = { "id": string; "email": string; "displayName": string; "timezone": string; "baseCurrency": string }
export type RiskReward = { "entryPrice": number; "stopPrice": number; "targetPrice": number }
export type RiskRewardResponse = { "ratio": number; "riskPerUnit": number; "rewardPerUnit": number; "breakevenWinRate": number }
export type SavedCalculationWrite = { "toolType": string; "inputs": JsonElement; "currency": string; "symbol": null | string; "note": null | string }
export type SessionTokens = { "accessToken": string; "expiresAt": string }
export type SymbolsResponse = { "contractVersion": number | string; "items": Array<PublishedSymbolResponse> }
export type TradeEvidenceResponse = { "id": string; "actionDecisionId": string; "symbol": string; "side": TradeSide; "quantity": number; "price": number; "currency": string; "executedAt": string; "note": null | string; "createdAt": string; "updatedAt": string }
export type TradeEvidenceWrite = { "symbol": string; "side": TradeSide; "quantity": number; "price": number; "currency": string; "executedAt": string; "note"?: null | string }
export type TradeSide = "buy" | "sell"
export type UserSettingsResponse = { "email": string; "displayName": string; "timezone": string; "journalDayRollover": string; "baseCurrency": string; "appearance": string; "locale": string; "accentTheme": string; "updatedAt": string }
export type UserSettingsWrite = { "displayName": string; "timezone": string; "journalDayRollover": string; "baseCurrency": string; "appearance": string; "locale": string; "accentTheme": string }
export type WatchlistItemResponse = { "instrumentId": string; "note": null | string; "createdAt": string; "updatedAt": string }
export type WatchlistCreateWrite = { "note": string }
export type WatchlistNoteWrite = { "note": null | string }

export type RequestOptions = { baseUrl?: string; token?: string | null; refresh?: () => Promise<string | null>; onUnauthorized?: () => void }
export class ApiError extends Error {
  readonly status: number
  readonly responseBody: string
  constructor(status: number, responseBody: string) {
    super(`request_failed_${status}`)
    this.status = status
    this.responseBody = responseBody
  }
}

let options: RequestOptions = {}
let refreshInFlight: Promise<string | null> | null = null
export const configureClient = (next: RequestOptions) => { options = next; refreshInFlight = null }
async function send(path: string, init: RequestInit, token: string | null | undefined): Promise<Response> {
  return fetch(`${options.baseUrl ?? ''}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers } })
}
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response = await send(path, init, options.token)
  if (response.status === 401 && options.refresh && !path.endsWith('/api/auth/refresh')) {
    refreshInFlight ??= options.refresh().finally(() => { refreshInFlight = null })
    const fresh = await refreshInFlight
    if (fresh) response = await send(path, init, fresh)
    else { options.onUnauthorized?.(); throw new ApiError(401, '') }
  }
  if (response.status === 401) options.onUnauthorized?.()
  if (!response.ok) throw new ApiError(response.status, await response.text())
  return response.status === 204 ? undefined as T : response.json()
}
const withQuery = (query: Record<string, unknown>) => {
  const params = new URLSearchParams(Object.entries(query).filter(([, value]) => value !== undefined && value !== null).map(([key, value]) => [key, String(value)]))
  return params.size ? `?${params}` : ''
}

export const postApiAuthRegister = (body: RegisterRequest, extra?: RequestInit) => request<RegisterResponse>("/api/auth/register", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAuthApiKeyToken = (body: ApiKeyTokenRequest, extra?: RequestInit) => request<ApiKeyTokenResponse>("/api/auth/api-key/token", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppAgents = (extra?: RequestInit) => request<CollectionResponseOfAgentManagementResponse>("/api/app/agents", { method: "GET", ...extra })
export const postApiAppAgents = (body: AgentRequest, extra?: RequestInit) => request<AgentProvisionResponse>("/api/app/agents", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppAgentsIdToken = (id: string, body: AgentTokenRequest, extra?: RequestInit) => request<AgentTokenResponse>(`/api/app/agents/${encodeURIComponent(String(id))}/token`, { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppAgentsIdToken = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/agents/${encodeURIComponent(String(id))}/token`, { method: "DELETE", ...extra })
export const getApiAppAccessGrants = (extra?: RequestInit) => request<CollectionResponseOfAccessGrantResponse>("/api/app/access-grants", { method: "GET", ...extra })
export const postApiAppAccessGrants = (body: AccessGrantWrite, extra?: RequestInit) => request<AccessGrantResponse>("/api/app/access-grants", { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppAccessGrantsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/access-grants/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAgentJournalRecords = (query: { "from"?: string; "to"?: string; "subjectType"?: string; "subject"?: string; "instrumentId"?: string; "tag"?: string; "reviewReadiness"?: string; "author"?: string; "cursor"?: string; "limit"?: number | string }, extra?: RequestInit) => request<GrantedRecordPage>("/api/agent/journal-records" + withQuery(query), { method: "GET", ...extra })
export const getApiAgentJournalChanges = (query: { "cursor": string; "from"?: string; "to"?: string; "subjectType"?: string; "subject"?: string; "instrumentId"?: string; "tag"?: string; "reviewReadiness"?: string; "author"?: string; "limit"?: number | string }, extra?: RequestInit) => request<GrantedChangePage>("/api/agent/journal-changes" + withQuery(query), { method: "GET", ...extra })
export const deleteApiAppApiKeysId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/api-keys/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppQuickObservations = (body: QuickObservationWrite, extra?: RequestInit) => request<QuickObservationResponse>("/api/app/quick-observations", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppMarketObservations = (query: { "query"?: string; "from"?: string; "to"?: string; "subjectType"?: ObservationSubjectType; "subject"?: string; "instrumentId"?: string; "market"?: string; "symbol"?: string; "tag"?: string; "author"?: string; "cursor"?: string; "limit"?: number | string }, extra?: RequestInit) => request<ObservationSearchPage>("/api/app/market-observations" + withQuery(query), { method: "GET", ...extra })
export const deleteApiAppMarketObservationsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/market-observations/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppMarketObservationsToday = (extra?: RequestInit) => request<MarketObservationResponse>("/api/app/market-observations/today", { method: "GET", ...extra })
export const putApiAppObservationUpdatesId = (id: string, body: ObservationUpdateWrite, extra?: RequestInit) => request<ObservationUpdateEditResponse>(`/api/app/observation-updates/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppObservationUpdatesId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/observation-updates/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppObservationUpdatesUpdateIdExpectations = (updateId: string, body: ExpectationWrite, extra?: RequestInit) => request<ExpectationResponse>(`/api/app/observation-updates/${encodeURIComponent(String(updateId))}/expectations`, { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppExpectations = (query: { "observationUpdateId"?: string }, extra?: RequestInit) => request<CollectionResponseOfExpectationResponse>("/api/app/expectations" + withQuery(query), { method: "GET", ...extra })
export const getApiAppExpectationsId = (id: string, extra?: RequestInit) => request<ExpectationResponse>(`/api/app/expectations/${encodeURIComponent(String(id))}`, { method: "GET", ...extra })
export const putApiAppExpectationsId = (id: string, body: ExpectationWrite, extra?: RequestInit) => request<ExpectationEditResponse>(`/api/app/expectations/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppExpectationsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/expectations/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppExpectationsIdInvalidate = (id: string, extra?: RequestInit) => request<ExpectationResponse>(`/api/app/expectations/${encodeURIComponent(String(id))}/invalidate`, { method: "POST", ...extra })
export const getApiAppExpectationsIdReview = (id: string, extra?: RequestInit) => request<ExpectationReviewResponse>(`/api/app/expectations/${encodeURIComponent(String(id))}/review`, { method: "GET", ...extra })
export const putApiAppExpectationsIdReview = (id: string, body: ExpectationReviewWrite, extra?: RequestInit) => request<ExpectationReviewResponse>(`/api/app/expectations/${encodeURIComponent(String(id))}/review`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppExpectationsIdReview = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/expectations/${encodeURIComponent(String(id))}/review`, { method: "DELETE", ...extra })
export const getApiAppReasoningLabels = (extra?: RequestInit) => request<CollectionResponseOfReasoningLabelResponse>("/api/app/reasoning-labels", { method: "GET", ...extra })
export const postApiAppReasoningLabels = (body: ReasoningLabelWrite, extra?: RequestInit) => request<ReasoningLabelResponse>("/api/app/reasoning-labels", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppReasoningLabelsId = (id: string, body: ReasoningLabelWrite, extra?: RequestInit) => request<ReasoningLabelResponse>(`/api/app/reasoning-labels/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppReasoningLabelsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/reasoning-labels/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppObservationUpdatesUpdateIdActionDecisions = (updateId: string, extra?: RequestInit) => request<CollectionResponseOfActionDecisionResponse>(`/api/app/observation-updates/${encodeURIComponent(String(updateId))}/action-decisions`, { method: "GET", ...extra })
export const postApiAppObservationUpdatesUpdateIdActionDecisions = (updateId: string, body: ActionDecisionWrite, extra?: RequestInit) => request<ActionDecisionResponse>(`/api/app/observation-updates/${encodeURIComponent(String(updateId))}/action-decisions`, { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppActionDecisionsId = (id: string, extra?: RequestInit) => request<ActionDecisionResponse>(`/api/app/action-decisions/${encodeURIComponent(String(id))}`, { method: "GET", ...extra })
export const putApiAppActionDecisionsId = (id: string, body: ActionDecisionWrite, extra?: RequestInit) => request<ActionDecisionEditResponse>(`/api/app/action-decisions/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppActionDecisionsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/action-decisions/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppActionDecisionsDecisionIdTrades = (decisionId: string, extra?: RequestInit) => request<CollectionResponseOfTradeEvidenceResponse>(`/api/app/action-decisions/${encodeURIComponent(String(decisionId))}/trades`, { method: "GET", ...extra })
export const postApiAppActionDecisionsDecisionIdTrades = (decisionId: string, body: TradeEvidenceWrite, extra?: RequestInit) => request<TradeEvidenceResponse>(`/api/app/action-decisions/${encodeURIComponent(String(decisionId))}/trades`, { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppActionDecisionsDecisionIdTradesId = (decisionId: string, id: string, body: TradeEvidenceWrite, extra?: RequestInit) => request<TradeEvidenceResponse>(`/api/app/action-decisions/${encodeURIComponent(String(decisionId))}/trades/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppActionDecisionsDecisionIdTradesId = (decisionId: string, id: string, extra?: RequestInit) => request<unknown>(`/api/app/action-decisions/${encodeURIComponent(String(decisionId))}/trades/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppWatchlist = (extra?: RequestInit) => request<CollectionResponseOfWatchlistItemResponse>("/api/app/watchlist", { method: "GET", ...extra })
export const postApiAppWatchlistInstrumentId = (instrumentId: string, body: WatchlistCreateWrite, extra?: RequestInit) => request<WatchlistItemResponse>(`/api/app/watchlist/${encodeURIComponent(String(instrumentId))}`, { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppWatchlistInstrumentId = (instrumentId: string, extra?: RequestInit) => request<unknown>(`/api/app/watchlist/${encodeURIComponent(String(instrumentId))}`, { method: "DELETE", ...extra })
export const putApiAppWatchlistInstrumentIdNote = (instrumentId: string, body: WatchlistNoteWrite, extra?: RequestInit) => request<WatchlistItemResponse>(`/api/app/watchlist/${encodeURIComponent(String(instrumentId))}/note`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppPatternReview = (query: { "range": string; "from"?: string; "to"?: string }, extra?: RequestInit) => request<PatternReviewResponse>("/api/app/pattern-review" + withQuery(query), { method: "GET", ...extra })
export const getApiAppComparison = (query: { "agentUserId": string; "from": string; "to": string; "subjectType"?: string; "subject"?: string; "instrumentId"?: string }, extra?: RequestInit) => request<OwnerComparisonResponse>("/api/app/comparison" + withQuery(query), { method: "GET", ...extra })
export const getApiAppDisciplinePrinciples = (extra?: RequestInit) => request<CollectionResponseOfDisciplinePrincipleResponse>("/api/app/discipline-principles", { method: "GET", ...extra })
export const postApiAppDisciplinePrinciples = (body: DisciplinePrincipleCreate, extra?: RequestInit) => request<DisciplinePrincipleResponse>("/api/app/discipline-principles", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppDisciplinePrinciplesToday = (extra?: RequestInit) => request<DisciplinePrincipleResponse>("/api/app/discipline-principles/today", { method: "GET", ...extra })
export const putApiAppDisciplinePrinciplesId = (id: string, body: DisciplinePrincipleUpdate, extra?: RequestInit) => request<unknown>(`/api/app/discipline-principles/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const postApiAppDisciplinePrinciplesIdSelect = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/discipline-principles/${encodeURIComponent(String(id))}/select`, { method: "POST", ...extra })
export const getApiAppMarketSymbols = (extra?: RequestInit) => request<SymbolsResponse>("/api/app/market/symbols", { method: "GET", ...extra })
export const getApiAppMarketBarsSymbol = (symbol: string, query: { "from"?: string; "to"?: string }, extra?: RequestInit) => request<BarsResponse>(`/api/app/market/bars/${encodeURIComponent(String(symbol))}` + withQuery(query), { method: "GET", ...extra })
export const getApiAppMarketProvidersHealth = (extra?: RequestInit) => request<ProvidersHealthResponse>("/api/app/market/providers/health", { method: "GET", ...extra })
export const postApiAppToolsPositionSizing = (body: PositionSizing, extra?: RequestInit) => request<PositionSizingResponse>("/api/app/tools/position-sizing", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsRiskReward = (body: RiskReward, extra?: RequestInit) => request<RiskRewardResponse>("/api/app/tools/risk-reward", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsAverageCost = (body: AverageCost, extra?: RequestInit) => request<AverageCostResponse>("/api/app/tools/average-cost", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsProfitLoss = (body: ProfitLoss, extra?: RequestInit) => request<ProfitLossResponse>("/api/app/tools/profit-loss", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppToolPresets = (extra?: RequestInit) => request<unknown>("/api/app/tool-presets", { method: "GET", ...extra })
export const postApiAppToolPresets = (body: PresetWrite, extra?: RequestInit) => request<unknown>("/api/app/tool-presets", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppToolPresetsId = (id: string, body: PresetWrite, extra?: RequestInit) => request<unknown>(`/api/app/tool-presets/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppToolPresetsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/tool-presets/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppToolPresetsIdUse = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/tool-presets/${encodeURIComponent(String(id))}/use`, { method: "POST", ...extra })
export const getApiAppSavedCalculations = (query: { "limit"?: number | string }, extra?: RequestInit) => request<unknown>("/api/app/saved-calculations" + withQuery(query), { method: "GET", ...extra })
export const postApiAppSavedCalculations = (body: SavedCalculationWrite, extra?: RequestInit) => request<unknown>("/api/app/saved-calculations", { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppSavedCalculationsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/saved-calculations/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppBootstrap = (extra?: RequestInit) => request<AppBootstrapResponse>("/api/app/bootstrap", { method: "GET", ...extra })
export const getApiAppCalendar = (query: { "year": number; "month": number }, extra?: RequestInit) => request<CalendarResponse>("/api/app/calendar" + withQuery(query), { method: "GET", ...extra })
export const getApiAppSettings = (extra?: RequestInit) => request<UserSettingsResponse>("/api/app/settings", { method: "GET", ...extra })
export const putApiAppSettings = (body: UserSettingsWrite, extra?: RequestInit) => request<UserSettingsResponse>("/api/app/settings", { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppAccountExport = (extra?: RequestInit) => request<AccountExportResponse>("/api/app/account-export", { method: "GET", ...extra })
export const deleteApiAppAccount = (body: AccountDeletionWrite, extra?: RequestInit) => request<unknown>("/api/app/account", { method: "DELETE", body: JSON.stringify(body), ...extra })
export const postApiAuthLogin = (body: LoginRequest, extra?: RequestInit) => request<SessionTokens>("/api/auth/login", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAuthRefresh = (extra?: RequestInit) => request<SessionTokens>("/api/auth/refresh", { method: "POST", ...extra })
export const postApiAuthLogout = (extra?: RequestInit) => request<unknown>("/api/auth/logout", { method: "POST", ...extra })
