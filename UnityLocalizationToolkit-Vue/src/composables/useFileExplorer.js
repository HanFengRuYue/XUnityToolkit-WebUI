import { ref } from 'vue';
// Module-level singleton state (one modal at a time)
const show = ref(false);
const mode = ref('file');
const options = ref({});
let resolveCallback = null;
function open(selectMode, opts) {
    // If already open, resolve previous with null
    if (resolveCallback) {
        resolveCallback(null);
        resolveCallback = null;
    }
    return new Promise((resolve) => {
        resolveCallback = resolve;
        mode.value = selectMode;
        options.value = opts ?? {};
        show.value = true;
    });
}
function confirm(path) {
    if (resolveCallback) {
        resolveCallback(path);
        resolveCallback = null;
    }
    show.value = false;
}
function cancel() {
    if (resolveCallback) {
        resolveCallback(null);
        resolveCallback = null;
    }
    show.value = false;
}
export function useFileExplorer() {
    return {
        show,
        mode,
        options,
        selectFile: (opts) => open('file', opts),
        selectFolder: (opts) => open('folder', opts),
        confirm,
        cancel,
    };
}
