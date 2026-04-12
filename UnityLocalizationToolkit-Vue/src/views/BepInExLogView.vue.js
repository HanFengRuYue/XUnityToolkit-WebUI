import { ref, computed, onMounted } from 'vue';
import { formatBytes } from '@/utils/format';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NIcon, NInput, NSelect, NSpin, useMessage } from 'naive-ui';
import { ArrowBackOutlined, RefreshOutlined, FileDownloadOutlined, AutoFixHighOutlined, TerminalOutlined, SearchOutlined, } from '@vicons/material';
import { bepinexLogApi, gamesApi } from '@/api/games';
import { Marked } from 'marked';
defineOptions({ name: 'BepInExLogView' });
const route = useRoute();
const router = useRouter();
const message = useMessage();
const gameId = computed(() => route.params.id);
const game = ref(null);
// State
const logContent = ref('');
const fileSize = ref(0);
const lastModified = ref('');
const loading = ref(false);
const analyzing = ref(false);
const analysisResult = ref(null);
const searchQuery = ref('');
const levelFilter = ref('All');
// Level filter options
const levelOptions = [
    { label: '全部', value: 'All' },
    { label: 'Info', value: 'Info' },
    { label: 'Warning', value: 'Warning' },
    { label: 'Error', value: 'Error' },
];
// BepInEx log line format: [Level  : Source] Message
// Level can be: Info, Warning, Error, Fatal, Debug, Message
const logLevelRegex = /^\[(Info|Warning|Error|Fatal|Debug|Message)\s*:/i;
// Parse log lines with level detection
const parsedLines = computed(() => {
    if (!logContent.value)
        return [];
    const raw = logContent.value.split('\n');
    const result = [];
    let currentLevel = 'Info';
    for (const line of raw) {
        const match = line.match(logLevelRegex);
        if (match && match[1]) {
            currentLevel = match[1];
        }
        result.push({ text: line, level: currentLevel });
    }
    return result;
});
// Filtered lines
const filteredLines = computed(() => {
    let lines = parsedLines.value;
    // Level filter (Error includes Fatal)
    if (levelFilter.value !== 'All') {
        const filterVal = levelFilter.value.toLowerCase();
        lines = lines.filter((l) => {
            const lvl = l.level.toLowerCase();
            if (filterVal === 'error')
                return lvl === 'error' || lvl === 'fatal';
            return lvl === filterVal;
        });
    }
    // Search filter
    if (searchQuery.value) {
        const q = searchQuery.value.toLowerCase();
        lines = lines.filter((l) => l.text.toLowerCase().includes(q));
    }
    return lines;
});
// Level to CSS class
function levelClass(level) {
    switch (level.toLowerCase()) {
        case 'error':
        case 'fatal':
            return 'log-error';
        case 'warning':
            return 'log-warning';
        default:
            return 'log-info';
    }
}
// Load log
async function loadLog() {
    loading.value = true;
    try {
        const resp = await bepinexLogApi.get(gameId.value);
        logContent.value = resp.content;
        fileSize.value = resp.fileSize;
        lastModified.value = resp.lastModified;
    }
    catch (e) {
        const msg = e instanceof Error ? e.message : '加载日志失败';
        message.error(msg);
    }
    finally {
        loading.value = false;
    }
}
// Export / download
async function handleExport() {
    try {
        const url = bepinexLogApi.getDownloadUrl(gameId.value);
        const resp = await fetch(url);
        if (!resp.ok)
            throw new Error(`HTTP ${resp.status}`);
        const blob = await resp.blob();
        const blobUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = 'LogOutput.log';
        a.click();
        setTimeout(() => URL.revokeObjectURL(blobUrl), 1000);
    }
    catch (e) {
        const msg = e instanceof Error ? e.message : '导出失败';
        message.error(msg);
    }
}
// AI analysis
async function handleAnalyze() {
    analyzing.value = true;
    analysisResult.value = null;
    try {
        const result = await bepinexLogApi.analyze(gameId.value);
        analysisResult.value = result;
    }
    catch (e) {
        const msg = e instanceof Error ? e.message : 'AI 分析失败';
        message.error(msg);
    }
    finally {
        analyzing.value = false;
    }
}
// Render markdown
const safeMarked = new Marked({
    renderer: {
        html({ text }) {
            return escapeHtml(text);
        }
    }
});
function renderMarkdown(md) {
    return safeMarked.parse(md, { async: false });
}
// Highlight search matches in text
function highlightText(text) {
    if (!searchQuery.value)
        return escapeHtml(text);
    const escaped = escapeHtml(text);
    const q = escapeHtml(searchQuery.value);
    const regex = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
    return escaped.replace(regex, '<mark class="search-highlight">$1</mark>');
}
function escapeHtml(str) {
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
onMounted(async () => {
    try {
        game.value = await gamesApi.get(gameId.value);
    }
    catch { /* ignore */ }
    loadLog();
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['log-line']} */ ;
/** @type {__VLS_StyleScopedClasses['log-line']} */ ;
/** @type {__VLS_StyleScopedClasses['log-line']} */ ;
/** @type {__VLS_StyleScopedClasses['log-line']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
/** @type {__VLS_StyleScopedClasses['filter-row']} */ ;
/** @type {__VLS_StyleScopedClasses['search-input']} */ ;
/** @type {__VLS_StyleScopedClasses['level-select']} */ ;
/** @type {__VLS_StyleScopedClasses['log-content']} */ ;
/** @type {__VLS_StyleScopedClasses['file-meta']} */ ;
/** @type {__VLS_StyleScopedClasses['analysis-meta']} */ ;
/** @type {__VLS_StyleScopedClasses['log-lines']} */ ;
/** @type {__VLS_StyleScopedClasses['log-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sub-page" },
});
/** @type {__VLS_StyleScopedClasses['sub-page']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sub-page-header" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['sub-page-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}`);
            // @ts-ignore
            [router, gameId,];
        } },
    ...{ class: "back-button" },
});
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    size: (20),
}));
const __VLS_2 = __VLS_1({
    size: (20),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
let __VLS_6;
/** @ts-ignore @type {typeof __VLS_components.ArrowBackOutlined} */
ArrowBackOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
// @ts-ignore
[];
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
(__VLS_ctx.game?.name ?? '...');
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "page-title" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['page-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "page-title-icon" },
});
/** @type {__VLS_StyleScopedClasses['page-title-icon']} */ ;
let __VLS_11;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
    size: (24),
}));
const __VLS_13 = __VLS_12({
    size: (24),
}, ...__VLS_functionalComponentArgsRest(__VLS_12));
const { default: __VLS_16 } = __VLS_14.slots;
let __VLS_17;
/** @ts-ignore @type {typeof __VLS_components.TerminalOutlined} */
TerminalOutlined;
// @ts-ignore
const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
// @ts-ignore
[game,];
var __VLS_14;
if (__VLS_ctx.fileSize) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "file-meta" },
    });
    /** @type {__VLS_StyleScopedClasses['file-meta']} */ ;
    (__VLS_ctx.formatBytes(__VLS_ctx.fileSize));
    if (__VLS_ctx.lastModified) {
        (new Date(__VLS_ctx.lastModified).toLocaleString());
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-header" },
});
/** @type {__VLS_StyleScopedClasses['section-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "section-title" },
});
/** @type {__VLS_StyleScopedClasses['section-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "section-icon" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
let __VLS_22;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    size: (16),
}));
const __VLS_24 = __VLS_23({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
const { default: __VLS_27 } = __VLS_25.slots;
let __VLS_28;
/** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
SearchOutlined;
// @ts-ignore
const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
// @ts-ignore
[fileSize, fileSize, formatBytes, lastModified, lastModified,];
var __VLS_25;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "header-actions" },
});
/** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
let __VLS_33;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.loading),
}));
const __VLS_35 = __VLS_34({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.loading),
}, ...__VLS_functionalComponentArgsRest(__VLS_34));
let __VLS_38;
const __VLS_39 = ({ click: {} },
    { onClick: (__VLS_ctx.loadLog) });
const { default: __VLS_40 } = __VLS_36.slots;
{
    const { icon: __VLS_41 } = __VLS_36.slots;
    let __VLS_42;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({}));
    const __VLS_44 = __VLS_43({}, ...__VLS_functionalComponentArgsRest(__VLS_43));
    const { default: __VLS_47 } = __VLS_45.slots;
    let __VLS_48;
    /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
    RefreshOutlined;
    // @ts-ignore
    const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({}));
    const __VLS_50 = __VLS_49({}, ...__VLS_functionalComponentArgsRest(__VLS_49));
    // @ts-ignore
    [loading, loadLog,];
    var __VLS_45;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_36;
var __VLS_37;
let __VLS_53;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
    ...{ 'onClick': {} },
    size: "small",
    disabled: (!__VLS_ctx.logContent),
}));
const __VLS_55 = __VLS_54({
    ...{ 'onClick': {} },
    size: "small",
    disabled: (!__VLS_ctx.logContent),
}, ...__VLS_functionalComponentArgsRest(__VLS_54));
let __VLS_58;
const __VLS_59 = ({ click: {} },
    { onClick: (__VLS_ctx.handleExport) });
const { default: __VLS_60 } = __VLS_56.slots;
{
    const { icon: __VLS_61 } = __VLS_56.slots;
    let __VLS_62;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_63 = __VLS_asFunctionalComponent1(__VLS_62, new __VLS_62({}));
    const __VLS_64 = __VLS_63({}, ...__VLS_functionalComponentArgsRest(__VLS_63));
    const { default: __VLS_67 } = __VLS_65.slots;
    let __VLS_68;
    /** @ts-ignore @type {typeof __VLS_components.FileDownloadOutlined} */
    FileDownloadOutlined;
    // @ts-ignore
    const __VLS_69 = __VLS_asFunctionalComponent1(__VLS_68, new __VLS_68({}));
    const __VLS_70 = __VLS_69({}, ...__VLS_functionalComponentArgsRest(__VLS_69));
    // @ts-ignore
    [logContent, handleExport,];
    var __VLS_65;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_56;
