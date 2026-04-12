import { ref, computed, watch, onMounted, onBeforeUnmount, onDeactivated, onActivated, nextTick } from 'vue';
import { NButton, NIcon, NInput, NSwitch, NTooltip } from 'naive-ui';
import { TerminalOutlined, SearchOutlined, FileDownloadOutlined, DeleteOutlined, HistoryOutlined, VerticalAlignBottomOutlined, InfoOutlined, WarningAmberOutlined, BugReportOutlined, } from '@vicons/material';
import { useLogStore } from '@/stores/log';
import { logsApi } from '@/api/games';
defineOptions({ name: 'LogView' });
const logStore = useLogStore();
const selectedLevels = ref(new Set(['DBG', 'INF', 'WRN', 'ERR', 'CRI']));
const keyword = ref('');
const autoScroll = ref(true);
const loading = ref(false);
const historyLoading = ref(false);
const containerRef = ref(null);
const levelDefs = [
    { key: 'DBG', label: 'DEBUG', icon: BugReportOutlined, colorVar: '--text-3' },
    { key: 'INF', label: 'INFO', icon: InfoOutlined, colorVar: '--accent' },
    { key: 'WRN', label: 'WARN', icon: WarningAmberOutlined, colorVar: '--warning' },
    { key: 'ERR', label: 'ERROR', icon: BugReportOutlined, colorVar: '--danger' },
    { key: 'CRI', label: 'CRIT', icon: BugReportOutlined, colorVar: '--danger' },
];
function toggleLevel(key) {
    const s = new Set(selectedLevels.value);
    if (s.has(key))
        s.delete(key);
    else
        s.add(key);
    selectedLevels.value = s;
}
const levelCounts = logStore.levelCounts;
const normalizedKeyword = computed(() => keyword.value.trim().toLowerCase());
const filteredEntries = computed(() => {
    const levels = selectedLevels.value;
    const kw = normalizedKeyword.value;
    return logStore.entries.filter(e => {
        if (!levels.has(e.level))
            return false;
        if (kw && !e.message.toLowerCase().includes(kw) && !e.category.toLowerCase().includes(kw))
            return false;
        return true;
    });
});
function scrollToBottom() {
    const el = containerRef.value;
    if (el)
        el.scrollTop = el.scrollHeight;
}
function handleScroll() {
    const el = containerRef.value;
    if (!el)
        return;
    const nearBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 50;
    autoScroll.value = nearBottom;
}
watch(() => logStore.entries.length, async () => {
    if (autoScroll.value) {
        await nextTick();
        scrollToBottom();
    }
});
async function handleLoadHistory() {
    historyLoading.value = true;
    try {
        await logStore.fetchHistory();
    }
    finally {
        historyLoading.value = false;
    }
    if (autoScroll.value) {
        await nextTick();
        scrollToBottom();
    }
}
function handleExport() {
    window.open(logsApi.getDownloadUrl(), '_blank');
}
function handleClear() {
    logStore.clearLocal();
}
onMounted(async () => {
    loading.value = true;
    try {
        await logStore.fetchRecent();
        await logStore.connect();
    }
    finally {
        loading.value = false;
    }
    await nextTick();
    scrollToBottom();
});
onActivated(async () => {
    if (loading.value)
        return;
    await logStore.connect();
    if (autoScroll.value) {
        await nextTick();
        scrollToBottom();
    }
});
onDeactivated(() => {
    logStore.disconnect();
});
onBeforeUnmount(() => {
    logStore.disconnect();
});
function levelClass(level) {
    switch (level) {
        case 'ERR':
        case 'CRI': return 'level-err';
        case 'WRN': return 'level-wrn';
        case 'DBG':
        case 'TRC': return 'level-dbg';
        default: return 'level-inf';
    }
}
function formatTimestamp(ts) {
    return ts.substring(11, 23);
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-inf']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-count']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-wrn']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-count']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-err']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-count']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-cri']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-count']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['pill-cri']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-card']} */ ;
/** @type {__VLS_StyleScopedClasses['log-panel']} */ ;
/** @type {__VLS_StyleScopedClasses['log-panel']} */ ;
/** @type {__VLS_StyleScopedClasses['empty-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['level-err']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['level-wrn']} */ ;
/** @type {__VLS_StyleScopedClasses['log-level-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['log-level-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['level-wrn']} */ ;
/** @type {__VLS_StyleScopedClasses['log-level-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['level-err']} */ ;
/** @type {__VLS_StyleScopedClasses['log-level-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['level-err']} */ ;
/** @type {__VLS_StyleScopedClasses['log-message']} */ ;
/** @type {__VLS_StyleScopedClasses['log-page']} */ ;
/** @type {__VLS_StyleScopedClasses['stats-row']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
/** @type {__VLS_StyleScopedClasses['search-box']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-card']} */ ;
/** @type {__VLS_StyleScopedClasses['log-row']} */ ;
/** @type {__VLS_StyleScopedClasses['log-line-no']} */ ;
/** @type {__VLS_StyleScopedClasses['stats-row']} */ ;
/** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['log-line-no']} */ ;
/** @type {__VLS_StyleScopedClasses['log-category']} */ ;
/** @type {__VLS_StyleScopedClasses['log-time']} */ ;
/** @type {__VLS_StyleScopedClasses['auto-scroll-toggle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "log-page" },
});
/** @type {__VLS_StyleScopedClasses['log-page']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "page-title" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['page-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "page-title-icon" },
});
/** @type {__VLS_StyleScopedClasses['page-title-icon']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    size: (24),
}));
const __VLS_2 = __VLS_1({
    size: (24),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
let __VLS_6;
/** @ts-ignore @type {typeof __VLS_components.TerminalOutlined} */
TerminalOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "stats-row" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['stats-row']} */ ;
for (const [def] of __VLS_vFor((__VLS_ctx.levelDefs))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.toggleLevel(def.key);
                // @ts-ignore
                [levelDefs, toggleLevel,];
            } },
        key: (def.key),
        ...{ class: "level-pill" },
        ...{ class: ([`pill-${def.key.toLowerCase()}`, { active: __VLS_ctx.selectedLevels.has(def.key), 'has-count': (__VLS_ctx.levelCounts[def.key] ?? 0) > 0 }]) },
    });
    /** @type {__VLS_StyleScopedClasses['level-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    /** @type {__VLS_StyleScopedClasses['has-count']} */ ;
    let __VLS_11;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
        size: (14),
    }));
    const __VLS_13 = __VLS_12({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_12));
    const { default: __VLS_16 } = __VLS_14.slots;
    const __VLS_17 = (def.icon);
    // @ts-ignore
    const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
    const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
    // @ts-ignore
    [selectedLevels, levelCounts,];
    var __VLS_14;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "pill-label" },
    });
    /** @type {__VLS_StyleScopedClasses['pill-label']} */ ;
    (def.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "pill-count" },
    });
    /** @type {__VLS_StyleScopedClasses['pill-count']} */ ;
    (__VLS_ctx.levelCounts[def.key]);
    // @ts-ignore
    [levelCounts,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-card" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['toolbar-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar" },
});
/** @type {__VLS_StyleScopedClasses['toolbar']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-left" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-box" },
});
/** @type {__VLS_StyleScopedClasses['search-box']} */ ;
let __VLS_22;
/** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    value: (__VLS_ctx.keyword),
    placeholder: "搜索日志内容或分类...",
    clearable: true,
    size: "small",
}));
const __VLS_24 = __VLS_23({
    value: (__VLS_ctx.keyword),
    placeholder: "搜索日志内容或分类...",
    clearable: true,
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
const { default: __VLS_27 } = __VLS_25.slots;
{
    const { prefix: __VLS_28 } = __VLS_25.slots;
    let __VLS_29;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_30 = __VLS_asFunctionalComponent1(__VLS_29, new __VLS_29({
        size: (14),
        color: "var(--text-3)",
    }));
    const __VLS_31 = __VLS_30({
        size: (14),
        color: "var(--text-3)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_30));
    const { default: __VLS_34 } = __VLS_32.slots;
    let __VLS_35;
    /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
    SearchOutlined;
    // @ts-ignore
    const __VLS_36 = __VLS_asFunctionalComponent1(__VLS_35, new __VLS_35({}));
    const __VLS_37 = __VLS_36({}, ...__VLS_functionalComponentArgsRest(__VLS_36));
    // @ts-ignore
    [keyword,];
    var __VLS_32;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_25;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-right" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "auto-scroll-toggle" },
});
/** @type {__VLS_StyleScopedClasses['auto-scroll-toggle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "toggle-label" },
});
/** @type {__VLS_StyleScopedClasses['toggle-label']} */ ;
let __VLS_40;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_41 = __VLS_asFunctionalComponent1(__VLS_40, new __VLS_40({
    value: (__VLS_ctx.autoScroll),
    size: "small",
}));
const __VLS_42 = __VLS_41({
    value: (__VLS_ctx.autoScroll),
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_41));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-divider" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-divider']} */ ;
let __VLS_45;
/** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
NTooltip;
// @ts-ignore
const __VLS_46 = __VLS_asFunctionalComponent1(__VLS_45, new __VLS_45({
    trigger: "hover",
}));
const __VLS_47 = __VLS_46({
    trigger: "hover",
}, ...__VLS_functionalComponentArgsRest(__VLS_46));
const { default: __VLS_50 } = __VLS_48.slots;
{
    const { trigger: __VLS_51 } = __VLS_48.slots;
    let __VLS_52;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_53 = __VLS_asFunctionalComponent1(__VLS_52, new __VLS_52({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
        loading: (__VLS_ctx.historyLoading),
    }));
    const __VLS_54 = __VLS_53({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
        loading: (__VLS_ctx.historyLoading),
    }, ...__VLS_functionalComponentArgsRest(__VLS_53));
    let __VLS_57;
    const __VLS_58 = ({ click: {} },
        { onClick: (__VLS_ctx.handleLoadHistory) });
    const { default: __VLS_59 } = __VLS_55.slots;
    {
        const { icon: __VLS_60 } = __VLS_55.slots;
        let __VLS_61;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_62 = __VLS_asFunctionalComponent1(__VLS_61, new __VLS_61({}));
        const __VLS_63 = __VLS_62({}, ...__VLS_functionalComponentArgsRest(__VLS_62));
        const { default: __VLS_66 } = __VLS_64.slots;
        let __VLS_67;
        /** @ts-ignore @type {typeof __VLS_components.HistoryOutlined} */
        HistoryOutlined;
        // @ts-ignore
        const __VLS_68 = __VLS_asFunctionalComponent1(__VLS_67, new __VLS_67({}));
        const __VLS_69 = __VLS_68({}, ...__VLS_functionalComponentArgsRest(__VLS_68));
        // @ts-ignore
        [autoScroll, historyLoading, handleLoadHistory,];
        var __VLS_64;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_55;
    var __VLS_56;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_48;
