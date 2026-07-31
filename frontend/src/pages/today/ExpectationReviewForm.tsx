import { useEffect, useState } from 'react'
import type { ExpectationReviewWrite, ReasoningLabelKind } from '../../features/api'
import {
  useCreateReasoningLabelMutation,
  useExpectationReviewQuery,
  useReasoningLabelsQuery,
  useSaveExpectationReviewMutation,
} from '../../features/queries'
import { Button, Card, Field, SelectBox, TextArea, TextInput } from '../../ui'
import { useI18n } from '../../i18n'

export function ExpectationReviewForm({ expectationId, onClose }: { expectationId: string; onClose: () => void }) {
  const { t } = useI18n()
  const existing = useExpectationReviewQuery(expectationId)
  const labels = useReasoningLabelsQuery()
  const save = useSaveExpectationReviewMutation(expectationId)
  const createLabel = useCreateReasoningLabelMutation()
  const [outcome, setOutcome] = useState<ExpectationReviewWrite['outcome']>('confirmed')
  const [quality, setQuality] = useState<ExpectationReviewWrite['reasoningQuality']>('sound')
  const [explanation, setExplanation] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [customKind, setCustomKind] = useState<ReasoningLabelKind>('issue')
  const [customName, setCustomName] = useState('')

  useEffect(() => {
    if (!existing.data) return
    setOutcome(existing.data.outcome)
    setQuality(existing.data.reasoningQuality)
    setExplanation(existing.data.explanation ?? '')
    setSelected(existing.data.labels.map(label => label.id ?? label.key))
  }, [existing.data])

  const needsExplanation = outcome === 'partially_confirmed' || outcome === 'indeterminate'
  const toggle = (key: string) => setSelected(current => current.includes(key) ? current.filter(item => item !== key) : [...current, key])

  async function submit() {
    if (needsExplanation && !explanation.trim()) return
    const chosen = (labels.data ?? []).filter(label => selected.includes(label.id ?? label.key))
    await save.mutateAsync({
      outcome,
      reasoningQuality: quality,
      explanation: explanation.trim() || null,
      systemIssueKeys: chosen.filter(label => label.isSystem && label.kind === 'issue').map(label => label.key),
      systemStrengthKeys: chosen.filter(label => label.isSystem && label.kind === 'strength').map(label => label.key),
      customLabelIds: chosen.filter(label => !label.isSystem).map(label => label.id!),
    })
    onClose()
  }

  async function addCustomLabel() {
    if (!customName.trim()) return
    const created = await createLabel.mutateAsync({ kind: customKind, name: customName.trim() })
    setSelected(current => [...current, created.id!])
    setCustomName('')
  }

  return <Card as="section" className="stack">
    <h2>{t('today.review.title')}</h2>
    <Field label={t('today.review.outcome')}><SelectBox value={outcome} onChange={event => setOutcome(event.target.value as ExpectationReviewWrite['outcome'])}>
      <option value="confirmed">{t('today.review.outcome.confirmed')}</option>
      <option value="partially_confirmed">{t('today.review.outcome.partiallyConfirmed')}</option>
      <option value="invalidated">{t('today.review.outcome.invalidated')}</option>
      <option value="indeterminate">{t('today.review.outcome.indeterminate')}</option>
    </SelectBox></Field>
    <Field label={t('today.review.quality')}><SelectBox value={quality} onChange={event => setQuality(event.target.value as ExpectationReviewWrite['reasoningQuality'])}>
      <option value="sound">{t('today.review.quality.sound')}</option>
      <option value="mixed">{t('today.review.quality.mixed')}</option>
      <option value="weak">{t('today.review.quality.weak')}</option>
    </SelectBox></Field>
    <Field label={`${t('today.review.explanation')}${needsExplanation ? '' : ` (${t('common.optional')})`}`}>
      <TextArea value={explanation} onChange={event => setExplanation(event.target.value)} />
    </Field>
    {needsExplanation && !explanation.trim() ? <p role="alert">{t('today.review.explanationRequired')}</p> : null}
    {(['issue', 'strength'] as const).map(kind => <fieldset className="stack" key={kind}>
      <legend>{kind === 'issue' ? t('today.review.issues') : t('today.review.strengths')}</legend>
      {(labels.data ?? []).filter(label => label.kind === kind).map(label => <label key={label.id ?? label.key}>
        <input type="checkbox" checked={selected.includes(label.id ?? label.key)} onChange={() => toggle(label.id ?? label.key)} /> {label.isSystem ? t(`reasoning.${label.key}` as Parameters<typeof t>[0]) : label.name}
      </label>)}
    </fieldset>)}
    <div className="stack">
      <h3>{t('today.review.customLabel')}</h3>
      <SelectBox value={customKind} onChange={event => setCustomKind(event.target.value as ReasoningLabelKind)}>
        <option value="issue">{t('today.review.issues')}</option>
        <option value="strength">{t('today.review.strengths')}</option>
      </SelectBox>
      <TextInput value={customName} onChange={event => setCustomName(event.target.value)} />
      <Button variant="ghost" size="sm" disabled={!customName.trim()} loading={createLabel.isPending} onClick={addCustomLabel}>{t('common.add')}</Button>
    </div>
    <div className="form-actions">
      <Button variant="primary" loading={save.isPending} disabled={needsExplanation && !explanation.trim()} onClick={submit}>{t('common.save')}</Button>
      <Button variant="ghost" onClick={onClose}>{t('common.cancel')}</Button>
    </div>
  </Card>
}
