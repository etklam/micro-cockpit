import { Link } from 'react-router-dom'
import { Card, ThemeControls } from './ui'
import { Icon } from './icons'
import { useI18n } from './i18n'
import { TOOL_CATALOG } from './features/toolsCatalog'
import type { MessageKey } from './i18n'

const FEATURES: { titleKey: MessageKey; bodyKey: MessageKey }[] = [
  { titleKey: 'landing.feature.diary.title', bodyKey: 'landing.feature.diary.body' },
  { titleKey: 'landing.feature.calendar.title', bodyKey: 'landing.feature.calendar.body' },
  { titleKey: 'landing.feature.discipline.title', bodyKey: 'landing.feature.discipline.body' },
  { titleKey: 'landing.feature.review.title', bodyKey: 'landing.feature.review.body' },
]

const WORKFLOW: { titleKey: MessageKey; bodyKey: MessageKey }[] = [
  { titleKey: 'landing.workflow.1.title', bodyKey: 'landing.workflow.1.body' },
  { titleKey: 'landing.workflow.2.title', bodyKey: 'landing.workflow.2.body' },
  { titleKey: 'landing.workflow.3.title', bodyKey: 'landing.workflow.3.body' },
  { titleKey: 'landing.workflow.4.title', bodyKey: 'landing.workflow.4.body' },
]

const CAPABILITY_GROUPS: {
  titleKey: MessageKey
  badgeKey: MessageKey
  items: MessageKey[]
}[] = [
  {
    titleKey: 'landing.capability.reflect',
    badgeKey: 'landing.capability.account',
    items: ['landing.capability.diary', 'landing.capability.discipline', 'landing.capability.private'],
  },
  {
    titleKey: 'landing.capability.review',
    badgeKey: 'landing.capability.account',
    items: ['landing.capability.calendar', 'landing.capability.monthly', 'landing.capability.alerts'],
  },
  {
    titleKey: 'landing.capability.decide',
    badgeKey: 'landing.capability.public',
    items: ['landing.capability.tools'],
  },
]

const TRUST: MessageKey[] = [
  'landing.trust.journalFirst',
  'landing.trust.private',
  'landing.trust.sharing',
  'landing.trust.noBroker',
  'landing.trust.noHoldings',
  'landing.trust.notAdvice',
  'landing.trust.local',
]

