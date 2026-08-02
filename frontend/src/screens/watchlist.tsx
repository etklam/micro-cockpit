import { useEffect, useId, useMemo, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  useAddWatchlistMutation,
  useInstrumentDirectoryQuery,
  useRemoveWatchlistMutation,
  useSaveWatchlistNoteMutation,
  useWatchlistQuery,
} from '../features/queries'
import type { InstrumentDirectoryItem, WatchlistItem } from '../features/api'
import { Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, TextArea, TextInput, Badge, useConfirm } from '../ui'
import { PageSkeleton, SectionError } from '../shell'
import { useI18n } from '../i18n'

type SortMode = 'recent' | 'oldest'
const EMPTY_WATCHLIST: WatchlistItem[] = []
const EMPTY_INSTRUMENT_DIRECTORY: InstrumentDirectoryItem[] = []

function InstrumentCombobox({
  options,
  value,
  onChange,
  disabled,
}: {
  options: InstrumentDirectoryItem[]
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}) {
  const { t } = useI18n()
  const listboxId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const selected = options.find(item => item.instrumentId === value)
  const selectedLabel = selected ? `${selected.symbol} · ${selected.name}` : ''
  const visible = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    if (!query) return options
    return options.filter(item => `${item.symbol} ${item.name}`.toLocaleLowerCase().includes(query))
  }, [options, search])

  useEffect(() => {
    setActive(index => Math.min(index, Math.max(visible.length - 1, 0)))
  }, [visible.length])

  function choose(item: InstrumentDirectoryItem) {
    onChange(item.instrumentId)
    setSearch('')
    setOpen(false)
    inputRef.current?.focus()
  }

  function onKeyDown(event: ReactKeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setOpen(true)
      setActive(index => visible.length ? (index + 1) % visible.length : 0)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setOpen(true)
      setActive(index => visible.length ? (index - 1 + visible.length) % visible.length : 0)
    } else if (event.key === 'Enter' && open && visible[active]) {
      event.preventDefault()
      choose(visible[active])
    } else if (event.key === 'Escape') {
      setOpen(false)
    }
  }

  return (
    <div className="watchlist-combobox">
      <div className="watchlist-combobox__input-wrap">
        <TextInput
          ref={inputRef}
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={open && visible[active] ? `${listboxId}-${visible[active].instrumentId}` : undefined}
          autoComplete="off"
          disabled={disabled}
          value={selected ? selectedLabel : search}
          placeholder={t('watchlist.instrumentPlaceholder')}
          onFocus={() => setOpen(true)}
          onChange={event => {
            if (selected && event.target.value !== selectedLabel) onChange('')
            setSearch(event.target.value)
            setOpen(true)
            setActive(0)
          }}
          onKeyDown={onKeyDown}
        />
        {selected ? <button type="button" className="watchlist-combobox__clear" aria-label={t('watchlist.clearInstrument')} onClick={() => { onChange(''); setSearch(''); inputRef.current?.focus() }}>×</button> : null}
      </div>
      {open ? (
        <ul id={listboxId} role="listbox" className="watchlist-combobox__options">
          {visible.length ? visible.map((item, index) => (
            <li key={item.instrumentId}>
              <button
                id={`${listboxId}-${item.instrumentId}`}
                type="button"
                role="option"
                aria-selected={item.instrumentId === value}
                className={index === active ? 'is-active' : undefined}
                onMouseDown={event => event.preventDefault()}
                onClick={() => choose(item)}
              >
                <strong>{item.symbol}</strong><span>{item.name}</span>
              </button>
            </li>
          )) : <li className="watchlist-combobox__empty" role="presentation">{t('watchlist.noSearchResults')}</li>}
        </ul>
      ) : null}
    </div>
  )
}

