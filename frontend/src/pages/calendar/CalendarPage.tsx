import { useEffect, useMemo, useState, type KeyboardEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  useBootstrapQuery,
  useCalendarQuery,
  useExpectationsQuery,
  useObservationHistoryQuery,
  useTodayDisciplineQuery,
  useQuickObservationMutation,
} from '../../features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, TextArea } from '../../ui'
import { Icon } from '../../icons'
import { SectionError } from '../../shell'
import { cx, formatDate, formatLongDate, monthLabel } from '../../format'
import { useIdempotencyKey } from '../../features/api'
import { useI18n } from '../../i18n'
import { ActionDecisionPanel } from '../today/ActionDecisionPanel'
import { WEEKDAYS, validCalendarDay } from '../shared'

function observationCopy(content: string) {
  const lines = content.trim().split(/\n+/).map(line => line.trim()).filter(Boolean)
  return { title: lines[0] ?? content.trim(), preview: lines.slice(1).join(' ') }
}

function isoDate(year: number, month: number, day: number) {
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

function CalendarObservation({ update, timezone, locale }: { update: NonNullable<ReturnType<typeof useObservationHistoryQuery>['data']>['pages'][number]['items'][number]['update']; timezone: string; locale: string }) {
  const copy = observationCopy(update.content)
  const time = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: timezone })
  return <article className="calendar-detail__observation">
    <div className="calendar-detail__observation-time"><Icon name="today" size={14} /><time dateTime={update.recordedAt}>{time.format(new Date(update.recordedAt))}</time></div>
    <h3>{copy.title}</h3>
    {copy.preview ? <p>{copy.preview}</p> : null}
    <div className="calendar-detail__observation-meta">
      {update.primarySubject ? <Badge tone="primary">{update.primarySubject.symbol ?? update.primarySubject.name ?? update.primarySubject.displayName ?? update.primarySubject.type}</Badge> : null}
      {update.tags.map(tag => <Badge key={tag}>{tag}</Badge>)}
    </div>
  </article>
}

