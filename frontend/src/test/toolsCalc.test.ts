import { expect, test } from 'vitest'
import { calculateTool, ToolInputError } from '../features/toolsCalc'

test('position sizing matches tool-service formula', () => {
  expect(calculateTool('position-sizing', {
    accountValue: 100_000, riskPercent: 1, entryPrice: 50, stopPrice: 45,
  })).toEqual({ riskAmount: 1000, quantity: 200, perUnitRisk: 5 })
})

test('risk reward ratio', () => {
  expect(calculateTool('risk-reward', {
    entryPrice: 100, stopPrice: 90, targetPrice: 130,
  })).toEqual({ risk: 10, reward: 30, ratio: 3 })
})

test('seasonality rejects empty returns', () => {
  expect(() => calculateTool('seasonality', { returns: '' })).toThrow(ToolInputError)
})

test('seasonality average and positive rate', () => {
  expect(calculateTool('seasonality', { returns: '1, -1, 2' })).toEqual({
    observations: 3, averageReturn: 0.6667, positiveRate: 66.67,
  })
})
