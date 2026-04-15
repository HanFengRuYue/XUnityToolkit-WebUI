import { readdirSync, statSync } from 'node:fs'
import path from 'node:path'

const rootDir = path.resolve(import.meta.dirname, '..')
const srcDir = path.join(rootDir, 'src')
const shadowFiles = []

walk(srcDir)

if (shadowFiles.length > 0) {
  console.error('Found stale transpiled source mirrors that would shadow the real TS/Vue sources:')
  for (const filePath of shadowFiles) {
    console.error(`- ${path.relative(rootDir, filePath)}`)
  }
  console.error('Delete the shadow .js files under src/ before running Vite again.')
  process.exit(1)
}

function walk(currentDir) {
  for (const entry of readdirSync(currentDir, { withFileTypes: true })) {
    const fullPath = path.join(currentDir, entry.name)
    if (entry.isDirectory()) {
      walk(fullPath)
      continue
    }

    if (!entry.isFile() || !entry.name.endsWith('.js')) {
      continue
    }

    if (isShadowSource(fullPath)) {
      shadowFiles.push(fullPath)
    }
  }
}

function isShadowSource(filePath) {
  if (filePath.endsWith('.vue.js')) {
    const vueSource = filePath.slice(0, -3)
    return exists(vueSource)
  }

  const tsSource = filePath.slice(0, -3) + '.ts'
  return exists(tsSource)
}

function exists(filePath) {
  try {
    return statSync(filePath).isFile()
  }
  catch {
    return false
  }
}
