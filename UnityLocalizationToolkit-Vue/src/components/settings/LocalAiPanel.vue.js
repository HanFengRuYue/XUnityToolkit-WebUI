import { ref, computed, onMounted, onBeforeUnmount, onActivated, onDeactivated } from 'vue';
import { NIcon, NButton, NTag, NInputNumber, NInput, NSlider, NProgress, NPopconfirm, useMessage, } from 'naive-ui';
import { PlayArrowOutlined, StopOutlined, DownloadOutlined, DeleteOutlined, FolderOpenOutlined, StorageOutlined, CloseOutlined, PauseOutlined, ExpandMoreOutlined, RestoreOutlined, ScienceOutlined, SystemUpdateAltOutlined, } from '@vicons/material';
import { useLocalLlmStore } from '@/stores/localLlm';
import { localLlmApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts';
import { formatBytes, formatSpeed } from '@/utils/format';
const props = defineProps();
const emit = defineEmits();
function updateAiSettings(patch) {
    emit('update:modelValue', { ...props.modelValue, ...patch });
}
const store = useLocalLlmStore();
const { selectFile } = useFileExplorer();
const message = useMessage();
const gpuLayers = ref(-1);
const contextLength = ref(4096);
const kvCacheType = ref('q8_0');
const catalogExpanded = ref(false);
const testing = ref(false);
const allModels = computed(() => store.settings?.models ?? []);
const pausedDownloads = computed(() => (store.settings?.pausedDownloads ?? []).filter(p => !store.downloads.has(p.catalogId)));
const hasDownloadTasks = computed(() => store.downloads.size > 0 || pausedDownloads.value.length > 0);
// llama binary status
const llamaInstalled = computed(() => store.llamaStatus?.backends?.some(b => b.isInstalled) ?? false);
const showErrorBanner = computed(() => store.status?.state === 'Failed' && !!store.status?.error);
function getDownloadName(downloadId) {
    return store.catalog.find(c => c.id === downloadId)?.name ?? downloadId;
}
function isModelDownloaded(catalogId) {
    return allModels.value.some(m => m.catalogId === catalogId);
}
function isModelPaused(catalogId) {
    return pausedDownloads.value.some(p => p.catalogId === catalogId);
}
// Max GPU VRAM in GB (from DXGI detection)
const maxVramGb = computed(() => {
    if (store.gpus.length === 0)
        return 0;
    const maxBytes = Math.max(...store.gpus.map(g => g.vramBytes));
    return maxBytes / (1024 * 1024 * 1024);
});
function isModelRecommended(item) {
    return maxVramGb.value > 0 && item.recommendedVramGb <= maxVramGb.value;
}
// Sort catalog: recommended models first (by VRAM desc), then non-recommended (by VRAM asc)
const sortedCatalog = computed(() => {
    const vram = maxVramGb.value;
    if (vram <= 0)
        return store.catalog; // No GPU info — keep original order
    return [...store.catalog].sort((a, b) => {
        const aFits = a.recommendedVramGb <= vram;
        const bFits = b.recommendedVramGb <= vram;
        if (aFits && !bFits)
            return -1;
        if (!aFits && bFits)
            return 1;
        if (aFits && bFits)
            return b.recommendedVramGb - a.recommendedVramGb; // Bigger first
        return a.recommendedVramGb - b.recommendedVramGb; // Smaller first
    });
});
function getDownloadProgress(catalogId) {
    return store.downloads.get(catalogId);
}
async function handleStart(model) {
    try {
        await store.startServer(model.filePath, gpuLayers.value, contextLength.value);
        message.success('本地 AI 启动中...');
    }
    catch (e) {
        message.error(`启动失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handleStop() {
    try {
        await store.stopServer();
        message.success('本地 AI 已停止');
    }
    catch (e) {
        message.error(`停止失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handleDownload(catalog) {
    await handleSaveSettings();
    try {
        await store.downloadModel(catalog.id);
        message.info(`开始下载 ${catalog.name}...`);
    }
    catch (e) {
        message.error(`下载失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handleResumeDownload(catalogId) {
    await handleSaveSettings();
    const catalog = store.catalog.find(c => c.id === catalogId);
    try {
        await store.downloadModel(catalogId);
        message.info(`继续下载 ${catalog?.name ?? ''}...`);
        store.fetchSettings(); // clear paused state
    }
    catch (e) {
        message.error(`恢复下载失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handlePauseDownload(catalogId) {
    try {
        await store.pauseDownload(catalogId);
        message.info('下载已暂停');
    }
    catch { /* ignore */ }
}
async function handleCancelDownload(catalogId) {
    try {
        await store.cancelDownload(catalogId);
        message.info('下载已取消');
    }
    catch { /* ignore */ }
}
async function handleAddModel() {
    try {
        const filePath = await selectFile({
            title: '选择 GGUF 模型文件',
            filters: [{ label: 'GGUF 模型', extensions: ['.gguf'] }],
        });
        if (!filePath)
            return;
        const name = filePath.split(/[\\/]/).pop()?.replace('.gguf', '') ?? 'Custom Model';
        await localLlmApi.addModel(filePath, name);
        await store.fetchModels();
        message.success('模型已添加');
    }
    catch (e) {
        message.error(`添加失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handleRemoveModel(model) {
    try {
        await localLlmApi.removeModel(model.id);
        await store.fetchModels();
        message.success('模型已移除');
    }
    catch (e) {
        message.error(`移除失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
}
async function handleTest() {
    testing.value = true;
    try {
        const result = await localLlmApi.test();
        if (result.success) {
            message.success(`测试成功 (${result.responseTimeMs.toFixed(0)}ms)：${result.translations?.join(', ')}`, { duration: 5000 });
        }
        else {
            message.error(`测试失败：${result.error ?? '未知错误'}`, { duration: 5000 });
        }
    }
    catch (e) {
        message.error(`测试失败: ${e instanceof Error ? e.message : '未知错误'}`);
    }
    finally {
        testing.value = false;
    }
}
async function handleSaveSettings() {
    if (gpuLayers.value === null || contextLength.value === null)
        return;
    try {
        await localLlmApi.saveSettings({
            gpuLayers: gpuLayers.value,
            contextLength: contextLength.value,
            kvCacheType: kvCacheType.value,
        });
    }
    catch { /* ignore */ }
}
function handleRestorePrompt() {
    updateAiSettings({ systemPrompt: DEFAULT_SYSTEM_PROMPT });
}
onMounted(async () => {
    await store.connect();
    await Promise.all([
        store.fetchStatus(),
        store.fetchSettings(),
        store.fetchGpus(),
        store.fetchCatalog(),
        store.fetchLlamaStatus(),
    ]);
    if (store.settings) {
        gpuLayers.value = store.settings.gpuLayers;
        contextLength.value = store.settings.contextLength;
        kvCacheType.value = store.settings.kvCacheType ?? 'q8_0';
    }
});
onActivated(async () => {
    await store.connect();
    await store.fetchStatus();
});
onDeactivated(() => {
    store.disconnect();
});
onBeforeUnmount(() => {
    store.disconnect();
});
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['running']} */ ;
/** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['starting']} */ ;
/** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['stopping']} */ ;
/** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['failed']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['info-section']} */ ;
/** @type {__VLS_StyleScopedClasses['info-header']} */ ;
/** @type {__VLS_StyleScopedClasses['info-header']} */ ;
/** @type {__VLS_StyleScopedClasses['clickable']} */ ;
/** @type {__VLS_StyleScopedClasses['collapse-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
/** @type {__VLS_StyleScopedClasses['restore-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['model-item']} */ ;
/** @type {__VLS_StyleScopedClasses['download-task-item']} */ ;
/** @type {__VLS_StyleScopedClasses['catalog-item']} */ ;
/** @type {__VLS_StyleScopedClasses['catalog-item']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['status-right']} */ ;
/** @type {__VLS_StyleScopedClasses['catalog-item']} */ ;
/** @type {__VLS_StyleScopedClasses['catalog-action']} */ ;
/** @type {__VLS_StyleScopedClasses['model-item']} */ ;
/** @type {__VLS_StyleScopedClasses['model-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['download-task-item']} */ ;
/** @type {__VLS_StyleScopedClasses['download-task-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['info-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "local-ai-panel" },
});
/** @type {__VLS_StyleScopedClasses['local-ai-panel']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "status-bar" },
    ...{ class: (__VLS_ctx.store.status?.state?.toLowerCase()) },
});
/** @type {__VLS_StyleScopedClasses['status-bar']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "status-left" },
});
/** @type {__VLS_StyleScopedClasses['status-left']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "status-dot" },
    ...{ class: (__VLS_ctx.store.status?.state?.toLowerCase()) },
});
/** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "status-text" },
});
/** @type {__VLS_StyleScopedClasses['status-text']} */ ;
if (__VLS_ctx.store.status?.state === 'Running') {
}
else if (__VLS_ctx.store.status?.state === 'Starting') {
}
else if (__VLS_ctx.store.status?.state === 'Stopping') {
}
else if (__VLS_ctx.store.status?.state === 'Failed') {
}
else {
}
if (__VLS_ctx.store.isRunning && __VLS_ctx.store.status?.loadedModelName) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-model" },
    });
    /** @type {__VLS_StyleScopedClasses['status-model']} */ ;
    (__VLS_ctx.store.status.loadedModelName);
    if (__VLS_ctx.store.status?.gpuBackendName) {
        let __VLS_0;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
            size: "small",
            type: "info",
        }));
        const __VLS_2 = __VLS_1({
            size: "small",
            type: "info",
        }, ...__VLS_functionalComponentArgsRest(__VLS_1));
        const { default: __VLS_5 } = __VLS_3.slots;
        (__VLS_ctx.store.status.gpuBackendName);
        // @ts-ignore
        [store, store, store, store, store, store, store, store, store, store, store,];
        var __VLS_3;
    }
    if (__VLS_ctx.store.status?.gpuUtilizationPercent != null) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "gpu-stats" },
        });
        /** @type {__VLS_StyleScopedClasses['gpu-stats']} */ ;
        (__VLS_ctx.store.status.gpuUtilizationPercent);
        ((__VLS_ctx.store.status.gpuVramUsedMb / 1024).toFixed(1));
        ((__VLS_ctx.store.status.gpuVramTotalMb / 1024).toFixed(1));
    }
}
if (!__VLS_ctx.store.isRunning && !__VLS_ctx.store.isBusy && __VLS_ctx.llamaInstalled && __VLS_ctx.store.llamaStatus) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-backend-info" },
    });
    /** @type {__VLS_StyleScopedClasses['status-backend-info']} */ ;
    (__VLS_ctx.store.llamaStatus.recommendedBackend);
    (__VLS_ctx.store.llamaStatus.bundledVersion);
}
if (__VLS_ctx.store.isRunning) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-right" },
    });
    /** @type {__VLS_StyleScopedClasses['status-right']} */ ;
    let __VLS_6;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({
        ...{ 'onClick': {} },
        size: "small",
        ghost: true,
        loading: (__VLS_ctx.testing),
    }));
    const __VLS_8 = __VLS_7({
        ...{ 'onClick': {} },
        size: "small",
        ghost: true,
        loading: (__VLS_ctx.testing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_7));
    let __VLS_11;
    const __VLS_12 = ({ click: {} },
        { onClick: (__VLS_ctx.handleTest) });
    const { default: __VLS_13 } = __VLS_9.slots;
    {
        const { icon: __VLS_14 } = __VLS_9.slots;
        let __VLS_15;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_16 = __VLS_asFunctionalComponent1(__VLS_15, new __VLS_15({}));
        const __VLS_17 = __VLS_16({}, ...__VLS_functionalComponentArgsRest(__VLS_16));
        const { default: __VLS_20 } = __VLS_18.slots;
        let __VLS_21;
        /** @ts-ignore @type {typeof __VLS_components.ScienceOutlined} */
        ScienceOutlined;
        // @ts-ignore
        const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({}));
        const __VLS_23 = __VLS_22({}, ...__VLS_functionalComponentArgsRest(__VLS_22));
        // @ts-ignore
        [store, store, store, store, store, store, store, store, store, store, llamaInstalled, testing, handleTest,];
        var __VLS_18;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_9;
    var __VLS_10;
    let __VLS_26;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        ghost: true,
        loading: (__VLS_ctx.store.status?.state === 'Stopping'),
    }));
    const __VLS_28 = __VLS_27({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        ghost: true,
        loading: (__VLS_ctx.store.status?.state === 'Stopping'),
    }, ...__VLS_functionalComponentArgsRest(__VLS_27));
    let __VLS_31;
    const __VLS_32 = ({ click: {} },
        { onClick: (__VLS_ctx.handleStop) });
    const { default: __VLS_33 } = __VLS_29.slots;
    {
        const { icon: __VLS_34 } = __VLS_29.slots;
        let __VLS_35;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_36 = __VLS_asFunctionalComponent1(__VLS_35, new __VLS_35({}));
        const __VLS_37 = __VLS_36({}, ...__VLS_functionalComponentArgsRest(__VLS_36));
        const { default: __VLS_40 } = __VLS_38.slots;
        let __VLS_41;
        /** @ts-ignore @type {typeof __VLS_components.StopOutlined} */
        StopOutlined;
        // @ts-ignore
        const __VLS_42 = __VLS_asFunctionalComponent1(__VLS_41, new __VLS_41({}));
        const __VLS_43 = __VLS_42({}, ...__VLS_functionalComponentArgsRest(__VLS_42));
        // @ts-ignore
        [store, handleStop,];
        var __VLS_38;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_29;
    var __VLS_30;
}
if (__VLS_ctx.llamaInstalled && __VLS_ctx.store.llamaStatus?.needsUpdate && !__VLS_ctx.store.llamaDownload) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-banner update" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
    /** @type {__VLS_StyleScopedClasses['update']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-info" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-info']} */ ;
    let __VLS_46;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_47 = __VLS_asFunctionalComponent1(__VLS_46, new __VLS_46({
        size: (20),
    }));
    const __VLS_48 = __VLS_47({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_47));
    const { default: __VLS_51 } = __VLS_49.slots;
    let __VLS_52;
    /** @ts-ignore @type {typeof __VLS_components.SystemUpdateAltOutlined} */
    SystemUpdateAltOutlined;
    // @ts-ignore
    const __VLS_53 = __VLS_asFunctionalComponent1(__VLS_52, new __VLS_52({}));
    const __VLS_54 = __VLS_53({}, ...__VLS_functionalComponentArgsRest(__VLS_53));
    // @ts-ignore
    [store, store, llamaInstalled,];
    var __VLS_49;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-title" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-desc" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-desc']} */ ;
    (__VLS_ctx.store.llamaStatus.installedVersion);
    (__VLS_ctx.store.llamaStatus.bundledVersion);
    let __VLS_57;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({
        ...{ 'onClick': {} },
        type: "primary",
        disabled: (__VLS_ctx.store.isRunning),
    }));
    const __VLS_59 = __VLS_58({
        ...{ 'onClick': {} },
        type: "primary",
        disabled: (__VLS_ctx.store.isRunning),
    }, ...__VLS_functionalComponentArgsRest(__VLS_58));
    let __VLS_62;
    const __VLS_63 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.llamaInstalled && __VLS_ctx.store.llamaStatus?.needsUpdate && !__VLS_ctx.store.llamaDownload))
                    return;
                __VLS_ctx.store.downloadLlama();
                // @ts-ignore
                [store, store, store, store,];
            } });
    const { default: __VLS_64 } = __VLS_60.slots;
    {
        const { icon: __VLS_65 } = __VLS_60.slots;
        let __VLS_66;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_67 = __VLS_asFunctionalComponent1(__VLS_66, new __VLS_66({}));
        const __VLS_68 = __VLS_67({}, ...__VLS_functionalComponentArgsRest(__VLS_67));
        const { default: __VLS_71 } = __VLS_69.slots;
        let __VLS_72;
        /** @ts-ignore @type {typeof __VLS_components.SystemUpdateAltOutlined} */
        SystemUpdateAltOutlined;
        // @ts-ignore
        const __VLS_73 = __VLS_asFunctionalComponent1(__VLS_72, new __VLS_72({}));
        const __VLS_74 = __VLS_73({}, ...__VLS_functionalComponentArgsRest(__VLS_73));
        // @ts-ignore
        [];
        var __VLS_69;
        // @ts-ignore
        [];
    }
    (__VLS_ctx.store.isRunning ? '请先停止服务' : '更新 llama.cpp');
    // @ts-ignore
    [store,];
    var __VLS_60;
    var __VLS_61;
}
if (!__VLS_ctx.llamaInstalled && !__VLS_ctx.store.llamaDownload) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-banner" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-info" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-info']} */ ;
    let __VLS_77;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_78 = __VLS_asFunctionalComponent1(__VLS_77, new __VLS_77({
        size: (20),
    }));
    const __VLS_79 = __VLS_78({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_78));
    const { default: __VLS_82 } = __VLS_80.slots;
    let __VLS_83;
    /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
    DownloadOutlined;
    // @ts-ignore
    const __VLS_84 = __VLS_asFunctionalComponent1(__VLS_83, new __VLS_83({}));
    const __VLS_85 = __VLS_84({}, ...__VLS_functionalComponentArgsRest(__VLS_84));
    // @ts-ignore
    [store, llamaInstalled,];
    var __VLS_80;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-title" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-desc" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-desc']} */ ;
    let __VLS_88;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_89 = __VLS_asFunctionalComponent1(__VLS_88, new __VLS_88({
        ...{ 'onClick': {} },
        type: "primary",
    }));
    const __VLS_90 = __VLS_89({
        ...{ 'onClick': {} },
        type: "primary",
    }, ...__VLS_functionalComponentArgsRest(__VLS_89));
    let __VLS_93;
    const __VLS_94 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(!__VLS_ctx.llamaInstalled && !__VLS_ctx.store.llamaDownload))
                    return;
                __VLS_ctx.store.downloadLlama();
                // @ts-ignore
                [store,];
            } });
    const { default: __VLS_95 } = __VLS_91.slots;
    {
        const { icon: __VLS_96 } = __VLS_91.slots;
        let __VLS_97;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_98 = __VLS_asFunctionalComponent1(__VLS_97, new __VLS_97({}));
        const __VLS_99 = __VLS_98({}, ...__VLS_functionalComponentArgsRest(__VLS_98));
        const { default: __VLS_102 } = __VLS_100.slots;
        let __VLS_103;
        /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
        DownloadOutlined;
        // @ts-ignore
        const __VLS_104 = __VLS_asFunctionalComponent1(__VLS_103, new __VLS_103({}));
        const __VLS_105 = __VLS_104({}, ...__VLS_functionalComponentArgsRest(__VLS_104));
        // @ts-ignore
        [];
        var __VLS_100;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_91;
    var __VLS_92;
}
if (__VLS_ctx.store.llamaDownload && !__VLS_ctx.store.llamaDownload.done && !__VLS_ctx.store.llamaDownload.error) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-banner downloading" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
    /** @type {__VLS_StyleScopedClasses['downloading']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-progress-info" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-progress-info']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "llama-download-stats" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-stats']} */ ;
    (__VLS_ctx.formatBytes(__VLS_ctx.store.llamaDownload.bytesDownloaded));
    if (__VLS_ctx.store.llamaDownload.totalBytes > 0) {
        (__VLS_ctx.formatBytes(__VLS_ctx.store.llamaDownload.totalBytes));
    }
    if (__VLS_ctx.store.llamaDownload.speedBytesPerSec > 0) {
        (__VLS_ctx.formatSpeed(__VLS_ctx.store.llamaDownload.speedBytesPerSec));
    }
    let __VLS_108;
    /** @ts-ignore @type {typeof __VLS_components.NProgress} */
    NProgress;
    // @ts-ignore
    const __VLS_109 = __VLS_asFunctionalComponent1(__VLS_108, new __VLS_108({
        type: "line",
        percentage: (__VLS_ctx.store.llamaDownload.totalBytes > 0
            ? Math.round(__VLS_ctx.store.llamaDownload.bytesDownloaded / __VLS_ctx.store.llamaDownload.totalBytes * 100)
            : 0),
        showIndicator: (false),
        ...{ style: {} },
    }));
    const __VLS_110 = __VLS_109({
        type: "line",
        percentage: (__VLS_ctx.store.llamaDownload.totalBytes > 0
            ? Math.round(__VLS_ctx.store.llamaDownload.bytesDownloaded / __VLS_ctx.store.llamaDownload.totalBytes * 100)
            : 0),
        showIndicator: (false),
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_109));
    let __VLS_113;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_114 = __VLS_asFunctionalComponent1(__VLS_113, new __VLS_113({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }));
    const __VLS_115 = __VLS_114({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_114));
    let __VLS_118;
    const __VLS_119 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.store.llamaDownload && !__VLS_ctx.store.llamaDownload.done && !__VLS_ctx.store.llamaDownload.error))
                    return;
                __VLS_ctx.store.cancelLlamaDownload();
                // @ts-ignore
                [store, store, store, store, store, store, store, store, store, store, store, store, formatBytes, formatBytes, formatSpeed,];
            } });
    const { default: __VLS_120 } = __VLS_116.slots;
    {
        const { icon: __VLS_121 } = __VLS_116.slots;
        let __VLS_122;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_123 = __VLS_asFunctionalComponent1(__VLS_122, new __VLS_122({}));
        const __VLS_124 = __VLS_123({}, ...__VLS_functionalComponentArgsRest(__VLS_123));
        const { default: __VLS_127 } = __VLS_125.slots;
        let __VLS_128;
        /** @ts-ignore @type {typeof __VLS_components.CloseOutlined} */
        CloseOutlined;
        // @ts-ignore
        const __VLS_129 = __VLS_asFunctionalComponent1(__VLS_128, new __VLS_128({}));
        const __VLS_130 = __VLS_129({}, ...__VLS_functionalComponentArgsRest(__VLS_129));
        // @ts-ignore
        [];
        var __VLS_125;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_116;
    var __VLS_117;
}
if (__VLS_ctx.store.llamaDownload?.error) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llama-download-banner error" },
    });
    /** @type {__VLS_StyleScopedClasses['llama-download-banner']} */ ;
    /** @type {__VLS_StyleScopedClasses['error']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.store.llamaDownload.error);
    let __VLS_133;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_134 = __VLS_asFunctionalComponent1(__VLS_133, new __VLS_133({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
    }));
    const __VLS_135 = __VLS_134({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
    }, ...__VLS_functionalComponentArgsRest(__VLS_134));
    let __VLS_138;
    const __VLS_139 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.store.llamaDownload?.error))
                    return;
                __VLS_ctx.store.retryLlamaDownload();
                // @ts-ignore
                [store, store, store,];
            } });
    const { default: __VLS_140 } = __VLS_136.slots;
    // @ts-ignore
    [];
    var __VLS_136;
    var __VLS_137;
}
if (__VLS_ctx.showErrorBanner) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "error-banner" },
    });
    /** @type {__VLS_StyleScopedClasses['error-banner']} */ ;
    (__VLS_ctx.store.status.error);
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-section" },
});
/** @type {__VLS_StyleScopedClasses['info-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "subsection-title" },
});
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
let __VLS_141;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_142 = __VLS_asFunctionalComponent1(__VLS_141, new __VLS_141({
    size: (16),
}));
const __VLS_143 = __VLS_142({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_142));
const { default: __VLS_146 } = __VLS_144.slots;
let __VLS_147;
/** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
StorageOutlined;
// @ts-ignore
const __VLS_148 = __VLS_asFunctionalComponent1(__VLS_147, new __VLS_147({}));
const __VLS_149 = __VLS_148({}, ...__VLS_functionalComponentArgsRest(__VLS_148));
// @ts-ignore
[store, showErrorBanner,];
var __VLS_144;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-grid" },
});
/** @type {__VLS_StyleScopedClasses['settings-grid']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_152;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_153 = __VLS_asFunctionalComponent1(__VLS_152, new __VLS_152({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.gpuLayers),
    min: (-1),
    max: (999),
    ...{ style: {} },
}));
const __VLS_154 = __VLS_153({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.gpuLayers),
    min: (-1),
    max: (999),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_153));
let __VLS_157;
const __VLS_158 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.handleSaveSettings) });
var __VLS_155;
var __VLS_156;
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
(__VLS_ctx.contextLength);
let __VLS_159;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_160 = __VLS_asFunctionalComponent1(__VLS_159, new __VLS_159({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.contextLength),
    min: (512),
    max: (32768),
    step: (512),
    tooltip: (true),
}));
const __VLS_161 = __VLS_160({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.contextLength),
    min: (512),
    max: (32768),
    step: (512),
    tooltip: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_160));
let __VLS_164;
const __VLS_165 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.handleSaveSettings) });
var __VLS_162;
var __VLS_163;
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
let __VLS_166;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_167 = __VLS_asFunctionalComponent1(__VLS_166, new __VLS_166({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.kvCacheType),
    options: ([
        { label: 'f16（最高精度）', value: 'f16' },
        { label: 'q8_0（推荐，省约 50% 显存）', value: 'q8_0' },
        { label: 'q4_0（省约 75% 显存）', value: 'q4_0' },
    ]),
    ...{ style: {} },
}));
const __VLS_168 = __VLS_167({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.kvCacheType),
    options: ([
        { label: 'f16（最高精度）', value: 'f16' },
        { label: 'q8_0（推荐，省约 50% 显存）', value: 'q8_0' },
        { label: 'q4_0（省约 75% 显存）', value: 'q4_0' },
    ]),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_167));
let __VLS_171;
const __VLS_172 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.handleSaveSettings) });
var __VLS_169;
var __VLS_170;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-section" },
});
/** @type {__VLS_StyleScopedClasses['info-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "subsection-title" },
});
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "settings-grid" },
});
/** @type {__VLS_StyleScopedClasses['settings-grid']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_173;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_174 = __VLS_asFunctionalComponent1(__VLS_173, new __VLS_173({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.port),
    min: (1024),
    max: (65535),
    ...{ style: {} },
}));
const __VLS_175 = __VLS_174({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.port),
    min: (1024),
    max: (65535),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_174));
let __VLS_178;
const __VLS_179 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ port: v ?? 51821 })) });
var __VLS_176;
var __VLS_177;
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
(__VLS_ctx.modelValue.temperature);
let __VLS_180;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_181 = __VLS_asFunctionalComponent1(__VLS_180, new __VLS_180({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.temperature),
    min: (0),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}));
const __VLS_182 = __VLS_181({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.temperature),
    min: (0),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}, ...__VLS_functionalComponentArgsRest(__VLS_181));
let __VLS_185;
const __VLS_186 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ temperature: v })) });
var __VLS_183;
var __VLS_184;
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
(__VLS_ctx.modelValue.localContextSize);
let __VLS_187;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_188 = __VLS_asFunctionalComponent1(__VLS_187, new __VLS_187({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localContextSize),
    min: (0),
    max: (10),
    step: (1),
    tooltip: (true),
}));
const __VLS_189 = __VLS_188({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localContextSize),
    min: (0),
    max: (10),
    step: (1),
    tooltip: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_188));
let __VLS_192;
const __VLS_193 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ localContextSize: v })) });
var __VLS_190;
var __VLS_191;
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
(__VLS_ctx.modelValue.localMinP?.toFixed(2) ?? '0.05');
let __VLS_194;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_195 = __VLS_asFunctionalComponent1(__VLS_194, new __VLS_194({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localMinP ?? 0.05),
    min: (0),
    max: (1),
    step: (0.01),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(2)),
}));
const __VLS_196 = __VLS_195({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localMinP ?? 0.05),
    min: (0),
    max: (1),
    step: (0.01),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(2)),
}, ...__VLS_functionalComponentArgsRest(__VLS_195));
let __VLS_199;
const __VLS_200 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ localMinP: v })) });
var __VLS_197;
var __VLS_198;
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
(__VLS_ctx.modelValue.localRepeatPenalty?.toFixed(1) ?? '1.0');
let __VLS_201;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_202 = __VLS_asFunctionalComponent1(__VLS_201, new __VLS_201({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localRepeatPenalty ?? 1.0),
    min: (0.5),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}));
const __VLS_203 = __VLS_202({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.localRepeatPenalty ?? 1.0),
    min: (0.5),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}, ...__VLS_functionalComponentArgsRest(__VLS_202));
let __VLS_206;
const __VLS_207 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ localRepeatPenalty: v })) });
var __VLS_204;
var __VLS_205;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "prompt-wrapper" },
});
/** @type {__VLS_StyleScopedClasses['prompt-wrapper']} */ ;
let __VLS_208;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_209 = __VLS_asFunctionalComponent1(__VLS_208, new __VLS_208({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.systemPrompt),
    type: "textarea",
    rows: (3),
    placeholder: "指导 LLM 如何翻译",
}));
const __VLS_210 = __VLS_209({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.systemPrompt),
    type: "textarea",
    rows: (3),
    placeholder: "指导 LLM 如何翻译",
}, ...__VLS_functionalComponentArgsRest(__VLS_209));
let __VLS_213;
const __VLS_214 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.updateAiSettings({ systemPrompt: v })) });
var __VLS_211;
var __VLS_212;
let __VLS_215;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_216 = __VLS_asFunctionalComponent1(__VLS_215, new __VLS_215({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    ...{ class: "restore-btn" },
}));
const __VLS_217 = __VLS_216({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    ...{ class: "restore-btn" },
}, ...__VLS_functionalComponentArgsRest(__VLS_216));
let __VLS_220;
const __VLS_221 = ({ click: {} },
    { onClick: (__VLS_ctx.handleRestorePrompt) });
