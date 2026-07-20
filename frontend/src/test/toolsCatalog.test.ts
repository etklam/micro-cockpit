import { describe, expect, it } from 'vitest'
import { TOOL_IDS } from '../features/toolsCalc'
import {
  DEFAULT_TOOL_ID,
  parseToolQuery,
  TOOL_CATALOG,
  toolHref,
} from '../features/toolsCatalog'

describe('toolsCatalog', () => {
  it('matches TOOL_IDS order with public fields', () => {
    expect(TOOL_CATALOG.map(t => t.id)).toEqual([...TOOL_IDS])
    TOOL_CATALOG.forEach((t, i) => {
      expect(t.order).toBe(i + 1)
      expect(t.public).toBe(true)
      expect(t.route).toBe('/tools')
      expect(t.query).toBe(t.id)
      expect(t.icon).toBeTruthy()
      expect(t.href).toBe(`/tools?tool=${t.id}`)
    })
  })

  it('always uses stable deep links including default tool', () => {
    expect(toolHref('position-sizing')).toBe('/tools?tool=position-sizing')
    expect(toolHref('risk-reward')).toBe('/tools?tool=risk-reward')
    expect(DEFAULT_TOOL_ID).toBe('position-sizing')
  })

  it('falls back unknown query to default tool', () => {
    expect(parseToolQuery(null)).toBe('position-sizing')
    expect(parseToolQuery('nope')).toBe('position-sizing')
    expect(parseToolQuery('fire')).toBe('fire')
  })
})
