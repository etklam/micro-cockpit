import { Link } from 'react-router-dom'
import { Card } from './ui'
import { useI18n } from './i18n'
import type { ToolId } from './features/toolsCalc'

const FEATURES = [
  { titleKey: 'landing.feature.diary.title', bodyKey: 'landing.feature.diary.body' },
  { titleKey: 'landing.feature.calendar.title', bodyKey: 'landing.feature.calendar.body' },
  { titleKey: 'landing.feature.discipline.title', bodyKey: 'landing.feature.discipline.body' },
  { titleKey: 'landing.feature.review.title', bodyKey: 'landing.feature.review.body' },
] as const

const TOOLS: { id: ToolId; titleKey: 'landing.tool.positionSizing.title' | 'landing.tool.riskReward.title' | 'landing.tool.fire.title' | 'landing.tool.relativeValue.title' | 'landing.tool.seasonality.title'; bodyKey: 'landing.tool.positionSizing.body' | 'landing.tool.riskReward.body' | 'landing.tool.fire.body' | 'landing.tool.relativeValue.body' | 'landing.tool.seasonality.body' }[] = [
  { id: 'position-sizing', titleKey: 'landing.tool.positionSizing.title', bodyKey: 'landing.tool.positionSizing.body' },
  { id: 'risk-reward', titleKey: 'landing.tool.riskReward.title', bodyKey: 'landing.tool.riskReward.body' },
  { id: 'fire', titleKey: 'landing.tool.fire.title', bodyKey: 'landing.tool.fire.body' },
  { id: 'relative-value', titleKey: 'landing.tool.relativeValue.title', bodyKey: 'landing.tool.relativeValue.body' },
  { id: 'seasonality', titleKey: 'landing.tool.seasonality.title', bodyKey: 'landing.tool.seasonality.body' },
]

export function LandingPage() {
  const { t } = useI18n()
  return (
    <div className="landing">
      <section className="landing__hero">
        <p className="landing__eyebrow">{t('landing.eyebrow')}</p>
        <h1>{t('landing.title')}</h1>
        <p className="landing__lead">{t('landing.lead')}</p>
        <div className="landing__cta">
          <Link className="btn btn--primary" to="/register"><span className="btn__label">{t('landing.cta.register')}</span></Link>
          <Link className="btn btn--subtle" to="/login"><span className="btn__label">{t('landing.cta.signIn')}</span></Link>
          <Link className="btn btn--ghost" to="/tools"><span className="btn__label">{t('landing.cta.tools')}</span></Link>
        </div>
      </section>

      <section className="landing__section" aria-labelledby="landing-what">
        <h2 id="landing-what">{t('landing.what.title')}</h2>
        <p className="landing__section-lead">{t('landing.what.body')}</p>
        <div className="feature-grid">
          {FEATURES.map(item => (
            <Card key={item.titleKey} className="landing__card">
              <strong>{t(item.titleKey)}</strong>
              <span>{t(item.bodyKey)}</span>
            </Card>
          ))}
        </div>
      </section>

      <section className="landing__section" aria-labelledby="landing-tools">
        <div className="landing__section-head">
          <div>
            <h2 id="landing-tools">{t('landing.tools.title')}</h2>
            <p className="landing__section-lead">{t('landing.tools.body')}</p>
          </div>
          <Link className="btn btn--subtle btn--sm" to="/tools"><span className="btn__label">{t('landing.tools.open')}</span></Link>
        </div>
        <div className="feature-grid">
          {TOOLS.map(item => (
            <Link key={item.id} className="feature-link card" to={`/tools?tool=${item.id}`}>
              <strong>{t(item.titleKey)}</strong>
              <span>{t(item.bodyKey)}</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="landing__foot card">
        <h2>{t('landing.foot.title')}</h2>
        <p>{t('landing.foot.body')}</p>
        <div className="landing__cta">
          <Link className="btn btn--primary" to="/register"><span className="btn__label">{t('landing.cta.register')}</span></Link>
          <Link className="btn btn--subtle" to="/login"><span className="btn__label">{t('landing.cta.signIn')}</span></Link>
        </div>
      </section>
    </div>
  )
}
