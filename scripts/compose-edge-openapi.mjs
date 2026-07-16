// Composes the Edge public OpenAPI contract from the per-service documents plus
// the Edge route table. Single source of truth: service DTOs -> service docs ->
// this composition -> Edge doc -> frontend client. Proxy/forward ops copy their
// real request/response schemas from the owning service (path rewritten to public);
// the three aggregation endpoints declare Edge-owned response shapes that reference
// the same service schemas by resolved name.
import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const docsDir = resolve(root, 'contracts/openapi')
const serviceFiles = [
  'identity-service', 'journal-service', 'performance-service', 'discipline-service',
  'reminder-service', 'stock-research-service', 'market-data-service', 'price-alert-service',
  'rotation-service', 'partner-service', 'content-service', 'tool-service', 'operations-service',
]
const docs = Object.fromEntries(serviceFiles.map((s) => [s, JSON.parse(readFileSync(resolve(docsDir, `${s}.openapi.json`), 'utf8'))]))
const docFor = (svc) => docs[`${svc}-service`] // every Edge alias maps to `<alias>-service`

const edgeRoot = resolve(root, 'gateway/TradeDiary.EdgeApi')
const edgeFiles = (directory) => readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
  const path = resolve(directory, entry.name)
  return entry.isDirectory() ? edgeFiles(path) : entry.name.endsWith('.cs') ? [path] : []
})
const edgeSource = edgeFiles(edgeRoot).map((file) => readFileSync(file, 'utf8')).join('\n')
const norm = (p) => p.replace(/\{([^}:]+):[^}]+\}/g, '{$1}')
// Structural form ignores param NAMES so e.g. Edge's {symbol} matches the service's {raw}.
const struct = (p) => p.replace(/\{[^}]+\}/g, '{_}')
const pathParams = (p) => (p.match(/\{([^}]+)\}/g) || []).map((x) => x.slice(1, -1))
const ANON_PUBLIC = (p) => p.startsWith('/api/content/') || ['/api/auth/register', '/api/auth/login', '/api/auth/refresh', '/api/auth/logout', '/api/auth/api-key/token'].includes(p)
// Deterministic operationId from public method+path so the generated client has stable names
// (matches the prior contract's naming, e.g. postApiAppDiaries, putApiAppDiariesId).
const opId = (method, path) => `${method}_${path}`.replace(/[^a-zA-Z0-9]+/g, '_').replace(/^_+|_+$/g, '')

// Find the actual path key in a service doc whose structure + method match an internal route.
function findServicePath(doc, internal, method) {
  const want = struct(internal)
  for (const path of Object.keys(doc.paths || {})) {
    if (struct(path) === want && doc.paths[path][method]) return path
  }
  return null
}

// --- parse Edge route table -------------------------------------------------
const routes = []
for (const m of edgeSource.matchAll(/MapProxy\(app,\s*"([^"]+)",\s*"([\w-]+)",\s*"([^"]+)",\s*\[([^\]]+)\]\)/g)) {
  const pub = norm(m[1]); const svc = m[2]; const internal = norm(m[3])
  for (const vm of m[4].matchAll(/HttpMethods\.(Get|Post|Put|Patch|Delete)/g)) routes.push({ pub, method: vm[1].toLowerCase(), svc, internal })
}
// Explicit Forward/ForwardNoBody routes. Some span two source lines (the Forward call sits
// on the line after app.MapXxx), so use dotAll + a tempered group that cannot cross into the
// next app.Map route. Aggregations/auth have no Forward() and are skipped.
const fwdRe = /app\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]+)"(?:(?!app\.Map).)*?\bForward(?:NoBody)?\(\s*clients,\s*"([\w-]+)",\s*\$?"([^"]+)"/gs
for (const m of edgeSource.matchAll(fwdRe)) routes.push({ pub: norm(m[2]), method: m[1].toLowerCase(), svc: m[3], internal: norm(m[4]) })
routes.push(
  { pub: '/api/app/diaries/{diaryId}/review', method: 'get', svc: 'journal', internal: '/internal/diaries/{diaryId}/review' },
  { pub: '/api/app/diaries/{diaryId}/review', method: 'put', svc: 'journal', internal: '/internal/diaries/{diaryId}/review' },
  { pub: '/api/app/diary-review-summary', method: 'get', svc: 'journal', internal: '/internal/diary-review-summary' },
)

// --- schema collection ------------------------------------------------------
const collected = new Map()
const refRe = /"\$ref":\s*"#\/components\/schemas\/([^"]+)"/g
function collectFromObj(obj, sourceDoc, stack = new Set()) {
  const names = new Set()
  for (const m of JSON.stringify(obj).matchAll(refRe)) names.add(m[1])
  for (const name of names) {
    if (stack.has(name)) continue
    const schema = sourceDoc.components?.schemas?.[name]
    if (!schema) throw new Error(`schema '${name}' not found in service doc`)
    if (collected.has(name)) {
      if (JSON.stringify(collected.get(name)) !== JSON.stringify(schema)) throw new Error(`schema name clash across services: ${name}`)
      continue
    }
    collected.set(name, schema)
    collectFromObj(schema, sourceDoc, new Set(stack).add(name))
  }
}

