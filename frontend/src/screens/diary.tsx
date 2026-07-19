import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent, KeyboardEvent } from 'react'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Diary, DiaryReviewWrite, Transaction } from '../features/api'
import { useIdempotencyKey } from '../features/api'
import {
  diaryDetailPath, diaryFiltersActive, diaryFiltersToSearch, emptyDiaryFilters, listPathFromReturnTo,
  normalizeTags, parseDiaryFilters, type DiaryFilters,
} from '../features/diaryFilters'
import { accountDateTimeLocalToUtc, formatTimezoneLabel, nowAccountDateTimeLocal, utcToAccountDateTimeLocal } from '../features/accountTime'
import { MarkdownView, plainExcerpt } from '../features/markdown'
import {
  useBootstrapQuery, useCreateTransactionMutation, useDeleteDiaryMutation, useDeleteTransactionMutation,
  useDiariesInfiniteQuery, useDiaryQuery, useSaveDiaryMutation,
  useDeleteDiaryReviewMutation, useDiaryReviewQuery, useDiaryReviewSummaryQuery, useSaveDiaryReviewMutation,
  useTransactionsQuery, useUpdateTransactionMutation,
} from '../features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from '../ui'
import { PageSkeleton, SectionError, useCockpit } from '../shell'
import { quantity } from '../format'
import {
  diaryDeleteErrorMessage, diaryMutationErrorMessage, transactionDeleteErrorMessage,
  transactionUpdateErrorMessage, useI18n,
} from '../i18n'

