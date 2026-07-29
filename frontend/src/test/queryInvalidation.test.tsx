import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, expect, test, vi } from 'vitest'

vi.mock('../features/api', () => ({
  createExpectation: vi.fn().mockResolvedValue({}),
  updateExpectation: vi.fn().mockResolvedValue({}),
  invalidateExpectation: vi.fn().mockResolvedValue({}),
}))

import {
  useCreateExpectationMutation,
  useInvalidateExpectationMutation,
  useUpdateExpectationMutation,
} from '../features/queries'

let client: QueryClient
const wrapper = ({ children }: { children: ReactNode }) =>
  <QueryClientProvider client={client}>{children}</QueryClientProvider>

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
})

test('expectation create invalidates expectations, Today, and calendar queries', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const { result } = renderHook(() => useCreateExpectationMutation(), { wrapper })
  await act(() => result.current.mutateAsync({
    updateId: 'update-1',
    key: 'idempotency-key',
    body: {
      expectedBehavior: 'Breadth holds',
      deadline: '2026-07-17T08:00:00Z',
      deadlinePreset: null,
      invalidationCondition: 'Breadth breaks',
      confidence: 'medium',
      market: 'US',
    },
  }))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['expectations'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['market-observations', 'today'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})

test('expectation edit and invalidation refresh expectations and calendar', async () => {
  const invalidation = vi.spyOn(client, 'invalidateQueries')
  const edit = renderHook(() => useUpdateExpectationMutation(), { wrapper })
  await act(() => edit.result.current.mutateAsync({
    id: 'expectation-1',
    body: {
      expectedBehavior: 'Breadth holds',
      deadline: '2026-07-17T08:00:00Z',
      deadlinePreset: null,
      invalidationCondition: 'Breadth breaks',
      confidence: 'high',
      market: 'US',
    },
  }))
  const invalidate = renderHook(() => useInvalidateExpectationMutation(), { wrapper })
  await act(() => invalidate.result.current.mutateAsync('expectation-1'))
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['expectations'] })
  expect(invalidation).toHaveBeenCalledWith({ queryKey: ['calendar'] })
})