function resolveRef(doc, ref) { return doc.components?.schemas?.[ref.split('/').pop()] }
function refName(schema) { return schema?.$ref ? schema.$ref.split('/').pop() : null }
function successSchema(doc, path, method) {
  const op = doc.paths?.[path]?.[method]
  if (!op) throw new Error(`no ${method.toUpperCase()} ${path} in service doc`)
  for (const [status, resp] of Object.entries(op.responses || {})) {
    if (!status.startsWith('2')) continue
    return resp.content?.['application/json']?.schema || null
  }
  return null
}
// success schema resolved through a structural lookup, so callers pass any equivalent path.
function successSchemaByStruct(doc, internal, method) {
  const path = findServicePath(doc, internal, method)
  if (!path) throw new Error(`no ${method.toUpperCase()} matching ${internal} in service doc`)
  return successSchema(doc, path, method)
}
function collectionItemName(doc, internal, method) {
  let schema = successSchemaByStruct(doc, internal, method)
  if (schema?.$ref) schema = resolveRef(doc, schema.$ref)
  return refName(schema?.properties?.items?.items)
}

// --- build Edge paths -------------------------------------------------------
const paths = {}
for (const r of routes) {
  const doc = docFor(r.svc)
  const servicePath = findServicePath(doc, r.internal, r.method)
  if (!servicePath) throw new Error(`Edge ${r.method.toUpperCase()} ${r.pub} -> ${r.svc} ${r.internal}: no matching service op`)
  const op = JSON.parse(JSON.stringify(doc.paths[servicePath][r.method]))
  // Remap path parameters from the service's names to the public path's (positional), then set the public path.
  const fromParams = pathParams(servicePath), toParams = pathParams(r.pub)
  if (fromParams.length === toParams.length) {
    const map = Object.fromEntries(fromParams.map((f, i) => [f, toParams[i]]))
    for (const param of op.parameters || []) if (param.in === 'path' && map[param.name]) param.name = map[param.name]
  }
  op.security = ANON_PUBLIC(r.pub) ? [] : [{ bearerAuth: [] }]
  op.operationId = opId(r.method, r.pub)
  for (const status of Object.keys(op.responses || {})) {
    if (!status.startsWith('2')) op.responses[status] = { $ref: '#/components/responses/Problem' }
  }
  collectFromObj({ requestBody: op.requestBody, responses: op.responses, parameters: op.parameters }, doc)
  ;(paths[r.pub] ??= {})[r.method] = op
}

// --- aggregation endpoints (Edge-owned shapes referencing service schemas) --
const perfDay = refName(successSchemaByStruct(docFor('performance'), '/internal/performance/day/{date}', 'get'))
const monthSummary = refName(successSchemaByStruct(docFor('performance'), '/internal/performance/month-summary', 'get'))
const diaryItem = collectionItemName(docFor('journal'), '/internal/diaries', 'get')
const discipline = refName(successSchemaByStruct(docFor('discipline'), '/internal/disciplines/today', 'get'))
const stock = refName(successSchemaByStruct(docFor('stock-research'), '/internal/stocks/{symbol}', 'get'))
const bars = refName(successSchemaByStruct(docFor('market-data'), '/internal/v1/bars/{symbol}', 'get'))
collectFromObj({ $ref: `#/components/schemas/${perfDay}` }, docFor('performance'))
collectFromObj({ $ref: `#/components/schemas/${monthSummary}` }, docFor('performance'))
if (diaryItem) collectFromObj({ $ref: `#/components/schemas/${diaryItem}` }, docFor('journal'))
if (discipline) collectFromObj({ $ref: `#/components/schemas/${discipline}` }, docFor('discipline'))
if (stock) collectFromObj({ $ref: `#/components/schemas/${stock}` }, docFor('stock-research'))
if (bars) collectFromObj({ $ref: `#/components/schemas/${bars}` }, docFor('market-data'))

