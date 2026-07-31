import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, expect, test, vi } from 'vitest'
import type { MarketObservation, WatchlistItem } from '../features/api'

const mockedApi = vi.hoisted(() => ({
  saveQuickObservation: vi.fn(),
  addWatchlist: vi.fn(),
  removeWatchlist: vi.fn(),
}))

vi.mock('../features/api', () => mockedApi)

import {
  queryKeys,
  useAddWatchlistMutation,
  useQuickObservationMutation,
  useRemoveWatchlistMutation,
} from '../features/queries'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve
    reject = promiseReject
  })
  return { promise, resolve, reject }
}

const observation: MarketObservation = {
  id: 'observation-1',
  journalDay: '2026-08-01',
  timezone: 'UTC',
  rollover: '00:00',
  updates: [{
    id: 'update-1',
    content: 'Existing note',
    recordedAt: '2026-08-01T08:00:00Z',
    updatedAt: '2026-08-01T08:00:00Z',
    signal: null,
    interpretation: null,
    mentalState: null,
    tags: [],
    primarySubject: null,
    relatedSubjects: [],
    evidence: null,
  }],
}

const watchlist: WatchlistItem[] = [
  { instrumentId: 'instrument-1', note: null, createdAt: '2026-08-01T08:00:00Z', updatedAt: '2026-08-01T08:00:00Z' },
]

let client: QueryClient
const wrapper = ({ children }: { children: ReactNode }) =>
  <QueryClientProvider client={client}>{children}</QueryClientProvider>

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  mockedApi.saveQuickObservation.mockReset()
  mockedApi.addWatchlist.mockReset()
  mockedApi.removeWatchlist.mockReset()
})

test('quick observations appear while pending and concurrent failures only rollback their own update', async () => {
  const first = deferred<unknown>()
  const second = deferred<unknown>()
  mockedApi.saveQuickObservation.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
  client.setQueryData(queryKeys.todayObservation, observation)
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useQuickObservationMutation(), { wrapper })

  const firstRequest = result.current.mutateAsync({ content: 'First note', key: 'first' })
  await waitFor(() => expect(client.getQueryData<MarketObservation>(queryKeys.todayObservation)?.updates).toHaveLength(2))
  const secondRequest = result.current.mutateAsync({ content: 'Second note', key: 'second' })
  await waitFor(() => expect(client.getQueryData<MarketObservation>(queryKeys.todayObservation)?.updates).toHaveLength(3))

  await act(async () => {
    first.reject(new Error('first failed'))
    await expect(firstRequest).rejects.toThrow('first failed')
  })
  expect(client.getQueryData<MarketObservation>(queryKeys.todayObservation)?.updates.map(update => update.content))
    .toEqual(['Existing note', 'Second note'])

  await act(async () => {
    second.resolve({})
    await secondRequest
  })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: queryKeys.todayObservation })
})

test('watchlist add and remove are immediate, then rollback on failure', async () => {
  const add = deferred<unknown>()
  const remove = deferred<unknown>()
  mockedApi.addWatchlist.mockReturnValue(add.promise)
  mockedApi.removeWatchlist.mockReturnValue(remove.promise)
  client.setQueryData(queryKeys.watchlist, watchlist)
  const { result: addResult } = renderHook(() => useAddWatchlistMutation(), { wrapper })

  const addRequest = addResult.current.mutateAsync('instrument-2')
  await waitFor(() => expect(client.getQueryData<WatchlistItem[]>(queryKeys.watchlist)?.map(item => item.instrumentId))
    .toEqual(['instrument-1', 'instrument-2']))
  await act(async () => {
    add.reject(new Error('add failed'))
    await expect(addRequest).rejects.toThrow('add failed')
  })
  expect(client.getQueryData<WatchlistItem[]>(queryKeys.watchlist)).toEqual(watchlist)

  const { result: removeResult } = renderHook(() => useRemoveWatchlistMutation(), { wrapper })
  const removeRequest = removeResult.current.mutateAsync('instrument-1')
  await waitFor(() => expect(client.getQueryData<WatchlistItem[]>(queryKeys.watchlist)).toEqual([]))
  await act(async () => {
    remove.reject(new Error('remove failed'))
    await expect(removeRequest).rejects.toThrow('remove failed')
  })
  expect(client.getQueryData<WatchlistItem[]>(queryKeys.watchlist)).toEqual(watchlist)
})
