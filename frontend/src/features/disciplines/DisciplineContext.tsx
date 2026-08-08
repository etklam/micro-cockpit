import { useDisciplinesQuery } from '../queries'
import { useI18n } from '../../i18n'
import './DisciplineContext.css'

export function DisciplineContext() {
  const { t } = useI18n()
  const disciplines = useDisciplinesQuery()
  const active = (disciplines.data?.items ?? []).filter(item => item.status === 'active')

  if (disciplines.isLoading || disciplines.isError || active.length === 0) return null

  return <details className="discipline-context">
    <summary>{t('discipline.context.summary', { count: active.length })}</summary>
    <p>{t('discipline.context.hint')}</p>
    <ul>{active.map(item => <li key={item.id}>{item.content}</li>)}</ul>
  </details>
}
