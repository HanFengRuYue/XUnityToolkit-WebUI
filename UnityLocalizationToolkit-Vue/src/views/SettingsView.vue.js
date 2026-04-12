import { ref, reactive, computed, onMounted, onActivated, watch, nextTick } from 'vue';
import { NButton, NInput, NSelect, NIcon, NProgress, NSpin, NSwitch, NSlider, NColorPicker, NTag, useMessage, useDialog, } from 'naive-ui';
import { CloudDownloadOutlined, InfoOutlined, PaletteOutlined, TuneOutlined, LayersOutlined, DisplaySettingsOutlined, PersonOutlined, OpenInNewOutlined, WarningAmberOutlined, ImageOutlined, ColorLensOutlined, SystemUpdateAltOutlined, FolderOpenOutlined, FileDownloadOutlined, FileUploadOutlined, StorageOutlined, ExpandMoreOutlined, ZoomInOutlined, } from '@vicons/material';
import { LogoGithub } from '@vicons/ionicons5';
import { settingsApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { useThemeStore, accentPresets } from '@/stores/theme';
import { useAutoSave } from '@/composables/useAutoSave';
import { Marked } from 'marked';
import { useUpdateStore } from '@/stores/update';
function escapeHtml(str) {
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
const safeMarked = new Marked({
    renderer: {
        html({ text }) {
            return escapeHtml(text);
        },
    },
});
defineOptions({ name: 'SettingsView' });
const collapsed = reactive({
    covers: true,
    data: true,
});
const message = useMessage();
const dialog = useDialog();
const { selectFile } = useFileExplorer();
const themeStore = useThemeStore();
const updateStore = useUpdateStore();
const showColorPicker = ref(false);
const isCustomAccent = computed(() => !accentPresets.some(p => p.hex === settings.value.accentColor));
// Settings
const modelDownloadSourceOptions = [
    { label: 'HuggingFace', value: 'HuggingFace' },
    { label: 'ModelScope（魔搭社区，默认）', value: 'ModelScope' },
];
const settings = ref({
    hfMirrorUrl: 'https://hf-mirror.com',
    modelDownloadSource: 'ModelScope',
    theme: themeStore.mode,
    aiTranslation: {
        enabled: true,
        activeMode: 'cloud',
        maxConcurrency: 4,
        port: 51821,
        systemPrompt: '',
        temperature: 0.3,
        contextSize: 10,
        localContextSize: 3,
        localMinP: 0.05,
        localRepeatPenalty: 1.0,
        endpoints: [],
        glossaryExtractionEnabled: false,
        enablePreTranslationCache: true,
        termAuditEnabled: true,
        naturalTranslationMode: true,
        enableTranslationMemory: true,
        fuzzyMatchThreshold: 85,
        enableLlmPatternAnalysis: true,
        enableMultiRoundTranslation: true,
        enableAutoTermExtraction: true,
        autoApplyExtractedTerms: false,
    },
    steamGridDbApiKey: undefined,
    libraryViewMode: 'grid',
    librarySortBy: 'name',
    accentColor: themeStore.accentColor,
    libraryCardSize: 'medium',
    libraryGap: 'normal',
    libraryShowLabels: true,
    pageZoom: 0,
    receivePreReleaseUpdates: false,
    installOptions: {
        autoInstallTmpFont: true,
        autoDeployAiEndpoint: true,
        autoGenerateConfig: true,
        autoApplyOptimalConfig: true,
        autoExtractAssets: true,
        autoVerifyHealth: true,
    },
});
const themeOptions = [
    { label: '跟随系统', value: 'system' },
    { label: '深色主题', value: 'dark' },
    { label: '浅色主题', value: 'light' },
];
const { enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(() => settings.value, async () => {
    try {
        await settingsApi.save(settings.value);
    }
    catch {
        message.error('保存设置失败');
    }
}, { debounceMs: 1000, deep: true });
// Sync theme changes immediately when user selects
watch(() => settings.value.theme, (newTheme) => {
    themeStore.setTheme(newTheme);
});
// Sync accent color changes immediately
watch(() => settings.value.accentColor, (newColor) => {
    if (newColor)
        themeStore.setAccentColor(newColor);
});
// Sync page zoom changes immediately
watch(() => settings.value.pageZoom, (newZoom) => {
    if (typeof newZoom === 'number')
        themeStore.setPageZoom(newZoom);
});
function handleZoomChange(value) {
    if (value === null)
        return;
    settings.value.pageZoom = value;
}
function handleZoomReset() {
    settings.value.pageZoom = 0;
}
async function loadSettings() {
    disableAutoSave();
    try {
        const loaded = await settingsApi.get();
        // Use frontend theme store as source of truth (localStorage + OS detection),
        // don't let backend defaults override the correctly detected theme
        loaded.theme = themeStore.mode;
        loaded.accentColor = loaded.accentColor || themeStore.accentColor;
        settings.value = loaded;
    }
    catch {
        // Use defaults
    }
    await nextTick();
    enableAutoSave();
}
// Version
const version = ref('...');
const shortVersion = computed(() => {
    const match = version.value.match(/^(\d+\.\d+)/);
    return match ? match[1] : version.value;
});
const buildNumber = computed(() => {
    const match = version.value.match(/^\d+\.\d+\.(.+)/);
    return match ? match[1] : '';
});
const changelogHtml = computed(() => {
    const md = updateStore.availableInfo?.changelog;
    if (!md)
        return '';
    return safeMarked.parse(md, { async: false });
});
const typeLabels = {
    feat: '新功能',
    fix: '修复',
    docs: '文档',
    refactor: '重构',
    perf: '性能',
    ci: 'CI',
    chore: '杂项',
    style: '样式',
    test: '测试',
};
const changelogEntries = computed(() => {
    const md = updateStore.availableInfo?.changelog;
    if (!md)
        return [];
    const entries = [];
    for (const line of md.split('\n')) {
        // Match conventional commit: * type: description
        const match = line.match(/^[\*\-]\s+(\w+):\s+(.+)\s*$/);
        if (match && match[1] && match[2]) {
            let text = match[2];
            let hash;
            // Extract trailing hash: (abc1234) or (`abc1234`)
            const hashMatch = text.match(/\s*\(?`?([a-f0-9]{6,40})`?\)?\s*$/);
            if (hashMatch && hashMatch[1]) {
                hash = hashMatch[1];
                text = text.slice(0, -hashMatch[0].length).trim();
            }
            entries.push({ type: match[1], text, hash });
        }
        else {
            // Fallback for lines without conventional commit format
            const plain = line.replace(/^[\*\-]\s+/, '').trim();
            if (plain) {
                entries.push({ type: '', text: plain });
            }
        }
    }
    return entries;
});
async function loadVersion() {
    try {
        const info = await settingsApi.getVersion();
        version.value = info.version;
    }
    catch {
        version.value = '1.0.0';
    }
}
// Reset
const resetLoading = ref(false);
function handleReset() {
    dialog.warning({
        title: '重置所有配置',
        content: '此操作将删除所有设置、游戏库、下载缓存和备份数据，且无法恢复。确定要继续吗？',
        positiveText: '确认重置',
        negativeText: '取消',
        onPositiveClick: async () => {
            resetLoading.value = true;
            try {
                const result = await settingsApi.reset();
                if (result.partial) {
                    message.warning('部分数据清除失败，请关闭占用的程序后重试');
                }
                else {
                    message.success('已重置所有配置');
                }
                localStorage.clear();
                setTimeout(() => window.location.reload(), 500);
            }
            catch {
                message.error('重置失败');
            }
            finally {
                resetLoading.value = false;
            }
        },
    });
}
// Data management
const dataPath = ref('');
const exportLoading = ref(false);
const importLoading = ref(false);
async function loadDataPath() {
    try {
        const info = await settingsApi.getDataPath();
        dataPath.value = info.path;
    }
    catch {
        dataPath.value = '(unknown)';
    }
}
async function handleOpenDataFolder() {
    try {
        await settingsApi.openDataFolder();
    }
    catch {
        message.error('无法打开文件夹');
    }
}
async function handleExport() {
    exportLoading.value = true;
    try {
        await settingsApi.exportData();
        message.success('导出成功');
    }
    catch {
        message.error('导出失败');
    }
    finally {
        exportLoading.value = false;
    }
}
async function handleImport() {
    const path = await selectFile({
        title: '导入配置文件',
        filters: [{ label: 'ZIP 文件', extensions: ['.zip'] }],
    });
    if (!path)
        return;
    dialog.warning({
        title: '导入配置',
        content: '导入将覆盖当前所有配置数据，导入完成后需要重启程序。确定要继续吗？',
        positiveText: '确认导入',
        negativeText: '取消',
        onPositiveClick: async () => {
            importLoading.value = true;
            try {
                await settingsApi.importFromPath(path);
                message.success('导入成功，请重启程序以使配置生效');
            }
            catch (e) {
                message.error(e instanceof Error ? e.message : '导入失败');
            }
            finally {
                importLoading.value = false;
            }
        },
    });
}
const hasChecked = ref(false);
async function handleCheckUpdate() {
    await updateStore.checkForUpdate();
    hasChecked.value = true;
    if (updateStore.state === 'None') {
        message.success('已是最新版本');
    }
}
onMounted(() => {
    loadSettings();
    loadVersion();
    loadDataPath();
});
onActivated(() => loadSettings());
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['info-card']} */ ;
/** @type {__VLS_StyleScopedClasses['zoom-slider-row']} */ ;
/** @type {__VLS_StyleScopedClasses['accent-swatch']} */ ;
/** @type {__VLS_StyleScopedClasses['accent-swatch']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-entry']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-entry']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['about-link']} */ ;
/** @type {__VLS_StyleScopedClasses['about-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['data-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['data-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
/** @type {__VLS_StyleScopedClasses['about-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['danger-action']} */ ;
/** @type {__VLS_StyleScopedClasses['data-path-row']} */ ;
/** @type {__VLS_StyleScopedClasses['update-ready']} */ ;
/** @type {__VLS_StyleScopedClasses['update-error']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-page" },
});
/** @type {__VLS_StyleScopedClasses['settings-page']} */ ;
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
/** @ts-ignore @type {typeof __VLS_components.TuneOutlined} */
TuneOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
var __VLS_3;
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
    ...{ class: "section-icon download" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['download']} */ ;
let __VLS_11;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
    size: (16),
}));
const __VLS_13 = __VLS_12({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_12));
const { default: __VLS_16 } = __VLS_14.slots;
let __VLS_17;
/** @ts-ignore @type {typeof __VLS_components.DisplaySettingsOutlined} */
DisplaySettingsOutlined;
// @ts-ignore
const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
var __VLS_14;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-form" },
});
/** @type {__VLS_StyleScopedClasses['settings-form']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_22;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    size: (14),
    color: "var(--text-3)",
}));
const __VLS_24 = __VLS_23({
    size: (14),
    color: "var(--text-3)",
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
const { default: __VLS_27 } = __VLS_25.slots;
let __VLS_28;
/** @ts-ignore @type {typeof __VLS_components.PaletteOutlined} */
PaletteOutlined;
// @ts-ignore
const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
var __VLS_25;
let __VLS_33;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
    value: (__VLS_ctx.settings.theme),
    options: (__VLS_ctx.themeOptions),
}));
const __VLS_35 = __VLS_34({
    value: (__VLS_ctx.settings.theme),
    options: (__VLS_ctx.themeOptions),
}, ...__VLS_functionalComponentArgsRest(__VLS_34));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_38;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_39 = __VLS_asFunctionalComponent1(__VLS_38, new __VLS_38({
    size: (14),
    color: "var(--text-3)",
}));
const __VLS_40 = __VLS_39({
    size: (14),
    color: "var(--text-3)",
}, ...__VLS_functionalComponentArgsRest(__VLS_39));
const { default: __VLS_43 } = __VLS_41.slots;
let __VLS_44;
/** @ts-ignore @type {typeof __VLS_components.ColorLensOutlined} */
ColorLensOutlined;
// @ts-ignore
const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({}));
const __VLS_46 = __VLS_45({}, ...__VLS_functionalComponentArgsRest(__VLS_45));
// @ts-ignore
[settings, themeOptions,];
var __VLS_41;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "accent-color-row" },
});
/** @type {__VLS_StyleScopedClasses['accent-color-row']} */ ;
for (const [preset] of __VLS_vFor((__VLS_ctx.accentPresets))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.settings.accentColor = preset.hex;
                // @ts-ignore
                [settings, accentPresets,];
            } },
        key: (preset.hex),
        ...{ class: "accent-swatch" },
        ...{ class: ({ active: __VLS_ctx.settings.accentColor === preset.hex }) },
        ...{ style: ({ '--swatch-color': preset.hex }) },
        title: (preset.name),
    });
    /** @type {__VLS_StyleScopedClasses['accent-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    if (__VLS_ctx.settings.accentColor === preset.hex) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "swatch-check" },
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['swatch-check']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M4 8.5L7 11.5L12 5",
            stroke: "white",
            'stroke-width': "2",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
    }
    // @ts-ignore
    [settings, settings,];
}
let __VLS_49;
/** @ts-ignore @type {typeof __VLS_components.NColorPicker | typeof __VLS_components.NColorPicker} */
NColorPicker;
// @ts-ignore
const __VLS_50 = __VLS_asFunctionalComponent1(__VLS_49, new __VLS_49({
    ...{ 'onUpdate:show': {} },
    ...{ 'onUpdate:value': {} },
    show: (__VLS_ctx.showColorPicker),
    value: (__VLS_ctx.settings.accentColor),
    modes: (['hex']),
    showAlpha: (false),
    swatches: (__VLS_ctx.accentPresets.map(p => p.hex)),
    actions: ([]),
}));
const __VLS_51 = __VLS_50({
    ...{ 'onUpdate:show': {} },
    ...{ 'onUpdate:value': {} },
    show: (__VLS_ctx.showColorPicker),
    value: (__VLS_ctx.settings.accentColor),
    modes: (['hex']),
    showAlpha: (false),
    swatches: (__VLS_ctx.accentPresets.map(p => p.hex)),
    actions: ([]),
}, ...__VLS_functionalComponentArgsRest(__VLS_50));
let __VLS_54;
const __VLS_55 = ({ 'update:show': {} },
    { 'onUpdate:show': (...[$event]) => {
            __VLS_ctx.showColorPicker = $event;
            // @ts-ignore
            [settings, accentPresets, showColorPicker, showColorPicker,];
        } });
