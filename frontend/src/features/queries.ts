import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as session from '../api'
import * as api from './api'
import { isAppearance, reconcileAccent, reconcileAppearance } from './appearance'
import { queueSettingsWrite } from './settingsWrites'
import { isLocale, reconcileLocale } from '../i18n'

export type DiaryListKeyFilters = {
  q: string
  from: string
  to: string
  review: string
  symbol: string
  tag: string
}

export const queryKeys = {
  bootstrap: ['bootstrap'] as const,
  settings: ['settings'] as const,
  dashboard: ['dashboard'] as const,
  diaries: ['diaries'] as const,
  diariesList: (filters: DiaryListKeyFilters) => ['diaries', 'list', filters.q, filters.from, filters.to, filters.review, filters.symbol, filters.tag] as const,
  diary: (id: string) => ['diary', id] as const,
  transactions: (id: string) => ['transactions', id] as const,
  calendar: (year: number, month: number) => ['calendar', year, month] as const,
  disciplines: ['disciplines'] as const,
  alerts: ['alerts'] as const,
  watchlist: ['watchlist'] as const,
  researchNote: (id: string) => ['research-note', id] as const,
  researchTimeline: (id: string) => ['research-timeline', id] as const,
  priceAlerts: {
    list: ['price-alerts', 'list'] as const,
    triggers: (id: string) => ['price-alerts', 'triggers', id] as const,
  },
  rotation: {
    universes: ['rotation', 'universes'] as const,
    monitor: (universe: string, scope: string) => ['rotation', 'monitor', universe, scope] as const,
  },
  partners: ['partners'] as const,
  partnerInvitations: ['partners', 'invitations'] as const,
  partnerCompare: (linkId: string, from: string, to: string) => ['partners', 'compare', linkId, from, to] as const,
  articles: ['articles'] as const,
  article: (slug: string) => ['article', slug] as const,
  diaryReview: {
    detail: (diaryId: string) => ['diary-review', 'detail', diaryId] as const,
    summary: (from: string, to: string) => ['diary-review', 'summary', from, to] as const,
    summaries: ['diary-review', 'summary'] as const,
    itemsPrefix: ['diary-review', 'items'] as const,
    items: (from: string, to: string, status: api.DiaryReviewFilterStatus, assessment: api.DiaryReviewAssessmentFilter, tag: string, cursor: string) =>
      ['diary-review', 'items', from, to, status, assessment, tag, cursor] as const,
  },
}

