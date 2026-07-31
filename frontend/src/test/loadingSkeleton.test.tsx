import { render } from '@testing-library/react'
import { expect, test } from 'vitest'
import { PageSkeleton } from '../shell'

test('page loading uses skeleton placeholders instead of a plain loading paragraph', () => {
  const { container } = render(<PageSkeleton rows={1} />)
  expect(container.querySelectorAll('.skel').length).toBeGreaterThan(0)
  expect(container.querySelector('p')).toBeNull()
})
