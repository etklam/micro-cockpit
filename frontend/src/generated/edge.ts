// Generated from contracts/openapi/edge-api.openapi.json. Do not edit.

export type JsonValue = unknown
export type WriteRequest = { [key: string]: unknown }
export type Health = { "status": string }
export type Version = { "service": string; "version": string }
export type Problem = { "error"?: string; "detail"?: string }
export type Collection = { "items": Array<JsonValue> }
export type AuthTokens = { "accessToken": string; "refreshToken": string; "expiresIn"?: number }
export type AuthWrite = { "email"?: string; "password"?: string; "refreshToken"?: string }
export type Diary = { "id"?: string; "title"?: string; "content"?: string; "localDate"?: string }
export type DiaryWrite = { "title": string; "content": string; "localDate": string }
export type QuickNoteWrite = { "content": string; "diaryId"?: string | null }
export type Transaction = { "id"?: string; "symbol"?: string; "side"?: string; "quantity"?: number; "price"?: number }
export type TransactionWrite = { "symbol": string; "side": string; "quantity": number; "price": number }
export type DailyPerformance = { "localDate"?: string; "profitLoss"?: number; "percentReturn"?: number | null }
export type DailyPerformanceWrite = { "profitLoss": number; "capitalBase"?: number | null }
export type Discipline = { "id"?: string; "text"?: string; "position"?: number }
export type DisciplineWrite = { "text": string; "ids"?: Array<string> }
export type DiaryAlert = { "id"?: string; "diaryId"?: string; "scheduledFor"?: string; "status"?: string }
export type DiaryAlertWrite = { "diaryId": string; "scheduledFor": string; "recurrence"?: string }
export type Stock = { "id"?: string; "symbol"?: string; "name"?: string }
export type StockWrite = { "symbol": string; "name"?: string }
export type PriceAlert = { "id"?: string; "symbol"?: string; "threshold"?: number; "status"?: string }
export type PriceAlertWrite = { "symbol": string; "conditionType": string; "threshold": number }
export type RotationUniverse = { "id"?: string; "name"?: string; "symbols"?: Array<string> }
export type RotationUniverseWrite = { "name": string; "symbols"?: Array<string> }
export type Partner = { "id"?: string; "status"?: string; "partnerUserId"?: string }
export type PartnerWrite = { "email"?: string; "resources"?: Array<string> }
export type Post = { "id"?: string; "slug"?: string; "title"?: string; "body"?: string }
export type PostWrite = { "slug": string; "title": string; "body"?: string; "status"?: string }
export type AuditEvent = { "id"?: string; "action"?: string; "resourceType"?: string; "occurredAt"?: string }
export type AuditWrite = { "action": string; "resourceType": string; "resourceId"?: string | null }
export type MarketBar = { "tradingDate"?: string; "open"?: number; "high"?: number; "low"?: number; "close"?: number; "volume"?: number }
export type MarketBarWrite = { "tradingDate": string; "close": number }

export type RequestOptions = { baseUrl?: string; token?: string | null; onUnauthorized?: () => void }

let options: RequestOptions = {}
export const configureClient = (next: RequestOptions) => { options = next }
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${options.baseUrl ?? ''}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}), ...init.headers } })
  if (response.status === 401) options.onUnauthorized?.()
  if (!response.ok) throw new Error(`request_failed_${response.status}`)
  return response.status === 204 ? undefined as T : response.json()
}

