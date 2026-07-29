import { isToolId, type ToolId } from './toolsCalc'

export type ToolContext = {
  tool: ToolId
  values: Record<string, string>
  currency?: string
  symbol?: string
  label: string
  returnTo: string
}

const positive = (value: unknown) =>
  typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) > 0
const safePath = (value: unknown): value is string =>
  typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')

/** Normalizes optional calculator navigation state at the router boundary. */
export function readToolContext(state: unknown, expectedTool: ToolId): ToolContext | null {
  if (!state || typeof state !== 'object') return null
  const candidate = state as Partial<ToolContext>
  if (!candidate.tool || !isToolId(candidate.tool) || candidate.tool !== expectedTool ||
      !candidate.values || typeof candidate.values !== 'object') return null
  if (typeof candidate.label !== 'string' || candidate.label.trim().length < 1 ||
      candidate.label.length > 120 || !safePath(candidate.returnTo)) return null
  if (candidate.currency && !/^[A-Z]{3}$/.test(candidate.currency)) return null
  if (candidate.symbol && !/^[A-Z0-9.-]{1,20}$/.test(candidate.symbol)) return null
  const values = Object.fromEntries(
    Object.entries(candidate.values).filter(([, value]) => typeof value === 'string'),
  ) as Record<string, string>
  const numericValues = Object.entries(values).filter(([key]) => key !== 'side')
  if (numericValues.some(([key, value]) =>
    key === 'entryFee' || key === 'exitFee'
      ? !(value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) >= 0)
      : !positive(value))) return null
  if (values.side && values.side !== 'long' && values.side !== 'short') return null
  return {
    tool: candidate.tool,
    values,
    currency: candidate.currency,
    symbol: candidate.symbol,
    label: candidate.label.trim(),
    returnTo: candidate.returnTo,
  }
}