/** @type {__VLS_StyleScopedClasses['restore-btn']} */ ;
const { default: __VLS_222 } = __VLS_218.slots;
{
    const { icon: __VLS_223 } = __VLS_218.slots;
    let __VLS_224;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_225 = __VLS_asFunctionalComponent1(__VLS_224, new __VLS_224({
        size: (14),
    }));
    const __VLS_226 = __VLS_225({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_225));
    const { default: __VLS_229 } = __VLS_227.slots;
    let __VLS_230;
    /** @ts-ignore @type {typeof __VLS_components.RestoreOutlined} */
    RestoreOutlined;
    // @ts-ignore
    const __VLS_231 = __VLS_asFunctionalComponent1(__VLS_230, new __VLS_230({}));
    const __VLS_232 = __VLS_231({}, ...__VLS_functionalComponentArgsRest(__VLS_231));
    // @ts-ignore
    [gpuLayers, handleSaveSettings, handleSaveSettings, handleSaveSettings, contextLength, contextLength, kvCacheType, modelValue, modelValue, modelValue, modelValue, modelValue, modelValue, modelValue, modelValue, modelValue, modelValue, updateAiSettings, updateAiSettings, updateAiSettings, updateAiSettings, updateAiSettings, updateAiSettings, handleRestorePrompt,];
    var __VLS_227;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_218;
var __VLS_219;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-section" },
});
/** @type {__VLS_StyleScopedClasses['info-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-header" },
});
/** @type {__VLS_StyleScopedClasses['info-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "subsection-title" },
});
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
let __VLS_235;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_236 = __VLS_asFunctionalComponent1(__VLS_235, new __VLS_235({
    ...{ 'onClick': {} },
    size: "small",
}));
const __VLS_237 = __VLS_236({
    ...{ 'onClick': {} },
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_236));
let __VLS_240;
const __VLS_241 = ({ click: {} },
    { onClick: (__VLS_ctx.handleAddModel) });
const { default: __VLS_242 } = __VLS_238.slots;
{
    const { icon: __VLS_243 } = __VLS_238.slots;
    let __VLS_244;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_245 = __VLS_asFunctionalComponent1(__VLS_244, new __VLS_244({}));
    const __VLS_246 = __VLS_245({}, ...__VLS_functionalComponentArgsRest(__VLS_245));
    const { default: __VLS_249 } = __VLS_247.slots;
    let __VLS_250;
    /** @ts-ignore @type {typeof __VLS_components.FolderOpenOutlined} */
    FolderOpenOutlined;
    // @ts-ignore
    const __VLS_251 = __VLS_asFunctionalComponent1(__VLS_250, new __VLS_250({}));
    const __VLS_252 = __VLS_251({}, ...__VLS_functionalComponentArgsRest(__VLS_251));
    // @ts-ignore
    [handleAddModel,];
    var __VLS_247;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_238;
