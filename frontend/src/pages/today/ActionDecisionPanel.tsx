import { useState } from 'react'
import type { ActionDecision, ActionDecisionIntent, ExecutionReview, Expectation } from '../../features/api'
import {
  useActionDecisionsQuery,
  useCreateActionDecisionMutation,
  useDeleteActionDecisionMutation,
  useUpdateActionDecisionMutation,
} from '../../features/queries'
import { Button, Field, SelectBox, TextArea } from '../../ui'
import { useI18n } from '../../i18n'
import { TradeEvidenceForm, TradeEvidenceList } from './TradeEvidence'

export function ActionDecisionPanel({ updateId, expectations }: { updateId: string; expectations: Expectation[] }) {
  const { t } = useI18n()
  const decisions = useActionDecisionsQuery(updateId)
  const create = useCreateActionDecisionMutation(updateId)
  const update = useUpdateActionDecisionMutation(updateId)
  const remove = useDeleteActionDecisionMutation(updateId)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [intent, setIntent] = useState<ActionDecisionIntent>('continue_observing')
  const [reason, setReason] = useState('')
  const [expectationId, setExpectationId] = useState('')
  const [executionReview, setExecutionReview] = useState<'' | NonNullable<ExecutionReview>>('')
  const [honestyReminder, setHonestyReminder] = useState(false)
  const [tradeDecisionId, setTradeDecisionId] = useState<string | null>(null)

  function begin(item?: ActionDecision) {
    setEditing(item?.id ?? 'new')
    setIntent(item?.intent ?? 'continue_observing')
    setReason(item?.reason ?? '')
    setExpectationId(item?.expectationId ?? '')
    setExecutionReview(item?.executionReview ?? '')
    setHonestyReminder(!!item)
  }

  async function save() {
    if (!reason.trim()) return
    const body = { intent, reason: reason.trim(), expectationId: expectationId || null, executionReview: executionReview || null }
    if (editing === 'new') await create.mutateAsync(body)
    else if (editing) await update.mutateAsync({ id: editing, body })
    setEditing(null)
  }

  return <section className="stack" aria-label={t('today.decisions.title')}>
    <div className="form-actions">
      <strong>{t('today.decisions.title')}</strong>
      {!editing ? <Button variant="ghost" size="sm" onClick={() => begin()}>{t('today.decisions.add')}</Button> : null}
    </div>
    {editing ? <div className="stack">
      {honestyReminder ? <p className="form-hint" role="note">{t('today.decisions.honestyReminder')}</p> : null}
      <Field label={t('today.decisions.intent')}><SelectBox value={intent} onChange={event => setIntent(event.target.value as ActionDecisionIntent)}>
        <option value="trade">{t('today.decisions.intent.trade')}</option>
        <option value="continue_observing">{t('today.decisions.intent.continueObserving')}</option>
        <option value="avoid_trade">{t('today.decisions.intent.avoidTrade')}</option>
      </SelectBox></Field>
      <Field label={t('today.decisions.reason')}><TextArea value={reason} onChange={event => setReason(event.target.value)} /></Field>
      <Field label={t('today.decisions.expectation')}><SelectBox value={expectationId} onChange={event => setExpectationId(event.target.value)}>
        <option value="">{t('common.optional')}</option>
        {expectations.map(item => <option key={item.id} value={item.id}>{item.expectedBehavior}</option>)}
      </SelectBox></Field>
      {editing !== 'new' ? <Field label={t('today.decisions.execution')}><SelectBox value={executionReview} onChange={event => setExecutionReview(event.target.value as '' | NonNullable<ExecutionReview>)}>
        <option value="">{t('common.optional')}</option>
        <option value="followed">{t('today.decisions.execution.followed')}</option>
        <option value="partially_followed">{t('today.decisions.execution.partiallyFollowed')}</option>
        <option value="deviated">{t('today.decisions.execution.deviated')}</option>
      </SelectBox></Field> : null}
      <div className="form-actions">
        <Button variant="primary" size="sm" loading={create.isPending || update.isPending} disabled={!reason.trim()} onClick={save}>{t('common.save')}</Button>
        <Button variant="ghost" size="sm" onClick={() => setEditing(null)}>{t('common.cancel')}</Button>
      </div>
    </div> : null}
    {(decisions.data ?? []).map(item => <article className="stack" key={item.id}>
      <p><strong>{{
        trade: t('today.decisions.intent.trade'),
        continue_observing: t('today.decisions.intent.continueObserving'),
        avoid_trade: t('today.decisions.intent.avoidTrade'),
      }[item.intent]}</strong> · <time dateTime={item.recordedAt}>{new Date(item.recordedAt).toLocaleString()}</time></p>
      <p>{item.reason}</p>
      {item.executionReview ? <p>{t('today.decisions.execution')}: {{
        followed: t('today.decisions.execution.followed'),
        partially_followed: t('today.decisions.execution.partiallyFollowed'),
        deviated: t('today.decisions.execution.deviated'),
      }[item.executionReview]}</p> : null}
      <TradeEvidenceList decisionId={item.id} />
      <div className="form-actions">
        <Button variant="ghost" size="sm" onClick={() => begin(item)}>{t('common.edit')}</Button>
        <Button variant="ghost" size="sm" onClick={() => setTradeDecisionId(item.id)}>{t('today.decisions.addTrade')}</Button>
        <Button variant="danger" size="sm" loading={remove.isPending} onClick={() => remove.mutate(item.id)}>{t('common.delete')}</Button>
      </div>
      {tradeDecisionId === item.id ? <TradeEvidenceForm decisionId={item.id} onClose={() => setTradeDecisionId(null)} /> : null}
    </article>)}
  </section>
}
