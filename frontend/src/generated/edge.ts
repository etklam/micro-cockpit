// Generated from contracts/openapi/edge-api.openapi.json. Do not edit.

export type AgentRequest = { "name": string; "displayName": string; "timezone": string; "baseCurrency": string; "scopes": Array<string>; "expiresAt": null | string }
export type AgentResponse = { "userId": string; "keyId": string; "apiKey": string; "scopes": Array<string> }
export type ApiKeyTokenRequest = { "apiKey": string }
export type ApiKeyTokenResponse = { "accessToken": string; "expiresAt": string }
export type AppBootstrapResponse = { "currentUser": { "id": string; "email": string; "displayName": string }; "timezone": string; "baseCurrency": string; "role": string; "accountType": string; "currentLocalDate": string; "availableProductAreas": Array<string> }
export type AuditResponse = { "id": string; "actorUserId": null | string; "action": string; "resourceType": string; "resourceId": null | string; "details": JsonElement; "occurredAt": string }
export type AuthorizationResponse = { "allowed": boolean }
export type BarsResponse = { "contractVersion": number | string; "symbol": string; "items": Array<PublishedBarResponse> }
export type CalculateResponse = { "universeId": string; "snapshotDate": string; "status": string; "formulaVersion": string }
export type CalendarResponse = { "year": number; "month": number; "summary": MonthSummaryResponse | null; "days": Array<{ "date": string; "performance": PerformanceResponse | null; "diaryCount": number; "transactionCount": number; "alertCount": number | null }>; "capabilities": { "alerts": CapabilityStatus } }
export type CapabilityStatus = "available" | "empty" | "unavailable"
export type CollectionResponseOfAuditResponse = { "items": Array<AuditResponse> }
export type CollectionResponseOfDiaryAlertResponse = { "items": Array<DiaryAlertResponse> }
export type CollectionResponseOfDiaryResponse = { "items": Array<DiaryResponse> }
export type CollectionResponseOfDisciplineResponse = { "items": Array<DisciplineResponse> }
export type CollectionResponseOfJobResponse = { "items": Array<JobResponse> }
export type CollectionResponseOfLink = { "items": Array<Link> }
export type CollectionResponseOfPostResponse = { "items": Array<PostResponse> }
export type CollectionResponseOfPriceAlertResponse = { "items": Array<PriceAlertResponse> }
export type CollectionResponseOfStockResponse = { "items": Array<StockResponse> }
export type CollectionResponseOfTimelineResponse = { "items": Array<TimelineResponse> }
export type CollectionResponseOfTransactionResponse = { "items": Array<TransactionResponse> }
export type CollectionResponseOfTriggerResponse = { "items": Array<TriggerResponse> }
export type CollectionResponseOfUniverseResponse = { "items": Array<UniverseResponse> }
export type CollectionResponseOfWatchlistResponse = { "items": Array<WatchlistResponse> }
export type CreatedResponse = { "id": string }
export type DashboardResponse = { "localDate": string; "diary": { "writtenToday": boolean; "count": number }; "performance": PerformanceResponse | null; "pendingAlerts": number | null; "discipline": DisciplineResponse | null; "recentDiaries": Array<DiaryResponse>; "capabilities": { "alerts": CapabilityStatus; "discipline": CapabilityStatus } }
export type DiaryAlertResponse = { "id": string; "diaryId": string; "startLocalDate": string; "nextLocalDate": null | string; "localTime": string; "timezone": string; "repeatMode": string; "recurrenceEndLocalDate": string; "nextTriggerAt": null | string; "status": string; "createdAt": string; "updatedAt": string }
export type DiaryAlertWrite = { "diaryId": string; "startLocalDate": string; "localTime": string; "timezone": string; "repeatMode": string }
export type DiaryResponse = { "id": string; "localDate": string; "title": string; "content": string; "createdAt": string; "updatedAt": string }
export type DiaryReviewResponse = { "diaryId": string; "thesis": null | string; "plannedAction": null | string; "actualAction": null | string; "emotion": null | string; "disciplineScore": null | number | string; "executionScore": null | number | string; "processAssessment": null | string; "mistakeTags": Array<string>; "lesson": null | string; "nextAction": null | string; "createdAt": string; "updatedAt": string }
export type DiaryReviewSummaryResponse = { "reviewedCount": number | string; "averageDisciplineScore": number | null; "averageExecutionScore": number | null; "emotionCounts": { [key: string]: number | string }; "processAssessmentCounts": { [key: string]: number | string }; "topMistakeTags": Array<MistakeTagCountResponse> }
export type DiaryReviewWrite = { "thesis": null | string; "plannedAction": null | string; "actualAction": null | string; "emotion": null | string; "disciplineScore": null | number | string; "executionScore": null | number | string; "processAssessment": null | string; "mistakeTags": null | Array<string>; "lesson": null | string; "nextAction": null | string }
export type DiaryWrite = { "localDate": string; "title": string; "content": null | string }
export type DisciplineResponse = { "id": string; "content": string; "position": number | string; "createdAt": string; "updatedAt": string }
export type DisciplineWrite = { "content": string }
export type EdgeProblemDetails = { "code": string; "title": string; "status": number; "detail": string; "correlationId": string }
export type EtfSnapshotResponse = { "symbol": string; "label": string; "sector": null | string; "close": number | null; "return2w": number | null; "return1m": number | null; "return3m": number | null; "rank2w": null | number | string; "rankGroup": string; "percentile2w": number | null; "aboveMa20": null | boolean; "aboveMa50": null | boolean; "aboveMa200": null | boolean; "status": string }
export type EvaluationPrice = "open" | "close"
export type Fire = { "annualExpenses": number; "withdrawalRatePercent": number; "investedAssets": number }
export type FireResponse = { "target": number; "gap": number }
export type HealthWrite = { "serviceName": string; "status": string }
export type JobAcceptedResponse = { "id": string; "status": string }
export type JobResponse = { "id": string; "jobType": string; "status": string; "requestedBy": string; "createdAt": string; "updatedAt": string }
export type JobWrite = { "jobType": string; "payload": JsonElement }
export type JsonElement = unknown
export type Link = { "id": string; "requesterUserId": string; "partnerUserId": string; "partnerType": string; "status": string; "createdAt": string; "updatedAt": string }
export type LinkWrite = { "partnerUserId": string; "partnerType": string }
export type LoginRequest = { "email": string; "password": string }
export type MistakeTagCountResponse = { "tag": string; "count": number | string }
export type MonitorMarketState = { "state": null | string; "breadthPercent": number | null; "benchmarkAboveMa200": null | boolean; "status": string }
export type MonitorResponse = { "universe": MonitorUniverse; "snapshotDate": null | string; "formulaVersion": string; "status": string; "marketState": MonitorMarketState; "sectorBreadth": Array<SectorBreadthResponse>; "etfs": Array<EtfSnapshotResponse> }
export type MonitorUniverse = { "id": string; "code": string; "name": string; "rankScope": string }
export type MonthSummaryResponse = { "year": number | string; "month": number | string; "total": number; "recordedDays": number | string; "profitDays": number | string; "lossDays": number | string; "flatDays": number | string; "bestDay": number | null; "worstDay": number | null }
export type NoteResponse = { "stockId": string; "content": string; "createdAt": string; "updatedAt": string }
export type NoteWrite = { "content": null | string }
export type PerformanceResponse = { "localDate": string; "pnlAmount": number; "capitalBase": number | null; "pnlPercent": number | null; "note": string }
export type PerformanceWrite = { "pnlAmount": number; "capitalBase": number | null; "note": null | string }
export type PositionSizing = { "accountValue": number; "riskPercent": number; "entryPrice": number; "stopPrice": number }
export type PositionSizingResponse = { "riskAmount": number; "quantity": number; "perUnitRisk": number }
export type PostResponse = { "id": string; "slug": string; "title": string; "body": string; "publishedAt": string }
export type PostWrite = { "slug": string; "title": string; "body": null | string; "status": string }
export type PriceAlertResponse = { "id": string; "symbol": string; "conditionType": string; "threshold": number; "lookbackDays": null | number | string; "direction": null | string; "evaluationPrice": EvaluationPrice; "status": PriceAlertStatus; "baselineClose": number | null; "lastEvaluatedDate": null | string; "createdAt": string; "updatedAt": string }
export type PriceAlertStatus = "active" | "triggered" | "dismissed"
export type PriceAlertWrite = { "symbol": string; "conditionType": string; "threshold": number; "lookbackDays": null | number | string; "direction": null | string; "evaluationPrice"?: null | EvaluationPrice }
export type ProviderHealthResponse = { "provider": string; "lastSuccessAt": string; "healthy": boolean }
export type ProvidersHealthResponse = { "contractVersion": number | string; "healthy": boolean; "items": Array<ProviderHealthResponse> }
export type PublishedBarResponse = { "tradingDate": string; "open": number; "high": number; "low": number; "close": number; "volume": number; "provider": string; "publishedAt": string }
export type PublishedSymbolResponse = { "symbol": string; "name": string; "exchange": string; "currency": string; "timezone": string }
export type QuickNote = { "localDate": string; "content": string; "targetDiaryId": null | string }
export type QuickNoteResponse = { "diaryId": null | string; "appended": boolean }
export type RegisterRequest = { "email": string; "password": string; "displayName": string; "timezone": string; "baseCurrency": string }
export type RegisterResponse = { "id": string; "email": string; "displayName": string; "timezone": string; "baseCurrency": string }
export type RelativeValue = { "assetPrice": number; "benchmarkPrice": number; "historicalRatio": number }
export type RelativeValueResponse = { "currentRatio": number; "deviationPercent": number }
export type RiskReward = { "entryPrice": number; "stopPrice": number; "targetPrice": number }
export type RiskRewardResponse = { "risk": number; "reward": number; "ratio": number }
export type Seasonality = { "returns": Array<number> }
export type SeasonalityResponse = { "observations": number | string; "averageReturn": number; "positiveRate": number }
export type SectorBreadthResponse = { "sector": string; "memberCount": number | string; "availableCount": number | string; "aboveMa20Percent": number | null; "aboveMa50Percent": number | null; "aboveMa200Percent": number | null; "status": string }
export type SessionTokens = { "accessToken": string; "expiresAt": string }
export type SharePolicy = { "shareDiaries": boolean; "shareTransactions": boolean; "sharePerformance": boolean }
export type StockPageResponse = { "stock": StockResponse; "bars": BarsResponse | null; "capabilities": { "marketData": CapabilityStatus } }
export type StockResponse = { "id": string; "symbol": string; "name": string; "exchange": string; "assetType": string; "createdAt": string }
export type StockWrite = { "symbol": null | string; "name": null | string; "exchange": null | string; "assetType": null | string }
export type SymbolsResponse = { "contractVersion": number | string; "items": Array<PublishedSymbolResponse> }
export type TimelineResponse = { "id": string; "stockId": string; "eventTime": string; "sourceType": string; "title": string; "content": string; "diaryId": null | string; "correctionOfId": null | string; "createdAt": string }
export type TimelineWrite = { "eventTime": null | string; "sourceType": null | string; "title": null | string; "content": null | string; "diaryId": null | string }
export type TransactionResponse = { "id": string; "diaryId": string; "symbol": string; "side": string; "quantity": number; "price": number; "currency": string; "tradedAt": string; "notes": string; "createdAt": string; "updatedAt": string }
export type TransactionWrite = { "symbol": string; "side": string; "quantity": number; "price": number; "currency": string; "tradedAt": string; "notes": null | string }
export type TriggerResponse = { "id": string; "tradingDate": string; "observedClose": number; "observedPrice": number; "priceType": EvaluationPrice; "triggeredAt": string; "dismissedAt": null | string }
export type UniverseCreatedResponse = { "id": string; "code": string; "name": string; "rankScope": string }
export type UniverseResponse = { "id": string; "code": string; "name": string; "rankScope": string; "createdAt": string; "updatedAt": string }
export type UniverseSymbolWrite = { "symbol": string; "label": string; "sector": null | string; "sortOrder": null | number | string }
export type UniverseWrite = { "code": string; "name": string; "rankScope": null | string }
export type WatchlistItemCreatedResponse = { "stockId": string }
export type WatchlistResponse = { "stock": StockResponse; "currentNote": null | string; "noteUpdatedAt": null | string; "timelineCount": number | string }

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

