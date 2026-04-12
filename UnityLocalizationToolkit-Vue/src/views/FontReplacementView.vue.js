import { computed, h, onBeforeUnmount, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NAlert, NButton, NDataTable, NEmpty, NIcon, NProgress, NSelect, NTag, useDialog, useMessage, } from 'naive-ui';
import { ArrowBackOutlined, CloudUploadOutlined, DeleteOutlineOutlined, FontDownloadOutlined, RestoreOutlined, SearchOutlined, } from '@vicons/material';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { api } from '@/api/client';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { formatBytes } from '@/utils/format';
const route = useRoute();
const router = useRouter();
const message = useMessage();
const dialog = useDialog();
const { selectFile } = useFileExplorer();
const gameId = computed(() => route.params.id);
const game = ref(null);
const status = ref(null);
const fonts = ref([]);
const checkedTmpRowKeys = ref([]);
const checkedTtfRowKeys = ref([]);
const selectedSourceIds = ref({});
const scanning = ref(false);
const replacing = ref(false);
const cancelling = ref(false);
const restoring = ref(false);
const progress = ref(null);
const tmpBatchSourceId = ref(null);
const ttfBatchSourceId = ref(null);
const phaseLabels = {
    loading: '加载资源',
    scanning: '扫描中',
    replacing: '替换中',
    saving: '写入文件',
    'clearing-crc': '更新 CRC',
    completed: '已完成',
    failed: '替换失败',
    cancelled: '已取消',
};
let connection = null;
function isTerminalProgressPhase(phase) {
    return phase === 'completed' || phase === 'failed' || phase === 'cancelled';
}
function getCurrentProgress() {
    return progress.value;
}
function fontKey(font) {
    return `${font.assetFile}:${font.pathId}`;
}
function getSourcesByKind(kind) {
    if (!status.value)
        return [];
    return kind === 'TMP' ? status.value.availableSources.tmp : status.value.availableSources.ttf;
}
function getDefaultSourceId(kind) {
    const sources = getSourcesByKind(kind);
    return sources.find(source => source.isDefault)?.id ?? sources[0]?.id ?? null;
}
function getPreferredSourceId(kind, current) {
    const sources = getSourcesByKind(kind);
    if (current && sources.some(source => source.id === current)) {
        return current;
    }
    return getDefaultSourceId(kind);
}
function canReplaceRow(font) {
    return font.replacementSupported && !!getPreferredSourceId(font.fontType);
}
function getStatusText(font) {
    if (!font.replacementSupported) {
        return font.unsupportedReason ?? '不支持替换';
    }
    if (!getPreferredSourceId(font.fontType)) {
        return '当前没有可用的替换源';
    }
    if (font.fontType === 'TTF' && font.ttfMode === 'osFallback') {
        return '可转内嵌替换';
    }
    if (font.fontType === 'TTF' && font.ttfMode === 'dynamicEmbedded') {
        return '可直接替换';
    }
    return '可替换';
}
function getReplacementStateLabel() {
    if (status.value?.isReplaced)
        return '已替换';
    if (status.value?.isExternallyRestored)
        return '检测到外部还原';
    return '未替换';
}
function getReplacementStateClass() {
    if (status.value?.isReplaced)
        return 'status-chip--success';
    if (status.value?.isExternallyRestored)
        return 'status-chip--warning';
    return 'status-chip--neutral';
}
function getBackupStateLabel() {
    return status.value?.backupExists ? '可恢复' : '无备份';
}
function getBackupStateClass() {
    return status.value?.backupExists ? 'status-chip--success' : 'status-chip--neutral';
}
function getTtfModeLabel(mode) {
    switch (mode) {
        case 'dynamicEmbedded':
            return '内嵌动态字体';
        case 'staticAtlas':
            return '静态图集';
        case 'osFallback':
            return '系统回退 / 名称映射';
        case 'unknown':
            return '未知';
        default:
            return '未识别';
    }
}
function getTtfDetails(font) {
    const parts = [];
    if (font.ttfMode === 'osFallback')
        parts.push('可转内嵌');
    if (font.characterRectCount > 0)
        parts.push(`字符矩形 ${font.characterRectCount} 项`);
    if (font.fontNamesCount > 0)
        parts.push(`字体名 ${font.fontNamesCount} 项`);
    if (font.hasTextureRef)
        parts.push('纹理引用');
    return parts.length > 0 ? parts.join(' / ') : '无额外结构';
}
function formatTime(value) {
    if (!value)
        return '未记录';
    return new Date(value).toLocaleString();
}
function formatSourceTime(source) {
    return source.uploadedAt ? new Date(source.uploadedAt).toLocaleString() : '内置';
}
function buildSourceOptions(kind) {
    return getSourcesByKind(kind).map(source => ({
        label: source.isDefault ? `${source.displayName} · 默认` : source.displayName,
        value: source.id,
    }));
}
function setFontSource(font, sourceId) {
    if (!sourceId)
        return;
    selectedSourceIds.value = {
        ...selectedSourceIds.value,
        [fontKey(font)]: sourceId,
    };
}
function reconcileSelections() {
    if (!fonts.value.length) {
        tmpBatchSourceId.value = getPreferredSourceId('TMP', tmpBatchSourceId.value);
        ttfBatchSourceId.value = getPreferredSourceId('TTF', ttfBatchSourceId.value);
        return;
    }
    const nextMap = { ...selectedSourceIds.value };
    for (const font of fonts.value) {
        const preferredSourceId = getPreferredSourceId(font.fontType, nextMap[fontKey(font)]);
        if (preferredSourceId) {
            nextMap[fontKey(font)] = preferredSourceId;
        }
        else {
            delete nextMap[fontKey(font)];
        }
    }
    selectedSourceIds.value = nextMap;
    tmpBatchSourceId.value = getPreferredSourceId('TMP', tmpBatchSourceId.value);
    ttfBatchSourceId.value = getPreferredSourceId('TTF', ttfBatchSourceId.value);
}
function selectSupportedRows() {
    checkedTmpRowKeys.value = fonts.value
        .filter(font => font.fontType === 'TMP' && canReplaceRow(font))
        .map(fontKey);
    checkedTtfRowKeys.value = fonts.value
        .filter(font => font.fontType === 'TTF' && canReplaceRow(font))
        .map(fontKey);
}
const tmpFonts = computed(() => fonts.value.filter(font => font.fontType === 'TMP'));
const ttfFonts = computed(() => fonts.value.filter(font => font.fontType === 'TTF'));
const tmpSourceOptions = computed(() => buildSourceOptions('TMP'));
const ttfSourceOptions = computed(() => buildSourceOptions('TTF'));
const tmpSelectedCount = computed(() => checkedTmpRowKeys.value.length);
const ttfSelectedCount = computed(() => checkedTtfRowKeys.value.length);
const totalSelectedCount = computed(() => tmpSelectedCount.value + ttfSelectedCount.value);
const selectionSummaryText = computed(() => {
    if (totalSelectedCount.value === 0) {
        return '尚未选择需要替换的字体';
    }
    return `已选 TMP ${tmpSelectedCount.value} 个，TTF ${ttfSelectedCount.value} 个`;
});
const usedSourceSummary = computed(() => {
    if (!status.value)
        return '尚未执行替换';
    if (status.value.usedSources.length > 0)
        return status.value.usedSources.join('、');
    return status.value.fontSource || '尚未执行替换';
});
function getSelectedFonts(kind) {
    const keys = new Set(kind === 'TMP'
        ? checkedTmpRowKeys.value
        : kind === 'TTF'
            ? checkedTtfRowKeys.value
            : [...checkedTmpRowKeys.value, ...checkedTtfRowKeys.value]);
    return fonts.value.filter(font => keys.has(fontKey(font)) && canReplaceRow(font));
}
function buildReplaceRequest(kind) {
    const targets = getSelectedFonts(kind).flatMap((font) => {
        const sourceId = selectedSourceIds.value[fontKey(font)];
        if (!sourceId) {
            return [];
        }
        return [{
                pathId: font.pathId,
                assetFile: font.assetFile,
                sourceId,
            }];
    });
    return { fonts: targets };
}
function applyBatchSource(kind) {
    const sourceId = kind === 'TMP' ? tmpBatchSourceId.value : ttfBatchSourceId.value;
    if (!sourceId) {
        message.warning('请先选择要批量应用的替换源');
        return;
    }
    const targets = getSelectedFonts(kind);
    if (targets.length === 0) {
        message.warning('当前分区没有已选中的可替换字体');
        return;
    }
    const nextMap = { ...selectedSourceIds.value };
    for (const font of targets) {
        nextMap[fontKey(font)] = sourceId;
    }
    selectedSourceIds.value = nextMap;
    message.success(`已为 ${targets.length} 个${kind === 'TMP' ? 'TMP' : 'TTF'}字体应用新的替换源`);
}
async function loadGame() {
    try {
        game.value = await api.get(`/api/games/${gameId.value}`);
    }
    catch (error) {
        message.error(error.message);
    }
}
async function loadStatus() {
    try {
        status.value = await api.get(`/api/games/${gameId.value}/font-replacement/status`);
        reconcileSelections();
    }
    catch {
        status.value = null;
    }
}
async function scanFonts() {
    scanning.value = true;
    fonts.value = [];
    checkedTmpRowKeys.value = [];
    checkedTtfRowKeys.value = [];
    progress.value = null;
    try {
        fonts.value = await api.post(`/api/games/${gameId.value}/font-replacement/scan`);
        reconcileSelections();
        selectSupportedRows();
        if (fonts.value.length === 0) {
            message.info('未在游戏资源中找到可识别的字体资源');
        }
        else {
            message.success(`扫描完成，共找到 ${fonts.value.length} 个字体资源`);
        }
    }
    catch (error) {
        message.error(`扫描失败: ${error.message}`);
    }
    finally {
        scanning.value = false;
    }
}
async function doReplace() {
    const request = buildReplaceRequest();
    if (request.fonts.length === 0) {
        message.warning('没有可执行替换的字体');
        return;
    }
    replacing.value = true;
    cancelling.value = false;
    progress.value = null;
    try {
        const result = await api.post(`/api/games/${gameId.value}/font-replacement/replace`, request);
        if (result.failedFonts.length > 0) {
            message.warning(`替换已完成，成功 ${result.successCount} 个，失败 ${result.failedFonts.length} 个`, { duration: 6000 });
        }
        else {
            message.success(`替换完成，共处理 ${result.successCount} 个字体`);
        }
        const currentProgress = getCurrentProgress();
        if (!isTerminalProgressPhase(currentProgress?.phase)) {
            progress.value = {
                phase: 'completed',
                current: request.fonts.length,
                total: request.fonts.length,
            };
        }
        replacing.value = false;
        cancelling.value = false;
        await loadStatus();
        if (fonts.value.length > 0) {
            try {
                fonts.value = await api.post(`/api/games/${gameId.value}/font-replacement/scan`);
                checkedTmpRowKeys.value = [];
                checkedTtfRowKeys.value = [];
                reconcileSelections();
            }
            catch {
                // Keep the replacement result if the follow-up refresh fails.
            }
        }
    }
    catch (error) {
        const isCancelled = error.message === '字体替换已取消';
        const currentProgress = getCurrentProgress();
        if (!isTerminalProgressPhase(currentProgress?.phase)) {
            progress.value = {
                phase: isCancelled ? 'cancelled' : 'failed',
                current: currentProgress?.current ?? 0,
                total: currentProgress?.total || request.fonts.length,
                currentFile: currentProgress?.currentFile,
                message: error.message,
            };
        }
        replacing.value = false;
        cancelling.value = false;
        await loadStatus();
        if (isCancelled) {
            message.info('字体替换已取消');
        }
        else {
            message.error(`替换失败: ${error.message}`);
        }
    }
}
function replaceFonts() {
    const targets = getSelectedFonts();
    if (targets.length === 0) {
        message.warning('请先选择要替换的字体');
        return;
    }
    dialog.warning({
        title: '确认替换',
        content: `将使用当前设置的替换源处理 ${targets.length} 个已选字体，是否继续？`,
        positiveText: '开始替换',
        negativeText: '取消',
        onPositiveClick: () => {
            void doReplace();
        },
    });
}
async function cancelReplacement() {
    if (!replacing.value || cancelling.value)
        return;
    cancelling.value = true;
    progress.value = progress.value
        ? { ...progress.value, message: '正在请求取消，当前阶段结束后生效。' }
        : {
            phase: 'replacing',
            current: 0,
            total: 0,
            message: '正在请求取消，等待后端响应。',
        };
    try {
        await api.post(`/api/games/${gameId.value}/font-replacement/cancel`);
        message.info('已发送取消请求，当前阶段结束后会停止。', { duration: 4000 });
    }
    catch (error) {
        cancelling.value = false;
        progress.value = progress.value
            ? { ...progress.value, message: undefined }
            : null;
        message.error(`取消失败: ${error.message}`);
    }
}
function restoreFonts() {
    dialog.warning({
        title: '确认还原',
        content: '将把已经修改过的字体资源恢复到备份状态，是否继续？',
        positiveText: '还原',
        negativeText: '取消',
        onPositiveClick: async () => {
            restoring.value = true;
            try {
                await api.post(`/api/games/${gameId.value}/font-replacement/restore`);
                message.success('字体资源已还原');
                fonts.value = [];
                checkedTmpRowKeys.value = [];
                checkedTtfRowKeys.value = [];
                selectedSourceIds.value = {};
                await loadStatus();
            }
            catch (error) {
                message.error(`还原失败: ${error.message}`);
            }
            finally {
                restoring.value = false;
            }
        },
    });
}
async function uploadSource(kind) {
    const path = await selectFile({
        title: kind === 'TTF' ? '选择 TTF/OTF 字体' : '选择 TMP 字体资源',
        filters: kind === 'TTF'
            ? [{ label: 'Font Files', extensions: ['.ttf', '.otf'] }]
            : [],
    });
    if (!path)
        return;
    try {
        await api.post(`/api/games/${gameId.value}/font-replacement/upload-from-path`, {
            filePath: path,
            kind: kind.toLowerCase(),
        });
        message.success(`已添加${kind === 'TTF' ? ' TTF/OTF ' : ' TMP '}替换源`);
        await loadStatus();
    }
    catch (error) {
        message.error(error.message || '上传失败');
    }
}
async function deleteSource(source) {
    dialog.warning({
        title: '删除替换源',
        content: `确认删除替换源“${source.displayName}”？`,
        positiveText: '删除',
        negativeText: '取消',
        onPositiveClick: async () => {
            try {
                await api.del(`/api/games/${gameId.value}/font-replacement/custom-fonts/${encodeURIComponent(source.id)}`);
                message.success('替换源已删除');
                await loadStatus();
                reconcileSelections();
            }
            catch (error) {
                message.error(`删除失败: ${error.message}`);
            }
        },
    });
}
function renderSourceSelect(font) {
    return h(NSelect, {
        value: selectedSourceIds.value[fontKey(font)] ?? null,
        options: font.fontType === 'TMP' ? tmpSourceOptions.value : ttfSourceOptions.value,
        disabled: !canReplaceRow(font) || replacing.value,
        size: 'small',
        style: { width: '220px' },
        placeholder: '选择替换源',
        onUpdateValue: (value) => setFontSource(font, value),
    });
}
const tmpColumns = computed(() => [
    {
        type: 'selection',
        disabled: (row) => !canReplaceRow(row),
    },
    {
        title: '字体名称',
        key: 'name',
        minWidth: 180,
        ellipsis: { tooltip: true },
    },
    {
        title: '所在文件',
        key: 'assetFile',
        minWidth: 220,
        ellipsis: { tooltip: true },
    },
    {
        title: '图集',
        key: 'atlas',
        width: 120,
        render: (row) => row.atlasWidth > 0 ? `${row.atlasWidth} × ${row.atlasHeight}` : '—',
    },
    {
        title: '字形数',
        key: 'glyphCount',
        width: 90,
    },
    {
        title: '字符数',
        key: 'characterCount',
        width: 90,
    },
    {
        title: '替换为',
        key: 'replaceSource',
        width: 250,
        render: (row) => renderSourceSelect(row),
    },
    {
        title: '状态',
        key: 'status',
        minWidth: 220,
        ellipsis: { tooltip: true },
        render: (row) => h(NTag, {
            size: 'small',
            bordered: false,
            type: canReplaceRow(row) ? 'success' : 'warning',
        }, {
            default: () => getStatusText(row),
        }),
    },
]);
const ttfColumns = computed(() => [
    {
        type: 'selection',
        disabled: (row) => !canReplaceRow(row),
    },
    {
        title: '字体名称',
        key: 'name',
        minWidth: 180,
        ellipsis: { tooltip: true },
    },
    {
        title: 'TTF 模式',
        key: 'ttfMode',
        width: 140,
        render: (row) => h(NTag, {
            size: 'small',
            bordered: false,
            type: row.ttfMode === 'dynamicEmbedded'
                ? 'success'
                : row.ttfMode === 'osFallback'
                    ? 'info'
                    : 'warning',
        }, { default: () => getTtfModeLabel(row.ttfMode) }),
    },
    {
        title: '所在文件',
        key: 'assetFile',
        minWidth: 220,
        ellipsis: { tooltip: true },
    },
    {
        title: '内嵌大小',
        key: 'fontDataSize',
        width: 120,
        render: (row) => row.fontDataSize > 0 ? formatBytes(row.fontDataSize) : '0 B',
    },
    {
        title: '结构说明',
        key: 'ttfDetails',
        width: 180,
        render: (row) => getTtfDetails(row),
    },
    {
        title: '替换为',
        key: 'replaceSource',
        width: 250,
        render: (row) => renderSourceSelect(row),
    },
    {
        title: '状态',
        key: 'status',
        minWidth: 240,
        ellipsis: { tooltip: true },
        render: (row) => h(NTag, {
            size: 'small',
            bordered: false,
            type: canReplaceRow(row) ? 'success' : 'warning',
        }, {
            default: () => getStatusText(row),
        }),
    },
]);
const rowClassName = (row) => (canReplaceRow(row) ? '' : 'font-disabled');
onMounted(async () => {
    await loadGame();
    await loadStatus();
    try {
        connection = new HubConnectionBuilder()
            .withUrl('/hubs/install')
            .withAutomaticReconnect()
            .build();
        connection.on('fontReplacementProgress', (payload) => {
            progress.value = payload;
            if (isTerminalProgressPhase(payload.phase)) {
                replacing.value = false;
                cancelling.value = false;
            }
        });
        connection.onreconnected(async () => {
            try {
                await connection?.invoke('JoinFontReplacementGroup', gameId.value);
            }
            catch {
                // Ignore reconnect errors.
            }
        });
        await connection.start();
        await connection.invoke('JoinFontReplacementGroup', gameId.value);
    }
    catch {
        // Ignore SignalR connection failures.
    }
});
onBeforeUnmount(async () => {
    try {
        if (connection) {
            if (connection.state === HubConnectionState.Connected) {
                await connection.invoke('LeaveFontReplacementGroup', gameId.value);
            }
            await connection.stop();
        }
    }
    catch {
        // Ignore shutdown errors.
    }
    connection = null;
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['status-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['source-group-header']} */ ;
/** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
/** @type {__VLS_StyleScopedClasses['status-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['source-library']} */ ;
/** @type {__VLS_StyleScopedClasses['status-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['source-library']} */ ;
/** @type {__VLS_StyleScopedClasses['selection-hint']} */ ;
/** @type {__VLS_StyleScopedClasses['source-item']} */ ;
if (__VLS_ctx.game) {
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
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}`);
                // @ts-ignore
                [game, router, gameId,];
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
    (__VLS_ctx.game.name);
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
    /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
    FontDownloadOutlined;
    // @ts-ignore
    const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
    const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
    // @ts-ignore
    [game,];
    var __VLS_14;
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
    /** @ts-ignore @type {typeof __VLS_components.RestoreOutlined} */
    RestoreOutlined;
    // @ts-ignore
    const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
    const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
    // @ts-ignore
    [];
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
        loading: (__VLS_ctx.restoring),
        disabled: (!__VLS_ctx.status?.backupExists || __VLS_ctx.replacing),
    }));
    const __VLS_35 = __VLS_34({
        ...{ 'onClick': {} },
        size: "small",
        loading: (__VLS_ctx.restoring),
        disabled: (!__VLS_ctx.status?.backupExists || __VLS_ctx.replacing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_34));
    let __VLS_38;
    const __VLS_39 = ({ click: {} },
        { onClick: (__VLS_ctx.restoreFonts) });
    const { default: __VLS_40 } = __VLS_36.slots;
    {
        const { icon: __VLS_41 } = __VLS_36.slots;
        let __VLS_42;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
            size: (16),
        }));
        const __VLS_44 = __VLS_43({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_43));
        const { default: __VLS_47 } = __VLS_45.slots;
        let __VLS_48;
        /** @ts-ignore @type {typeof __VLS_components.RestoreOutlined} */
        RestoreOutlined;
        // @ts-ignore
        const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({}));
        const __VLS_50 = __VLS_49({}, ...__VLS_functionalComponentArgsRest(__VLS_49));
        // @ts-ignore
        [restoring, status, replacing, restoreFonts,];
        var __VLS_45;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_36;
    var __VLS_37;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-grid" },
    });
    /** @type {__VLS_StyleScopedClasses['status-grid']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-block" },
    });
    /** @type {__VLS_StyleScopedClasses['status-block']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-label" },
    });
    /** @type {__VLS_StyleScopedClasses['status-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-value" },
    });
    /** @type {__VLS_StyleScopedClasses['status-value']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-chip" },
        ...{ class: (__VLS_ctx.getReplacementStateClass()) },
    });
    /** @type {__VLS_StyleScopedClasses['status-chip']} */ ;
    (__VLS_ctx.getReplacementStateLabel());
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-text" },
    });
    /** @type {__VLS_StyleScopedClasses['status-text']} */ ;
    (__VLS_ctx.status?.isReplaced
        ? `上次替换时间 ${__VLS_ctx.formatTime(__VLS_ctx.status.replacedAt)}`
        : __VLS_ctx.status?.isExternallyRestored
            ? '资源文件可能已由外部工具或平台验证恢复'
            : '当前没有执行中的替换记录');
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-block" },
    });
    /** @type {__VLS_StyleScopedClasses['status-block']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-label" },
    });
    /** @type {__VLS_StyleScopedClasses['status-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-value" },
    });
    /** @type {__VLS_StyleScopedClasses['status-value']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-text" },
    });
    /** @type {__VLS_StyleScopedClasses['status-text']} */ ;
    (__VLS_ctx.usedSourceSummary);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-block" },
    });
    /** @type {__VLS_StyleScopedClasses['status-block']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-label" },
    });
    /** @type {__VLS_StyleScopedClasses['status-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-value" },
    });
    /** @type {__VLS_StyleScopedClasses['status-value']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-chip" },
        ...{ class: (__VLS_ctx.getBackupStateClass()) },
    });
    /** @type {__VLS_StyleScopedClasses['status-chip']} */ ;
    (__VLS_ctx.getBackupStateLabel());
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "status-text" },
    });
    /** @type {__VLS_StyleScopedClasses['status-text']} */ ;
    (__VLS_ctx.status?.backupExists ? '已检测到字体替换备份文件' : '执行替换后会自动创建备份');
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
    let __VLS_53;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
        size: (16),
    }));
    const __VLS_55 = __VLS_54({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_54));
    const { default: __VLS_58 } = __VLS_56.slots;
    let __VLS_59;
    /** @ts-ignore @type {typeof __VLS_components.CloudUploadOutlined} */
    CloudUploadOutlined;
    // @ts-ignore
    const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({}));
    const __VLS_61 = __VLS_60({}, ...__VLS_functionalComponentArgsRest(__VLS_60));
    // @ts-ignore
    [status, status, status, status, getReplacementStateClass, getReplacementStateLabel, formatTime, usedSourceSummary, getBackupStateClass, getBackupStateLabel,];
    var __VLS_56;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        ...{ 'onClick': {} },
        size: "small",
        disabled: (__VLS_ctx.replacing),
    }));
    const __VLS_66 = __VLS_65({
        ...{ 'onClick': {} },
        size: "small",
        disabled: (__VLS_ctx.replacing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
    let __VLS_69;
    const __VLS_70 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.uploadSource('TTF');
                // @ts-ignore
                [replacing, uploadSource,];
            } });
    const { default: __VLS_71 } = __VLS_67.slots;
    {
        const { icon: __VLS_72 } = __VLS_67.slots;
        let __VLS_73;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
            size: (16),
        }));
        const __VLS_75 = __VLS_74({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_74));
        const { default: __VLS_78 } = __VLS_76.slots;
        let __VLS_79;
        /** @ts-ignore @type {typeof __VLS_components.CloudUploadOutlined} */
        CloudUploadOutlined;
        // @ts-ignore
        const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({}));
        const __VLS_81 = __VLS_80({}, ...__VLS_functionalComponentArgsRest(__VLS_80));
        // @ts-ignore
        [];
        var __VLS_76;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_67;
    var __VLS_68;
    let __VLS_84;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({
        ...{ 'onClick': {} },
        size: "small",
        disabled: (__VLS_ctx.replacing),
    }));
    const __VLS_86 = __VLS_85({
        ...{ 'onClick': {} },
        size: "small",
        disabled: (__VLS_ctx.replacing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_85));
    let __VLS_89;
    const __VLS_90 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.uploadSource('TMP');
                // @ts-ignore
                [replacing, uploadSource,];
            } });
    const { default: __VLS_91 } = __VLS_87.slots;
    {
        const { icon: __VLS_92 } = __VLS_87.slots;
        let __VLS_93;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
            size: (16),
        }));
        const __VLS_95 = __VLS_94({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_94));
        const { default: __VLS_98 } = __VLS_96.slots;
        let __VLS_99;
        /** @ts-ignore @type {typeof __VLS_components.CloudUploadOutlined} */
        CloudUploadOutlined;
        // @ts-ignore
        const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({}));
        const __VLS_101 = __VLS_100({}, ...__VLS_functionalComponentArgsRest(__VLS_100));
        // @ts-ignore
        [];
        var __VLS_96;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_87;
    var __VLS_88;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "source-library" },
    });
    /** @type {__VLS_StyleScopedClasses['source-library']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.section, __VLS_intrinsics.section)({
        ...{ class: "source-group" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "source-group-header" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "source-group-meta" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group-meta']} */ ;
    (__VLS_ctx.getSourcesByKind('TMP').length);
    if (__VLS_ctx.getSourcesByKind('TMP').length > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "source-list" },
        });
        /** @type {__VLS_StyleScopedClasses['source-list']} */ ;
        for (const [source] of __VLS_vFor((__VLS_ctx.getSourcesByKind('TMP')))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (source.id),
                ...{ class: "source-item" },
            });
            /** @type {__VLS_StyleScopedClasses['source-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-main" },
            });
            /** @type {__VLS_StyleScopedClasses['source-main']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-title-line" },
            });
            /** @type {__VLS_StyleScopedClasses['source-title-line']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "source-title" },
            });
            /** @type {__VLS_StyleScopedClasses['source-title']} */ ;
            (source.displayName);
            let __VLS_104;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_105 = __VLS_asFunctionalComponent1(__VLS_104, new __VLS_104({
                size: "small",
                bordered: (false),
                type: (source.isDefault ? 'success' : 'default'),
            }));
            const __VLS_106 = __VLS_105({
                size: "small",
                bordered: (false),
                type: (source.isDefault ? 'success' : 'default'),
            }, ...__VLS_functionalComponentArgsRest(__VLS_105));
            const { default: __VLS_109 } = __VLS_107.slots;
            (source.isDefault ? '默认' : '自定义');
            // @ts-ignore
            [getSourcesByKind, getSourcesByKind, getSourcesByKind,];
            var __VLS_107;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-meta" },
            });
            /** @type {__VLS_StyleScopedClasses['source-meta']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (source.fileName);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.formatBytes(source.fileSize));
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.formatSourceTime(source));
            if (!source.isDefault) {
                let __VLS_110;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_111 = __VLS_asFunctionalComponent1(__VLS_110, new __VLS_110({
                    ...{ 'onClick': {} },
                    text: true,
                    size: "small",
                    type: "error",
                }));
                const __VLS_112 = __VLS_111({
                    ...{ 'onClick': {} },
                    text: true,
                    size: "small",
                    type: "error",
                }, ...__VLS_functionalComponentArgsRest(__VLS_111));
                let __VLS_115;
                const __VLS_116 = ({ click: {} },
                    { onClick: (...[$event]) => {
                            if (!(__VLS_ctx.game))
                                return;
                            if (!(__VLS_ctx.getSourcesByKind('TMP').length > 0))
                                return;
                            if (!(!source.isDefault))
                                return;
                            __VLS_ctx.deleteSource(source);
                            // @ts-ignore
                            [formatBytes, formatSourceTime, deleteSource,];
                        } });
                const { default: __VLS_117 } = __VLS_113.slots;
                {
                    const { icon: __VLS_118 } = __VLS_113.slots;
                    let __VLS_119;
                    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                    NIcon;
                    // @ts-ignore
                    const __VLS_120 = __VLS_asFunctionalComponent1(__VLS_119, new __VLS_119({
                        size: (14),
                    }));
                    const __VLS_121 = __VLS_120({
                        size: (14),
                    }, ...__VLS_functionalComponentArgsRest(__VLS_120));
                    const { default: __VLS_124 } = __VLS_122.slots;
                    let __VLS_125;
                    /** @ts-ignore @type {typeof __VLS_components.DeleteOutlineOutlined} */
                    DeleteOutlineOutlined;
                    // @ts-ignore
                    const __VLS_126 = __VLS_asFunctionalComponent1(__VLS_125, new __VLS_125({}));
                    const __VLS_127 = __VLS_126({}, ...__VLS_functionalComponentArgsRest(__VLS_126));
                    // @ts-ignore
                    [];
                    var __VLS_122;
                    // @ts-ignore
                    [];
                }
                // @ts-ignore
                [];
                var __VLS_113;
                var __VLS_114;
            }
            // @ts-ignore
            [];
        }
    }
    else {
        let __VLS_130;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_131 = __VLS_asFunctionalComponent1(__VLS_130, new __VLS_130({
            size: "small",
            description: "没有可用的 TMP 替换源",
        }));
        const __VLS_132 = __VLS_131({
            size: "small",
            description: "没有可用的 TMP 替换源",
        }, ...__VLS_functionalComponentArgsRest(__VLS_131));
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.section, __VLS_intrinsics.section)({
        ...{ class: "source-group" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "source-group-header" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "source-group-meta" },
    });
    /** @type {__VLS_StyleScopedClasses['source-group-meta']} */ ;
    (__VLS_ctx.getSourcesByKind('TTF').length);
    if (__VLS_ctx.getSourcesByKind('TTF').length > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "source-list" },
        });
        /** @type {__VLS_StyleScopedClasses['source-list']} */ ;
        for (const [source] of __VLS_vFor((__VLS_ctx.getSourcesByKind('TTF')))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (source.id),
                ...{ class: "source-item" },
            });
            /** @type {__VLS_StyleScopedClasses['source-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-main" },
            });
            /** @type {__VLS_StyleScopedClasses['source-main']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-title-line" },
            });
            /** @type {__VLS_StyleScopedClasses['source-title-line']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "source-title" },
            });
            /** @type {__VLS_StyleScopedClasses['source-title']} */ ;
            (source.displayName);
            let __VLS_135;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_136 = __VLS_asFunctionalComponent1(__VLS_135, new __VLS_135({
                size: "small",
                bordered: (false),
                type: (source.isDefault ? 'success' : 'default'),
            }));
            const __VLS_137 = __VLS_136({
                size: "small",
                bordered: (false),
                type: (source.isDefault ? 'success' : 'default'),
            }, ...__VLS_functionalComponentArgsRest(__VLS_136));
            const { default: __VLS_140 } = __VLS_138.slots;
            (source.isDefault ? '默认' : '自定义');
            // @ts-ignore
            [getSourcesByKind, getSourcesByKind, getSourcesByKind,];
            var __VLS_138;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "source-meta" },
            });
            /** @type {__VLS_StyleScopedClasses['source-meta']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (source.fileName);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.formatBytes(source.fileSize));
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.formatSourceTime(source));
            if (!source.isDefault) {
                let __VLS_141;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_142 = __VLS_asFunctionalComponent1(__VLS_141, new __VLS_141({
                    ...{ 'onClick': {} },
                    text: true,
                    size: "small",
                    type: "error",
                }));
                const __VLS_143 = __VLS_142({
                    ...{ 'onClick': {} },
                    text: true,
                    size: "small",
                    type: "error",
                }, ...__VLS_functionalComponentArgsRest(__VLS_142));
                let __VLS_146;
                const __VLS_147 = ({ click: {} },
                    { onClick: (...[$event]) => {
                            if (!(__VLS_ctx.game))
                                return;
                            if (!(__VLS_ctx.getSourcesByKind('TTF').length > 0))
                                return;
                            if (!(!source.isDefault))
                                return;
                            __VLS_ctx.deleteSource(source);
                            // @ts-ignore
                            [formatBytes, formatSourceTime, deleteSource,];
                        } });
                const { default: __VLS_148 } = __VLS_144.slots;
                {
                    const { icon: __VLS_149 } = __VLS_144.slots;
                    let __VLS_150;
                    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                    NIcon;
                    // @ts-ignore
                    const __VLS_151 = __VLS_asFunctionalComponent1(__VLS_150, new __VLS_150({
                        size: (14),
                    }));
                    const __VLS_152 = __VLS_151({
                        size: (14),
                    }, ...__VLS_functionalComponentArgsRest(__VLS_151));
                    const { default: __VLS_155 } = __VLS_153.slots;
                    let __VLS_156;
                    /** @ts-ignore @type {typeof __VLS_components.DeleteOutlineOutlined} */
                    DeleteOutlineOutlined;
                    // @ts-ignore
                    const __VLS_157 = __VLS_asFunctionalComponent1(__VLS_156, new __VLS_156({}));
                    const __VLS_158 = __VLS_157({}, ...__VLS_functionalComponentArgsRest(__VLS_157));
                    // @ts-ignore
                    [];
                    var __VLS_153;
                    // @ts-ignore
                    [];
                }
                // @ts-ignore
                [];
                var __VLS_144;
                var __VLS_145;
            }
            // @ts-ignore
            [];
        }
    }
    else {
        let __VLS_161;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_162 = __VLS_asFunctionalComponent1(__VLS_161, new __VLS_161({
            size: "small",
            description: "没有可用的 TTF / OTF 替换源",
        }));
        const __VLS_163 = __VLS_162({
            size: "small",
            description: "没有可用的 TTF / OTF 替换源",
        }, ...__VLS_functionalComponentArgsRest(__VLS_162));
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
    let __VLS_166;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_167 = __VLS_asFunctionalComponent1(__VLS_166, new __VLS_166({
        size: (16),
    }));
    const __VLS_168 = __VLS_167({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_167));
    const { default: __VLS_171 } = __VLS_169.slots;
    let __VLS_172;
    /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
    SearchOutlined;
    // @ts-ignore
    const __VLS_173 = __VLS_asFunctionalComponent1(__VLS_172, new __VLS_172({}));
    const __VLS_174 = __VLS_173({}, ...__VLS_functionalComponentArgsRest(__VLS_173));
    // @ts-ignore
    [];
    var __VLS_169;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "selection-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['selection-hint']} */ ;
    (__VLS_ctx.selectionSummaryText);
    let __VLS_177;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_178 = __VLS_asFunctionalComponent1(__VLS_177, new __VLS_177({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.replacing),
        disabled: (__VLS_ctx.totalSelectedCount === 0 || __VLS_ctx.scanning || __VLS_ctx.replacing),
    }));
    const __VLS_179 = __VLS_178({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.replacing),
        disabled: (__VLS_ctx.totalSelectedCount === 0 || __VLS_ctx.scanning || __VLS_ctx.replacing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_178));
    let __VLS_182;
    const __VLS_183 = ({ click: {} },
        { onClick: (__VLS_ctx.replaceFonts) });
    const { default: __VLS_184 } = __VLS_180.slots;
    (__VLS_ctx.totalSelectedCount);
    // @ts-ignore
    [replacing, replacing, selectionSummaryText, totalSelectedCount, totalSelectedCount, scanning, replaceFonts,];
    var __VLS_180;
    var __VLS_181;
    if (__VLS_ctx.replacing) {
        let __VLS_185;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_186 = __VLS_asFunctionalComponent1(__VLS_185, new __VLS_185({
            ...{ 'onClick': {} },
            size: "small",
            type: "warning",
            loading: (__VLS_ctx.cancelling),
            disabled: (__VLS_ctx.cancelling),
        }));
        const __VLS_187 = __VLS_186({
            ...{ 'onClick': {} },
            size: "small",
            type: "warning",
            loading: (__VLS_ctx.cancelling),
            disabled: (__VLS_ctx.cancelling),
        }, ...__VLS_functionalComponentArgsRest(__VLS_186));
        let __VLS_190;
        const __VLS_191 = ({ click: {} },
            { onClick: (__VLS_ctx.cancelReplacement) });
        const { default: __VLS_192 } = __VLS_188.slots;
        (__VLS_ctx.cancelling ? '正在取消…' : '取消替换');
        // @ts-ignore
        [replacing, cancelling, cancelling, cancelling, cancelReplacement,];
        var __VLS_188;
        var __VLS_189;
    }
    let __VLS_193;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_194 = __VLS_asFunctionalComponent1(__VLS_193, new __VLS_193({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.scanning),
        disabled: (__VLS_ctx.replacing),
    }));
    const __VLS_195 = __VLS_194({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.scanning),
        disabled: (__VLS_ctx.replacing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_194));
    let __VLS_198;
    const __VLS_199 = ({ click: {} },
        { onClick: (__VLS_ctx.scanFonts) });
    const { default: __VLS_200 } = __VLS_196.slots;
    {
        const { icon: __VLS_201 } = __VLS_196.slots;
        let __VLS_202;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_203 = __VLS_asFunctionalComponent1(__VLS_202, new __VLS_202({
            size: (16),
        }));
        const __VLS_204 = __VLS_203({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_203));
        const { default: __VLS_207 } = __VLS_205.slots;
        let __VLS_208;
        /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
        SearchOutlined;
        // @ts-ignore
        const __VLS_209 = __VLS_asFunctionalComponent1(__VLS_208, new __VLS_208({}));
        const __VLS_210 = __VLS_209({}, ...__VLS_functionalComponentArgsRest(__VLS_209));
        // @ts-ignore
        [replacing, scanning, scanFonts,];
        var __VLS_205;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_196;
    var __VLS_197;
    if (__VLS_ctx.progress) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "progress-section" },
        });
        /** @type {__VLS_StyleScopedClasses['progress-section']} */ ;
        let __VLS_213;
        /** @ts-ignore @type {typeof __VLS_components.NProgress} */
        NProgress;
        // @ts-ignore
        const __VLS_214 = __VLS_asFunctionalComponent1(__VLS_213, new __VLS_213({
            type: "line",
            percentage: (__VLS_ctx.progress.total > 0 ? Math.round((__VLS_ctx.progress.current / __VLS_ctx.progress.total) * 100) : 0),
        }));
        const __VLS_215 = __VLS_214({
            type: "line",
            percentage: (__VLS_ctx.progress.total > 0 ? Math.round((__VLS_ctx.progress.current / __VLS_ctx.progress.total) * 100) : 0),
        }, ...__VLS_functionalComponentArgsRest(__VLS_214));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "progress-text" },
        });
        /** @type {__VLS_StyleScopedClasses['progress-text']} */ ;
        (__VLS_ctx.phaseLabels[__VLS_ctx.progress.phase] || __VLS_ctx.progress.phase);
        (__VLS_ctx.progress.currentFile ? ` · ${__VLS_ctx.progress.currentFile}` : '');
        (__VLS_ctx.progress.current);
        (__VLS_ctx.progress.total);
        if (__VLS_ctx.progress.message) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "progress-message" },
            });
            /** @type {__VLS_StyleScopedClasses['progress-message']} */ ;
            (__VLS_ctx.progress.message);
        }
    }
    if (__VLS_ctx.fonts.length === 0 && !__VLS_ctx.progress) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "empty-state" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
    }
    if (__VLS_ctx.fonts.length > 0) {
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
        let __VLS_218;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_219 = __VLS_asFunctionalComponent1(__VLS_218, new __VLS_218({
            size: (16),
        }));
        const __VLS_220 = __VLS_219({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_219));
        const { default: __VLS_223 } = __VLS_221.slots;
        let __VLS_224;
        /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
        FontDownloadOutlined;
        // @ts-ignore
        const __VLS_225 = __VLS_asFunctionalComponent1(__VLS_224, new __VLS_224({}));
        const __VLS_226 = __VLS_225({}, ...__VLS_functionalComponentArgsRest(__VLS_225));
        // @ts-ignore
        [progress, progress, progress, progress, progress, progress, progress, progress, progress, progress, progress, progress, progress, phaseLabels, fonts, fonts,];
        var __VLS_221;
        let __VLS_229;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_230 = __VLS_asFunctionalComponent1(__VLS_229, new __VLS_229({
            size: "small",
            bordered: (false),
        }));
        const __VLS_231 = __VLS_230({
            size: "small",
            bordered: (false),
        }, ...__VLS_functionalComponentArgsRest(__VLS_230));
        const { default: __VLS_234 } = __VLS_232.slots;
        (__VLS_ctx.tmpFonts.length);
        // @ts-ignore
        [tmpFonts,];
        var __VLS_232;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "header-actions group-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
        /** @type {__VLS_StyleScopedClasses['group-actions']} */ ;
        let __VLS_235;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_236 = __VLS_asFunctionalComponent1(__VLS_235, new __VLS_235({
            value: (__VLS_ctx.tmpBatchSourceId),
            size: "small",
            options: (__VLS_ctx.tmpSourceOptions),
            placeholder: "批量设置替换源",
            ...{ style: {} },
        }));
        const __VLS_237 = __VLS_236({
            value: (__VLS_ctx.tmpBatchSourceId),
            size: "small",
            options: (__VLS_ctx.tmpSourceOptions),
            placeholder: "批量设置替换源",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_236));
        let __VLS_240;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_241 = __VLS_asFunctionalComponent1(__VLS_240, new __VLS_240({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.tmpSelectedCount === 0),
        }));
        const __VLS_242 = __VLS_241({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.tmpSelectedCount === 0),
        }, ...__VLS_functionalComponentArgsRest(__VLS_241));
        let __VLS_245;
        const __VLS_246 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.fonts.length > 0))
                        return;
                    __VLS_ctx.applyBatchSource('TMP');
                    // @ts-ignore
                    [tmpBatchSourceId, tmpSourceOptions, tmpSelectedCount, applyBatchSource,];
                } });
        const { default: __VLS_247 } = __VLS_243.slots;
        // @ts-ignore
        [];
        var __VLS_243;
        var __VLS_244;
        if (__VLS_ctx.tmpFonts.length > 0) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "table-container" },
            });
            /** @type {__VLS_StyleScopedClasses['table-container']} */ ;
            let __VLS_248;
            /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
            NDataTable;
            // @ts-ignore
            const __VLS_249 = __VLS_asFunctionalComponent1(__VLS_248, new __VLS_248({
                checkedRowKeys: (__VLS_ctx.checkedTmpRowKeys),
                columns: (__VLS_ctx.tmpColumns),
                data: (__VLS_ctx.tmpFonts),
                rowKey: (__VLS_ctx.fontKey),
                rowClassName: (__VLS_ctx.rowClassName),
                maxHeight: (420),
                bordered: (false),
                size: "small",
            }));
            const __VLS_250 = __VLS_249({
                checkedRowKeys: (__VLS_ctx.checkedTmpRowKeys),
                columns: (__VLS_ctx.tmpColumns),
                data: (__VLS_ctx.tmpFonts),
                rowKey: (__VLS_ctx.fontKey),
                rowClassName: (__VLS_ctx.rowClassName),
                maxHeight: (420),
                bordered: (false),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_249));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "empty-state compact" },
            });
            /** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
            /** @type {__VLS_StyleScopedClasses['compact']} */ ;
        }
    }
    if (__VLS_ctx.fonts.length > 0) {
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
        let __VLS_253;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_254 = __VLS_asFunctionalComponent1(__VLS_253, new __VLS_253({
            size: (16),
        }));
        const __VLS_255 = __VLS_254({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_254));
        const { default: __VLS_258 } = __VLS_256.slots;
        let __VLS_259;
        /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
        FontDownloadOutlined;
        // @ts-ignore
        const __VLS_260 = __VLS_asFunctionalComponent1(__VLS_259, new __VLS_259({}));
        const __VLS_261 = __VLS_260({}, ...__VLS_functionalComponentArgsRest(__VLS_260));
        // @ts-ignore
        [fonts, tmpFonts, tmpFonts, checkedTmpRowKeys, tmpColumns, fontKey, rowClassName,];
        var __VLS_256;
        let __VLS_264;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_265 = __VLS_asFunctionalComponent1(__VLS_264, new __VLS_264({
            size: "small",
            bordered: (false),
        }));
        const __VLS_266 = __VLS_265({
            size: "small",
            bordered: (false),
        }, ...__VLS_functionalComponentArgsRest(__VLS_265));
        const { default: __VLS_269 } = __VLS_267.slots;
        (__VLS_ctx.ttfFonts.length);
        // @ts-ignore
        [ttfFonts,];
        var __VLS_267;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "header-actions group-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
        /** @type {__VLS_StyleScopedClasses['group-actions']} */ ;
        let __VLS_270;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_271 = __VLS_asFunctionalComponent1(__VLS_270, new __VLS_270({
            value: (__VLS_ctx.ttfBatchSourceId),
            size: "small",
            options: (__VLS_ctx.ttfSourceOptions),
            placeholder: "批量设置替换源",
            ...{ style: {} },
        }));
        const __VLS_272 = __VLS_271({
            value: (__VLS_ctx.ttfBatchSourceId),
            size: "small",
            options: (__VLS_ctx.ttfSourceOptions),
            placeholder: "批量设置替换源",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_271));
        let __VLS_275;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_276 = __VLS_asFunctionalComponent1(__VLS_275, new __VLS_275({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.ttfSelectedCount === 0),
        }));
        const __VLS_277 = __VLS_276({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.ttfSelectedCount === 0),
        }, ...__VLS_functionalComponentArgsRest(__VLS_276));
        let __VLS_280;
        const __VLS_281 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.fonts.length > 0))
                        return;
                    __VLS_ctx.applyBatchSource('TTF');
                    // @ts-ignore
                    [applyBatchSource, ttfBatchSourceId, ttfSourceOptions, ttfSelectedCount,];
                } });
        const { default: __VLS_282 } = __VLS_278.slots;
        // @ts-ignore
        [];
        var __VLS_278;
        var __VLS_279;
        let __VLS_283;
        /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
        NAlert;
        // @ts-ignore
        const __VLS_284 = __VLS_asFunctionalComponent1(__VLS_283, new __VLS_283({
            type: "info",
            showIcon: (false),
            ...{ class: "mode-note" },
        }));
        const __VLS_285 = __VLS_284({
            type: "info",
            showIcon: (false),
            ...{ class: "mode-note" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_284));
        /** @type {__VLS_StyleScopedClasses['mode-note']} */ ;
        const { default: __VLS_288 } = __VLS_286.slots;
        // @ts-ignore
        [];
        var __VLS_286;
        if (__VLS_ctx.ttfFonts.length > 0) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "table-container" },
            });
            /** @type {__VLS_StyleScopedClasses['table-container']} */ ;
            let __VLS_289;
            /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
            NDataTable;
            // @ts-ignore
            const __VLS_290 = __VLS_asFunctionalComponent1(__VLS_289, new __VLS_289({
                checkedRowKeys: (__VLS_ctx.checkedTtfRowKeys),
                columns: (__VLS_ctx.ttfColumns),
                data: (__VLS_ctx.ttfFonts),
                rowKey: (__VLS_ctx.fontKey),
                rowClassName: (__VLS_ctx.rowClassName),
                maxHeight: (420),
                bordered: (false),
                size: "small",
            }));
            const __VLS_291 = __VLS_290({
                checkedRowKeys: (__VLS_ctx.checkedTtfRowKeys),
                columns: (__VLS_ctx.ttfColumns),
                data: (__VLS_ctx.ttfFonts),
                rowKey: (__VLS_ctx.fontKey),
                rowClassName: (__VLS_ctx.rowClassName),
                maxHeight: (420),
                bordered: (false),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_290));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "empty-state compact" },
            });
            /** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
            /** @type {__VLS_StyleScopedClasses['compact']} */ ;
        }
    }
}
// @ts-ignore
[fontKey, rowClassName, ttfFonts, ttfFonts, checkedTtfRowKeys, ttfColumns,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
