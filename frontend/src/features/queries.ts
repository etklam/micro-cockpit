import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as session from '../api'
import * as api from './api'
import { isAppearance, reconcileAccent, reconcileAppearance } from './appearance'
import { queueSettingsWrite } from './settingsWrites'
import { isLocale, reconcileLocale } from '../i18n'

export const queryKeys = {
  bootstrap: ['bootstrap'] as const,
  settings: ['settings'] as const,
  agents: ['agents'] as const,
  accessGrants: ['access-grants'] as const,
  todayObservation: ['market-observations', 'today'] as const,
  expectations: ['expectations'] as const,
  expectationReview: (id: string) => ['expectations', id, 'review'] as const,
  reasoningLabels: ['reasoning-labels'] as const,
  actionDecisions: (updateId: string) => ['action-decisions', updateId] as const,
  tradeEvidence: (decisionId: string) => ['action-decisions', decisionId, 'trades'] as const,
  instrumentDirectory: ['market', 'instruments'] as const,
  observationHistory: (filters: api.ObservationSearchFilters) =>
    ['market-observations', 'history', filters.query ?? '', filters.from ?? '', filters.to ?? '',
      filters.subjectType ?? '', filters.subject ?? '', filters.instrumentId ?? '', filters.market ?? '',
      filters.symbol ?? '', filters.tag ?? '', filters.author ?? ''] as const,
  calendar: (year: number, month: number) => ['calendar', year, month] as const,
  disciplines: ['disciplines'] as const,
  todayDiscipline: ['discipline-principles', 'today'] as const,
  patternReview: (range: string, from: string, to: string) => ['pattern-review', range, from, to] as const,
  comparison: (query: api.ComparisonQuery | null) => ['comparison', query] as const,
  watchlist: ['watchlist'] as const,
}

export const useBootstrapQuery = (enabled = true) =>
  useQuery({ queryKey: queryKeys.bootstrap, queryFn: api.getBootstrap, staleTime: 60_000, enabled })
export const useSettingsQuery = () => useQuery({ queryKey: queryKeys.settings, queryFn: api.getSettings })
export const useAgentsQuery = () => useQuery({ queryKey: queryKeys.agents, queryFn: api.getAgents })
export const useAccessGrantsQuery = () => useQuery({ queryKey: queryKeys.accessGrants, queryFn: api.getAccessGrants })
export const useTodayObservationQuery = () =>
  useQuery({ queryKey: queryKeys.todayObservation, queryFn: api.getTodayMarketObservation, refetchInterval: 60_000 })
export const useExpectationsQuery = () => useQuery({ queryKey: queryKeys.expectations, queryFn: api.getExpectations })
export const useExpectationReviewQuery = (id: string) =>
  useQuery({ queryKey: queryKeys.expectationReview(id), queryFn: () => api.getExpectationReview(id), enabled: !!id })
export const useReasoningLabelsQuery = () =>
  useQuery({ queryKey: queryKeys.reasoningLabels, queryFn: api.getReasoningLabels })
export const useActionDecisionsQuery = (updateId: string) =>
  useQuery({ queryKey: queryKeys.actionDecisions(updateId), queryFn: () => api.getActionDecisions(updateId), enabled: !!updateId })
export const useTradeEvidenceQuery = (decisionId: string) =>
  useQuery({ queryKey: queryKeys.tradeEvidence(decisionId), queryFn: () => api.getTradeEvidence(decisionId), enabled: !!decisionId })
export const useInstrumentDirectoryQuery = () =>
  useQuery({ queryKey: queryKeys.instrumentDirectory, queryFn: api.getInstrumentDirectory, staleTime: 300_000 })
export const useObservationHistoryQuery = (filters: api.ObservationSearchFilters) => useInfiniteQuery({
  queryKey: queryKeys.observationHistory(filters),
  initialPageParam: undefined as string | undefined,
  queryFn: ({ pageParam }) => api.getObservationHistory(filters, pageParam),
  getNextPageParam: page => page.nextCursor ?? undefined,
})
export const useCalendarQuery = (year: number, month: number) =>
  useQuery({ queryKey: queryKeys.calendar(year, month), queryFn: () => api.getCalendar(year, month) })
export const useDisciplinesQuery = () => useQuery({ queryKey: queryKeys.disciplines, queryFn: api.getDisciplines })
export const useTodayDisciplineQuery = () =>
  useQuery({ queryKey: queryKeys.todayDiscipline, queryFn: api.getTodayDiscipline })