const __VLS_56 = ({ 'update:value': {} },
    { 'onUpdate:value': (...[$event]) => {
            __VLS_ctx.settings.accentColor = $event;
            // @ts-ignore
            [settings,];
        } });
const { default: __VLS_57 } = __VLS_52.slots;
{
    const { trigger: __VLS_58 } = __VLS_52.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.showColorPicker = !__VLS_ctx.showColorPicker;
                // @ts-ignore
                [showColorPicker, showColorPicker,];
            } },
        ...{ class: "accent-swatch custom-swatch" },
        ...{ class: ({ active: __VLS_ctx.isCustomAccent }) },
        ...{ style: (__VLS_ctx.isCustomAccent ? { '--swatch-color': __VLS_ctx.settings.accentColor } : {}) },
        title: "自定义颜色",
    });
    /** @type {__VLS_StyleScopedClasses['accent-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['custom-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    if (__VLS_ctx.isCustomAccent) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "swatch-check" },
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['swatch-check']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M4 8.5L7 11.5L12 5",
            stroke: "white",
            'stroke-width': "2",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
    }
    // @ts-ignore
    [settings, isCustomAccent, isCustomAccent, isCustomAccent,];
}
// @ts-ignore
[];
var __VLS_52;
var __VLS_53;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_59;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({
    size: (14),
    color: "var(--text-3)",
}));
const __VLS_61 = __VLS_60({
    size: (14),
    color: "var(--text-3)",
}, ...__VLS_functionalComponentArgsRest(__VLS_60));
const { default: __VLS_64 } = __VLS_62.slots;
let __VLS_65;
/** @ts-ignore @type {typeof __VLS_components.ZoomInOutlined} */
ZoomInOutlined;
// @ts-ignore
const __VLS_66 = __VLS_asFunctionalComponent1(__VLS_65, new __VLS_65({}));
const __VLS_67 = __VLS_66({}, ...__VLS_functionalComponentArgsRest(__VLS_66));
// @ts-ignore
[];
var __VLS_62;
(__VLS_ctx.themeStore.effectiveZoom);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "zoom-slider-row" },
});
/** @type {__VLS_StyleScopedClasses['zoom-slider-row']} */ ;
let __VLS_70;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_71 = __VLS_asFunctionalComponent1(__VLS_70, new __VLS_70({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.themeStore.effectiveZoom),
    min: (50),
    max: (200),
    step: (5),
    formatTooltip: ((v) => `${v}%`),
}));
const __VLS_72 = __VLS_71({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.themeStore.effectiveZoom),
    min: (50),
    max: (200),
    step: (5),
    formatTooltip: ((v) => `${v}%`),
}, ...__VLS_functionalComponentArgsRest(__VLS_71));
let __VLS_75;
const __VLS_76 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.handleZoomChange) });
var __VLS_73;
var __VLS_74;
let __VLS_77;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_78 = __VLS_asFunctionalComponent1(__VLS_77, new __VLS_77({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    disabled: (__VLS_ctx.settings.pageZoom === 0),
}));
const __VLS_79 = __VLS_78({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    disabled: (__VLS_ctx.settings.pageZoom === 0),
}, ...__VLS_functionalComponentArgsRest(__VLS_78));
let __VLS_82;
const __VLS_83 = ({ click: {} },
    { onClick: (__VLS_ctx.handleZoomReset) });