export const useBootstrapQuery = (enabled = true) => useQuery({ queryKey: queryKeys.bootstrap, queryFn: api.getBootstrap, staleTime: 60_000, enabled })
export const useSettingsQuery = () => useQuery({ queryKey: queryKeys.settings, queryFn: api.getSettings })
export const useDashboardQuery = () => useQuery({ queryKey: queryKeys.dashboard, queryFn: api.getDashboard })
export const useDiariesInfiniteQuery = (filters: DiaryListKeyFilters) => useInfiniteQuery({
  queryKey: queryKeys.diariesList(filters),
  initialPageParam: undefined as string | undefined,
  queryFn: ({ pageParam }) => api.getDiaries({
    query: filters.q || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
    reviewStatus: (filters.review as api.DiaryListQuery['reviewStatus']) || 'all',
    symbol: filters.symbol || undefined,
    tag: filters.tag || undefined,
    cursor: pageParam,
    limit: 20,
  }),
  getNextPageParam: (last) => last.nextCursor ?? undefined,
})
/** Searchable diary picker (alerts). Server-side page; not an archive dump. */
export const useDiaryPickerQuery = (q: string) => useQuery({
  queryKey: ['diaries', 'picker', q] as const,
  queryFn: () => api.getDiaries({ query: q || undefined, limit: 20 }),
})
// Compact list for pickers that still need a simple list. Prefer useDiaryPickerQuery for large accounts.
export const useDiariesQuery = () => useQuery({
  queryKey: queryKeys.diariesList({ q: '', from: '', to: '', review: 'all', symbol: '', tag: '' }),
  queryFn: () => api.getDiaries({ limit: 100 }),
})
export const useDiaryQuery = (id: string) => useQuery({ queryKey: queryKeys.diary(id), queryFn: () => api.getDiary(id), enabled: !!id })
export const useTransactionsQuery = (id: string) => useQuery({ queryKey: queryKeys.transactions(id), queryFn: () => api.getTransactions(id), enabled: !!id })
export const useCalendarQuery = (year: number, month: number) => useQuery({ queryKey: queryKeys.calendar(year, month), queryFn: () => api.getCalendar(year, month) })
export const useDisciplinesQuery = () => useQuery({ queryKey: queryKeys.disciplines, queryFn: api.getDisciplines })
export const useAlertsQuery = () => useQuery({ queryKey: queryKeys.alerts, queryFn: api.getAlerts })
export const useWatchlistQuery = () => useQuery({ queryKey: queryKeys.watchlist, queryFn: api.getWatchlist })
export const useResearchNoteQuery = (id: string) => useQuery({ queryKey: queryKeys.researchNote(id), queryFn: () => api.getResearchNote(id), enabled: !!id })
export const useResearchTimelineQuery = (id: string) => useQuery({ queryKey: queryKeys.researchTimeline(id), queryFn: () => api.getResearchTimeline(id), enabled: !!id })
export const usePriceAlertsQuery = () => useQuery({ queryKey: queryKeys.priceAlerts.list, queryFn: api.getPriceAlerts })
export const usePriceAlertTriggersQuery = (id: string | null) => useQuery({ queryKey: queryKeys.priceAlerts.triggers(id ?? ''), queryFn: () => api.getPriceAlertTriggers(id!), enabled: !!id })
export const useRotationUniversesQuery = () => useQuery({ queryKey: queryKeys.rotation.universes, queryFn: api.getRotationUniverses, staleTime: 60_000 })
export const useRotationQuery = (universe: string, scope: string) => useQuery({
  queryKey: queryKeys.rotation.monitor(universe, scope),
  queryFn: () => api.getMarketRotation(universe),
  enabled: !!universe && !!scope,
  placeholderData: previous => previous,
})
export const usePartnersQuery = () => useQuery({ queryKey: queryKeys.partners, queryFn: api.getPartners })
export const usePartnerInvitationsQuery = () => useQuery({ queryKey: queryKeys.partnerInvitations, queryFn: api.getPartnerInvitations })
export const usePartnerCompareQuery = (linkId: string, from: string, to: string) => useQuery({
  queryKey: queryKeys.partnerCompare(linkId, from, to),
  queryFn: () => api.getPartnerCompare(linkId, from || undefined, to || undefined),
  enabled: !!linkId && !!from && !!to,
})
export function useCreatePartnerInvitationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: api.createPartnerInvitation,
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.partnerInvitations }) },
  })
}
export function useRevokePartnerInvitationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.revokePartnerInvitation(id),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.partnerInvitations }) },
  })
}
export function useRedeemPartnerInvitationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (code: string) => api.redeemPartnerInvitation(code),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.partners }),
        client.invalidateQueries({ queryKey: queryKeys.partnerInvitations }),
      ])
    },
  })
}
export function useAcceptPartnerMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.acceptPartner(id),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.partners }) },
  })
}
export function useRevokePartnerMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.revokePartner(id),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.partners }) },
  })
}
export function usePartnerShareDiariesMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, shareDiaries }: { id: string; shareDiaries: boolean }) => api.setPartnerShareDiaries(id, shareDiaries),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.partners }) },
  })
}
export const useArticlesQuery = () => useQuery({ queryKey: queryKeys.articles, queryFn: api.getArticles })
export const useArticleQuery = (slug: string) => useQuery({ queryKey: queryKeys.article(slug), queryFn: () => api.getArticle(slug), enabled: !!slug })
export const useDiaryReviewQuery = (diaryId: string) => useQuery({ queryKey: queryKeys.diaryReview.detail(diaryId), queryFn: () => api.getDiaryReview(diaryId), enabled: !!diaryId })
export const useDiaryReviewSummaryQuery = (from: string, to: string) => useQuery({ queryKey: queryKeys.diaryReview.summary(from, to), queryFn: () => api.getDiaryReviewSummary(from, to), enabled: !!from && !!to })
export const useDiaryReviewItemsQuery = (from: string, to: string, status: api.DiaryReviewFilterStatus, assessment: api.DiaryReviewAssessmentFilter, tag: string, cursor: string) => useQuery({
  queryKey: queryKeys.diaryReview.items(from, to, status, assessment, tag, cursor),
  queryFn: () => api.getDiaryReviewItems(from, to, status, assessment, tag || undefined, cursor || undefined),
  enabled: !!from && !!to,
})

