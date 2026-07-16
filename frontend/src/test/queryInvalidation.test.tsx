import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, expect, test, vi } from 'vitest'

vi.mock('../features/api', () => ({
  createDiary: vi.fn().mockResolvedValue({}),
  updateDiary: vi.fn().mockResolvedValue({}),
  saveDiaryReview: vi.fn().mockResolvedValue({}),
  deleteDiaryReview: vi.fn().mockResolvedValue({}),
}))

import { useSaveDiaryMutation, useSaveDiaryReviewMutation } from '../features/queries'

let client: QueryClient
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>

beforeEach(() => { client = new QueryClient({ defaultOptions: { mutations: { retry: false } } }) })

test('diary mutation invalidates diaries, dashboard, and calendar queries', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useSaveDiaryMutation(), { wrapper })
  await act(() => result.current.mutateAsync({ date: '2026-07-16', title: 'Day', content: 'Notes', key: 'idempotency-key' }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diaries'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['dashboard'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})

test('review save invalidates only its detail, summaries, and diary detail', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useSaveDiaryReviewMutation('diary-1'), { wrapper })
  await act(() => result.current.mutateAsync({ thesis: null, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'detail', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary-review', 'summary'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['diary', 'diary-1'] })
  expect(invalidation).toHaveBeenCalledTimes(3)
})
