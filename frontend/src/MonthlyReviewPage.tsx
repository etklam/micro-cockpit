import { useEffect, useRef, useState } from 'react'
import { Navigate, Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useBootstrapQuery, useCalendarQuery, useDiaryReviewItemsQuery, useDiaryReviewSummaryQuery } from './features/queries'
import type { DiaryReviewAssessmentFilter, DiaryReviewFilterStatus } from './features/api'
import type { DiaryReviewItemResponse } from './generated/edge'
import { Button, Card, EmptyBox, ErrorBox, IconButton, PageHeader, SelectBox, Stat } from './ui'
import { cx, formatDate, monthLabel, signed } from './format'

export function MonthlyReviewRedirect() {
  const bootstrap = useBootstrapQuery()
  if (!bootstrap.data) return null
  const [year, month] = bootstrap.data.currentLocalDate.split('-')
  return <Navigate to={`/review/${year}/${month}`} replace />
}

export function MonthlyReviewPage() {
  const params = useParams()
  const rawYear = params.year ?? ''
  const rawMonth = params.month ?? ''
  const requestedMonth = Number(rawMonth)
  if (!/^\d{4}$/.test(rawYear) || !Number.isInteger(requestedMonth) || requestedMonth < 1 || requestedMonth > 12) return <Navigate to="/review" replace />
  const canonicalMonth = String(requestedMonth).padStart(2, '0')
  if (rawMonth !== canonicalMonth) return <Navigate to={`/review/${rawYear}/${canonicalMonth}`} replace />
  return <MonthlyReviewWorkspace year={Number(rawYear)} month={requestedMonth} />
}

function MonthlyReviewWorkspace({ year, month }: { year: number; month: number }) {
  const navigate = useNavigate()
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  const from = `${year}-${String(month).padStart(2, '0')}-01`
  const to = `${year}-${String(month).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`
  const calendar = useCalendarQuery(year, month)
  const reviews = useDiaryReviewSummaryQuery(from, to)
  const [search, setSearch] = useSearchParams()
  const reviewStatus = validStatus(search.get('reviewStatus'))
  const assessment = validAssessment(search.get('assessment'))
  const tag = validTag(search.get('tag'))
  const cursor = search.get('cursor') ?? ''

  const shift = (delta: number) => {
    const date = new Date(Date.UTC(year, month - 1 + delta, 1))
    navigate(`/review/${date.getUTCFullYear()}/${String(date.getUTCMonth() + 1).padStart(2, '0')}`)
  }

  return <>
    <PageHeader title="Monthly review" subtitle="A read-only view of decisions and results" />
    <div className="review-month-nav">
      <IconButton icon="left" label="Previous month" onClick={() => shift(-1)} />
      <h2>{monthLabel(year, month)}</h2>
      <IconButton icon="right" label="Next month" onClick={() => shift(1)} />
    </div>
    <Card className="review-separation" as="section">
      <strong>Process and outcome are reviewed separately.</strong>
      <span>A profitable month does not automatically mean the process was sound.</span>
    </Card>
    <div className="monthly-review-grid">
      <OutcomeSection year={year} month={month} calendar={calendar} />
      <div className="monthly-review-process-column">
        <ProcessSection calendar={calendar} reviews={reviews} />
        <EvidenceSection from={from} to={to} reviewStatus={reviewStatus} assessment={assessment} tag={tag} cursor={cursor} search={search} setSearch={setSearch} />
      </div>
    </div>
    <nav className="review-related" aria-label="Related review pages">
      <Link className="text-link" to="/diary">Open diary</Link>
      <Link className="text-link" to={`/calendar/${year}/${String(month).padStart(2, '0')}`}>Open calendar</Link>
    </nav>
  </>
}

type CalendarQuery = ReturnType<typeof useCalendarQuery>
type ReviewQuery = ReturnType<typeof useDiaryReviewSummaryQuery>