function AddWatchlistDialog({
  open,
  onClose,
  options,
  directoryLoading,
  directoryError,
  existing,
}: {
  open: boolean
  onClose: () => void
  options: InstrumentDirectoryItem[]
  directoryLoading: boolean
  directoryError: boolean
  existing: WatchlistItem[]
}) {
  const { t } = useI18n()
  const headingId = useId()
  const noteRef = useRef<HTMLTextAreaElement>(null)
  const [instrumentId, setInstrumentId] = useState('')
  const [note, setNote] = useState('')
  const [validation, setValidation] = useState<{ instrument?: string; note?: string }>({})
  const [error, setError] = useState(false)
  const addWatchlist = useAddWatchlistMutation()
  const available = options.filter(item => !existing.some(member => member.instrumentId === item.instrumentId))

  useEffect(() => {
    if (!open) return
    setValidation({})
    setError(false)
    window.setTimeout(() => noteRef.current?.focus(), 0)
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onClose() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  async function submit(event: FormEvent) {
    event.preventDefault()
    const next = {
      instrument: instrumentId ? undefined : t('watchlist.instrumentRequired'),
      note: note.trim() ? undefined : t('watchlist.noteRequired'),
    }
    setValidation(next)
    if (next.instrument || next.note) return
    setError(false)
    try {
      await addWatchlist.mutateAsync({ instrumentId, note: note.trim() })
      setInstrumentId('')
      setNote('')
      onClose()
    } catch {
      setError(true)
    }
  }

  return (
    <div className="watchlist-dialog" role="presentation">
      <button type="button" className="watchlist-dialog__backdrop" aria-label={t('common.cancel')} onClick={onClose} />
      <section className="watchlist-dialog__panel" role="dialog" aria-modal="true" aria-labelledby={headingId}>
        <div className="watchlist-dialog__head">
          <div><h2 id={headingId}>{t('watchlist.addTitle')}</h2><p className="form-hint">{t('watchlist.addHint')}</p></div>
          <IconButton icon="close" label={t('common.cancel')} onClick={onClose} />
        </div>
        {directoryError ? <p className="form-error" role="alert">{t('watchlist.directoryError')}</p> : null}
        {!directoryError && !directoryLoading && !available.length ? <p className="form-hint" role="status">{existing.length ? t('watchlist.noMoreInstrumentsHint') : t('watchlist.noInstrumentsHint')}</p> : null}
        <form className="stack" onSubmit={submit}>
          <Field label={t('watchlist.instrument')} hint={validation.instrument ?? (directoryLoading ? t('common.loading') : undefined)} error={validation.instrument}>
            <InstrumentCombobox options={available} value={instrumentId} onChange={id => { setInstrumentId(id); setValidation(current => ({ ...current, instrument: undefined })) }} disabled={directoryLoading || directoryError || !available.length} />
          </Field>
          <Field label={t('watchlist.note')} hint={t('watchlist.noteHint')} error={validation.note}>
            <TextArea ref={noteRef} required maxLength={500} value={note} placeholder={t('watchlist.notePlaceholder')} onChange={event => { setNote(event.target.value); setValidation(current => ({ ...current, note: undefined })) }} />
          </Field>
          {error ? <p className="form-error" role="alert">{t('watchlist.addError')}</p> : null}
          <div className="form-actions">
            <Button variant="ghost" type="button" onClick={onClose}>{t('common.cancel')}</Button>
            <Button variant="primary" icon="plus" type="submit" loading={addWatchlist.isPending} disabled={directoryLoading || directoryError || !available.length}>{t('watchlist.add')}</Button>
          </div>
        </form>
      </section>
    </div>
  )
}

export function WatchlistPage() {
  const { t, format } = useI18n()
  const list = useWatchlistQuery()
  const instruments = useInstrumentDirectoryQuery()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<SortMode>('recent')
  const [editError, setEditError] = useState(false)
  const [removeError, setRemoveError] = useState(false)
  const { confirm, confirmNode } = useConfirm()
  const removeWatchlist = useRemoveWatchlistMutation()
  const saveNote = useSaveWatchlistNoteMutation()
  const items = list.data ?? EMPTY_WATCHLIST
  const directory = instruments.data ?? EMPTY_INSTRUMENT_DIRECTORY
  const instrument = (id: string) => directory.find(item => item.instrumentId === id)
  const filteredItems = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    const result = items.filter(item => {
      const details = directory.find(entry => entry.instrumentId === item.instrumentId)
      return !query || `${details?.symbol ?? ''} ${details?.name ?? ''} ${item.note ?? ''}`.toLocaleLowerCase().includes(query)
    })
    return result.sort((a, b) => {
      const left = Date.parse(a.updatedAt)
      const right = Date.parse(b.updatedAt)
      return sort === 'recent' ? right - left || a.instrumentId.localeCompare(b.instrumentId) : left - right || a.instrumentId.localeCompare(b.instrumentId)
    })
  }, [items, directory, search, sort])

  async function remove(item: WatchlistItem) {
    const details = instrument(item.instrumentId)
    const accepted = await confirm({
      title: t('watchlist.removeConfirmTitle', { symbol: details?.symbol ?? item.instrumentId }),
      message: t('watchlist.removeConfirmMessage'),
      confirmText: t('watchlist.removeConfirm'),
      tone: 'danger',
    })
    if (!accepted) return
    setRemoveError(false)
    try {
      await removeWatchlist.mutateAsync(item.instrumentId)
      if (editingId === item.instrumentId) setEditingId(null)
    } catch {
      setRemoveError(true)
    }
  }

  async function save(item: WatchlistItem, event: FormEvent) {
    event.preventDefault()
    setEditError(false)
    try {
      await saveNote.mutateAsync({ instrumentId: item.instrumentId, note: note.trim() })
      setEditingId(null)
    } catch {
      setEditError(true)
    }
  }

  const loadedEmpty = !list.isLoading && !instruments.isLoading && !items.length

  return <>
    <PageHeader
      title={t('watchlist.title')}
      subtitle={t('watchlist.subtitle')}
      actions={loadedEmpty ? undefined : <Button variant="primary" icon="plus" onClick={() => setDialogOpen(true)}>{t('watchlist.add')}</Button>}
    />
    {loadedEmpty ? (
      <Card as="section" className="watchlist-onboarding" aria-labelledby="watchlist-onboarding-title">
        <div className="watchlist-onboarding__copy">
          <Badge tone="primary">{t('watchlist.continuingObservation')}</Badge>
          <h2 id="watchlist-onboarding-title">{t('watchlist.emptyTitle')}</h2>
          <p>{t('watchlist.emptyHint')}</p>
        </div>
        <Button variant="primary" icon="plus" onClick={() => setDialogOpen(true)}>{t('watchlist.addFirst')}</Button>
      </Card>
    ) : null}
    {list.isLoading || instruments.isLoading ? <PageSkeleton rows={2} /> :
      list.isError || instruments.isError ? <SectionError onRetry={() => { void list.refetch(); void instruments.refetch() }} /> :
      items.length ? <>
        <Card as="section" className="watchlist-toolbar" aria-label={t('watchlist.controls')}>
          <Field label={t('watchlist.search')}>
            <TextInput type="search" value={search} placeholder={t('watchlist.searchPlaceholder')} onChange={event => setSearch(event.target.value)} />
          </Field>
          <Field label={t('watchlist.sort')}>
            <SelectBox value={sort} onChange={event => setSort(event.target.value as SortMode)}>
              <option value="recent">{t('watchlist.sortRecent')}</option>
              <option value="oldest">{t('watchlist.sortOldest')}</option>
            </SelectBox>
          </Field>
        </Card>
        {removeError ? <p className="form-error" role="alert">{t('watchlist.removeError')}</p> : null}
        {!filteredItems.length ? <EmptyBox title={t('watchlist.filteredEmptyTitle')} hint={t('watchlist.filteredEmptyHint')} /> :
          <ul className="compact-list watchlist-items">{filteredItems.map(item => {
            const details = instrument(item.instrumentId)
            const isEditing = editingId === item.instrumentId
            return <li key={item.instrumentId}>
              <Card as="article" className="watchlist-item">
                <div className="watchlist-item__header">
                  <div className="row-main"><strong>{details?.symbol ?? t('common.unavailable')}</strong><span>{details?.name ?? item.instrumentId}</span></div>
                  <Badge>{t('watchlist.instrumentType')}</Badge>
                </div>
                <div className="watchlist-item__reason">
                  <span className="watchlist-item__label">{t('watchlist.note')}</span>
                  {item.note ? <p>{item.note}</p> : <p className="is-muted">{t('watchlist.noNote')}</p>}
                </div>
                <dl className="watchlist-item__dates">
                  <div><dt>{t('watchlist.createdAt')}</dt><dd>{format.dateTime(item.createdAt)}</dd></div>
                  <div><dt>{t('watchlist.updatedAt')}</dt><dd>{format.dateTime(item.updatedAt)}</dd></div>
                </dl>
                <div className="form-actions watchlist-item__actions">
                  <Link className="btn btn--primary btn--sm" to={`/today/observations?instrumentId=${encodeURIComponent(item.instrumentId)}`}>{t('watchlist.timeline')}</Link>
                  <Link className="btn btn--subtle btn--sm" to={`/today?instrumentId=${encodeURIComponent(item.instrumentId)}`}>{t('today.observations.observeAgain')}</Link>
                  <Button variant="ghost" size="sm" onClick={() => { setEditingId(item.instrumentId); setNote(item.note ?? ''); setEditError(false) }}>{t('watchlist.editNote')}</Button>
                  <IconButton icon="trash" label={t('watchlist.remove', { symbol: details?.symbol ?? item.instrumentId })} onClick={() => { void remove(item) }} />
                </div>
                {isEditing ? <form className="watchlist-item__edit stack" onSubmit={event => { void save(item, event) }}>
                  <Field label={t('watchlist.note')} hint={t('watchlist.noteHint')}>
                    <TextArea maxLength={500} value={note} onChange={event => setNote(event.target.value)} />
                  </Field>
                  {editError ? <p className="form-error" role="alert">{t('watchlist.editError')}</p> : null}
                  <div className="form-actions">
                    <Button variant="primary" type="submit" loading={saveNote.isPending}>{t('common.save')}</Button>
                    <Button variant="ghost" type="button" onClick={() => setEditingId(null)}>{t('common.cancel')}</Button>
                  </div>
                </form> : null}
              </Card>
            </li>
          })}</ul>}
      </> : null}
    <AddWatchlistDialog open={dialogOpen} onClose={() => setDialogOpen(false)} options={directory} directoryLoading={instruments.isLoading} directoryError={instruments.isError} existing={items} />
    {confirmNode}
  </>
}
