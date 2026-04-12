import { ref } from 'vue'

export interface FileExplorerFilter {
  label: string
  extensions: string[] // e.g. ['.exe'], ['.dll', '.zip']; empty = all files
}

export interface FileExplorerOptions {
  title?: string
  filters?: FileExplorerFilter[]
  initialPath?: string
}

// Module-level singleton state (one modal at a time)
const show = ref(false)
const mode = ref<'file' | 'folder'>('file')
const options = ref<FileExplorerOptions>({})
let resolveCallback: ((path: string | null) => void) | null = null

function open(
  selectMode: 'file' | 'folder',
  opts?: FileExplorerOptions,
): Promise<string | null> {
  // If already open, resolve previous with null
  if (resolveCallback) {
    resolveCallback(null)
    resolveCallback = null
  }

  return new Promise<string | null>((resolve) => {
    resolveCallback = resolve
    mode.value = selectMode
    options.value = opts ?? {}
    show.value = true
  })
}

function confirm(path: string) {
  if (resolveCallback) {
    resolveCallback(path)
    resolveCallback = null
  }
  show.value = false
}

function cancel() {
  if (resolveCallback) {
    resolveCallback(null)
    resolveCallback = null
  }
  show.value = false
}

export function useFileExplorer() {
  return {
    show,
    mode,
    options,
    selectFile: (opts?: FileExplorerOptions) => open('file', opts),
    selectFolder: (opts?: FileExplorerOptions) => open('folder', opts),
    confirm,
    cancel,
  }
}