function OutcomeSection({ year, month, calendar }: { year: number; month: number; calendar: CalendarQuery }) {
  const summary = calendar.data?.summary
  const hasRecordedPerformance = summary != null && Number(summary.recordedDays) > 0
  const recorded = calendar.data?.days.filter(day => day.performance != null) ?? []
  return <section className="monthly-review-section" aria-labelledby="outcome-heading">
    <h2 id="outcome-heading">Outcome</h2>
    <p className="is-muted">Financial results recorded for this month.</p>
    {calendar.isError ? <ErrorBox message="Outcome data is unavailable." onRetry={() => { void calendar.refetch() }} />
      : calendar.isLoading ? <p className="is-muted">Loading outcome…</p>
      : !hasRecordedPerformance ? <EmptyBox title="No performance recorded this month" hint="Missing days are not counted as zero." dense />
      : <>
        <div className="monthly-review-stats">
          <Stat label="Net P/L" value={signed(summary.total)} tone={tone(summary.total)} />
          <Stat label="Recorded days" value={Number(summary.recordedDays)} />
          <Stat label="Winning days" value={Number(summary.profitDays)} tone="gain" />
          <Stat label="Losing days" value={Number(summary.lossDays)} tone="loss" />
          <Stat label="Flat days" value={Number(summary.flatDays)} />
          <Stat label="Best day" value={summary.bestDay == null ? 'Unavailable' : signed(summary.bestDay)} tone={summary.bestDay == null ? 'muted' : tone(summary.bestDay)} />
          <Stat label="Worst day" value={summary.worstDay == null ? 'Unavailable' : signed(summary.worstDay)} tone={summary.worstDay == null ? 'muted' : tone(summary.worstDay)} />
        </div>
        <DailyPnlChart year={year} month={month} days={recorded} />
      </>}
  </section>
}

function DailyPnlChart({ year, month, days }: { year: number; month: number; days: NonNullable<CalendarQuery['data']>['days'] }) {
  const max = Math.max(1, ...days.map(day => Math.abs(day.performance?.pnlAmount ?? 0)))
  return <div className="daily-pnl">
    <h3>Daily P/L</h3>
    <ul className="daily-pnl__chart" aria-label={`Daily P/L for ${monthLabel(year, month)}`}>
      {days.map(day => {
        const amount = day.performance!.pnlAmount
        const direction = amount > 0 ? 'gain' : amount < 0 ? 'loss' : 'flat'
        const dateLabel = new Date(`${day.date}T00:00:00Z`).toLocaleDateString('en-US', { month: 'long', day: 'numeric', timeZone: 'UTC' })
        return <li key={day.date} aria-label={`${dateLabel}, P/L ${amount}, ${direction}`} className="daily-pnl__day"><Link aria-label={`Open ${dateLabel} in calendar`} to={`/calendar/${year}/${String(month).padStart(2, '0')}?day=${day.date}`}>
          <span className="daily-pnl__amount">{signed(amount)}</span>
          <span className={cx('daily-pnl__track', `is-${direction}`)}><span style={{ height: amount === 0 ? 3 : `${Math.max(8, Math.abs(amount) / max * 100)}%` }} /></span>
          <time dateTime={day.date}>{Number(day.date.slice(-2))}</time>
        </Link></li>
      })}
    </ul>
  </div>
}