const { default: __VLS_84 } = __VLS_80.slots;
// @ts-ignore
[settings, themeStore, themeStore, handleZoomChange, handleZoomReset,];
var __VLS_80;
var __VLS_81;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_85;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_86 = __VLS_asFunctionalComponent1(__VLS_85, new __VLS_85({
    size: (14),
    color: "var(--text-3)",
}));
const __VLS_87 = __VLS_86({
    size: (14),
    color: "var(--text-3)",
}, ...__VLS_functionalComponentArgsRest(__VLS_86));
const { default: __VLS_90 } = __VLS_88.slots;
let __VLS_91;
/** @ts-ignore @type {typeof __VLS_components.CloudDownloadOutlined} */
CloudDownloadOutlined;
// @ts-ignore
const __VLS_92 = __VLS_asFunctionalComponent1(__VLS_91, new __VLS_91({}));
const __VLS_93 = __VLS_92({}, ...__VLS_functionalComponentArgsRest(__VLS_92));
// @ts-ignore
[];
var __VLS_88;
let __VLS_96;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_97 = __VLS_asFunctionalComponent1(__VLS_96, new __VLS_96({
    value: (__VLS_ctx.settings.modelDownloadSource),
    options: (__VLS_ctx.modelDownloadSourceOptions),
    ...{ style: {} },
}));
const __VLS_98 = __VLS_97({
    value: (__VLS_ctx.settings.modelDownloadSource),
    options: (__VLS_ctx.modelDownloadSourceOptions),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_97));
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
if (__VLS_ctx.settings.modelDownloadSource === 'HuggingFace') {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_101;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_102 = __VLS_asFunctionalComponent1(__VLS_101, new __VLS_101({
        size: (14),
        color: "var(--text-3)",
    }));
    const __VLS_103 = __VLS_102({
        size: (14),
        color: "var(--text-3)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_102));
    const { default: __VLS_106 } = __VLS_104.slots;
    let __VLS_107;
    /** @ts-ignore @type {typeof __VLS_components.CloudDownloadOutlined} */
    CloudDownloadOutlined;
    // @ts-ignore
    const __VLS_108 = __VLS_asFunctionalComponent1(__VLS_107, new __VLS_107({}));
    const __VLS_109 = __VLS_108({}, ...__VLS_functionalComponentArgsRest(__VLS_108));
    // @ts-ignore
    [settings, settings, modelDownloadSourceOptions,];
    var __VLS_104;
    let __VLS_112;
    /** @ts-ignore @type {typeof __VLS_components.NInput} */
    NInput;
    // @ts-ignore
    const __VLS_113 = __VLS_asFunctionalComponent1(__VLS_112, new __VLS_112({
        value: (__VLS_ctx.settings.hfMirrorUrl),
        placeholder: "https://hf-mirror.com",
        clearable: true,
    }));
    const __VLS_114 = __VLS_113({
        value: (__VLS_ctx.settings.hfMirrorUrl),
        placeholder: "https://hf-mirror.com",
        clearable: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_113));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "form-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card" },
    ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.covers }) },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.collapsed.covers = !__VLS_ctx.collapsed.covers;
            // @ts-ignore
            [settings, collapsed, collapsed, collapsed,];
        } },
    ...{ class: "section-header collapsible" },
});
/** @type {__VLS_StyleScopedClasses['section-header']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "section-title" },
});
/** @type {__VLS_StyleScopedClasses['section-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "section-icon covers" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['covers']} */ ;
let __VLS_117;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_118 = __VLS_asFunctionalComponent1(__VLS_117, new __VLS_117({
    size: (16),
}));
const __VLS_119 = __VLS_118({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_118));
const { default: __VLS_122 } = __VLS_120.slots;
let __VLS_123;
/** @ts-ignore @type {typeof __VLS_components.ImageOutlined} */
ImageOutlined;
// @ts-ignore
const __VLS_124 = __VLS_asFunctionalComponent1(__VLS_123, new __VLS_123({}));
const __VLS_125 = __VLS_124({}, ...__VLS_functionalComponentArgsRest(__VLS_124));
// @ts-ignore
[];
var __VLS_120;
let __VLS_128;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_129 = __VLS_asFunctionalComponent1(__VLS_128, new __VLS_128({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.covers }) },
}));
const __VLS_130 = __VLS_129({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.covers }) },
}, ...__VLS_functionalComponentArgsRest(__VLS_129));
/** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
/** @type {__VLS_StyleScopedClasses['expanded']} */ ;
const { default: __VLS_133 } = __VLS_131.slots;
let __VLS_134;
/** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
ExpandMoreOutlined;
// @ts-ignore
const __VLS_135 = __VLS_asFunctionalComponent1(__VLS_134, new __VLS_134({}));
const __VLS_136 = __VLS_135({}, ...__VLS_functionalComponentArgsRest(__VLS_135));
// @ts-ignore
[collapsed,];
var __VLS_131;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body" },
    ...{ class: ({ collapsed: __VLS_ctx.collapsed.covers }) },
});
/** @type {__VLS_StyleScopedClasses['section-body']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body-inner" },
});
/** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-form" },
});
/** @type {__VLS_StyleScopedClasses['settings-form']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_139;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_140 = __VLS_asFunctionalComponent1(__VLS_139, new __VLS_139({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.settings.steamGridDbApiKey ?? ''),
    type: "password",
    showPasswordOn: "click",
    placeholder: "输入 SteamGridDB API Key",
}));
const __VLS_141 = __VLS_140({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.settings.steamGridDbApiKey ?? ''),
    type: "password",
    showPasswordOn: "click",
    placeholder: "输入 SteamGridDB API Key",
}, ...__VLS_functionalComponentArgsRest(__VLS_140));
let __VLS_144;
const __VLS_145 = ({ 'update:value': {} },
    { 'onUpdate:value': (...[$event]) => {
            __VLS_ctx.settings.steamGridDbApiKey = $event || undefined;
            // @ts-ignore
            [settings, settings, collapsed,];
        } });
var __VLS_142;
var __VLS_143;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
    href: "https://www.steamgriddb.com/profile/preferences/api",
    target: "_blank",
    rel: "noopener noreferrer",
    ...{ class: "about-link" },
});
/** @type {__VLS_StyleScopedClasses['about-link']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card" },
    ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.data }) },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.collapsed.data = !__VLS_ctx.collapsed.data;
            // @ts-ignore
            [collapsed, collapsed, collapsed,];
        } },
    ...{ class: "section-header collapsible" },
});
/** @type {__VLS_StyleScopedClasses['section-header']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "section-title" },
});
/** @type {__VLS_StyleScopedClasses['section-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "section-icon" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
let __VLS_146;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_147 = __VLS_asFunctionalComponent1(__VLS_146, new __VLS_146({
    size: (16),
}));
const __VLS_148 = __VLS_147({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_147));
const { default: __VLS_151 } = __VLS_149.slots;
let __VLS_152;
/** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
StorageOutlined;
// @ts-ignore
const __VLS_153 = __VLS_asFunctionalComponent1(__VLS_152, new __VLS_152({}));
const __VLS_154 = __VLS_153({}, ...__VLS_functionalComponentArgsRest(__VLS_153));
// @ts-ignore
[];
var __VLS_149;
let __VLS_157;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_158 = __VLS_asFunctionalComponent1(__VLS_157, new __VLS_157({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.data }) },
}));
const __VLS_159 = __VLS_158({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.data }) },
}, ...__VLS_functionalComponentArgsRest(__VLS_158));
/** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
/** @type {__VLS_StyleScopedClasses['expanded']} */ ;
const { default: __VLS_162 } = __VLS_160.slots;
let __VLS_163;
/** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
ExpandMoreOutlined;
// @ts-ignore
const __VLS_164 = __VLS_asFunctionalComponent1(__VLS_163, new __VLS_163({}));
const __VLS_165 = __VLS_164({}, ...__VLS_functionalComponentArgsRest(__VLS_164));
// @ts-ignore
[collapsed,];
var __VLS_160;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body" },
    ...{ class: ({ collapsed: __VLS_ctx.collapsed.data }) },
});
/** @type {__VLS_StyleScopedClasses['section-body']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body-inner" },
});
/** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-form" },
});
/** @type {__VLS_StyleScopedClasses['settings-form']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_168;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_169 = __VLS_asFunctionalComponent1(__VLS_168, new __VLS_168({
    size: (14),
    color: "var(--text-3)",
}));
const __VLS_170 = __VLS_169({
    size: (14),
    color: "var(--text-3)",
}, ...__VLS_functionalComponentArgsRest(__VLS_169));
const { default: __VLS_173 } = __VLS_171.slots;
let __VLS_174;
/** @ts-ignore @type {typeof __VLS_components.FolderOpenOutlined} */
FolderOpenOutlined;
// @ts-ignore
const __VLS_175 = __VLS_asFunctionalComponent1(__VLS_174, new __VLS_174({}));
const __VLS_176 = __VLS_175({}, ...__VLS_functionalComponentArgsRest(__VLS_175));
// @ts-ignore
[collapsed,];
var __VLS_171;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "data-path-row" },
});
/** @type {__VLS_StyleScopedClasses['data-path-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "data-path-text" },
});
/** @type {__VLS_StyleScopedClasses['data-path-text']} */ ;
(__VLS_ctx.dataPath);
let __VLS_179;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_180 = __VLS_asFunctionalComponent1(__VLS_179, new __VLS_179({
    ...{ 'onClick': {} },
    size: "small",
}));
const __VLS_181 = __VLS_180({
    ...{ 'onClick': {} },
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_180));
let __VLS_184;
const __VLS_185 = ({ click: {} },
    { onClick: (__VLS_ctx.handleOpenDataFolder) });
