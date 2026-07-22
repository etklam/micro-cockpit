/** Deterministic, client-side investment calculations used by the public Tools area. */

export type ToolId = 'position-sizing' | 'risk-reward' | 'average-cost' | 'profit-loss'

export const TOOL_IDS: ToolId[] = ['position-sizing', 'risk-reward', 'average-cost', 'profit-loss']

export function isToolId(value: string): value is ToolId {
  return (TOOL_IDS as string[]).includes(value)
}

export type ToolValidationCode =
  | 'required'
  | 'positive'
  | 'nonnegative'
  | 'riskPercent'
  | 'oppositeSides'

export type ToolValidationErrors = Record<string, ToolValidationCode>

export class ToolInputError extends Error {
  readonly errors: ToolValidationErrors

  constructor(errors: ToolValidationErrors) {
    super('invalid_input')
    this.name = 'ToolInputError'
    this.errors = errors
  }
}

function readNumber(values: Record<string, unknown>, key: string, errors: ToolValidationErrors): number {
  const raw = values[key]
  if (raw === '' || raw == null) {
    errors[key] = 'required'
    return Number.NaN
  }
  const value = typeof raw === 'number' ? raw : Number(raw)
  if (!Number.isFinite(value)) {
    errors[key] = 'required'
    return Number.NaN
  }
  return value
}

function positive(values: Record<string, unknown>, key: string, errors: ToolValidationErrors): number {
  const value = readNumber(values, key, errors)
  if (Number.isFinite(value) && value <= 0) errors[key] = 'positive'
  return value
}

function nonnegative(values: Record<string, unknown>, key: string, errors: ToolValidationErrors): number {
  const value = readNumber(values, key, errors)
  if (Number.isFinite(value) && value < 0) errors[key] = 'nonnegative'
  return value
}

function round(value: number, digits = 2): number {
  const factor = 10 ** digits
  return Math.round((value + Number.EPSILON) * factor) / factor
}

/**
 * Validates raw, editable form values without coercing or mutating the caller's state.
 * The returned field codes are presentation-independent and are translated by ToolsPage.
 */
export function validateTool(tool: ToolId, values: Record<string, unknown>): ToolValidationErrors {
  const errors: ToolValidationErrors = {}
  switch (tool) {
    case 'position-sizing': {
      positive(values, 'accountValue', errors)
      const riskPercent = positive(values, 'riskPercent', errors)
      positive(values, 'entryPrice', errors)
      positive(values, 'stopPrice', errors)
      if (Number.isFinite(riskPercent) && riskPercent > 100) errors.riskPercent = 'riskPercent'
      if (!errors.entryPrice && !errors.stopPrice && Number(values.entryPrice) === Number(values.stopPrice)) errors.stopPrice = 'oppositeSides'
      break
    }
    case 'risk-reward': {
      const entry = positive(values, 'entryPrice', errors)
      const stop = positive(values, 'stopPrice', errors)
      const target = positive(values, 'targetPrice', errors)
      if (Number.isFinite(entry) && Number.isFinite(stop) && Number.isFinite(target)) {
        const validLong = stop < entry && target > entry
        const validShort = stop > entry && target < entry
        if (!validLong && !validShort) errors.targetPrice = 'oppositeSides'
      }
      break
    }
    case 'average-cost':
      positive(values, 'currentQuantity', errors)
      positive(values, 'currentAverageCost', errors)
      positive(values, 'addedQuantity', errors)
      positive(values, 'addedPrice', errors)
      break
    case 'profit-loss':
      positive(values, 'entryPrice', errors)
      positive(values, 'exitPrice', errors)
      positive(values, 'quantity', errors)
      nonnegative(values, 'entryFee', errors)
      nonnegative(values, 'exitFee', errors)
      if (values.side !== 'long' && values.side !== 'short') errors.side = 'required'
      break
  }
  return errors
}

/**
 * Runs a deterministic calculator after full validation.
 * Throws ToolInputError for invalid inputs; callers must not treat this client result as
 * authoritative persistence because Tool service recalculates saved snapshots separately.
 */
export function calculateTool(tool: ToolId, values: Record<string, unknown>): Record<string, number> {
  const errors = validateTool(tool, values)
  if (Object.keys(errors).length) throw new ToolInputError(errors)

  switch (tool) {
    case 'position-sizing': {
      const accountValue = Number(values.accountValue)
      const riskPercent = Number(values.riskPercent)
      const entryPrice = Number(values.entryPrice)
      const perUnitRisk = Math.abs(entryPrice - Number(values.stopPrice))
      const riskBudget = accountValue * riskPercent / 100
      const quantity = Math.floor(riskBudget / perUnitRisk)
      return {
        quantity,
        plannedLoss: round(quantity * perUnitRisk),
        riskBudget: round(riskBudget),
        positionValue: round(quantity * entryPrice),
        perUnitRisk: round(perUnitRisk, 6),
      }
    }
    case 'risk-reward': {
      const entry = Number(values.entryPrice)
      const risk = Math.abs(entry - Number(values.stopPrice))
      const reward = Math.abs(Number(values.targetPrice) - entry)
      const ratio = reward / risk
      return {
        ratio: round(ratio, 4),
        riskPerUnit: round(risk, 6),
        rewardPerUnit: round(reward, 6),
        breakevenWinRate: round(100 / (1 + ratio), 2),
      }
    }
    case 'average-cost': {
      const currentQuantity = Number(values.currentQuantity)
      const currentAverageCost = Number(values.currentAverageCost)
      const addedQuantity = Number(values.addedQuantity)
      const addedPrice = Number(values.addedPrice)
      const totalQuantity = currentQuantity + addedQuantity
      const currentCost = currentQuantity * currentAverageCost
      const addedCost = addedQuantity * addedPrice
      const averageCost = (currentCost + addedCost) / totalQuantity
      return {
        averageCost: round(averageCost, 6),
        totalQuantity: round(totalQuantity, 6),
        totalCost: round(currentCost + addedCost),
        averageCostChange: round((averageCost / currentAverageCost - 1) * 100, 2),
      }
    }
    case 'profit-loss': {
      const entry = Number(values.entryPrice)
      const exit = Number(values.exitPrice)
      const quantity = Number(values.quantity)
      const entryFee = Number(values.entryFee)
      const exitFee = Number(values.exitFee)
      const direction = values.side === 'short' ? -1 : 1
      const grossPnl = (exit - entry) * quantity * direction
      const totalFees = entryFee + exitFee
      const netPnl = grossPnl - totalFees
      const capitalAtRisk = entry * quantity + entryFee
      return {
        netPnl: round(netPnl),
        returnPercent: round(netPnl / capitalAtRisk * 100, 2),
        grossPnl: round(grossPnl),
        totalFees: round(totalFees),
        exitValue: round(exit * quantity),
      }
    }
  }
}
