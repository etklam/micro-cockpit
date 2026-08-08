import { useEffect, useState, type FormEvent } from 'react'
import type { Expectation, ExpectationReviewContext, ExpectationReviewWrite, ReasoningLabelKind } from '../../features/api'
import {
  useCreateReasoningLabelMutation,
  useExpectationReviewContextQuery,
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

function RetainedDecisionContext({ decisions }: { decisions: ExpectationReviewContext['actionDecisions'] }) {
  const { t, format } = useI18n()
  if (!decisions.length) return null
  return <div className="review-context__retained-block">
    <h4>{t('today.decisions.title')}</h4>
    {decisions.map(({ decision, trades }) => <article key={decision.id}>
      <p><strong>{{
        trade: t('today.decisions.intent.trade'),
        continue_observing: t('today.decisions.intent.continueObserving'),
        avoid_trade: t('today.decisions.intent.avoidTrade'),
      }[decision.intent]}</strong> · <time dateTime={decision.recordedAt}>{format.dateTime(decision.recordedAt)}</time></p>
      <p>{decision.reason}</p>
      {decision.executionReview ? <p>{t('today.decisions.execution')}: {{
        followed: t('today.decisions.execution.followed'),
        partially_followed: t('today.decisions.execution.partiallyFollowed'),
        deviated: t('today.decisions.execution.deviated'),
      }[decision.executionReview]}</p> : null}
      {trades.length ? <ul className="review-context__trades">{trades.map(trade => <li key={trade.id}>{t('today.decisions.tradeSummary', {
        side: trade.side, quantity: trade.quantity, symbol: trade.symbol, price: trade.price, currency: trade.currency,
      })}</li>)}</ul> : null}
    </article>)}
  </div>
}

export function ExpectationSelfReviewForm({ expectation, onClose }: ExpectationSelfReviewFormProps) {
  const { t, format } = useI18n()
  const existing = useExpectationReviewQuery(expectation.id)
  const context = useExpectationReviewContextQuery(expectation.id)
  const labels = useReasoningLabelsQuery()
  const save = useSaveExpectationReviewMutation(expectation.id)
  const createLabel = useCreateReasoningLabelMutation()
  const [outcome, setOutcome] = useState<ExpectationReviewWrite['outcome']>('confirmed')
  const [quality, setQuality] = useState<ExpectationReviewWrite['reasoningQuality']>('sound')
  const [explanation, setExplanation] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [customKind, setCustomKind] = useState<ReasoningLabelKind>('issue')
  const [customName, setCustomName] = useState('')
  const [reviewSaved, setReviewSaved] = useState(false)
  const [formError, setFormError] = useState('')
  const retainedUpdate = context.data?.observationUpdate

  useEffect(() => {
    if (!existing.data) return
    setOutcome(existing.data.outcome)
    setQuality(existing.data.reasoningQuality)
    setExplanation(existing.data.explanation ?? '')
    setSelected(existing.data.labels.map(label => label.id ?? label.key))
  }, [existing.data])

  const needsExplanation = outcome === 'partially_confirmed' || outcome === 'indeterminate'
  const toggle = (key: string) => setSelected(current => current.includes(key) ? current.filter(item => item !== key) : [...current, key])

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
      {retainedUpdate ? <div className="review-context__retained-block">
        <h4>{t('review.selfReview.observation')}</h4>
        <p>{retainedUpdate.content}</p>
        {retainedUpdate.signal ? <p><strong>{t('today.observations.signal')}:</strong> {retainedUpdate.signal}</p> : null}
        {retainedUpdate.interpretation ? <p><strong>{t('today.observations.interpretation')}:</strong> {retainedUpdate.interpretation}</p> : null}
        {retainedUpdate.evidence ? <p><strong>{t('review.selfReview.supportingEvidence')}:</strong> <a href={retainedUpdate.evidence.url} target="_blank" rel="noreferrer">{retainedUpdate.evidence.title || retainedUpdate.evidence.url}</a>{retainedUpdate.evidence.quote ? <> · {retainedUpdate.evidence.quote}</> : null}</p> : null}
      </div> : context.isLoading ? <p className="form-hint" role="status">{t('review.selfReview.loadingContext')}</p> : null}
      {context.isError ? <p className="form-error" role="alert">{t('review.selfReview.contextLoadError')}</p> : null}
      {context.data?.availability === 'partial' ? <p className="review-context__limitation" role="status">{t('review.selfReview.partialContext')}</p> : null}
      <RetainedDecisionContext decisions={context.data?.actionDecisions ?? []} />
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
      <p className="form-hint">{t('review.selfReview.outcomeQualitySeparation')}</p>
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

      {formError && !needsExplanation ? <p className="form-error" role="alert">{formError}</p> : null}
      {reviewSaved ? <p className="review-self-review__success" role="status">{t('review.selfReview.savedHint')}</p> : null}
      <div className="form-actions">
        <Button variant="primary" type="submit" loading={save.isPending} disabled={reviewSaved}>{t('common.save')}</Button>
        <Button variant="ghost" type="button" onClick={onClose}>{reviewSaved ? t('review.selfReview.backToQueue') : t('common.cancel')}</Button>
      </div>
    </form>
  </Card>
}
