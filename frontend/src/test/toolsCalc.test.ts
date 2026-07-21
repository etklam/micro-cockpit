import { describe, expect, test } from 'vitest'
import { calculateTool, ToolInputError, validateTool } from '../features/toolsCalc'

describe('position sizing', () => {
  test('uses whole units and exposes the actual planned loss', () => {
    expect(calculateTool('position-sizing', {
      accountValue: 100_000, riskPercent: 1, entryPrice: 50, stopPrice: 46,
    })).toEqual({ quantity: 250, plannedLoss: 1000, riskBudget: 1000, positionValue: 12_500, perUnitRisk: 4 })
  })

  test('rounds quantity down when the risk budget does not divide evenly', () => {
    expect(calculateTool('position-sizing', {
      accountValue: 10_000, riskPercent: 1, entryPrice: 12, stopPrice: 9,
    })).toMatchObject({ quantity: 33, plannedLoss: 99, riskBudget: 100 })
  })

  test('rejects zero distance and risk above 100 percent', () => {
    expect(validateTool('position-sizing', { accountValue: 1000, riskPercent: 101, entryPrice: 10, stopPrice: 10 })).toEqual({
      riskPercent: 'riskPercent', stopPrice: 'oppositeSides',
    })
  })
})

describe('risk / reward', () => {
  test('calculates long and short setups', () => {
    expect(calculateTool('risk-reward', { entryPrice: 100, stopPrice: 90, targetPrice: 130 })).toEqual({
      ratio: 3, riskPerUnit: 10, rewardPerUnit: 30, breakevenWinRate: 25,
    })
    expect(calculateTool('risk-reward', { entryPrice: 100, stopPrice: 110, targetPrice: 80 })).toEqual({
      ratio: 2, riskPerUnit: 10, rewardPerUnit: 20, breakevenWinRate: 33.33,
    })
  })

  test('rejects a target on the stop side of entry', () => {
    expect(validateTool('risk-reward', { entryPrice: 100, stopPrice: 90, targetPrice: 80 })).toEqual({ targetPrice: 'oppositeSides' })
  })
})

describe('average cost', () => {
  test('weights fractional quantities correctly', () => {
    expect(calculateTool('average-cost', {
      currentQuantity: 10.5, currentAverageCost: 100, addedQuantity: 4.5, addedPrice: 80,
    })).toEqual({ averageCost: 94, totalQuantity: 15, totalCost: 1410, averageCostChange: -6 })
  })
})

describe('profit / loss', () => {
  test('subtracts fees from a long gain', () => {
    expect(calculateTool('profit-loss', {
      side: 'long', entryPrice: 50, exitPrice: 60, quantity: 100, entryFee: 5, exitFee: 5,
    })).toEqual({ netPnl: 990, returnPercent: 19.78, grossPnl: 1000, totalFees: 10, exitValue: 6000 })
  })

  test('handles a short gain and decimal inputs', () => {
    expect(calculateTool('profit-loss', {
      side: 'short', entryPrice: 20.5, exitPrice: 18, quantity: 10.25, entryFee: 1, exitFee: 1,
    })).toEqual({ netPnl: 23.63, returnPercent: 11.19, grossPnl: 25.63, totalFees: 2, exitValue: 184.5 })
  })

  test('reports invalid and negative values by field', () => {
    expect(validateTool('profit-loss', { side: '', entryPrice: '', exitPrice: 0, quantity: -1, entryFee: -2, exitFee: 0 })).toEqual({
      entryPrice: 'required', exitPrice: 'positive', quantity: 'positive', entryFee: 'nonnegative', side: 'required',
    })
    expect(() => calculateTool('profit-loss', { side: 'long' })).toThrow(ToolInputError)
  })
})
