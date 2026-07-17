import type { Components } from 'react-markdown'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

/** Allow only safe URL schemes; reject javascript:/data:/etc. */
export function safeUrlTransform(url: string): string {
  const value = url.trim()
  if (!value) return ''
  const lower = value.toLowerCase()
  if (lower.startsWith('https:') || lower.startsWith('http:') || lower.startsWith('mailto:') || lower.startsWith('#')) return value
  return ''
}

const components: Components = {
  a: ({ href, children }) => (
    <a href={href} rel="noopener noreferrer nofollow" target="_blank">{children}</a>
  ),
  // No remote image embedding in this phase.
  img: () => null,
}

export function MarkdownView({ content, className }: { content: string; className?: string }) {
  const source = content.trim()
  if (!source) return <p className={className ? `${className} is-muted` : 'is-muted'}>Nothing to preview.</p>
  return (
    <div className={className}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        urlTransform={safeUrlTransform}
        skipHtml
        components={components}
      >
        {source}
      </ReactMarkdown>
    </div>
  )
}

/** Plain-text excerpt safe for list cards (no raw HTML, no MD syntax noise). */
export function plainExcerpt(content: string, max = 180): string {
  let text = content.replace(/\r\n/g, '\n')
  text = text.replace(/```[\s\S]*?```/g, ' ')
  text = text.replace(/`([^`]+)`/g, '$1')
  text = text.replace(/!\[[^\]]*]\([^)]*\)/g, ' ')
  text = text.replace(/\[([^\]]+)]\([^)]*\)/g, '$1')
  text = text.replace(/^#{1,6}\s+/gm, '')
  text = text.replace(/^\s{0,3}([-*+]|\d+\.)\s+/gm, '')
  text = text.replace(/^\s{0,3}>\s?/gm, '')
  text = text.replace(/[*_~|]+/g, '')
  text = text.replace(/<[^>]*>/g, '')
  text = text.replace(/\s+/g, ' ').trim()
  if (text.length <= max) return text
  return `${text.slice(0, max - 1).trimEnd()}…`
}