var __VLS_57;
let __VLS_73;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
    ...{ 'onClick': {} },
    size: "small",
    type: "primary",
    loading: (__VLS_ctx.analyzing),
    disabled: (!__VLS_ctx.logContent),
}));
const __VLS_75 = __VLS_74({
    ...{ 'onClick': {} },
    size: "small",
    type: "primary",
    loading: (__VLS_ctx.analyzing),
    disabled: (!__VLS_ctx.logContent),
}, ...__VLS_functionalComponentArgsRest(__VLS_74));
let __VLS_78;
const __VLS_79 = ({ click: {} },
    { onClick: (__VLS_ctx.handleAnalyze) });
const { default: __VLS_80 } = __VLS_76.slots;
{
    const { icon: __VLS_81 } = __VLS_76.slots;
    let __VLS_82;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_83 = __VLS_asFunctionalComponent1(__VLS_82, new __VLS_82({}));
    const __VLS_84 = __VLS_83({}, ...__VLS_functionalComponentArgsRest(__VLS_83));
    const { default: __VLS_87 } = __VLS_85.slots;
    let __VLS_88;
    /** @ts-ignore @type {typeof __VLS_components.AutoFixHighOutlined} */
    AutoFixHighOutlined;
    // @ts-ignore
    const __VLS_89 = __VLS_asFunctionalComponent1(__VLS_88, new __VLS_88({}));
    const __VLS_90 = __VLS_89({}, ...__VLS_functionalComponentArgsRest(__VLS_89));
    // @ts-ignore
    [logContent, analyzing, handleAnalyze,];
    var __VLS_85;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_76;
var __VLS_77;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "filter-row" },
});
/** @type {__VLS_StyleScopedClasses['filter-row']} */ ;
let __VLS_93;
/** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索日志...",
    clearable: true,
    size: "small",
    ...{ class: "search-input" },
}));
const __VLS_95 = __VLS_94({
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索日志...",
    clearable: true,
    size: "small",
    ...{ class: "search-input" },
}, ...__VLS_functionalComponentArgsRest(__VLS_94));
/** @type {__VLS_StyleScopedClasses['search-input']} */ ;
const { default: __VLS_98 } = __VLS_96.slots;
{
    const { prefix: __VLS_99 } = __VLS_96.slots;
    let __VLS_100;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_101 = __VLS_asFunctionalComponent1(__VLS_100, new __VLS_100({
        size: (16),
    }));
    const __VLS_102 = __VLS_101({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_101));
    const { default: __VLS_105 } = __VLS_103.slots;
    let __VLS_106;
    /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
    SearchOutlined;
    // @ts-ignore
    const __VLS_107 = __VLS_asFunctionalComponent1(__VLS_106, new __VLS_106({}));
    const __VLS_108 = __VLS_107({}, ...__VLS_functionalComponentArgsRest(__VLS_107));
    // @ts-ignore
    [searchQuery,];
    var __VLS_103;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_96;
let __VLS_111;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_112 = __VLS_asFunctionalComponent1(__VLS_111, new __VLS_111({
    value: (__VLS_ctx.levelFilter),
    options: (__VLS_ctx.levelOptions),
    size: "small",
    ...{ class: "level-select" },
}));
const __VLS_113 = __VLS_112({
    value: (__VLS_ctx.levelFilter),
    options: (__VLS_ctx.levelOptions),
    size: "small",
    ...{ class: "level-select" },
}, ...__VLS_functionalComponentArgsRest(__VLS_112));
/** @type {__VLS_StyleScopedClasses['level-select']} */ ;
if (__VLS_ctx.loading && !__VLS_ctx.logContent) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-loading" },
    });
    /** @type {__VLS_StyleScopedClasses['log-loading']} */ ;
    let __VLS_116;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_117 = __VLS_asFunctionalComponent1(__VLS_116, new __VLS_116({
        size: "large",
    }));
    const __VLS_118 = __VLS_117({
        size: "large",
    }, ...__VLS_functionalComponentArgsRest(__VLS_117));
}
else if (!__VLS_ctx.logContent && !__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-empty" },
    });
    /** @type {__VLS_StyleScopedClasses['log-empty']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-content" },
    });
    /** @type {__VLS_StyleScopedClasses['log-content']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-lines" },
    });
    /** @type {__VLS_StyleScopedClasses['log-lines']} */ ;
    for (const [line, idx] of __VLS_vFor((__VLS_ctx.filteredLines))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
            key: (idx),
            ...{ class: "log-line" },
            ...{ class: (__VLS_ctx.levelClass(line.level)) },
        });
        __VLS_asFunctionalDirective(__VLS_directives.vHtml, {})(null, { ...__VLS_directiveBindingRestFields, value: (__VLS_ctx.highlightText(line.text)) }, null, null);
        /** @type {__VLS_StyleScopedClasses['log-line']} */ ;
        // @ts-ignore
        [loading, loading, logContent, logContent, levelFilter, levelOptions, filteredLines, levelClass, highlightText,];
    }
    if (__VLS_ctx.filteredLines.length === 0 && __VLS_ctx.logContent) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "log-empty" },
        });
        /** @type {__VLS_StyleScopedClasses['log-empty']} */ ;
    }
}
if (__VLS_ctx.analyzing || __VLS_ctx.analysisResult) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-header" },
    });
    /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
        ...{ class: "section-title" },
    });
    /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "section-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
    let __VLS_121;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_122 = __VLS_asFunctionalComponent1(__VLS_121, new __VLS_121({
        size: (16),
    }));
    const __VLS_123 = __VLS_122({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_122));
    const { default: __VLS_126 } = __VLS_124.slots;
    let __VLS_127;
    /** @ts-ignore @type {typeof __VLS_components.AutoFixHighOutlined} */
    AutoFixHighOutlined;
    // @ts-ignore
    const __VLS_128 = __VLS_asFunctionalComponent1(__VLS_127, new __VLS_127({}));
    const __VLS_129 = __VLS_128({}, ...__VLS_functionalComponentArgsRest(__VLS_128));
    // @ts-ignore
    [logContent, analyzing, filteredLines, analysisResult,];
    var __VLS_124;
    if (__VLS_ctx.analysisResult) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "analysis-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['analysis-meta']} */ ;
        (__VLS_ctx.analysisResult.endpointName);
        (new Date(__VLS_ctx.analysisResult.analyzedAt).toLocaleString());
    }
    if (__VLS_ctx.analyzing) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "analysis-loading" },
        });
        /** @type {__VLS_StyleScopedClasses['analysis-loading']} */ ;
        let __VLS_132;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_133 = __VLS_asFunctionalComponent1(__VLS_132, new __VLS_132({
            size: "medium",
        }));
        const __VLS_134 = __VLS_133({
            size: "medium",
        }, ...__VLS_functionalComponentArgsRest(__VLS_133));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    }
    else if (__VLS_ctx.analysisResult) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
            ...{ class: "analysis-content markdown-body" },
        });
        __VLS_asFunctionalDirective(__VLS_directives.vHtml, {})(null, { ...__VLS_directiveBindingRestFields, value: (__VLS_ctx.renderMarkdown(__VLS_ctx.analysisResult.report)) }, null, null);
        /** @type {__VLS_StyleScopedClasses['analysis-content']} */ ;
        /** @type {__VLS_StyleScopedClasses['markdown-body']} */ ;
    }
}
// @ts-ignore
[analyzing, analysisResult, analysisResult, analysisResult, analysisResult, analysisResult, renderMarkdown,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
