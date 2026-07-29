import { describe, expect, it } from 'vitest'
import { readToolContext } from '../features/toolsWorkflow'

describe('tool workflow context', () => {
  it('accepts safe calculator state and rejects malformed navigation state', () => {
    expect(readToolContext({
      tool: 'profit-loss',
      values: { entryPrice: '190', quantity: '3', side: 'long' },
      currency: 'USD',
      symbol: 'AAPL',
      label: 'AAPL scenario',
      returnTo: '/tools',
    }, 'profit-loss')).toMatchObject({ symbol: 'AAPL', currency: 'USD' })
    expect(readToolContext({
      tool: 'profit-loss',
      values: { entryPrice: '-2' },
      label: 'bad',
      returnTo: 'https://evil.test',
    }, 'profit-loss')).toBeNull()
  })
})