const { default: __VLS_186 } = __VLS_182.slots;
// @ts-ignore
[dataPath, handleOpenDataFolder,];
var __VLS_182;
var __VLS_183;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "data-actions" },
});
/** @type {__VLS_StyleScopedClasses['data-actions']} */ ;
let __VLS_187;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_188 = __VLS_asFunctionalComponent1(__VLS_187, new __VLS_187({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.exportLoading),
}));
const __VLS_189 = __VLS_188({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.exportLoading),
}, ...__VLS_functionalComponentArgsRest(__VLS_188));
let __VLS_192;
const __VLS_193 = ({ click: {} },
    { onClick: (__VLS_ctx.handleExport) });
const { default: __VLS_194 } = __VLS_190.slots;
{
    const { icon: __VLS_195 } = __VLS_190.slots;
    let __VLS_196;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_197 = __VLS_asFunctionalComponent1(__VLS_196, new __VLS_196({}));
    const __VLS_198 = __VLS_197({}, ...__VLS_functionalComponentArgsRest(__VLS_197));
    const { default: __VLS_201 } = __VLS_199.slots;
    let __VLS_202;
    /** @ts-ignore @type {typeof __VLS_components.FileDownloadOutlined} */
    FileDownloadOutlined;
    // @ts-ignore
    const __VLS_203 = __VLS_asFunctionalComponent1(__VLS_202, new __VLS_202({}));
    const __VLS_204 = __VLS_203({}, ...__VLS_functionalComponentArgsRest(__VLS_203));
    // @ts-ignore
    [exportLoading, handleExport,];
    var __VLS_199;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_190;
var __VLS_191;
let __VLS_207;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_208 = __VLS_asFunctionalComponent1(__VLS_207, new __VLS_207({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.importLoading),
}));
const __VLS_209 = __VLS_208({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.importLoading),
}, ...__VLS_functionalComponentArgsRest(__VLS_208));
let __VLS_212;
const __VLS_213 = ({ click: {} },
    { onClick: (__VLS_ctx.handleImport) });
