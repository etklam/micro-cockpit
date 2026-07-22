import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { server } from './setup'

function renderTool(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
}

describe('Tools page', () => {
  it('renders the focused catalogue with no removed tools', async () => {
    renderTool('/tools?tool=position-sizing')
    expect(await screen.findByRole('heading', { name: 'Tools' })).toBeInTheDocument()
    const catalogue = screen.getByRole('navigation', { name: 'Investment tools' })
    expect(within(catalogue).getAllByRole('link')).toHaveLength(4)
    expect(within(catalogue).queryByText('Seasonality')).not.toBeInTheDocument()
    expect(within(catalogue).queryByText('Relative value')).not.toBeInTheDocument()
  })

  it('validates missing inputs without producing a result', async () => {
    renderTool('/tools?tool=position-sizing')
    await screen.findByRole('heading', { name: 'Position size' })
    await userEvent.click(screen.getByRole('button', { name: 'Calculate position size' }))
    expect(screen.getAllByText('Enter a valid number.')).toHaveLength(4)
    expect(screen.getByText('Ready when your inputs are')).toBeInTheDocument()
  })

  it('calculates and clears a fee-aware P/L result', async () => {
    renderTool('/tools?tool=profit-loss')
    await screen.findByRole('heading', { name: 'Profit / loss' })
    await userEvent.type(screen.getByLabelText('Entry price'), '50')
    await userEvent.type(screen.getByLabelText('Exit or current price'), '60')
    await userEvent.type(screen.getByLabelText('Quantity'), '100')
    await userEvent.clear(screen.getByLabelText('Entry fees'))
    await userEvent.type(screen.getByLabelText('Entry fees'), '5')
    await userEvent.clear(screen.getByLabelText('Exit fees'))
    await userEvent.type(screen.getByLabelText('Exit fees'), '5')
    await userEvent.click(screen.getByRole('button', { name: 'Calculate P/L' }))
    const gain = screen.getByText('+$990.00')
    expect(gain).toHaveClass('is-gain')
    expect(screen.getByText('+19.78%')).toBeInTheDocument()
    const accentSwitch = screen.getByRole('switch', { name: 'Green or red accent' })
    if (accentSwitch.getAttribute('aria-checked') !== 'true') await userEvent.click(accentSwitch)
    expect(document.documentElement).toHaveAttribute('data-accent', 'red')
    expect(gain).toHaveClass('is-gain')
    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))
    expect(screen.getByText('Ready when your inputs are')).toBeInTheDocument()
    expect(screen.getByLabelText('Entry fees')).toHaveValue(0)
  })

  it('navigates between tools and resets the form state', async () => {
    renderTool('/tools?tool=position-sizing')
    await screen.findByRole('heading', { name: 'Position size' })
    await userEvent.type(screen.getByLabelText('Account value'), '10000')
    await userEvent.click(screen.getByRole('link', { name: /Average cost/ }))
    expect(await screen.findByRole('heading', { name: 'Average cost' })).toBeInTheDocument()
    expect(screen.getByLabelText('Current quantity')).toHaveValue(null)
    expect(window.location.search).toBe('?tool=average-cost')
  })
})