export function LandingPage() {
  const { t, locale, setLocale } = useI18n()
  return (
    <div className="landing">
      <section className="landing__hero landing__hero--split" aria-labelledby="landing-title">
        <div className="landing__hero-copy">
          <p className="landing__eyebrow">{t('landing.eyebrow')}</p>
          <h1 id="landing-title">{t('landing.title')}</h1>
          <p className="landing__lead">{t('landing.lead')}</p>
          <div className="landing__cta">
            <Link className="btn btn--primary" to="/register"><span className="btn__label">{t('landing.cta.register')}</span></Link>
            <Link className="btn btn--subtle" to="/login"><span className="btn__label">{t('landing.cta.signIn')}</span></Link>
            <Link className="btn btn--ghost" to="/tools?tool=position-sizing"><span className="btn__label">{t('landing.cta.tools')}</span></Link>
          </div>
        </div>
        <aside className="landing__preview card" aria-label={t('landing.preview.badge')}>
          <p className="landing__preview-badge">{t('landing.preview.badge')}</p>
          <div className="landing__preview-row">
            <span className="landing__preview-label">{t('landing.preview.quickNote')}</span>
            <strong>{t('landing.preview.quickNoteValue')}</strong>
          </div>
          <div className="landing__preview-row">
            <span className="landing__preview-label">{t('landing.preview.diary')}</span>
            <strong>{t('landing.preview.diaryValue')}</strong>
          </div>
          <div className="landing__preview-row">
            <span className="landing__preview-label">{t('landing.preview.pnl')}</span>
            <strong className="num gain">+1,240</strong>
          </div>
          <div className="landing__preview-row">
            <span className="landing__preview-label">{t('landing.preview.discipline')}</span>
            <em className="landing__preview-quote">{t('landing.preview.disciplineValue')}</em>
          </div>
        </aside>
      </section>

      <section className="landing__section" id="product" aria-labelledby="landing-workflow">
        <h2 id="landing-workflow">{t('landing.workflow.title')}</h2>
        <p className="landing__section-lead">{t('landing.workflow.body')}</p>
        <ol className="landing__workflow">
          {WORKFLOW.map((step, i) => (
            <li key={step.titleKey} className="landing__workflow-step card">
              <span className="landing__workflow-n" aria-hidden="true">{i + 1}</span>
              <strong>{t(step.titleKey)}</strong>
              <span>{t(step.bodyKey)}</span>
            </li>
          ))}
        </ol>
      </section>

      <section className="landing__section" id="features" aria-labelledby="landing-what">
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

      <section className="landing__section" aria-labelledby="landing-capability">
        <h2 id="landing-capability">{t('landing.capability.title')}</h2>
        <p className="landing__section-lead">{t('landing.capability.body')}</p>
        <div className="landing__capability-grid">
          {CAPABILITY_GROUPS.map(group => (
            <Card key={group.titleKey} className="landing__capability">
              <div className="landing__capability-head">
                <strong>{t(group.titleKey)}</strong>
                <span className="landing__capability-badge">{t(group.badgeKey)}</span>
              </div>
              <ul>
                {group.items.map(key => (
                  <li key={key}>{t(key)}</li>
                ))}
              </ul>
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
          <Link className="btn btn--subtle btn--sm" to="/tools?tool=position-sizing">
            <span className="btn__label">{t('landing.tools.open')}</span>
          </Link>
        </div>
        <div className="feature-grid">
          {TOOL_CATALOG.map(item => (
            <Link key={item.id} className="feature-link card landing__tool-card" to={item.href}>
              <span className="landing__tool-top">
                <Icon name={item.icon} size={18} />
                <span className="landing__tool-free">{t('landing.tools.free')}</span>
              </span>
              <strong>{t(item.titleKey)}</strong>
              <span>{t(item.bodyKey)}</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="landing__section" aria-labelledby="landing-trust">
        <h2 id="landing-trust">{t('landing.trust.title')}</h2>
        <p className="landing__section-lead">{t('landing.trust.body')}</p>
        <ul className="landing__trust-list">
          {TRUST.map(key => (
            <li key={key}>{t(key)}</li>
          ))}
        </ul>
      </section>

      <section className="landing__foot card">
        <h2>{t('landing.foot.title')}</h2>
        <p>{t('landing.foot.body')}</p>
        <div className="landing__cta">
          <Link className="btn btn--primary" to="/register"><span className="btn__label">{t('landing.cta.register')}</span></Link>
          <Link className="btn btn--subtle" to="/login"><span className="btn__label">{t('landing.cta.signIn')}</span></Link>
        </div>
      </section>

      <footer className="landing__footer">
        <div className="landing__footer-links">
          <a href="#product">{t('landing.footer.product')}</a>
          <a href="#features">{t('landing.footer.features')}</a>
          <Link to="/tools?tool=position-sizing">{t('landing.footer.tools')}</Link>
          <Link to="/login">{t('landing.footer.signIn')}</Link>
          <Link to="/register">{t('landing.footer.register')}</Link>
        </div>
        <div className="landing__footer-controls">
          <div className="lang-toggle" role="group" aria-label={t('settings.language')}>
            <button type="button" className={locale === 'en' ? 'is-active' : undefined} onClick={() => { void setLocale('en') }}>EN</button>
            <button type="button" className={locale === 'zh-Hant' ? 'is-active' : undefined} onClick={() => { void setLocale('zh-Hant') }}>繁</button>
          </div>
          <ThemeControls />
        </div>
        <p className="landing__footer-disclaimer">{t('landing.footer.disclaimer')}</p>
      </footer>
    </div>
  )
}
