import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthProvider'
import { calculateTool, ToolInputError, validateTool, type ToolId, type ToolValidationCode, type ToolValidationErrors } from '../../features/toolsCalc'
import { parseToolQuery, TOOL_CATALOG, type ToolCategory } from '../../features/toolsCatalog'
import { cx } from '../../format'
import { Icon } from '../../icons'
import { useI18n, type MessageKey } from '../../i18n'
import { Button, Card, EmptyBox, Field, PageHeader, SelectBox, TextInput } from '../../ui'
import { useBootstrapQuery } from '../../features/queries'
import { buildDiaryDraft, buildTradeDraft, readToolContext } from '../../features/toolsWorkflow'
import { createToolPreset, deleteSavedCalculation, deleteToolPreset, editableInputs, listSavedCalculations, listToolPresets, markToolPresetUsed, persistedInputs, saveCalculation, updateToolPreset, type SavedCalculation, type ToolPreset } from '../../features/toolsPersistence'
import { ApiError } from '../../generated/edge'

type FieldConfig = {
  key: string
  labelKey: MessageKey
  hintKey?: MessageKey
  kind?: 'number' | 'select'
  options?: { value: string; labelKey: MessageKey }[]
}

type ResultKind = 'currency' | 'signedCurrency' | 'percent' | 'signedPercent' | 'number' | 'multiple'
type ResultConfig = { key: string; labelKey: MessageKey; kind: ResultKind; primary?: boolean }

const FIELDS: Record<ToolId, FieldConfig[]> = {
  'position-sizing': [
    { key: 'accountValue', labelKey: 'tools.field.accountValue', hintKey: 'tools.hint.accountValue' },
    { key: 'riskPercent', labelKey: 'tools.field.riskPercent', hintKey: 'tools.hint.riskPercent' },
    { key: 'entryPrice', labelKey: 'tools.field.entryPrice' },
    { key: 'stopPrice', labelKey: 'tools.field.stopPrice' },
  ],
  'risk-reward': [
    { key: 'entryPrice', labelKey: 'tools.field.entryPrice' },
    { key: 'stopPrice', labelKey: 'tools.field.stopPrice' },
    { key: 'targetPrice', labelKey: 'tools.field.targetPrice', hintKey: 'tools.hint.targetPrice' },
  ],
  'average-cost': [
    { key: 'currentQuantity', labelKey: 'tools.field.currentQuantity' },
    { key: 'currentAverageCost', labelKey: 'tools.field.currentAverageCost' },
    { key: 'addedQuantity', labelKey: 'tools.field.addedQuantity' },
    { key: 'addedPrice', labelKey: 'tools.field.addedPrice' },
  ],
  'profit-loss': [
    { key: 'side', labelKey: 'tools.field.side', kind: 'select', options: [{ value: 'long', labelKey: 'tools.option.long' }, { value: 'short', labelKey: 'tools.option.short' }] },
    { key: 'entryPrice', labelKey: 'tools.field.entryPrice' },
    { key: 'exitPrice', labelKey: 'tools.field.exitPrice' },
    { key: 'quantity', labelKey: 'tools.field.quantity' },
    { key: 'entryFee', labelKey: 'tools.field.entryFee', hintKey: 'tools.hint.fees' },
    { key: 'exitFee', labelKey: 'tools.field.exitFee', hintKey: 'tools.hint.fees' },
  ],
}

const RESULTS: Record<ToolId, ResultConfig[]> = {
  'position-sizing': [
    { key: 'quantity', labelKey: 'tools.result.quantity', kind: 'number', primary: true },
    { key: 'plannedLoss', labelKey: 'tools.result.plannedLoss', kind: 'currency' },
    { key: 'riskBudget', labelKey: 'tools.result.riskBudget', kind: 'currency' },
    { key: 'positionValue', labelKey: 'tools.result.positionValue', kind: 'currency' },
    { key: 'perUnitRisk', labelKey: 'tools.result.perUnitRisk', kind: 'currency' },
  ],
  'risk-reward': [
    { key: 'ratio', labelKey: 'tools.result.ratio', kind: 'multiple', primary: true },
    { key: 'riskPerUnit', labelKey: 'tools.result.riskPerUnit', kind: 'currency' },
    { key: 'rewardPerUnit', labelKey: 'tools.result.rewardPerUnit', kind: 'currency' },
    { key: 'breakevenWinRate', labelKey: 'tools.result.breakevenWinRate', kind: 'percent' },
  ],
  'average-cost': [
    { key: 'averageCost', labelKey: 'tools.result.averageCost', kind: 'currency', primary: true },
    { key: 'totalQuantity', labelKey: 'tools.result.totalQuantity', kind: 'number' },
    { key: 'totalCost', labelKey: 'tools.result.totalCost', kind: 'currency' },
    { key: 'averageCostChange', labelKey: 'tools.result.averageCostChange', kind: 'signedPercent' },
  ],
  'profit-loss': [
    { key: 'netPnl', labelKey: 'tools.result.netPnl', kind: 'signedCurrency', primary: true },
    { key: 'returnPercent', labelKey: 'tools.result.returnPercent', kind: 'signedPercent' },
    { key: 'grossPnl', labelKey: 'tools.result.grossPnl', kind: 'signedCurrency' },
    { key: 'totalFees', labelKey: 'tools.result.totalFees', kind: 'currency' },
    { key: 'exitValue', labelKey: 'tools.result.exitValue', kind: 'currency' },
  ],
}

