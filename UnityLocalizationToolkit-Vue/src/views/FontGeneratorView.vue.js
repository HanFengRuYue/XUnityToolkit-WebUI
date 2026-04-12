import { ref, reactive, computed, watch, onMounted, onActivated, onBeforeUnmount, onDeactivated } from 'vue';
import { NButton, NIcon, NSelect, NProgress, NSpace, NAlert, useMessage, NPopconfirm, NCheckboxGroup, NCheckbox, NRadioGroup, NRadio, NCollapse, NCollapseItem, NSpin, NTag, NDescriptions, NDescriptionsItem, NInputNumber, NSwitch, } from 'naive-ui';
import { FontDownloadOutlined, UploadFileOutlined, DownloadOutlined, DeleteOutlined, TextFieldsOutlined, AssessmentOutlined, ExpandMoreOutlined, } from '@vicons/material';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { api } from '@/api/client';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { formatBytes } from '@/utils/format';
defineOptions({ name: 'FontGeneratorView' });
const isMobile = ref(false);
function checkMobile() {
    isMobile.value = window.innerWidth <= 768;
}
const collapsed = reactive({
    charset: true,
});
const message = useMessage();
const { selectFile } = useFileExplorer();
// Upload state
const uploadedFont = ref(null);
const uploading = ref(false);
// Settings
const unityVersion = ref('2022');
const samplingSize = ref(64);
const atlasSize = ref(8192);
const renderMode = ref('SDFAA');
const samplingSizeMode = ref('manual');
const paddingMode = ref('percentage');
const paddingValue = ref(10);
// Generation state
const isGenerating = ref(false);
const phase = ref('');
const current = ref(0);
const total = ref(0);
const progressPercent = computed(() => total.value > 0 ? Math.round(current.value / total.value * 100) : 0);
// History
const generatedFonts = ref([]);
const games = ref([]);
// Character set configuration
const useAllFontCharacters = ref(false);
const builtinCharsets = ref([]);
const selectedCharsets = ref(['GB2312']);
const customCharsetFile = ref(null);
const uploadingCharset = ref(false);
const translationMode = ref('none');
const translationGameId = ref(null);
const translationFile = ref(null);
const uploadingTranslation = ref(false);
// Charset preview
const charsetPreview = ref(null);
const loadingPreview = ref(false);
let previewDebounceTimer = null;
// Generation report
const generationReport = ref(null);
const viewingReport = ref(null);
const activeReport = computed(() => viewingReport.value ?? generationReport.value);
const phaseLabels = {
    parsing: '解析字体',
    sdf: '生成 SDF 位图',
    packing: '打包 Atlas',
    compositing: '合成纹理',
    serializing: '序列化 Bundle',
};
const versionOptions = [
    { label: 'Unity 2018', value: '2018' },
    { label: 'Unity 2019', value: '2019' },
    { label: 'Unity 2020', value: '2020' },
    { label: 'Unity 2021', value: '2021' },
    { label: 'Unity 2022', value: '2022' },
    { label: 'Unity 6000', value: '6000' },
];
const samplingOptions = [
    { label: '32px', value: 32 },
    { label: '48px', value: 48 },
    { label: '64px (推荐)', value: 64 },
    { label: '96px', value: 96 },
];
const atlasOptions = [
    { label: '2048 x 2048', value: 2048 },
    { label: '4096 x 4096', value: 4096 },
    { label: '8192 x 8192 (推荐)', value: 8192 },
];
const renderModeOptions = [
    { label: 'SDFAA (默认)', value: 'SDFAA' },
    { label: 'SDF8 (8x 上采样)', value: 'SDF8' },
    { label: 'SDF16 (16x 上采样)', value: 'SDF16' },
    { label: 'SDF32 (32x 上采样，最高质量)', value: 'SDF32' },
];
// Upload handler (drag-and-drop fallback)
async function handleUpload({ file }) {
    uploading.value = true;
    const formData = new FormData();
    formData.append('file', file.file);
    try {
        const resp = await fetch('/api/font-generation/upload', { method: 'POST', body: formData });
        const json = await resp.json();
        if (json.success) {
            uploadedFont.value = json.data;
            message.success(`已上传: ${json.data.fontName}`);
        }
        else {
            message.error(json.error || '上传失败');
        }
    }
    catch (e) {
        message.error(e.message || '上传失败');
    }
    finally {
        uploading.value = false;
    }
    return false;
}
async function handleSelectFontFile() {
    const path = await selectFile({
        title: '选择字体文件',
        filters: [{ label: '字体文件', extensions: ['.ttf', '.otf'] }],
    });
    if (!path)
        return;
    uploading.value = true;
    try {
        const result = await api.post('/api/font-generation/upload-from-path', { filePath: path });
        uploadedFont.value = result;
        message.success(`已上传: ${result.fontName}`);
    }
    catch (e) {
        message.error(e.message || '上传失败');
    }
    finally {
        uploading.value = false;
    }
}
// Character set helpers
function buildCharsetConfig() {
    if (useAllFontCharacters.value) {
        return {
            builtinSets: [],
            useAllFontCharacters: true,
        };
    }
    return {
        builtinSets: selectedCharsets.value,
        customCharsetFileName: customCharsetFile.value?.fileName,
        translationGameId: translationMode.value === 'game' ? translationGameId.value ?? undefined : undefined,
        translationFileName: translationMode.value === 'upload' ? translationFile.value?.fileName : undefined,
    };
}
function triggerPreview() {
    if (previewDebounceTimer)
        clearTimeout(previewDebounceTimer);
    previewDebounceTimer = setTimeout(loadPreview, 500);
}
async function loadPreview() {
    const config = buildCharsetConfig();
    if (config.useAllFontCharacters) {
        if (!uploadedFont.value) {
            charsetPreview.value = null;
            return;
        }
    }
    else if (config.builtinSets.length === 0 && !config.customCharsetFileName
        && !config.translationGameId && !config.translationFileName) {
        charsetPreview.value = null;
        return;
    }
    loadingPreview.value = true;
    try {
        charsetPreview.value = await api.post('/api/font-generation/charset/preview', {
            characterSet: config,
            atlasWidth: atlasSize.value,
            atlasHeight: atlasSize.value,
            samplingSize: samplingSize.value,
            fontFileName: uploadedFont.value?.fileName,
        });
    }
    catch (e) {
        message.error(e.message || '预览失败');
    }
    finally {
        loadingPreview.value = false;
    }
}
async function handleSelectCharsetFile() {
    const path = await selectFile({
        title: '选择字符集文件',
        filters: [{ label: '文本文件', extensions: ['.txt'] }],
    });
    if (!path)
        return;
    uploadingCharset.value = true;
    try {
        const result = await api.post('/api/font-generation/charset/upload-custom-from-path', { filePath: path });
        customCharsetFile.value = result;
        message.success(`已上传自定义字符集（${result.characterCount} 字符）`);
    }
    catch (e) {
        message.error(e.message || '上传失败');
    }
    finally {
        uploadingCharset.value = false;
    }
}
async function handleSelectTranslationFile() {
    const path = await selectFile({
        title: '选择翻译文件',
        filters: [{ label: '文本文件', extensions: ['.txt'] }],
    });
    if (!path)
        return;
    uploadingTranslation.value = true;
    try {
        const result = await api.post('/api/font-generation/charset/upload-translation-from-path', { filePath: path });
        translationFile.value = result;
        message.success(`已提取翻译文件字符（${result.characterCount} 字符）`);
    }
    catch (e) {
        message.error(e.message || '上传失败');
    }
    finally {
        uploadingTranslation.value = false;
    }
}
async function startGeneration() {
    if (!uploadedFont.value)
        return;
    try {
        await api.post('/api/font-generation/generate', {
            fileName: uploadedFont.value.fileName,
            unityVersion: unityVersion.value,
            samplingSize: samplingSize.value,
            atlasWidth: atlasSize.value,
            atlasHeight: atlasSize.value,
            characterSet: buildCharsetConfig(),
            renderMode: renderMode.value,
            samplingSizeMode: samplingSizeMode.value,
            paddingMode: paddingMode.value,
            paddingValue: paddingValue.value,
        });
        isGenerating.value = true;
        phase.value = 'parsing';
        current.value = 0;
        total.value = 0;
        generationReport.value = null;
    }
    catch (e) {
        message.error(e.message || '启动生成失败');
    }
}
async function cancelGeneration() {
    try {
        await api.post('/api/font-generation/cancel');
    }
    catch { /* ignore */ }
}
function downloadFont(fileName) {
    const a = document.createElement('a');
    a.href = `/api/font-generation/download/${encodeURIComponent(fileName)}`;
    a.download = fileName;
    a.click();
}
async function deleteFont(fileName) {
    try {
        await api.del(`/api/font-generation/${encodeURIComponent(fileName)}`);
        await loadHistory();
        message.success('已删除');
    }
    catch (e) {
        message.error(e.message || '删除失败');
    }
}
async function installTmpFont(fileName, gameId) {
    try {
        await api.post(`/api/font-generation/install-tmp-font/${gameId}`, { fileName });
        message.success('已安装为 XUnity TMP 字体');
    }
    catch (e) {
        message.error(e.message || '安装失败');
    }
}
function clearUploadedFont() {
    uploadedFont.value = null;
}
async function loadHistory() {
    try {
        generatedFonts.value = await api.get('/api/font-generation/history');
    }
    catch { /* ignore */ }
}
async function viewReport(fileName) {
    try {
        viewingReport.value = await api.get(`/api/font-generation/report/${fileName}`);
    }
    catch (e) {
        message.error(e.message || '获取报告失败');
    }
}
function formatDate(dateStr) {
    return new Date(dateStr).toLocaleString('zh-CN');
}
function formatDuration(ms) {
    if (ms < 1000)
        return `${ms} 毫秒`;
    const seconds = Math.floor(ms / 1000);
    if (seconds < 60)
        return `${seconds} 秒`;
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    return `${minutes} 分 ${remainingSeconds} 秒`;
}
// Watchers
watch([selectedCharsets, customCharsetFile, translationMode, translationGameId, translationFile, atlasSize, samplingSize, useAllFontCharacters], triggerPreview, { deep: true });
watch(uploadedFont, () => {
    if (useAllFontCharacters.value)
        triggerPreview();
});
// SignalR (created in onMounted)
let connection = null;
onMounted(async () => {
    checkMobile();
    try {
        connection = new HubConnectionBuilder()
            .withUrl('/hubs/install')
            .withAutomaticReconnect()
            .build();
        connection.on('FontGenerationProgress', (p) => {
            phase.value = p.phase;
            current.value = p.current;
            total.value = p.total;
        });
        connection.on('FontGenerationComplete', (result) => {
            isGenerating.value = false;
            if (result.success) {
                message.success(`字体 ${result.fontName} 生成成功（${result.glyphCount} 字形）`);
                generationReport.value = result.report ?? null;
            }
            else if (result.error === '已取消') {
                message.info('字体生成已取消');
                generationReport.value = null;
            }
            else {
                message.error(result.error || '生成失败');
                generationReport.value = null;
            }
            loadHistory();
        });
        connection.onreconnected(async () => {
            try {
                await connection?.invoke('JoinFontGenerationGroup');
            }
            catch { /* ignore */ }
        });
        await connection.start();
        await connection.invoke('JoinFontGenerationGroup');
    }
    catch (e) {
        // SignalR connection failed — silently ignore
    }
    await loadHistory();
    // Load games for "use as custom" dropdown
    try {
        const gamesData = await api.get('/api/games');
        games.value = gamesData.map((g) => ({ id: g.id, name: g.name }));
    }
    catch { /* ignore */ }
    // Load built-in charsets
    try {
        builtinCharsets.value = await api.get('/api/font-generation/charsets');
    }
    catch { /* ignore */ }
    // Check if generation is already in progress
    try {
        const status = await api.get('/api/font-generation/status');
        if (status.isGenerating) {
            isGenerating.value = true;
            phase.value = status.phase || '';
            current.value = status.current;
            total.value = status.total;
        }
    }
    catch { /* ignore */ }
});
async function cleanupConnection() {
    try {
        if (connection) {
            if (connection.state === HubConnectionState.Connected) {
                await connection.invoke('LeaveFontGenerationGroup');
            }
            await connection.stop();
        }
    }
    catch { /* ignore */ }
    connection = null;
}
onActivated(async () => {
    window.addEventListener('resize', checkMobile);
    // Re-establish SignalR after KeepAlive reactivation
    if (!connection || connection.state === HubConnectionState.Disconnected) {
        try {
            connection = new HubConnectionBuilder()
                .withUrl('/hubs/install')
                .withAutomaticReconnect()
                .build();
            connection.on('FontGenerationProgress', (p) => {
                phase.value = p.phase;
                current.value = p.current;
                total.value = p.total;
            });
            connection.on('FontGenerationComplete', (result) => {
                isGenerating.value = false;
                if (result.success) {
                    message.success(`字体 ${result.fontName} 生成成功（${result.glyphCount} 字形）`);
                    generationReport.value = result.report ?? null;
                }
                else if (result.error === '已取消') {
                    message.info('字体生成已取消');
                    generationReport.value = null;
                }
                else {
                    message.error(result.error || '生成失败');
                    generationReport.value = null;
                }
                loadHistory();
            });
            connection.onreconnected(async () => {
                try {
                    await connection?.invoke('JoinFontGenerationGroup');
                }
                catch { /* ignore */ }
            });
            await connection.start();
            await connection.invoke('JoinFontGenerationGroup');
        }
        catch {
            // SignalR reconnection failed — silently ignore
        }
    }
});
onDeactivated(async () => {
    window.removeEventListener('resize', checkMobile);
    if (previewDebounceTimer)
        clearTimeout(previewDebounceTimer);
    await cleanupConnection();
});
onBeforeUnmount(async () => {
    window.removeEventListener('resize', checkMobile);
    if (previewDebounceTimer)
        clearTimeout(previewDebounceTimer);
    await cleanupConnection();
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['upload-area']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['charset-section-label']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['charset-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['n-space']} */ ;
/** @type {__VLS_StyleScopedClasses['upload-area']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['n-space']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['n-space']} */ ;
/** @type {__VLS_StyleScopedClasses['font-item']} */ ;
/** @type {__VLS_StyleScopedClasses['n-space']} */ ;
/** @type {__VLS_StyleScopedClasses['preview-total']} */ ;
/** @type {__VLS_StyleScopedClasses['progress-info']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "font-generator-page" },
});
/** @type {__VLS_StyleScopedClasses['font-generator-page']} */ ;
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
    size: (22),
}));
const __VLS_2 = __VLS_1({
    size: (22),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
let __VLS_6;
/** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
FontDownloadOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "page-subtitle" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['page-subtitle']} */ ;
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
/** @ts-ignore @type {typeof __VLS_components.UploadFileOutlined} */
UploadFileOutlined;
// @ts-ignore
const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
var __VLS_14;
if (!__VLS_ctx.uploadedFont) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(!__VLS_ctx.uploadedFont))
                    return;
                !__VLS_ctx.isGenerating && __VLS_ctx.handleSelectFontFile();
                // @ts-ignore
                [uploadedFont, isGenerating, handleSelectFontFile,];
            } },
        ...{ class: "upload-area" },
        ...{ class: ({ disabled: __VLS_ctx.isGenerating }) },
    });
    /** @type {__VLS_StyleScopedClasses['upload-area']} */ ;
    /** @type {__VLS_StyleScopedClasses['disabled']} */ ;
    let __VLS_22;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
        size: (36),
        color: "var(--text-3)",
    }));
    const __VLS_24 = __VLS_23({
        size: (36),
        color: "var(--text-3)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_23));
    const { default: __VLS_27 } = __VLS_25.slots;
    let __VLS_28;
    /** @ts-ignore @type {typeof __VLS_components.UploadFileOutlined} */
    UploadFileOutlined;
    // @ts-ignore
    const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
    const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
    // @ts-ignore
    [isGenerating,];
    var __VLS_25;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "upload-text" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-text']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "upload-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-hint']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-settings-grid" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-settings-grid']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-column" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-column']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-result" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-result']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-result-info" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-result-info']} */ ;
    let __VLS_33;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
        size: (20),
        color: "var(--accent)",
    }));
    const __VLS_35 = __VLS_34({
        size: (20),
        color: "var(--accent)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_34));
    const { default: __VLS_38 } = __VLS_36.slots;
    let __VLS_39;
    /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
    FontDownloadOutlined;
    // @ts-ignore
    const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({}));
    const __VLS_41 = __VLS_40({}, ...__VLS_functionalComponentArgsRest(__VLS_40));
    // @ts-ignore
    [];
    var __VLS_36;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-font-name" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-font-name']} */ ;
    (__VLS_ctx.uploadedFont.fontName);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-file-size" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-file-size']} */ ;
    (__VLS_ctx.formatBytes(__VLS_ctx.uploadedFont.fileSize));
    let __VLS_44;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
        type: "error",
        disabled: (__VLS_ctx.isGenerating),
    }));
    const __VLS_46 = __VLS_45({
        ...{ 'onClick': {} },
        size: "small",
        quaternary: true,
        type: "error",
        disabled: (__VLS_ctx.isGenerating),
    }, ...__VLS_functionalComponentArgsRest(__VLS_45));
    let __VLS_49;
    const __VLS_50 = ({ click: {} },
        { onClick: (__VLS_ctx.clearUploadedFont) });
    const { default: __VLS_51 } = __VLS_47.slots;
    {
        const { icon: __VLS_52 } = __VLS_47.slots;
        let __VLS_53;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
            size: (14),
        }));
        const __VLS_55 = __VLS_54({
            size: (14),
        }, ...__VLS_functionalComponentArgsRest(__VLS_54));
        const { default: __VLS_58 } = __VLS_56.slots;
        let __VLS_59;
        /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
        DeleteOutlined;
        // @ts-ignore
        const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({}));
        const __VLS_61 = __VLS_60({}, ...__VLS_functionalComponentArgsRest(__VLS_60));
        // @ts-ignore
        [uploadedFont, uploadedFont, isGenerating, formatBytes, clearUploadedFont,];
        var __VLS_56;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_47;
    var __VLS_48;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-column" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-column']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        value: (__VLS_ctx.unityVersion),
        options: (__VLS_ctx.versionOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }));
    const __VLS_66 = __VLS_65({
        value: (__VLS_ctx.unityVersion),
        options: (__VLS_ctx.versionOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_69;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_70 = __VLS_asFunctionalComponent1(__VLS_69, new __VLS_69({
        value: (__VLS_ctx.renderMode),
        options: (__VLS_ctx.renderModeOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }));
    const __VLS_71 = __VLS_70({
        value: (__VLS_ctx.renderMode),
        options: (__VLS_ctx.renderModeOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_70));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_74;
    /** @ts-ignore @type {typeof __VLS_components.NSpace | typeof __VLS_components.NSpace} */
    NSpace;
    // @ts-ignore
    const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({
        align: "center",
        size: (8),
    }));
    const __VLS_76 = __VLS_75({
        align: "center",
        size: (8),
    }, ...__VLS_functionalComponentArgsRest(__VLS_75));
    const { default: __VLS_79 } = __VLS_77.slots;
    let __VLS_80;
    /** @ts-ignore @type {typeof __VLS_components.NRadioGroup | typeof __VLS_components.NRadioGroup} */
    NRadioGroup;
    // @ts-ignore
    const __VLS_81 = __VLS_asFunctionalComponent1(__VLS_80, new __VLS_80({
        value: (__VLS_ctx.samplingSizeMode),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }));
    const __VLS_82 = __VLS_81({
        value: (__VLS_ctx.samplingSizeMode),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_81));
    const { default: __VLS_85 } = __VLS_83.slots;
    let __VLS_86;
    /** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
    NRadio;
    // @ts-ignore
    const __VLS_87 = __VLS_asFunctionalComponent1(__VLS_86, new __VLS_86({
        value: "manual",
    }));
    const __VLS_88 = __VLS_87({
        value: "manual",
    }, ...__VLS_functionalComponentArgsRest(__VLS_87));
    const { default: __VLS_91 } = __VLS_89.slots;
    // @ts-ignore
    [isGenerating, isGenerating, isGenerating, unityVersion, versionOptions, renderMode, renderModeOptions, samplingSizeMode,];
    var __VLS_89;
    let __VLS_92;
    /** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
    NRadio;
    // @ts-ignore
    const __VLS_93 = __VLS_asFunctionalComponent1(__VLS_92, new __VLS_92({
        value: "auto",
    }));
    const __VLS_94 = __VLS_93({
        value: "auto",
    }, ...__VLS_functionalComponentArgsRest(__VLS_93));
    const { default: __VLS_97 } = __VLS_95.slots;
    // @ts-ignore
    [];
    var __VLS_95;
    // @ts-ignore
    [];
    var __VLS_83;
    if (__VLS_ctx.samplingSizeMode === 'manual') {
        let __VLS_98;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_99 = __VLS_asFunctionalComponent1(__VLS_98, new __VLS_98({
            value: (__VLS_ctx.samplingSize),
            options: (__VLS_ctx.samplingOptions),
            disabled: (__VLS_ctx.isGenerating),
            size: "small",
            ...{ style: {} },
        }));
        const __VLS_100 = __VLS_99({
            value: (__VLS_ctx.samplingSize),
            options: (__VLS_ctx.samplingOptions),
            disabled: (__VLS_ctx.isGenerating),
            size: "small",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_99));
    }
    // @ts-ignore
    [isGenerating, samplingSizeMode, samplingSize, samplingOptions,];
    var __VLS_77;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_103;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_104 = __VLS_asFunctionalComponent1(__VLS_103, new __VLS_103({
        value: (__VLS_ctx.atlasSize),
        options: (__VLS_ctx.atlasOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }));
    const __VLS_105 = __VLS_104({
        value: (__VLS_ctx.atlasSize),
        options: (__VLS_ctx.atlasOptions),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_104));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-row" },
    });
    /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
        ...{ class: "form-label" },
    });
    /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
    let __VLS_108;
    /** @ts-ignore @type {typeof __VLS_components.NSpace | typeof __VLS_components.NSpace} */
    NSpace;
    // @ts-ignore
    const __VLS_109 = __VLS_asFunctionalComponent1(__VLS_108, new __VLS_108({
        align: "center",
        size: (8),
    }));
    const __VLS_110 = __VLS_109({
        align: "center",
        size: (8),
    }, ...__VLS_functionalComponentArgsRest(__VLS_109));
    const { default: __VLS_113 } = __VLS_111.slots;
    let __VLS_114;
    /** @ts-ignore @type {typeof __VLS_components.NRadioGroup | typeof __VLS_components.NRadioGroup} */
    NRadioGroup;
    // @ts-ignore
    const __VLS_115 = __VLS_asFunctionalComponent1(__VLS_114, new __VLS_114({
        value: (__VLS_ctx.paddingMode),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }));
    const __VLS_116 = __VLS_115({
        value: (__VLS_ctx.paddingMode),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_115));
    const { default: __VLS_119 } = __VLS_117.slots;
    let __VLS_120;
    /** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
    NRadio;
    // @ts-ignore
    const __VLS_121 = __VLS_asFunctionalComponent1(__VLS_120, new __VLS_120({
        value: "percentage",
    }));
    const __VLS_122 = __VLS_121({
        value: "percentage",
    }, ...__VLS_functionalComponentArgsRest(__VLS_121));
    const { default: __VLS_125 } = __VLS_123.slots;
    // @ts-ignore
    [isGenerating, isGenerating, atlasSize, atlasOptions, paddingMode,];
    var __VLS_123;
    let __VLS_126;
    /** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
    NRadio;
    // @ts-ignore
    const __VLS_127 = __VLS_asFunctionalComponent1(__VLS_126, new __VLS_126({
        value: "pixel",
    }));
    const __VLS_128 = __VLS_127({
        value: "pixel",
    }, ...__VLS_functionalComponentArgsRest(__VLS_127));
    const { default: __VLS_131 } = __VLS_129.slots;
    // @ts-ignore
    [];
    var __VLS_129;
    // @ts-ignore
    [];
    var __VLS_117;
    let __VLS_132;
    /** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
    NInputNumber;
    // @ts-ignore
    const __VLS_133 = __VLS_asFunctionalComponent1(__VLS_132, new __VLS_132({
        value: (__VLS_ctx.paddingValue),
        min: (1),
        max: (__VLS_ctx.paddingMode === 'percentage' ? 50 : 100),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_134 = __VLS_133({
        value: (__VLS_ctx.paddingValue),
        min: (1),
        max: (__VLS_ctx.paddingMode === 'percentage' ? 50 : 100),
        disabled: (__VLS_ctx.isGenerating),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_133));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ style: {} },
    });
    (__VLS_ctx.paddingMode === 'percentage' ? '%' : 'px');
    // @ts-ignore
    [isGenerating, paddingMode, paddingMode, paddingValue,];
    var __VLS_111;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card" },
    ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.charset }) },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.collapsed.charset = !__VLS_ctx.collapsed.charset;
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
let __VLS_137;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_138 = __VLS_asFunctionalComponent1(__VLS_137, new __VLS_137({
    size: (16),
}));
const __VLS_139 = __VLS_138({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_138));
const { default: __VLS_142 } = __VLS_140.slots;
let __VLS_143;
/** @ts-ignore @type {typeof __VLS_components.TextFieldsOutlined} */
TextFieldsOutlined;
// @ts-ignore
const __VLS_144 = __VLS_asFunctionalComponent1(__VLS_143, new __VLS_143({}));
const __VLS_145 = __VLS_144({}, ...__VLS_functionalComponentArgsRest(__VLS_144));
// @ts-ignore
[];
var __VLS_140;
let __VLS_148;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_149 = __VLS_asFunctionalComponent1(__VLS_148, new __VLS_148({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.charset }) },
}));
const __VLS_150 = __VLS_149({
    size: (18),
    ...{ class: "collapse-chevron" },
    ...{ class: ({ expanded: !__VLS_ctx.collapsed.charset }) },
}, ...__VLS_functionalComponentArgsRest(__VLS_149));
/** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
/** @type {__VLS_StyleScopedClasses['expanded']} */ ;
const { default: __VLS_153 } = __VLS_151.slots;
let __VLS_154;
/** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
ExpandMoreOutlined;
// @ts-ignore
const __VLS_155 = __VLS_asFunctionalComponent1(__VLS_154, new __VLS_154({}));
const __VLS_156 = __VLS_155({}, ...__VLS_functionalComponentArgsRest(__VLS_155));
// @ts-ignore
[collapsed,];
var __VLS_151;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body" },
    ...{ class: ({ collapsed: __VLS_ctx.collapsed.charset }) },
});
/** @type {__VLS_StyleScopedClasses['section-body']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-body-inner" },
});
/** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "charset-all-font-row" },
});
/** @type {__VLS_StyleScopedClasses['charset-all-font-row']} */ ;
let __VLS_159;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_160 = __VLS_asFunctionalComponent1(__VLS_159, new __VLS_159({
    value: (__VLS_ctx.useAllFontCharacters),
    disabled: (__VLS_ctx.isGenerating || !__VLS_ctx.uploadedFont),
}));
const __VLS_161 = __VLS_160({
    value: (__VLS_ctx.useAllFontCharacters),
    disabled: (__VLS_ctx.isGenerating || !__VLS_ctx.uploadedFont),
}, ...__VLS_functionalComponentArgsRest(__VLS_160));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "charset-section-label" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['charset-section-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "hint-text" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['hint-text']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: ({ 'charset-disabled': __VLS_ctx.useAllFontCharacters }) },
});
/** @type {__VLS_StyleScopedClasses['charset-disabled']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "charset-section-label" },
});
/** @type {__VLS_StyleScopedClasses['charset-section-label']} */ ;
let __VLS_164;
/** @ts-ignore @type {typeof __VLS_components.NCheckboxGroup | typeof __VLS_components.NCheckboxGroup} */
NCheckboxGroup;
// @ts-ignore
const __VLS_165 = __VLS_asFunctionalComponent1(__VLS_164, new __VLS_164({
    value: (__VLS_ctx.selectedCharsets),
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
}));
const __VLS_166 = __VLS_165({
    value: (__VLS_ctx.selectedCharsets),
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
}, ...__VLS_functionalComponentArgsRest(__VLS_165));
const { default: __VLS_169 } = __VLS_167.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "charset-grid" },
});
/** @type {__VLS_StyleScopedClasses['charset-grid']} */ ;
for (const [cs] of __VLS_vFor((__VLS_ctx.builtinCharsets))) {
    let __VLS_170;
    /** @ts-ignore @type {typeof __VLS_components.NCheckbox} */
    NCheckbox;
    // @ts-ignore
    const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
        key: (cs.id),
        value: (cs.id),
        label: (`${cs.name}（~${cs.characterCount.toLocaleString()} 字符）`),
    }));
    const __VLS_172 = __VLS_171({
        key: (cs.id),
        value: (cs.id),
        label: (`${cs.name}（~${cs.characterCount.toLocaleString()} 字符）`),
    }, ...__VLS_functionalComponentArgsRest(__VLS_171));
    // @ts-ignore
    [uploadedFont, isGenerating, isGenerating, collapsed, useAllFontCharacters, useAllFontCharacters, useAllFontCharacters, selectedCharsets, builtinCharsets,];
}
if (__VLS_ctx.builtinCharsets.length === 0) {
    let __VLS_175;
    /** @ts-ignore @type {typeof __VLS_components.NCheckbox} */
    NCheckbox;
    // @ts-ignore
    const __VLS_176 = __VLS_asFunctionalComponent1(__VLS_175, new __VLS_175({
        value: "GB2312",
        label: "GB2312（~7500 字符）",
    }));
    const __VLS_177 = __VLS_176({
        value: "GB2312",
        label: "GB2312（~7500 字符）",
    }, ...__VLS_functionalComponentArgsRest(__VLS_176));
}
// @ts-ignore
[builtinCharsets,];
var __VLS_167;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "hint-text" },
});
/** @type {__VLS_StyleScopedClasses['hint-text']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "charset-section-label" },
});
/** @type {__VLS_StyleScopedClasses['charset-section-label']} */ ;
let __VLS_180;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_181 = __VLS_asFunctionalComponent1(__VLS_180, new __VLS_180({
    ...{ 'onClick': {} },
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
    loading: (__VLS_ctx.uploadingCharset),
    size: "small",
}));
const __VLS_182 = __VLS_181({
    ...{ 'onClick': {} },
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
    loading: (__VLS_ctx.uploadingCharset),
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_181));
let __VLS_185;
const __VLS_186 = ({ click: {} },
    { onClick: (__VLS_ctx.handleSelectCharsetFile) });
