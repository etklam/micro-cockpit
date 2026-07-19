/** Pure client-side tool calculators — same formulas as tool-service. */

export type ToolId = 'position-sizing' | 'risk-reward' | 'fire' | 'relative-value' | 'seasonality'

export const TOOL_IDS: ToolId[] = ['position-sizing', 'risk-reward', 'fire', 'relative-value', 'seasonality']

export function isToolId(value: string): value is ToolId {
  return (TOOL_IDS as string[]).includes(value)
}

export class ToolInputError extends Error {
  constructor() {
    super('invalid_input')
    this.name = 'ToolInputError'
  }
}

function n(value: unknown): number {
  const x = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(x)) throw new ToolInputError()
  return x
}

function round(value: number, digits: number): number {
  const f = 10 ** digits
  return Math.round(value * f) / f
}

export function calculateTool(tool: ToolId, values: Record<string, unknown>): Record<string, number> {
  switch (tool) {
    case 'position-sizing': {
      const accountValue = n(values.accountValue)
      const riskPercent = n(values.riskPercent)
      const entryPrice = n(values.entryPrice)
      const stopPrice = n(values.stopPrice)
      if (accountValue <= 0 || riskPercent <= 0 || entryPrice <= 0 || stopPrice < 0 || entryPrice === stopPrice) throw new ToolInputError()
      const riskAmount = accountValue * riskPercent / 100
      const perUnitRisk = Math.abs(entryPrice - stopPrice)
      return { riskAmount, quantity: Math.floor(riskAmount / perUnitRisk), perUnitRisk }
    }
    case 'risk-reward': {
      const entryPrice = n(values.entryPrice)
      const stopPrice = n(values.stopPrice)
      const targetPrice = n(values.targetPrice)
      if (entryPrice <= 0 || stopPrice < 0 || targetPrice <= 0 || entryPrice === stopPrice) throw new ToolInputError()
      const risk = Math.abs(entryPrice - stopPrice)
      const reward = Math.abs(targetPrice - entryPrice)
      return { risk, reward, ratio: round(reward / risk, 4) }
    }
    case 'fire': {
      const annualExpenses = n(values.annualExpenses)
      const withdrawalRatePercent = n(values.withdrawalRatePercent)
      const investedAssets = n(values.investedAssets)
      if (annualExpenses <= 0 || withdrawalRatePercent <= 0) throw new ToolInputError()
      const target = round(annualExpenses / (withdrawalRatePercent / 100), 2)
      return { target, gap: Math.max(0, round(target - investedAssets, 2)) }
    }
    case 'relative-value': {
      const assetPrice = n(values.assetPrice)
      const benchmarkPrice = n(values.benchmarkPrice)
      const historicalRatio = n(values.historicalRatio)
      if (assetPrice <= 0 || benchmarkPrice <= 0 || historicalRatio <= 0) throw new ToolInputError()
      const currentRatio = round(assetPrice / benchmarkPrice, 6)
      return { currentRatio, deviationPercent: round((currentRatio / historicalRatio - 1) * 100, 4) }
    }
    case 'seasonality': {
      const returns = Array.isArray(values.returns)
        ? values.returns.map(n)
        : String(values.returns ?? '').split(',').map(s => s.trim()).filter(Boolean).map(n)
      if (returns.length === 0 || returns.length > 120) throw new ToolInputError()
      const averageReturn = round(returns.reduce((a, b) => a + b, 0) / returns.length, 4)
      const positiveRate = round(returns.filter(v => v > 0).length / returns.length * 100, 2)
      return { observations: returns.length, averageReturn, positiveRate }
    }
  }
}
