import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useBootstrapQuery, useCalendarQuery } from '../../features/queries'
import { Card, IconButton, PageHeader } from '../../ui'
import { SectionError } from '../../shell'
import { cx, formatDate, monthLabel } from '../../format'
import { useI18n } from '../../i18n'
import { WEEKDAYS, validCalendarDay } from '../shared'

export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const { t } = useI18n()
  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentJournalDay
  const year = Number(params.year) || (accountToday ? Number(accountToday.slice(0, 4)) : new Date().getFullYear())
  const month = Number(params.month) || (accountToday ? Number(accountToday.slice(5, 7)) : new Date().getMonth() + 1)
  const cursor = { year, month }
  const [search, setSearch] = useSearchParams()
  const { data, isLoading: loading, isError: error, refetch: reload } = useCalendarQuery(year, month)
  const requestedDay = search.get('day')
  const defaultDay = accountToday && validCalendarDay(accountToday, year, month) ? accountToday : `${year}-${String(month).padStart(2, '0')}-01`
  const selectedFromUrl = validCalendarDay(requestedDay, year, month) ? requestedDay! : defaultDay
  const [selected, setSelected] = useState(selectedFromUrl)
  useEffect(() => { setSelected(selectedFromUrl) }, [selectedFromUrl])
  const day = data?.days.find(item => item.date === selected)
  const firstWeekday = new Date(cursor.year, cursor.month - 1, 1).getDay()

  const shift = (delta: number) => {
    const date = new Date(cursor.year, cursor.month - 1 + delta, 1)
    navigate(`/calendar/${date.getFullYear()}/${String(date.getMonth() + 1).padStart(2, '0')}`)
  }

  return <>
    <PageHeader title={t('calendar.title')} subtitle={t('calendar.observationSubtitle')} />
    <div className="cal-head">
      <IconButton icon="left" label={t('calendar.prevMonth')} onClick={() => shift(-1)} />
      <h2 className="cal-head__title">{monthLabel(cursor.year, cursor.month)}</h2>
      <IconButton icon="right" label={t('calendar.nextMonth')} onClick={() => shift(1)} />
    </div>
    <Link className="text-link cal-review-link" to="/review">{t('calendar.reviewMonth')}</Link>
    {error ? <SectionError onRetry={reload} /> : <Card flush as="section" className="cal">
      <div className="cal__weekdays">{WEEKDAYS.map(key => <span key={key}>{t(key)}</span>)}</div>
      <div className="cal__grid">
        {loading ? Array.from({ length: 35 }, (_, index) => <span key={index} className="day day--skel"><span className="skel" style={{ height: '100%' }} /></span>) : <>
          {Array.from({ length: firstWeekday }, (_, index) => <span key={`b${index}`} className="day day--blank" />)}
          {data?.days.map(item => {
            const label = item.updateCount > 0 ? t('calendar.day.observations', { count: item.updateCount }) : t('calendar.day.noObservations')
            return <button key={item.date} type="button" className={cx('day', selected === item.date && 'is-selected')} aria-label={`${formatDate(item.date)}, ${label}`} onClick={() => {
              setSelected(item.date)
              const next = new URLSearchParams(search); next.set('day', item.date); setSearch(next)
            }}>
              <span className="day__num num">{Number(item.date.slice(-2))}</span>
              <span className={cx('day__pnl', item.updateCount === 0 && 'is-dash')}>{item.updateCount || '·'}</span>
              {item.updateCount > 0 ? <span className="day__note" aria-label={label} /> : null}
            </button>
          })}
        </>}
      </div>
    </Card>}
    <Card as="section">
      <h2>{selected}</h2>
      {day?.updateCount ? <>
        <p>{t('calendar.day.observations', { count: day.updateCount })}</p>
        <Link className="text-link" to={`/today/observations?from=${selected}&to=${selected}`}>{t('calendar.openObservations')}</Link>
        {day.readyForReviewCount != null ? <p>{t('calendar.readyForReview', { count: day.readyForReviewCount })}</p> : null}
      </> : <p className="form-hint">{t('calendar.day.noObservations')}</p>}
    </Card>
  </>
}