const { default: __VLS_187 } = __VLS_183.slots;
// @ts-ignore
[isGenerating, useAllFontCharacters, uploadingCharset, handleSelectCharsetFile,];
var __VLS_183;
var __VLS_184;
if (__VLS_ctx.customCharsetFile) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-info" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-info']} */ ;
    let __VLS_188;
    /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
    NTag;
    // @ts-ignore
    const __VLS_189 = __VLS_asFunctionalComponent1(__VLS_188, new __VLS_188({
        size: "small",
        type: "success",
    }));
    const __VLS_190 = __VLS_189({
        size: "small",
        type: "success",
    }, ...__VLS_functionalComponentArgsRest(__VLS_189));
    const { default: __VLS_193 } = __VLS_191.slots;
    (__VLS_ctx.customCharsetFile.fileName);
    // @ts-ignore
    [customCharsetFile, customCharsetFile,];
    var __VLS_191;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.customCharsetFile.characterCount.toLocaleString());
    let __VLS_194;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_195 = __VLS_asFunctionalComponent1(__VLS_194, new __VLS_194({
        ...{ 'onClick': {} },
        text: true,
        size: "tiny",
    }));
    const __VLS_196 = __VLS_195({
        ...{ 'onClick': {} },
        text: true,
        size: "tiny",
    }, ...__VLS_functionalComponentArgsRest(__VLS_195));
    let __VLS_199;
    const __VLS_200 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.customCharsetFile))
                    return;
                __VLS_ctx.customCharsetFile = null;
                // @ts-ignore
                [customCharsetFile, customCharsetFile,];
            } });
    const { default: __VLS_201 } = __VLS_197.slots;
    // @ts-ignore
    [];
    var __VLS_197;
    var __VLS_198;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "hint-text" },
});
/** @type {__VLS_StyleScopedClasses['hint-text']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "charset-section-label" },
});
/** @type {__VLS_StyleScopedClasses['charset-section-label']} */ ;
let __VLS_202;
/** @ts-ignore @type {typeof __VLS_components.NRadioGroup | typeof __VLS_components.NRadioGroup} */
NRadioGroup;
// @ts-ignore
const __VLS_203 = __VLS_asFunctionalComponent1(__VLS_202, new __VLS_202({
    value: (__VLS_ctx.translationMode),
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
}));
const __VLS_204 = __VLS_203({
    value: (__VLS_ctx.translationMode),
    disabled: (__VLS_ctx.isGenerating || __VLS_ctx.useAllFontCharacters),
}, ...__VLS_functionalComponentArgsRest(__VLS_203));
const { default: __VLS_207 } = __VLS_205.slots;
let __VLS_208;
/** @ts-ignore @type {typeof __VLS_components.NSpace | typeof __VLS_components.NSpace} */
NSpace;
// @ts-ignore
const __VLS_209 = __VLS_asFunctionalComponent1(__VLS_208, new __VLS_208({
    size: (16),
}));
const __VLS_210 = __VLS_209({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_209));
const { default: __VLS_213 } = __VLS_211.slots;
let __VLS_214;
/** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
NRadio;
// @ts-ignore
const __VLS_215 = __VLS_asFunctionalComponent1(__VLS_214, new __VLS_214({
    value: "none",
}));
const __VLS_216 = __VLS_215({
    value: "none",
}, ...__VLS_functionalComponentArgsRest(__VLS_215));
const { default: __VLS_219 } = __VLS_217.slots;
// @ts-ignore
[isGenerating, useAllFontCharacters, translationMode,];
var __VLS_217;
let __VLS_220;
/** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
NRadio;
// @ts-ignore
const __VLS_221 = __VLS_asFunctionalComponent1(__VLS_220, new __VLS_220({
    value: "game",
}));
const __VLS_222 = __VLS_221({
    value: "game",
}, ...__VLS_functionalComponentArgsRest(__VLS_221));
const { default: __VLS_225 } = __VLS_223.slots;
// @ts-ignore
[];
var __VLS_223;
let __VLS_226;
/** @ts-ignore @type {typeof __VLS_components.NRadio | typeof __VLS_components.NRadio} */
NRadio;
// @ts-ignore
const __VLS_227 = __VLS_asFunctionalComponent1(__VLS_226, new __VLS_226({
    value: "upload",
}));
const __VLS_228 = __VLS_227({
    value: "upload",
}, ...__VLS_functionalComponentArgsRest(__VLS_227));
const { default: __VLS_231 } = __VLS_229.slots;
// @ts-ignore
[];
var __VLS_229;
// @ts-ignore
[];
var __VLS_211;
// @ts-ignore
[];
var __VLS_205;
if (__VLS_ctx.translationMode === 'game') {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    let __VLS_232;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_233 = __VLS_asFunctionalComponent1(__VLS_232, new __VLS_232({
        value: (__VLS_ctx.translationGameId),
        placeholder: "选择已安装的游戏",
        options: (__VLS_ctx.games.map(g => ({ label: g.name, value: g.id }))),
        disabled: (__VLS_ctx.isGenerating),
        clearable: true,
        filterable: true,
        ...{ style: {} },
    }));
    const __VLS_234 = __VLS_233({
        value: (__VLS_ctx.translationGameId),
        placeholder: "选择已安装的游戏",
        options: (__VLS_ctx.games.map(g => ({ label: g.name, value: g.id }))),
        disabled: (__VLS_ctx.isGenerating),
        clearable: true,
        filterable: true,
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_233));
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "hint-text" },
    });
    /** @type {__VLS_StyleScopedClasses['hint-text']} */ ;
}
if (__VLS_ctx.translationMode === 'upload') {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    let __VLS_237;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_238 = __VLS_asFunctionalComponent1(__VLS_237, new __VLS_237({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isGenerating),
        loading: (__VLS_ctx.uploadingTranslation),
        size: "small",
    }));
    const __VLS_239 = __VLS_238({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isGenerating),
        loading: (__VLS_ctx.uploadingTranslation),
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_238));
    let __VLS_242;
    const __VLS_243 = ({ click: {} },
        { onClick: (__VLS_ctx.handleSelectTranslationFile) });
    const { default: __VLS_244 } = __VLS_240.slots;
    // @ts-ignore
    [isGenerating, isGenerating, translationMode, translationMode, translationGameId, games, uploadingTranslation, handleSelectTranslationFile,];
    var __VLS_240;
    var __VLS_241;
    if (__VLS_ctx.translationFile) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "upload-info" },
        });
        /** @type {__VLS_StyleScopedClasses['upload-info']} */ ;
        let __VLS_245;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_246 = __VLS_asFunctionalComponent1(__VLS_245, new __VLS_245({
            size: "small",
            type: "success",
        }));
        const __VLS_247 = __VLS_246({
            size: "small",
            type: "success",
        }, ...__VLS_functionalComponentArgsRest(__VLS_246));
        const { default: __VLS_250 } = __VLS_248.slots;
        (__VLS_ctx.translationFile.fileName);
        // @ts-ignore
        [translationFile, translationFile,];
        var __VLS_248;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.translationFile.characterCount.toLocaleString());
        let __VLS_251;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_252 = __VLS_asFunctionalComponent1(__VLS_251, new __VLS_251({
            ...{ 'onClick': {} },
            text: true,
            size: "tiny",
        }));
        const __VLS_253 = __VLS_252({
            ...{ 'onClick': {} },
            text: true,
            size: "tiny",
        }, ...__VLS_functionalComponentArgsRest(__VLS_252));
        let __VLS_256;
        const __VLS_257 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.translationMode === 'upload'))
                        return;
                    if (!(__VLS_ctx.translationFile))
                        return;
                    __VLS_ctx.translationFile = null;
                    // @ts-ignore
                    [translationFile, translationFile,];
                } });
        const { default: __VLS_258 } = __VLS_254.slots;
        // @ts-ignore
        [];
        var __VLS_254;
        var __VLS_255;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "hint-text" },
    });
    /** @type {__VLS_StyleScopedClasses['hint-text']} */ ;
}
if (__VLS_ctx.charsetPreview || __VLS_ctx.loadingPreview) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "charset-preview" },
    });
    /** @type {__VLS_StyleScopedClasses['charset-preview']} */ ;
    if (__VLS_ctx.loadingPreview) {
        let __VLS_259;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_260 = __VLS_asFunctionalComponent1(__VLS_259, new __VLS_259({
            size: (16),
            ...{ style: {} },
        }));
        const __VLS_261 = __VLS_260({
            size: (16),
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_260));
    }
    if (__VLS_ctx.charsetPreview) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "preview-stats" },
        });
        /** @type {__VLS_StyleScopedClasses['preview-stats']} */ ;
        for (const [count, source] of __VLS_vFor((__VLS_ctx.charsetPreview.sourceBreakdown))) {
            let __VLS_264;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_265 = __VLS_asFunctionalComponent1(__VLS_264, new __VLS_264({
                key: (source),
                size: "small",
            }));
            const __VLS_266 = __VLS_265({
                key: (source),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_265));
            const { default: __VLS_269 } = __VLS_267.slots;
            (source);
            (count.toLocaleString());
            // @ts-ignore
            [charsetPreview, charsetPreview, charsetPreview, loadingPreview, loadingPreview,];
            var __VLS_267;
            // @ts-ignore
            [];
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "preview-total" },
        });
        /** @type {__VLS_StyleScopedClasses['preview-total']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
        (__VLS_ctx.charsetPreview.totalCharacters.toLocaleString());
        for (const [warning, idx] of __VLS_vFor((__VLS_ctx.charsetPreview.warnings))) {
            let __VLS_270;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_271 = __VLS_asFunctionalComponent1(__VLS_270, new __VLS_270({
                key: (idx),
                type: "warning",
                bordered: (false),
                ...{ style: {} },
            }));
            const __VLS_272 = __VLS_271({
                key: (idx),
                type: "warning",
                bordered: (false),
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_271));
            const { default: __VLS_275 } = __VLS_273.slots;
            (warning);
            // @ts-ignore
            [charsetPreview, charsetPreview,];
            var __VLS_273;
            // @ts-ignore
            [];
        }
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-actions" },
});
/** @type {__VLS_StyleScopedClasses['form-actions']} */ ;
let __VLS_276;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_277 = __VLS_asFunctionalComponent1(__VLS_276, new __VLS_276({
    ...{ 'onClick': {} },
    type: "primary",
    disabled: (!__VLS_ctx.uploadedFont || __VLS_ctx.isGenerating),
    loading: (__VLS_ctx.isGenerating),
}));
const __VLS_278 = __VLS_277({
    ...{ 'onClick': {} },
    type: "primary",
    disabled: (!__VLS_ctx.uploadedFont || __VLS_ctx.isGenerating),
    loading: (__VLS_ctx.isGenerating),
}, ...__VLS_functionalComponentArgsRest(__VLS_277));
let __VLS_281;
const __VLS_282 = ({ click: {} },
    { onClick: (__VLS_ctx.startGeneration) });
