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
