// Generates committed OpenAPI documents by running each service briefly and
// fetching its real /openapi.json (derived from DTOs via Microsoft.AspNetCore.OpenApi).
// Usage:
//   node scripts/generate-openapi.mjs            # write contracts/openapi/*.openapi.json
//   node scripts/generate-openapi.mjs --check    # regenerate to temp, fail if committed docs differ
import { spawn, spawnSync } from 'node:child_process'
import { writeFileSync, readFileSync, mkdirSync, rmSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { randomBytes } from 'node:crypto'

const root = resolve(import.meta.dirname, '..')
const outDir = resolve(root, 'contracts/openapi')
const check = process.argv.includes('--check')
const documentationEnvironment = {
  ConnectionStrings__Identity: 'Host=127.0.0.1;Database=trade_diary;Username=identity_service',
  ConnectionStrings__Journal: 'Host=127.0.0.1;Database=trade_diary;Username=journal_service',
  ConnectionStrings__Performance: 'Host=127.0.0.1;Database=trade_diary;Username=performance_service',
  ConnectionStrings__Discipline: 'Host=127.0.0.1;Database=trade_diary;Username=discipline_service',
  ConnectionStrings__Reminder: 'Host=127.0.0.1;Database=trade_diary;Username=reminder_service',
  ConnectionStrings__StockResearch: 'Host=127.0.0.1;Database=trade_diary;Username=stock_research_service',
  ConnectionStrings__MarketData: 'Host=127.0.0.1;Database=trade_diary;Username=market_data_service',
  ConnectionStrings__PriceAlert: 'Host=127.0.0.1;Database=trade_diary;Username=price_alert_service',
  ConnectionStrings__Rotation: 'Host=127.0.0.1;Database=trade_diary;Username=rotation_service',
  ConnectionStrings__Partner: 'Host=127.0.0.1;Database=trade_diary;Username=partner_service',
  ConnectionStrings__Content: 'Host=127.0.0.1;Database=trade_diary;Username=content_service',
  ConnectionStrings__Operations: 'Host=127.0.0.1;Database=trade_diary;Username=operations_service',
  Auth__LocalRegistrationKey: 'TEST-ONLY-NOT-A-SECRET',
  Internal__ServiceKey: 'TEST-ONLY-NOT-A-SECRET',
}

// name -> { project, port }. Ports are throwaway localhost ports for doc generation only.
const services = [
  ['identity-service', 'services/identity-service/src/TradeDiary.Identity', 5600],
  ['journal-service', 'services/journal-service/src/TradeDiary.Journal', 5601],
  ['performance-service', 'services/performance-service/src/TradeDiary.Performance', 5602],
  ['discipline-service', 'services/discipline-service/src/TradeDiary.Discipline', 5603],
  ['reminder-service', 'services/reminder-service/src/TradeDiary.Reminder', 5604],
  ['stock-research-service', 'services/stock-research-service/src/TradeDiary.StockResearch', 5605],
  ['market-data-service', 'services/market-data-service/src/TradeDiary.MarketData', 5606],
  ['price-alert-service', 'services/price-alert-service/src/TradeDiary.PriceAlert', 5607],
  ['rotation-service', 'services/rotation-service/src/TradeDiary.Rotation', 5608],
  ['partner-service', 'services/partner-service/src/TradeDiary.Partner', 5609],
  ['content-service', 'services/content-service/src/TradeDiary.Content', 5610],
  ['tool-service', 'services/tool-service/src/TradeDiary.Tool', 5611],
  ['operations-service', 'services/operations-service/src/TradeDiary.Operations', 5612],
].map(([name, project, port]) => ({ name, project: resolve(root, project), port }))

// Keep generation deterministic on constrained runners: service startup only needs one compiled
// artifact at a time, and disabling reusable nodes avoids named-pipe contention on macOS sandboxes.
const build = spawnSync('dotnet', ['build', 'TradeDiary.slnx', '--nologo', '-m:1', '--disable-build-servers'], { cwd: root, stdio: 'inherit' })
if (build.status !== 0) { console.error('Solution build failed'); process.exit(build.status ?? 1) }

const workDir = check ? resolve(root, `.openapi-tmp-${randomBytes(4).toString('hex')}`) : outDir
if (check) mkdirSync(workDir, { recursive: true })

const procs = []
const reap = (p) => { try { process.kill(-p.pid, 'SIGKILL') } catch { try { p.kill('SIGKILL') } catch {} } }
const cleanup = () => { for (const p of procs) reap(p); if (check) { try { rmSync(workDir, { recursive: true, force: true }) } catch {} } }
process.on('exit', cleanup); process.on('SIGINT', () => { cleanup(); process.exit(130) })

async function fetchDoc({ name, project, port }) {
  // Run the built DLL directly so .kill() reaps the actual app process (dotnet run
  // would orphan its child dll process and leave the port bound to a stale doc).
  const assembly = project.split('/').slice(-1)[0]
  const dll = resolve(project, `bin/Debug/net10.0/${assembly}.dll`)
  if (!existsSync(dll)) throw new Error(`${name}: build output not found at ${dll}`)
  const child = spawn('dotnet', [dll, '--urls', `http://127.0.0.1:${port}`], {
    env: {
      ...process.env,
      ...documentationEnvironment,
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ASPNETCORE_ENVIRONMENT: 'Development',
    },
    stdio: 'ignore',
    detached: true,
  })
  procs.push(child)
  const url = `http://127.0.0.1:${port}/openapi.json`
  const deadline = Date.now() + 25000
  let lastErr = null
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url)
      if (res.ok) { const text = await res.text(); reap(child); return text }
      lastErr = `HTTP ${res.status}`
    } catch (err) { lastErr = err.message }
    await new Promise((r) => setTimeout(r, 500))
  }
  reap(child)
  throw new Error(`${name}: /openapi.json not ready after 25s (${lastErr})`)
}

const written = []
const errors = []
for (const svc of services) {
  try {
    const raw = await fetchDoc(svc)
    const doc = JSON.parse(raw)
    if (doc.openapi == null || doc.paths == null) throw new Error('not an OpenAPI document')
    const file = resolve(workDir, `${svc.name}.openapi.json`)
    writeFileSync(file, JSON.stringify(doc, null, 2) + '\n')
    written.push(`${svc.name}.openapi.json (${Object.keys(doc.paths).length} paths)`)
  } catch (err) {
    errors.push(`${svc.name}: ${err.message}`)
  }
}

if (errors.length) {
  console.error('OpenAPI generation failed:\n  ' + errors.join('\n  '))
  process.exit(1)
}

if (check) {
  const drift = []
  for (const svc of services) {
    const name = `${svc.name}.openapi.json`
    const fresh = readFileSync(resolve(workDir, name), 'utf8')
    const committed = resolve(outDir, name)
    if (!existsSync(committed)) { drift.push(`${name}: missing (run: node scripts/generate-openapi.mjs)`); continue }
    const current = readFileSync(committed, 'utf8')
    if (current !== fresh) drift.push(`${name}: stale (run: node scripts/generate-openapi.mjs)`)
  }
  if (drift.length) { console.error('Committed OpenAPI drifted from runtime:\n  ' + drift.join('\n  ')); process.exit(1) }
  console.log(`OpenAPI freshness OK: ${written.length} documents match committed`)
} else {
  console.log(`Generated ${written.length} OpenAPI documents:\n  ` + written.join('\n  '))
}