const calendarPrefix = ['calendar'] as const
const invalidateCalendar = (client: ReturnType<typeof useQueryClient>) => client.invalidateQueries({ queryKey: calendarPrefix })

export function useQuickNoteMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: ({ date, content, key }: { date: string; content: string; key: string }) => api.saveQuickNote(date, content, key), onSuccess: async () => {
    await Promise.all([client.invalidateQueries({ queryKey: queryKeys.diaries }), client.invalidateQueries({ queryKey: queryKeys.dashboard }), invalidateCalendar(client)])
  } })
}

export function useSaveDiaryMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: ({ id, date, title, content, tags, key }: { id?: string; date: string; title: string; content: string; tags: string[]; key: string }) => id ? api.updateDiary(id, date, title, content, tags) : api.createDiary(date, title, content, tags, key), onSuccess: async (result, vars) => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
      client.invalidateQueries({ queryKey: queryKeys.dashboard }),
      invalidateCalendar(client),
      vars.id ? client.invalidateQueries({ queryKey: queryKeys.diary(vars.id) }) : Promise.resolve(),
    ])
    return result
  } })
}

export function useDeleteDiaryMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.deleteDiary, onSuccess: async (_, id) => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
      client.invalidateQueries({ queryKey: queryKeys.dashboard }),
      invalidateCalendar(client),
      client.invalidateQueries({ queryKey: queryKeys.diary(id) }),
    ])
  } })
}

export function useCreateTransactionMutation(diaryId: string) {
  const client = useQueryClient()
  return useMutation({ mutationFn: ({ body, key }: { body: Parameters<typeof api.createTransaction>[1]; key: string }) => api.createTransaction(diaryId, body, key), onSuccess: async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.transactions(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
      invalidateCalendar(client),
    ])
  } })
}

export function useUpdateTransactionMutation(diaryId: string) {
  const client = useQueryClient()
  return useMutation({ mutationFn: ({ id, body }: { id: string; body: Parameters<typeof api.updateTransaction>[2] }) => api.updateTransaction(diaryId, id, body), onSuccess: async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.transactions(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
      invalidateCalendar(client),
    ])
  } })
}

export function useDeleteTransactionMutation(diaryId: string) {
  const client = useQueryClient()
  return useMutation({ mutationFn: (id: string) => api.deleteTransaction(diaryId, id), onSuccess: async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.transactions(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
      invalidateCalendar(client),
    ])
  } })
}

export function useSavePerformanceMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: ({ date, amount, capital, note }: { date: string; amount: number; capital: number | null; note: string }) => api.savePerformance(date, amount, capital, note), onSuccess: async () => {
    await Promise.all([client.invalidateQueries({ queryKey: queryKeys.dashboard }), invalidateCalendar(client)])
  } })
}

function useInvalidatingMutation<T>(operation: (value: T) => Promise<unknown>, keys: readonly (readonly unknown[])[]) {
  const client = useQueryClient()
  return useMutation({ mutationFn: operation, onSuccess: async () => { await Promise.all(keys.map(queryKey => client.invalidateQueries({ queryKey }))) } })
}