const ref = (n) => (n ? { $ref: `#/components/schemas/${n}` } : {})
const capability = { $ref: '#/components/schemas/CapabilityStatus' }
const dashboardSchema = {
  type: 'object', required: ['localDate', 'diary', 'performance', 'pendingAlerts', 'discipline', 'recentDiaries', 'capabilities'],
  properties: {
    localDate: { type: 'string' },
    diary: { type: 'object', required: ['writtenToday', 'count'], properties: { writtenToday: { type: 'boolean' }, count: { type: 'integer' } } },
    performance: perfDay ? { oneOf: [ref(perfDay), { type: 'null' }] } : { type: 'null' },
    pendingAlerts: { type: ['integer', 'null'] },
    discipline: discipline ? { oneOf: [ref(discipline), { type: 'null' }] } : { type: 'null' },
    recentDiaries: diaryItem ? { type: 'array', items: ref(diaryItem) } : { type: 'array', items: {} },
    capabilities: { type: 'object', required: ['alerts', 'discipline'], properties: { alerts: capability, discipline: capability } },
  },
}
const calendarSchema = {
  type: 'object', required: ['year', 'month', 'summary', 'days', 'capabilities'],
  properties: {
    year: { type: 'integer' }, month: { type: 'integer' },
    summary: monthSummary ? { oneOf: [ref(monthSummary), { type: 'null' }] } : { type: 'null' },
    days: { type: 'array', items: { type: 'object', required: ['date', 'performance', 'diaryCount', 'transactionCount', 'alertCount'], properties: {
      date: { type: 'string' },
      performance: perfDay ? { oneOf: [ref(perfDay), { type: 'null' }] } : { type: 'null' },
      diaryCount: { type: 'integer' }, transactionCount: { type: 'integer' }, alertCount: { type: ['integer', 'null'] },
    } } },
    capabilities: { type: 'object', required: ['alerts'], properties: { alerts: capability } },
  },
}
const stockPageSchema = {
  type: 'object', required: ['stock', 'bars', 'capabilities'],
  properties: {
    stock: stock ? ref(stock) : {},
    bars: bars ? { oneOf: [ref(bars), { type: 'null' }] } : { type: 'null' },
    capabilities: { type: 'object', required: ['marketData'], properties: { marketData: capability } },
  },
}
const bootstrapSchema = {
  type: 'object',
  required: ['currentUser', 'timezone', 'baseCurrency', 'role', 'accountType', 'currentLocalDate', 'availableProductAreas'],
  properties: {
    currentUser: {
      type: 'object', required: ['id', 'email', 'displayName'],
      properties: { id: { type: 'string', format: 'uuid' }, email: { type: 'string' }, displayName: { type: 'string' } },
    },
    timezone: { type: 'string' }, baseCurrency: { type: 'string' }, role: { type: 'string' }, accountType: { type: 'string' },
    currentLocalDate: { type: 'string', format: 'date' },
    availableProductAreas: { type: 'array', items: { type: 'string' } },
  },
}
collected.set('CapabilityStatus', { type: 'string', enum: ['available', 'empty', 'unavailable'] })
collected.set('AppBootstrapResponse', bootstrapSchema)
collected.set('DashboardResponse', dashboardSchema)
collected.set('CalendarResponse', calendarSchema)
collected.set('StockPageResponse', stockPageSchema)
collected.set('EdgeProblemDetails', {
  type: 'object', required: ['code', 'title', 'status', 'detail', 'correlationId'],
  properties: { code: { type: 'string' }, title: { type: 'string' }, status: { type: 'integer' }, detail: { type: 'string' }, correlationId: { type: 'string' } },
})

