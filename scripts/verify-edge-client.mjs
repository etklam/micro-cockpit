import { readFileSync, readdirSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { relative, resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const generated = resolve(root, 'frontend/src/generated/edge.ts')
const before = readFileSync(generated, 'utf8')
const result = spawnSync(process.execPath, [resolve(root, 'scripts/generate-edge-client.mjs')], { stdio: 'inherit' })
if (result.status !== 0) process.exit(result.status ?? 1)
if (readFileSync(generated, 'utf8') !== before) throw new Error('Generated Edge client is stale; run npm run api:generate')

const frontendRoot = resolve(root, 'frontend')
const sourceRoot = resolve(frontendRoot, 'src')
const rawFetchPattern = /\bfetch\s*\(/u

function findRawFetch(directory, matches = []) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name)
    if (entry.isDirectory()) {
      if (path !== resolve(sourceRoot, 'generated')) findRawFetch(path, matches)
      continue
    }
    const source = readFileSync(path, 'utf8')
    const lines = source.split(/\r?\n/u)
    lines.forEach((line, index) => {
      if (rawFetchPattern.test(line)) matches.push(`${relative(frontendRoot, path)}:${index + 1}:${line.trim()}`)
    })
  }
  return matches
}

const rawFetch = findRawFetch(sourceRoot)
if (rawFetch.length) throw new Error(`Raw fetch outside generated client:\n${rawFetch.join('\n')}`)
