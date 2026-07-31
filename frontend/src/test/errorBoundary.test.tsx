import { render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { ErrorBoundary } from '../shell'

function ThrowingChild(): never {
  throw new Error('render failed')
}

test('error boundary replaces a throwing child with its recovery UI', () => {
  const error = vi.spyOn(console, 'error').mockImplementation(() => undefined)
  render(
    <ErrorBoundary fallback={() => <div role="alert">Recovered</div>}>
      <ThrowingChild />
    </ErrorBoundary>,
  )
  expect(screen.getByRole('alert')).toHaveTextContent('Recovered')
  error.mockRestore()
})
