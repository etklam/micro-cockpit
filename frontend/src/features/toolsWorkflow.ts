import type { Transaction } from './api'
import { isToolId, type ToolId } from './toolsCalc'

export type ToolContext = {
  tool: ToolId
  values: Record<string, string>
  currency?: string
  symbol?: string
  diaryDate?: string
  label: string
  returnTo: string
  sourceDiaryId?: string
  sourceTransactionId?: string
}

export type TradeDraft = { symbol: string; side: 'buy' | 'sell'; quantity: string; price: string; currency: string; notes: string }
export type DiaryDraft = { date: string; title: string; content: string }

const positive = (value: unknown) => typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) > 0
const safePath = (value: unknown): value is string => typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')

/**
 * Treats React Router state as an untrusted boundary and returns a normalized context only
 * when the tool, source metadata, values, and internal return path are all safe to apply.
 */
export function readToolContext(state: unknown, expectedTool: ToolId): ToolContext | null {
  if (!state || typeof state !== 'object') return null
  const candidate = state as Partial<ToolContext>
  if (!candidate.tool || !isToolId(candidate.tool) || candidate.tool !== expectedTool || !candidate.values || typeof candidate.values !== 'object') return null
  if (typeof candidate.label !== 'string' || candidate.label.trim().length < 1 || candidate.label.length > 120 || !safePath(candidate.returnTo)) return null
  if (candidate.currency && !/^[A-Z]{3}$/.test(candidate.currency)) return null
  if (candidate.symbol && !/^[A-Z0-9.-]{1,20}$/.test(candidate.symbol)) return null
  if (candidate.diaryDate && !/^\d{4}-\d{2}-\d{2}$/.test(candidate.diaryDate)) return null
  const values = Object.fromEntries(Object.entries(candidate.values).filter(([, value]) => typeof value === 'string')) as Record<string, string>
  const numericValues = Object.entries(values).filter(([key]) => key !== 'side')
  if (numericValues.some(([key, value]) => key === 'entryFee' || key === 'exitFee' ? !(value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) >= 0) : !positive(value))) return null
  if (values.side && values.side !== 'long' && values.side !== 'short') return null
  if ((candidate.sourceDiaryId && typeof candidate.sourceDiaryId !== 'string') || (candidate.sourceTransactionId && typeof candidate.sourceTransactionId !== 'string')) return null
  return { tool: candidate.tool, values, currency: candidate.currency, symbol: candidate.symbol, diaryDate: candidate.diaryDate, label: candidate.label.trim(), returnTo: candidate.returnTo, sourceDiaryId: candidate.sourceDiaryId, sourceTransactionId: candidate.sourceTransactionId }
}

/** Maps a recorded transaction to editable profit/loss assumptions without inventing exit data. */
export function toolContextForTrade(trade: Transaction, returnTo: string, diaryDate?: string): ToolContext {
  return {
    tool: 'profit-loss',
    values: { side: trade.side === 'sell' ? 'short' : 'long', entryPrice: String(trade.price), quantity: String(trade.quantity), entryFee: '0', exitFee: '0' },
    currency: trade.currency.toUpperCase(), symbol: trade.symbol.toUpperCase(), diaryDate,
    label: `${trade.symbol.toUpperCase()} trade`, returnTo, sourceDiaryId: trade.diaryId, sourceTransactionId: trade.id,
  }
}

/** Maps only the transaction facts known to risk/reward; stop and target remain user decisions. */
export function toolContextForRiskReward(trade: Transaction, returnTo: string, diaryDate?: string): ToolContext {
  return { tool: 'risk-reward', values: { entryPrice: String(trade.price) }, currency: trade.currency.toUpperCase(), symbol: trade.symbol.toUpperCase(), diaryDate, label: `${trade.symbol.toUpperCase()} trade`, returnTo, sourceDiaryId: trade.diaryId, sourceTransactionId: trade.id }
}

/** Builds position-sizing context from valid draft fields and omits empty or invalid values. */
export function toolContextForPositionDraft(draft: Partial<TradeDraft>, returnTo: string, diaryDate?: string, sourceDiaryId?: string): ToolContext {
  const values: Record<string, string> = {}
  if (positive(draft.price)) values.entryPrice = draft.price!
  return { tool: 'position-sizing', values, currency: draft.currency && /^[A-Z]{3}$/.test(draft.currency) ? draft.currency : undefined, symbol: draft.symbol && /^[A-Z0-9.-]{1,20}$/.test(draft.symbol) ? draft.symbol : undefined, diaryDate, label: draft.symbol ? `${draft.symbol} trade draft` : 'Trade draft', returnTo, sourceDiaryId }
}

const toolName = (tool: ToolId) => ({ 'position-sizing': 'Position size', 'risk-reward': 'Risk / reward', 'average-cost': 'Average cost', 'profit-loss': 'Profit / loss' })[tool]

/**
 * Converts applicable calculator output into an editable transaction draft.
 * This function never persists or submits the draft; the diary transaction form remains the gate.
 */
export function buildTradeDraft(tool: ToolId, inputs: Record<string, string>, output: Record<string, number>, currency: string, symbol = ''): TradeDraft {
  return {
    symbol, side: 'buy', quantity: String(output.quantity ?? ''), price: inputs.entryPrice ?? '', currency,
    notes: `${toolName(tool)} calculator draft. Review all values before saving.${inputs.stopPrice ? ` Planned stop: ${inputs.stopPrice}.` : ''}`,
  }
}

/** Produces a readable Markdown snapshot for the diary editor without publishing it. */
export function buildDiaryDraft(tool: ToolId, inputs: Record<string, string>, output: Record<string, number>, currency: string, symbol = '', date = ''): DiaryDraft {
  const primary = tool === 'position-sizing' ? `Quantity: ${output.quantity}` : tool === 'risk-reward' ? `Reward/risk: ${output.ratio}×` : tool === 'average-cost' ? `Average cost: ${output.averageCost} ${currency}` : `Net P/L: ${output.netPnl} ${currency}`
  const assumptions = Object.entries(inputs).map(([key, value]) => `${key}: ${value}`).join('; ')
  return {
    date, title: `${toolName(tool)}${symbol ? ` — ${symbol}` : ''}`,
    content: `### Calculation\n\n- Tool used: ${toolName(tool)}\n- Assumptions: ${assumptions}\n- Primary result: ${primary}\n- Decision notes: Review assumptions before acting.\n- Calculated: ${new Date().toISOString()}`,
  }
}