export function DiaryPage() {
  const { confirm } = useCockpit()
  const { t, locale, format } = useI18n()
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const filters = useMemo(() => parseDiaryFilters(search), [search])
  const [keywordDraft, setKeywordDraft] = useState(filters.q)
  const keywordTimer = useRef<number | null>(null)
  useEffect(() => { setKeywordDraft(filters.q) }, [filters.q])
  // Debounce keyword against the latest URL filters so a pending timer cannot clobber newer date/symbol/tag/review.
  useEffect(() => {
    if (keywordTimer.current != null) window.clearTimeout(keywordTimer.current)
    keywordTimer.current = window.setTimeout(() => {
      keywordTimer.current = null
      setSearch(prev => {
        const current = parseDiaryFilters(prev)
        const q = keywordDraft.trim()
        if (q === current.q) return prev
        return diaryFiltersToSearch({ ...current, q })
      }, { replace: true })
    }, 300)
    return () => {
      if (keywordTimer.current != null) {
        window.clearTimeout(keywordTimer.current)
        keywordTimer.current = null
      }
    }
  }, [keywordDraft, setSearch])

  const list = useDiariesInfiniteQuery(filters)
  const items = useMemo(() => {
    const seen = new Set<string>()
    const rows: Diary[] = []
    for (const page of list.data?.pages ?? []) {
      for (const item of page.items) {
        if (seen.has(item.id)) continue
        seen.add(item.id)
        rows.push(item)
      }
    }
    return rows
  }, [list.data])

  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentLocalDate
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [date, setDate] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [tagDraft, setTagDraft] = useState('')
  const [mode, setMode] = useState<'write' | 'preview'>('write')
  const [editing, setEditing] = useState<Diary | null>(null)
  const [formError, setFormError] = useState('')
  const [listError, setListError] = useState('')
  const idem = useIdempotencyKey()
  const saveDiary = useSaveDiaryMutation()
  const deleteDiary = useDeleteDiaryMutation()
  useEffect(() => {
    if (accountToday && !editing && !date) setDate(accountToday)
  }, [accountToday, editing, date])
  const reviewWindow = useMemo(() => {
    const to = accountToday ?? ''
    if (!to) return { from: '', to: '' }
    const fromDate = new Date(`${to}T00:00:00Z`); fromDate.setUTCDate(fromDate.getUTCDate() - 29)
    return { from: fromDate.toISOString().slice(0, 10), to }
  }, [accountToday])
  const reviewSummary = useDiaryReviewSummaryQuery(reviewWindow.from, reviewWindow.to)
  const activeFilters = diaryFiltersActive(filters)

  function writeFilters(next: DiaryFilters, opts?: { replace?: boolean }) {
    setSearch(diaryFiltersToSearch(next), { replace: opts?.replace ?? false })
  }

  function clearFilters() {
    if (keywordTimer.current != null) {
      window.clearTimeout(keywordTimer.current)
      keywordTimer.current = null
    }
    setKeywordDraft('')
    writeFilters(emptyDiaryFilters, { replace: true })
  }

  function startEdit(d: Diary) {
    setEditing(d); setTitle(d.title); setContent(d.content); setDate(d.localDate)
    setTags([...(d.tags ?? [])]); setTagDraft(''); setMode('write'); setFormError('')
    document.getElementById('diary-form')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
  function reset() {
    setEditing(null); setTitle(''); setContent(''); setDate(accountToday ?? ''); setTags([]); setTagDraft('')
    setMode('write'); setFormError(''); idem.reset()
  }

  function commitTagDraft() {
    const pieces = tagDraft.split(',').map(part => part.trim()).filter(Boolean)
    if (!pieces.length) return
    const next = normalizeTags([...tags, ...pieces])
    if (next.error) { setFormError(t(next.error)); return }
    setTags(next.tags); setTagDraft(''); setFormError('')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    const normalized = normalizeTags(tagDraft.trim() ? [...tags, ...tagDraft.split(',')] : tags)
    if (normalized.error) { setFormError(t(normalized.error)); return }
    try {
      await saveDiary.mutateAsync({ id: editing?.id, date, title, content, tags: normalized.tags, key: idem.key() })
      reset()
    } catch (error) {
      setFormError(diaryMutationErrorMessage(locale, error))
    }
  }

  async function remove(d: Diary) {
    const ok = await confirm({
      title: t('diary.deleteConfirmTitle'),
      message: t('diary.deleteConfirmMessage', { title: d.title }),
      confirmText: t('common.delete'),
      tone: 'danger',
    })
    if (!ok) return
    setListError('')
    try {
      await deleteDiary.mutateAsync(d.id)
      if (editing?.id === d.id) reset()
    } catch (error) {
      setListError(diaryDeleteErrorMessage(locale, error))
    }
  }

  return (
    <>
      <PageHeader title={t('diary.title')} subtitle={t('diary.subtitle', { count: items.length })} />
      <Link className="text-link" to="/review">{t('diary.openReview')}</Link>

      <Card as="section" className="review-patterns">
        <h2>{t('diary.reviewPatterns')}</h2>
        {reviewSummary.isLoading ? <p className="is-muted">{t('diary.reviewPatternsLoading')}</p> : reviewSummary.isError ? <SectionError onRetry={() => { void reviewSummary.refetch() }} /> : Number(reviewSummary.data?.reviewedCount ?? 0) === 0 ? (
          <EmptyBox title={t('diary.reviewEmptyTitle')} hint={t('diary.reviewEmptyHint')} dense />
        ) : <div className="card-grid">
          <Stat label={t('diary.stat.reviewed')} value={Number(reviewSummary.data?.reviewedCount)} />
          <Stat label={t('diary.stat.avgDiscipline')} value={reviewSummary.data?.averageDisciplineScore == null ? format.empty : Number(reviewSummary.data.averageDisciplineScore).toFixed(1)} />
          <Stat label={t('diary.stat.avgExecution')} value={reviewSummary.data?.averageExecutionScore == null ? format.empty : Number(reviewSummary.data.averageExecutionScore).toFixed(1)} />
          <Stat label={t('diary.stat.topMistakes')} value={reviewSummary.data?.topMistakeTags.map(item => item.tag.replaceAll('_', ' ')).join(', ') || format.empty} />
        </div>}
      </Card>

      <Card flush as="section" id="diary-form" className="diary-form">
        <form className="diary-form__body" onSubmit={submit}>
          <div className="form-row">
            <Field label={t('diary.form.date')}>
              <TextInput type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label={t('diary.form.title')} className="field--grow">
              <TextInput required maxLength={160} value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t('diary.form.titlePlaceholder')} />
            </Field>
          </div>
          <div className="diary-mode" role="tablist" aria-label={t('diary.form.editorMode')}>
            <Button type="button" size="sm" variant={mode === 'write' ? 'primary' : 'ghost'} aria-selected={mode === 'write'} onClick={() => setMode('write')}>{t('diary.form.write')}</Button>
            <Button type="button" size="sm" variant={mode === 'preview' ? 'primary' : 'ghost'} aria-selected={mode === 'preview'} onClick={() => setMode('preview')}>{t('diary.form.preview')}</Button>
          </div>
          {mode === 'write' ? (
            <Field label={t('diary.form.reflection')}>
              <TextArea
                required value={content} onChange={(e) => setContent(e.target.value)}
                placeholder={t('diary.form.placeholder')}
                className="textarea--prose textarea--lg"
              />
            </Field>
          ) : (
            <Field label={t('diary.form.preview')}>
              <MarkdownView content={content} className="prose entry__body diary-preview" />
            </Field>
          )}
          <Field label={t('diary.form.tags')} hint={t('diary.form.tagsHint')}>
            <div className="tag-editor">
              <div className="tag-chips">
                {tags.map(tag => (
                  <button key={tag} type="button" className="tag-chip" onClick={() => setTags(tags.filter(item => item !== tag))} aria-label={t('diary.form.removeTag', { tag })}>
                    {tag} ×
                  </button>
                ))}
              </div>
              <TextInput
                aria-label={t('diary.form.addTag')}
                value={tagDraft}
                onChange={(e) => setTagDraft(e.target.value)}
                onKeyDown={(e: KeyboardEvent<HTMLInputElement>) => {
                  if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); commitTagDraft() }
                }}
                onBlur={commitTagDraft}
                placeholder={t('diary.form.tagsPlaceholder')}
                disabled={tags.length >= 10}
              />
            </div>
          </Field>
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            {editing ? <Button variant="ghost" onClick={reset}>{t('diary.form.cancel')}</Button> : null}
            <Button variant="primary" type="submit" icon="check" loading={saveDiary.isPending}>
              {editing ? t('diary.form.update') : t('diary.form.add')}
            </Button>
          </div>
        </form>
      </Card>

      <Card as="section" className="diary-filters">
        <div className="form-row diary-filters__row">
          <Field label={t('diary.filter.search')} className="field--grow">
            <TextInput aria-label={t('diary.filter.keyword')} value={keywordDraft} onChange={(e) => setKeywordDraft(e.target.value)} placeholder={t('diary.filter.keywordPlaceholder')} />
          </Field>
          <Field label={t('diary.filter.from')}><TextInput type="date" value={filters.from} onChange={(e) => writeFilters({ ...filters, from: e.target.value })} /></Field>
          <Field label={t('diary.filter.to')}><TextInput type="date" value={filters.to} onChange={(e) => writeFilters({ ...filters, to: e.target.value })} /></Field>
          <Field label={t('diary.filter.review')}>
            <SelectBox value={filters.review} onChange={(e) => writeFilters({ ...filters, review: e.target.value as DiaryFilters['review'] })}>
              <option value="all">{t('common.all')}</option>
              <option value="reviewed">{t('diary.filter.reviewed')}</option>
              <option value="unreviewed">{t('diary.filter.unreviewed')}</option>
            </SelectBox>
          </Field>
          <Field label={t('diary.filter.symbol')}><TextInput value={filters.symbol} onChange={(e) => writeFilters({ ...filters, symbol: e.target.value.toUpperCase() }, { replace: true })} placeholder="AAPL" /></Field>
          <Field label={t('diary.filter.tag')}><TextInput value={filters.tag} onChange={(e) => writeFilters({ ...filters, tag: e.target.value.toLowerCase() }, { replace: true })} placeholder="fomo" /></Field>
        </div>
        <div className="diary-filters__summary">
          <span className="is-muted">{t('diary.filter.loaded', { count: items.length })}{activeFilters ? ` · ${t('diary.filter.filtersActive')}` : ''}</span>
          {activeFilters ? <Button size="sm" variant="ghost" onClick={clearFilters}>{t('diary.filter.clear')}</Button> : null}
        </div>
        {activeFilters ? (
          <div className="tag-chips" aria-label={t('diary.filter.active')}>
            {filters.q ? <span className="tag-chip">q: {filters.q}</span> : null}
            {filters.from ? <span className="tag-chip">from: {filters.from}</span> : null}
            {filters.to ? <span className="tag-chip">to: {filters.to}</span> : null}
            {filters.review !== 'all' ? <span className="tag-chip">review: {filters.review}</span> : null}
            {filters.symbol ? <span className="tag-chip">symbol: {filters.symbol}</span> : null}
            {filters.tag ? <span className="tag-chip">tag: {filters.tag}</span> : null}
          </div>
        ) : null}
      </Card>

      {listError ? <p className="form-error" role="alert">{listError}</p> : null}

      {list.isError ? (
        <SectionError onRetry={() => { void list.refetch() }} />
      ) : list.isLoading ? (
        <DiaryListSkeleton />
      ) : items.length === 0 ? (
        <EmptyBox
          icon="diary"
          title={activeFilters ? t('diary.emptyFilteredTitle') : t('diary.emptyTitle')}
          hint={activeFilters ? t('diary.emptyFilteredHint') : t('diary.emptyHint')}
        />
      ) : (
        <>
          <ul className="entry-list">
            {items.map((d) => (
              <li key={d.id}>
                <Card flush as="article" className="entry">
                  <div className="entry__head">
                    <span className="entry__date">{d.localDate}</span>
                    <div className="entry__actions">
                      <IconButton icon="layers" label={t('diary.trades')} size={16} onClick={() => navigate(diaryDetailPath(d.id, search.toString()))} />
                      <IconButton icon="edit" label={t('diary.editEntry')} size={16} onClick={() => startEdit(d)} />
                      <IconButton icon="trash" label={t('diary.deleteEntry')} size={16} className="icon-btn--danger" onClick={() => { void remove(d) }} />
                    </div>
                  </div>
                  <h3 className="entry__title">{d.title}</h3>
                  {d.tags?.length ? (
                    <div className="tag-chips entry__tags">
                      {d.tags.map(tag => (
                        <button key={tag} type="button" className="tag-chip" onClick={() => writeFilters({ ...filters, tag })}>{tag}</button>
                      ))}
                    </div>
                  ) : null}
                  {d.content
                    ? <p className="entry__body">{plainExcerpt(d.content)}</p>
                    : <p className="entry__body is-muted">{t('diary.noContent')}</p>}
                </Card>
              </li>
            ))}
          </ul>
          {list.hasNextPage ? (
            <div className="form-actions">
              <Button variant="subtle" loading={list.isFetchingNextPage} onClick={() => { void list.fetchNextPage() }}>{t('diary.loadMore')}</Button>
            </div>
          ) : null}
        </>
      )}
    </>
  )
}

