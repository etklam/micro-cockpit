import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const source = resolve(root, 'contracts/openapi/edge-api.openapi.json')
const target = resolve(root, 'frontend/src/generated/edge.ts')
const api = JSON.parse(readFileSync(source, 'utf8'))

const refName = (ref) => ref.split('/').at(-1)
const typeOf = (schema = {}) => {
  if (schema.$ref) return refName(schema.$ref)
  if (Array.isArray(schema.type)) {
    // .NET's schema exporter marks decimal as ["number","string"] (string preserves precision),
    // often nullable as ["null","number","string"]. The Edge runtime emits JSON numbers, so collapse.
    const nonNull = schema.type.filter((t) => t !== 'null')
    if (nonNull.length === 2 && nonNull.includes('number') && nonNull.includes('string')) {
      return schema.type.includes('null') ? 'number | null' : 'number'
    }
    return schema.type.map((type) => type === 'null' ? 'null' : typeOf({ ...schema, type })).join(' | ')
  }
  if (schema.enum) return schema.enum.map(JSON.stringify).join(' | ')
  if (schema.oneOf || schema.anyOf) return (schema.oneOf || schema.anyOf).map(typeOf).join(' | ')
  if (schema.allOf) return schema.allOf.map(typeOf).join(' & ')
  if (schema.type === 'array') return `Array<${typeOf(schema.items)}>`
  if (schema.type === 'object' || schema.properties || schema.additionalProperties) {
    const required = new Set(schema.required || [])
    const fields = Object.entries(schema.properties || {}).map(([name, value]) => `${JSON.stringify(name)}${required.has(name) ? '' : '?'}: ${typeOf(value)}`)
    if (schema.additionalProperties) fields.push(`[key: string]: ${schema.additionalProperties === true ? 'unknown' : typeOf(schema.additionalProperties)}`)
    return `{ ${fields.join('; ')} }`
  }
  return ({ string: 'string', number: 'number', integer: 'number', boolean: 'boolean', null: 'null' })[schema.type] || 'unknown'
}

const successSchema = (operation) => {
  for (const [status, response] of Object.entries(operation.responses || {})) {
    if (!status.startsWith('2')) continue
    return response.content?.['application/json']?.schema || null
  }
  return null
}
const camel = (value) => value.replace(/_([a-z0-9])/g, (_, char) => char.toUpperCase())
const methods = new Set(['get', 'post', 'put', 'patch', 'delete'])
const operations = []
for (const [path, pathItem] of Object.entries(api.paths)) {
  for (const [method, operation] of Object.entries(pathItem)) {
    if (!methods.has(method)) continue
    const parameters = [...(pathItem.parameters || []), ...(operation.parameters || [])]
    const pathParams = parameters.filter((p) => p.in === 'path')
    const queryParams = parameters.filter((p) => p.in === 'query')
    const body = operation.requestBody?.content?.['application/json']?.schema
    const args = []
    for (const param of pathParams) args.push(`${param.name}: ${typeOf(param.schema)}`)
    if (queryParams.length) args.push(`query: ${typeOf({ type: 'object', properties: Object.fromEntries(queryParams.map((p) => [p.name, p.schema])), required: queryParams.filter((p) => p.required).map((p) => p.name) })}`)
    if (body) args.push(`body: ${typeOf(body)}`)
    let renderedPath = JSON.stringify(path)
    for (const param of pathParams) renderedPath = renderedPath.replace(`{${param.name}}`, `\${encodeURIComponent(String(${param.name}))}`)
    if (pathParams.length) renderedPath = '`' + renderedPath.slice(1, -1) + '`'
    const init = [`method: ${JSON.stringify(method.toUpperCase())}`]
    if (body) init.push('body: JSON.stringify(body)')
    const querySuffix = queryParams.length ? ' + withQuery(query)' : ''
    operations.push(`export const ${camel(operation.operationId)} = (${[...args, 'extra?: RequestInit'].join(', ')}) => request<${typeOf(successSchema(operation) || {})}>(${renderedPath}${querySuffix}, { ${init.join(', ')}, ...extra })`)
  }
}

const schemas = Object.entries(api.components?.schemas || {}).map(([name, schema]) => `export type ${name} = ${typeOf(schema)}`).join('\n')
const queryHelper = Object.values(api.paths).some((pathItem) => Object.values(pathItem).some((operation) => operation?.parameters?.some?.((parameter) => parameter.in === 'query'))) ? `const withQuery = (query: Record<string, unknown>) => {\n  const params = new URLSearchParams(Object.entries(query).filter(([, value]) => value !== undefined && value !== null).map(([key, value]) => [key, String(value)]))\n  return params.size ? \`?${'${params}'}\` : ''\n}\n` : ''
const output = `// Generated from contracts/openapi/edge-api.openapi.json. Do not edit.\n\n${schemas}\n\nexport type RequestOptions = { baseUrl?: string; token?: string | null; refresh?: () => Promise<string | null>; onUnauthorized?: () => void }\n\nlet options: RequestOptions = {}\nlet refreshInFlight: Promise<string | null> | null = null\nexport const configureClient = (next: RequestOptions) => { options = next; refreshInFlight = null }\nasync function send(path: string, init: RequestInit, token: string | null | undefined): Promise<Response> {\n  return fetch(\`${'${options.baseUrl ?? \'\'}${path}'}\`, { ...init, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: \`Bearer ${'${token}'}\` } : {}), ...init.headers } })\n}\nexport async function request<T>(path: string, init: RequestInit = {}): Promise<T> {\n  let response = await send(path, init, options.token)\n  if (response.status === 401 && options.refresh && !path.endsWith('/api/auth/refresh')) {\n    refreshInFlight ??= options.refresh().finally(() => { refreshInFlight = null })\n    const fresh = await refreshInFlight\n    if (fresh) response = await send(path, init, fresh)\n    else { options.onUnauthorized?.(); throw new Error('request_failed_401') }\n  }\n  if (response.status === 401) options.onUnauthorized?.()\n  if (!response.ok) throw new Error(\`request_failed_${'${response.status}'}\`)\n  return response.status === 204 ? undefined as T : response.json()\n}\n${queryHelper}\n${operations.join('\n')}\n`

mkdirSync(dirname(target), { recursive: true })
writeFileSync(target, output)
