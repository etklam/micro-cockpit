import { Button, Card } from '../../ui'
import { useI18n } from '../../i18n'

type AccountSectionProps = {
  busy: 'export' | 'delete' | ''
  error: string
  onExport: () => void
  onDelete: () => void
}

export function AccountSection({ busy, error, onExport, onDelete }: AccountSectionProps) {
  const { t } = useI18n()
  return <Card className="settings-form stack">
    <section className="stack">
      <h2>{t('settings.account.title')}</h2>
      <p className="form-hint">{t('settings.account.exportHint')}</p>
      <div className="form-actions"><Button onClick={onExport} loading={busy === 'export'}>{t('settings.account.export')}</Button></div>
      <p className="form-hint">{t('settings.account.deleteHint')}</p>
      <div className="form-actions"><Button variant="danger" onClick={onDelete} loading={busy === 'delete'}>{t('settings.account.delete')}</Button></div>
      {error ? <p className="form-error" role="alert">{error}</p> : null}
    </section>
  </Card>
}