export const useCreateDisciplineMutation = () => useInvalidatingMutation(api.createDiscipline, [queryKeys.disciplines, queryKeys.dashboard])
export const useDeleteDisciplineMutation = () => useInvalidatingMutation(api.deleteDiscipline, [queryKeys.disciplines, queryKeys.dashboard])
export const useCreateAlertMutation = () => useInvalidatingMutation(api.createAlert, [queryKeys.alerts, queryKeys.dashboard, calendarPrefix])
export const useDismissAlertMutation = () => useInvalidatingMutation(api.dismissAlert, [queryKeys.alerts, queryKeys.dashboard, calendarPrefix])
export const useDeleteAlertMutation = () => useInvalidatingMutation(api.deleteAlert, [queryKeys.alerts, queryKeys.dashboard, calendarPrefix])
export const useAddWatchlistMutation = () => useInvalidatingMutation(api.addWatchlist, [queryKeys.watchlist])
export const useRemoveWatchlistMutation = () => useInvalidatingMutation(api.removeWatchlist, [queryKeys.watchlist])
export const useSaveResearchNoteMutation = (id: string) => useInvalidatingMutation((content: string) => api.saveResearchNote(id, content), [queryKeys.researchNote(id), queryKeys.researchTimeline(id), queryKeys.watchlist])
export const useAddPriceAlertMutation = () => useInvalidatingMutation(({ symbol, threshold, condition, evaluationPrice }: { symbol: string; threshold: number; condition: string; evaluationPrice: api.PriceAlertEvaluationPrice }) => api.addPriceAlert(symbol, threshold, condition, evaluationPrice), [queryKeys.priceAlerts.list])
export function useDeletePriceAlertMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.deletePriceAlert, onSuccess: async (_, id) => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.priceAlerts.list }),
      client.invalidateQueries({ queryKey: queryKeys.priceAlerts.triggers(id) }),
    ])
  } })
}
function usePriceAlertLifecycleMutation(operation: (id: string) => Promise<unknown>) {
  const client = useQueryClient()
  return useMutation({ mutationFn: operation, onSuccess: async (_, id) => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.priceAlerts.list }),
      client.invalidateQueries({ queryKey: queryKeys.priceAlerts.triggers(id) }),
    ])
  } })
}
export const useDismissPriceAlertMutation = () => usePriceAlertLifecycleMutation(api.dismissPriceAlert)
export const useReactivatePriceAlertMutation = () => usePriceAlertLifecycleMutation(api.reactivatePriceAlert)
export const useCreateAgentMutation = () => useMutation({ mutationFn: api.createAgent })

export function useSaveDiaryReviewMutation(diaryId: string) {
  const client = useQueryClient()
  return useMutation({ mutationFn: (body: api.DiaryReviewWrite) => api.saveDiaryReview(diaryId, body), onSuccess: async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.detail(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.summaries }),
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.itemsPrefix }),
      client.invalidateQueries({ queryKey: queryKeys.diary(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
    ])
  } })
}

export function useDeleteDiaryReviewMutation(diaryId: string) {
  const client = useQueryClient()
  return useMutation({ mutationFn: () => api.deleteDiaryReview(diaryId), onSuccess: async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.detail(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.summaries }),
      client.invalidateQueries({ queryKey: queryKeys.diaryReview.itemsPrefix }),
      client.invalidateQueries({ queryKey: queryKeys.diary(diaryId) }),
      client.invalidateQueries({ queryKey: queryKeys.diaries }),
    ])
  } })
}

export type SaveSettingsResult =
  | { status: 'ok'; settings: api.UserSettings }
  | { status: 'saved_session_stale'; settings: api.UserSettings; message: string }

export function useSaveSettingsMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async (body: api.UserSettingsWrite): Promise<SaveSettingsResult> => {
      const settings = await queueSettingsWrite(body, client)
      if (!settings) throw new Error('settings_write_skipped')
      const refreshed = await session.refreshSession()
      if (!refreshed) {
        // Settings persisted in Identity; access token claims may be stale. Clear the
        // in-memory token without a second refresh-rotation attempt.
        session.clearAccessToken()
        return {
          status: 'saved_session_stale',
          settings,
          message: 'Settings were saved, but the session could not be refreshed. Sign in again to apply timezone and currency.',
        }
      }
      if (isAppearance(settings.appearance)) reconcileAppearance(settings.appearance)
      reconcileAccent(settings.accentTheme)
      if (isLocale(settings.locale)) reconcileLocale(settings.locale)
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.settings }),
        client.invalidateQueries({ queryKey: queryKeys.bootstrap }),
        client.invalidateQueries({ queryKey: queryKeys.dashboard }),
        client.invalidateQueries({ queryKey: calendarPrefix }),
        client.invalidateQueries({ queryKey: queryKeys.diaries }),
        client.invalidateQueries({ queryKey: queryKeys.diaryReview.summaries }),
        client.invalidateQueries({ queryKey: queryKeys.alerts }),
      ])
      await client.refetchQueries({ queryKey: queryKeys.bootstrap })
      return { status: 'ok', settings }
    },
  })
}