let __VLS_72;
/** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
NTooltip;
// @ts-ignore
const __VLS_73 = __VLS_asFunctionalComponent1(__VLS_72, new __VLS_72({
    trigger: "hover",
}));
const __VLS_74 = __VLS_73({
    trigger: "hover",
}, ...__VLS_functionalComponentArgsRest(__VLS_73));
const { default: __VLS_77 } = __VLS_75.slots;
{
    const { trigger: __VLS_78 } = __VLS_75.slots;
    let __VLS_79;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }));
    const __VLS_81 = __VLS_80({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_80));
    let __VLS_84;
    const __VLS_85 = ({ click: {} },
        { onClick: (__VLS_ctx.handleExport) });
    const { default: __VLS_86 } = __VLS_82.slots;
    {
        const { icon: __VLS_87 } = __VLS_82.slots;
        let __VLS_88;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_89 = __VLS_asFunctionalComponent1(__VLS_88, new __VLS_88({}));
        const __VLS_90 = __VLS_89({}, ...__VLS_functionalComponentArgsRest(__VLS_89));
        const { default: __VLS_93 } = __VLS_91.slots;
        let __VLS_94;
        /** @ts-ignore @type {typeof __VLS_components.FileDownloadOutlined} */
        FileDownloadOutlined;
        // @ts-ignore
        const __VLS_95 = __VLS_asFunctionalComponent1(__VLS_94, new __VLS_94({}));
        const __VLS_96 = __VLS_95({}, ...__VLS_functionalComponentArgsRest(__VLS_95));
        // @ts-ignore
        [handleExport,];
        var __VLS_91;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_82;
    var __VLS_83;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_75;