const DEFAULTS: Partial<Record<ToolId, Record<string, string>>> = {
  'profit-loss': { side: 'long', entryFee: '0', exitFee: '0' },
}

const CATEGORY_KEYS: Record<ToolCategory, MessageKey> = {
  risk: 'tools.category.risk',
  position: 'tools.category.position',
}

const FORMULA_KEYS: Record<ToolId, MessageKey> = {
  'position-sizing': 'tools.formula.positionSizing',
  'risk-reward': 'tools.formula.riskReward',
  'average-cost': 'tools.formula.averageCost',
  'profit-loss': 'tools.formula.profitLoss',
}

const ERROR_KEYS: Record<ToolValidationCode, MessageKey> = {
  required: 'tools.validation.required',
  positive: 'tools.validation.positive',
  nonnegative: 'tools.validation.nonnegative',
  riskPercent: 'tools.validation.riskPercent',
  oppositeSides: 'tools.validation.oppositeSides',
}

const CURRENCIES = ['USD', 'TWD', 'HKD', 'JPY', 'EUR'] as const

export function ToolsPage() {
  const { t, format } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const { state: authState } = useAuth()
  const bootstrap = useBootstrapQuery(authState === 'authenticated')
  const requested = searchParams.get('tool')
  const tool = parseToolQuery(requested)
  const selected = useMemo(() => TOOL_CATALOG.find(item => item.id === tool)!, [tool])
  const [currency, setCurrency] = useState<(typeof CURRENCIES)[number]>('USD')
  const [values, setValues] = useState<Record<string, string>>(() => ({ ...DEFAULTS[tool] }))
  const [errors, setErrors] = useState<ToolValidationErrors>({})
  const [answer, setAnswer] = useState<Record<string, number> | null>(null)
  const [context, setContext] = useState(() => readToolContext(location.state, tool))
  const [presets, setPresets] = useState<ToolPreset[]>([])
  const [history, setHistory] = useState<SavedCalculation[]>([])
  const [presetName, setPresetName] = useState('')
  const [selectedPresetId, setSelectedPresetId] = useState('')
  const [note, setNote] = useState('')
  const [workflowStatus, setWorkflowStatus] = useState('')
  const [persistenceError, setPersistenceError] = useState('')
  const [savingResult, setSavingResult] = useState(false)
  const [persistenceLoading, setPersistenceLoading] = useState(false)
  const saveKey = useRef<string | null>(null)

  useEffect(() => {
    if (requested !== tool) setSearchParams({ tool }, { replace: true })
  }, [requested, setSearchParams, tool])

  useEffect(() => {
    const nextContext = readToolContext(location.state, tool)
    setContext(nextContext)
    setValues({ ...DEFAULTS[tool], ...(nextContext?.values ?? {}) })
    if (nextContext?.currency && CURRENCIES.includes(nextContext.currency as typeof CURRENCIES[number])) setCurrency(nextContext.currency as typeof CURRENCIES[number])
    else if (bootstrap.data?.baseCurrency && CURRENCIES.includes(bootstrap.data.baseCurrency as typeof CURRENCIES[number])) setCurrency(bootstrap.data.baseCurrency as typeof CURRENCIES[number])
    setErrors({})
    setAnswer(null)
    setWorkflowStatus('')
    setSelectedPresetId('')
    saveKey.current = null
  }, [bootstrap.data?.baseCurrency, location.state, tool])

  useEffect(() => {
    if (authState !== 'authenticated') { setPresets([]); setHistory([]); setPersistenceLoading(false); return }
    let active = true
    setPersistenceLoading(true)
    Promise.all([listToolPresets(), listSavedCalculations(10)]).then(([presetData, historyData]) => { if (active) { setPresets(presetData.items); setHistory(historyData.items) } }).catch(() => { if (active) setPersistenceError(t('tools.workflow.loadError')) }).finally(() => { if (active) setPersistenceLoading(false) })
    return () => { active = false }
  }, [authState, t])

  function update(key: string, value: string) {
    setValues(current => ({ ...current, [key]: value }))
    setErrors(current => {
      if (!current[key]) return current
      const next = { ...current }
      delete next[key]
      return next
    })
    setAnswer(null)
    setWorkflowStatus('')
    saveKey.current = null
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const nextErrors = validateTool(tool, values)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) {
      setAnswer(null)
      return
    }
    try {
      setAnswer(calculateTool(tool, values))
    } catch (error) {
      setErrors(error instanceof ToolInputError ? error.errors : { form: 'required' })
      setAnswer(null)
    }
  }

  function reset() {
    setValues({ ...DEFAULTS[tool] })
    setErrors({})
    setAnswer(null)
    setContext(null)
    setWorkflowStatus('')
    saveKey.current = null
  }

  function clearContext() {
    setContext(null); setValues({ ...DEFAULTS[tool] }); setAnswer(null); setWorkflowStatus(''); saveKey.current = null
    navigate(`${location.pathname}${location.search}`, { replace: true, state: null })
  }

  async function refreshPersistence() {
    const [presetData, historyData] = await Promise.all([listToolPresets(), listSavedCalculations(10)])
    setPresets(presetData.items); setHistory(historyData.items)
  }

  async function savePreset() {
    const name = presetName.trim()
    if (!name) { setPersistenceError(t('tools.workflow.presetNameRequired')); return }
    setPersistenceError('')
    try {
      await createToolPreset({ name, toolType: tool, inputs: persistedInputs(values), currency })
      setPresetName(''); await refreshPersistence(); setWorkflowStatus(t('tools.workflow.presetSaved'))
    } catch { setPersistenceError(t('tools.workflow.presetError')) }
  }

  async function updatePreset() {
    const preset = presets.find(item => item.id === selectedPresetId)
    if (!preset) return
    try { await updateToolPreset(preset.id, { name: preset.name, toolType: tool, inputs: persistedInputs(values), currency }); await refreshPersistence(); setWorkflowStatus(t('tools.workflow.presetUpdated')) } catch { setPersistenceError(t('tools.workflow.presetError')) }
  }

  async function removePreset() {
    if (!selectedPresetId) return
    try { await deleteToolPreset(selectedPresetId); setSelectedPresetId(''); await refreshPersistence(); setWorkflowStatus(t('tools.workflow.presetDeleted')) } catch { setPersistenceError(t('tools.workflow.presetError')) }
  }

  async function applyPreset(id: string) {
    const preset = presets.find(item => item.id === id); setSelectedPresetId(id)
    if (!preset) return
    setValues(current => ({ ...current, ...editableInputs(preset.inputs) })); if (preset.currency && CURRENCIES.includes(preset.currency as typeof CURRENCIES[number])) setCurrency(preset.currency as typeof CURRENCIES[number])
    setAnswer(null); setWorkflowStatus(t('tools.workflow.presetApplied', { name: preset.name })); saveKey.current = null
    void markToolPresetUsed(id).then(refreshPersistence).catch(() => undefined)
  }

  async function saveResult() {
    if (!answer || savingResult) return
    setSavingResult(true); setPersistenceError('')
    saveKey.current ??= crypto.randomUUID()
    try {
      await saveCalculation({ toolType: tool, inputs: persistedInputs(values), currency, symbol: context?.symbol ?? null, sourceDiaryId: context?.sourceDiaryId ?? null, sourceTransactionId: context?.sourceTransactionId ?? null, note: note.trim() || null }, saveKey.current)
      await refreshPersistence(); setWorkflowStatus(t('tools.workflow.resultSaved'))
    } catch (error) { setPersistenceError(error instanceof ApiError && error.status === 404 ? t('tools.workflow.sourceMissing') : t('tools.workflow.resultError')) } finally { setSavingResult(false) }
  }

  function reopen(item: SavedCalculation) {
    setSearchParams({ tool: item.toolType }); setValues(editableInputs(item.inputs)); setCurrency(item.currency as typeof currency); setAnswer(null); setContext(null); setNote(item.note ?? ''); setWorkflowStatus(t('tools.workflow.reopened')); saveKey.current = null
  }

  function toTradeDraft() {
    if (!answer || !context?.returnTo.startsWith('/diary/')) return
    navigate(context.returnTo, { state: { tradeDraft: buildTradeDraft(tool, values, answer, currency, context.symbol) } })
  }

  function toDiaryDraft() {
    if (!answer) return
    navigate('/diary', { state: { diaryDraft: buildDiaryDraft(tool, values, answer, currency, context?.symbol, context?.diaryDate ?? bootstrap.data?.currentLocalDate ?? '') } })
  }

  function formatResult(config: ResultConfig, value: number, resultCurrency: string = currency): string {
    if (config.kind === 'currency') return format.money(value, resultCurrency)
    if (config.kind === 'signedCurrency') return format.signed(value, resultCurrency)
    if (config.kind === 'percent') return format.pct(value, 2).replace(/^\+/, '')
    if (config.kind === 'signedPercent') return format.pct(value, 2)
    if (config.kind === 'multiple') return `${format.number(value, { maximumFractionDigits: 4 })}×`
    return format.number(value, { maximumFractionDigits: 6 })
  }

  return (
    <div className="tools-page">
      <PageHeader title={t('tools.title')} subtitle={t('tools.subtitle')} />
      <p className="tools-page__privacy">{t('tools.localNote')}</p>

      <nav className="tools-catalog" aria-label={t('tools.catalogue')}>
        {(['risk', 'position'] as ToolCategory[]).map(category => (
          <section key={category} className="tools-catalog__group" aria-labelledby={`tools-${category}`}>
            <h2 id={`tools-${category}`}>{t(CATEGORY_KEYS[category])}</h2>
            <div className="tools-catalog__grid">
              {TOOL_CATALOG.filter(item => item.category === category).map(item => (
                <Link key={item.id} to={item.href} className={cx('tools-catalog__card', item.id === tool && 'is-active')} aria-current={item.id === tool ? 'page' : undefined}>
                  <Icon name={item.icon} size={18} />
                  <span><strong>{t(item.titleKey)}</strong><small>{t(item.bodyKey)}</small></span>
                  <span className="tools-catalog__action">{t(item.actionKey)}</span>
                </Link>
              ))}
            </div>
          </section>
        ))}
      </nav>

      <section className="tool-workspace" aria-labelledby="active-tool-title">
        <div className="tool-workspace__head">
          <span className="tool-workspace__icon"><Icon name={selected.icon} size={20} /></span>
          <div><h2 id="active-tool-title">{t(selected.titleKey)}</h2><p>{t(selected.bodyKey)}</p></div>
        </div>
        {context ? <div className="tool-context" role="status"><span>{t('tools.workflow.prefilled', { source: context.label })}</span><div><Link to={context.returnTo}>{t('tools.workflow.return')}</Link><Button variant="ghost" onClick={clearContext}>{t('tools.workflow.clearContext')}</Button></div></div> : null}
        {authState === 'authenticated' ? <Card as="section" className="tool-presets"><h3>{t('tools.workflow.presets')}</h3><div className="tool-inline-actions"><SelectBox aria-label={t('tools.workflow.presets')} value={selectedPresetId} onChange={event => { void applyPreset(event.target.value) }}><option value="">{t('tools.workflow.choosePreset')}</option>{presets.filter(item => item.toolType === tool).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</SelectBox><TextInput aria-label={t('tools.workflow.presetName')} maxLength={80} value={presetName} onChange={event => setPresetName(event.target.value)} placeholder={t('tools.workflow.presetName')} /><Button onClick={() => { void savePreset() }}>{t('tools.workflow.savePreset')}</Button>{selectedPresetId ? <><Button onClick={() => { void updatePreset() }}>{t('tools.workflow.updatePreset')}</Button><Button variant="danger" onClick={() => { void removePreset() }}>{t('common.delete')}</Button></> : null}</div></Card> : null}
        <div className="tool-workspace__grid">
          <Card as="section" className="tool-form-card">
            <form onSubmit={submit} noValidate>
              <div className="tool-form-card__header">
                <h3>{t('tools.inputs')}</h3>
                <Field label={t('tools.currency')} className="tool-currency">
                  <SelectBox value={currency} onChange={event => setCurrency(event.target.value as typeof currency)}>
                    {CURRENCIES.map(code => <option key={code} value={code}>{code}</option>)}
                  </SelectBox>
                </Field>
              </div>
              <div className="tool-fields">
                {FIELDS[tool].map(field => (
                  <Field key={field.key} label={t(field.labelKey)} hint={field.hintKey ? t(field.hintKey) : undefined} error={errors[field.key] ? t(ERROR_KEYS[errors[field.key]]) : undefined}>
                    {field.kind === 'select' ? (
                      <SelectBox aria-label={t(field.labelKey)} value={values[field.key] ?? ''} onChange={event => update(field.key, event.target.value)} aria-invalid={Boolean(errors[field.key])}>
                        {field.options?.map(option => <option key={option.value} value={option.value}>{t(option.labelKey)}</option>)}
                      </SelectBox>
                    ) : (
                      <TextInput aria-label={t(field.labelKey)} type="number" inputMode="decimal" step="any" min="0" value={values[field.key] ?? ''} onChange={event => update(field.key, event.target.value)} aria-invalid={Boolean(errors[field.key])} />
                    )}
                  </Field>
                ))}
              </div>
              <div className="form-actions tool-form-actions">
                <Button type="reset" onClick={reset}>{t('tools.reset')}</Button>
                <Button variant="primary" type="submit">{t(selected.actionKey)}</Button>
              </div>
            </form>
          </Card>

          <Card as="section" className="tool-result-card" aria-live="polite" aria-labelledby="tool-result-title">
            <h3 id="tool-result-title">{t('tools.result')}</h3>
            {answer ? (
              <><div className="tool-results">
                {RESULTS[tool].map(config => {
                  const value = answer[config.key]
                  const semantic = (config.kind === 'signedCurrency' || config.kind === 'signedPercent') && value !== 0
                    ? value > 0 ? 'is-gain' : 'is-loss'
                    : undefined
                  return <div key={config.key} className={cx('tool-result', config.primary && 'tool-result--primary')}><span>{t(config.labelKey)}</span><strong className={semantic}>{formatResult(config, value)}</strong></div>
                })}
              </div><div className="tool-result-actions">{authState === 'authenticated' ? <><TextInput aria-label={t('tools.workflow.note')} value={note} maxLength={1000} onChange={event => setNote(event.target.value)} placeholder={t('tools.workflow.note')} /><Button loading={savingResult} onClick={() => { void saveResult() }}>{t('tools.workflow.saveResult')}</Button></> : null}{(tool === 'position-sizing' || tool === 'risk-reward') && context?.returnTo.startsWith('/diary/') ? <Button variant="primary" onClick={toTradeDraft}>{t('tools.workflow.tradeDraft')}</Button> : null}<Button variant="subtle" onClick={toDiaryDraft}>{t('tools.workflow.diaryDraft')}</Button></div></>
            ) : (
              <EmptyBox icon="compass" title={t('tools.empty.title')} hint={t('tools.empty.body')} dense />
            )}
          </Card>
        </div>
        <Card as="section" className="tool-formula">
          <h3>{t('tools.assumptions')}</h3>
          <p>{t(FORMULA_KEYS[tool])}</p>
        </Card>
        {workflowStatus ? <p className="tool-workflow-status" role="status">{workflowStatus}</p> : null}
        {persistenceError ? <p className="form-error" role="alert">{persistenceError}</p> : null}
        {authState === 'authenticated' ? <Card as="section" className="tool-history"><h3>{t('tools.workflow.recent')}</h3>{persistenceLoading ? <p className="is-muted">{t('tools.workflow.loading')}</p> : history.length === 0 ? <EmptyBox title={t('tools.workflow.noHistory')} dense /> : <ul>{history.map(item => { const primary = RESULTS[item.toolType].find(result => result.primary)!; return <li key={item.id}><div><strong>{t(TOOL_CATALOG.find(entry => entry.id === item.toolType)!.titleKey)}</strong><span>{formatResult(primary, item.output[primary.key], item.currency)}</span><small>{new Date(item.createdAt).toLocaleString()}</small></div><div><Button variant="subtle" onClick={() => reopen(item)}>{t('tools.workflow.reopen')}</Button><Button variant="danger" onClick={() => { void deleteSavedCalculation(item.id).then(refreshPersistence).catch(() => setPersistenceError(t('tools.workflow.deleteError'))) }}>{t('common.delete')}</Button></div></li>})}</ul>}</Card> : null}
      </section>
    </div>
  )
}
