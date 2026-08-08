import { useState, type FormEvent } from 'react'
import type { Discipline, DisciplinePrincipleStatus } from '../../features/api'
import { useCreateDisciplineMutation, useDisciplinesQuery, useSelectDisciplineMutation, useUpdateDisciplineMutation } from '../../features/queries'
import { Badge, Button, Card, EmptyBox, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'

export function DisciplinePrinciplesSection() {
  const { t } = useI18n()
  const [content, setContent] = useState('')
  const [formError, setFormError] = useState('')
  const { data, isLoading, isError, refetch } = useDisciplinesQuery()
  const create = useCreateDisciplineMutation()
  const update = useUpdateDisciplineMutation()
  const select = useSelectDisciplineMutation()
  const items = data?.items ?? []

  async function add(event: FormEvent) {
    event.preventDefault()
    setFormError('')
    try {
      await create.mutateAsync({ content: content.trim() })
      setContent('')
    } catch {
      setFormError(t('discipline.addError'))
    }
  }

  const setStatus = (principle: Discipline, status: DisciplinePrincipleStatus) => update.mutate({ id: principle.id, content: principle.content, status })

  return <Card as="section" className="stack review-disciplines" aria-labelledby="review-disciplines-title">
    <header><p className="eyebrow">{t('discipline.eyebrow')}</p><h2 id="review-disciplines-title">{t('discipline.title')}</h2><p className="form-hint">{t('discipline.subtitle')}</p></header>
    <form className="inline-form" onSubmit={add}><TextInput value={content} onChange={event => setContent(event.target.value)} placeholder={t('discipline.placeholder')} required maxLength={280} /><Button variant="primary" type="submit" icon="plus" loading={create.isPending}>{t('discipline.add')}</Button></form>
    {formError ? <p className="form-error" role="alert">{formError}</p> : null}
    {isError ? <SectionError onRetry={refetch} /> : isLoading ? <ul className="principle-list">{Array.from({ length: 3 }, (_, index) => <li key={index}><div className="skel" style={{ height: 48 }} /></li>)}</ul> : items.length === 0 ? <EmptyBox icon="compass" title={t('discipline.emptyTitle')} hint={t('discipline.emptyHint')} /> : <ol className="principle-list">{items.map(item => <li key={item.id}><article className="principle"><blockquote className="principle__text">{item.content}</blockquote>{item.confirmedPatternLabel ? <p className="form-hint">{t('discipline.fromPattern', { pattern: item.confirmedPatternLabel })}</p> : null}<div className="form-actions">
      {item.status === 'active' && !item.selectedForToday ? <Button size="sm" variant="subtle" onClick={() => select.mutate(item.id)}>{t('discipline.select')}</Button> : null}
      {item.selectedForToday ? <Badge tone="gain">{t('discipline.selected')}</Badge> : null}
      {item.status === 'active' ? <Button size="sm" variant="ghost" onClick={() => setStatus(item, 'disabled')}>{t('discipline.disable')}</Button> : null}
      {item.status === 'disabled' ? <Button size="sm" variant="ghost" onClick={() => setStatus(item, 'active')}>{t('discipline.enable')}</Button> : null}
      {item.status !== 'archived' ? <Button size="sm" variant="danger" onClick={() => setStatus(item, 'archived')}>{t('discipline.archive')}</Button> : <Badge tone="muted">{t('discipline.archived')}</Badge>}
    </div></article></li>)}</ol>}
  </Card>
}