let __VLS_99;
/** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
NTooltip;
// @ts-ignore
const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({
    trigger: "hover",
}));
const __VLS_101 = __VLS_100({
    trigger: "hover",
}, ...__VLS_functionalComponentArgsRest(__VLS_100));
const { default: __VLS_104 } = __VLS_102.slots;
{
    const { trigger: __VLS_105 } = __VLS_102.slots;
    let __VLS_106;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_107 = __VLS_asFunctionalComponent1(__VLS_106, new __VLS_106({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }));
    const __VLS_108 = __VLS_107({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_107));
    let __VLS_111;
    const __VLS_112 = ({ click: {} },
        { onClick: (__VLS_ctx.handleClear) });
    const { default: __VLS_113 } = __VLS_109.slots;
    {
        const { icon: __VLS_114 } = __VLS_109.slots;
        let __VLS_115;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_116 = __VLS_asFunctionalComponent1(__VLS_115, new __VLS_115({}));
        const __VLS_117 = __VLS_116({}, ...__VLS_functionalComponentArgsRest(__VLS_116));
        const { default: __VLS_120 } = __VLS_118.slots;
        let __VLS_121;
        /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
        DeleteOutlined;
        // @ts-ignore
        const __VLS_122 = __VLS_asFunctionalComponent1(__VLS_121, new __VLS_121({}));
        const __VLS_123 = __VLS_122({}, ...__VLS_functionalComponentArgsRest(__VLS_122));
        // @ts-ignore
        [handleClear,];
        var __VLS_118;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_109;
    var __VLS_110;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_102;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "log-panel" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['log-panel']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onScroll: (__VLS_ctx.handleScroll) },
    ref: "containerRef",
    ...{ class: "log-container" },
});
/** @type {__VLS_StyleScopedClasses['log-container']} */ ;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-empty-state" },
    });
    /** @type {__VLS_StyleScopedClasses['log-empty-state']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "empty-icon loading-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['loading-icon']} */ ;
    let __VLS_126;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_127 = __VLS_asFunctionalComponent1(__VLS_126, new __VLS_126({
        size: (32),
    }));
    const __VLS_128 = __VLS_127({
        size: (32),
    }, ...__VLS_functionalComponentArgsRest(__VLS_127));
    const { default: __VLS_131 } = __VLS_129.slots;
    let __VLS_132;
    /** @ts-ignore @type {typeof __VLS_components.TerminalOutlined} */
    TerminalOutlined;
    // @ts-ignore
    const __VLS_133 = __VLS_asFunctionalComponent1(__VLS_132, new __VLS_132({}));
    const __VLS_134 = __VLS_133({}, ...__VLS_functionalComponentArgsRest(__VLS_133));
    // @ts-ignore
    [handleScroll, loading,];
    var __VLS_129;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "empty-text" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-text']} */ ;
}
else if (__VLS_ctx.filteredEntries.length === 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "log-empty-state" },
    });
    /** @type {__VLS_StyleScopedClasses['log-empty-state']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "empty-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-icon']} */ ;
    let __VLS_137;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_138 = __VLS_asFunctionalComponent1(__VLS_137, new __VLS_137({
        size: (32),
    }));
    const __VLS_139 = __VLS_138({
        size: (32),
    }, ...__VLS_functionalComponentArgsRest(__VLS_138));
    const { default: __VLS_142 } = __VLS_140.slots;
    let __VLS_143;
    /** @ts-ignore @type {typeof __VLS_components.TerminalOutlined} */
    TerminalOutlined;
    // @ts-ignore
    const __VLS_144 = __VLS_asFunctionalComponent1(__VLS_143, new __VLS_143({}));
    const __VLS_145 = __VLS_144({}, ...__VLS_functionalComponentArgsRest(__VLS_144));
    // @ts-ignore
    [filteredEntries,];
    var __VLS_140;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "empty-text" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-text']} */ ;
    (__VLS_ctx.keyword ? '没有匹配的日志' : '暂无日志记录');
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "empty-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-hint']} */ ;
    (__VLS_ctx.keyword ? '尝试调整搜索关键词或筛选条件' : '日志将在应用运行时实时显示');
}
else {
    for (const [entry, i] of __VLS_vFor((__VLS_ctx.filteredEntries))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (`${entry.timestamp}-${i}`),
            ...{ class: "log-row" },
            ...{ class: (__VLS_ctx.levelClass(entry.level)) },
        });
        /** @type {__VLS_StyleScopedClasses['log-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "log-line-no" },
        });
        /** @type {__VLS_StyleScopedClasses['log-line-no']} */ ;
        (i + 1);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "log-time" },
        });
        /** @type {__VLS_StyleScopedClasses['log-time']} */ ;
        (__VLS_ctx.formatTimestamp(entry.timestamp));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "log-level-badge" },
            ...{ class: (__VLS_ctx.levelClass(entry.level)) },
        });
        /** @type {__VLS_StyleScopedClasses['log-level-badge']} */ ;
        (entry.level);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "log-category" },
        });
        /** @type {__VLS_StyleScopedClasses['log-category']} */ ;
        (entry.category);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "log-message" },
        });
        /** @type {__VLS_StyleScopedClasses['log-message']} */ ;
        (entry.message);
        // @ts-ignore
        [keyword, keyword, filteredEntries, levelClass, levelClass, formatTimestamp,];
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "log-footer" },
});
/** @type {__VLS_StyleScopedClasses['log-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "footer-left" },
});
/** @type {__VLS_StyleScopedClasses['footer-left']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "log-count" },
});
/** @type {__VLS_StyleScopedClasses['log-count']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "count-filtered" },
});
/** @type {__VLS_StyleScopedClasses['count-filtered']} */ ;
(__VLS_ctx.filteredEntries.length);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "count-sep" },
});
/** @type {__VLS_StyleScopedClasses['count-sep']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "count-total" },
});
/** @type {__VLS_StyleScopedClasses['count-total']} */ ;
(__VLS_ctx.logStore.entries.length);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "count-label" },
});
/** @type {__VLS_StyleScopedClasses['count-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "footer-right" },
});
/** @type {__VLS_StyleScopedClasses['footer-right']} */ ;
let __VLS_148;
/** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
Transition;
// @ts-ignore
const __VLS_149 = __VLS_asFunctionalComponent1(__VLS_148, new __VLS_148({
    name: "fade-btn",
}));
const __VLS_150 = __VLS_149({
    name: "fade-btn",
}, ...__VLS_functionalComponentArgsRest(__VLS_149));
const { default: __VLS_153 } = __VLS_151.slots;
if (!__VLS_ctx.autoScroll) {
    let __VLS_154;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_155 = __VLS_asFunctionalComponent1(__VLS_154, new __VLS_154({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
        ...{ class: "scroll-btn" },
    }));
    const __VLS_156 = __VLS_155({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
        ...{ class: "scroll-btn" },
    }, ...__VLS_functionalComponentArgsRest(__VLS_155));
    let __VLS_159;
    const __VLS_160 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(!__VLS_ctx.autoScroll))
                    return;
                __VLS_ctx.autoScroll = true;
                __VLS_ctx.scrollToBottom();
                // @ts-ignore
                [autoScroll, autoScroll, filteredEntries, logStore, scrollToBottom,];
            } });
    /** @type {__VLS_StyleScopedClasses['scroll-btn']} */ ;
    const { default: __VLS_161 } = __VLS_157.slots;
    {
        const { icon: __VLS_162 } = __VLS_157.slots;
        let __VLS_163;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_164 = __VLS_asFunctionalComponent1(__VLS_163, new __VLS_163({
            size: (14),
        }));
        const __VLS_165 = __VLS_164({
            size: (14),
        }, ...__VLS_functionalComponentArgsRest(__VLS_164));
        const { default: __VLS_168 } = __VLS_166.slots;
        let __VLS_169;
        /** @ts-ignore @type {typeof __VLS_components.VerticalAlignBottomOutlined} */
        VerticalAlignBottomOutlined;
        // @ts-ignore
        const __VLS_170 = __VLS_asFunctionalComponent1(__VLS_169, new __VLS_169({}));
        const __VLS_171 = __VLS_170({}, ...__VLS_functionalComponentArgsRest(__VLS_170));
        // @ts-ignore
        [];
        var __VLS_166;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_157;
    var __VLS_158;
}
// @ts-ignore
[];
var __VLS_151;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