const { default: __VLS_214 } = __VLS_210.slots;
{
    const { icon: __VLS_215 } = __VLS_210.slots;
    let __VLS_216;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_217 = __VLS_asFunctionalComponent1(__VLS_216, new __VLS_216({}));
    const __VLS_218 = __VLS_217({}, ...__VLS_functionalComponentArgsRest(__VLS_217));
    const { default: __VLS_221 } = __VLS_219.slots;
    let __VLS_222;
    /** @ts-ignore @type {typeof __VLS_components.FileUploadOutlined} */
    FileUploadOutlined;
    // @ts-ignore
    const __VLS_223 = __VLS_asFunctionalComponent1(__VLS_222, new __VLS_222({}));
    const __VLS_224 = __VLS_223({}, ...__VLS_functionalComponentArgsRest(__VLS_223));
    // @ts-ignore
    [importLoading, handleImport,];
    var __VLS_219;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_210;
var __VLS_211;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
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
    ...{ class: "section-icon danger" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['danger']} */ ;
let __VLS_227;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_228 = __VLS_asFunctionalComponent1(__VLS_227, new __VLS_227({
    size: (16),
}));
const __VLS_229 = __VLS_228({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_228));
const { default: __VLS_232 } = __VLS_230.slots;
let __VLS_233;
/** @ts-ignore @type {typeof __VLS_components.WarningAmberOutlined} */
WarningAmberOutlined;
// @ts-ignore
const __VLS_234 = __VLS_asFunctionalComponent1(__VLS_233, new __VLS_233({}));
const __VLS_235 = __VLS_234({}, ...__VLS_functionalComponentArgsRest(__VLS_234));
// @ts-ignore
[];
var __VLS_230;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "danger-action" },
});
/** @type {__VLS_StyleScopedClasses['danger-action']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "danger-text" },
});
/** @type {__VLS_StyleScopedClasses['danger-text']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "danger-description" },
});
/** @type {__VLS_StyleScopedClasses['danger-description']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "danger-hint" },
});
/** @type {__VLS_StyleScopedClasses['danger-hint']} */ ;
let __VLS_238;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_239 = __VLS_asFunctionalComponent1(__VLS_238, new __VLS_238({
    ...{ 'onClick': {} },
    type: "error",
    loading: (__VLS_ctx.resetLoading),
    ghost: true,
}));
const __VLS_240 = __VLS_239({
    ...{ 'onClick': {} },
    type: "error",
    loading: (__VLS_ctx.resetLoading),
    ghost: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_239));
let __VLS_243;
const __VLS_244 = ({ click: {} },
    { onClick: (__VLS_ctx.handleReset) });
