/**
 * Canonical public five-tool catalogue — single ordered source for
 * landing, public/auth nav, and Tools selector. Keep aligned with toolsCalc.
 */
import { TOOL_IDS, type ToolId, isToolId } from './toolsCalc'
import type { MessageKey } from '../i18n'
import type { IconName } from '../icons'

export type ToolCatalogEntry = {
  id: ToolId
  order: number
  public: true
  icon: IconName
  route: '/tools'
  query: ToolId
  labelKey: MessageKey
  titleKey: MessageKey
  bodyKey: MessageKey
  href: string
}

export const DEFAULT_TOOL_ID: ToolId = 'position-sizing'

const TOOL_ICONS: Record<ToolId, IconName> = {
  'position-sizing': 'layers',
  'risk-reward': 'compass',
  fire: 'sparkle',
  'relative-value': 'arrow',
  seasonality: 'calendar',
}

export const TOOL_CATALOG: readonly ToolCatalogEntry[] = TOOL_IDS.map((id, index) => ({
  id,
  order: index + 1,
  public: true as const,
  icon: TOOL_ICONS[id],
  route: '/tools' as const,
  query: id,
  labelKey: labelKeyFor(id),
  titleKey: labelKeyFor(id),
  bodyKey: bodyKeyFor(id),
  href: toolHref(id),
}))

function labelKeyFor(id: ToolId): MessageKey {
  switch (id) {
    case 'position-sizing': return 'landing.tool.positionSizing.title'
    case 'risk-reward': return 'landing.tool.riskReward.title'
    case 'fire': return 'landing.tool.fire.title'
    case 'relative-value': return 'landing.tool.relativeValue.title'
    case 'seasonality': return 'landing.tool.seasonality.title'
  }
}

function bodyKeyFor(id: ToolId): MessageKey {
  switch (id) {
    case 'position-sizing': return 'landing.tool.positionSizing.body'
    case 'risk-reward': return 'landing.tool.riskReward.body'
    case 'fire': return 'landing.tool.fire.body'
    case 'relative-value': return 'landing.tool.relativeValue.body'
    case 'seasonality': return 'landing.tool.seasonality.body'
  }
}

/** Stable deep link — always includes ?tool= (including default). */
export function toolHref(id: ToolId): string {
  return `/tools?tool=${id}`
}

export function parseToolQuery(value: string | null | undefined): ToolId {
  return value && isToolId(value) ? value : DEFAULT_TOOL_ID
}

export { TOOL_IDS, isToolId, type ToolId }