export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const { t, locale } = useI18n()
  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentJournalDay
  const year = Number(params.year) || (accountToday ? Number(accountToday.slice(0, 4)) : new Date().getFullYear())
  const month = Number(params.month) || (accountToday ? Number(accountToday.slice(5, 7)) : new Date().getMonth() + 1)
  const cursor = { year, month }
  const [search, setSearch] = useSearchParams()
  const calendar = useCalendarQuery(year, month)
  const expectations = useExpectationsQuery()
  const requestedDay = search.get('day')
  const defaultDay = accountToday && validCalendarDay(accountToday, year, month) ? accountToday : isoDate(year, month, 1)
  const selectedFromUrl = validCalendarDay(requestedDay, year, month) ? requestedDay! : defaultDay
  const [selected, setSelected] = useState(selectedFromUrl)
  const [detailOpen, setDetailOpen] = useState(true)

  useEffect(() => { setSelected(selectedFromUrl) }, [selectedFromUrl])

  const selectedHistory = useObservationHistoryQuery({ from: selected, to: selected })
  const selectedUpdates = useMemo(
    () => selectedHistory.data?.pages.flatMap(page => page.items) ?? [],
    [selectedHistory.data?.pages],
  )
  const selectedUpdateIds = useMemo(() => new Set(selectedUpdates.map(item => item.update.id)), [selectedUpdates])
  const selectedExpectations = useMemo(
    () => (expectations.data ?? []).filter(item => selectedUpdateIds.has(item.observationUpdateId)),
    [expectations.data, selectedUpdateIds],
  )
  const todayDiscipline = useTodayDisciplineQuery()
  const saveQuickObservation = useQuickObservationMutation()
  const observationIdem = useIdempotencyKey()
  const [composerOpen, setComposerOpen] = useState(false)
  const [composerContent, setComposerContent] = useState('')
  const [composerError, setComposerError] = useState(false)
  const [composerSaved, setComposerSaved] = useState(false)
  const selectedDay = calendar.data?.days.find(item => item.date === selected)
  const firstWeekday = new Date(Date.UTC(cursor.year, cursor.month - 1, 1)).getUTCDay()
  const activeDays = calendar.data?.days.filter(item => item.updateCount > 0).length ?? 0
  const recordCount = calendar.data?.days.reduce((sum, item) => sum + item.updateCount, 0) ?? 0
  const readyCountAvailable = calendar.data?.days.some(item => item.readyForReviewCount != null)
  const readyCount = readyCountAvailable ? calendar.data?.days.reduce((sum, item) => sum + (item.readyForReviewCount ?? 0), 0) ?? 0 : null

  const selectDay = (date: string) => {
    setSelected(date)
    setDetailOpen(true)
    const next = new URLSearchParams(search)
    next.set('day', date)
    setSearch(next, { replace: true })
  }

  const shift = (delta: number) => {
    const date = new Date(Date.UTC(cursor.year, cursor.month - 1 + delta, 1))
    navigate(`/calendar/${date.getUTCFullYear()}/${String(date.getUTCMonth() + 1).padStart(2, '0')}`)
  }

  const goToday = () => {
    if (accountToday) {
      const todayYear = Number(accountToday.slice(0, 4))
      const todayMonth = Number(accountToday.slice(5, 7))
      navigate(`/calendar/${todayYear}/${String(todayMonth).padStart(2, '0')}?day=${accountToday}`)
      return
    }
    selectDay(isoDate(new Date().getFullYear(), new Date().getMonth() + 1, new Date().getDate()))
  }

  const onDayKeyDown = (event: KeyboardEvent<HTMLButtonElement>, date: string) => {
    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
    event.preventDefault()
    const days = calendar.data?.days ?? []
    const currentIndex = days.findIndex(item => item.date === date)
    if (currentIndex < 0) return
    const currentCellIndex = firstWeekday + currentIndex
    const lastCellIndex = firstWeekday + days.length - 1
    const requestedCellIndex = event.key === 'ArrowLeft'
      ? currentCellIndex - 1
      : event.key === 'ArrowRight'
        ? currentCellIndex + 1
        : event.key === 'ArrowUp'
          ? currentCellIndex - 7
          : event.key === 'ArrowDown'
            ? currentCellIndex + 7
            : event.key === 'Home'
              ? firstWeekday
              : lastCellIndex
    const nextCellIndex = Math.max(firstWeekday, Math.min(lastCellIndex, requestedCellIndex))
    const next = days[nextCellIndex - firstWeekday]
    if (next) selectDay(next.date)
  }

  const selectedEmpty = !selectedHistory.isLoading && !selectedHistory.isError && selectedUpdates.length === 0
  const selectedIsToday = selected === accountToday

  useEffect(() => {
    setComposerOpen(false)
    setComposerContent('')
    setComposerError(false)
    setComposerSaved(false)
  }, [selected])

  const openComposer = () => {
    setComposerError(false)
    setComposerSaved(false)
    setComposerOpen(true)
  }

  const saveSelectedObservation = async () => {
    const content = composerContent.trim()
    if (!content) return
    setComposerError(false)
    try {
      await saveQuickObservation.mutateAsync({ content, key: observationIdem.key(), journalDay: selected })
      setComposerContent('')
      setComposerOpen(false)
      setComposerSaved(true)
      observationIdem.reset()
    } catch {
      setComposerError(true)
    }
  }

  return <>
    <PageHeader
      title={t('calendar.title')}
      subtitle={t('calendar.subtitleDescription')}
      actions={<div className="calendar-head__actions">
        <Button variant="ghost" size="sm" onClick={goToday}>{t('calendar.today')}</Button>
        {selectedIsToday ? <Link className="btn btn--primary btn--sm" to="/today#composer">
          <Icon name="plus" size={15} />
          <span className="btn__label">{t('calendar.addObservation')}</span>
        </Link> : <Button variant="primary" size="sm" icon="plus" onClick={openComposer}>{t('calendar.addObservation')}</Button>}
      </div>}
    />

    <div className="calendar-toolbar">
      <Button variant="ghost" size="sm" onClick={() => shift(-1)} icon="left" aria-label={t('calendar.prevMonth')}>{t('calendar.prevMonthShort')}</Button>
      <h2>{monthLabel(cursor.year, cursor.month)}</h2>
      <Button variant="ghost" size="sm" onClick={() => shift(1)} icon="right" aria-label={t('calendar.nextMonth')}>{t('calendar.nextMonthShort')}</Button>
      <span className="calendar-toolbar__view">{t('calendar.monthView')}</span>
    </div>

    {!calendar.isLoading && calendar.data ? <Card as="section" className="calendar-summary" aria-label={t('calendar.summary.label')}>
      <div><span>{t('calendar.summary.records')}</span><strong>{recordCount}</strong></div>
      <div><span>{t('calendar.summary.activeDays')}</span><strong>{activeDays}</strong></div>
      {readyCount != null ? <div><span>{t('calendar.summary.ready')}</span><strong>{readyCount}</strong></div> : null}
    </Card> : null}

    {calendar.isError ? <SectionError onRetry={() => { void calendar.refetch() }} /> : <div className={cx('calendar-workspace', !detailOpen && 'is-detail-closed')}>
      <Card flush as="section" className="cal" aria-label={t('calendar.gridLabel')}>
        <div className="cal__weekdays">{WEEKDAYS.map(key => <span key={key}>{t(key)}</span>)}</div>
        <div className="cal__grid" role="grid" aria-label={monthLabel(cursor.year, cursor.month)}>
          {calendar.isLoading ? Array.from({ length: 35 }, (_, index) => <span key={index} className="day day--skel"><span className="skel" style={{ height: '100%' }} /></span>) : <>
            {Array.from({ length: firstWeekday }, (_, index) => <span key={`blank-${index}`} className="day day--blank" aria-hidden="true" />)}
            {calendar.data?.days.map(item => {
              const isToday = item.date === accountToday
              const isSelected = item.date === selected
              const hasReview = (item.readyForReviewCount ?? 0) > 0
              const label = item.updateCount > 0
                ? t('calendar.day.observations', { count: item.updateCount })
                : t('calendar.day.noObservations')
              return <button
                key={item.date}
                type="button"
                role="gridcell"
                tabIndex={isSelected || (!calendar.data?.days.some(day => day.date === selected) && item.date === calendar.data.days[0]?.date) ? 0 : -1}
                className={cx('day', isSelected && 'is-selected', isToday && 'is-today', item.updateCount > 0 && 'has-activity', hasReview && 'needs-review')}
                aria-label={`${formatDate(item.date)}, ${label}${isToday ? `, ${t('calendar.todayIndicator')}` : ''}${isSelected ? `, ${t('calendar.selectedIndicator')}` : ''}`}
                aria-current={isToday ? 'date' : undefined}
                aria-pressed={isSelected}
                onClick={() => selectDay(item.date)}
                onKeyDown={event => onDayKeyDown(event, item.date)}
              >
                <span className="day__top"><span className="day__num num">{Number(item.date.slice(-2))}</span>{isToday ? <span className="day__today">{t('calendar.today')}</span> : null}</span>
                {item.updateCount > 0 ? <span className="day__activity">{t('calendar.day.activity', { count: item.updateCount })}</span> : null}
                {hasReview ? <span className="day__review">{t('calendar.day.review', { count: item.readyForReviewCount ?? 0 })}</span> : null}
              </button>
            })}
          </>}
        </div>
      </Card>

      {detailOpen ? <Card as="section" className="calendar-detail" aria-labelledby="calendar-detail-title" role="complementary">
        <div className="calendar-detail__head">
          <div><p className="eyebrow">{t('calendar.detail.eyebrow')}</p><h2 id="calendar-detail-title" aria-live="polite">{formatLongDate(selected)}</h2></div>
          <IconButton icon="close" label={t('calendar.detail.close')} onClick={() => setDetailOpen(false)} />
        </div>
        {selectedDay?.updateCount ? <div className="calendar-detail__status"><Badge tone="primary">{t('calendar.detail.active')}</Badge><span>{t('calendar.day.observations', { count: selectedDay.updateCount })}</span>{selectedDay.readyForReviewCount != null ? <span>{t('calendar.readyForReview', { count: selectedDay.readyForReviewCount })}</span> : null}</div> : null}
        {composerSaved ? <p className="calendar-detail__saved" role="status">{t('calendar.detail.saved')}</p> : null}
        {composerOpen ? <form className="calendar-detail__composer" onSubmit={event => { event.preventDefault(); void saveSelectedObservation() }}>
          <Field label={t('calendar.detail.composeLabel')} hint={selectedIsToday ? undefined : t('calendar.detail.historicalContext', { date: formatLongDate(selected) })} error={composerError ? t('calendar.detail.saveError') : undefined}>
            <TextArea autoFocus value={composerContent} onChange={event => setComposerContent(event.target.value)} placeholder={t('calendar.detail.composePlaceholder')} aria-label={t('calendar.detail.composeLabel')} maxLength={1000} />
          </Field>
          <div className="calendar-detail__composer-actions">
            <Button type="submit" variant="primary" size="sm" loading={saveQuickObservation.isPending} disabled={!composerContent.trim()}>{t('calendar.detail.save')}</Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => setComposerOpen(false)}>{t('calendar.detail.cancel')}</Button>
          </div>
        </form> : null}
        {selectedHistory.isLoading ? <p className="form-hint" role="status">{t('common.loading')}</p> : selectedHistory.isError ? <SectionError onRetry={() => { void selectedHistory.refetch() }} /> : selectedEmpty ? <EmptyBox dense icon="diary" title={t('calendar.detail.emptyTitle')} hint={t('calendar.detail.emptyHint')} action={<div className="calendar-detail__actions"><Button variant="primary" size="sm" onClick={openComposer}>{t('calendar.addObservation')}</Button><Link className="btn btn--ghost btn--sm" to={`/today/observations?from=${selected}&to=${selected}`}>{t('calendar.detail.openJournalDay')}</Link></div>} /> : <>
          <section className="calendar-detail__section" aria-labelledby="calendar-observations-title">
            <h3 id="calendar-observations-title">{t('calendar.detail.observations')}</h3>
            <div className="calendar-detail__observations">{selectedUpdates.map(item => <div key={item.update.id}><CalendarObservation update={item.update} timezone={bootstrap.data?.timezone ?? 'UTC'} locale={locale} /><ActionDecisionPanel updateId={item.update.id} expectations={selectedExpectations.filter(expectation => expectation.observationUpdateId === item.update.id)} /></div>)}</div>
          </section>
          {selectedExpectations.length ? <section className="calendar-detail__section" aria-labelledby="calendar-expectations-title"><h3 id="calendar-expectations-title">{t('calendar.detail.expectations')}</h3><ul className="calendar-detail__list">{selectedExpectations.map(expectation => <li key={expectation.id}><strong>{expectation.expectedBehavior}</strong><span>{expectation.confidence} · {expectation.readiness}</span></li>)}</ul></section> : null}
          {selected === accountToday && todayDiscipline.data ? <section className="calendar-detail__section"><h3>{t('calendar.detail.discipline')}</h3><blockquote>{todayDiscipline.data.content}</blockquote></section> : null}
          <div className="calendar-detail__actions">{selectedIsToday ? <Link className="btn btn--primary btn--sm" to="/today#composer">{t('calendar.addObservation')}</Link> : <Button variant="primary" size="sm" onClick={openComposer}>{t('calendar.addObservation')}</Button>}<Link className="btn btn--ghost btn--sm" to={`/today/observations?from=${selected}&to=${selected}`}>{t('calendar.detail.openJournalDay')}</Link></div>
        </>}
      </Card> : <Button className="calendar-detail__reopen" variant="subtle" onClick={() => setDetailOpen(true)}>{t('calendar.detail.open')}</Button>}
    </div>}
  </>
}