const { default: __VLS_245 } = __VLS_241.slots;
// @ts-ignore
[resetLoading, handleReset,];
var __VLS_241;
var __VLS_242;
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
let __VLS_246;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_247 = __VLS_asFunctionalComponent1(__VLS_246, new __VLS_246({
    size: (16),
}));
const __VLS_248 = __VLS_247({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_247));
const { default: __VLS_251 } = __VLS_249.slots;
let __VLS_252;
/** @ts-ignore @type {typeof __VLS_components.SystemUpdateAltOutlined} */
SystemUpdateAltOutlined;
// @ts-ignore
const __VLS_253 = __VLS_asFunctionalComponent1(__VLS_252, new __VLS_252({}));
const __VLS_254 = __VLS_253({}, ...__VLS_functionalComponentArgsRest(__VLS_253));
// @ts-ignore
[];
var __VLS_249;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "header-actions" },
});
/** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
let __VLS_257;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_258 = __VLS_asFunctionalComponent1(__VLS_257, new __VLS_257({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.updateStore.state === 'Checking'),
    disabled: (__VLS_ctx.updateStore.isDownloading || __VLS_ctx.updateStore.isApplying),
}));
const __VLS_259 = __VLS_258({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.updateStore.state === 'Checking'),
    disabled: (__VLS_ctx.updateStore.isDownloading || __VLS_ctx.updateStore.isApplying),
}, ...__VLS_functionalComponentArgsRest(__VLS_258));
let __VLS_262;
const __VLS_263 = ({ click: {} },
    { onClick: (__VLS_ctx.handleCheckUpdate) });
const { default: __VLS_264 } = __VLS_260.slots;
// @ts-ignore
[updateStore, updateStore, updateStore, handleCheckUpdate,];
var __VLS_260;
var __VLS_261;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "update-version-block" },
});
/** @type {__VLS_StyleScopedClasses['update-version-block']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "update-version-info" },
});
/** @type {__VLS_StyleScopedClasses['update-version-info']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "update-version-number" },
});
/** @type {__VLS_StyleScopedClasses['update-version-number']} */ ;
(__VLS_ctx.shortVersion);
if (__VLS_ctx.buildNumber) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "update-build-number" },
    });
    /** @type {__VLS_StyleScopedClasses['update-build-number']} */ ;
    (__VLS_ctx.buildNumber);
}
if (__VLS_ctx.updateStore.state === 'Checking') {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge checking" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['checking']} */ ;
    let __VLS_265;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_266 = __VLS_asFunctionalComponent1(__VLS_265, new __VLS_265({
        size: (12),
    }));
    const __VLS_267 = __VLS_266({
        size: (12),
    }, ...__VLS_functionalComponentArgsRest(__VLS_266));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
}
else if (__VLS_ctx.updateStore.isUpdateAvailable) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge available" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['available']} */ ;
}
else if (__VLS_ctx.updateStore.isDownloading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge checking" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['checking']} */ ;
    let __VLS_270;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_271 = __VLS_asFunctionalComponent1(__VLS_270, new __VLS_270({
        size: (12),
    }));
    const __VLS_272 = __VLS_271({
        size: (12),
    }, ...__VLS_functionalComponentArgsRest(__VLS_271));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
}
else if (__VLS_ctx.updateStore.isReady) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge ready" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['ready']} */ ;
}
else if (__VLS_ctx.updateStore.isApplying) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge checking" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['checking']} */ ;
    let __VLS_275;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_276 = __VLS_asFunctionalComponent1(__VLS_275, new __VLS_275({
        size: (12),
    }));
    const __VLS_277 = __VLS_276({
        size: (12),
    }, ...__VLS_functionalComponentArgsRest(__VLS_276));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
}
else if (__VLS_ctx.updateStore.hasError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge error-badge" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['error-badge']} */ ;
}
else if (__VLS_ctx.hasChecked) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-status-badge latest" },
    });
    /** @type {__VLS_StyleScopedClasses['update-status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['latest']} */ ;
}
if (__VLS_ctx.updateStore.isUpdateAvailable && __VLS_ctx.updateStore.availableInfo) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-details" },
    });
    /** @type {__VLS_StyleScopedClasses['update-details']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-details-header" },
    });
    /** @type {__VLS_StyleScopedClasses['update-details-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-new-version-row" },
    });
    /** @type {__VLS_StyleScopedClasses['update-new-version-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "update-new-version" },
    });
    /** @type {__VLS_StyleScopedClasses['update-new-version']} */ ;
    (__VLS_ctx.updateStore.availableInfo.version);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "update-size-badge" },
    });
    /** @type {__VLS_StyleScopedClasses['update-size-badge']} */ ;
    let __VLS_280;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_281 = __VLS_asFunctionalComponent1(__VLS_280, new __VLS_280({
        size: (13),
    }));
    const __VLS_282 = __VLS_281({
        size: (13),
    }, ...__VLS_functionalComponentArgsRest(__VLS_281));
    const { default: __VLS_285 } = __VLS_283.slots;
    let __VLS_286;
    /** @ts-ignore @type {typeof __VLS_components.CloudDownloadOutlined} */
    CloudDownloadOutlined;
    // @ts-ignore
    const __VLS_287 = __VLS_asFunctionalComponent1(__VLS_286, new __VLS_286({}));
    const __VLS_288 = __VLS_287({}, ...__VLS_functionalComponentArgsRest(__VLS_287));
    // @ts-ignore
    [updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, shortVersion, buildNumber, buildNumber, hasChecked,];
    var __VLS_283;
    (__VLS_ctx.updateStore.formatBytes(__VLS_ctx.updateStore.availableInfo.downloadSize));
    if (__VLS_ctx.changelogEntries.length) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "update-changelog" },
        });
        /** @type {__VLS_StyleScopedClasses['update-changelog']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "changelog-entries" },
        });
        /** @type {__VLS_StyleScopedClasses['changelog-entries']} */ ;
        for (const [entry, i] of __VLS_vFor((__VLS_ctx.changelogEntries))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (i),
                ...{ class: "changelog-entry" },
            });
            /** @type {__VLS_StyleScopedClasses['changelog-entry']} */ ;
            if (entry.type) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: (['changelog-type-badge', `type-${entry.type}`]) },
                });
                /** @type {__VLS_StyleScopedClasses['changelog-type-badge']} */ ;
                (__VLS_ctx.typeLabels[entry.type] || entry.type);
            }
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "changelog-text" },
            });
            /** @type {__VLS_StyleScopedClasses['changelog-text']} */ ;
            (entry.text);
            if (entry.hash) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.code, __VLS_intrinsics.code)({
                    ...{ class: "changelog-hash" },
                });
                /** @type {__VLS_StyleScopedClasses['changelog-hash']} */ ;
                (entry.hash.slice(0, 7));
            }
            // @ts-ignore
            [updateStore, updateStore, changelogEntries, changelogEntries, typeLabels,];
        }
    }
    else if (__VLS_ctx.changelogHtml) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "update-changelog" },
        });
        /** @type {__VLS_StyleScopedClasses['update-changelog']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "changelog-content" },
        });
        __VLS_asFunctionalDirective(__VLS_directives.vHtml, {})(null, { ...__VLS_directiveBindingRestFields, value: (__VLS_ctx.changelogHtml) }, null, null);
        /** @type {__VLS_StyleScopedClasses['changelog-content']} */ ;
    }
    if (__VLS_ctx.updateStore.availableInfo.changedPackages?.length) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "update-packages" },
        });
        /** @type {__VLS_StyleScopedClasses['update-packages']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "packages-label" },
        });
        /** @type {__VLS_StyleScopedClasses['packages-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "packages-tags" },
        });
        /** @type {__VLS_StyleScopedClasses['packages-tags']} */ ;
        for (const [pkg] of __VLS_vFor((__VLS_ctx.updateStore.availableInfo.changedPackages))) {
            let __VLS_291;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_292 = __VLS_asFunctionalComponent1(__VLS_291, new __VLS_291({
                key: (pkg),
                size: "small",
                round: true,
                bordered: (false),
            }));
            const __VLS_293 = __VLS_292({
                key: (pkg),
                size: "small",
                round: true,
                bordered: (false),
            }, ...__VLS_functionalComponentArgsRest(__VLS_292));
            const { default: __VLS_296 } = __VLS_294.slots;
            (pkg);
            // @ts-ignore
            [updateStore, updateStore, changelogHtml, changelogHtml,];
            var __VLS_294;
            // @ts-ignore
            [];
        }
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['update-actions']} */ ;
    let __VLS_297;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_298 = __VLS_asFunctionalComponent1(__VLS_297, new __VLS_297({
        ...{ 'onClick': {} },
        type: "primary",
    }));
    const __VLS_299 = __VLS_298({
        ...{ 'onClick': {} },
        type: "primary",
    }, ...__VLS_functionalComponentArgsRest(__VLS_298));
    let __VLS_302;
    const __VLS_303 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.updateStore.isUpdateAvailable && __VLS_ctx.updateStore.availableInfo))
                    return;
                __VLS_ctx.updateStore.downloadUpdate();
                // @ts-ignore
                [updateStore,];
            } });
    const { default: __VLS_304 } = __VLS_300.slots;
    {
        const { icon: __VLS_305 } = __VLS_300.slots;
        let __VLS_306;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_307 = __VLS_asFunctionalComponent1(__VLS_306, new __VLS_306({}));
        const __VLS_308 = __VLS_307({}, ...__VLS_functionalComponentArgsRest(__VLS_307));
        const { default: __VLS_311 } = __VLS_309.slots;
        let __VLS_312;
        /** @ts-ignore @type {typeof __VLS_components.CloudDownloadOutlined} */
        CloudDownloadOutlined;
        // @ts-ignore
        const __VLS_313 = __VLS_asFunctionalComponent1(__VLS_312, new __VLS_312({}));
        const __VLS_314 = __VLS_313({}, ...__VLS_functionalComponentArgsRest(__VLS_313));
        // @ts-ignore
        [];
        var __VLS_309;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_300;
    var __VLS_301;
    let __VLS_317;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_318 = __VLS_asFunctionalComponent1(__VLS_317, new __VLS_317({
        ...{ 'onClick': {} },
        quaternary: true,
    }));
    const __VLS_319 = __VLS_318({
        ...{ 'onClick': {} },
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_318));
    let __VLS_322;
    const __VLS_323 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.updateStore.isUpdateAvailable && __VLS_ctx.updateStore.availableInfo))
                    return;
                __VLS_ctx.updateStore.dismissUpdate();
                // @ts-ignore
                [updateStore,];
            } });
    const { default: __VLS_324 } = __VLS_320.slots;
    // @ts-ignore
    [];
    var __VLS_320;
    var __VLS_321;
}
if (__VLS_ctx.updateStore.isDownloading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-progress" },
    });
    /** @type {__VLS_StyleScopedClasses['update-progress']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-top" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-top']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-header" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-header']} */ ;
    if (__VLS_ctx.updateStore.currentPackage) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "progress-package" },
        });
        /** @type {__VLS_StyleScopedClasses['progress-package']} */ ;
        (__VLS_ctx.updateStore.currentPackage);
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-bytes" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-bytes']} */ ;
    (__VLS_ctx.updateStore.formatBytes(__VLS_ctx.updateStore.downloadedBytes));
    (__VLS_ctx.updateStore.formatBytes(__VLS_ctx.updateStore.totalBytes));
    let __VLS_325;
    /** @ts-ignore @type {typeof __VLS_components.NProgress} */
    NProgress;
    // @ts-ignore
    const __VLS_326 = __VLS_asFunctionalComponent1(__VLS_325, new __VLS_325({
        type: "line",
        percentage: (Math.round(__VLS_ctx.updateStore.progress)),
        showIndicator: (false),
        height: (6),
        borderRadius: (3),
    }));
    const __VLS_327 = __VLS_326({
        type: "line",
        percentage: (Math.round(__VLS_ctx.updateStore.progress)),
        showIndicator: (false),
        height: (6),
        borderRadius: (3),
    }, ...__VLS_functionalComponentArgsRest(__VLS_326));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-actions']} */ ;
    let __VLS_330;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_331 = __VLS_asFunctionalComponent1(__VLS_330, new __VLS_330({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }));
    const __VLS_332 = __VLS_331({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_331));
    let __VLS_335;
    const __VLS_336 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.updateStore.isDownloading))
                    return;
                __VLS_ctx.updateStore.cancelDownload();
                // @ts-ignore
                [updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore, updateStore,];
            } });
    const { default: __VLS_337 } = __VLS_333.slots;
    // @ts-ignore
    [];
    var __VLS_333;
    var __VLS_334;
}
if (__VLS_ctx.updateStore.isReady) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-ready" },
    });
    /** @type {__VLS_StyleScopedClasses['update-ready']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "ready-info" },
    });
    /** @type {__VLS_StyleScopedClasses['ready-info']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "ready-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['ready-icon']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    let __VLS_338;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_339 = __VLS_asFunctionalComponent1(__VLS_338, new __VLS_338({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
    }));
    const __VLS_340 = __VLS_339({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_339));
    let __VLS_343;
    const __VLS_344 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.updateStore.isReady))
                    return;
                __VLS_ctx.updateStore.applyUpdate();
                // @ts-ignore
                [updateStore, updateStore,];
            } });
    const { default: __VLS_345 } = __VLS_341.slots;
    // @ts-ignore
    [];
    var __VLS_341;
    var __VLS_342;
}
if (__VLS_ctx.updateStore.isApplying) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-applying" },
    });
    /** @type {__VLS_StyleScopedClasses['update-applying']} */ ;
    let __VLS_346;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_347 = __VLS_asFunctionalComponent1(__VLS_346, new __VLS_346({
        size: (14),
    }));
    const __VLS_348 = __VLS_347({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_347));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.updateStore.message || '正在应用更新，应用即将重启...');
}
if (__VLS_ctx.updateStore.hasError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "update-error" },
    });
    /** @type {__VLS_StyleScopedClasses['update-error']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-text" },
    });
    /** @type {__VLS_StyleScopedClasses['error-text']} */ ;
    (__VLS_ctx.updateStore.error || '更新过程中出现错误');
    let __VLS_351;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_352 = __VLS_asFunctionalComponent1(__VLS_351, new __VLS_351({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_353 = __VLS_352({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_352));
    let __VLS_356;
    const __VLS_357 = ({ click: {} },
        { onClick: (__VLS_ctx.handleCheckUpdate) });
    const { default: __VLS_358 } = __VLS_354.slots;
    // @ts-ignore
    [updateStore, updateStore, updateStore, updateStore, handleCheckUpdate,];
    var __VLS_354;
    var __VLS_355;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "update-divider" },
});
/** @type {__VLS_StyleScopedClasses['update-divider']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "setting-row" },
});
/** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "setting-info" },
});
/** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "setting-label" },
});
/** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "setting-description" },
});
/** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
let __VLS_359;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_360 = __VLS_asFunctionalComponent1(__VLS_359, new __VLS_359({
    value: (__VLS_ctx.settings.receivePreReleaseUpdates),
}));
const __VLS_361 = __VLS_360({
    value: (__VLS_ctx.settings.receivePreReleaseUpdates),
}, ...__VLS_functionalComponentArgsRest(__VLS_360));
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
    ...{ class: "section-icon about" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['about']} */ ;
