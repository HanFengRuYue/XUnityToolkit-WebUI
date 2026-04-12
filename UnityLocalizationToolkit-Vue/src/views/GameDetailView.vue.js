import { ref, reactive, h, onMounted, onBeforeUnmount, computed, watch, nextTick } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NAlert, NIcon, NInput, NDropdown, NSwitch, useMessage, useDialog, } from 'naive-ui';
import { GamepadFilled, FolderOpenOutlined, PlayArrowFilled, InfoOutlined, FolderOutlined, InsertDriveFileOutlined, CodeOutlined, MemoryOutlined, GetAppOutlined, TranslateOutlined, WarningAmberOutlined, ExtensionOutlined, WidgetsOutlined, DataObjectOutlined, SmartToyOutlined, MenuBookOutlined, DescriptionOutlined, PhotoCameraOutlined, DriveFileRenameOutlineOutlined, CloudUploadOutlined, DeleteOutlineOutlined, Inventory2Outlined, FileUploadOutlined, ImageSearchOutlined, WallpaperOutlined, FontDownloadOutlined, ArticleOutlined, ExpandMoreOutlined, RefreshOutlined, } from '@vicons/material';
import { useGamesStore } from '@/stores/games';
import { useInstallStore } from '@/stores/install';
import { gamesApi, settingsApi, pluginPackageApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import ConfigPanel from '@/components/config/ConfigPanel.vue';
import PluginHealthCard from '@/components/health/PluginHealthCard.vue';
import { useAutoSave } from '@/composables/useAutoSave';
import { defineAsyncComponent } from 'vue';
const CoverPickerModal = defineAsyncComponent(() => import('@/components/library/CoverPickerModal.vue'));
const IconPickerModal = defineAsyncComponent(() => import('@/components/library/IconPickerModal.vue'));
const BackgroundPickerModal = defineAsyncComponent(() => import('@/components/library/BackgroundPickerModal.vue'));
const collapsed = reactive({ config: true, description: true, tools: false, installOptions: true });
const route = useRoute();
const router = useRouter();
const gamesStore = useGamesStore();
const installStore = useInstallStore();
const message = useMessage();
const dialog = useDialog();
const { selectFile } = useFileExplorer();
const gameId = route.params['id'];
const game = ref(null);
const config = ref(null);
const loading = ref(true);
const aiEndpointInstalled = ref(null);
const aiEndpointLoading = ref(false);
const aiDescription = ref('');
const showCoverPicker = ref(false);
const showIconPicker = ref(false);
const showBackgroundPicker = ref(false);
const packageExporting = ref(false);
const packageImporting = ref(false);
const installOptions = ref({
    autoInstallTmpFont: true,
    autoDeployAiEndpoint: true,
    autoGenerateConfig: true,
    autoApplyOptimalConfig: true,
    autoExtractAssets: true,
    autoVerifyHealth: true,
});
// Name editing state
const editingName = ref(false);
const editNameValue = ref('');
// Icon context menu state
const showIconContextMenu = ref(false);
const iconContextMenuX = ref(0);
const iconContextMenuY = ref(0);
const iconContextMenuOptions = [
    { label: '更换封面', key: 'cover', icon: () => h(NIcon, { size: 16 }, { default: () => h(PhotoCameraOutlined) }) },
    { label: '更换背景', key: 'background', icon: () => h(NIcon, { size: 16 }, { default: () => h(WallpaperOutlined) }) },
    { label: '搜索图标', key: 'web-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(ImageSearchOutlined) }) },
    { label: '上传自定义图标', key: 'upload-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(CloudUploadOutlined) }) },
    { label: '删除自定义图标', key: 'delete-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
    { label: '删除背景图', key: 'delete-background', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
];
const iconUrl = computed(() => game.value ? `/api/games/${gameId}/icon?t=${game.value.updatedAt}` : '');
const bgTimestamp = ref(Date.now());
const noBackground = computed(() => game.value?.hasBackground === false);
const backgroundUrl = computed(() => {
    if (!game.value || noBackground.value)
        return '';
    return `${gamesApi.getBackgroundUrl(gameId)}?t=${bgTimestamp.value}`;
});
const heroBgLoaded = ref(false);
const heroScrollY = ref(0);
function onHeroBgLoad() {
    heroBgLoaded.value = true;
}
function onHeroBgError() {
    heroBgLoaded.value = false;
}
// Parallax scroll for hero background
const scrollContainer = ref(null);
function onScroll() {
    if (scrollContainer.value) {
        heroScrollY.value = scrollContainer.value.scrollTop;
    }
}
onMounted(() => {
    // Find the scroll container (parent .main-content)
    nextTick(() => {
        const el = document.querySelector('.main-content');
        if (el) {
            scrollContainer.value = el;
            el.addEventListener('scroll', onScroll, { passive: true });
        }
    });
});
onBeforeUnmount(() => {
    if (scrollContainer.value) {
        scrollContainer.value.removeEventListener('scroll', onScroll);
    }
});
const isInstalled = computed(() => game.value?.installState === 'FullyInstalled');
const isInstalling = computed(() => {
    if (!installStore.status)
        return false;
    if (installStore.activeGameId !== gameId)
        return false;
    const step = installStore.status.step;
    return step !== 'Idle' && step !== 'Complete' && step !== 'Failed';
});
const installStepLabel = computed(() => {
    const labels = {
        DetectingGame: '检测游戏',
        InstallingBepInEx: '安装 BepInEx',
        InstallingXUnity: '安装 XUnity',
        InstallingTmpFont: '安装 TMP 字体',
        InstallingAiTranslation: '部署 AI 翻译引擎',
        GeneratingConfig: '生成配置',
        ApplyingConfig: '应用最佳配置',
        ExtractingAssets: '提取游戏资产',
        VerifyingHealth: '验证插件状态',
    };
    return labels[installStore.status?.step ?? ''] ?? '安装中';
});
const hasBepInEx = computed(() => game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled' ||
    game.value?.installState === 'PartiallyInstalled');
const otherFrameworks = computed(() => game.value?.detectedFrameworks?.filter((f) => f.framework !== 'BepInEx') ?? []);
const frameworkDisplayNames = {
    BepInEx: 'BepInEx',
    MelonLoader: 'MelonLoader',
    IPA: 'IPA / BSIPA',
    ReiPatcher: 'ReiPatcher',
    Sybaris: 'Sybaris',
    UnityInjector: 'UnityInjector',
    Standalone: 'Standalone',
};
// Auto-save for description
const { enable: enableDescAutoSave, disable: disableDescAutoSave } = useAutoSave(() => aiDescription.value, async () => {
    try {
        await gamesApi.saveDescription(gameId, aiDescription.value || null);
        if (game.value)
            game.value.aiDescription = aiDescription.value || undefined;
    }
    catch {
        message.error('保存描述失败');
    }
}, { debounceMs: 1000 });
onMounted(async () => {
    await loadGame();
});
async function loadGame() {
    loading.value = true;
    disableDescAutoSave();
    try {
        game.value = await gamesApi.get(gameId);
        if (isInstalled.value) {
            config.value = await gamesApi.getConfig(gameId);
            try {
                const status = await gamesApi.getAiEndpointStatus(gameId);
                aiEndpointInstalled.value = status.installed;
            }
            catch {
                aiEndpointInstalled.value = null;
            }
        }
        else {
            config.value = null;
            aiEndpointInstalled.value = null;
        }
        // Load install options from global settings
        try {
            const appSettings = await settingsApi.get();
            if (appSettings.installOptions) {
                installOptions.value = { ...installOptions.value, ...appSettings.installOptions };
            }
        }
        catch { /* use defaults */ }
        // Description is always available (not gated by install state)
        aiDescription.value = game.value?.aiDescription ?? '';
    }
    catch {
        message.error('加载游戏信息失败');
    }
    finally {
        loading.value = false;
    }
    await nextTick();
    enableDescAutoSave();
}
async function handleInstall() {
    try {
        // Save install options to global settings before starting
        try {
            const appSettings = await settingsApi.get();
            appSettings.installOptions = installOptions.value;
            await settingsApi.save(appSettings);
        }
        catch { /* non-critical */ }
        await installStore.startInstall(gameId, config.value ?? undefined, installOptions.value);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '安装失败');
    }
}
function handleUninstall() {
    dialog.warning({
        title: '确认卸载',
        content: '将移除 BepInEx 和 XUnity.AutoTranslator 的所有文件。确定要继续吗？',
        positiveText: '确认卸载',
        negativeText: '取消',
        onPositiveClick: async () => {
            try {
                await installStore.startUninstall(gameId);
            }
            catch (e) {
                message.error(e instanceof Error ? e.message : '卸载失败');
            }
        },
    });
}
async function handleOpenFolder() {
    try {
        await gamesApi.openFolder(gameId);
    }
    catch {
        message.error('打开目录失败');
    }
}
async function handleLaunch() {
    try {
        await gamesStore.launchGame(gameId);
        message.success('游戏已启动');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '启动游戏失败');
    }
}
function handleRemoveGame() {
    dialog.error({
        title: '移除游戏',
        content: '将从游戏库中移除此游戏（不会删除游戏文件）。移除后该游戏的术语库、翻译缓存等数据将被清除，重新添加后无法恢复。建议先导出术语库。确定吗？',
        positiveText: '确认移除',
        negativeText: '取消',
        onPositiveClick: async () => {
            try {
                await gamesStore.removeGame(gameId);
                message.success('已移除');
                router.push('/');
            }
            catch {
                message.error('移除失败');
            }
        },
    });
}
function startEditName() {
    editNameValue.value = game.value?.name ?? '';
    editingName.value = true;
}
async function saveEditName() {
    if (!editingName.value)
        return;
    editingName.value = false;
    const name = editNameValue.value.trim();
    if (!name || !game.value || name === game.value.name)
        return;
    try {
        await gamesStore.renameGame(gameId, name);
        game.value.name = name;
        message.success('游戏名称已更新');
    }
    catch {
        message.error('重命名失败');
    }
}
function cancelEditName() {
    editingName.value = false;
}
function handleIconContextMenu(e) {
    e.preventDefault();
    iconContextMenuX.value = e.clientX;
    iconContextMenuY.value = e.clientY;
    showIconContextMenu.value = true;
}
async function handleIconContextMenuSelect(key) {
    showIconContextMenu.value = false;
    if (key === 'cover') {
        showCoverPicker.value = true;
    }
    else if (key === 'web-icon') {
        showIconPicker.value = true;
    }
    else if (key === 'upload-icon') {
        handleUploadIconFromExplorer();
    }
    else if (key === 'delete-icon') {
        try {
            await gamesApi.deleteCustomIcon(gameId);
            await gamesStore.refreshGame(gameId);
            if (game.value)
                game.value = await gamesApi.get(gameId);
            message.success('自定义图标已删除');
        }
        catch {
            message.error('删除图标失败');
        }
    }
    else if (key === 'background') {
        showBackgroundPicker.value = true;
    }
    else if (key === 'delete-background') {
        try {
            await gamesApi.deleteBackground(gameId);
            if (game.value)
                game.value.hasBackground = false;
            heroBgLoaded.value = false;
            bgTimestamp.value = Date.now();
            message.success('背景图已删除');
        }
        catch {
            message.error('删除背景图失败');
        }
    }
}
async function handleUploadIconFromExplorer() {
    const path = await selectFile({
        title: '选择图标图片',
        filters: [{ label: '图片文件', extensions: ['.jpg', '.jpeg', '.png', '.webp'] }],
    });
    if (!path)
        return;
    try {
        await gamesApi.uploadIconFromPath(gameId, path);
        await gamesStore.refreshGame(gameId);
        if (game.value)
            game.value = await gamesApi.get(gameId);
        message.success('图标已更新');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '上传图标失败');
    }
}
async function handleIconSaved() {
    await gamesStore.refreshGame(gameId);
    if (game.value)
        game.value = await gamesApi.get(gameId);
}
function handleBackgroundSaved() {
    if (game.value)
        game.value.hasBackground = true;
    bgTimestamp.value = Date.now();
    heroBgLoaded.value = false;
}
async function handleInstallAiEndpoint() {
    aiEndpointLoading.value = true;
    try {
        const result = await gamesApi.installAiEndpoint(gameId);
        aiEndpointInstalled.value = result.installed;
        message.success('AI 翻译引擎已安装');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '安装失败');
    }
    finally {
        aiEndpointLoading.value = false;
    }
}
async function handleReinstallAiEndpoint() {
    aiEndpointLoading.value = true;
    try {
        const result = await gamesApi.installAiEndpoint(gameId);
        aiEndpointInstalled.value = result.installed;
        message.success('AI 翻译引擎已重装');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '重装失败');
    }
    finally {
        aiEndpointLoading.value = false;
    }
}
function handleUninstallAiEndpoint() {
    dialog.warning({
        title: '卸载 AI 翻译引擎',
        content: '将移除游戏内的 AI 翻译引擎 DLL 文件。确定要继续吗？',
        positiveText: '确认卸载',
        negativeText: '取消',
        onPositiveClick: async () => {
            aiEndpointLoading.value = true;
            try {
                const result = await gamesApi.uninstallAiEndpoint(gameId);
                aiEndpointInstalled.value = result.installed;
                message.success('AI 翻译引擎已卸载');
            }
            catch (e) {
                message.error(e instanceof Error ? e.message : '卸载失败');
            }
            finally {
                aiEndpointLoading.value = false;
            }
        },
    });
}
async function handleExportPackage() {
    packageExporting.value = true;
    try {
        const url = pluginPackageApi.getExportUrl(gameId);
        const resp = await fetch(url, { method: 'POST' });
        if (!resp.ok) {
            const text = await resp.text();
            let msg = `HTTP ${resp.status}`;
            try {
                const json = JSON.parse(text);
                if (json.error)
                    msg = json.error;
            }
            catch { /* ignore */ }
            throw new Error(msg);
        }
        const blob = await resp.blob();
        const disposition = resp.headers.get('content-disposition');
        let fileName = '汉化包.zip';
        if (disposition) {
            // Extract filename from content-disposition, handling both filename= and filename*=UTF-8''
            const utf8Match = disposition.match(/filename\*=UTF-8''(.+?)(?:;|$)/i);
            const plainMatch = disposition.match(/filename="?(.+?)"?(?:;|$)/i);
            const raw = utf8Match?.[1] ?? plainMatch?.[1];
            if (raw)
                fileName = decodeURIComponent(raw);
        }
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = fileName;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
        message.success('汉化包已生成');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '生成汉化包失败');
    }
    finally {
        packageExporting.value = false;
    }
}
async function handleImportPackage() {
    packageImporting.value = true;
    try {
        const filePath = await selectFile({
            title: '选择汉化包',
            filters: [{ label: 'ZIP 压缩包', extensions: ['.zip'] }],
        });
        if (!filePath)
            return;
        await pluginPackageApi.importPackage(gameId, filePath);
        message.success('汉化包导入成功');
        await loadGame();
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '导入汉化包失败');
    }
    finally {
        packageImporting.value = false;
    }
}
function handleUninstallFramework(framework) {
    dialog.warning({
        title: `卸载 ${frameworkDisplayNames[framework]}`,
        content: `将移除 ${frameworkDisplayNames[framework]} 框架及其关联的 XUnity 插件文件。确定要继续吗？`,
        positiveText: '确认卸载',
        negativeText: '取消',
        onPositiveClick: async () => {
            try {
                const updated = await gamesApi.uninstallFramework(gameId, framework);
                game.value = updated;
                await gamesStore.refreshGame(gameId);
                message.success(`${frameworkDisplayNames[framework]} 已卸载`);
            }
            catch (e) {
                message.error(e instanceof Error ? e.message : '卸载失败');
            }
        },
    });
}
const stopWatch = watch(() => installStore.status?.step, async (step) => {
    if (step === 'Complete') {
        await loadGame();
        await gamesStore.refreshGame(gameId);
    }
});
onBeforeUnmount(() => stopWatch());
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['hero-backdrop']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-nav']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-action-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
/** @type {__VLS_StyleScopedClasses['on-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
/** @type {__VLS_StyleScopedClasses['back-button']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon-edit']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon-fallback']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['game-title']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['edit-name-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['title-name-row']} */ ;
/** @type {__VLS_StyleScopedClasses['edit-name-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['edit-name-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['title-status']} */ ;
/** @type {__VLS_StyleScopedClasses['on-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['status-text']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['info-card']} */ ;
/** @type {__VLS_StyleScopedClasses['info-value']} */ ;
/** @type {__VLS_StyleScopedClasses['info-value']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-options-arrow']} */ ;
/** @type {__VLS_StyleScopedClasses['install-options-body']} */ ;
/** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
/** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
/** @type {__VLS_StyleScopedClasses['installed-info-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['cta-visual']} */ ;
/** @type {__VLS_StyleScopedClasses['version-card']} */ ;
/** @type {__VLS_StyleScopedClasses['version-card']} */ ;
/** @type {__VLS_StyleScopedClasses['ai-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['ai-status-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['installed']} */ ;
/** @type {__VLS_StyleScopedClasses['ai-status-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['ai-endpoint-desc']} */ ;
/** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
/** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
/** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-backdrop']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['game-title']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['info-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['version-card-value']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['cta-left']} */ ;
/** @type {__VLS_StyleScopedClasses['install-options-toggle']} */ ;
/** @type {__VLS_StyleScopedClasses['installed-info-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['installed-hint-inline']} */ ;
/** @type {__VLS_StyleScopedClasses['ai-endpoint-content']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-backdrop']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
/** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['game-title']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['info-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
/** @type {__VLS_StyleScopedClasses['install-button']} */ ;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "detail-loading" },
    });
    /** @type {__VLS_StyleScopedClasses['detail-loading']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "loading-spinner" },
    });
    /** @type {__VLS_StyleScopedClasses['loading-spinner']} */ ;
}
else if (__VLS_ctx.game) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "detail-page" },
    });
    /** @type {__VLS_StyleScopedClasses['detail-page']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-backdrop" },
        ...{ class: ({ 'hero-visible': __VLS_ctx.heroBgLoaded }) },
    });
    /** @type {__VLS_StyleScopedClasses['hero-backdrop']} */ ;
    /** @type {__VLS_StyleScopedClasses['hero-visible']} */ ;
    if (!__VLS_ctx.noBackground) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
            ...{ onLoad: (__VLS_ctx.onHeroBgLoad) },
            ...{ onError: (__VLS_ctx.onHeroBgError) },
            src: (__VLS_ctx.backgroundUrl),
            ...{ class: "hero-bg-img" },
            alt: "",
            ...{ style: ({ transform: `translateY(${__VLS_ctx.heroScrollY * 0.3}px) scale(1.05)` }) },
        });
        /** @type {__VLS_StyleScopedClasses['hero-bg-img']} */ ;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-gradient-overlay" },
    });
    /** @type {__VLS_StyleScopedClasses['hero-gradient-overlay']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-vignette" },
    });
    /** @type {__VLS_StyleScopedClasses['hero-vignette']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-nav" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['hero-nav']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.router.push('/');
                // @ts-ignore
                [loading, game, heroBgLoaded, noBackground, onHeroBgLoad, onHeroBgError, backgroundUrl, heroScrollY, router,];
            } },
        ...{ class: "back-button" },
        ...{ class: ({ 'on-hero': __VLS_ctx.heroBgLoaded }) },
    });
    /** @type {__VLS_StyleScopedClasses['back-button']} */ ;
    /** @type {__VLS_StyleScopedClasses['on-hero']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "20",
        height: "20",
        viewBox: "0 0 20 20",
        fill: "none",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        d: "M12 5L7 10L12 15",
        stroke: "currentColor",
        'stroke-width': "1.5",
        'stroke-linecap': "round",
        'stroke-linejoin': "round",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-nav-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['hero-nav-actions']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.showBackgroundPicker = true;
                // @ts-ignore
                [heroBgLoaded, showBackgroundPicker,];
            } },
        ...{ class: "hero-action-btn" },
        title: (__VLS_ctx.heroBgLoaded ? '更换背景' : '设置背景'),
    });
    /** @type {__VLS_StyleScopedClasses['hero-action-btn']} */ ;
    let __VLS_0;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
        size: (16),
    }));
    const __VLS_2 = __VLS_1({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_1));
    const { default: __VLS_5 } = __VLS_3.slots;
    let __VLS_6;
    /** @ts-ignore @type {typeof __VLS_components.WallpaperOutlined} */
    WallpaperOutlined;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
    const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
    // @ts-ignore
    [heroBgLoaded,];
    var __VLS_3;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.handleRemoveGame) },
        ...{ class: "hero-action-btn" },
        title: "从库中移除",
    });
    /** @type {__VLS_StyleScopedClasses['hero-action-btn']} */ ;
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
    /** @ts-ignore @type {typeof __VLS_components.DeleteOutlineOutlined} */
    DeleteOutlineOutlined;
    // @ts-ignore
    const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
    const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
    // @ts-ignore
    [handleRemoveGame,];
    var __VLS_14;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero-title-banner" },
        ...{ class: ({ 'has-hero': __VLS_ctx.heroBgLoaded }) },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['hero-title-banner']} */ ;
    /** @type {__VLS_StyleScopedClasses['has-hero']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.showIconPicker = true;
                // @ts-ignore
                [heroBgLoaded, showIconPicker,];
            } },
        ...{ onContextmenu: (__VLS_ctx.handleIconContextMenu) },
        ...{ class: "title-icon" },
        title: "左键更换图标 / 右键更多操作",
    });
    /** @type {__VLS_StyleScopedClasses['title-icon']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
        ...{ onError: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                $event.target.style.display = 'none';
                $event.target.nextElementSibling?.classList.add('visible');
                // @ts-ignore
                [handleIconContextMenu,];
            } },
        src: (__VLS_ctx.iconUrl),
        alt: (__VLS_ctx.game.name),
        ...{ class: "title-icon-img" },
    });
    /** @type {__VLS_StyleScopedClasses['title-icon-img']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-icon-fallback" },
    });
    /** @type {__VLS_StyleScopedClasses['title-icon-fallback']} */ ;
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
    /** @ts-ignore @type {typeof __VLS_components.GamepadFilled} */
    GamepadFilled;
    // @ts-ignore
    const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
    const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
    // @ts-ignore
    [game, iconUrl,];
    var __VLS_25;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-icon-edit" },
    });
    /** @type {__VLS_StyleScopedClasses['title-icon-edit']} */ ;
    let __VLS_33;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
        size: (16),
        color: "#fff",
    }));
    const __VLS_35 = __VLS_34({
        size: (16),
        color: "#fff",
    }, ...__VLS_functionalComponentArgsRest(__VLS_34));
    const { default: __VLS_38 } = __VLS_36.slots;
    let __VLS_39;
    /** @ts-ignore @type {typeof __VLS_components.PhotoCameraOutlined} */
    PhotoCameraOutlined;
    // @ts-ignore
    const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({}));
    const __VLS_41 = __VLS_40({}, ...__VLS_functionalComponentArgsRest(__VLS_40));
    // @ts-ignore
    [];
    var __VLS_36;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-text-group" },
    });
    /** @type {__VLS_StyleScopedClasses['title-text-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-name-row" },
    });
    /** @type {__VLS_StyleScopedClasses['title-name-row']} */ ;
    if (!__VLS_ctx.editingName) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
            ...{ onDblclick: (__VLS_ctx.startEditName) },
            ...{ class: "game-title" },
        });
        /** @type {__VLS_StyleScopedClasses['game-title']} */ ;
        (__VLS_ctx.game.name);
    }
    else {
        let __VLS_44;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({
            ...{ 'onKeyup': {} },
            ...{ 'onKeyup': {} },
            ...{ 'onBlur': {} },
            value: (__VLS_ctx.editNameValue),
            ...{ class: "game-title-input" },
            size: "large",
            autofocus: true,
        }));
        const __VLS_46 = __VLS_45({
            ...{ 'onKeyup': {} },
            ...{ 'onKeyup': {} },
            ...{ 'onBlur': {} },
            value: (__VLS_ctx.editNameValue),
            ...{ class: "game-title-input" },
            size: "large",
            autofocus: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_45));
        let __VLS_49;
        const __VLS_50 = ({ keyup: {} },
            { onKeyup: (__VLS_ctx.saveEditName) });
        const __VLS_51 = ({ keyup: {} },
            { onKeyup: (__VLS_ctx.cancelEditName) });
        const __VLS_52 = ({ blur: {} },
            { onBlur: (__VLS_ctx.saveEditName) });
        /** @type {__VLS_StyleScopedClasses['game-title-input']} */ ;
        var __VLS_47;
        var __VLS_48;
    }
    if (!__VLS_ctx.editingName) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (__VLS_ctx.startEditName) },
            ...{ class: "edit-name-btn" },
            title: "重命名",
        });
        /** @type {__VLS_StyleScopedClasses['edit-name-btn']} */ ;
        let __VLS_53;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
            size: (16),
            color: "currentColor",
        }));
        const __VLS_55 = __VLS_54({
            size: (16),
            color: "currentColor",
        }, ...__VLS_functionalComponentArgsRest(__VLS_54));
        const { default: __VLS_58 } = __VLS_56.slots;
        let __VLS_59;
        /** @ts-ignore @type {typeof __VLS_components.DriveFileRenameOutlineOutlined} */
        DriveFileRenameOutlineOutlined;
        // @ts-ignore
        const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({}));
        const __VLS_61 = __VLS_60({}, ...__VLS_functionalComponentArgsRest(__VLS_60));
        // @ts-ignore
        [game, editingName, editingName, startEditName, startEditName, editNameValue, saveEditName, saveEditName, cancelEditName,];
        var __VLS_56;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-meta-row" },
    });
    /** @type {__VLS_StyleScopedClasses['title-meta-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "title-status" },
        ...{ class: ({ 'on-hero': __VLS_ctx.heroBgLoaded }) },
    });
    /** @type {__VLS_StyleScopedClasses['title-status']} */ ;
    /** @type {__VLS_StyleScopedClasses['on-hero']} */ ;
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "status-indicator" },
            ...{ class: ({
                    'status-installed': __VLS_ctx.isInstalled,
                    'status-partial': __VLS_ctx.hasBepInEx && !__VLS_ctx.isInstalled,
                    'status-none': !__VLS_ctx.hasBepInEx,
                }) },
        });
        /** @type {__VLS_StyleScopedClasses['status-indicator']} */ ;
        /** @type {__VLS_StyleScopedClasses['status-installed']} */ ;
        /** @type {__VLS_StyleScopedClasses['status-partial']} */ ;
        /** @type {__VLS_StyleScopedClasses['status-none']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "status-text" },
        });
        /** @type {__VLS_StyleScopedClasses['status-text']} */ ;
        (__VLS_ctx.isInstalled ? '已安装' : __VLS_ctx.hasBepInEx ? '仅 BepInEx' : '未安装');
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "status-text" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['status-text']} */ ;
    }
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "workspace-mode-row" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-mode-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "workspace-mode-chip active" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
        (__VLS_ctx.game.xUnityStatus.state);
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame))
                        return;
                    __VLS_ctx.router.push(/games/ / __VLS_ctx.manual - __VLS_ctx.translation);
                    // @ts-ignore
                    [game, game, game, heroBgLoaded, router, isInstalled, isInstalled, isInstalled, hasBepInEx, hasBepInEx, hasBepInEx, manual, translation,];
                } },
            ...{ class: "workspace-mode-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-mode-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
        (__VLS_ctx.game.manualTranslationStatus.overrideCount);
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
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        size: (16),
    }));
    const __VLS_66 = __VLS_65({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
    const { default: __VLS_69 } = __VLS_67.slots;
    let __VLS_70;
    /** @ts-ignore @type {typeof __VLS_components.InfoOutlined} */
    InfoOutlined;
    // @ts-ignore
    const __VLS_71 = __VLS_asFunctionalComponent1(__VLS_70, new __VLS_70({}));
    const __VLS_72 = __VLS_71({}, ...__VLS_functionalComponentArgsRest(__VLS_71));
    // @ts-ignore
    [game,];
    var __VLS_67;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    let __VLS_75;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_76 = __VLS_asFunctionalComponent1(__VLS_75, new __VLS_75({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_77 = __VLS_76({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_76));
    let __VLS_80;
    const __VLS_81 = ({ click: {} },
        { onClick: (__VLS_ctx.handleOpenFolder) });
    const { default: __VLS_82 } = __VLS_78.slots;
    {
        const { icon: __VLS_83 } = __VLS_78.slots;
        let __VLS_84;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({
            size: (16),
        }));
        const __VLS_86 = __VLS_85({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_85));
        const { default: __VLS_89 } = __VLS_87.slots;
        let __VLS_90;
        /** @ts-ignore @type {typeof __VLS_components.FolderOpenOutlined} */
        FolderOpenOutlined;
        // @ts-ignore
        const __VLS_91 = __VLS_asFunctionalComponent1(__VLS_90, new __VLS_90({}));
        const __VLS_92 = __VLS_91({}, ...__VLS_functionalComponentArgsRest(__VLS_91));
        // @ts-ignore
        [handleOpenFolder,];
        var __VLS_87;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_78;
    var __VLS_79;
    let __VLS_95;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_96 = __VLS_asFunctionalComponent1(__VLS_95, new __VLS_95({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_97 = __VLS_96({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_96));
    let __VLS_100;
    const __VLS_101 = ({ click: {} },
        { onClick: (__VLS_ctx.handleLaunch) });
    const { default: __VLS_102 } = __VLS_98.slots;
    {
        const { icon: __VLS_103 } = __VLS_98.slots;
        let __VLS_104;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_105 = __VLS_asFunctionalComponent1(__VLS_104, new __VLS_104({
            size: (16),
        }));
        const __VLS_106 = __VLS_105({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_105));
        const { default: __VLS_109 } = __VLS_107.slots;
        let __VLS_110;
        /** @ts-ignore @type {typeof __VLS_components.PlayArrowFilled} */
        PlayArrowFilled;
        // @ts-ignore
        const __VLS_111 = __VLS_asFunctionalComponent1(__VLS_110, new __VLS_110({}));
        const __VLS_112 = __VLS_111({}, ...__VLS_functionalComponentArgsRest(__VLS_111));
        // @ts-ignore
        [handleLaunch,];
        var __VLS_107;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_98;
    var __VLS_99;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-grid" },
    });
    /** @type {__VLS_StyleScopedClasses['info-grid']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-card" },
    });
    /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-card-icon folder" },
    });
    /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['folder']} */ ;
    let __VLS_115;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_116 = __VLS_asFunctionalComponent1(__VLS_115, new __VLS_115({
        size: (18),
    }));
    const __VLS_117 = __VLS_116({
        size: (18),
    }, ...__VLS_functionalComponentArgsRest(__VLS_116));
    const { default: __VLS_120 } = __VLS_118.slots;
    let __VLS_121;
    /** @ts-ignore @type {typeof __VLS_components.FolderOutlined} */
    FolderOutlined;
    // @ts-ignore
    const __VLS_122 = __VLS_asFunctionalComponent1(__VLS_121, new __VLS_121({}));
    const __VLS_123 = __VLS_122({}, ...__VLS_functionalComponentArgsRest(__VLS_122));
    // @ts-ignore
    [];
    var __VLS_118;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-card-content" },
    });
    /** @type {__VLS_StyleScopedClasses['info-card-content']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "info-label" },
    });
    /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "info-value mono" },
    });
    /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
    /** @type {__VLS_StyleScopedClasses['mono']} */ ;
    (__VLS_ctx.game.gamePath);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-card" },
    });
    /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-card-icon file" },
    });
    /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['file']} */ ;
    let __VLS_126;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_127 = __VLS_asFunctionalComponent1(__VLS_126, new __VLS_126({
        size: (18),
    }));
    const __VLS_128 = __VLS_127({
        size: (18),
    }, ...__VLS_functionalComponentArgsRest(__VLS_127));
    const { default: __VLS_131 } = __VLS_129.slots;
    let __VLS_132;
    /** @ts-ignore @type {typeof __VLS_components.InsertDriveFileOutlined} */
    InsertDriveFileOutlined;
    // @ts-ignore
    const __VLS_133 = __VLS_asFunctionalComponent1(__VLS_132, new __VLS_132({}));
    const __VLS_134 = __VLS_133({}, ...__VLS_functionalComponentArgsRest(__VLS_133));
    // @ts-ignore
    [game,];
    var __VLS_129;
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
        ...{ class: ({ muted: !__VLS_ctx.game.executableName }) },
    });
    /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
    /** @type {__VLS_StyleScopedClasses['muted']} */ ;
    (__VLS_ctx.game.executableName || '未知');
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card-icon unity" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['unity']} */ ;
        let __VLS_137;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_138 = __VLS_asFunctionalComponent1(__VLS_137, new __VLS_137({
            size: (18),
        }));
        const __VLS_139 = __VLS_138({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_138));
        const { default: __VLS_142 } = __VLS_140.slots;
        let __VLS_143;
        /** @ts-ignore @type {typeof __VLS_components.WidgetsOutlined} */
        WidgetsOutlined;
        // @ts-ignore
        const __VLS_144 = __VLS_asFunctionalComponent1(__VLS_143, new __VLS_143({}));
        const __VLS_145 = __VLS_144({}, ...__VLS_functionalComponentArgsRest(__VLS_144));
        // @ts-ignore
        [game, game, game,];
        var __VLS_140;
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
            ...{ class: ({ muted: !__VLS_ctx.game.detectedInfo }) },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        /** @type {__VLS_StyleScopedClasses['muted']} */ ;
        (__VLS_ctx.game.detectedInfo?.unityVersion || '未知');
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card-icon code" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['code']} */ ;
        let __VLS_148;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_149 = __VLS_asFunctionalComponent1(__VLS_148, new __VLS_148({
            size: (18),
        }));
        const __VLS_150 = __VLS_149({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_149));
        const { default: __VLS_153 } = __VLS_151.slots;
        let __VLS_154;
        /** @ts-ignore @type {typeof __VLS_components.CodeOutlined} */
        CodeOutlined;
        // @ts-ignore
        const __VLS_155 = __VLS_asFunctionalComponent1(__VLS_154, new __VLS_154({}));
        const __VLS_156 = __VLS_155({}, ...__VLS_functionalComponentArgsRest(__VLS_155));
        // @ts-ignore
        [game, game,];
        var __VLS_151;
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
            ...{ class: ({ muted: !__VLS_ctx.game.detectedInfo }) },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        /** @type {__VLS_StyleScopedClasses['muted']} */ ;
        (__VLS_ctx.game.detectedInfo?.backend || '未知');
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card-icon arch" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['arch']} */ ;
        let __VLS_159;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_160 = __VLS_asFunctionalComponent1(__VLS_159, new __VLS_159({
            size: (18),
        }));
        const __VLS_161 = __VLS_160({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_160));
        const { default: __VLS_164 } = __VLS_162.slots;
        let __VLS_165;
        /** @ts-ignore @type {typeof __VLS_components.MemoryOutlined} */
        MemoryOutlined;
        // @ts-ignore
        const __VLS_166 = __VLS_asFunctionalComponent1(__VLS_165, new __VLS_165({}));
        const __VLS_167 = __VLS_166({}, ...__VLS_functionalComponentArgsRest(__VLS_166));
        // @ts-ignore
        [game, game,];
        var __VLS_162;
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
            ...{ class: ({ muted: !__VLS_ctx.game.detectedInfo }) },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        /** @type {__VLS_StyleScopedClasses['muted']} */ ;
        (__VLS_ctx.game.detectedInfo?.architecture || '未知');
    }
    if (!__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card-icon']} */ ;
        let __VLS_170;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
            size: (18),
        }));
        const __VLS_172 = __VLS_171({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_171));
        const { default: __VLS_175 } = __VLS_173.slots;
        let __VLS_176;
        /** @ts-ignore @type {typeof __VLS_components.InfoOutlined} */
        InfoOutlined;
        // @ts-ignore
        const __VLS_177 = __VLS_asFunctionalComponent1(__VLS_176, new __VLS_176({}));
        const __VLS_178 = __VLS_177({}, ...__VLS_functionalComponentArgsRest(__VLS_177));
        // @ts-ignore
        [game, game, game,];
        var __VLS_173;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-card-content" },
        });
        /** @type {__VLS_StyleScopedClasses['info-card-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-label" },
        });
        /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-value muted" },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        /** @type {__VLS_StyleScopedClasses['muted']} */ ;
    }
    if (__VLS_ctx.otherFrameworks.length > 0) {
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
            ...{ class: "section-icon warning" },
        });
        /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['warning']} */ ;
        let __VLS_181;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_182 = __VLS_asFunctionalComponent1(__VLS_181, new __VLS_181({
            size: (16),
        }));
        const __VLS_183 = __VLS_182({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_182));
        const { default: __VLS_186 } = __VLS_184.slots;
        let __VLS_187;
        /** @ts-ignore @type {typeof __VLS_components.WarningAmberOutlined} */
        WarningAmberOutlined;
        // @ts-ignore
        const __VLS_188 = __VLS_asFunctionalComponent1(__VLS_187, new __VLS_187({}));
        const __VLS_189 = __VLS_188({}, ...__VLS_functionalComponentArgsRest(__VLS_188));
        // @ts-ignore
        [otherFrameworks,];
        var __VLS_184;
        let __VLS_192;
        /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
        NAlert;
        // @ts-ignore
        const __VLS_193 = __VLS_asFunctionalComponent1(__VLS_192, new __VLS_192({
            type: "warning",
            ...{ style: {} },
        }));
        const __VLS_194 = __VLS_193({
            type: "warning",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_193));
        const { default: __VLS_197 } = __VLS_195.slots;
        // @ts-ignore
        [];
        var __VLS_195;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "framework-list" },
        });
        /** @type {__VLS_StyleScopedClasses['framework-list']} */ ;
        for (const [fw] of __VLS_vFor((__VLS_ctx.otherFrameworks))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (fw.framework),
                ...{ class: "framework-item" },
            });
            /** @type {__VLS_StyleScopedClasses['framework-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "framework-info" },
            });
            /** @type {__VLS_StyleScopedClasses['framework-info']} */ ;
            let __VLS_198;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_199 = __VLS_asFunctionalComponent1(__VLS_198, new __VLS_198({
                size: (16),
                color: "var(--warning)",
            }));
            const __VLS_200 = __VLS_199({
                size: (16),
                color: "var(--warning)",
            }, ...__VLS_functionalComponentArgsRest(__VLS_199));
            const { default: __VLS_203 } = __VLS_201.slots;
            let __VLS_204;
            /** @ts-ignore @type {typeof __VLS_components.ExtensionOutlined} */
            ExtensionOutlined;
            // @ts-ignore
            const __VLS_205 = __VLS_asFunctionalComponent1(__VLS_204, new __VLS_204({}));
            const __VLS_206 = __VLS_205({}, ...__VLS_functionalComponentArgsRest(__VLS_205));
            // @ts-ignore
            [otherFrameworks,];
            var __VLS_201;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "framework-name" },
            });
            /** @type {__VLS_StyleScopedClasses['framework-name']} */ ;
            (__VLS_ctx.frameworkDisplayNames[fw.framework]);
            if (fw.version) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "framework-version" },
                });
                /** @type {__VLS_StyleScopedClasses['framework-version']} */ ;
                (fw.version);
            }
            if (fw.hasXUnityPlugin) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "framework-xunity" },
                });
                /** @type {__VLS_StyleScopedClasses['framework-xunity']} */ ;
                (fw.xUnityVersion ? ` v${fw.xUnityVersion}` : '');
            }
            let __VLS_209;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_210 = __VLS_asFunctionalComponent1(__VLS_209, new __VLS_209({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
            }));
            const __VLS_211 = __VLS_210({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
            }, ...__VLS_functionalComponentArgsRest(__VLS_210));
            let __VLS_214;
            const __VLS_215 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!(__VLS_ctx.otherFrameworks.length > 0))
                            return;
                        __VLS_ctx.handleUninstallFramework(fw.framework);
                        // @ts-ignore
                        [frameworkDisplayNames, handleUninstallFramework,];
                    } });
            const { default: __VLS_216 } = __VLS_212.slots;
            // @ts-ignore
            [];
            var __VLS_212;
            var __VLS_213;
            // @ts-ignore
            [];
        }
    }
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.2s' : '0.15s' }) },
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
        let __VLS_217;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_218 = __VLS_asFunctionalComponent1(__VLS_217, new __VLS_217({
            size: (16),
        }));
        const __VLS_219 = __VLS_218({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_218));
        const { default: __VLS_222 } = __VLS_220.slots;
        let __VLS_223;
        /** @ts-ignore @type {typeof __VLS_components.GetAppOutlined} */
        GetAppOutlined;
        // @ts-ignore
        const __VLS_224 = __VLS_asFunctionalComponent1(__VLS_223, new __VLS_223({}));
        const __VLS_225 = __VLS_224({}, ...__VLS_functionalComponentArgsRest(__VLS_224));
        // @ts-ignore
        [game, otherFrameworks,];
        var __VLS_220;
        if (__VLS_ctx.isInstalled) {
            let __VLS_228;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_229 = __VLS_asFunctionalComponent1(__VLS_228, new __VLS_228({
                ...{ 'onClick': {} },
                type: "error",
                ghost: true,
                size: "small",
            }));
            const __VLS_230 = __VLS_229({
                ...{ 'onClick': {} },
                type: "error",
                ghost: true,
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_229));
            let __VLS_233;
            const __VLS_234 = ({ click: {} },
                { onClick: (__VLS_ctx.handleUninstall) });
            const { default: __VLS_235 } = __VLS_231.slots;
            // @ts-ignore
            [isInstalled, handleUninstall,];
            var __VLS_231;
            var __VLS_232;
        }
        if (__VLS_ctx.game.detectedInfo?.backend === 'IL2CPP') {
            let __VLS_236;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_237 = __VLS_asFunctionalComponent1(__VLS_236, new __VLS_236({
                type: "warning",
                ...{ style: {} },
            }));
            const __VLS_238 = __VLS_237({
                type: "warning",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_237));
            const { default: __VLS_241 } = __VLS_239.slots;
            // @ts-ignore
            [game,];
            var __VLS_239;
        }
        if (!__VLS_ctx.isInstalled && __VLS_ctx.isInstalling) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-cta-horizontal installing" },
            });
            /** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
            /** @type {__VLS_StyleScopedClasses['installing']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-left" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-left']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-visual installing" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-visual']} */ ;
            /** @type {__VLS_StyleScopedClasses['installing']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                ...{ class: "cta-icon-spin" },
                width: "28",
                height: "28",
                viewBox: "0 0 28 28",
                fill: "none",
            });
            /** @type {__VLS_StyleScopedClasses['cta-icon-spin']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
                cx: "14",
                cy: "14",
                r: "11",
                stroke: "currentColor",
                'stroke-width': "2.5",
                opacity: "0.2",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M14 3A11 11 0 0 1 25 14",
                stroke: "currentColor",
                'stroke-width': "2.5",
                'stroke-linecap': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-text" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-text']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cta-title" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-title']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cta-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-desc']} */ ;
            (__VLS_ctx.installStepLabel);
            let __VLS_242;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_243 = __VLS_asFunctionalComponent1(__VLS_242, new __VLS_242({
                ...{ 'onClick': {} },
                type: "primary",
                size: "large",
                ...{ class: "install-button" },
            }));
            const __VLS_244 = __VLS_243({
                ...{ 'onClick': {} },
                type: "primary",
                size: "large",
                ...{ class: "install-button" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_243));
            let __VLS_247;
            const __VLS_248 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!(__VLS_ctx.game.isUnityGame))
                            return;
                        if (!(!__VLS_ctx.isInstalled && __VLS_ctx.isInstalling))
                            return;
                        __VLS_ctx.installStore.isDrawerOpen = true;
                        // @ts-ignore
                        [isInstalled, isInstalling, installStepLabel, installStore,];
                    } });
            /** @type {__VLS_StyleScopedClasses['install-button']} */ ;
            const { default: __VLS_249 } = __VLS_245.slots;
            // @ts-ignore
            [];
            var __VLS_245;
            var __VLS_246;
        }
        else if (!__VLS_ctx.isInstalled) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-uninstalled-area" },
            });
            /** @type {__VLS_StyleScopedClasses['install-uninstalled-area']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-cta-horizontal" },
            });
            /** @type {__VLS_StyleScopedClasses['install-cta-horizontal']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-left" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-left']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-visual" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-visual']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                ...{ class: "cta-icon" },
                width: "40",
                height: "40",
                viewBox: "0 0 40 40",
                fill: "none",
            });
            /** @type {__VLS_StyleScopedClasses['cta-icon']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
                x: "4",
                y: "4",
                width: "32",
                height: "32",
                rx: "8",
                stroke: "currentColor",
                'stroke-width': "1.5",
                opacity: "0.2",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M20 12V24M20 24L14 18M20 24L26 18",
                stroke: "currentColor",
                'stroke-width': "2",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M12 28H28",
                stroke: "currentColor",
                'stroke-width': "2",
                'stroke-linecap': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cta-text" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-text']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cta-title" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-title']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cta-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['cta-desc']} */ ;
            let __VLS_250;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_251 = __VLS_asFunctionalComponent1(__VLS_250, new __VLS_250({
                ...{ 'onClick': {} },
                type: "primary",
                size: "large",
                disabled: (!__VLS_ctx.game.detectedInfo),
                ...{ class: "install-button" },
            }));
            const __VLS_252 = __VLS_251({
                ...{ 'onClick': {} },
                type: "primary",
                size: "large",
                disabled: (!__VLS_ctx.game.detectedInfo),
                ...{ class: "install-button" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_251));
            let __VLS_255;
            const __VLS_256 = ({ click: {} },
                { onClick: (__VLS_ctx.handleInstall) });
            /** @type {__VLS_StyleScopedClasses['install-button']} */ ;
            const { default: __VLS_257 } = __VLS_253.slots;
            {
                const { icon: __VLS_258 } = __VLS_253.slots;
                __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                    width: "18",
                    height: "18",
                    viewBox: "0 0 18 18",
                    fill: "none",
                });
                __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                    d: "M9 3V12M9 12L5 8M9 12L13 8M3 15H15",
                    stroke: "currentColor",
                    'stroke-width': "1.5",
                    'stroke-linecap': "round",
                    'stroke-linejoin': "round",
                });
                // @ts-ignore
                [game, isInstalled, handleInstall,];
            }
            // @ts-ignore
            [];
            var __VLS_253;
            var __VLS_254;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!(__VLS_ctx.game.isUnityGame))
                            return;
                        if (!!(!__VLS_ctx.isInstalled && __VLS_ctx.isInstalling))
                            return;
                        if (!(!__VLS_ctx.isInstalled))
                            return;
                        __VLS_ctx.collapsed.installOptions = !__VLS_ctx.collapsed.installOptions;
                        // @ts-ignore
                        [collapsed, collapsed,];
                    } },
                ...{ class: "install-options-toggle" },
            });
            /** @type {__VLS_StyleScopedClasses['install-options-toggle']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-options-label" },
            });
            /** @type {__VLS_StyleScopedClasses['install-options-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                ...{ class: "install-options-arrow" },
                ...{ class: ({ expanded: !__VLS_ctx.collapsed.installOptions }) },
                width: "16",
                height: "16",
                viewBox: "0 0 16 16",
                fill: "none",
            });
            /** @type {__VLS_StyleScopedClasses['install-options-arrow']} */ ;
            /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M4 6L8 10L12 6",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-options-body" },
                ...{ class: ({ collapsed: __VLS_ctx.collapsed.installOptions }) },
            });
            /** @type {__VLS_StyleScopedClasses['install-options-body']} */ ;
            /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-options-body-inner" },
            });
            /** @type {__VLS_StyleScopedClasses['install-options-body-inner']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_259;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_260 = __VLS_asFunctionalComponent1(__VLS_259, new __VLS_259({
                value: (__VLS_ctx.installOptions.autoInstallTmpFont),
                size: "small",
            }));
            const __VLS_261 = __VLS_260({
                value: (__VLS_ctx.installOptions.autoInstallTmpFont),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_260));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_264;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_265 = __VLS_asFunctionalComponent1(__VLS_264, new __VLS_264({
                value: (__VLS_ctx.installOptions.autoDeployAiEndpoint),
                size: "small",
            }));
            const __VLS_266 = __VLS_265({
                value: (__VLS_ctx.installOptions.autoDeployAiEndpoint),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_265));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_269;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_270 = __VLS_asFunctionalComponent1(__VLS_269, new __VLS_269({
                value: (__VLS_ctx.installOptions.autoGenerateConfig),
                size: "small",
            }));
            const __VLS_271 = __VLS_270({
                value: (__VLS_ctx.installOptions.autoGenerateConfig),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_270));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_274;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_275 = __VLS_asFunctionalComponent1(__VLS_274, new __VLS_274({
                value: (__VLS_ctx.installOptions.autoApplyOptimalConfig),
                size: "small",
            }));
            const __VLS_276 = __VLS_275({
                value: (__VLS_ctx.installOptions.autoApplyOptimalConfig),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_275));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_279;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_280 = __VLS_asFunctionalComponent1(__VLS_279, new __VLS_279({
                value: (__VLS_ctx.installOptions.autoExtractAssets),
                size: "small",
            }));
            const __VLS_281 = __VLS_280({
                value: (__VLS_ctx.installOptions.autoExtractAssets),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_280));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-row" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "install-option-info" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-name" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-name']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "install-option-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['install-option-desc']} */ ;
            let __VLS_284;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_285 = __VLS_asFunctionalComponent1(__VLS_284, new __VLS_284({
                value: (__VLS_ctx.installOptions.autoVerifyHealth),
                size: "small",
            }));
            const __VLS_286 = __VLS_285({
                value: (__VLS_ctx.installOptions.autoVerifyHealth),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_285));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "installed-info-horizontal" },
            });
            /** @type {__VLS_StyleScopedClasses['installed-info-horizontal']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "version-card" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "version-card-header" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-header']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                width: "18",
                height: "18",
                viewBox: "0 0 18 18",
                fill: "none",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
                x: "2",
                y: "2",
                width: "14",
                height: "14",
                rx: "3",
                stroke: "currentColor",
                'stroke-width': "1.2",
                opacity: "0.5",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M6 9L8 11L12 7",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-label" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-value" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-value']} */ ;
            (__VLS_ctx.game.installedBepInExVersion);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-desc']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "version-card" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "version-card-header" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-header']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                width: "18",
                height: "18",
                viewBox: "0 0 18 18",
                fill: "none",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
                x: "2",
                y: "2",
                width: "14",
                height: "14",
                rx: "3",
                stroke: "currentColor",
                'stroke-width': "1.2",
                opacity: "0.5",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M6 9L8 11L12 7",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-label" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-value" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-value']} */ ;
            (__VLS_ctx.game.installedXUnityVersion);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "version-card-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['version-card-desc']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "installed-hint-inline" },
            });
            /** @type {__VLS_StyleScopedClasses['installed-hint-inline']} */ ;
        }
    }
    if (__VLS_ctx.game.isUnityGame && __VLS_ctx.hasBepInEx && !__VLS_ctx.isInstalling) {
        const __VLS_289 = PluginHealthCard;
        // @ts-ignore
        const __VLS_290 = __VLS_asFunctionalComponent1(__VLS_289, new __VLS_289({
            gameId: (__VLS_ctx.gameId),
            initialReport: (__VLS_ctx.installStore.activeGameId === __VLS_ctx.gameId ? __VLS_ctx.installStore.healthReport : null),
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.25s' : '0.2s' }) },
        }));
        const __VLS_291 = __VLS_290({
            gameId: (__VLS_ctx.gameId),
            initialReport: (__VLS_ctx.installStore.activeGameId === __VLS_ctx.gameId ? __VLS_ctx.installStore.healthReport : null),
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.25s' : '0.2s' }) },
        }, ...__VLS_functionalComponentArgsRest(__VLS_290));
    }
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.config }) },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.3s' : '0.25s' }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame))
                        return;
                    __VLS_ctx.collapsed.config = !__VLS_ctx.collapsed.config;
                    // @ts-ignore
                    [game, game, game, game, hasBepInEx, otherFrameworks, otherFrameworks, isInstalling, installStore, installStore, collapsed, collapsed, collapsed, collapsed, collapsed, installOptions, installOptions, installOptions, installOptions, installOptions, installOptions, gameId, gameId,];
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
            ...{ class: "section-icon translate" },
        });
        /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['translate']} */ ;
        let __VLS_294;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_295 = __VLS_asFunctionalComponent1(__VLS_294, new __VLS_294({
            size: (16),
        }));
        const __VLS_296 = __VLS_295({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_295));
        const { default: __VLS_299 } = __VLS_297.slots;
        let __VLS_300;
        /** @ts-ignore @type {typeof __VLS_components.TranslateOutlined} */
        TranslateOutlined;
        // @ts-ignore
        const __VLS_301 = __VLS_asFunctionalComponent1(__VLS_300, new __VLS_300({}));
        const __VLS_302 = __VLS_301({}, ...__VLS_functionalComponentArgsRest(__VLS_301));
        // @ts-ignore
        [];
        var __VLS_297;
        let __VLS_305;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_306 = __VLS_asFunctionalComponent1(__VLS_305, new __VLS_305({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.config }) },
        }));
        const __VLS_307 = __VLS_306({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.config }) },
        }, ...__VLS_functionalComponentArgsRest(__VLS_306));
        /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
        /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
        const { default: __VLS_310 } = __VLS_308.slots;
        let __VLS_311;
        /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
        ExpandMoreOutlined;
        // @ts-ignore
        const __VLS_312 = __VLS_asFunctionalComponent1(__VLS_311, new __VLS_311({}));
        const __VLS_313 = __VLS_312({}, ...__VLS_functionalComponentArgsRest(__VLS_312));
        // @ts-ignore
        [collapsed,];
        var __VLS_308;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body" },
            ...{ class: ({ collapsed: __VLS_ctx.collapsed.config }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
        /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body-inner" },
        });
        /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
        const __VLS_316 = ConfigPanel;
        // @ts-ignore
        const __VLS_317 = __VLS_asFunctionalComponent1(__VLS_316, new __VLS_316({
            config: (__VLS_ctx.config),
            disabled: (!__VLS_ctx.isInstalled),
            gameId: (__VLS_ctx.gameId),
        }));
        const __VLS_318 = __VLS_317({
            config: (__VLS_ctx.config),
            disabled: (!__VLS_ctx.isInstalled),
            gameId: (__VLS_ctx.gameId),
        }, ...__VLS_functionalComponentArgsRest(__VLS_317));
    }
    if (__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.35s' : '0.3s' }) },
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
            ...{ class: "section-icon ai" },
        });
        /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['ai']} */ ;
        let __VLS_321;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_322 = __VLS_asFunctionalComponent1(__VLS_321, new __VLS_321({
            size: (16),
        }));
        const __VLS_323 = __VLS_322({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_322));
        const { default: __VLS_326 } = __VLS_324.slots;
        let __VLS_327;
        /** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
        SmartToyOutlined;
        // @ts-ignore
        const __VLS_328 = __VLS_asFunctionalComponent1(__VLS_327, new __VLS_327({}));
        const __VLS_329 = __VLS_328({}, ...__VLS_functionalComponentArgsRest(__VLS_328));
        // @ts-ignore
        [game, isInstalled, isInstalled, otherFrameworks, collapsed, gameId, config,];
        var __VLS_324;
        if (__VLS_ctx.aiEndpointInstalled !== null) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "ai-status-badge" },
                ...{ class: ({ installed: __VLS_ctx.aiEndpointInstalled }) },
            });
            /** @type {__VLS_StyleScopedClasses['ai-status-badge']} */ ;
            /** @type {__VLS_StyleScopedClasses['installed']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "ai-status-dot" },
            });
            /** @type {__VLS_StyleScopedClasses['ai-status-dot']} */ ;
            (__VLS_ctx.aiEndpointInstalled ? '已安装' : '未安装');
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "ai-endpoint-content" },
        });
        /** @type {__VLS_StyleScopedClasses['ai-endpoint-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "ai-endpoint-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['ai-endpoint-desc']} */ ;
        if (__VLS_ctx.aiEndpointInstalled) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({});
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({});
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "ai-endpoint-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['ai-endpoint-actions']} */ ;
        if (!__VLS_ctx.aiEndpointInstalled) {
            let __VLS_332;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_333 = __VLS_asFunctionalComponent1(__VLS_332, new __VLS_332({
                ...{ 'onClick': {} },
                type: "primary",
                loading: (__VLS_ctx.aiEndpointLoading),
            }));
            const __VLS_334 = __VLS_333({
                ...{ 'onClick': {} },
                type: "primary",
                loading: (__VLS_ctx.aiEndpointLoading),
            }, ...__VLS_functionalComponentArgsRest(__VLS_333));
            let __VLS_337;
            const __VLS_338 = ({ click: {} },
                { onClick: (__VLS_ctx.handleInstallAiEndpoint) });
            const { default: __VLS_339 } = __VLS_335.slots;
            {
                const { icon: __VLS_340 } = __VLS_335.slots;
                let __VLS_341;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_342 = __VLS_asFunctionalComponent1(__VLS_341, new __VLS_341({
                    size: (16),
                }));
                const __VLS_343 = __VLS_342({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_342));
                const { default: __VLS_346 } = __VLS_344.slots;
                let __VLS_347;
                /** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
                SmartToyOutlined;
                // @ts-ignore
                const __VLS_348 = __VLS_asFunctionalComponent1(__VLS_347, new __VLS_347({}));
                const __VLS_349 = __VLS_348({}, ...__VLS_functionalComponentArgsRest(__VLS_348));
                // @ts-ignore
                [aiEndpointInstalled, aiEndpointInstalled, aiEndpointInstalled, aiEndpointInstalled, aiEndpointInstalled, aiEndpointLoading, handleInstallAiEndpoint,];
                var __VLS_344;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_335;
            var __VLS_336;
        }
        else {
            let __VLS_352;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_353 = __VLS_asFunctionalComponent1(__VLS_352, new __VLS_352({
                ...{ 'onClick': {} },
                ghost: true,
                loading: (__VLS_ctx.aiEndpointLoading),
            }));
            const __VLS_354 = __VLS_353({
                ...{ 'onClick': {} },
                ghost: true,
                loading: (__VLS_ctx.aiEndpointLoading),
            }, ...__VLS_functionalComponentArgsRest(__VLS_353));
            let __VLS_357;
            const __VLS_358 = ({ click: {} },
                { onClick: (__VLS_ctx.handleReinstallAiEndpoint) });
            const { default: __VLS_359 } = __VLS_355.slots;
            {
                const { icon: __VLS_360 } = __VLS_355.slots;
                let __VLS_361;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_362 = __VLS_asFunctionalComponent1(__VLS_361, new __VLS_361({
                    size: (16),
                }));
                const __VLS_363 = __VLS_362({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_362));
                const { default: __VLS_366 } = __VLS_364.slots;
                let __VLS_367;
                /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
                RefreshOutlined;
                // @ts-ignore
                const __VLS_368 = __VLS_asFunctionalComponent1(__VLS_367, new __VLS_367({}));
                const __VLS_369 = __VLS_368({}, ...__VLS_functionalComponentArgsRest(__VLS_368));
                // @ts-ignore
                [aiEndpointLoading, handleReinstallAiEndpoint,];
                var __VLS_364;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_355;
            var __VLS_356;
            let __VLS_372;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_373 = __VLS_asFunctionalComponent1(__VLS_372, new __VLS_372({
                ...{ 'onClick': {} },
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.aiEndpointLoading),
            }));
            const __VLS_374 = __VLS_373({
                ...{ 'onClick': {} },
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.aiEndpointLoading),
            }, ...__VLS_functionalComponentArgsRest(__VLS_373));
            let __VLS_377;
            const __VLS_378 = ({ click: {} },
                { onClick: (__VLS_ctx.handleUninstallAiEndpoint) });
            const { default: __VLS_379 } = __VLS_375.slots;
            // @ts-ignore
            [aiEndpointLoading, handleUninstallAiEndpoint,];
            var __VLS_375;
            var __VLS_376;
        }
    }
    if (__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.tools }) },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.4s' : '0.35s' }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                        return;
                    __VLS_ctx.collapsed.tools = !__VLS_ctx.collapsed.tools;
                    // @ts-ignore
                    [game, isInstalled, otherFrameworks, collapsed, collapsed, collapsed,];
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
        let __VLS_380;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_381 = __VLS_asFunctionalComponent1(__VLS_380, new __VLS_380({
            size: (16),
        }));
        const __VLS_382 = __VLS_381({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_381));
        const { default: __VLS_385 } = __VLS_383.slots;
        let __VLS_386;
        /** @ts-ignore @type {typeof __VLS_components.ExtensionOutlined} */
        ExtensionOutlined;
        // @ts-ignore
        const __VLS_387 = __VLS_asFunctionalComponent1(__VLS_386, new __VLS_386({}));
        const __VLS_388 = __VLS_387({}, ...__VLS_functionalComponentArgsRest(__VLS_387));
        // @ts-ignore
        [];
        var __VLS_383;
        let __VLS_391;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_392 = __VLS_asFunctionalComponent1(__VLS_391, new __VLS_391({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.tools }) },
        }));
        const __VLS_393 = __VLS_392({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.tools }) },
        }, ...__VLS_functionalComponentArgsRest(__VLS_392));
        /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
        /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
        const { default: __VLS_396 } = __VLS_394.slots;
        let __VLS_397;
        /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
        ExpandMoreOutlined;
        // @ts-ignore
        const __VLS_398 = __VLS_asFunctionalComponent1(__VLS_397, new __VLS_397({}));
        const __VLS_399 = __VLS_398({}, ...__VLS_functionalComponentArgsRest(__VLS_398));
        // @ts-ignore
        [collapsed,];
        var __VLS_394;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body" },
            ...{ class: ({ collapsed: __VLS_ctx.collapsed.tools }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
        /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body-inner" },
        });
        /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-list" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-list']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                        return;
                    __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/asset-extraction`);
                    // @ts-ignore
                    [router, collapsed, gameId,];
                } },
            ...{ class: "tool-item" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
        let __VLS_402;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_403 = __VLS_asFunctionalComponent1(__VLS_402, new __VLS_402({
            size: (18),
        }));
        const __VLS_404 = __VLS_403({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_403));
        const { default: __VLS_407 } = __VLS_405.slots;
        let __VLS_408;
        /** @ts-ignore @type {typeof __VLS_components.DataObjectOutlined} */
        DataObjectOutlined;
        // @ts-ignore
        const __VLS_409 = __VLS_asFunctionalComponent1(__VLS_408, new __VLS_408({}));
        const __VLS_410 = __VLS_409({}, ...__VLS_functionalComponentArgsRest(__VLS_409));
        // @ts-ignore
        [];
        var __VLS_405;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-content" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-title" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "tool-item-arrow" },
            width: "16",
            height: "16",
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M6 4L10 8L6 12",
            stroke: "currentColor",
            'stroke-width': "1.5",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                        return;
                    __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/translation-editor`);
                    // @ts-ignore
                    [router, gameId,];
                } },
            ...{ class: "tool-item" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
        let __VLS_413;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_414 = __VLS_asFunctionalComponent1(__VLS_413, new __VLS_413({
            size: (18),
        }));
        const __VLS_415 = __VLS_414({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_414));
        const { default: __VLS_418 } = __VLS_416.slots;
        let __VLS_419;
        /** @ts-ignore @type {typeof __VLS_components.DriveFileRenameOutlineOutlined} */
        DriveFileRenameOutlineOutlined;
        // @ts-ignore
        const __VLS_420 = __VLS_asFunctionalComponent1(__VLS_419, new __VLS_419({}));
        const __VLS_421 = __VLS_420({}, ...__VLS_functionalComponentArgsRest(__VLS_420));
        // @ts-ignore
        [];
        var __VLS_416;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-content" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-title" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "tool-item-arrow" },
            width: "16",
            height: "16",
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M6 4L10 8L6 12",
            stroke: "currentColor",
            'stroke-width': "1.5",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                        return;
                    __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/term-editor`);
                    // @ts-ignore
                    [router, gameId,];
                } },
            ...{ class: "tool-item" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
        let __VLS_424;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_425 = __VLS_asFunctionalComponent1(__VLS_424, new __VLS_424({
            size: (18),
        }));
        const __VLS_426 = __VLS_425({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_425));
        const { default: __VLS_429 } = __VLS_427.slots;
        let __VLS_430;
        /** @ts-ignore @type {typeof __VLS_components.MenuBookOutlined} */
        MenuBookOutlined;
        // @ts-ignore
        const __VLS_431 = __VLS_asFunctionalComponent1(__VLS_430, new __VLS_430({}));
        const __VLS_432 = __VLS_431({}, ...__VLS_functionalComponentArgsRest(__VLS_431));
        // @ts-ignore
        [];
        var __VLS_427;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-content" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-title" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "tool-item-arrow" },
            width: "16",
            height: "16",
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M6 4L10 8L6 12",
            stroke: "currentColor",
            'stroke-width': "1.5",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                        return;
                    __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/font-replacement`);
                    // @ts-ignore
                    [router, gameId,];
                } },
            ...{ class: "tool-item" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
        let __VLS_435;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_436 = __VLS_asFunctionalComponent1(__VLS_435, new __VLS_435({
            size: (18),
        }));
        const __VLS_437 = __VLS_436({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_436));
        const { default: __VLS_440 } = __VLS_438.slots;
        let __VLS_441;
        /** @ts-ignore @type {typeof __VLS_components.FontDownloadOutlined} */
        FontDownloadOutlined;
        // @ts-ignore
        const __VLS_442 = __VLS_asFunctionalComponent1(__VLS_441, new __VLS_441({}));
        const __VLS_443 = __VLS_442({}, ...__VLS_functionalComponentArgsRest(__VLS_442));
        // @ts-ignore
        [];
        var __VLS_438;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tool-item-content" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-title" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tool-item-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "tool-item-arrow" },
            width: "16",
            height: "16",
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M6 4L10 8L6 12",
            stroke: "currentColor",
            'stroke-width': "1.5",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
        if (__VLS_ctx.hasBepInEx) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                            return;
                        if (!(__VLS_ctx.hasBepInEx))
                            return;
                        __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/bepinex-log`);
                        // @ts-ignore
                        [router, hasBepInEx, gameId,];
                    } },
                ...{ class: "tool-item" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "tool-item-icon" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
            let __VLS_446;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_447 = __VLS_asFunctionalComponent1(__VLS_446, new __VLS_446({
                size: (18),
            }));
            const __VLS_448 = __VLS_447({
                size: (18),
            }, ...__VLS_functionalComponentArgsRest(__VLS_447));
            const { default: __VLS_451 } = __VLS_449.slots;
            let __VLS_452;
            /** @ts-ignore @type {typeof __VLS_components.ArticleOutlined} */
            ArticleOutlined;
            // @ts-ignore
            const __VLS_453 = __VLS_asFunctionalComponent1(__VLS_452, new __VLS_452({}));
            const __VLS_454 = __VLS_453({}, ...__VLS_functionalComponentArgsRest(__VLS_453));
            // @ts-ignore
            [];
            var __VLS_449;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "tool-item-content" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "tool-item-title" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "tool-item-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                ...{ class: "tool-item-arrow" },
                width: "16",
                height: "16",
                viewBox: "0 0 16 16",
                fill: "none",
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M6 4L10 8L6 12",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
        }
        if (__VLS_ctx.hasBepInEx) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!(__VLS_ctx.game.isUnityGame && __VLS_ctx.isInstalled))
                            return;
                        if (!(__VLS_ctx.hasBepInEx))
                            return;
                        __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}/plugin-manager`);
                        // @ts-ignore
                        [router, hasBepInEx, gameId,];
                    } },
                ...{ class: "tool-item" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "tool-item-icon" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-icon']} */ ;
            let __VLS_457;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_458 = __VLS_asFunctionalComponent1(__VLS_457, new __VLS_457({
                size: (18),
            }));
            const __VLS_459 = __VLS_458({
                size: (18),
            }, ...__VLS_functionalComponentArgsRest(__VLS_458));
            const { default: __VLS_462 } = __VLS_460.slots;
            let __VLS_463;
            /** @ts-ignore @type {typeof __VLS_components.WidgetsOutlined} */
            WidgetsOutlined;
            // @ts-ignore
            const __VLS_464 = __VLS_asFunctionalComponent1(__VLS_463, new __VLS_463({}));
            const __VLS_465 = __VLS_464({}, ...__VLS_functionalComponentArgsRest(__VLS_464));
            // @ts-ignore
            [];
            var __VLS_460;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "tool-item-content" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-content']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "tool-item-title" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-title']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "tool-item-desc" },
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-desc']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                ...{ class: "tool-item-arrow" },
                width: "16",
                height: "16",
                viewBox: "0 0 16 16",
                fill: "none",
            });
            /** @type {__VLS_StyleScopedClasses['tool-item-arrow']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M6 4L10 8L6 12",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
        }
    }
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.description }) },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.45s' : '0.4s' }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.game.isUnityGame))
                        return;
                    __VLS_ctx.collapsed.description = !__VLS_ctx.collapsed.description;
                    // @ts-ignore
                    [game, otherFrameworks, collapsed, collapsed, collapsed,];
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
        let __VLS_468;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_469 = __VLS_asFunctionalComponent1(__VLS_468, new __VLS_468({
            size: (16),
        }));
        const __VLS_470 = __VLS_469({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_469));
        const { default: __VLS_473 } = __VLS_471.slots;
        let __VLS_474;
        /** @ts-ignore @type {typeof __VLS_components.DescriptionOutlined} */
        DescriptionOutlined;
        // @ts-ignore
        const __VLS_475 = __VLS_asFunctionalComponent1(__VLS_474, new __VLS_474({}));
        const __VLS_476 = __VLS_475({}, ...__VLS_functionalComponentArgsRest(__VLS_475));
        // @ts-ignore
        [];
        var __VLS_471;
        let __VLS_479;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_480 = __VLS_asFunctionalComponent1(__VLS_479, new __VLS_479({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.description }) },
        }));
        const __VLS_481 = __VLS_480({
            size: (18),
            ...{ class: "collapse-chevron" },
            ...{ class: ({ expanded: !__VLS_ctx.collapsed.description }) },
        }, ...__VLS_functionalComponentArgsRest(__VLS_480));
        /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
        /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
        const { default: __VLS_484 } = __VLS_482.slots;
        let __VLS_485;
        /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
        ExpandMoreOutlined;
        // @ts-ignore
        const __VLS_486 = __VLS_asFunctionalComponent1(__VLS_485, new __VLS_485({}));
        const __VLS_487 = __VLS_486({}, ...__VLS_functionalComponentArgsRest(__VLS_486));
        // @ts-ignore
        [collapsed,];
        var __VLS_482;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body" },
            ...{ class: ({ collapsed: __VLS_ctx.collapsed.description }) },
        });
        /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
        /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-body-inner" },
        });
        /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
            ...{ class: "ai-description-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['ai-description-hint']} */ ;
        let __VLS_490;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_491 = __VLS_asFunctionalComponent1(__VLS_490, new __VLS_490({
            value: (__VLS_ctx.aiDescription),
            type: "textarea",
            rows: (4),
            placeholder: "例如：这是一款日式 RPG 游戏，背景设定在中世纪奇幻世界。主角是一名年轻的剑士，同伴包括女法师艾拉和精灵弓手雷恩...",
        }));
        const __VLS_492 = __VLS_491({
            value: (__VLS_ctx.aiDescription),
            type: "textarea",
            rows: (4),
            placeholder: "例如：这是一款日式 RPG 游戏，背景设定在中世纪奇幻世界。主角是一名年轻的剑士，同伴包括女法师艾拉和精灵弓手雷恩...",
        }, ...__VLS_functionalComponentArgsRest(__VLS_491));
    }
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: ({ animationDelay: __VLS_ctx.otherFrameworks.length > 0 ? '0.5s' : '0.45s' }) },
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
        let __VLS_495;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_496 = __VLS_asFunctionalComponent1(__VLS_495, new __VLS_495({
            size: (16),
        }));
        const __VLS_497 = __VLS_496({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_496));
        const { default: __VLS_500 } = __VLS_498.slots;
        let __VLS_501;
        /** @ts-ignore @type {typeof __VLS_components.Inventory2Outlined} */
        Inventory2Outlined;
        // @ts-ignore
        const __VLS_502 = __VLS_asFunctionalComponent1(__VLS_501, new __VLS_501({}));
        const __VLS_503 = __VLS_502({}, ...__VLS_functionalComponentArgsRest(__VLS_502));
        // @ts-ignore
        [game, otherFrameworks, collapsed, aiDescription,];
        var __VLS_498;
        __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
            ...{ class: "asset-extraction-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['asset-extraction-desc']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "pkg-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['pkg-actions']} */ ;
        let __VLS_506;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_507 = __VLS_asFunctionalComponent1(__VLS_506, new __VLS_506({
            ...{ 'onClick': {} },
            type: "primary",
            loading: (__VLS_ctx.packageExporting),
            disabled: (!__VLS_ctx.isInstalled),
        }));
        const __VLS_508 = __VLS_507({
            ...{ 'onClick': {} },
            type: "primary",
            loading: (__VLS_ctx.packageExporting),
            disabled: (!__VLS_ctx.isInstalled),
        }, ...__VLS_functionalComponentArgsRest(__VLS_507));
        let __VLS_511;
        const __VLS_512 = ({ click: {} },
            { onClick: (__VLS_ctx.handleExportPackage) });
        const { default: __VLS_513 } = __VLS_509.slots;
        {
            const { icon: __VLS_514 } = __VLS_509.slots;
            let __VLS_515;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_516 = __VLS_asFunctionalComponent1(__VLS_515, new __VLS_515({}));
            const __VLS_517 = __VLS_516({}, ...__VLS_functionalComponentArgsRest(__VLS_516));
            const { default: __VLS_520 } = __VLS_518.slots;
            let __VLS_521;
            /** @ts-ignore @type {typeof __VLS_components.Inventory2Outlined} */
            Inventory2Outlined;
            // @ts-ignore
            const __VLS_522 = __VLS_asFunctionalComponent1(__VLS_521, new __VLS_521({}));
            const __VLS_523 = __VLS_522({}, ...__VLS_functionalComponentArgsRest(__VLS_522));
            // @ts-ignore
            [isInstalled, packageExporting, handleExportPackage,];
            var __VLS_518;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_509;
        var __VLS_510;
        let __VLS_526;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_527 = __VLS_asFunctionalComponent1(__VLS_526, new __VLS_526({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.packageImporting),
        }));
        const __VLS_528 = __VLS_527({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.packageImporting),
        }, ...__VLS_functionalComponentArgsRest(__VLS_527));
        let __VLS_531;
        const __VLS_532 = ({ click: {} },
            { onClick: (__VLS_ctx.handleImportPackage) });
        const { default: __VLS_533 } = __VLS_529.slots;
        {
            const { icon: __VLS_534 } = __VLS_529.slots;
            let __VLS_535;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_536 = __VLS_asFunctionalComponent1(__VLS_535, new __VLS_535({}));
            const __VLS_537 = __VLS_536({}, ...__VLS_functionalComponentArgsRest(__VLS_536));
            const { default: __VLS_540 } = __VLS_538.slots;
            let __VLS_541;
            /** @ts-ignore @type {typeof __VLS_components.FileUploadOutlined} */
            FileUploadOutlined;
            // @ts-ignore
            const __VLS_542 = __VLS_asFunctionalComponent1(__VLS_541, new __VLS_541({}));
            const __VLS_543 = __VLS_542({}, ...__VLS_functionalComponentArgsRest(__VLS_542));
            // @ts-ignore
            [packageImporting, handleImportPackage,];
            var __VLS_538;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_529;
        var __VLS_530;
    }
    let __VLS_546;
    /** @ts-ignore @type {typeof __VLS_components.NDropdown} */
    NDropdown;
    // @ts-ignore
    const __VLS_547 = __VLS_asFunctionalComponent1(__VLS_546, new __VLS_546({
        ...{ 'onClickoutside': {} },
        ...{ 'onSelect': {} },
        trigger: "manual",
        show: (__VLS_ctx.showIconContextMenu),
        options: (__VLS_ctx.iconContextMenuOptions),
        x: (__VLS_ctx.iconContextMenuX),
        y: (__VLS_ctx.iconContextMenuY),
        placement: "bottom-start",
    }));
    const __VLS_548 = __VLS_547({
        ...{ 'onClickoutside': {} },
        ...{ 'onSelect': {} },
        trigger: "manual",
        show: (__VLS_ctx.showIconContextMenu),
        options: (__VLS_ctx.iconContextMenuOptions),
        x: (__VLS_ctx.iconContextMenuX),
        y: (__VLS_ctx.iconContextMenuY),
        placement: "bottom-start",
    }, ...__VLS_functionalComponentArgsRest(__VLS_547));
    let __VLS_551;
    const __VLS_552 = ({ clickoutside: {} },
        { onClickoutside: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.showIconContextMenu = false;
                // @ts-ignore
                [showIconContextMenu, showIconContextMenu, iconContextMenuOptions, iconContextMenuX, iconContextMenuY,];
            } });
    const __VLS_553 = ({ select: {} },
        { onSelect: (__VLS_ctx.handleIconContextMenuSelect) });
    var __VLS_549;
    var __VLS_550;
    if (__VLS_ctx.showCoverPicker) {
        let __VLS_554;
        /** @ts-ignore @type {typeof __VLS_components.CoverPickerModal} */
        CoverPickerModal;
        // @ts-ignore
        const __VLS_555 = __VLS_asFunctionalComponent1(__VLS_554, new __VLS_554({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showCoverPicker),
            game: (__VLS_ctx.game),
        }));
        const __VLS_556 = __VLS_555({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showCoverPicker),
            game: (__VLS_ctx.game),
        }, ...__VLS_functionalComponentArgsRest(__VLS_555));
        let __VLS_559;
        const __VLS_560 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.showCoverPicker))
                        return;
                    __VLS_ctx.showCoverPicker = $event;
                    // @ts-ignore
                    [game, handleIconContextMenuSelect, showCoverPicker, showCoverPicker, showCoverPicker,];
                } });
        const __VLS_561 = ({ saved: {} },
            { onSaved: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.showCoverPicker))
                        return;
                    __VLS_ctx.loadGame();
                    // @ts-ignore
                    [loadGame,];
                } });
        var __VLS_557;
        var __VLS_558;
    }
    if (__VLS_ctx.showIconPicker && __VLS_ctx.game) {
        let __VLS_562;
        /** @ts-ignore @type {typeof __VLS_components.IconPickerModal} */
        IconPickerModal;
        // @ts-ignore
        const __VLS_563 = __VLS_asFunctionalComponent1(__VLS_562, new __VLS_562({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showIconPicker),
            game: (__VLS_ctx.game),
        }));
        const __VLS_564 = __VLS_563({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showIconPicker),
            game: (__VLS_ctx.game),
        }, ...__VLS_functionalComponentArgsRest(__VLS_563));
        let __VLS_567;
        const __VLS_568 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.showIconPicker && __VLS_ctx.game))
                        return;
                    __VLS_ctx.showIconPicker = $event;
                    // @ts-ignore
                    [game, game, showIconPicker, showIconPicker, showIconPicker,];
                } });
        const __VLS_569 = ({ saved: {} },
            { onSaved: (__VLS_ctx.handleIconSaved) });
        var __VLS_565;
        var __VLS_566;
    }
    if (__VLS_ctx.showBackgroundPicker && __VLS_ctx.game) {
        let __VLS_570;
        /** @ts-ignore @type {typeof __VLS_components.BackgroundPickerModal} */
        BackgroundPickerModal;
        // @ts-ignore
        const __VLS_571 = __VLS_asFunctionalComponent1(__VLS_570, new __VLS_570({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showBackgroundPicker),
            game: (__VLS_ctx.game),
        }));
        const __VLS_572 = __VLS_571({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showBackgroundPicker),
            game: (__VLS_ctx.game),
        }, ...__VLS_functionalComponentArgsRest(__VLS_571));
        let __VLS_575;
        const __VLS_576 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    if (!(__VLS_ctx.showBackgroundPicker && __VLS_ctx.game))
                        return;
                    __VLS_ctx.showBackgroundPicker = $event;
                    // @ts-ignore
                    [game, game, showBackgroundPicker, showBackgroundPicker, showBackgroundPicker, handleIconSaved,];
                } });
        const __VLS_577 = ({ saved: {} },
            { onSaved: (__VLS_ctx.handleBackgroundSaved) });
        var __VLS_573;
        var __VLS_574;
    }
}
// @ts-ignore
[handleBackgroundSaved,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
