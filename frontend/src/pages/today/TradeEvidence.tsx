import { useState } from 'react'
import { useCreateTradeEvidenceMutation, useTradeEvidenceQuery } from '../../features/queries'
import { Button, Field, SelectBox, TextArea, TextInput } from '../../ui'
import { useI18n } from '../../i18n'

export function TradeEvidenceList({ decisionId }: { decisionId: string }) {
  const { t } = useI18n()
  const trades = useTradeEvidenceQuery(decisionId)
  return (trades.data ?? []).length ? <ul>
    {trades.data!.map(item => <li key={item.id}>{t('today.decisions.tradeSummary', { side: item.side, quantity: item.quantity, symbol: item.symbol, price: item.price, currency: item.currency })}</li>)}
  </ul> : null
}

export function TradeEvidenceForm({ decisionId, onClose }: { decisionId: string; onClose: () => void }) {
  const { t } = useI18n()
  const create = useCreateTradeEvidenceMutation(decisionId)
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState<'buy' | 'sell'>('buy')
  const [quantity, setQuantity] = useState('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState('USD')
  const [executedAt, setExecutedAt] = useState('')
  const [note, setNote] = useState('')

  async function save() {
    if (!symbol.trim() || !quantity || !price || !executedAt) return
    await create.mutateAsync({
      symbol: symbol.trim(), side, quantity: Number(quantity), price: Number(price), currency: currency.trim(),
      executedAt: new Date(executedAt).toISOString(), note: note.trim() || null,
    })
    onClose()
  }

  return <div className="stack">
    <p className="form-hint">{t('today.decisions.tradeHint')}</p>
    <Field label={t('today.decisions.symbol')}><TextInput value={symbol} onChange={event => setSymbol(event.target.value)} /></Field>
    <Field label={t('today.decisions.side')}><SelectBox value={side} onChange={event => setSide(event.target.value as 'buy' | 'sell')}>
      <option value="buy">{t('today.decisions.side.buy')}</option><option value="sell">{t('today.decisions.side.sell')}</option>
    </SelectBox></Field>
    <Field label={t('today.decisions.quantity')}><TextInput type="number" min="0" step="any" value={quantity} onChange={event => setQuantity(event.target.value)} /></Field>
    <Field label={t('today.decisions.price')}><TextInput type="number" min="0" step="any" value={price} onChange={event => setPrice(event.target.value)} /></Field>
    <Field label={t('today.decisions.currency')}><TextInput value={currency} onChange={event => setCurrency(event.target.value)} /></Field>
    <Field label={t('today.decisions.executedAt')}><TextInput type="datetime-local" value={executedAt} onChange={event => setExecutedAt(event.target.value)} /></Field>
    <Field label={t('today.decisions.note')}><TextArea value={note} onChange={event => setNote(event.target.value)} /></Field>
    <div className="form-actions">
      <Button variant="primary" size="sm" loading={create.isPending} disabled={!symbol.trim() || !quantity || !price || !executedAt} onClick={save}>{t('common.save')}</Button>
      <Button variant="ghost" size="sm" onClick={onClose}>{t('common.cancel')}</Button>
    </div>
  </div>
}
