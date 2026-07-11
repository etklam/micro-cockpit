// Relative by default: same-origin /api is proxied by Vite in dev and by nginx
// in Compose. Set VITE_API_URL to target a different Edge host.
const base = import.meta.env.VITE_API_URL ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = localStorage.getItem('accessToken')
  const response = await fetch(`${base}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers } })
  if (response.status === 401) { localStorage.removeItem('accessToken'); location.reload(); throw new Error('unauthorized') }
  if (!response.ok) throw new Error(`request_failed_${response.status}`)
  return response.status === 204 ? undefined as T : response.json()
}
async function requestOptional<T>(path: string): Promise<T | null> {
  try { return await request<T>(path) } catch (error) { if (String(error).includes('request_failed_404')) return null; throw error }
}

export type Diary = { id: string; localDate: string; title: string; content: string; createdAt: string; updatedAt: string }
export type Transaction = { id: string; diaryId: string; symbol: string; side: string; quantity: number; price: number; currency: string; tradedAt: string; notes: string }
export type Discipline = { id: string; content: string; position: number }
export type Alert = { id: string; diaryId: string; startLocalDate: string; nextLocalDate: string | null; localTime: string; timezone: string; repeatMode: string; status: string }
export type CalendarDay = { date: string; performance: null | { pnlAmount: number; pnlPercent: number | null; note?: string }; diaryCount: number; transactionCount: number; alertCount: number | null }
export type Calendar = { year: number; month: number; summary: { totalPnl: number; tradingDays: number; winningDays: number; losingDays: number }; days: CalendarDay[] }
export type Capability = 'available' | 'unavailable' | 'empty'
export type Dashboard = {
  localDate: string
  diary: { writtenToday: boolean; count: number }
  performance: null | { pnlAmount: number; pnlPercent: number | null; note?: string }
  pendingAlerts: number | null
  discipline: null | { content: string }
  recentDiaries: Diary[]
  capabilities?: { alerts: Capability; discipline: Capability }
}
export type Stock = { id: string; symbol: string; name: string; exchange?: string; assetType?: string }
export type WatchlistItem = { stock: Stock; note: string | null; noteUpdatedAt: string | null; timelineCount: number }
export type ResearchNote = { stockId: string; content: string; createdAt: string; updatedAt: string }
export type TimelineEvent = { id: string; eventTime: string; sourceType: string; title: string; content?: string }
export type PriceAlert = { id: string; symbol: string; threshold: number; conditionType: string; status: string; createdAt?: string }
export type RotationItem = { symbol: string; label: string; return2w: number | null; rank2w: number | null; status?: string }
export type Partner = { id: string; requesterUserId: string; partnerUserId: string; partnerType: string; status: string; createdAt: string }
export type Article = { id: string; slug: string; title: string; body: string; publishedAt?: string }

export async function login(email: string, password: string) { const result = await request<{ accessToken: string }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }); localStorage.setItem('accessToken', result.accessToken) }
export const getDashboard = () => request<Dashboard>('/api/app/dashboard')
export const saveQuickNote = (localDate: string, content: string) => request('/api/app/quick-note', { method: 'POST', body: JSON.stringify({ localDate, content }) })
export const getDiaries = () => request<{ items: Diary[] }>('/api/app/diaries')
export const createDiary = (localDate: string, title: string, content: string) => request<Diary>('/api/app/diaries', { method: 'POST', body: JSON.stringify({ localDate, title, content }) })
export const updateDiary = (id: string, localDate: string, title: string, content: string) => request<void>(`/api/app/diaries/${id}`, { method: 'PUT', body: JSON.stringify({ localDate, title, content }) })
export const deleteDiary = (id: string) => request<void>(`/api/app/diaries/${id}`, { method: 'DELETE' })
export const getTransactions = (diaryId: string) => request<{ items: Transaction[] }>(`/api/app/diaries/${diaryId}/transactions`)
export const createTransaction = (diaryId: string, body: Omit<Transaction, 'id' | 'diaryId'>) => request<Transaction>(`/api/app/diaries/${diaryId}/transactions`, { method: 'POST', body: JSON.stringify(body) })
export const deleteTransaction = (diaryId: string, id: string) => request<void>(`/api/app/diaries/${diaryId}/transactions/${id}`, { method: 'DELETE' })
export const getCalendar = (year: number, month: number) => request<Calendar>(`/api/app/calendar?year=${year}&month=${month}`)
export const savePerformance = (date: string, pnlAmount: number, capitalBase: number | null, note: string) => request(`/api/app/daily-performance/${date}`, { method: 'PUT', body: JSON.stringify({ pnlAmount, capitalBase, note }) })
export const getDisciplines = () => request<{ items: Discipline[] }>('/api/app/disciplines')
export const createDiscipline = (content: string) => request<Discipline>('/api/app/disciplines', { method: 'POST', body: JSON.stringify({ content }) })
export const deleteDiscipline = (id: string) => request<void>(`/api/app/disciplines/${id}`, { method: 'DELETE' })
export const getAlerts = () => request<{ items: Alert[] }>('/api/app/diary-alerts')
export const createAlert = (body: { diaryId: string; startLocalDate: string; localTime: string; timezone: string; repeatMode: string }) => request<Alert>('/api/app/diary-alerts', { method: 'POST', body: JSON.stringify(body) })
export const dismissAlert = (id: string) => request<void>(`/api/app/diary-alerts/${id}/dismiss`, { method: 'POST' })
export const deleteAlert = (id: string) => request<void>(`/api/app/diary-alerts/${id}`, { method: 'DELETE' })
export const getWatchlist = () => request<{ items: WatchlistItem[] }>('/api/app/watchlist')
export async function addWatchlist(symbol: string) { const stock = await request<Stock>(`/api/app/stocks/${encodeURIComponent(symbol)}`); return request<void>(`/api/app/watchlist/${stock.id}`, { method: 'POST' }) }
export const removeWatchlist = (id: string) => request<void>(`/api/app/watchlist/${id}`, { method: 'DELETE' })
export const getResearchNote = (id: string) => requestOptional<ResearchNote>(`/api/app/stocks/${id}/note`)
export const saveResearchNote = (id: string, content: string) => request<ResearchNote>(`/api/app/stocks/${id}/note`, { method: 'PUT', body: JSON.stringify({ content }) })
export const getResearchTimeline = (id: string) => request<{ items: TimelineEvent[] }>(`/api/app/stocks/${id}/timeline`)
export const getPriceAlerts = () => request<{ items: PriceAlert[] }>('/api/app/price-alerts')
export const addPriceAlert = (symbol: string, threshold: number, conditionType: string) => request<PriceAlert>('/api/app/price-alerts', { method: 'POST', body: JSON.stringify({ symbol, threshold, conditionType }) })
export const deletePriceAlert = (id: string) => request<void>(`/api/app/price-alerts/${id}`, { method: 'DELETE' })
export async function getMarketRotation() { const universes = await request<{ items: { code: string }[] }>('/api/app/rotation/universes'); if (!universes.items.length) return { snapshotDate: null as string | null, etfs: [] as RotationItem[] }; return request<{ snapshotDate: string | null; etfs: RotationItem[] }>(`/api/app/rotation/monitor?universe=${encodeURIComponent(universes.items[0].code)}`) }
export const getPartners = () => request<{ items: Partner[] }>('/api/app/partners')
export const getArticles = () => request<{ items: Article[] }>('/api/content/posts')
export const calculate = (tool: string, values: Record<string, unknown>) => request<Record<string, number>>(`/api/app/tools/${tool}`, { method: 'POST', body: JSON.stringify(values) })
export const createAgent = (name: string) => request<{ userId: string; keyId: string; apiKey: string }>('/api/app/agents', { method: 'POST', body: JSON.stringify({ name, displayName: name, timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', baseCurrency: 'USD', scopes: ['research:read'] }) })
