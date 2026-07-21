import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { calculateTool, ToolInputError, validateTool, type ToolId, type ToolValidationCode, type ToolValidationErrors } from '../../features/toolsCalc'
import { parseToolQuery, TOOL_CATALOG, type ToolCategory } from '../../features/toolsCatalog'
import { cx } from '../../format'
import { Icon } from '../../icons'
import { useI18n, type MessageKey } from '../../i18n'
import { Button, Card, EmptyBox, Field, PageHeader, SelectBox, TextInput } from '../../ui'

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
  const requested = searchParams.get('tool')
  const tool = parseToolQuery(requested)
  const selected = useMemo(() => TOOL_CATALOG.find(item => item.id === tool)!, [tool])
  const [currency, setCurrency] = useState<(typeof CURRENCIES)[number]>('USD')
  const [values, setValues] = useState<Record<string, string>>(() => ({ ...DEFAULTS[tool] }))
  const [errors, setErrors] = useState<ToolValidationErrors>({})
  const [answer, setAnswer] = useState<Record<string, number> | null>(null)

  useEffect(() => {
    if (requested !== tool) setSearchParams({ tool }, { replace: true })
  }, [requested, setSearchParams, tool])

  useEffect(() => {
    setValues({ ...DEFAULTS[tool] })
    setErrors({})
    setAnswer(null)
  }, [tool])

  function update(key: string, value: string) {
    setValues(current => ({ ...current, [key]: value }))
    setErrors(current => {
      if (!current[key]) return current
      const next = { ...current }
      delete next[key]
      return next
    })
    setAnswer(null)
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
  }

  function formatResult(config: ResultConfig, value: number): string {
    if (config.kind === 'currency') return format.money(value, currency)
    if (config.kind === 'signedCurrency') return format.signed(value, currency)
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
              <div className="tool-results">
                {RESULTS[tool].map(config => {
                  const value = answer[config.key]
                  const semantic = (config.kind === 'signedCurrency' || config.kind === 'signedPercent') && value !== 0
                    ? value > 0 ? 'is-gain' : 'is-loss'
                    : undefined
                  return <div key={config.key} className={cx('tool-result', config.primary && 'tool-result--primary')}><span>{t(config.labelKey)}</span><strong className={semantic}>{formatResult(config, value)}</strong></div>
                })}
              </div>
            ) : (
              <EmptyBox icon="compass" title={t('tools.empty.title')} hint={t('tools.empty.body')} dense />
            )}
          </Card>
        </div>
        <Card as="section" className="tool-formula">
          <h3>{t('tools.assumptions')}</h3>
          <p>{t(FORMULA_KEYS[tool])}</p>
        </Card>
      </section>
    </div>
  )
}