function ProcessSection({ calendar, reviews }: { calendar: CalendarQuery; reviews: ReviewQuery }) {
  const reviewed = Number(reviews.data?.reviewedCount ?? 0)
  const diaryCount = calendar.data?.days.reduce((sum, day) => sum + Number(day.diaryCount), 0)
  const coverage = diaryCount == null || diaryCount === 0 ? 'Unavailable' : `${reviewed} of ${diaryCount} diaries (${(reviewed / diaryCount * 100).toFixed(1)}%)`
  const remaining = diaryCount == null ? 'Review completion unavailable' : Math.max(0, diaryCount - reviewed) === 0 ? 'All monthly diaries reviewed' : `${Math.max(0, diaryCount - reviewed)} diaries still need review`
  return <section className="monthly-review-section" aria-labelledby="process-heading">
    <h2 id="process-heading">Process</h2>
    <p className="is-muted">Structured review patterns, without inferring outcomes.</p>
    {reviews.isError ? <ErrorBox message="Process data is unavailable." onRetry={() => { void reviews.refetch() }} />
      : reviews.isLoading ? <p className="is-muted">Loading process…</p>
      : <>
        <div className="monthly-review-stats">
          <Stat label="Reviewed entries" value={reviewed} />
          <Stat label="Review coverage" value={coverage} />
          <Stat label="Completion" value={remaining} />
          <Stat label="Average discipline" value={reviews.data?.averageDisciplineScore == null ? 'Unavailable' : Number(reviews.data.averageDisciplineScore).toFixed(1)} />
          <Stat label="Average execution" value={reviews.data?.averageExecutionScore == null ? 'Unavailable' : Number(reviews.data.averageExecutionScore).toFixed(1)} />
        </div>
        <Distribution title="Process assessments" values={reviews.data?.processAssessmentCounts ?? {}} />
        <Distribution title="Emotions" values={reviews.data?.emotionCounts ?? {}} />
        <div className="review-distribution"><h3>Top mistake tags</h3>{!reviews.data?.topMistakeTags.length ? <p className="is-muted">No mistake tags recorded.</p> : <ol>{[...reviews.data.topMistakeTags].sort((a, b) => Number(b.count) - Number(a.count) || a.tag.localeCompare(b.tag)).map(item => <li key={item.tag}><span>{label(item.tag)}</span><strong>{Number(item.count)}</strong></li>)}</ol>}</div>
      </>}
  </section>
}

type SearchSetter = ReturnType<typeof useSearchParams>[1]
const statusValues = ['all', 'reviewed', 'unreviewed'] as const
const assessmentValues = ['all', 'good', 'mixed', 'poor'] as const
const mistakeTags = ['no_plan', 'fomo', 'poor_timing', 'risk_violation', 'overtrading', 'ignored_signal', 'early_exit', 'late_exit', 'other'] as const
const validStatus = (value: string | null): DiaryReviewFilterStatus => statusValues.includes(value as DiaryReviewFilterStatus) ? value as DiaryReviewFilterStatus : 'all'
const validAssessment = (value: string | null): DiaryReviewAssessmentFilter => assessmentValues.includes(value as DiaryReviewAssessmentFilter) ? value as DiaryReviewAssessmentFilter : 'all'
const validTag = (value: string | null): string => mistakeTags.includes(value as typeof mistakeTags[number]) ? value! : ''