export const usePatternReviewQuery = (range: 'weekly' | 'monthly' | 'custom', from = '', to = '') => useQuery({
  queryKey: queryKeys.patternReview(range, from, to),
  queryFn: () => api.getPatternReview(range, from || undefined, to || undefined),
  enabled: range !== 'custom' || (!!from && !!to && from <= to),
})
export const useComparisonQuery = (query: api.ComparisonQuery | null) => useQuery({
  queryKey: queryKeys.comparison(query),
  queryFn: () => api.getComparison(query!),
  enabled: query !== null,
})
export const useWatchlistQuery = () => useQuery({ queryKey: queryKeys.watchlist, queryFn: api.getWatchlist })

const calendarPrefix = ['calendar'] as const
const invalidateCalendar = (client: ReturnType<typeof useQueryClient>) =>
  client.invalidateQueries({ queryKey: calendarPrefix })

export function useCreateExpectationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ updateId, body, key }: { updateId: string; body: api.ExpectationWrite; key: string }) =>
      api.createExpectation(updateId, body, key),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.expectations }),
        client.invalidateQueries({ queryKey: queryKeys.todayObservation }),
        invalidateCalendar(client),
      ])
    },
  })
}

function useInvalidatingMutation<T>(operation: (value: T) => Promise<unknown>, keys: readonly (readonly unknown[])[]) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: operation,
    onSuccess: async () => { await Promise.all(keys.map(queryKey => client.invalidateQueries({ queryKey }))) },
  })
}

export const useUpdateExpectationMutation = () => useInvalidatingMutation(
  ({ id, body }: { id: string; body: api.ExpectationWrite }) => api.updateExpectation(id, body),
  [queryKeys.expectations, calendarPrefix],
)
export const useInvalidateExpectationMutation = () =>
  useInvalidatingMutation(api.invalidateExpectation, [queryKeys.expectations, calendarPrefix])
export function useSaveExpectationReviewMutation(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: api.ExpectationReviewWrite) => api.saveExpectationReview(id, body),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: queryKeys.expectationReview(id) }),
        client.invalidateQueries({ queryKey: queryKeys.expectations }),
        invalidateCalendar(client),
      ])
    },
  })
}
export function useCreateReasoningLabelMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, name }: { kind: api.ReasoningLabelKind; name: string }) => api.createReasoningLabel(kind, name),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.reasoningLabels }) },
  })
}
export function useCreateActionDecisionMutation(updateId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: api.ActionDecisionWrite) => api.createActionDecision(updateId, body),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.actionDecisions(updateId) }) },
  })
}
export function useUpdateActionDecisionMutation(updateId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: api.ActionDecisionWrite }) => api.updateActionDecision(id, body),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.actionDecisions(updateId) }) },
  })
}
export function useDeleteActionDecisionMutation(updateId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: api.deleteActionDecision,
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.actionDecisions(updateId) }) },
  })
}
export function useCreateTradeEvidenceMutation(decisionId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: api.TradeEvidenceWrite) => api.createTradeEvidence(decisionId, body),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.tradeEvidence(decisionId) }) },
  })
}
export function useQuickObservationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ content, key, sourceLabel }: { content: string; key: string; sourceLabel?: string }) => api.saveQuickObservation(content, key, sourceLabel),
    onMutate: async ({ content }) => {
      await client.cancelQueries({ queryKey: queryKeys.todayObservation })
      const previous = client.getQueryData<api.MarketObservation | null>(queryKeys.todayObservation)
      const optimisticId = `optimistic-${crypto.randomUUID()}`
      const now = new Date().toISOString()
      client.setQueryData<api.MarketObservation | null>(queryKeys.todayObservation, current => {
        const observation = current ?? {
          id: `optimistic-observation-${crypto.randomUUID()}`,
          journalDay: now.slice(0, 10),
          timezone: 'UTC',
          rollover: '00:00',
          updates: [],
        }
        return {
          ...observation,
          updates: [...observation.updates, {
            id: optimisticId,
            content,
            recordedAt: now,
            updatedAt: now,
            signal: null,
            interpretation: null,
            mentalState: null,
            tags: [],
            primarySubject: null,
            relatedSubjects: [],
            evidence: null,
          }],
        }
      })
      return { optimisticId, previous }
    },
    onError: (_error, _variables, context) => {
      if (!context) return
      client.setQueryData<api.MarketObservation | null>(queryKeys.todayObservation, current => {
        if (!current) return context.previous ?? null
        const updates = current.updates.filter(update => update.id !== context.optimisticId)
        if (updates.length === current.updates.length) return current
        if (updates.length > 0) return { ...current, updates }
        if (current.id.startsWith('optimistic-observation-')) return null
        return context.previous ?? { ...current, updates }
      })
    },
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.todayObservation }) },
  })
}
export function useUpdateObservationMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: api.ObservationUpdateWrite }) => api.updateObservation(id, body),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.todayObservation }) },
  })
}

