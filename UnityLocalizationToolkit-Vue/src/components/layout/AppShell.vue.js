import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue';
import { RouterView, useRouter, useRoute } from 'vue-router';
import { NIcon, NTooltip } from 'naive-ui';
import { GamepadFilled, SettingsOutlined, SmartToyOutlined, ArticleOutlined, FontDownloadOutlined, KeyboardDoubleArrowLeftOutlined, KeyboardDoubleArrowRightOutlined } from '@vicons/material';
import InstallProgressDrawer from '@/components/progress/InstallProgressDrawer.vue';
import { settingsApi } from '@/api/games';
import { useUpdateStore } from '@/stores/update';
import { useSidebarStore } from '@/stores/sidebar';
import { useWindowControls } from '@/composables/useWindowControls';
const { isWebView2, isMaximized, minimize, toggleMaximize, close: closeWindow } = useWindowControls();
const router = useRouter();
const route = useRoute();
const sidebarOpen = ref(false);
const appVersion = ref('');
const updateStore = useUpdateStore();
const sidebarStore = useSidebarStore();
const showUpdateBadge = computed(() => updateStore.isUpdateAvailable || updateStore.isReady || updateStore.isDownloading);
onMounted(async () => {
    try {
        const info = await settingsApi.getVersion();
        const match = info.version.match(/^(\d+\.\d+\.\d+)/);
        appVersion.value = match?.[1] ?? info.version;
    }
    catch {
        appVersion.value = '1.0.0';
    }
    updateStore.init();
    window.addEventListener('resize', updateViewport);
});
onBeforeUnmount(() => {
    removeGuard();
    window.removeEventListener('resize', updateViewport);
    document.removeEventListener('mousemove', onResizeMove);
    document.removeEventListener('mouseup', onResizeEnd);
});
// Cache top-level pages to avoid full re-renders on navigation
const cachedPages = ['LibraryView', 'AiTranslationView', 'FontGeneratorView', 'LogView', 'SettingsView'];
const mainNavItems = [
    { label: '游戏库', key: '/', icon: GamepadFilled },
    { label: 'XUnity AI 翻译', key: '/ai-translation', icon: SmartToyOutlined },
    { label: '字体生成', key: '/font-generator', icon: FontDownloadOutlined },
    { label: '运行日志', key: '/logs', icon: ArticleOutlined },
];
const settingsNavItem = { label: '设置', key: '/settings', icon: SettingsOutlined };
function navigateTo(key) {
    router.push(key);
    sidebarOpen.value = false;
}
function isActive(key) {
    if (key === '/')
        return route.path === '/' || route.path.startsWith('/games/');
    return route.path.startsWith(key);
}
// Render the initial route without Transition; enable route animations only after the first real navigation.
const transitionDirection = ref('page');
const routeTransitionsStarted = ref(false);
const removeGuard = router.beforeEach((to, from) => {
    if (from.matched.length === 0) {
        return;
    }
    if (!routeTransitionsStarted.value) {
        routeTransitionsStarted.value = true;
    }
    const toDepth = to.meta.depth ?? 1;
    const fromDepth = from.meta.depth ?? 1;
    if (toDepth > fromDepth) {
        transitionDirection.value = 'page-slide-left';
    }
    else if (toDepth < fromDepth) {
        transitionDirection.value = 'page-slide-right';
    }
    else {
        transitionDirection.value = 'page';
    }
});
watch(() => route.path, () => {
    sidebarOpen.value = false;
});
// Viewport & responsive breakpoints
const viewportWidth = ref(window.innerWidth);
const isMobile = computed(() => viewportWidth.value <= 768);
const isNarrowDesktop = computed(() => !isMobile.value && viewportWidth.value <= 900);
function updateViewport() {
    viewportWidth.value = window.innerWidth;
}
// Sidebar width (inline style, not applied on mobile; auto-collapse on narrow desktop)
const sidebarStyle = computed(() => {
    if (isMobile.value)
        return undefined;
    if (isNarrowDesktop.value)
        return { width: sidebarStore.COLLAPSED_WIDTH + 'px' };
    return { width: sidebarStore.effectiveWidth + 'px' };
});
// Drag resize
const isResizing = ref(false);
let startX = 0;
let startWidth = 0;
function onResizeStart(e) {
    if (sidebarStore.collapsed || isNarrowDesktop.value || isMobile.value)
        return;
    e.preventDefault();
    isResizing.value = true;
    startX = e.clientX;
    startWidth = sidebarStore.customWidth;
    document.addEventListener('mousemove', onResizeMove);
    document.addEventListener('mouseup', onResizeEnd);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
}
function onResizeMove(e) {
    if (!isResizing.value)
        return;
    const delta = e.clientX - startX;
    sidebarStore.setWidth(startWidth + delta);
}
function onResizeEnd() {
    if (!isResizing.value)
        return;
    isResizing.value = false;
    document.removeEventListener('mousemove', onResizeMove);
    document.removeEventListener('mouseup', onResizeEnd);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
}
function onResizeDoubleClick() {
    sidebarStore.resetWidth();
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['has-titlebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-header']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['n-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-bottom-nav']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-header']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-logo']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['logo-text']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-divider']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-nav']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['update-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-collapse-toggle']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-bottom-nav']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['bottom-divider']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-footer']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['main-content']} */ ;
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['mobile-topbar']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['topbar-logo']} */ ;
/** @type {__VLS_StyleScopedClasses['logo-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-overlay']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-collapse-toggle']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['main-content']} */ ;
/** @type {__VLS_StyleScopedClasses['window-titlebar']} */ ;
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['has-titlebar']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-header']} */ ;
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['has-titlebar']} */ ;
/** @type {__VLS_StyleScopedClasses['main-content']} */ ;
/** @type {__VLS_StyleScopedClasses['mobile-topbar']} */ ;
/** @type {__VLS_StyleScopedClasses['mobile-topbar']} */ ;
/** @type {__VLS_StyleScopedClasses['has-wv2-controls']} */ ;
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['mobile-topbar']} */ ;
/** @type {__VLS_StyleScopedClasses['has-wv2-controls']} */ ;
/** @type {__VLS_StyleScopedClasses['topbar-window-controls']} */ ;
/** @type {__VLS_StyleScopedClasses['topbar-window-controls']} */ ;
/** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['main-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "app-layout" },
    ...{ class: ({ 'has-titlebar': __VLS_ctx.isWebView2 }) },
});
/** @type {__VLS_StyleScopedClasses['app-layout']} */ ;
/** @type {__VLS_StyleScopedClasses['has-titlebar']} */ ;
if (__VLS_ctx.isWebView2) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "window-titlebar" },
    });
    /** @type {__VLS_StyleScopedClasses['window-titlebar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "window-controls" },
    });
    /** @type {__VLS_StyleScopedClasses['window-controls']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.minimize) },
        ...{ class: "win-btn win-minimize" },
        title: "最小化",
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-minimize']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "10",
        height: "1",
        viewBox: "0 0 10 1",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        width: "10",
        height: "1",
        fill: "currentColor",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.toggleMaximize) },
        ...{ class: "win-btn win-maximize" },
        title: (__VLS_ctx.isMaximized ? '向下还原' : '最大化'),
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-maximize']} */ ;
    if (!__VLS_ctx.isMaximized) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            width: "10",
            height: "10",
            viewBox: "0 0 10 10",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "0.5",
            y: "0.5",
            width: "9",
            height: "9",
            rx: "1",
            fill: "none",
            stroke: "currentColor",
            'stroke-width': "1",
        });
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            width: "10",
            height: "10",
            viewBox: "0 0 10 10",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "2.5",
            y: "0.5",
            width: "7",
            height: "7",
            rx: "1",
            fill: "none",
            stroke: "currentColor",
            'stroke-width': "1",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "0.5",
            y: "2.5",
            width: "7",
            height: "7",
            rx: "1",
            fill: "var(--bg-surface)",
            stroke: "currentColor",
            'stroke-width': "1",
        });
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.closeWindow) },
        ...{ class: "win-btn win-close" },
        title: "关闭",
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-close']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "10",
        height: "10",
        viewBox: "0 0 10 10",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        d: "M1 1L9 9M9 1L1 9",
        stroke: "currentColor",
        'stroke-width': "1.2",
        'stroke-linecap': "round",
    });
}
__VLS_asFunctionalElement1(__VLS_intrinsics.header, __VLS_intrinsics.header)({
    ...{ class: "mobile-topbar" },
    ...{ class: ({ 'has-wv2-controls': __VLS_ctx.isWebView2 }) },
});
/** @type {__VLS_StyleScopedClasses['mobile-topbar']} */ ;
/** @type {__VLS_StyleScopedClasses['has-wv2-controls']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.sidebarOpen = !__VLS_ctx.sidebarOpen;
            // @ts-ignore
            [isWebView2, isWebView2, isWebView2, minimize, toggleMaximize, isMaximized, isMaximized, closeWindow, sidebarOpen, sidebarOpen,];
        } },
    ...{ class: "hamburger" },
    ...{ class: ({ active: __VLS_ctx.sidebarOpen }) },
});
/** @type {__VLS_StyleScopedClasses['hamburger']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "topbar-logo" },
});
/** @type {__VLS_StyleScopedClasses['topbar-logo']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.img)({
    ...{ class: "logo-icon" },
    src: "/logo.png",
    width: "24",
    height: "24",
    alt: "UnityLocalizationToolkit",
});
/** @type {__VLS_StyleScopedClasses['logo-icon']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "topbar-title" },
});
/** @type {__VLS_StyleScopedClasses['topbar-title']} */ ;
if (__VLS_ctx.isWebView2) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "topbar-window-controls" },
    });
    /** @type {__VLS_StyleScopedClasses['topbar-window-controls']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.minimize) },
        ...{ class: "win-btn win-minimize" },
        title: "最小化",
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-minimize']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "10",
        height: "1",
        viewBox: "0 0 10 1",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        width: "10",
        height: "1",
        fill: "currentColor",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.toggleMaximize) },
        ...{ class: "win-btn win-maximize" },
        title: (__VLS_ctx.isMaximized ? '向下还原' : '最大化'),
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-maximize']} */ ;
    if (!__VLS_ctx.isMaximized) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            width: "10",
            height: "10",
            viewBox: "0 0 10 10",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "0.5",
            y: "0.5",
            width: "9",
            height: "9",
            rx: "1",
            fill: "none",
            stroke: "currentColor",
            'stroke-width': "1",
        });
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            width: "10",
            height: "10",
            viewBox: "0 0 10 10",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "2.5",
            y: "0.5",
            width: "7",
            height: "7",
            rx: "1",
            fill: "none",
            stroke: "currentColor",
            'stroke-width': "1",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: "0.5",
            y: "2.5",
            width: "7",
            height: "7",
            rx: "1",
            fill: "var(--bg-surface)",
            stroke: "currentColor",
            'stroke-width': "1",
        });
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.closeWindow) },
        ...{ class: "win-btn win-close" },
        title: "关闭",
    });
    /** @type {__VLS_StyleScopedClasses['win-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['win-close']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "10",
        height: "10",
        viewBox: "0 0 10 10",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        d: "M1 1L9 9M9 1L1 9",
        stroke: "currentColor",
        'stroke-width': "1.2",
        'stroke-linecap': "round",
    });
}
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
Transition;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    name: "overlay",
}));
const __VLS_2 = __VLS_1({
    name: "overlay",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
if (__VLS_ctx.sidebarOpen) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.sidebarOpen))
                    return;
                __VLS_ctx.sidebarOpen = false;
                // @ts-ignore
                [isWebView2, minimize, toggleMaximize, isMaximized, isMaximized, closeWindow, sidebarOpen, sidebarOpen, sidebarOpen,];
            } },
        ...{ class: "sidebar-overlay" },
    });
    /** @type {__VLS_StyleScopedClasses['sidebar-overlay']} */ ;
}
// @ts-ignore
[];
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.aside, __VLS_intrinsics.aside)({
    ...{ class: "sidebar" },
    ...{ class: ({ open: __VLS_ctx.sidebarOpen, collapsed: (__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile, resizing: __VLS_ctx.isResizing }) },
    ...{ style: (__VLS_ctx.sidebarStyle) },
});
/** @type {__VLS_StyleScopedClasses['sidebar']} */ ;
/** @type {__VLS_StyleScopedClasses['open']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['resizing']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-header" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-logo" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-logo']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.img)({
    ...{ class: "logo-icon" },
    src: "/logo.png",
    width: "32",
    height: "32",
    alt: "UnityLocalizationToolkit",
});
/** @type {__VLS_StyleScopedClasses['logo-icon']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "logo-text" },
});
/** @type {__VLS_StyleScopedClasses['logo-text']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "logo-name" },
});
/** @type {__VLS_StyleScopedClasses['logo-name']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "logo-sub" },
});
/** @type {__VLS_StyleScopedClasses['logo-sub']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-divider" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-divider']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.nav, __VLS_intrinsics.nav)({
    ...{ class: "sidebar-nav" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-nav']} */ ;
for (const [item] of __VLS_vFor((__VLS_ctx.mainNavItems))) {
    (item.key);
    if ((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile) {
        let __VLS_6;
        /** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
        NTooltip;
        // @ts-ignore
        const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({
            placement: "right",
            showArrow: (false),
        }));
        const __VLS_8 = __VLS_7({
            placement: "right",
            showArrow: (false),
        }, ...__VLS_functionalComponentArgsRest(__VLS_7));
        const { default: __VLS_11 } = __VLS_9.slots;
        {
            const { trigger: __VLS_12 } = __VLS_9.slots;
            __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
                ...{ onClick: (...[$event]) => {
                        if (!((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile))
                            return;
                        __VLS_ctx.navigateTo(item.key);
                        // @ts-ignore
                        [sidebarOpen, sidebarStore, sidebarStore, isNarrowDesktop, isNarrowDesktop, isMobile, isMobile, isResizing, sidebarStyle, mainNavItems, navigateTo,];
                    } },
                ...{ class: "nav-item" },
                ...{ class: ({ active: __VLS_ctx.isActive(item.key) }) },
            });
            /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
            /** @type {__VLS_StyleScopedClasses['active']} */ ;
            let __VLS_13;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_14 = __VLS_asFunctionalComponent1(__VLS_13, new __VLS_13({
                size: (20),
            }));
            const __VLS_15 = __VLS_14({
                size: (20),
            }, ...__VLS_functionalComponentArgsRest(__VLS_14));
            const { default: __VLS_18 } = __VLS_16.slots;
            const __VLS_19 = (item.icon);
            // @ts-ignore
            const __VLS_20 = __VLS_asFunctionalComponent1(__VLS_19, new __VLS_19({}));
            const __VLS_21 = __VLS_20({}, ...__VLS_functionalComponentArgsRest(__VLS_20));
            // @ts-ignore
            [isActive,];
            var __VLS_16;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "nav-label" },
            });
            /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
            (item.label);
            // @ts-ignore
            [];
        }
        (item.label);
        // @ts-ignore
        [];
        var __VLS_9;
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
            ...{ onClick: (...[$event]) => {
                    if (!!((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile))
                        return;
                    __VLS_ctx.navigateTo(item.key);
                    // @ts-ignore
                    [navigateTo,];
                } },
            ...{ class: "nav-item" },
            ...{ class: ({ active: __VLS_ctx.isActive(item.key) }) },
        });
        /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        let __VLS_24;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_25 = __VLS_asFunctionalComponent1(__VLS_24, new __VLS_24({
            size: (20),
        }));
        const __VLS_26 = __VLS_25({
            size: (20),
        }, ...__VLS_functionalComponentArgsRest(__VLS_25));
        const { default: __VLS_29 } = __VLS_27.slots;
        const __VLS_30 = (item.icon);
        // @ts-ignore
        const __VLS_31 = __VLS_asFunctionalComponent1(__VLS_30, new __VLS_30({}));
        const __VLS_32 = __VLS_31({}, ...__VLS_functionalComponentArgsRest(__VLS_31));
        // @ts-ignore
        [isActive,];
        var __VLS_27;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "nav-label" },
        });
        /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
        (item.label);
    }
    // @ts-ignore
    [];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-spacer" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-spacer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-collapse-toggle" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-collapse-toggle']} */ ;