let __VLS_364;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_365 = __VLS_asFunctionalComponent1(__VLS_364, new __VLS_364({
    size: (16),
}));
const __VLS_366 = __VLS_365({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_365));
const { default: __VLS_369 } = __VLS_367.slots;
let __VLS_370;
/** @ts-ignore @type {typeof __VLS_components.InfoOutlined} */
InfoOutlined;
// @ts-ignore
const __VLS_371 = __VLS_asFunctionalComponent1(__VLS_370, new __VLS_370({}));
const __VLS_372 = __VLS_371({}, ...__VLS_functionalComponentArgsRest(__VLS_371));
// @ts-ignore
[settings,];
var __VLS_367;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "about-grid" },
});
/** @type {__VLS_StyleScopedClasses['about-grid']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card" },
});
/** @type {__VLS_StyleScopedClasses['info-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-icon tech" },
});
/** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['tech']} */ ;
let __VLS_375;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_376 = __VLS_asFunctionalComponent1(__VLS_375, new __VLS_375({
    size: (18),
}));
const __VLS_377 = __VLS_376({
    size: (18),
}, ...__VLS_functionalComponentArgsRest(__VLS_376));
const { default: __VLS_380 } = __VLS_378.slots;
let __VLS_381;
/** @ts-ignore @type {typeof __VLS_components.LayersOutlined} */
LayersOutlined;
// @ts-ignore
const __VLS_382 = __VLS_asFunctionalComponent1(__VLS_381, new __VLS_381({}));
const __VLS_383 = __VLS_382({}, ...__VLS_functionalComponentArgsRest(__VLS_382));
// @ts-ignore
[];
var __VLS_378;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-content" },
});
/** @type {__VLS_StyleScopedClasses['info-card-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-label" },
});
/** @type {__VLS_StyleScopedClasses['info-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-value" },
});
/** @type {__VLS_StyleScopedClasses['info-value']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card" },
});
/** @type {__VLS_StyleScopedClasses['info-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-icon author" },
});
/** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['author']} */ ;
let __VLS_386;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_387 = __VLS_asFunctionalComponent1(__VLS_386, new __VLS_386({
    size: (18),
}));
const __VLS_388 = __VLS_387({
    size: (18),
}, ...__VLS_functionalComponentArgsRest(__VLS_387));
const { default: __VLS_391 } = __VLS_389.slots;
let __VLS_392;
/** @ts-ignore @type {typeof __VLS_components.PersonOutlined} */
PersonOutlined;
// @ts-ignore
const __VLS_393 = __VLS_asFunctionalComponent1(__VLS_392, new __VLS_392({}));
const __VLS_394 = __VLS_393({}, ...__VLS_functionalComponentArgsRest(__VLS_393));
// @ts-ignore
[];
var __VLS_389;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-content" },
});
/** @type {__VLS_StyleScopedClasses['info-card-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-label" },
});
/** @type {__VLS_StyleScopedClasses['info-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-value" },
});
/** @type {__VLS_StyleScopedClasses['info-value']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
    href: "https://github.com/HanFengRuYue",
    target: "_blank",
    rel: "noopener noreferrer",
    ...{ class: "about-link" },
});
/** @type {__VLS_StyleScopedClasses['about-link']} */ ;
let __VLS_397;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_398 = __VLS_asFunctionalComponent1(__VLS_397, new __VLS_397({
    size: (12),
    ...{ style: {} },
}));
const __VLS_399 = __VLS_398({
    size: (12),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_398));