var __VLS_239;
if (__VLS_ctx.allModels.length === 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "empty-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-hint']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "model-list" },
    });
    /** @type {__VLS_StyleScopedClasses['model-list']} */ ;
    for (const [model] of __VLS_vFor((__VLS_ctx.allModels))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (model.id),
            ...{ class: "model-item" },
        });
        /** @type {__VLS_StyleScopedClasses['model-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "model-info" },
        });
        /** @type {__VLS_StyleScopedClasses['model-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "model-name" },
        });
        /** @type {__VLS_StyleScopedClasses['model-name']} */ ;
        (model.name);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "model-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['model-meta']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.formatBytes(model.fileSizeBytes));
        if (model.isBuiltIn) {
            let __VLS_255;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_256 = __VLS_asFunctionalComponent1(__VLS_255, new __VLS_255({
                size: "small",
            }));
            const __VLS_257 = __VLS_256({
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_256));
            const { default: __VLS_260 } = __VLS_258.slots;
            // @ts-ignore
            [formatBytes, allModels, allModels,];
            var __VLS_258;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "model-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['model-actions']} */ ;
        if (!__VLS_ctx.store.isRunning || __VLS_ctx.store.status?.loadedModelPath !== model.filePath) {
            let __VLS_261;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_262 = __VLS_asFunctionalComponent1(__VLS_261, new __VLS_261({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                ghost: true,
                disabled: (__VLS_ctx.store.isBusy || !__VLS_ctx.llamaInstalled),
            }));
            const __VLS_263 = __VLS_262({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                ghost: true,
                disabled: (__VLS_ctx.store.isBusy || !__VLS_ctx.llamaInstalled),
            }, ...__VLS_functionalComponentArgsRest(__VLS_262));
            let __VLS_266;
            const __VLS_267 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.allModels.length === 0))
                            return;
                        if (!(!__VLS_ctx.store.isRunning || __VLS_ctx.store.status?.loadedModelPath !== model.filePath))
                            return;
                        __VLS_ctx.handleStart(model);
                        // @ts-ignore
                        [store, store, store, llamaInstalled, handleStart,];
                    } });
            const { default: __VLS_268 } = __VLS_264.slots;
            {
                const { icon: __VLS_269 } = __VLS_264.slots;
                let __VLS_270;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_271 = __VLS_asFunctionalComponent1(__VLS_270, new __VLS_270({}));
                const __VLS_272 = __VLS_271({}, ...__VLS_functionalComponentArgsRest(__VLS_271));
                const { default: __VLS_275 } = __VLS_273.slots;
                let __VLS_276;
                /** @ts-ignore @type {typeof __VLS_components.PlayArrowOutlined} */
                PlayArrowOutlined;
                // @ts-ignore
                const __VLS_277 = __VLS_asFunctionalComponent1(__VLS_276, new __VLS_276({}));
                const __VLS_278 = __VLS_277({}, ...__VLS_functionalComponentArgsRest(__VLS_277));
                // @ts-ignore
                [];
                var __VLS_273;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_264;
            var __VLS_265;
        }
        else {
            let __VLS_281;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_282 = __VLS_asFunctionalComponent1(__VLS_281, new __VLS_281({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.store.status?.state === 'Stopping'),
            }));
            const __VLS_283 = __VLS_282({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.store.status?.state === 'Stopping'),
            }, ...__VLS_functionalComponentArgsRest(__VLS_282));
            let __VLS_286;
            const __VLS_287 = ({ click: {} },
                { onClick: (__VLS_ctx.handleStop) });
            const { default: __VLS_288 } = __VLS_284.slots;
            {
                const { icon: __VLS_289 } = __VLS_284.slots;
                let __VLS_290;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_291 = __VLS_asFunctionalComponent1(__VLS_290, new __VLS_290({}));
                const __VLS_292 = __VLS_291({}, ...__VLS_functionalComponentArgsRest(__VLS_291));
                const { default: __VLS_295 } = __VLS_293.slots;
                let __VLS_296;
                /** @ts-ignore @type {typeof __VLS_components.StopOutlined} */
                StopOutlined;
                // @ts-ignore
                const __VLS_297 = __VLS_asFunctionalComponent1(__VLS_296, new __VLS_296({}));
                const __VLS_298 = __VLS_297({}, ...__VLS_functionalComponentArgsRest(__VLS_297));
                // @ts-ignore
                [store, handleStop,];
                var __VLS_293;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_284;
            var __VLS_285;
        }
        let __VLS_301;
        /** @ts-ignore @type {typeof __VLS_components.NPopconfirm | typeof __VLS_components.NPopconfirm} */
        NPopconfirm;
        // @ts-ignore
        const __VLS_302 = __VLS_asFunctionalComponent1(__VLS_301, new __VLS_301({
            ...{ 'onPositiveClick': {} },
        }));
        const __VLS_303 = __VLS_302({
            ...{ 'onPositiveClick': {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_302));
        let __VLS_306;
        const __VLS_307 = ({ positiveClick: {} },
            { onPositiveClick: (...[$event]) => {
                    if (!!(__VLS_ctx.allModels.length === 0))
                        return;
                    __VLS_ctx.handleRemoveModel(model);
                    // @ts-ignore
                    [handleRemoveModel,];
                } });
        const { default: __VLS_308 } = __VLS_304.slots;
        {
            const { trigger: __VLS_309 } = __VLS_304.slots;
            let __VLS_310;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_311 = __VLS_asFunctionalComponent1(__VLS_310, new __VLS_310({
                size: "small",
                quaternary: true,
                type: "error",
            }));
            const __VLS_312 = __VLS_311({
                size: "small",
                quaternary: true,
                type: "error",
            }, ...__VLS_functionalComponentArgsRest(__VLS_311));
            const { default: __VLS_315 } = __VLS_313.slots;
            {
                const { icon: __VLS_316 } = __VLS_313.slots;
                let __VLS_317;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_318 = __VLS_asFunctionalComponent1(__VLS_317, new __VLS_317({}));
                const __VLS_319 = __VLS_318({}, ...__VLS_functionalComponentArgsRest(__VLS_318));
                const { default: __VLS_322 } = __VLS_320.slots;
                let __VLS_323;
                /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
                DeleteOutlined;
                // @ts-ignore
                const __VLS_324 = __VLS_asFunctionalComponent1(__VLS_323, new __VLS_323({}));
                const __VLS_325 = __VLS_324({}, ...__VLS_functionalComponentArgsRest(__VLS_324));
                // @ts-ignore
                [];
                var __VLS_320;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_313;
            // @ts-ignore
            [];
        }
        (model.isBuiltIn ? '（模型文件不会被删除）' : '');
        // @ts-ignore
        [];
        var __VLS_304;
        var __VLS_305;
        // @ts-ignore
        [];
    }
}
if (__VLS_ctx.hasDownloadTasks) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-section" },
    });
    /** @type {__VLS_StyleScopedClasses['info-section']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
        ...{ class: "subsection-title" },
    });
    /** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
    let __VLS_328;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_329 = __VLS_asFunctionalComponent1(__VLS_328, new __VLS_328({
        size: (16),
    }));
    const __VLS_330 = __VLS_329({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_329));
    const { default: __VLS_333 } = __VLS_331.slots;
    let __VLS_334;
    /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
    DownloadOutlined;
    // @ts-ignore
    const __VLS_335 = __VLS_asFunctionalComponent1(__VLS_334, new __VLS_334({}));
    const __VLS_336 = __VLS_335({}, ...__VLS_functionalComponentArgsRest(__VLS_335));
    // @ts-ignore
    [hasDownloadTasks,];
    var __VLS_331;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "download-task-list" },
    });
    /** @type {__VLS_StyleScopedClasses['download-task-list']} */ ;
    for (const [[downloadId, progress]] of __VLS_vFor((__VLS_ctx.store.downloads))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (downloadId),
            ...{ class: "download-task-item" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-info" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-name" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-name']} */ ;
        (__VLS_ctx.getDownloadName(downloadId));
        let __VLS_339;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_340 = __VLS_asFunctionalComponent1(__VLS_339, new __VLS_339({
            size: "small",
            bordered: (false),
            type: (progress.useModelScope ? 'success' : progress.useMirror ? 'info' : 'default'),
        }));
        const __VLS_341 = __VLS_340({
            size: "small",
            bordered: (false),
            type: (progress.useModelScope ? 'success' : progress.useMirror ? 'info' : 'default'),
        }, ...__VLS_functionalComponentArgsRest(__VLS_340));
        const { default: __VLS_344 } = __VLS_342.slots;
        (progress.useModelScope ? 'ModelScope' : progress.useMirror ? '镜像' : '官方');
        // @ts-ignore
        [store, getDownloadName,];
        var __VLS_342;
        let __VLS_345;
        /** @ts-ignore @type {typeof __VLS_components.NProgress} */
        NProgress;
        // @ts-ignore
        const __VLS_346 = __VLS_asFunctionalComponent1(__VLS_345, new __VLS_345({
            type: "line",
            percentage: (progress.totalBytes > 0 ? Math.round((progress.bytesDownloaded / progress.totalBytes) * 100) : 0),
            showIndicator: (false),
            height: (6),
        }));
        const __VLS_347 = __VLS_346({
            type: "line",
            percentage: (progress.totalBytes > 0 ? Math.round((progress.bytesDownloaded / progress.totalBytes) * 100) : 0),
            showIndicator: (false),
            height: (6),
        }, ...__VLS_functionalComponentArgsRest(__VLS_346));
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['download-meta']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.formatBytes(progress.bytesDownloaded));
        if (progress.totalBytes > 0) {
            (__VLS_ctx.formatBytes(progress.totalBytes));
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.formatSpeed(progress.speedBytesPerSec));
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-actions']} */ ;
        let __VLS_350;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_351 = __VLS_asFunctionalComponent1(__VLS_350, new __VLS_350({
            ...{ 'onClick': {} },
            size: "small",
            quaternary: true,
        }));
        const __VLS_352 = __VLS_351({
            ...{ 'onClick': {} },
            size: "small",
            quaternary: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_351));
        let __VLS_355;
        const __VLS_356 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.hasDownloadTasks))
                        return;
                    __VLS_ctx.handlePauseDownload(downloadId);
                    // @ts-ignore
                    [formatBytes, formatBytes, formatSpeed, handlePauseDownload,];
                } });
        const { default: __VLS_357 } = __VLS_353.slots;
        {
            const { icon: __VLS_358 } = __VLS_353.slots;
            let __VLS_359;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_360 = __VLS_asFunctionalComponent1(__VLS_359, new __VLS_359({}));
            const __VLS_361 = __VLS_360({}, ...__VLS_functionalComponentArgsRest(__VLS_360));
            const { default: __VLS_364 } = __VLS_362.slots;
            let __VLS_365;
            /** @ts-ignore @type {typeof __VLS_components.PauseOutlined} */
            PauseOutlined;
            // @ts-ignore
            const __VLS_366 = __VLS_asFunctionalComponent1(__VLS_365, new __VLS_365({}));
            const __VLS_367 = __VLS_366({}, ...__VLS_functionalComponentArgsRest(__VLS_366));
            // @ts-ignore
            [];
            var __VLS_362;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_353;
        var __VLS_354;
        let __VLS_370;
        /** @ts-ignore @type {typeof __VLS_components.NPopconfirm | typeof __VLS_components.NPopconfirm} */
        NPopconfirm;
        // @ts-ignore
        const __VLS_371 = __VLS_asFunctionalComponent1(__VLS_370, new __VLS_370({
            ...{ 'onPositiveClick': {} },
        }));
        const __VLS_372 = __VLS_371({
            ...{ 'onPositiveClick': {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_371));
        let __VLS_375;
        const __VLS_376 = ({ positiveClick: {} },
            { onPositiveClick: (...[$event]) => {
                    if (!(__VLS_ctx.hasDownloadTasks))
                        return;
                    __VLS_ctx.handleCancelDownload(downloadId);
                    // @ts-ignore
                    [handleCancelDownload,];
                } });
        const { default: __VLS_377 } = __VLS_373.slots;
        {
            const { trigger: __VLS_378 } = __VLS_373.slots;
            let __VLS_379;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_380 = __VLS_asFunctionalComponent1(__VLS_379, new __VLS_379({
                size: "small",
                quaternary: true,
                type: "error",
            }));
            const __VLS_381 = __VLS_380({
                size: "small",
                quaternary: true,
                type: "error",
            }, ...__VLS_functionalComponentArgsRest(__VLS_380));
            const { default: __VLS_384 } = __VLS_382.slots;
            {
                const { icon: __VLS_385 } = __VLS_382.slots;
                let __VLS_386;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_387 = __VLS_asFunctionalComponent1(__VLS_386, new __VLS_386({}));
                const __VLS_388 = __VLS_387({}, ...__VLS_functionalComponentArgsRest(__VLS_387));
                const { default: __VLS_391 } = __VLS_389.slots;
                let __VLS_392;
                /** @ts-ignore @type {typeof __VLS_components.CloseOutlined} */
                CloseOutlined;
                // @ts-ignore
                const __VLS_393 = __VLS_asFunctionalComponent1(__VLS_392, new __VLS_392({}));
                const __VLS_394 = __VLS_393({}, ...__VLS_functionalComponentArgsRest(__VLS_393));
                // @ts-ignore
                [];
                var __VLS_389;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_382;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_373;
        var __VLS_374;
        // @ts-ignore
        [];
    }
    for (const [paused] of __VLS_vFor((__VLS_ctx.pausedDownloads))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (paused.catalogId),
            ...{ class: "download-task-item paused" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['paused']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-info" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-name" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-name']} */ ;
        (__VLS_ctx.getDownloadName(paused.catalogId));
        let __VLS_397;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_398 = __VLS_asFunctionalComponent1(__VLS_397, new __VLS_397({
            size: "small",
            type: "warning",
            bordered: (false),
        }));
        const __VLS_399 = __VLS_398({
            size: "small",
            type: "warning",
            bordered: (false),
        }, ...__VLS_functionalComponentArgsRest(__VLS_398));
        const { default: __VLS_402 } = __VLS_400.slots;
        // @ts-ignore
        [getDownloadName, pausedDownloads,];
        var __VLS_400;
        let __VLS_403;
        /** @ts-ignore @type {typeof __VLS_components.NProgress} */
        NProgress;
        // @ts-ignore
        const __VLS_404 = __VLS_asFunctionalComponent1(__VLS_403, new __VLS_403({
            type: "line",
            status: "warning",
            percentage: (Math.round((paused.bytesDownloaded / Math.max(paused.totalBytes, 1)) * 100)),
            showIndicator: (false),
            height: (6),
        }));
        const __VLS_405 = __VLS_404({
            type: "line",
            status: "warning",
            percentage: (Math.round((paused.bytesDownloaded / Math.max(paused.totalBytes, 1)) * 100)),
            showIndicator: (false),
            height: (6),
        }, ...__VLS_functionalComponentArgsRest(__VLS_404));
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['download-meta']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.formatBytes(paused.bytesDownloaded));
        (__VLS_ctx.formatBytes(paused.totalBytes));
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "download-task-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['download-task-actions']} */ ;
        let __VLS_408;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_409 = __VLS_asFunctionalComponent1(__VLS_408, new __VLS_408({
            ...{ 'onClick': {} },
            size: "small",
            type: "primary",
            ghost: true,
        }));
        const __VLS_410 = __VLS_409({
            ...{ 'onClick': {} },
            size: "small",
            type: "primary",
            ghost: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_409));
        let __VLS_413;
        const __VLS_414 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.hasDownloadTasks))
                        return;
                    __VLS_ctx.handleResumeDownload(paused.catalogId);
                    // @ts-ignore
                    [formatBytes, formatBytes, handleResumeDownload,];
                } });
        const { default: __VLS_415 } = __VLS_411.slots;
        {
            const { icon: __VLS_416 } = __VLS_411.slots;
            let __VLS_417;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_418 = __VLS_asFunctionalComponent1(__VLS_417, new __VLS_417({}));
            const __VLS_419 = __VLS_418({}, ...__VLS_functionalComponentArgsRest(__VLS_418));
            const { default: __VLS_422 } = __VLS_420.slots;
            let __VLS_423;
            /** @ts-ignore @type {typeof __VLS_components.PlayArrowOutlined} */
            PlayArrowOutlined;
            // @ts-ignore
            const __VLS_424 = __VLS_asFunctionalComponent1(__VLS_423, new __VLS_423({}));
            const __VLS_425 = __VLS_424({}, ...__VLS_functionalComponentArgsRest(__VLS_424));
            // @ts-ignore
            [];
            var __VLS_420;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_411;
        var __VLS_412;
        let __VLS_428;
        /** @ts-ignore @type {typeof __VLS_components.NPopconfirm | typeof __VLS_components.NPopconfirm} */
        NPopconfirm;
        // @ts-ignore
        const __VLS_429 = __VLS_asFunctionalComponent1(__VLS_428, new __VLS_428({
            ...{ 'onPositiveClick': {} },
        }));
        const __VLS_430 = __VLS_429({
            ...{ 'onPositiveClick': {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_429));
        let __VLS_433;
        const __VLS_434 = ({ positiveClick: {} },
            { onPositiveClick: (...[$event]) => {
                    if (!(__VLS_ctx.hasDownloadTasks))
                        return;
                    __VLS_ctx.handleCancelDownload(paused.catalogId);
                    // @ts-ignore
                    [handleCancelDownload,];
                } });
        const { default: __VLS_435 } = __VLS_431.slots;
        {
            const { trigger: __VLS_436 } = __VLS_431.slots;
            let __VLS_437;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_438 = __VLS_asFunctionalComponent1(__VLS_437, new __VLS_437({
                size: "small",
                quaternary: true,
                type: "error",
            }));
            const __VLS_439 = __VLS_438({
                size: "small",
                quaternary: true,
                type: "error",
            }, ...__VLS_functionalComponentArgsRest(__VLS_438));
            const { default: __VLS_442 } = __VLS_440.slots;
            {
                const { icon: __VLS_443 } = __VLS_440.slots;
                let __VLS_444;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_445 = __VLS_asFunctionalComponent1(__VLS_444, new __VLS_444({}));
                const __VLS_446 = __VLS_445({}, ...__VLS_functionalComponentArgsRest(__VLS_445));
                const { default: __VLS_449 } = __VLS_447.slots;
                let __VLS_450;
                /** @ts-ignore @type {typeof __VLS_components.CloseOutlined} */
                CloseOutlined;
                // @ts-ignore
                const __VLS_451 = __VLS_asFunctionalComponent1(__VLS_450, new __VLS_450({}));
                const __VLS_452 = __VLS_451({}, ...__VLS_functionalComponentArgsRest(__VLS_451));
                // @ts-ignore
                [];
                var __VLS_447;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_440;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_431;
        var __VLS_432;
        // @ts-ignore
        [];
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-section" },
});
/** @type {__VLS_StyleScopedClasses['info-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.catalogExpanded = !__VLS_ctx.catalogExpanded;
            // @ts-ignore
            [catalogExpanded, catalogExpanded,];
        } },
    ...{ class: "info-header clickable" },
});
/** @type {__VLS_StyleScopedClasses['info-header']} */ ;
/** @type {__VLS_StyleScopedClasses['clickable']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "subsection-title" },
});
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
let __VLS_455;
/** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
NTag;
// @ts-ignore
const __VLS_456 = __VLS_asFunctionalComponent1(__VLS_455, new __VLS_455({
    size: "small",
    bordered: (false),
}));
const __VLS_457 = __VLS_456({
    size: "small",
    bordered: (false),
}, ...__VLS_functionalComponentArgsRest(__VLS_456));
const { default: __VLS_460 } = __VLS_458.slots;
(__VLS_ctx.store.catalog.length);
// @ts-ignore
[store,];
var __VLS_458;
let __VLS_461;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_462 = __VLS_asFunctionalComponent1(__VLS_461, new __VLS_461({
    size: (18),
    ...{ class: "collapse-icon" },
    ...{ class: ({ expanded: __VLS_ctx.catalogExpanded }) },
}));
const __VLS_463 = __VLS_462({
    size: (18),
    ...{ class: "collapse-icon" },
    ...{ class: ({ expanded: __VLS_ctx.catalogExpanded }) },
}, ...__VLS_functionalComponentArgsRest(__VLS_462));
/** @type {__VLS_StyleScopedClasses['collapse-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['expanded']} */ ;
const { default: __VLS_466 } = __VLS_464.slots;
let __VLS_467;
/** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
ExpandMoreOutlined;
// @ts-ignore
const __VLS_468 = __VLS_asFunctionalComponent1(__VLS_467, new __VLS_467({}));
const __VLS_469 = __VLS_468({}, ...__VLS_functionalComponentArgsRest(__VLS_468));
// @ts-ignore
[catalogExpanded,];
var __VLS_464;
if (__VLS_ctx.catalogExpanded) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "section-desc" },
    });
    /** @type {__VLS_StyleScopedClasses['section-desc']} */ ;
    if (__VLS_ctx.maxVramGb > 0) {
        (__VLS_ctx.maxVramGb.toFixed(0));
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "catalog-list" },
    });
    /** @type {__VLS_StyleScopedClasses['catalog-list']} */ ;
    for (const [item] of __VLS_vFor((__VLS_ctx.sortedCatalog))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (item.id),
            ...{ class: "catalog-item" },
            ...{ class: ({ recommended: __VLS_ctx.isModelRecommended(item) }) },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['recommended']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "catalog-info" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "catalog-name" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-name']} */ ;
        (item.name);
        if (__VLS_ctx.isModelRecommended(item)) {
            let __VLS_472;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_473 = __VLS_asFunctionalComponent1(__VLS_472, new __VLS_472({
                size: "small",
                type: "success",
                bordered: (false),
            }));
            const __VLS_474 = __VLS_473({
                size: "small",
                type: "success",
                bordered: (false),
            }, ...__VLS_functionalComponentArgsRest(__VLS_473));
            const { default: __VLS_477 } = __VLS_475.slots;
            // @ts-ignore
            [catalogExpanded, maxVramGb, maxVramGb, sortedCatalog, isModelRecommended, isModelRecommended,];
            var __VLS_475;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "catalog-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-desc']} */ ;
        (item.description);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "catalog-tags" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-tags']} */ ;
        for (const [tag] of __VLS_vFor((item.tags))) {
            let __VLS_478;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_479 = __VLS_asFunctionalComponent1(__VLS_478, new __VLS_478({
                key: (tag),
                size: "small",
            }));
            const __VLS_480 = __VLS_479({
                key: (tag),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_479));
            const { default: __VLS_483 } = __VLS_481.slots;
            (tag);
            // @ts-ignore
            [];
            var __VLS_481;
            // @ts-ignore
            [];
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "catalog-size" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-size']} */ ;
        (__VLS_ctx.formatBytes(item.fileSizeBytes));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "catalog-vram" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-vram']} */ ;
        (item.recommendedVramGb);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "catalog-action" },
        });
        /** @type {__VLS_StyleScopedClasses['catalog-action']} */ ;
        if (__VLS_ctx.getDownloadProgress(item.id)) {
            let __VLS_484;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_485 = __VLS_asFunctionalComponent1(__VLS_484, new __VLS_484({
                size: "small",
                type: "info",
            }));
            const __VLS_486 = __VLS_485({
                size: "small",
                type: "info",
            }, ...__VLS_functionalComponentArgsRest(__VLS_485));
            const { default: __VLS_489 } = __VLS_487.slots;
            // @ts-ignore
            [formatBytes, getDownloadProgress,];
            var __VLS_487;
        }
        else if (__VLS_ctx.isModelDownloaded(item.id)) {
            let __VLS_490;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_491 = __VLS_asFunctionalComponent1(__VLS_490, new __VLS_490({
                type: "success",
                size: "small",
            }));
            const __VLS_492 = __VLS_491({
                type: "success",
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_491));
            const { default: __VLS_495 } = __VLS_493.slots;
            // @ts-ignore
            [isModelDownloaded,];
            var __VLS_493;
        }
        else if (__VLS_ctx.isModelPaused(item.id)) {
            let __VLS_496;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_497 = __VLS_asFunctionalComponent1(__VLS_496, new __VLS_496({
                type: "warning",
                size: "small",
            }));
            const __VLS_498 = __VLS_497({
                type: "warning",
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_497));
            const { default: __VLS_501 } = __VLS_499.slots;
            // @ts-ignore
            [isModelPaused,];
            var __VLS_499;
        }
        else {
            let __VLS_502;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_503 = __VLS_asFunctionalComponent1(__VLS_502, new __VLS_502({
                ...{ 'onClick': {} },
                size: "small",
            }));
            const __VLS_504 = __VLS_503({
                ...{ 'onClick': {} },
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_503));
            let __VLS_507;
            const __VLS_508 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!(__VLS_ctx.catalogExpanded))
                            return;
                        if (!!(__VLS_ctx.getDownloadProgress(item.id)))
                            return;
                        if (!!(__VLS_ctx.isModelDownloaded(item.id)))
                            return;
                        if (!!(__VLS_ctx.isModelPaused(item.id)))
                            return;
                        __VLS_ctx.handleDownload(item);
                        // @ts-ignore
                        [handleDownload,];
                    } });
            const { default: __VLS_509 } = __VLS_505.slots;
            {
                const { icon: __VLS_510 } = __VLS_505.slots;
                let __VLS_511;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_512 = __VLS_asFunctionalComponent1(__VLS_511, new __VLS_511({}));
                const __VLS_513 = __VLS_512({}, ...__VLS_functionalComponentArgsRest(__VLS_512));
                const { default: __VLS_516 } = __VLS_514.slots;
                let __VLS_517;
                /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
                DownloadOutlined;
                // @ts-ignore
                const __VLS_518 = __VLS_asFunctionalComponent1(__VLS_517, new __VLS_517({}));
                const __VLS_519 = __VLS_518({}, ...__VLS_functionalComponentArgsRest(__VLS_518));
                // @ts-ignore
                [];
                var __VLS_514;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_505;
            var __VLS_506;
        }
        // @ts-ignore
        [];
    }
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __typeProps: {},
});
export default {};
