import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  useAddWatchlistMutation,
  useInstrumentDirectoryQuery,
  useRemoveWatchlistMutation,
  useSaveWatchlistNoteMutation,
  useWatchlistQuery,
} from '../features/queries'
import { Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, TextArea } from '../ui'
import { PageSkeleton, SectionError } from '../shell'
import { useI18n } from '../i18n'

export function WatchlistPage() {
  const { t } = useI18n()
  const list = useWatchlistQuery()
  const instruments = useInstrumentDirectoryQuery()
  const [instrumentId, setInstrumentId] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const addWatchlist = useAddWatchlistMutation()
  const removeWatchlist = useRemoveWatchlistMutation()
  const saveNote = useSaveWatchlistNoteMutation()
  const items = list.data ?? []
  const available = (instruments.data ?? []).filter(item => !items.some(member => member.instrumentId === item.instrumentId))
  const instrument = (id: string) => instruments.data?.find(item => item.instrumentId === id)

  async function add(event: FormEvent) {
    event.preventDefault()
    if (!instrumentId) return
    await addWatchlist.mutateAsync(instrumentId)
    setInstrumentId('')
  }

  async function remove(id: string) {
    await removeWatchlist.mutateAsync(id)
    if (editingId === id) setEditingId(null)
  }

  return <>
    <PageHeader title={t('watchlist.title')} subtitle={t('watchlist.subtitle')} />
    <Card as="section" className="stack" aria-labelledby="watchlist-add-title">
      <div><h2 id="watchlist-add-title">{t('watchlist.setupTitle')}</h2><p className="form-hint">{t('watchlist.setupHint')}</p></div>
      <form className="inline-form watchlist-add-form" onSubmit={add}>
        <Field label={t('watchlist.instrument')}><SelectBox required value={instrumentId} onChange={event => setInstrumentId(event.target.value)}>
          <option value="">{t('watchlist.choose')}</option>
          {available.map(item => <option key={item.instrumentId} value={item.instrumentId}>{item.symbol} · {item.name}</option>)}
        </SelectBox></Field>
        <Button variant="primary" icon="plus" type="submit" disabled={!instrumentId} loading={addWatchlist.isPending}>{t('watchlist.add')}</Button>
      </form>
    </Card>
    {list.isLoading || instruments.isLoading ? <PageSkeleton rows={2} /> :
      list.isError || instruments.isError ? <SectionError onRetry={() => { void list.refetch(); void instruments.refetch() }} /> :
      !items.length ? <EmptyBox title={t('watchlist.emptyTitle')} hint={t('watchlist.emptyHint')} /> :
      <ul className="compact-list">{items.map(item => {
        const details = instrument(item.instrumentId)
        return <li key={item.instrumentId}><Card className="stack">
          <div className="row-main"><strong>{details?.symbol ?? t('common.unavailable')}</strong><span>{details?.name ?? item.instrumentId}</span></div>
          {item.note ? <p>{item.note}</p> : <p className="is-muted">{t('watchlist.noNote')}</p>}
          <div className="form-actions">
            <Button variant="ghost" size="sm" onClick={() => { setEditingId(item.instrumentId); setNote(item.note ?? '') }}>{t('watchlist.editNote')}</Button>
            <Link className="text-link" to={`/today/observations?instrumentId=${item.instrumentId}`}>{t('watchlist.timeline')}</Link>
            <IconButton icon="trash" label={t('watchlist.remove', { symbol: details?.symbol ?? item.instrumentId })} onClick={() => { void remove(item.instrumentId) }} />
          </div>
          {editingId === item.instrumentId ? <form className="stack" onSubmit={async event => {
            event.preventDefault()
            await saveNote.mutateAsync({ instrumentId: item.instrumentId, note: note.trim() })
            setEditingId(null)
          }}>
            <Field label={t('watchlist.note')}><TextArea maxLength={500} value={note} onChange={event => setNote(event.target.value)} /></Field>
            <p className="form-hint">{t('watchlist.noteHint')}</p>
            <div className="form-actions">
              <Button variant="primary" type="submit" loading={saveNote.isPending}>{t('common.save')}</Button>
              <Button variant="ghost" onClick={() => setEditingId(null)}>{t('common.cancel')}</Button>
            </div>
          </form> : null}
        </Card></li>
      })}</ul>}
  </>
}