if ((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile) {
    let __VLS_35;
    /** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
    NTooltip;
    // @ts-ignore
    const __VLS_36 = __VLS_asFunctionalComponent1(__VLS_35, new __VLS_35({
        placement: "right",
        showArrow: (false),
    }));
    const __VLS_37 = __VLS_36({
        placement: "right",
        showArrow: (false),
    }, ...__VLS_functionalComponentArgsRest(__VLS_36));
    const { default: __VLS_40 } = __VLS_38.slots;
    {
        const { trigger: __VLS_41 } = __VLS_38.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
            ...{ onClick: (__VLS_ctx.sidebarStore.toggleCollapse) },
            ...{ class: "nav-item collapse-item" },
        });
        /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['collapse-item']} */ ;
        let __VLS_42;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
            size: (20),
        }));
        const __VLS_44 = __VLS_43({
            size: (20),
        }, ...__VLS_functionalComponentArgsRest(__VLS_43));
        const { default: __VLS_47 } = __VLS_45.slots;
        let __VLS_48;
        /** @ts-ignore @type {typeof __VLS_components.KeyboardDoubleArrowRightOutlined} */
        KeyboardDoubleArrowRightOutlined;
        // @ts-ignore
        const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({}));
        const __VLS_50 = __VLS_49({}, ...__VLS_functionalComponentArgsRest(__VLS_49));
        // @ts-ignore
        [sidebarStore, sidebarStore, isNarrowDesktop, isMobile,];
        var __VLS_45;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "nav-label" },
        });
        /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_38;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
        ...{ onClick: (__VLS_ctx.sidebarStore.toggleCollapse) },
        ...{ class: "nav-item collapse-item" },
    });
    /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapse-item']} */ ;
    let __VLS_53;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
        size: (20),
    }));
    const __VLS_55 = __VLS_54({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_54));
    const { default: __VLS_58 } = __VLS_56.slots;
    let __VLS_59;
    /** @ts-ignore @type {typeof __VLS_components.KeyboardDoubleArrowLeftOutlined} */
    KeyboardDoubleArrowLeftOutlined;
    // @ts-ignore
    const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({}));
    const __VLS_61 = __VLS_60({}, ...__VLS_functionalComponentArgsRest(__VLS_60));
    // @ts-ignore
    [sidebarStore,];
    var __VLS_56;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "nav-label" },
    });
    /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-bottom-nav" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-bottom-nav']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "bottom-divider" },
});
/** @type {__VLS_StyleScopedClasses['bottom-divider']} */ ;
if ((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile) {
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
    NTooltip;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        placement: "right",
        showArrow: (false),
    }));
    const __VLS_66 = __VLS_65({
        placement: "right",
        showArrow: (false),
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
    const { default: __VLS_69 } = __VLS_67.slots;
    {
        const { trigger: __VLS_70 } = __VLS_67.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
            ...{ onClick: (...[$event]) => {
                    if (!((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile))
                        return;
                    __VLS_ctx.navigateTo(__VLS_ctx.settingsNavItem.key);
                    // @ts-ignore
                    [sidebarStore, isNarrowDesktop, isMobile, navigateTo, settingsNavItem,];
                } },
            ...{ class: "nav-item" },
            ...{ class: ({ active: __VLS_ctx.isActive(__VLS_ctx.settingsNavItem.key) }) },
        });
        /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        let __VLS_71;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_72 = __VLS_asFunctionalComponent1(__VLS_71, new __VLS_71({
            size: (20),
        }));
        const __VLS_73 = __VLS_72({
            size: (20),
        }, ...__VLS_functionalComponentArgsRest(__VLS_72));
        const { default: __VLS_76 } = __VLS_74.slots;
        const __VLS_77 = (__VLS_ctx.settingsNavItem.icon);
        // @ts-ignore
        const __VLS_78 = __VLS_asFunctionalComponent1(__VLS_77, new __VLS_77({}));
        const __VLS_79 = __VLS_78({}, ...__VLS_functionalComponentArgsRest(__VLS_78));
        // @ts-ignore
        [isActive, settingsNavItem, settingsNavItem,];
        var __VLS_74;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "nav-label" },
        });
        /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
        (__VLS_ctx.settingsNavItem.label);
        if (__VLS_ctx.showUpdateBadge) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
                ...{ class: "update-dot" },
            });
            /** @type {__VLS_StyleScopedClasses['update-dot']} */ ;
        }
        // @ts-ignore
        [settingsNavItem, showUpdateBadge,];
    }
    (__VLS_ctx.settingsNavItem.label);
    // @ts-ignore
    [settingsNavItem,];
    var __VLS_67;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
        ...{ onClick: (...[$event]) => {
                if (!!((__VLS_ctx.sidebarStore.collapsed || __VLS_ctx.isNarrowDesktop) && !__VLS_ctx.isMobile))
                    return;
                __VLS_ctx.navigateTo(__VLS_ctx.settingsNavItem.key);
                // @ts-ignore
                [navigateTo, settingsNavItem,];
            } },
        ...{ class: "nav-item" },
        ...{ class: ({ active: __VLS_ctx.isActive(__VLS_ctx.settingsNavItem.key) }) },
    });
    /** @type {__VLS_StyleScopedClasses['nav-item']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    let __VLS_82;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_83 = __VLS_asFunctionalComponent1(__VLS_82, new __VLS_82({
        size: (20),
    }));
    const __VLS_84 = __VLS_83({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_83));
    const { default: __VLS_87 } = __VLS_85.slots;
    const __VLS_88 = (__VLS_ctx.settingsNavItem.icon);
    // @ts-ignore
    const __VLS_89 = __VLS_asFunctionalComponent1(__VLS_88, new __VLS_88({}));
    const __VLS_90 = __VLS_89({}, ...__VLS_functionalComponentArgsRest(__VLS_89));
    // @ts-ignore
    [isActive, settingsNavItem, settingsNavItem,];
    var __VLS_85;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "nav-label" },
    });
    /** @type {__VLS_StyleScopedClasses['nav-label']} */ ;
    (__VLS_ctx.settingsNavItem.label);
    if (__VLS_ctx.showUpdateBadge) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
            ...{ class: "update-dot" },
        });
        /** @type {__VLS_StyleScopedClasses['update-dot']} */ ;
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-footer" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "footer-version" },
});
/** @type {__VLS_StyleScopedClasses['footer-version']} */ ;
(__VLS_ctx.appVersion);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onMousedown: (__VLS_ctx.onResizeStart) },
    ...{ onDblclick: (__VLS_ctx.onResizeDoubleClick) },
    ...{ class: "sidebar-resize-handle" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-resize-handle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.main, __VLS_intrinsics.main)({
    ...{ class: "main-content" },
});
/** @type {__VLS_StyleScopedClasses['main-content']} */ ;
let __VLS_93;
/** @ts-ignore @type {typeof __VLS_components.RouterView | typeof __VLS_components.RouterView} */
RouterView;
// @ts-ignore
const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({}));
const __VLS_95 = __VLS_94({}, ...__VLS_functionalComponentArgsRest(__VLS_94));
{
    const { default: __VLS_98 } = __VLS_96.slots;
    const [{ Component }] = __VLS_vSlot(__VLS_98);
    if (!__VLS_ctx.routeTransitionsStarted) {
        let __VLS_99;
        /** @ts-ignore @type {typeof __VLS_components.KeepAlive | typeof __VLS_components.KeepAlive} */
        KeepAlive;
        // @ts-ignore
        const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({
            include: (__VLS_ctx.cachedPages),
        }));
        const __VLS_101 = __VLS_100({
            include: (__VLS_ctx.cachedPages),
        }, ...__VLS_functionalComponentArgsRest(__VLS_100));
        const { default: __VLS_104 } = __VLS_102.slots;
        const __VLS_105 = (Component);
        // @ts-ignore
        const __VLS_106 = __VLS_asFunctionalComponent1(__VLS_105, new __VLS_105({
            key: (__VLS_ctx.route.path),
        }));
        const __VLS_107 = __VLS_106({
            key: (__VLS_ctx.route.path),
        }, ...__VLS_functionalComponentArgsRest(__VLS_106));
        // @ts-ignore
        [settingsNavItem, showUpdateBadge, appVersion, onResizeStart, onResizeDoubleClick, routeTransitionsStarted, cachedPages, route,];
        var __VLS_102;
    }
    else {
        let __VLS_110;
        /** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
        Transition;
        // @ts-ignore
        const __VLS_111 = __VLS_asFunctionalComponent1(__VLS_110, new __VLS_110({
            name: (__VLS_ctx.transitionDirection),
            mode: "out-in",
        }));
        const __VLS_112 = __VLS_111({
            name: (__VLS_ctx.transitionDirection),
            mode: "out-in",
        }, ...__VLS_functionalComponentArgsRest(__VLS_111));
        const { default: __VLS_115 } = __VLS_113.slots;
        let __VLS_116;
        /** @ts-ignore @type {typeof __VLS_components.KeepAlive | typeof __VLS_components.KeepAlive} */
        KeepAlive;
        // @ts-ignore
        const __VLS_117 = __VLS_asFunctionalComponent1(__VLS_116, new __VLS_116({
            include: (__VLS_ctx.cachedPages),
        }));
        const __VLS_118 = __VLS_117({
            include: (__VLS_ctx.cachedPages),
        }, ...__VLS_functionalComponentArgsRest(__VLS_117));
        const { default: __VLS_121 } = __VLS_119.slots;
        const __VLS_122 = (Component);
        // @ts-ignore
        const __VLS_123 = __VLS_asFunctionalComponent1(__VLS_122, new __VLS_122({
            key: (__VLS_ctx.route.path),
        }));
        const __VLS_124 = __VLS_123({
            key: (__VLS_ctx.route.path),
        }, ...__VLS_functionalComponentArgsRest(__VLS_123));
        // @ts-ignore
        [cachedPages, route, transitionDirection,];
        var __VLS_119;
        // @ts-ignore
        [];
        var __VLS_113;
    }
    // @ts-ignore
    [];
    __VLS_96.slots['' /* empty slot name completion */];
}
var __VLS_96;
const __VLS_127 = InstallProgressDrawer;
// @ts-ignore
const __VLS_128 = __VLS_asFunctionalComponent1(__VLS_127, new __VLS_127({}));
const __VLS_129 = __VLS_128({}, ...__VLS_functionalComponentArgsRest(__VLS_128));
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
