import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, expect, test, vi } from 'vitest'

vi.mock('../features/api', () => ({
  createDiary: vi.fn().mockResolvedValue({}),
  updateDiary: vi.fn().mockResolvedValue({}),
}))

import { useSaveDiaryMutation } from '../features/queries'

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
