import './assets/main.css';
import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import router from './router';
const LAZY_ROUTE_RETRY_KEY = 'xut:lazy-route-retry';
const lazyRouteErrorPattern = /ChunkLoadError|Failed to fetch dynamically imported module|Importing a module script failed|Unable to preload CSS|error loading dynamically imported module/i;
function getCurrentRouteUrl() {
    return `${window.location.pathname}${window.location.search}${window.location.hash}`;
}
function tryRecoverLazyRouteLoad(target = getCurrentRouteUrl()) {
    const lastRetriedTarget = sessionStorage.getItem(LAZY_ROUTE_RETRY_KEY);
    if (lastRetriedTarget === target) {
        return false;
    }
    sessionStorage.setItem(LAZY_ROUTE_RETRY_KEY, target);
    window.location.replace(target);
    return true;
}
if (typeof window !== 'undefined') {
    window.addEventListener('vite:preloadError', (event) => {
        if (tryRecoverLazyRouteLoad()) {
            event.preventDefault();
        }
    });
}
router.onError((error, to) => {
    const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
    if (lazyRouteErrorPattern.test(message) && tryRecoverLazyRouteLoad(to.fullPath)) {
        return;
    }
    console.error('Route navigation failed', error);
});
router.afterEach((to) => {
    if (typeof window === 'undefined')
        return;
    if (sessionStorage.getItem(LAZY_ROUTE_RETRY_KEY) === to.fullPath) {
        sessionStorage.removeItem(LAZY_ROUTE_RETRY_KEY);
    }
});
const app = createApp(App);
app.use(createPinia());
app.use(router);
app.mount('#app');
