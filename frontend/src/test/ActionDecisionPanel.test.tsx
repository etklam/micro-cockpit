import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { expect, test } from 'vitest'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { ActionDecisionPanel } from '../pages/today/ActionDecisionPanel'
import { server } from './setup'

test('related expectation explains when this observation has no options', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  server.use(http.get('/api/app/observation-updates/update-1/action-decisions', () => HttpResponse.json({ items: [] })))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><AuthProvider><I18nProvider><ActionDecisionPanel updateId="update-1" expectations={[]} /></I18nProvider></AuthProvider></QueryClientProvider>)

  await userEvent.click(screen.getByRole('button', { name: 'Add decision' }))
  expect(screen.getByRole('combobox', { name: /Related expectation/ })).toBeDisabled()
  expect(screen.getByText('No expectations for this observation')).toBeInTheDocument()
  expect(screen.getByText('Add one above before linking it here.')).toBeInTheDocument()
})