function EvidenceSection({ from, to, reviewStatus, assessment, tag, cursor, search, setSearch }: {
  from: string; to: string; reviewStatus: DiaryReviewFilterStatus; assessment: DiaryReviewAssessmentFilter; tag: string; cursor: string;
  search: URLSearchParams; setSearch: SearchSetter
}) {
  const query = useDiaryReviewItemsQuery(from, to, reviewStatus, assessment, tag, cursor)
  const [visible, setVisible] = useState<DiaryReviewItemResponse[]>([])
  const pages = useRef(new Map<string, DiaryReviewItemResponse[]>())
  const bases = useRef(new Map<string, DiaryReviewItemResponse[]>())
  const filterKey = `${from}:${to}:${reviewStatus}:${assessment}:${tag}`
  useEffect(() => { pages.current.clear(); bases.current.clear(); setVisible([]) }, [filterKey])
  useEffect(() => {
    const cached = pages.current.get(cursor)
    if (cached) { setVisible(cached); return }
    if (!query.data) return
    const combined = [...(bases.current.get(cursor) ?? []), ...query.data.items]
      .filter((item, index, items) => items.findIndex(candidate => candidate.diaryId === item.diaryId) === index)
    pages.current.set(cursor, combined); setVisible(combined)
  }, [cursor, query.data])

  const changeFilter = (name: 'reviewStatus' | 'assessment' | 'tag', value: string) => {
    const next = new URLSearchParams(search)
    if (((name === 'reviewStatus' || name === 'assessment') && value === 'all') || (name === 'tag' && !value)) next.delete(name)
    else next.set(name, value)
    next.delete('cursor'); setSearch(next)
  }
  const loadMore = () => {
    const nextCursor = query.data?.nextCursor
    if (!nextCursor) return
    bases.current.set(nextCursor, visible)
    const next = new URLSearchParams(search); next.set('cursor', nextCursor); setSearch(next)
  }

  return <section className="monthly-review-section review-evidence" aria-labelledby="evidence-heading">
    <h2 id="evidence-heading">Review evidence</h2>
    <div className="review-evidence__filters">
      <label>Review status<SelectBox aria-label="Review status" value={reviewStatus} onChange={event => changeFilter('reviewStatus', event.target.value)}>{statusValues.map(value => <option key={value} value={value}>{label(value)}</option>)}</SelectBox></label>
      <label>Assessment<SelectBox aria-label="Assessment" value={assessment} onChange={event => changeFilter('assessment', event.target.value)}>{assessmentValues.map(value => <option key={value} value={value}>{label(value)}</option>)}</SelectBox></label>
      <label>Mistake tag<SelectBox aria-label="Mistake tag" value={tag} onChange={event => changeFilter('tag', event.target.value)}><option value="">All</option>{mistakeTags.map(value => <option key={value} value={value}>{label(value)}</option>)}</SelectBox></label>
    </div>
    {query.isError && visible.length === 0 ? <ErrorBox message="Review evidence is unavailable." onRetry={() => { void query.refetch() }} />
      : query.isLoading && visible.length === 0 ? <p className="is-muted">Loading review evidence…</p>
      : visible.length === 0 ? <EmptyBox title="No matching diary evidence" dense />
      : <>
        <ul className="review-evidence__list">{visible.map(item => <EvidenceItem key={item.diaryId} item={item} />)}</ul>
        {query.isError ? <ErrorBox message="Could not load more review evidence." onRetry={() => { void query.refetch() }} /> : null}
        {query.data?.nextCursor ? <Button onClick={loadMore} loading={query.isFetching}>Load more</Button> : null}
      </>}
  </section>
}

function EvidenceItem({ item }: { item: DiaryReviewItemResponse }) {
  return <li><Card as="article" className="review-evidence__item">
    <div className="review-evidence__head"><time dateTime={item.localDate}>{formatDate(item.localDate)}</time><span className="badge badge--muted">{label(item.reviewStatus)}</span></div>
    <h3>{item.title}</h3><p>{item.contentPreview}</p>
    <div className="review-evidence__meta">
      {item.processAssessment ? <span>Assessment: {label(item.processAssessment)}</span> : null}
      {item.disciplineScore != null ? <span>Discipline: {item.disciplineScore}</span> : null}
      {item.executionScore != null ? <span>Execution: {item.executionScore}</span> : null}
      {item.emotion ? <span>Emotion: {label(item.emotion)}</span> : null}
    </div>
    {item.mistakeTags.length ? <p className="is-muted">Tags: {item.mistakeTags.map(label).join(', ')}</p> : null}
    {item.lesson ? <p><strong>Lesson:</strong> {item.lesson}</p> : null}{item.nextAction ? <p><strong>Next action:</strong> {item.nextAction}</p> : null}
    <Link className="text-link" to={`/diary/${item.diaryId}#decision-review`}>{item.reviewStatus === 'reviewed' ? 'Open review' : 'Complete review'}</Link>
  </Card></li>
}

function Distribution({ title, values }: { title: string; values: Record<string, number | string> }) {
  const entries = Object.entries(values).sort(([left, a], [right, b]) => Number(b) - Number(a) || left.localeCompare(right))
  return <div className="review-distribution"><h3>{title}</h3>{entries.length === 0 ? <p className="is-muted">No patterns recorded.</p> : <ol>{entries.map(([name, count]) => <li key={name}><span>{label(name)}</span><strong>{Number(count)}</strong></li>)}</ol>}</div>
}

const label = (value: string) => value.replaceAll('_', ' ').replace(/\b\w/g, letter => letter.toUpperCase())
const tone = (value: number): 'gain' | 'loss' | 'muted' => value > 0 ? 'gain' : value < 0 ? 'loss' : 'muted'