const { default: __VLS_402 } = __VLS_400.slots;
let __VLS_403;
/** @ts-ignore @type {typeof __VLS_components.OpenInNewOutlined} */
OpenInNewOutlined;
// @ts-ignore
const __VLS_404 = __VLS_asFunctionalComponent1(__VLS_403, new __VLS_403({}));
const __VLS_405 = __VLS_404({}, ...__VLS_functionalComponentArgsRest(__VLS_404));
// @ts-ignore
[];
var __VLS_400;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card" },
});
/** @type {__VLS_StyleScopedClasses['info-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-icon github" },
});
/** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['github']} */ ;
let __VLS_408;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_409 = __VLS_asFunctionalComponent1(__VLS_408, new __VLS_408({
    size: (18),
}));
const __VLS_410 = __VLS_409({
    size: (18),
}, ...__VLS_functionalComponentArgsRest(__VLS_409));
const { default: __VLS_413 } = __VLS_411.slots;
let __VLS_414;
/** @ts-ignore @type {typeof __VLS_components.LogoGithub} */
LogoGithub;
// @ts-ignore
const __VLS_415 = __VLS_asFunctionalComponent1(__VLS_414, new __VLS_414({}));
const __VLS_416 = __VLS_415({}, ...__VLS_functionalComponentArgsRest(__VLS_415));
// @ts-ignore
[];
var __VLS_411;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-card-content" },
});
/** @type {__VLS_StyleScopedClasses['info-card-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-label" },
});
/** @type {__VLS_StyleScopedClasses['info-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "info-value" },
});
/** @type {__VLS_StyleScopedClasses['info-value']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.a, __VLS_intrinsics.a)({
    href: "https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI",
    target: "_blank",
    rel: "noopener noreferrer",
    ...{ class: "about-link" },
});
/** @type {__VLS_StyleScopedClasses['about-link']} */ ;
let __VLS_419;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_420 = __VLS_asFunctionalComponent1(__VLS_419, new __VLS_419({
    size: (12),
    ...{ style: {} },
}));
const __VLS_421 = __VLS_420({
    size: (12),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_420));
const { default: __VLS_424 } = __VLS_422.slots;
let __VLS_425;
/** @ts-ignore @type {typeof __VLS_components.OpenInNewOutlined} */
OpenInNewOutlined;
// @ts-ignore
const __VLS_426 = __VLS_asFunctionalComponent1(__VLS_425, new __VLS_425({}));
const __VLS_427 = __VLS_426({}, ...__VLS_functionalComponentArgsRest(__VLS_426));
// @ts-ignore
[];
var __VLS_422;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
