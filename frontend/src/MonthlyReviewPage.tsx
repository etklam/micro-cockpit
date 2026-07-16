import { Navigate, Link, useNavigate, useParams } from 'react-router-dom'
import { useBootstrapQuery, useCalendarQuery, useDiaryReviewSummaryQuery } from './features/queries'
import { Card, EmptyBox, ErrorBox, IconButton, PageHeader, Stat } from './ui'
import { cx, monthLabel, signed } from './format'

export function MonthlyReviewRedirect() {
  const bootstrap = useBootstrapQuery()
  if (!bootstrap.data) return null
  const [year, month] = bootstrap.data.currentLocalDate.split('-')
  return <Navigate to={`/review/${year}/${month}`} replace />
}

export function MonthlyReviewPage() {
  const params = useParams()
  const requestedYear = Number(params.year)
  const requestedMonth = Number(params.month)
  const validMonth = Number.isInteger(requestedYear) && requestedYear > 0 && Number.isInteger(requestedMonth) && requestedMonth > 0 && requestedMonth <= 12
  if (!validMonth) return <Navigate to="/review" replace />
  return <MonthlyReviewWorkspace year={requestedYear} month={requestedMonth} />
}

function MonthlyReviewWorkspace({ year, month }: { year: number; month: number }) {
  const navigate = useNavigate()
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  const from = `${year}-${String(month).padStart(2, '0')}-01`
  const to = `${year}-${String(month).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`
  const calendar = useCalendarQuery(year, month)
  const reviews = useDiaryReviewSummaryQuery(from, to)

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
      <ProcessSection calendar={calendar} reviews={reviews} />
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
        return <li key={day.date} aria-label={`${dateLabel}, P/L ${amount}, ${direction}`} className="daily-pnl__day">
          <span className="daily-pnl__amount">{signed(amount)}</span>
          <span className={cx('daily-pnl__track', `is-${direction}`)}><span style={{ height: amount === 0 ? 3 : `${Math.max(8, Math.abs(amount) / max * 100)}%` }} /></span>
          <time dateTime={day.date}>{Number(day.date.slice(-2))}</time>
        </li>
      })}
    </ul>
  </div>
}

function ProcessSection({ calendar, reviews }: { calendar: CalendarQuery; reviews: ReviewQuery }) {
  const reviewed = Number(reviews.data?.reviewedCount ?? 0)
  const diaryCount = calendar.data?.days.reduce((sum, day) => sum + Number(day.diaryCount), 0)
  const coverage = diaryCount == null || diaryCount === 0 ? 'Unavailable' : `${reviewed} of ${diaryCount} diaries (${(reviewed / diaryCount * 100).toFixed(1)}%)`
  return <section className="monthly-review-section" aria-labelledby="process-heading">
    <h2 id="process-heading">Process</h2>
    <p className="is-muted">Structured review patterns, without inferring outcomes.</p>
    {reviews.isError ? <ErrorBox message="Process data is unavailable." onRetry={() => { void reviews.refetch() }} />
      : reviews.isLoading ? <p className="is-muted">Loading process…</p>
      : <>
        <div className="monthly-review-stats">
          <Stat label="Reviewed entries" value={reviewed} />
          <Stat label="Review coverage" value={coverage} />
          <Stat label="Average discipline" value={reviews.data?.averageDisciplineScore == null ? 'Unavailable' : Number(reviews.data.averageDisciplineScore).toFixed(1)} />
          <Stat label="Average execution" value={reviews.data?.averageExecutionScore == null ? 'Unavailable' : Number(reviews.data.averageExecutionScore).toFixed(1)} />
        </div>
        <Distribution title="Process assessments" values={reviews.data?.processAssessmentCounts ?? {}} />
        <Distribution title="Emotions" values={reviews.data?.emotionCounts ?? {}} />
        <div className="review-distribution"><h3>Top mistake tags</h3>{!reviews.data?.topMistakeTags.length ? <p className="is-muted">No mistake tags recorded.</p> : <ol>{[...reviews.data.topMistakeTags].sort((a, b) => Number(b.count) - Number(a.count) || a.tag.localeCompare(b.tag)).map(item => <li key={item.tag}><span>{label(item.tag)}</span><strong>{Number(item.count)}</strong></li>)}</ol>}</div>
      </>}
  </section>
}

function Distribution({ title, values }: { title: string; values: Record<string, number | string> }) {
  const entries = Object.entries(values).sort(([left, a], [right, b]) => Number(b) - Number(a) || left.localeCompare(right))
  return <div className="review-distribution"><h3>{title}</h3>{entries.length === 0 ? <p className="is-muted">No patterns recorded.</p> : <ol>{entries.map(([name, count]) => <li key={name}><span>{label(name)}</span><strong>{Number(count)}</strong></li>)}</ol>}</div>
}

const label = (value: string) => value.replaceAll('_', ' ').replace(/\b\w/g, letter => letter.toUpperCase())
const tone = (value: number): 'gain' | 'loss' | 'muted' => value > 0 ? 'gain' : value < 0 ? 'loss' : 'muted'
