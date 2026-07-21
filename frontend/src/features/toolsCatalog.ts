import { TOOL_IDS, type ToolId, isToolId } from './toolsCalc'
import type { MessageKey } from '../i18n'
import type { IconName } from '../icons'

export type ToolCategory = 'risk' | 'position'

export type ToolCatalogEntry = {
  id: ToolId
  order: number
  public: true
  category: ToolCategory
  icon: IconName
  route: '/tools'
  query: ToolId
  labelKey: MessageKey
  titleKey: MessageKey
  bodyKey: MessageKey
  actionKey: MessageKey
  href: string
}

export const DEFAULT_TOOL_ID: ToolId = 'position-sizing'

const DETAILS: Record<ToolId, Pick<ToolCatalogEntry, 'category' | 'icon' | 'labelKey' | 'titleKey' | 'bodyKey' | 'actionKey'>> = {
  'position-sizing': { category: 'risk', icon: 'layers', labelKey: 'landing.tool.positionSizing.title', titleKey: 'landing.tool.positionSizing.title', bodyKey: 'landing.tool.positionSizing.body', actionKey: 'tools.action.positionSizing' },
  'risk-reward': { category: 'risk', icon: 'compass', labelKey: 'landing.tool.riskReward.title', titleKey: 'landing.tool.riskReward.title', bodyKey: 'landing.tool.riskReward.body', actionKey: 'tools.action.riskReward' },
  'average-cost': { category: 'position', icon: 'plus', labelKey: 'landing.tool.averageCost.title', titleKey: 'landing.tool.averageCost.title', bodyKey: 'landing.tool.averageCost.body', actionKey: 'tools.action.averageCost' },
  'profit-loss': { category: 'position', icon: 'arrow', labelKey: 'landing.tool.profitLoss.title', titleKey: 'landing.tool.profitLoss.title', bodyKey: 'landing.tool.profitLoss.body', actionKey: 'tools.action.profitLoss' },
}

export const TOOL_CATALOG: readonly ToolCatalogEntry[] = TOOL_IDS.map((id, index) => ({
  id,
  order: index + 1,
  public: true as const,
  ...DETAILS[id],
  route: '/tools' as const,
  query: id,
  href: toolHref(id),
}))

export function toolHref(id: ToolId): string {
  return `/tools?tool=${id}`
}

export function parseToolQuery(value: string | null | undefined): ToolId {
  return value && isToolId(value) ? value : DEFAULT_TOOL_ID
}

export { TOOL_IDS, isToolId, type ToolId }
