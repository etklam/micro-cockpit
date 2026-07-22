import { describe, expect, it } from 'vitest'
import { buildDiaryDraft, buildTradeDraft, readToolContext, toolContextForRiskReward, toolContextForTrade } from '../features/toolsWorkflow'

describe('tool workflow context', () => {
  it('maps a diary transaction into editable P/L inputs without persisting record IDs in the URL', () => {
    const context = toolContextForTrade({
      id: 'trade-id', diaryId: 'diary-id', symbol: 'AAPL', side: 'buy', quantity: 3,
      price: 190, currency: 'USD', tradedAt: '2026-07-20T08:00:00Z', notes: '',
      createdAt: '2026-07-20T08:00:00Z', updatedAt: '2026-07-20T08:00:00Z',
    }, '/diary/diary-id')
    expect(context.values).toMatchObject({ side: 'long', entryPrice: '190', quantity: '3' })
    expect(context.label).toBe('AAPL trade')
    expect(context.returnTo).toBe('/diary/diary-id')
    expect(context.sourceTransactionId).toBe('trade-id')
    expect('/tools?tool=profit-loss').not.toContain('trade-id')
  })

  it('maps a transaction entry into risk/reward context without inventing stop or target prices', () => {
    const trade = { id: 'trade-id', diaryId: 'diary-id', symbol: 'AAPL', side: 'buy', quantity: 3, price: 190, currency: 'USD', tradedAt: '2026-07-20T08:00:00Z', notes: '', createdAt: '2026-07-20T08:00:00Z', updatedAt: '2026-07-20T08:00:00Z' }
    expect(toolContextForRiskReward(trade, '/diary/diary-id').values).toEqual({ entryPrice: '190' })
  })

  it('rejects malformed navigation state', () => {
    expect(readToolContext({ tool: 'profit-loss', values: { entryPrice: '-2' }, label: 'bad', returnTo: 'https://evil.test' }, 'profit-loss')).toBeNull()
    expect(readToolContext({ tool: 'profit-loss', values: { entryPrice: '190' }, label: 'AAPL trade', returnTo: '/diary/1' }, 'risk-reward')).toBeNull()
  })

  it('creates review-only trade and diary drafts from results', () => {
    const trade = buildTradeDraft('position-sizing', { entryPrice: '100', stopPrice: '95' }, { quantity: 20 }, 'USD', 'AAPL')
    expect(trade).toMatchObject({ symbol: 'AAPL', side: 'buy', quantity: '20', price: '100', currency: 'USD' })
    expect(trade.notes).toContain('Position size')
    const diary = buildDiaryDraft('risk-reward', { entryPrice: '100', stopPrice: '95', targetPrice: '115' }, { ratio: 3 }, 'USD', 'AAPL', '2026-07-20')
    expect(diary.date).toBe('2026-07-20')
    expect(diary.content).toContain('Risk / reward')
    expect(diary.content).not.toContain('{')
  })
})