export function DiaryDetailPage() {
  const { diaryId = '' } = useParams()
  const navigate = useNavigate()
  const [detailSearch] = useSearchParams()
  const listTarget = listPathFromReturnTo(detailSearch.get('returnTo'))
  const backToList = () => navigate(listTarget)
  const { confirm } = useCockpit()
  const { t, locale } = useI18n()
  const diary = useDiaryQuery(diaryId)
  const removeDiary = useDeleteDiaryMutation()
  const [deleteError, setDeleteError] = useState('')
  async function remove() {
    if (!diary.data) return
    const ok = await confirm({
      title: t('diary.deleteConfirmTitle'),
      message: t('diary.deleteConfirmMessage', { title: diary.data.title }),
      confirmText: t('common.delete'),
      tone: 'danger',
    })
    if (!ok) return
    setDeleteError('')
    try {
      await removeDiary.mutateAsync(diaryId)
      navigate(listTarget, { replace: true })
    } catch (error) {
      setDeleteError(diaryDeleteErrorMessage(locale, error))
    }
  }
  if (diary.isLoading) return <PageSkeleton rows={2} />
  if (diary.isError || !diary.data) return <SectionError onRetry={() => { void diary.refetch() }} />
  return <>
    <PageHeader title={diary.data.title} subtitle={diary.data.localDate} />
    <Card as="article" className="entry">
      {diary.data.tags?.length ? <div className="tag-chips entry__tags">{diary.data.tags.map(tag => <span key={tag} className="tag-chip">{tag}</span>)}</div> : null}
      {diary.data.content
        ? <MarkdownView content={diary.data.content} className="prose entry__body" />
        : <p className="entry__body is-muted">{t('diary.noContent')}</p>}
      {deleteError ? <p className="form-error" role="alert">{deleteError}</p> : null}
      <div className="form-actions">
        <Button variant="ghost" onClick={backToList}>{t('diary.back')}</Button>
        <Button variant="danger" loading={removeDiary.isPending} onClick={() => { void remove() }}>{t('diary.deleteEntry')}</Button>
      </div>
      <TransactionPanel diaryId={diaryId} />
    </Card>
    <DecisionReview diaryId={diaryId} />
  </>
}