export const useCreateDisciplineMutation = () =>
  useInvalidatingMutation(api.createDiscipline, [queryKeys.disciplines, queryKeys.todayDiscipline])
export const useUpdateDisciplineMutation = () => useInvalidatingMutation(
  ({ id, content, status }: { id: string; content: string; status: api.DisciplinePrincipleStatus }) =>
    api.updateDiscipline(id, content, status),
  [queryKeys.disciplines, queryKeys.todayDiscipline],
)
export const useSelectDisciplineMutation = () =>
  useInvalidatingMutation(api.selectDiscipline, [queryKeys.disciplines, queryKeys.todayDiscipline])
export function useAddWatchlistMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { instrumentId: string; note: string }) =>
      api.addWatchlist(input.instrumentId, input.note),
    onMutate: async input => {
      const instrumentId = input.instrumentId
      const note = input.note.trim() || null
      await client.cancelQueries({ queryKey: queryKeys.watchlist })
      const previous = client.getQueryData<api.WatchlistItem[]>(queryKeys.watchlist)
      const exists = previous?.some(item => item.instrumentId === instrumentId) ?? false
      if (!exists) {
        const now = new Date().toISOString()
        client.setQueryData<api.WatchlistItem[]>(queryKeys.watchlist, current => [
          ...(current ?? []), { instrumentId, note, createdAt: now, updatedAt: now },
        ])
      }
      return { previous, added: !exists }
    },
    onError: (_error, input, context) => {
      if (!context?.added) return
      client.setQueryData<api.WatchlistItem[]>(queryKeys.watchlist, current =>
        current?.filter(item => item.instrumentId !== input.instrumentId),
      )
    },
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.watchlist }) },
  })
}

export function useRemoveWatchlistMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: api.removeWatchlist,
    onMutate: async instrumentId => {
      await client.cancelQueries({ queryKey: queryKeys.watchlist })
      const previous = client.getQueryData<api.WatchlistItem[]>(queryKeys.watchlist)
      const index = previous?.findIndex(item => item.instrumentId === instrumentId) ?? -1
      const removed = index >= 0 ? previous?.[index] : undefined
      if (removed) {
        client.setQueryData<api.WatchlistItem[]>(queryKeys.watchlist, current =>
          current?.filter(item => item.instrumentId !== instrumentId),
        )
      }
      return { removed, index }
    },
    onError: (_error, _instrumentId, context) => {
      if (!context?.removed) return
      client.setQueryData<api.WatchlistItem[]>(queryKeys.watchlist, current => {
        const removed = context.removed
        if (!removed || !current || current.some(item => item.instrumentId === removed.instrumentId)) return current
        const next = [...current]
        next.splice(Math.min(context.index, next.length), 0, removed)
        return next
      })
    },
    onSuccess: async () => { await client.invalidateQueries({ queryKey: queryKeys.watchlist }) },
  })
}
export const useSaveWatchlistNoteMutation = () => useInvalidatingMutation(
  ({ instrumentId, note }: { instrumentId: string; note: string }) => api.saveWatchlistNote(instrumentId, note),
  [queryKeys.watchlist],
)

export function useCreateAgentMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.createAgent, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: queryKeys.agents })
  } })
}
export function useRotateAgentTokenMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.rotateAgentToken, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: queryKeys.agents })
  } })
}
export function useRevokeAgentTokenMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.revokeAgentToken, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: queryKeys.agents })
  } })
}
export function useCreateAccessGrantMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.createAccessGrant, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: queryKeys.accessGrants })
  } })
}
export function useRevokeAccessGrantMutation() {
  const client = useQueryClient()
  return useMutation({ mutationFn: api.revokeAccessGrant, onSuccess: async () => {
    await client.invalidateQueries({ queryKey: queryKeys.accessGrants })
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
        client.invalidateQueries({ queryKey: queryKeys.todayObservation }),
        client.invalidateQueries({ queryKey: calendarPrefix }),
      ])
      await client.refetchQueries({ queryKey: queryKeys.bootstrap })
      return { status: 'ok', settings }
    },
  })
}
