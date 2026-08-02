import { useEffect, useState, type FormEvent } from 'react'
import type { Expectation, ExpectationReviewWrite, ReasoningLabelKind } from '../../features/api'
import {
  useCreateDisciplineMutation,
  useCreateReasoningLabelMutation,
  useExpectationReviewQuery,
  useReasoningLabelsQuery,
  useSaveExpectationReviewMutation,
} from '../../features/queries'
import { Badge, Button, Card, Field, SelectBox, TextArea, TextInput } from '../../ui'
import { useI18n } from '../../i18n'

type ExpectationSelfReviewFormProps = {
  expectation: Expectation
  onClose: () => void
}

export function ExpectationSelfReviewForm({ expectation, onClose }: ExpectationSelfReviewFormProps) {
  const { t, format } = useI18n()
  const existing = useExpectationReviewQuery(expectation.id)
  const labels = useReasoningLabelsQuery()
  const save = useSaveExpectationReviewMutation(expectation.id)
  const createLabel = useCreateReasoningLabelMutation()
  const createPrinciple = useCreateDisciplineMutation()
  const [outcome, setOutcome] = useState<ExpectationReviewWrite['outcome']>('confirmed')
  const [quality, setQuality] = useState<ExpectationReviewWrite['reasoningQuality']>('sound')
  const [explanation, setExplanation] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [customKind, setCustomKind] = useState<ReasoningLabelKind>('issue')
  const [customName, setCustomName] = useState('')
  const [principle, setPrinciple] = useState('')
  const [reviewSaved, setReviewSaved] = useState(false)
  const [principleSaved, setPrincipleSaved] = useState(false)
  const [formError, setFormError] = useState('')
  const [principleError, setPrincipleError] = useState('')

  useEffect(() => {
    if (!existing.data) return
    setOutcome(existing.data.outcome)
    setQuality(existing.data.reasoningQuality)
    setExplanation(existing.data.explanation ?? '')
    setSelected(existing.data.labels.map(label => label.id ?? label.key))
  }, [existing.data])

  const needsExplanation = outcome === 'partially_confirmed' || outcome === 'indeterminate'
  const toggle = (key: string) => setSelected(current => current.includes(key) ? current.filter(item => item !== key) : [...current, key])

  async function savePrinciple() {
    if (!principle.trim()) return
    setPrincipleError('')
    try {
      await createPrinciple.mutateAsync(principle.trim())
      setPrincipleSaved(true)
    } catch {
      setPrincipleError(t('review.selfReview.principleError'))
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError('')
    if (needsExplanation && !explanation.trim()) {
      setFormError(t('today.review.explanationRequired'))
      return
    }
    const chosen = (labels.data ?? []).filter(label => selected.includes(label.id ?? label.key))
    try {
      await save.mutateAsync({
        outcome,
        reasoningQuality: quality,
        explanation: explanation.trim() || null,
        systemIssueKeys: chosen.filter(label => label.isSystem && label.kind === 'issue').map(label => label.key),
        systemStrengthKeys: chosen.filter(label => label.isSystem && label.kind === 'strength').map(label => label.key),
        customLabelIds: chosen.filter(label => !label.isSystem).map(label => label.id!).filter(Boolean),
      })
      setReviewSaved(true)
      if (principle.trim() && !principleSaved) await savePrinciple()
    } catch {
      setFormError(t('review.selfReview.saveError'))
    }
  }

  async function addCustomLabel() {
    if (!customName.trim()) return
    setFormError('')
    try {
      const created = await createLabel.mutateAsync({ kind: customKind, name: customName.trim() })
      setSelected(current => [...current, created.id!].filter(Boolean))
      setCustomName('')
    } catch {
      setFormError(t('review.selfReview.labelError'))
    }
  }

  return <Card as="section" className="review-self-review" aria-labelledby="self-review-title">
    <header className="review-self-review__header">
      <div>
        <p className="eyebrow">{t('review.selfReview.eyebrow')}</p>
        <h2 id="self-review-title">{t('review.selfReview.title')}</h2>
        <p className="form-hint">{t('review.selfReview.subtitle')}</p>
      </div>
      <Badge tone={reviewSaved ? 'gain' : 'primary'}>{reviewSaved ? t('review.selfReview.saved') : t('review.status.unreviewed')}</Badge>
    </header>

    <section className="review-context" aria-labelledby="review-context-title">
      <div className="review-context__heading">
        <h3 id="review-context-title">{t('review.selfReview.contextTitle')}</h3>
        <span className="review-context__readonly">{t('review.selfReview.readOnly')}</span>
      </div>
      <p className="review-context__expectation">{expectation.expectedBehavior}</p>
      <dl className="review-context__facts">
        <div><dt>{t('today.expectations.invalidation')}</dt><dd>{expectation.invalidationCondition}</dd></div>
        <div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div>
        <div><dt>{t('today.expectations.market')}</dt><dd>{expectation.market}</dd></div>
        <div><dt>{t('today.expectations.deadline')}</dt><dd><time dateTime={expectation.deadline}>{format.dateTime(expectation.deadline)}</time></dd></div>
        <div><dt>{t('review.relationship')}</dt><dd><time dateTime={expectation.journalDay}>{format.date(expectation.journalDay)}</time></dd></div>
        <div><dt>{t('review.created')}</dt><dd><time dateTime={expectation.createdAt}>{format.dateTime(expectation.createdAt)}</time></dd></div>
      </dl>
      <p className="review-context__limitation">{t('review.selfReview.snapshotLimitation')}</p>
    </section>

    {existing.isLoading ? <p className="form-hint" role="status">{t('review.selfReview.loadingExisting')}</p> : null}
    {existing.isError ? <p className="form-error" role="alert">{t('review.selfReview.reviewLoadError')}</p> : null}

    <form className="review-self-review__form" onSubmit={submit} noValidate>
      <div className="review-self-review__fields">
        <Field label={t('today.review.outcome')}>
          <SelectBox value={outcome} onChange={event => { setOutcome(event.target.value as ExpectationReviewWrite['outcome']); setFormError('') }} disabled={reviewSaved}>
            <option value="confirmed">{t('today.review.outcome.confirmed')}</option>
            <option value="partially_confirmed">{t('today.review.outcome.partiallyConfirmed')}</option>
            <option value="invalidated">{t('today.review.outcome.invalidated')}</option>
            <option value="indeterminate">{t('today.review.outcome.indeterminate')}</option>
          </SelectBox>
        </Field>
        <Field label={t('today.review.quality')}>
          <SelectBox value={quality} onChange={event => setQuality(event.target.value as ExpectationReviewWrite['reasoningQuality'])} disabled={reviewSaved}>
            <option value="sound">{t('today.review.quality.sound')}</option>
            <option value="mixed">{t('today.review.quality.mixed')}</option>
            <option value="weak">{t('today.review.quality.weak')}</option>
          </SelectBox>
        </Field>
      </div>
      <Field label={`${t('today.review.explanation')}${needsExplanation ? '' : ` (${t('common.optional')})`}`}>
        <TextArea value={explanation} onChange={event => { setExplanation(event.target.value); setFormError('') }} disabled={reviewSaved} aria-invalid={!!formError && needsExplanation && !explanation.trim()} />
      </Field>
      {needsExplanation && !explanation.trim() && formError ? <p className="form-error" role="alert">{formError}</p> : null}

      <div className="review-self-review__labels">
        {(['issue', 'strength'] as const).map(kind => <fieldset className="review-tags" key={kind}>
          <legend>{kind === 'issue' ? t('today.review.issues') : t('today.review.strengths')}</legend>
          {(labels.data ?? []).filter(label => label.kind === kind).map(label => <label key={label.id ?? label.key}>
            <input type="checkbox" checked={selected.includes(label.id ?? label.key)} onChange={() => toggle(label.id ?? label.key)} disabled={reviewSaved} /> {label.isSystem ? t(`reasoning.${label.key}` as Parameters<typeof t>[0]) : label.name}
          </label>)}
        </fieldset>)}
      </div>

      <div className="review-self-review__custom-label">
        <h3>{t('today.review.customLabel')}</h3>
        <div className="form-row">
          <Field label={t('review.selfReview.labelKind')}>
            <SelectBox value={customKind} onChange={event => setCustomKind(event.target.value as ReasoningLabelKind)} disabled={reviewSaved}>
              <option value="issue">{t('today.review.issues')}</option>
              <option value="strength">{t('today.review.strengths')}</option>
            </SelectBox>
          </Field>
          <Field label={t('review.selfReview.labelName')}>
            <TextInput value={customName} onChange={event => setCustomName(event.target.value)} disabled={reviewSaved} />
          </Field>
          <Button variant="ghost" size="sm" disabled={reviewSaved || !customName.trim()} loading={createLabel.isPending} onClick={addCustomLabel}>{t('common.add')}</Button>
        </div>
      </div>

      <div className="review-self-review__principle">
        <Field label={t('review.selfReview.principleOptional')} hint={t('review.selfReview.principleHint')}>
          <TextInput value={principle} onChange={event => { setPrinciple(event.target.value); setPrincipleError('') }} disabled={reviewSaved} />
        </Field>
        {principleError ? <p className="form-error" role="alert">{principleError}</p> : null}
        {principleError ? <Button variant="ghost" size="sm" onClick={() => { void savePrinciple() }} loading={createPrinciple.isPending}>{t('review.selfReview.retryPrinciple')}</Button> : null}
      </div>

      {formError && !needsExplanation ? <p className="form-error" role="alert">{formError}</p> : null}
      {reviewSaved ? <p className="review-self-review__success" role="status">{principle && !principleSaved ? t('review.selfReview.reviewSavedPrinciplePending') : t('review.selfReview.savedHint')}</p> : null}
      <div className="form-actions">
        <Button variant="primary" type="submit" loading={save.isPending || createPrinciple.isPending} disabled={reviewSaved}>{t('common.save')}</Button>
        <Button variant="ghost" type="button" onClick={onClose}>{reviewSaved ? t('review.selfReview.backToQueue') : t('common.cancel')}</Button>
      </div>
    </form>
  </Card>
}
