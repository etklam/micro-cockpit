import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, expect, test, vi } from 'vitest'

vi.mock('../features/api', () => ({
  createDiary: vi.fn().mockResolvedValue({}),
  updateDiary: vi.fn().mockResolvedValue({}),
  saveDiaryReview: vi.fn().mockResolvedValue({}),
  deleteDiaryReview: vi.fn().mockResolvedValue({}),
  createTransaction: vi.fn().mockResolvedValue({}),
  updateTransaction: vi.fn().mockResolvedValue({}),
  deleteTransaction: vi.fn().mockResolvedValue({}),
}))

import {
  useCreateTransactionMutation,
  useDeleteDiaryReviewMutation,
  useDeleteTransactionMutation,
  useSaveDiaryMutation,
  useSaveDiaryReviewMutation,
  useUpdateTransactionMutation,
} from '../features/queries'

let client: QueryClient
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>

beforeEach(() => { client = new QueryClient({ defaultOptions: { mutations: { retry: false } } }) })

test('diary mutation invalidates diaries, dashboard, and calendar queries', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useSaveDiaryMutation(), { wrapper })
  await act(() => result.current.mutateAsync({ date: '2026-07-16', title: 'Day', content: 'Notes', tags: ['fomo'], key: 'idempotency-key' }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['dashboard'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})

test('review save invalidates list variants, detail, summaries, evidence items, and diary detail', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useSaveDiaryReviewMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync({ thesis: null, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'detail', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'summary'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'items'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledTimes(5)
})

test('review delete invalidates evidence item queries and diary lists', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useDeleteDiaryReviewMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync())
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'items'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
})

test('transaction create invalidates diary list variants, transactions, and calendar', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useCreateTransactionMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync({
    body: { symbol: 'AAPL', side: 'buy', quantity: 1, price: 100, currency: 'USD', tradedAt: '2026-07-16T08:00:00.000Z', notes: null },
    key: 'idempotency-key',
  }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['transactions', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})

test('transaction update invalidates diary list variants, transactions, and calendar', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useUpdateTransactionMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync({
    id: 'tx-1',
    body: { symbol: 'AAPL', side: 'sell', quantity: 2, price: 101, currency: 'USD', tradedAt: '2026-07-16T08:00:00.000Z', notes: 'Updated' },
  }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['transactions', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})

test('transaction delete invalidates diary list variants, transactions, and calendar', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useDeleteTransactionMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync('tx-1'))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['transactions', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})