export const getApiAdminOperationsAudit = () => request<{ "items"?: Array<AuditEvent> }>("/api/admin/operations/audit", { method: "GET" })
export const postApiAdminOperationsAudit = (body: AuditWrite) => request<AuditEvent>("/api/admin/operations/audit", { method: "POST", body: JSON.stringify(body) })
export const postApiAdminOperationsHealth = (body: WriteRequest) => request<JsonValue>("/api/admin/operations/health", { method: "POST", body: JSON.stringify(body) })
export const getApiAdminOperationsJobs = () => request<{ "items"?: Array<JsonValue> }>("/api/admin/operations/jobs", { method: "GET" })
export const postApiAdminOperationsJobs = (body: WriteRequest) => request<JsonValue>("/api/admin/operations/jobs", { method: "POST", body: JSON.stringify(body) })
export const postApiAdminPosts = (body: PostWrite) => request<Post>("/api/admin/posts", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAdminPostsId = (id: string) => request<Post>(`/api/admin/posts/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAdminPostsId = (id: string, body: PostWrite) => request<Post>(`/api/admin/posts/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const postApiAppAgents = (body: WriteRequest) => request<JsonValue>("/api/app/agents", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppApiKeysId = (id: string) => request<JsonValue>(`/api/app/api-keys/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const getApiAppCalendar = () => request<{ "items"?: Array<JsonValue> }>("/api/app/calendar", { method: "GET" })
export const putApiAppDailyPerformanceDate = (date: string, body: DailyPerformanceWrite) => request<DailyPerformance>(`/api/app/daily-performance/${encodeURIComponent(String(date))}`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppDashboard = () => request<{ "items"?: Array<JsonValue> }>("/api/app/dashboard", { method: "GET" })
export const getApiAppDiaries = () => request<{ "items"?: Array<Diary> }>("/api/app/diaries", { method: "GET" })
export const postApiAppDiaries = (body: DiaryWrite) => request<Diary>("/api/app/diaries", { method: "POST", body: JSON.stringify(body) })
export const getApiAppDiariesDiaryIdTransactions = (diaryId: string) => request<{ "items"?: Array<Transaction> }>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions`, { method: "GET" })
export const postApiAppDiariesDiaryIdTransactions = (diaryId: string, body: TransactionWrite) => request<Transaction>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions`, { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppDiariesDiaryIdTransactionsId = (diaryId: string, id: string) => request<Transaction>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAppDiariesDiaryIdTransactionsId = (diaryId: string, id: string, body: TransactionWrite) => request<Transaction>(`/api/app/diaries/${encodeURIComponent(String(diaryId))}/transactions/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const deleteApiAppDiariesId = (id: string) => request<Diary>(`/api/app/diaries/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAppDiariesId = (id: string, body: DiaryWrite) => request<Diary>(`/api/app/diaries/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppDiaryAlerts = () => request<{ "items"?: Array<DiaryAlert> }>("/api/app/diary-alerts", { method: "GET" })
export const postApiAppDiaryAlerts = (body: DiaryAlertWrite) => request<DiaryAlert>("/api/app/diary-alerts", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppDiaryAlertsId = (id: string) => request<DiaryAlert>(`/api/app/diary-alerts/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const postApiAppDiaryAlertsIdDismiss = (id: string, body: DiaryAlertWrite) => request<DiaryAlert>(`/api/app/diary-alerts/${encodeURIComponent(String(id))}/dismiss`, { method: "POST", body: JSON.stringify(body) })
export const getApiAppDisciplines = () => request<{ "items"?: Array<Discipline> }>("/api/app/disciplines", { method: "GET" })
export const postApiAppDisciplines = (body: DisciplineWrite) => request<Discipline>("/api/app/disciplines", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppDisciplinesId = (id: string) => request<Discipline>(`/api/app/disciplines/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAppDisciplinesId = (id: string, body: DisciplineWrite) => request<Discipline>(`/api/app/disciplines/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppMarketBarsSymbol = (symbol: string) => request<MarketBar>(`/api/app/market/bars/${encodeURIComponent(String(symbol))}`, { method: "GET" })
export const getApiAppMarketProvidersHealth = () => request<{ "items"?: Array<JsonValue> }>("/api/app/market/providers/health", { method: "GET" })
export const getApiAppMarketSymbols = () => request<{ "items"?: Array<JsonValue> }>("/api/app/market/symbols", { method: "GET" })
export const getApiAppPartners = () => request<{ "items"?: Array<Partner> }>("/api/app/partners", { method: "GET" })
export const postApiAppPartners = (body: PartnerWrite) => request<Partner>("/api/app/partners", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppPartnersId = (id: string) => request<Partner>(`/api/app/partners/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const postApiAppPartnersIdAccept = (id: string, body: PartnerWrite) => request<Partner>(`/api/app/partners/${encodeURIComponent(String(id))}/accept`, { method: "POST", body: JSON.stringify(body) })
export const putApiAppPartnersIdSharePolicy = (id: string, body: PartnerWrite) => request<Partner>(`/api/app/partners/${encodeURIComponent(String(id))}/share-policy`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppPartnersOwnerIdAuthorization = (ownerId: string) => request<{ "items"?: Array<Partner> }>(`/api/app/partners/${encodeURIComponent(String(ownerId))}/authorization`, { method: "GET" })
export const getApiAppPriceAlerts = () => request<{ "items"?: Array<PriceAlert> }>("/api/app/price-alerts", { method: "GET" })
export const postApiAppPriceAlerts = (body: PriceAlertWrite) => request<PriceAlert>("/api/app/price-alerts", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppPriceAlertsId = (id: string) => request<PriceAlert>(`/api/app/price-alerts/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAppPriceAlertsId = (id: string, body: PriceAlertWrite) => request<PriceAlert>(`/api/app/price-alerts/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const postApiAppPriceAlertsIdDismiss = (id: string, body: PriceAlertWrite) => request<PriceAlert>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/dismiss`, { method: "POST", body: JSON.stringify(body) })
export const postApiAppPriceAlertsIdReactivate = (id: string, body: PriceAlertWrite) => request<PriceAlert>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/reactivate`, { method: "POST", body: JSON.stringify(body) })
export const getApiAppPriceAlertsIdTriggers = (id: string) => request<{ "items"?: Array<PriceAlert> }>(`/api/app/price-alerts/${encodeURIComponent(String(id))}/triggers`, { method: "GET" })
export const postApiAppQuickNote = (body: QuickNoteWrite) => request<Diary>("/api/app/quick-note", { method: "POST", body: JSON.stringify(body) })
export const getApiAppRotationMonitor = () => request<{ "items"?: Array<JsonValue> }>("/api/app/rotation/monitor", { method: "GET" })
export const getApiAppRotationUniverses = () => request<{ "items"?: Array<RotationUniverse> }>("/api/app/rotation/universes", { method: "GET" })
export const postApiAppRotationUniverses = (body: RotationUniverseWrite) => request<RotationUniverse>("/api/app/rotation/universes", { method: "POST", body: JSON.stringify(body) })
export const deleteApiAppRotationUniversesId = (id: string) => request<RotationUniverse>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}`, { method: "DELETE" })
export const putApiAppRotationUniversesId = (id: string, body: RotationUniverseWrite) => request<RotationUniverse>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}`, { method: "PUT", body: JSON.stringify(body) })
export const postApiAppRotationUniversesIdCalculate = (id: string, body: RotationUniverseWrite) => request<RotationUniverse>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}/calculate`, { method: "POST", body: JSON.stringify(body) })
export const putApiAppRotationUniversesIdSymbols = (id: string, body: RotationUniverseWrite) => request<RotationUniverse>(`/api/app/rotation/universes/${encodeURIComponent(String(id))}/symbols`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppStocks = () => request<{ "items"?: Array<Stock> }>("/api/app/stocks", { method: "GET" })
export const postApiAppStocks = (body: StockWrite) => request<Stock>("/api/app/stocks", { method: "POST", body: JSON.stringify(body) })
export const getApiAppStocksStockIdNote = (stockId: string) => request<{ "items"?: Array<Stock> }>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/note`, { method: "GET" })
export const putApiAppStocksStockIdNote = (stockId: string, body: StockWrite) => request<Stock>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/note`, { method: "PUT", body: JSON.stringify(body) })
export const getApiAppStocksStockIdTimeline = (stockId: string) => request<{ "items"?: Array<Stock> }>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/timeline`, { method: "GET" })
export const postApiAppStocksStockIdTimeline = (stockId: string, body: StockWrite) => request<Stock>(`/api/app/stocks/${encodeURIComponent(String(stockId))}/timeline`, { method: "POST", body: JSON.stringify(body) })
export const getApiAppStocksSymbol = (symbol: string) => request<Stock>(`/api/app/stocks/${encodeURIComponent(String(symbol))}`, { method: "GET" })
export const getApiAppStocksSymbolPage = (symbol: string) => request<{ "items"?: Array<Stock> }>(`/api/app/stocks/${encodeURIComponent(String(symbol))}/page`, { method: "GET" })
export const getApiAppTimelineId = (id: string) => request<JsonValue>(`/api/app/timeline/${encodeURIComponent(String(id))}`, { method: "GET" })
export const postApiAppTimelineOriginalIdCorrections = (originalId: string, body: WriteRequest) => request<JsonValue>(`/api/app/timeline/${encodeURIComponent(String(originalId))}/corrections`, { method: "POST", body: JSON.stringify(body) })
export const postApiAppToolsFire = (body: WriteRequest) => request<JsonValue>("/api/app/tools/fire", { method: "POST", body: JSON.stringify(body) })
export const postApiAppToolsPositionSizing = (body: WriteRequest) => request<JsonValue>("/api/app/tools/position-sizing", { method: "POST", body: JSON.stringify(body) })
export const postApiAppToolsRelativeValue = (body: WriteRequest) => request<JsonValue>("/api/app/tools/relative-value", { method: "POST", body: JSON.stringify(body) })
export const postApiAppToolsRiskReward = (body: WriteRequest) => request<JsonValue>("/api/app/tools/risk-reward", { method: "POST", body: JSON.stringify(body) })
export const postApiAppToolsSeasonality = (body: WriteRequest) => request<JsonValue>("/api/app/tools/seasonality", { method: "POST", body: JSON.stringify(body) })
export const getApiAppWatchlist = () => request<{ "items"?: Array<Stock> }>("/api/app/watchlist", { method: "GET" })
export const deleteApiAppWatchlistStockId = (stockId: string) => request<Stock>(`/api/app/watchlist/${encodeURIComponent(String(stockId))}`, { method: "DELETE" })
export const postApiAppWatchlistStockId = (stockId: string, body: StockWrite) => request<Stock>(`/api/app/watchlist/${encodeURIComponent(String(stockId))}`, { method: "POST", body: JSON.stringify(body) })
export const postApiAuthApiKeyToken = (body: AuthWrite) => request<AuthTokens>("/api/auth/api-key/token", { method: "POST", body: JSON.stringify(body) })
export const postApiAuthLogin = (body: AuthWrite) => request<AuthTokens>("/api/auth/login", { method: "POST", body: JSON.stringify(body) })
export const postApiAuthLogout = (body: AuthWrite) => request<AuthTokens>("/api/auth/logout", { method: "POST", body: JSON.stringify(body) })
export const postApiAuthRefresh = (body: AuthWrite) => request<AuthTokens>("/api/auth/refresh", { method: "POST", body: JSON.stringify(body) })
export const postApiAuthRegister = (body: AuthWrite) => request<AuthTokens>("/api/auth/register", { method: "POST", body: JSON.stringify(body) })
export const getApiContentPosts = () => request<{ "items"?: Array<Post> }>("/api/content/posts", { method: "GET" })
export const getApiContentPostsSlug = (slug: string) => request<Post>(`/api/content/posts/${encodeURIComponent(String(slug))}`, { method: "GET" })
export const getHealthLive = () => request<{ "items"?: Array<Health> }>("/health/live", { method: "GET" })
export const getHealthReady = () => request<{ "items"?: Array<Health> }>("/health/ready", { method: "GET" })
export const getVersion = () => request<{ "items"?: Array<Version> }>("/version", { method: "GET" })