const agg = (path, schema, parameters = []) => ({
  [path]: {
    get: {
      operationId: opId('get', path),
      summary: `Edge aggregation: ${path}`,
      security: [{ bearerAuth: [] }],
      parameters,
      responses: {
        200: { description: 'Success', content: { 'application/json': { schema: { $ref: `#/components/schemas/${schema}` } } } },
        400: { $ref: '#/components/responses/Problem' },
        401: { $ref: '#/components/responses/Problem' },
        403: { $ref: '#/components/responses/Problem' },
        502: { $ref: '#/components/responses/Problem' },
        503: { $ref: '#/components/responses/Problem' },
        504: { $ref: '#/components/responses/Problem' },
      },
    },
  },
})
const intParam = (name) => ({ name, in: 'query', required: true, schema: { type: 'integer' } })
const strPathParam = (name) => ({ name, in: 'path', required: true, schema: { type: 'string' } })
Object.assign(paths,
  agg('/api/app/bootstrap', 'AppBootstrapResponse'),
  agg('/api/app/dashboard', 'DashboardResponse'),
  agg('/api/app/calendar', 'CalendarResponse', [intParam('year'), intParam('month')]),
  agg('/api/app/stocks/{symbol}/page', 'StockPageResponse', [strPathParam('symbol')]),
)

// Auth endpoints: Edge owns the browser-facing session shape. The refresh token lives only in the
// HttpOnly cookie Edge sets, so login/refresh return SessionTokens (no refresh token) and
// refresh/logout carry no body. Register forwards Identity's RegisterResponse verbatim.
const identity = docFor('identity')
collectFromObj({ $ref: '#/components/schemas/RegisterRequest' }, identity)
collectFromObj({ $ref: '#/components/schemas/RegisterResponse' }, identity)
collectFromObj({ $ref: '#/components/schemas/LoginRequest' }, identity)
collected.set('SessionTokens', { type: 'object', required: ['accessToken', 'expiresAt'], properties: { accessToken: { type: 'string' }, expiresAt: { type: 'string' } } })
const jsonBody = (schema) => ({ required: true, content: { 'application/json': { schema } } })
const okJson = (schema) => ({ description: 'Success', content: { 'application/json': { schema } } })
const problem = () => ({ $ref: '#/components/responses/Problem' })
Object.assign(paths, {
  '/api/auth/register': { post: { operationId: opId('post', '/api/auth/register'), security: [], parameters: [{ name: 'X-Registration-Key', in: 'header', required: false, schema: { type: 'string' } }], requestBody: jsonBody(ref('RegisterRequest')), responses: { 201: okJson(ref('RegisterResponse')), 400: problem(), 404: problem(), 409: problem() } } },
  '/api/auth/login': { post: { operationId: opId('post', '/api/auth/login'), security: [], requestBody: jsonBody(ref('LoginRequest')), responses: { 200: okJson(ref('SessionTokens')), 401: problem() } } },
  '/api/auth/refresh': { post: { operationId: opId('post', '/api/auth/refresh'), security: [], responses: { 200: okJson(ref('SessionTokens')), 401: problem() } } },
  '/api/auth/logout': { post: { operationId: opId('post', '/api/auth/logout'), security: [], responses: { 204: { description: 'No content' } } } },
})

const document = {
  openapi: '3.1.0',
  info: { title: 'Trade Diary edge-api', version: '0.1.0' },
  jsonSchemaDialect: 'https://json-schema.org/draft/2020-12/schema',
  paths,
  components: {
    securitySchemes: {
      bearerAuth: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' },
      serviceKey: { type: 'apiKey', in: 'header', name: 'X-Service-Key' },
    },
    schemas: Object.fromEntries([...collected].sort((a, b) => a[0].localeCompare(b[0]))),
    responses: { Problem: { description: 'Request failed', content: { 'application/problem+json': { schema: { $ref: '#/components/schemas/EdgeProblemDetails' } } } } },
  },
}

const serialized = JSON.stringify(document, null, 2) + '\n'
const target = resolve(docsDir, 'edge-api.openapi.json')
const opCount = Object.values(paths).reduce((n, item) => n + Object.keys(item).filter((m) => ['get', 'post', 'put', 'patch', 'delete'].includes(m)).length, 0)
if (process.argv.includes('--check')) {
  const existing = existsSync(target) ? readFileSync(target, 'utf8') : null
  if (existing !== serialized) { console.error('edge-api.openapi.json stale; run: node scripts/compose-edge-openapi.mjs'); process.exit(1) }
  console.log(`Edge OpenAPI freshness OK: ${Object.keys(paths).length} paths, ${opCount} operations`)
} else {
  writeFileSync(target, serialized)
  console.log(`Composed edge-api.openapi.json: ${Object.keys(paths).length} paths, ${opCount} operations, ${collected.size} schemas`)
}
