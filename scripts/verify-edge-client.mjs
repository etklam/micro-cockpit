import { readFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const generated = resolve(root, 'frontend/src/generated/edge.ts')
const before = readFileSync(generated, 'utf8')
const result = spawnSync(process.execPath, [resolve(root, 'scripts/generate-edge-client.mjs')], { stdio: 'inherit' })
if (result.status !== 0) process.exit(result.status ?? 1)
if (readFileSync(generated, 'utf8') !== before) throw new Error('Generated Edge client is stale; run npm run api:generate')

const frontend = spawnSync('rg', ['-n', String.raw`\bfetch\s*\(`, 'src', '--glob', '!src/generated/**'], { cwd: resolve(root, 'frontend'), encoding: 'utf8' })
if (frontend.status === 0) throw new Error(`Raw fetch outside generated client:\n${frontend.stdout}`)
if (frontend.status !== 1) throw new Error(frontend.stderr || 'Unable to scan frontend source')