const { default: __VLS_283 } = __VLS_279.slots;
// @ts-ignore
[uploadedFont, isGenerating, isGenerating, startGeneration,];
var __VLS_279;
var __VLS_280;
if (__VLS_ctx.isGenerating) {
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
    let __VLS_284;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_285 = __VLS_asFunctionalComponent1(__VLS_284, new __VLS_284({
        size: (16),
    }));
    const __VLS_286 = __VLS_285({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_285));
    const { default: __VLS_289 } = __VLS_287.slots;
    let __VLS_290;
    /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
    FontDownloadOutlined;
    // @ts-ignore
    const __VLS_291 = __VLS_asFunctionalComponent1(__VLS_290, new __VLS_290({}));
    const __VLS_292 = __VLS_291({}, ...__VLS_functionalComponentArgsRest(__VLS_291));
    // @ts-ignore
    [isGenerating,];
    var __VLS_287;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-info" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-info']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "progress-phase" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-phase']} */ ;
    (__VLS_ctx.phaseLabels[__VLS_ctx.phase] || __VLS_ctx.phase);
    if (__VLS_ctx.total > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "progress-count" },
        });
        /** @type {__VLS_StyleScopedClasses['progress-count']} */ ;
        (__VLS_ctx.current);
        (__VLS_ctx.total);
    }
    let __VLS_295;
    /** @ts-ignore @type {typeof __VLS_components.NProgress} */
    NProgress;
    // @ts-ignore
    const __VLS_296 = __VLS_asFunctionalComponent1(__VLS_295, new __VLS_295({
        percentage: (__VLS_ctx.progressPercent),
        showIndicator: (true),
        type: "line",
    }));
    const __VLS_297 = __VLS_296({
        percentage: (__VLS_ctx.progressPercent),
        showIndicator: (true),
        type: "line",
    }, ...__VLS_functionalComponentArgsRest(__VLS_296));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "form-actions" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['form-actions']} */ ;
    let __VLS_300;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_301 = __VLS_asFunctionalComponent1(__VLS_300, new __VLS_300({
        ...{ 'onClick': {} },
    }));
    const __VLS_302 = __VLS_301({
        ...{ 'onClick': {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_301));
    let __VLS_305;
    const __VLS_306 = ({ click: {} },
        { onClick: (__VLS_ctx.cancelGeneration) });
    const { default: __VLS_307 } = __VLS_303.slots;
    // @ts-ignore
    [phaseLabels, phase, phase, total, total, current, progressPercent, cancelGeneration,];
    var __VLS_303;
    var __VLS_304;
}
if (__VLS_ctx.activeReport && !__VLS_ctx.isGenerating) {
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
    let __VLS_308;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_309 = __VLS_asFunctionalComponent1(__VLS_308, new __VLS_308({
        size: (16),
    }));
    const __VLS_310 = __VLS_309({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_309));
    const { default: __VLS_313 } = __VLS_311.slots;
    let __VLS_314;
    /** @ts-ignore @type {typeof __VLS_components.AssessmentOutlined} */
    AssessmentOutlined;
    // @ts-ignore
    const __VLS_315 = __VLS_asFunctionalComponent1(__VLS_314, new __VLS_314({}));
    const __VLS_316 = __VLS_315({}, ...__VLS_functionalComponentArgsRest(__VLS_315));
    // @ts-ignore
    [isGenerating, activeReport,];
    var __VLS_311;
    let __VLS_319;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptions | typeof __VLS_components.NDescriptions} */
    NDescriptions;
    // @ts-ignore
    const __VLS_320 = __VLS_asFunctionalComponent1(__VLS_319, new __VLS_319({
        labelPlacement: "left",
        column: (__VLS_ctx.isMobile ? 1 : 2),
        bordered: true,
        size: "small",
    }));
    const __VLS_321 = __VLS_320({
        labelPlacement: "left",
        column: (__VLS_ctx.isMobile ? 1 : 2),
        bordered: true,
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_320));
    const { default: __VLS_324 } = __VLS_322.slots;
    let __VLS_325;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_326 = __VLS_asFunctionalComponent1(__VLS_325, new __VLS_325({
        label: "字体名称",
    }));
    const __VLS_327 = __VLS_326({
        label: "字体名称",
    }, ...__VLS_functionalComponentArgsRest(__VLS_326));
    const { default: __VLS_330 } = __VLS_328.slots;
    (__VLS_ctx.activeReport.fontName);
    // @ts-ignore
    [activeReport, isMobile,];
    var __VLS_328;
    let __VLS_331;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_332 = __VLS_asFunctionalComponent1(__VLS_331, new __VLS_331({
        label: "耗时",
    }));
    const __VLS_333 = __VLS_332({
        label: "耗时",
    }, ...__VLS_functionalComponentArgsRest(__VLS_332));
    const { default: __VLS_336 } = __VLS_334.slots;
    (__VLS_ctx.formatDuration(__VLS_ctx.activeReport.elapsedMilliseconds));
    // @ts-ignore
    [activeReport, formatDuration,];
    var __VLS_334;
    let __VLS_337;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_338 = __VLS_asFunctionalComponent1(__VLS_337, new __VLS_337({
        label: "总字符数",
    }));
    const __VLS_339 = __VLS_338({
        label: "总字符数",
    }, ...__VLS_functionalComponentArgsRest(__VLS_338));
    const { default: __VLS_342 } = __VLS_340.slots;
    (__VLS_ctx.activeReport.totalCharacters.toLocaleString());
    // @ts-ignore
    [activeReport,];
    var __VLS_340;
    let __VLS_343;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_344 = __VLS_asFunctionalComponent1(__VLS_343, new __VLS_343({
        label: "成功渲染",
    }));
    const __VLS_345 = __VLS_344({
        label: "成功渲染",
    }, ...__VLS_functionalComponentArgsRest(__VLS_344));
    const { default: __VLS_348 } = __VLS_346.slots;
    (__VLS_ctx.activeReport.successfulGlyphs.toLocaleString());
    // @ts-ignore
    [activeReport,];
    var __VLS_346;
    let __VLS_349;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_350 = __VLS_asFunctionalComponent1(__VLS_349, new __VLS_349({
        label: "缺失字形",
    }));
    const __VLS_351 = __VLS_350({
        label: "缺失字形",
    }, ...__VLS_functionalComponentArgsRest(__VLS_350));
    const { default: __VLS_354 } = __VLS_352.slots;
    (__VLS_ctx.activeReport.totalMissingCount.toLocaleString());
    // @ts-ignore
    [activeReport,];
    var __VLS_352;
    let __VLS_355;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_356 = __VLS_asFunctionalComponent1(__VLS_355, new __VLS_355({
        label: "Atlas",
    }));
    const __VLS_357 = __VLS_356({
        label: "Atlas",
    }, ...__VLS_functionalComponentArgsRest(__VLS_356));
    const { default: __VLS_360 } = __VLS_358.slots;
    (__VLS_ctx.activeReport.atlasCount);
    (__VLS_ctx.activeReport.atlasWidth);
    (__VLS_ctx.activeReport.atlasHeight);
    // @ts-ignore
    [activeReport, activeReport, activeReport,];
    var __VLS_358;
    let __VLS_361;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_362 = __VLS_asFunctionalComponent1(__VLS_361, new __VLS_361({
        label: "渲染模式",
    }));
    const __VLS_363 = __VLS_362({
        label: "渲染模式",
    }, ...__VLS_functionalComponentArgsRest(__VLS_362));
    const { default: __VLS_366 } = __VLS_364.slots;
    (__VLS_ctx.activeReport.renderMode);
    // @ts-ignore
    [activeReport,];
    var __VLS_364;
    if (__VLS_ctx.activeReport.samplingSizeMode === 'auto') {
        let __VLS_367;
        /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
        NDescriptionsItem;
        // @ts-ignore
        const __VLS_368 = __VLS_asFunctionalComponent1(__VLS_367, new __VLS_367({
            label: "实际采样大小",
        }));
        const __VLS_369 = __VLS_368({
            label: "实际采样大小",
        }, ...__VLS_functionalComponentArgsRest(__VLS_368));
        const { default: __VLS_372 } = __VLS_370.slots;
        (__VLS_ctx.activeReport.actualSamplingSize);
        // @ts-ignore
        [activeReport, activeReport,];
        var __VLS_370;
    }
    let __VLS_373;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_374 = __VLS_asFunctionalComponent1(__VLS_373, new __VLS_373({
        label: "Padding",
    }));
    const __VLS_375 = __VLS_374({
        label: "Padding",
    }, ...__VLS_functionalComponentArgsRest(__VLS_374));
    const { default: __VLS_378 } = __VLS_376.slots;
    (__VLS_ctx.activeReport.padding);
    // @ts-ignore
    [activeReport,];
    var __VLS_376;
    let __VLS_379;
    /** @ts-ignore @type {typeof __VLS_components.NDescriptionsItem | typeof __VLS_components.NDescriptionsItem} */
    NDescriptionsItem;
    // @ts-ignore
    const __VLS_380 = __VLS_asFunctionalComponent1(__VLS_379, new __VLS_379({
        label: "GradientScale",
    }));
    const __VLS_381 = __VLS_380({
        label: "GradientScale",
    }, ...__VLS_functionalComponentArgsRest(__VLS_380));
    const { default: __VLS_384 } = __VLS_382.slots;
    (__VLS_ctx.activeReport.gradientScale);
    // @ts-ignore
    [activeReport,];
    var __VLS_382;
    // @ts-ignore
    [];
    var __VLS_322;
    if (Object.keys(__VLS_ctx.activeReport.sourceBreakdown).length) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ style: {} },
        });
        for (const [count, source] of __VLS_vFor((__VLS_ctx.activeReport.sourceBreakdown))) {
            let __VLS_385;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_386 = __VLS_asFunctionalComponent1(__VLS_385, new __VLS_385({
                key: (source),
                size: "small",
                ...{ style: {} },
            }));
            const __VLS_387 = __VLS_386({
                key: (source),
                size: "small",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_386));
            const { default: __VLS_390 } = __VLS_388.slots;
            (source);
            (count.toLocaleString());
            // @ts-ignore
            [activeReport, activeReport,];
            var __VLS_388;
            // @ts-ignore
            [];
        }
    }
    if (__VLS_ctx.activeReport.totalMissingCount > 0) {
        let __VLS_391;
        /** @ts-ignore @type {typeof __VLS_components.NCollapse | typeof __VLS_components.NCollapse} */
        NCollapse;
        // @ts-ignore
        const __VLS_392 = __VLS_asFunctionalComponent1(__VLS_391, new __VLS_391({
            ...{ style: {} },
        }));
        const __VLS_393 = __VLS_392({
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_392));
        const { default: __VLS_396 } = __VLS_394.slots;
        let __VLS_397;
        /** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
        NCollapseItem;
        // @ts-ignore
        const __VLS_398 = __VLS_asFunctionalComponent1(__VLS_397, new __VLS_397({
            title: "查看缺失字符",
            name: "missing",
        }));
        const __VLS_399 = __VLS_398({
            title: "查看缺失字符",
            name: "missing",
        }, ...__VLS_functionalComponentArgsRest(__VLS_398));
        const { default: __VLS_402 } = __VLS_400.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "missing-chars" },
        });
        /** @type {__VLS_StyleScopedClasses['missing-chars']} */ ;
        (__VLS_ctx.activeReport.missingCharacters.join(''));
        if (__VLS_ctx.activeReport.totalMissingCount > __VLS_ctx.activeReport.missingCharacters.length) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.activeReport.totalMissingCount);
        }
        // @ts-ignore
        [activeReport, activeReport, activeReport, activeReport, activeReport,];
        var __VLS_400;
        // @ts-ignore
        [];
        var __VLS_394;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    let __VLS_403;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_404 = __VLS_asFunctionalComponent1(__VLS_403, new __VLS_403({
        ...{ 'onClick': {} },
        quaternary: true,
        size: "small",
    }));
    const __VLS_405 = __VLS_404({
        ...{ 'onClick': {} },
        quaternary: true,
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_404));
    let __VLS_408;
    const __VLS_409 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.activeReport && !__VLS_ctx.isGenerating))
                    return;
                __VLS_ctx.viewingReport = null;
                __VLS_ctx.generationReport = null;
                // @ts-ignore
                [viewingReport, generationReport,];
            } });
    const { default: __VLS_410 } = __VLS_406.slots;
    // @ts-ignore
    [];
    var __VLS_406;
    var __VLS_407;
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
let __VLS_411;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_412 = __VLS_asFunctionalComponent1(__VLS_411, new __VLS_411({
    size: (16),
}));
const __VLS_413 = __VLS_412({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_412));
const { default: __VLS_416 } = __VLS_414.slots;
let __VLS_417;
/** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
DownloadOutlined;
// @ts-ignore
const __VLS_418 = __VLS_asFunctionalComponent1(__VLS_417, new __VLS_417({}));
const __VLS_419 = __VLS_418({}, ...__VLS_functionalComponentArgsRest(__VLS_418));
// @ts-ignore
[];
var __VLS_414;
if (__VLS_ctx.generatedFonts.length === 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "empty-state" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "font-list" },
    });
    /** @type {__VLS_StyleScopedClasses['font-list']} */ ;
    for (const [font] of __VLS_vFor((__VLS_ctx.generatedFonts))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (font.fileName),
            ...{ class: "font-item" },
        });
        /** @type {__VLS_StyleScopedClasses['font-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "font-item-info" },
        });
        /** @type {__VLS_StyleScopedClasses['font-item-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "font-item-name" },
        });
        /** @type {__VLS_StyleScopedClasses['font-item-name']} */ ;
        (font.fontName);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "font-item-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['font-item-meta']} */ ;
        (__VLS_ctx.formatBytes(font.fileSize));
        (__VLS_ctx.formatDate(font.generatedAt));
        let __VLS_422;
        /** @ts-ignore @type {typeof __VLS_components.NSpace | typeof __VLS_components.NSpace} */
        NSpace;
        // @ts-ignore
        const __VLS_423 = __VLS_asFunctionalComponent1(__VLS_422, new __VLS_422({
            size: (8),
        }));
        const __VLS_424 = __VLS_423({
            size: (8),
        }, ...__VLS_functionalComponentArgsRest(__VLS_423));
        const { default: __VLS_427 } = __VLS_425.slots;
        let __VLS_428;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_429 = __VLS_asFunctionalComponent1(__VLS_428, new __VLS_428({
            ...{ 'onClick': {} },
            size: "small",
        }));
        const __VLS_430 = __VLS_429({
            ...{ 'onClick': {} },
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_429));
        let __VLS_433;
        const __VLS_434 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.generatedFonts.length === 0))
                        return;
                    __VLS_ctx.downloadFont(font.fileName);
                    // @ts-ignore
                    [formatBytes, generatedFonts, generatedFonts, formatDate, downloadFont,];
                } });
        const { default: __VLS_435 } = __VLS_431.slots;
        {
            const { icon: __VLS_436 } = __VLS_431.slots;
            let __VLS_437;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_438 = __VLS_asFunctionalComponent1(__VLS_437, new __VLS_437({}));
            const __VLS_439 = __VLS_438({}, ...__VLS_functionalComponentArgsRest(__VLS_438));
            const { default: __VLS_442 } = __VLS_440.slots;
            let __VLS_443;
            /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
            DownloadOutlined;
            // @ts-ignore
            const __VLS_444 = __VLS_asFunctionalComponent1(__VLS_443, new __VLS_443({}));
            const __VLS_445 = __VLS_444({}, ...__VLS_functionalComponentArgsRest(__VLS_444));
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
        if (font.hasReport) {
            let __VLS_448;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_449 = __VLS_asFunctionalComponent1(__VLS_448, new __VLS_448({
                ...{ 'onClick': {} },
                size: "small",
            }));
            const __VLS_450 = __VLS_449({
                ...{ 'onClick': {} },
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_449));
            let __VLS_453;
            const __VLS_454 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.generatedFonts.length === 0))
                            return;
                        if (!(font.hasReport))
                            return;
                        __VLS_ctx.viewReport(font.fileName);
                        // @ts-ignore
                        [viewReport,];
                    } });
            const { default: __VLS_455 } = __VLS_451.slots;
            // @ts-ignore
            [];
            var __VLS_451;
            var __VLS_452;
        }
        if (__VLS_ctx.games.length > 0) {
            let __VLS_456;
            /** @ts-ignore @type {typeof __VLS_components.NSelect} */
            NSelect;
            // @ts-ignore
            const __VLS_457 = __VLS_asFunctionalComponent1(__VLS_456, new __VLS_456({
                ...{ 'onUpdate:value': {} },
                size: "small",
                placeholder: "安装 TMP 字体",
                options: (__VLS_ctx.games.map(g => ({ label: g.name, value: g.id }))),
                ...{ style: {} },
            }));
            const __VLS_458 = __VLS_457({
                ...{ 'onUpdate:value': {} },
                size: "small",
                placeholder: "安装 TMP 字体",
                options: (__VLS_ctx.games.map(g => ({ label: g.name, value: g.id }))),
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_457));
            let __VLS_461;
            const __VLS_462 = ({ 'update:value': {} },
                { 'onUpdate:value': ((gameId) => __VLS_ctx.installTmpFont(font.fileName, gameId)) });
            var __VLS_459;
            var __VLS_460;
        }
        let __VLS_463;
        /** @ts-ignore @type {typeof __VLS_components.NPopconfirm | typeof __VLS_components.NPopconfirm} */
        NPopconfirm;
        // @ts-ignore
        const __VLS_464 = __VLS_asFunctionalComponent1(__VLS_463, new __VLS_463({
            ...{ 'onPositiveClick': {} },
        }));
        const __VLS_465 = __VLS_464({
            ...{ 'onPositiveClick': {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_464));
        let __VLS_468;
        const __VLS_469 = ({ positiveClick: {} },
            { onPositiveClick: (...[$event]) => {
                    if (!!(__VLS_ctx.generatedFonts.length === 0))
                        return;
                    __VLS_ctx.deleteFont(font.fileName);
                    // @ts-ignore
                    [games, games, installTmpFont, deleteFont,];
                } });
        const { default: __VLS_470 } = __VLS_466.slots;
        {
            const { trigger: __VLS_471 } = __VLS_466.slots;
            let __VLS_472;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_473 = __VLS_asFunctionalComponent1(__VLS_472, new __VLS_472({
                size: "small",
                type: "error",
                quaternary: true,
            }));
            const __VLS_474 = __VLS_473({
                size: "small",
                type: "error",
                quaternary: true,
            }, ...__VLS_functionalComponentArgsRest(__VLS_473));
            const { default: __VLS_477 } = __VLS_475.slots;
            {
                const { icon: __VLS_478 } = __VLS_475.slots;
                let __VLS_479;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_480 = __VLS_asFunctionalComponent1(__VLS_479, new __VLS_479({}));
                const __VLS_481 = __VLS_480({}, ...__VLS_functionalComponentArgsRest(__VLS_480));
                const { default: __VLS_484 } = __VLS_482.slots;
                let __VLS_485;
                /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
                DeleteOutlined;
                // @ts-ignore
                const __VLS_486 = __VLS_asFunctionalComponent1(__VLS_485, new __VLS_485({}));
                const __VLS_487 = __VLS_486({}, ...__VLS_functionalComponentArgsRest(__VLS_486));
                // @ts-ignore
                [];
                var __VLS_482;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_475;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_466;
        var __VLS_467;
        // @ts-ignore
        [];
        var __VLS_425;
        // @ts-ignore
        [];
    }
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