const reviewTags = ['no_plan', 'fomo', 'poor_timing', 'risk_violation', 'overtrading', 'ignored_signal', 'early_exit', 'late_exit', 'other']
const emptyReview: DiaryReviewWrite = { thesis: null, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null }

function DecisionReview({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const { t } = useI18n()
  const review = useDiaryReviewQuery(diaryId)
  const save = useSaveDiaryReviewMutation(diaryId)
  const removeReview = useDeleteDiaryReviewMutation(diaryId)
  const [form, setForm] = useState<DiaryReviewWrite>(emptyReview)
  const location = useLocation()
  const detailsRef = useRef<HTMLDetailsElement>(null)
  const headingRef = useRef<HTMLElement>(null)
  const deepLinked = location.hash === '#decision-review'
  useEffect(() => { if (review.data) setForm({ thesis: review.data.thesis, plannedAction: review.data.plannedAction, actualAction: review.data.actualAction, emotion: review.data.emotion, disciplineScore: review.data.disciplineScore, executionScore: review.data.executionScore, processAssessment: review.data.processAssessment, mistakeTags: review.data.mistakeTags ?? [], lesson: review.data.lesson, nextAction: review.data.nextAction }) }, [review.data])
  useEffect(() => {
    if (!deepLinked || review.isLoading) return
    if (detailsRef.current) detailsRef.current.open = true
    document.getElementById('decision-review')?.scrollIntoView?.({ block: 'start' })
    headingRef.current?.focus()
  }, [deepLinked, review.isLoading])
  const text = (key: keyof DiaryReviewWrite) => (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setForm(current => ({ ...current, [key]: event.target.value || null }))
  const toggleTag = (tag: string) => setForm(current => { const tags = current.mistakeTags ?? []; return { ...current, mistakeTags: tags.includes(tag) ? tags.filter(item => item !== tag) : [...tags, tag] } })
  async function remove() {
    if (!await confirm({
      title: t('diary.review.deleteTitle'),
      message: t('diary.review.deleteMessage'),
      confirmText: t('common.delete'),
      tone: 'danger',
    })) return
    await removeReview.mutateAsync(); setForm(emptyReview)
  }
  return <Card as="section" className="decision-review" id="decision-review"><details ref={detailsRef}><summary><strong ref={headingRef} tabIndex={-1}>{t('diary.decisionReview')}</strong></summary>
    {review.isLoading ? <p className="is-muted">{t('diary.review.loading')}</p> : review.isError ? <SectionError onRetry={() => { void review.refetch() }} /> : <form className="diary-form__body" onSubmit={event => { event.preventDefault(); void save.mutateAsync(form) }}>
      {!review.data ? <p className="is-muted">{t('diary.review.empty')}</p> : null}
      <Field label={t('diary.review.thesis')}><TextArea value={form.thesis ?? ''} onChange={text('thesis')} /></Field>
      <div className="form-row"><Field label={t('diary.review.plannedAction')} className="field--grow"><TextInput value={form.plannedAction ?? ''} onChange={text('plannedAction')} /></Field><Field label={t('diary.review.actualAction')} className="field--grow"><TextInput value={form.actualAction ?? ''} onChange={text('actualAction')} /></Field></div>
      <div className="form-row"><Field label={t('diary.review.emotion')}><SelectBox value={form.emotion ?? ''} onChange={event => setForm(current => ({ ...current, emotion: event.target.value || null }))}><option value="">{t('diary.review.notSet')}</option>{['calm','confident','uncertain','anxious','fomo','frustrated','overconfident','other'].map(value => <option key={value} value={value}>{value}</option>)}</SelectBox></Field><Field label={t('diary.review.disciplineScore')}><SelectBox value={form.disciplineScore == null ? '' : String(form.disciplineScore)} onChange={event => setForm(current => ({ ...current, disciplineScore: event.target.value ? Number(event.target.value) : null }))}><option value="">{t('diary.review.notSet')}</option>{[1,2,3,4,5].map(value => <option key={value} value={value}>{value}</option>)}</SelectBox></Field><Field label={t('diary.review.executionScore')}><SelectBox value={form.executionScore == null ? '' : String(form.executionScore)} onChange={event => setForm(current => ({ ...current, executionScore: event.target.value ? Number(event.target.value) : null }))}><option value="">{t('diary.review.notSet')}</option>{[1,2,3,4,5].map(value => <option key={value} value={value}>{value}</option>)}</SelectBox></Field></div>
      <Field label={t('diary.review.processAssessment')}><SelectBox value={form.processAssessment ?? ''} onChange={event => setForm(current => ({ ...current, processAssessment: event.target.value || null }))}><option value="">{t('diary.review.notSet')}</option><option value="good">{t('diary.review.good')}</option><option value="mixed">{t('diary.review.mixed')}</option><option value="poor">{t('diary.review.poor')}</option></SelectBox></Field>
      <fieldset className="review-tags"><legend>{t('diary.review.mistakeTags')}</legend>{reviewTags.map(tag => <label key={tag}><input type="checkbox" checked={(form.mistakeTags ?? []).includes(tag)} onChange={() => toggleTag(tag)} /> {tag.replaceAll('_', ' ')}</label>)}</fieldset>
      <div className="form-row"><Field label={t('diary.review.lesson')} className="field--grow"><TextArea value={form.lesson ?? ''} onChange={text('lesson')} /></Field><Field label={t('diary.review.nextAction')} className="field--grow"><TextArea value={form.nextAction ?? ''} onChange={text('nextAction')} /></Field></div>
      <div className="form-actions">{review.data ? <Button variant="danger" loading={removeReview.isPending} onClick={remove}>{t('diary.review.delete')}</Button> : null}<Button variant="primary" type="submit" loading={save.isPending}>{t('diary.review.save')}</Button></div>
    </form>}
  </details></Card>
}

function DiaryListSkeleton() {
  return (
    <ul className="entry-list">
      {Array.from({ length: 3 }, (_, i) => (
        <li key={i}><Card flush className="entry"><div className="entry__skel">
          <div className="skel" style={{ height: 12, width: 90 }} />
          <div className="skel" style={{ height: 22, width: '55%', marginTop: 14 }} />
          <div className="skel" style={{ height: 14, width: '90%', marginTop: 12 }} />
          <div className="skel" style={{ height: 14, width: '70%', marginTop: 8 }} />
        </div></Card></li>
      ))}
    </ul>
  )
}

/* -------------------------- Transactions --------------------------- */
function TransactionPanel({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const { t, locale } = useI18n()
  const bootstrap = useBootstrapQuery()
  const timeZone = bootstrap.data?.timezone ?? ''
  const baseCurrency = bootstrap.data?.baseCurrency ?? 'USD'
  const { data, isLoading: loading, isError: error, refetch: reload } = useTransactionsQuery(diaryId)
  const items = data?.items ?? []
  const [editingId, setEditingId] = useState<string | null>(null)
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState('buy')
  const [qty, setQty] = useState('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState(baseCurrency)
  const [tradedAt, setTradedAt] = useState('')
  const [notes, setNotes] = useState('')
  const [formError, setFormError] = useState('')
  const idem = useIdempotencyKey()
  const createTransaction = useCreateTransactionMutation(diaryId)
  const updateTransaction = useUpdateTransactionMutation(diaryId)
  const deleteTransaction = useDeleteTransactionMutation(diaryId)
  const saving = createTransaction.isPending || updateTransaction.isPending

  useEffect(() => {
    if (!timeZone || editingId) return
    if (!tradedAt) setTradedAt(nowAccountDateTimeLocal(timeZone))
    if (!editingId) setCurrency(current => current || baseCurrency)
  }, [timeZone, baseCurrency, editingId, tradedAt])

  function resetEdit() {
    setEditingId(null); setSymbol(''); setSide('buy'); setQty(''); setPrice(''); setCurrency(baseCurrency)
    setTradedAt(timeZone ? nowAccountDateTimeLocal(timeZone) : ''); setNotes(''); setFormError(''); idem.reset()
  }

  function startEdit(t: Transaction) {
    setEditingId(t.id); setSymbol(t.symbol); setSide(t.side); setQty(String(t.quantity)); setPrice(String(t.price))
    setCurrency(t.currency)
    setTradedAt(timeZone ? utcToAccountDateTimeLocal(t.tradedAt, timeZone) : '')
    setNotes(t.notes ?? ''); setFormError(''); idem.reset()
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (saving) return
    setFormError('')

    const normalizedSymbol = symbol.trim().toUpperCase()
    const normalizedCurrency = currency.trim().toUpperCase()
    const quantityValue = Number(qty)
    const priceValue = Number(price)
    setSymbol(normalizedSymbol)
    setCurrency(normalizedCurrency)

    if (!normalizedSymbol) { setFormError(t('diary.trade.enterSymbol')); return }
    if (side !== 'buy' && side !== 'sell') { setFormError(t('diary.trade.chooseSide')); return }
    if (!Number.isFinite(quantityValue) || !Number.isFinite(priceValue) || quantityValue <= 0 || priceValue <= 0) {
      setFormError(t('diary.trade.qtyPricePositive')); return
    }
    if (!/^[A-Z]{3}$/.test(normalizedCurrency)) { setFormError(t('diary.trade.currencyCode')); return }
    if (!timeZone) { setFormError(t('diary.trade.timezoneStillLoading')); return }
    const converted = accountDateTimeLocalToUtc(tradedAt, timeZone)
    if (!converted.ok) {
      setFormError(converted.error === 'nonexistent'
        ? t('diary.trade.dstGap')
        : t('diary.trade.invalidTime'))
      return
    }

    const body = { symbol: normalizedSymbol, side, quantity: quantityValue, price: priceValue, currency: normalizedCurrency, tradedAt: converted.iso, notes }
    try {
      if (editingId) {
        await updateTransaction.mutateAsync({ id: editingId, body })
        resetEdit()
      } else {
        await createTransaction.mutateAsync({ body, key: idem.key() })
        setSymbol(''); setQty(''); setPrice(''); setNotes(''); setTradedAt(nowAccountDateTimeLocal(timeZone)); idem.reset()
      }
    } catch (mutationError: unknown) {
      setFormError(editingId ? transactionUpdateErrorMessage(locale, mutationError) : t('diary.trade.addError'))
    }
  }

  async function remove(trade: Transaction) {
    const ok = await confirm({
      title: t('diary.trade.deleteTitle'),
      message: t('diary.trade.deleteMessage', { side: trade.side.toUpperCase(), symbol: trade.symbol }),
      confirmText: t('common.delete'),
      tone: 'danger',
    })
    if (!ok) return
    try {
      await deleteTransaction.mutateAsync(trade.id)
      if (editingId === trade.id) resetEdit()
    } catch (mutationError: unknown) {
      setFormError(transactionDeleteErrorMessage(locale, mutationError))
    }
  }

  if (!timeZone) return <p className="is-muted">{t('diary.trade.timezoneLoading')}</p>

  return (
    <div className="trades">
      <form className="trades__form" onSubmit={submit}>
        <TextInput aria-label={t('diary.trade.symbol')} placeholder={t('diary.trade.symbol')} required disabled={saving} value={symbol} onChange={(e) => setSymbol(e.target.value.toUpperCase())} className="input--symbol" />
        <SelectBox aria-label={t('diary.trade.side')} disabled={saving} value={side} onChange={(e) => setSide(e.target.value)}>
          <option value="buy">{t('diary.trade.buy')}</option>
          <option value="sell">{t('diary.trade.sell')}</option>
        </SelectBox>
        <TextInput aria-label={t('diary.trade.qty')} type="number" min="0" step="any" inputMode="decimal" placeholder={t('diary.trade.qty')} required disabled={saving} value={qty} onChange={(e) => setQty(e.target.value)} className="num" />
        <TextInput aria-label={t('diary.trade.price')} type="number" min="0" step="any" inputMode="decimal" placeholder={t('diary.trade.price')} required disabled={saving} value={price} onChange={(e) => setPrice(e.target.value)} className="num" />
        <TextInput aria-label={t('diary.trade.currency')} placeholder="CCY" required maxLength={3} disabled={saving} value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} className="input--ccy" />
        <div className="trades__when">
          <TextInput aria-label={t('diary.trade.tradedAt')} type="datetime-local" required disabled={saving} value={tradedAt} onChange={(e) => setTradedAt(e.target.value)} className="input--when" />
          <span className="form-hint" title={timeZone}>{formatTimezoneLabel(timeZone)}</span>
        </div>
        <TextInput aria-label={t('diary.trade.notes')} placeholder={t('diary.trade.notesOptional')} disabled={saving} value={notes} onChange={(e) => setNotes(e.target.value)} />
        {editingId ? <Button variant="ghost" disabled={saving} onClick={resetEdit}>{t('common.cancel')}</Button> : null}
        <Button variant="primary" type="submit" icon={editingId ? 'check' : 'plus'} loading={saving} className="trades__add">{editingId ? t('diary.trade.save') : t('diary.trade.add')}</Button>
      </form>
      {formError ? <p className="form-error" role="alert">{formError}</p> : null}

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <div className="trades__rows">{Array.from({ length: 2 }, (_, i) => <div key={i} className="skel" style={{ height: 22 }} />)}</div>
      ) : items.length === 0 ? (
        <p className="trades__empty">{t('diary.noTrades')}</p>
      ) : (
        <ul className="trades__rows">
          {items.map((trade) => (
            <li key={trade.id} className="trade">
              <Badge tone={trade.side === 'buy' ? 'gain' : 'loss'}>{trade.side === 'buy' ? t('diary.trade.buy') : t('diary.trade.sell')}</Badge>
              <span className="trade__sym">{trade.symbol}</span>
              <span className="trade__qty num">{quantity(trade.quantity)} × {trade.price} {trade.currency}</span>
              {trade.notes ? <span className="trade__notes">{trade.notes}</span> : null}
              <div className="trade__actions">
                <IconButton icon="edit" label={t('diary.trade.editLabel', { symbol: trade.symbol })} size={15} aria-pressed={editingId === trade.id} disabled={saving || deleteTransaction.isPending} onClick={() => startEdit(trade)} />
                <IconButton icon="trash" label={t('diary.trade.deleteLabel')} size={15} className="icon-btn--danger" disabled={saving || deleteTransaction.isPending} onClick={() => remove(trade)} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/* ============================= CALENDAR ============================= */
