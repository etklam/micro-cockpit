import * as G from '../generated/edge'
import type { ToolId } from './toolsCalc'

export type ToolPreset = { id: string; name: string; toolType: ToolId; inputs: Record<string, number | string>; currency: string | null; lastUsedAt: string | null; createdAt: string; updatedAt: string }
export type SavedCalculation = { id: string; toolType: ToolId; schemaVersion: number; inputs: Record<string, number | string>; output: Record<string, number>; currency: string; symbol: string | null; sourceDiaryId: string | null; sourceTransactionId: string | null; note: string | null; createdAt: string }
type Collection<T> = { items: T[] }

export const listToolPresets = () => G.request<Collection<ToolPreset>>('/api/app/tool-presets')
export const createToolPreset = (body: Omit<ToolPreset, 'id' | 'lastUsedAt' | 'createdAt' | 'updatedAt'>) => G.request<ToolPreset>('/api/app/tool-presets', { method: 'POST', body: JSON.stringify(body) })
export const updateToolPreset = (id: string, body: Omit<ToolPreset, 'id' | 'lastUsedAt' | 'createdAt' | 'updatedAt'>) => G.request<void>(`/api/app/tool-presets/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(body) })
export const markToolPresetUsed = (id: string) => G.request<void>(`/api/app/tool-presets/${encodeURIComponent(id)}/use`, { method: 'POST' })
export const deleteToolPreset = (id: string) => G.request<void>(`/api/app/tool-presets/${encodeURIComponent(id)}`, { method: 'DELETE' })
export const listSavedCalculations = (limit = 10) => G.request<Collection<SavedCalculation>>(`/api/app/saved-calculations?limit=${limit}`)
export const saveCalculation = (body: { toolType: ToolId; inputs: Record<string, number | string>; currency: string; symbol: string | null; sourceDiaryId: string | null; sourceTransactionId: string | null; note: string | null }, key: string) => G.request<SavedCalculation>('/api/app/saved-calculations', { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify(body) })
export const deleteSavedCalculation = (id: string) => G.request<void>(`/api/app/saved-calculations/${encodeURIComponent(id)}`, { method: 'DELETE' })

/** Converts editable strings to the versioned JSON input shape accepted by Tool service. */
export function persistedInputs(values: Record<string, string>): Record<string, number | string> {
  return Object.fromEntries(Object.entries(values).filter(([, value]) => value !== '').map(([key, value]) => [key, key === 'side' ? value : Number(value)]))
}

/** Restores a preset or snapshot into form-safe strings without triggering calculation. */
export function editableInputs(values: Record<string, number | string>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).map(([key, value]) => [key, String(value)]))
}
