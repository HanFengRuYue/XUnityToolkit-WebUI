import { watch, ref, onBeforeUnmount, type WatchSource } from 'vue'

/**
 * Auto-save composable: watches a reactive source and debounces save calls.
 * - Skips saves until `enable()` is called (prevents saving on initial data load)
 * - Flushes pending saves on component unmount
 * - Silent on success, saveFn should call message.error() on failure
 */
export function useAutoSave<T>(
  source: WatchSource<T>,
  saveFn: () => Promise<void>,
  options: {
    debounceMs?: number
    deep?: boolean
  } = {},
) {
  const { debounceMs = 1000, deep = false } = options
  const saving = ref(false)
  let ready = false
  let timer: ReturnType<typeof setTimeout> | null = null

  function enable() {
    ready = true
  }

  function disable() {
    ready = false
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
  }

  async function doSave() {
    timer = null
    saving.value = true
    try {
      await saveFn()
    } finally {
      saving.value = false
    }
  }

  function schedule() {
    if (!ready) return
    if (timer) clearTimeout(timer)
    timer = setTimeout(doSave, debounceMs)
  }

  async function flush() {
    if (timer) {
      clearTimeout(timer)
      await doSave()
    }
  }

  watch(source, schedule, { deep })

  onBeforeUnmount(flush)

  return { saving, enable, disable, flush }
}