export const getApiAppPartners = (extra?: RequestInit) => request<CollectionResponseOfLink>("/api/app/partners", { method: "GET", ...extra })
export const postApiAppPartners = (body: LinkWrite, extra?: RequestInit) => request<Link>("/api/app/partners", { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppPartnersId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/partners/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppPartnersIdAccept = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/partners/${encodeURIComponent(String(id))}/accept`, { method: "POST", ...extra })
export const putApiAppPartnersIdSharePolicy = (id: string, body: SharePolicy, extra?: RequestInit) => request<unknown>(`/api/app/partners/${encodeURIComponent(String(id))}/share-policy`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppPartnersOwnerIdAuthorization = (ownerId: string, query: { "resource": string }, extra?: RequestInit) => request<AuthorizationResponse>(`/api/app/partners/${encodeURIComponent(String(ownerId))}/authorization` + withQuery(query), { method: "GET", ...extra })
export const postApiAppToolsPositionSizing = (body: PositionSizing, extra?: RequestInit) => request<PositionSizingResponse>("/api/app/tools/position-sizing", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsRiskReward = (body: RiskReward, extra?: RequestInit) => request<RiskRewardResponse>("/api/app/tools/risk-reward", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsFire = (body: Fire, extra?: RequestInit) => request<FireResponse>("/api/app/tools/fire", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsRelativeValue = (body: RelativeValue, extra?: RequestInit) => request<RelativeValueResponse>("/api/app/tools/relative-value", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppToolsSeasonality = (body: Seasonality, extra?: RequestInit) => request<SeasonalityResponse>("/api/app/tools/seasonality", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAdminPosts = (body: PostWrite, extra?: RequestInit) => request<CreatedResponse>("/api/admin/posts", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAdminPostsId = (id: string, body: PostWrite, extra?: RequestInit) => request<unknown>(`/api/admin/posts/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAdminPostsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/admin/posts/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAdminOperationsAudit = (query: { "limit"?: number | string }, extra?: RequestInit) => request<CollectionResponseOfAuditResponse>("/api/admin/operations/audit" + withQuery(query), { method: "GET", ...extra })
export const getApiAdminOperationsJobs = (extra?: RequestInit) => request<CollectionResponseOfJobResponse>("/api/admin/operations/jobs", { method: "GET", ...extra })
export const postApiAdminOperationsJobs = (body: JobWrite, extra?: RequestInit) => request<JobAcceptedResponse>("/api/admin/operations/jobs", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAdminOperationsHealth = (body: HealthWrite, extra?: RequestInit) => request<unknown>("/api/admin/operations/health", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiContentPosts = (extra?: RequestInit) => request<CollectionResponseOfPostResponse>("/api/content/posts", { method: "GET", ...extra })
export const getApiContentPostsSlug = (slug: string, extra?: RequestInit) => request<PostResponse>(`/api/content/posts/${encodeURIComponent(String(slug))}`, { method: "GET", ...extra })
export const postApiAuthRegister = (body: RegisterRequest, extra?: RequestInit) => request<RegisterResponse>("/api/auth/register", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAuthApiKeyToken = (body: ApiKeyTokenRequest, extra?: RequestInit) => request<ApiKeyTokenResponse>("/api/auth/api-key/token", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAppAgents = (body: AgentRequest, extra?: RequestInit) => request<AgentResponse>("/api/app/agents", { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppApiKeysId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/api-keys/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const putApiAppDailyPerformanceDate = (date: string, body: PerformanceWrite, extra?: RequestInit) => request<PerformanceResponse>(`/api/app/daily-performance/${encodeURIComponent(String(date))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppDisciplines = (extra?: RequestInit) => request<CollectionResponseOfDisciplineResponse>("/api/app/disciplines", { method: "GET", ...extra })
export const postApiAppDisciplines = (body: DisciplineWrite, extra?: RequestInit) => request<DisciplineResponse>("/api/app/disciplines", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppDisciplinesId = (id: string, body: DisciplineWrite, extra?: RequestInit) => request<unknown>(`/api/app/disciplines/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppDisciplinesId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/disciplines/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppDiaryAlerts = (extra?: RequestInit) => request<CollectionResponseOfDiaryAlertResponse>("/api/app/diary-alerts", { method: "GET", ...extra })
export const postApiAppDiaryAlerts = (body: DiaryAlertWrite, extra?: RequestInit) => request<DiaryAlertResponse>("/api/app/diary-alerts", { method: "POST", body: JSON.stringify(body), ...extra })
export const deleteApiAppDiaryAlertsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/diary-alerts/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppDiaryAlertsIdDismiss = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/diary-alerts/${encodeURIComponent(String(id))}/dismiss`, { method: "POST", ...extra })
export const postApiAppQuickNote = (body: QuickNote, extra?: RequestInit) => request<QuickNoteResponse>("/api/app/quick-note", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppDiaries = (extra?: RequestInit) => request<CollectionResponseOfDiaryResponse>("/api/app/diaries", { method: "GET", ...extra })
export const postApiAppDiaries = (body: DiaryWrite, extra?: RequestInit) => request<DiaryResponse>("/api/app/diaries", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppDiariesId = (id: string, extra?: RequestInit) => request<DiaryResponse>(`/api/app/diaries/${encodeURIComponent(String(id))}`, { method: "GET", ...extra })
export const putApiAppDiariesId = (id: string, body: DiaryWrite, extra?: RequestInit) => request<unknown>(`/api/app/diaries/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppDiariesId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/diaries/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const getApiAppDiariesDiaryIdTransactions = (diaryId: string, extra?: RequestInit) => request<CollectionResponseOfTransactionResponse>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions`, { method: "GET", ...extra })
export const postApiAppDiariesDiaryIdTransactions = (diaryId: string, body: TransactionWrite, extra?: RequestInit) => request<TransactionResponse>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions`, { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppDiariesDiaryIdTransactionsId = (diaryId: string, id: string, body: TransactionWrite, extra?: RequestInit) => request<unknown>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppDiariesDiaryIdTransactionsId = (diaryId: string, id: string, extra?: RequestInit) => request<unknown>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const deleteApiAppDiariesDiaryIdReview = (diaryId: string, extra?: RequestInit) => request<unknown>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/review`, { method: "DELETE", ...extra })
export const getApiAppDiariesDiaryIdReview = (diaryId: string, extra?: RequestInit) => request<DiaryReviewResponse>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/review`, { method: "GET", ...extra })
export const putApiAppDiariesDiaryIdReview = (diaryId: string, body: DiaryReviewWrite, extra?: RequestInit) => request<DiaryReviewResponse>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/review`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppStocks = (query: { "query"?: string }, extra?: RequestInit) => request<CollectionResponseOfStockResponse>("/api/app/stocks" + withQuery(query), { method: "GET", ...extra })
export const postApiAppStocks = (body: StockWrite, extra?: RequestInit) => request<StockResponse>("/api/app/stocks", { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppStocksSymbol = (symbol: string, extra?: RequestInit) => request<StockResponse>(`/api/app/stocks/${encodeURIComponent(String(symbol))}`, { method: "GET", ...extra })
export const getApiAppWatchlist = (extra?: RequestInit) => request<CollectionResponseOfWatchlistResponse>("/api/app/watchlist", { method: "GET", ...extra })
export const postApiAppWatchlistStockId = (stockId: string, extra?: RequestInit) => request<WatchlistItemCreatedResponse>(`/api/app/watchlist/${encodeURIComponent(String(stockId))}`, { method: "POST", ...extra })
export const deleteApiAppWatchlistStockId = (stockId: string, extra?: RequestInit) => request<unknown>(`/api/app/watchlist/${encodeURIComponent(String(stockId))}`, { method: "DELETE", ...extra })
export const getApiAppStocksStockIdNote = (stockId: string, extra?: RequestInit) => request<NoteResponse>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/note`, { method: "GET", ...extra })
export const putApiAppStocksStockIdNote = (stockId: string, body: NoteWrite, extra?: RequestInit) => request<NoteResponse>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/note`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const getApiAppStocksStockIdTimeline = (stockId: string, extra?: RequestInit) => request<CollectionResponseOfTimelineResponse>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/timeline`, { method: "GET", ...extra })
export const postApiAppStocksStockIdTimeline = (stockId: string, body: TimelineWrite, extra?: RequestInit) => request<TimelineResponse>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/timeline`, { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppTimelineId = (id: string, extra?: RequestInit) => request<TimelineResponse>(`/api/app/timeline/${encodeURIComponent(String(id))}`, { method: "GET", ...extra })
export const postApiAppTimelineOriginalIdCorrections = (originalId: string, body: TimelineWrite, extra?: RequestInit) => request<TimelineResponse>(`/api/app/timeline/${encodeURIComponent(String(originalId))}/corrections`, { method: "POST", body: JSON.stringify(body), ...extra })
export const getApiAppMarketSymbols = (extra?: RequestInit) => request<SymbolsResponse>("/api/app/market/symbols", { method: "GET", ...extra })
export const getApiAppMarketBarsSymbol = (symbol: string, query: { "from"?: string; "to"?: string }, extra?: RequestInit) => request<BarsResponse>(`/api/app/market/bars/${encodeURIComponent(String(symbol))}` + withQuery(query), { method: "GET", ...extra })
export const getApiAppMarketProvidersHealth = (extra?: RequestInit) => request<ProvidersHealthResponse>("/api/app/market/providers/health", { method: "GET", ...extra })
export const getApiAppPriceAlerts = (extra?: RequestInit) => request<CollectionResponseOfPriceAlertResponse>("/api/app/price-alerts", { method: "GET", ...extra })
export const postApiAppPriceAlerts = (body: PriceAlertWrite, extra?: RequestInit) => request<PriceAlertResponse>("/api/app/price-alerts", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppPriceAlertsId = (id: string, body: PriceAlertWrite, extra?: RequestInit) => request<unknown>(`/api/app/price-alerts/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppPriceAlertsId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/price-alerts/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const postApiAppPriceAlertsIdDismiss = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/dismiss`, { method: "POST", ...extra })
export const postApiAppPriceAlertsIdReactivate = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/reactivate`, { method: "POST", ...extra })
export const getApiAppPriceAlertsIdTriggers = (id: string, extra?: RequestInit) => request<CollectionResponseOfTriggerResponse>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/triggers`, { method: "GET", ...extra })
export const getApiAppRotationUniverses = (extra?: RequestInit) => request<CollectionResponseOfUniverseResponse>("/api/app/rotation/universes", { method: "GET", ...extra })
export const postApiAppRotationUniverses = (body: UniverseWrite, extra?: RequestInit) => request<UniverseCreatedResponse>("/api/app/rotation/universes", { method: "POST", body: JSON.stringify(body), ...extra })
export const putApiAppRotationUniversesId = (id: string, body: UniverseWrite, extra?: RequestInit) => request<unknown>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const deleteApiAppRotationUniversesId = (id: string, extra?: RequestInit) => request<unknown>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}`, { method: "DELETE", ...extra })
export const putApiAppRotationUniversesIdSymbols = (id: string, body: Array<UniverseSymbolWrite>, extra?: RequestInit) => request<unknown>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}/symbols`, { method: "PUT", body: JSON.stringify(body), ...extra })
export const postApiAppRotationUniversesIdCalculate = (id: string, query: { "date": string }, extra?: RequestInit) => request<CalculateResponse>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}/calculate` + withQuery(query), { method: "POST", ...extra })
export const getApiAppDiaryReviewSummary = (query: { "from": string; "to": string }, extra?: RequestInit) => request<DiaryReviewSummaryResponse>("/api/app/diary-review-summary" + withQuery(query), { method: "GET", ...extra })
export const getApiAppRotationMonitor = (query: { "universe": string; "date"?: string }, extra?: RequestInit) => request<MonitorResponse>("/api/app/rotation/monitor" + withQuery(query), { method: "GET", ...extra })
export const getApiAppBootstrap = (extra?: RequestInit) => request<AppBootstrapResponse>("/api/app/bootstrap", { method: "GET", ...extra })
export const getApiAppDashboard = (extra?: RequestInit) => request<DashboardResponse>("/api/app/dashboard", { method: "GET", ...extra })
export const getApiAppCalendar = (query: { "year": number; "month": number }, extra?: RequestInit) => request<CalendarResponse>("/api/app/calendar" + withQuery(query), { method: "GET", ...extra })
export const getApiAppStocksSymbolPage = (symbol: string, extra?: RequestInit) => request<StockPageResponse>(`/api/app/stocks/${encodeURIComponent(String(symbol))}/page`, { method: "GET", ...extra })
export const postApiAuthLogin = (body: LoginRequest, extra?: RequestInit) => request<SessionTokens>("/api/auth/login", { method: "POST", body: JSON.stringify(body), ...extra })
export const postApiAuthRefresh = (extra?: RequestInit) => request<SessionTokens>("/api/auth/refresh", { method: "POST", ...extra })
export const postApiAuthLogout = (extra?: RequestInit) => request<unknown>("/api/auth/logout", { method: "POST", ...extra })
