import { ref, onMounted } from 'vue';
const isWebView2 = ref(false);
const isMaximized = ref(false);
let initialized = false;
function onHostMessage(e) {
    if (e.data === 'maximized')
        isMaximized.value = true;
    else if (e.data === 'normal')
        isMaximized.value = false;
}
function init() {
    if (initialized)
        return;
    initialized = true;
    if (window.chrome?.webview) {
        isWebView2.value = true;
        window.chrome.webview.addEventListener('message', onHostMessage);
    }
}
export function useWindowControls() {
    onMounted(init);
    function minimize() {
        window.chrome?.webview?.postMessage('minimize');
    }
    function toggleMaximize() {
        window.chrome?.webview?.postMessage('maximize');
    }
    function close() {
        window.chrome?.webview?.postMessage('close');
    }
    return { isWebView2, isMaximized, minimize, toggleMaximize, close };
}
