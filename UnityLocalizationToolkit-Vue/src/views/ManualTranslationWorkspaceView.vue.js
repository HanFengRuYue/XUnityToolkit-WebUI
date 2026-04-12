import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NEmpty, NInput, NSpin, NTag, NSwitch, useMessage } from 'naive-ui';
import { gamesApi, manualTranslationApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
defineOptions({ name: 'ManualTranslationWorkspaceView' });
const route = useRoute();
const router = useRouter();
const message = useMessage();
const { selectFile } = useFileExplorer();
const gameId = route.params['id'];
const game = ref(null);
const status = ref(null);
const assets = ref([]);
const selectedAsset = ref(null);
const selectedContent = ref(null);
const draftValue = ref('');
const search = ref('');
const scope = ref('all');
const editableOnly = ref(false);
const overriddenOnly = ref(false);
const loading = ref(true);
const scanning = ref(false);
const saving = ref(false);
const applying = ref(false);
const restoring = ref(false);
const packaging = ref(false);
const workspaceStats = computed(() => {
    const total = assets.value.length;
    const editable = assets.value.filter((item) => item.editable).length;
    const overridden = assets.value.filter((item) => item.overridden).length;
    return { total, editable, overridden };
});
const filteredAssets = computed(() => {
    return assets.value.filter((asset) => {
        if (scope.value === 'assets' && !['LooseAssetFile', 'BundleEntry'].includes(asset.storageKind))
            return false;
        if (scope.value === 'code' && !['ManagedAssembly', 'ManagedResource', 'Il2CppMetadata', 'NativeBinary'].includes(asset.storageKind))
            return false;
        if (editableOnly.value && !asset.editable)
            return false;
        if (overriddenOnly.value && !asset.overridden)
            return false;
        const query = search.value.trim().toLowerCase();
        if (!query)
            return true;
        return (asset.displayName?.toLowerCase().includes(query) ||
            asset.objectType.toLowerCase().includes(query) ||
            asset.preview?.toLowerCase().includes(query) ||
            asset.fieldPath?.toLowerCase().includes(query));
    });
});
const isSelectedDirty = computed(() => {
    if (!selectedContent.value)
        return false;
    return draftValue.value !== (selectedContent.value.overrideText ?? selectedContent.value.originalText ?? '');
});
watch(() => selectedContent.value?.assetId, () => {
    draftValue.value = selectedContent.value?.overrideText ?? selectedContent.value?.originalText ?? '';
});
watch([scope, editableOnly, overriddenOnly], async () => {
    if (status.value?.hasScan)
        await loadAssets();
});
let searchTimer;
watch(search, () => {
    if (!status.value?.hasScan)
        return;
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(() => {
        void loadAssets();
    }, 180);
});
onMounted(async () => {
    await loadWorkspace();
});
async function loadWorkspace() {
    loading.value = true;
    try {
        const [nextGame, nextStatus] = await Promise.all([
            gamesApi.get(gameId),
            manualTranslationApi.getStatus(gameId),
        ]);
        game.value = nextGame;
        status.value = nextStatus;
        if (nextStatus.hasScan) {
            await loadAssets();
        }
        else {
            assets.value = [];
            selectedAsset.value = null;
            selectedContent.value = null;
        }
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '加载手动翻译工作区失败');
    }
    finally {
        loading.value = false;
    }
}
async function loadAssets() {
    const response = await manualTranslationApi.getAssets(gameId, {
        scope: scope.value === 'all' ? undefined : scope.value,
        search: search.value || undefined,
        editableOnly: editableOnly.value,
        overriddenOnly: overriddenOnly.value,
    });
    assets.value = response.assets;
    if (selectedAsset.value) {
        const nextSelected = response.assets.find((item) => item.assetId === selectedAsset.value?.assetId) ?? null;
        if (nextSelected) {
            await selectAsset(nextSelected);
        }
        else {
            selectedAsset.value = null;
            selectedContent.value = null;
            draftValue.value = '';
        }
    }
}
async function selectAsset(asset) {
    selectedAsset.value = asset;
    selectedContent.value = await manualTranslationApi.getAssetContent(gameId, asset.assetId);
}
async function handleScan() {
    scanning.value = true;
    try {
        await manualTranslationApi.scan(gameId);
        message.success('手动翻译索引已刷新');
        await loadWorkspace();
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '扫描失败');
    }
    finally {
        scanning.value = false;
    }
}
async function handleSaveOverride() {
    if (!selectedAsset.value)
        return;
    saving.value = true;
    try {
        await manualTranslationApi.saveOverride(gameId, selectedAsset.value.assetId, draftValue.value, 'manual');
        message.success('覆盖内容已保存');
        await loadAssets();
        await selectAsset(selectedAsset.value);
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '保存覆盖失败');
    }
    finally {
        saving.value = false;
    }
}
async function handleDeleteOverride() {
    if (!selectedAsset.value)
        return;
    saving.value = true;
    try {
        await manualTranslationApi.deleteOverride(gameId, selectedAsset.value.assetId);
        message.success('覆盖内容已删除');
        await loadAssets();
        await selectAsset(selectedAsset.value);
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '删除覆盖失败');
    }
    finally {
        saving.value = false;
    }
}
async function handleApply() {
    applying.value = true;
    try {
        const result = await manualTranslationApi.apply(gameId);
        message.success(`已写回 ${result.appliedOverrides} 项覆盖，修改 ${result.modifiedFiles} 个文件`);
        if (result.warnings.length > 0)
            message.warning(result.warnings[0]);
        await loadWorkspace();
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '应用覆盖失败');
    }
    finally {
        applying.value = false;
    }
}
async function handleRestore() {
    restoring.value = true;
    try {
        await manualTranslationApi.restore(gameId);
        message.success('已从手动翻译备份恢复原始文件');
        await loadWorkspace();
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '恢复失败');
    }
    finally {
        restoring.value = false;
    }
}
async function handleBuildPackage() {
    packaging.value = true;
    try {
        const response = await fetch(manualTranslationApi.buildPackageUrl(gameId), { method: 'POST' });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(text || '构建补丁包失败');
        }
        const blob = await response.blob();
        const disposition = response.headers.get('content-disposition');
        const fileName = disposition?.match(/filename="?([^"]+)"?/)?.[1] ?? 'manual-translation.zip';
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        message.success('手动翻译补丁包已生成');
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '构建补丁包失败');
    }
    finally {
        packaging.value = false;
    }
}
async function handleImportPackage() {
    try {
        const zipPath = await selectFile({
            title: '选择手动翻译补丁包',
            filters: [{ label: 'ZIP 压缩包', extensions: ['.zip'] }],
        });
        if (!zipPath)
            return;
        packaging.value = true;
        await manualTranslationApi.importPackage(gameId, zipPath);
        message.success('补丁包已导入');
        await loadWorkspace();
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '导入补丁包失败');
    }
    finally {
        packaging.value = false;
    }
}
function handleExportSelected() {
    if (!selectedAsset.value)
        return;
    window.open(manualTranslationApi.exportAssetUrl(gameId, selectedAsset.value.assetId), '_blank');
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['workspace-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['switch-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['rail-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-title']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-stats']} */ ;
/** @type {__VLS_StyleScopedClasses['inspector-meta']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-rail']} */ ;
/** @type {__VLS_StyleScopedClasses['summary-metric']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-results']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-inspector']} */ ;
/** @type {__VLS_StyleScopedClasses['results-header']} */ ;
/** @type {__VLS_StyleScopedClasses['inspector-header']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['asset-row-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-inspector']} */ ;
/** @type {__VLS_StyleScopedClasses['text-panel']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-dock']} */ ;
/** @type {__VLS_StyleScopedClasses['dock-copy']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-shell']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-inspector']} */ ;
/** @type {__VLS_StyleScopedClasses['manual-workspace-page']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-hero']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-toolbar']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-dock']} */ ;
/** @type {__VLS_StyleScopedClasses['workspace-shell']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "manual-workspace-page" },
});
/** @type {__VLS_StyleScopedClasses['manual-workspace-page']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "workspace-hero" },
});
/** @type {__VLS_StyleScopedClasses['workspace-hero']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "workspace-copy" },
});
/** @type {__VLS_StyleScopedClasses['workspace-copy']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "workspace-eyebrow" },
});
/** @type {__VLS_StyleScopedClasses['workspace-eyebrow']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "workspace-switch" },
});
/** @type {__VLS_StyleScopedClasses['workspace-switch']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}`);
            // @ts-ignore
            [router, gameId,];
        } },
    ...{ class: "switch-chip" },
});
/** @type {__VLS_StyleScopedClasses['switch-chip']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ class: "switch-chip active" },
});
/** @type {__VLS_StyleScopedClasses['switch-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "workspace-loading" },
    });
    /** @type {__VLS_StyleScopedClasses['workspace-loading']} */ ;
    let __VLS_0;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
        size: "large",
    }));
    const __VLS_2 = __VLS_1({
        size: "large",
    }, ...__VLS_functionalComponentArgsRest(__VLS_1));
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "workspace-toolbar" },
    });
    /** @type {__VLS_StyleScopedClasses['workspace-toolbar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar-left" },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "workspace-title" },
    });
    /** @type {__VLS_StyleScopedClasses['workspace-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
    (__VLS_ctx.game?.name);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.status?.state ?? 'notScanned');
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar-stats" },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar-stats']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.status?.assetCount ?? 0);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.status?.editableAssetCount ?? 0);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.status?.overrideCount ?? 0);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar-actions']} */ ;
    let __VLS_5;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_6 = __VLS_asFunctionalComponent1(__VLS_5, new __VLS_5({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.scanning),
    }));
    const __VLS_7 = __VLS_6({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.scanning),
    }, ...__VLS_functionalComponentArgsRest(__VLS_6));
    let __VLS_10;
    const __VLS_11 = ({ click: {} },
        { onClick: (__VLS_ctx.handleScan) });
    const { default: __VLS_12 } = __VLS_8.slots;
    // @ts-ignore
    [loading, game, status, status, status, status, scanning, handleScan,];
    var __VLS_8;
    var __VLS_9;
    let __VLS_13;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_14 = __VLS_asFunctionalComponent1(__VLS_13, new __VLS_13({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.packaging),
    }));
    const __VLS_15 = __VLS_14({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.packaging),
    }, ...__VLS_functionalComponentArgsRest(__VLS_14));
    let __VLS_18;
    const __VLS_19 = ({ click: {} },
        { onClick: (__VLS_ctx.handleImportPackage) });
    const { default: __VLS_20 } = __VLS_16.slots;
    // @ts-ignore
    [packaging, handleImportPackage,];
    var __VLS_16;
    var __VLS_17;
    let __VLS_21;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.packaging),
    }));
    const __VLS_23 = __VLS_22({
        ...{ 'onClick': {} },
        secondary: true,
        loading: (__VLS_ctx.packaging),
    }, ...__VLS_functionalComponentArgsRest(__VLS_22));
    let __VLS_26;
    const __VLS_27 = ({ click: {} },
        { onClick: (__VLS_ctx.handleBuildPackage) });
    const { default: __VLS_28 } = __VLS_24.slots;
    // @ts-ignore
    [packaging, handleBuildPackage,];
    var __VLS_24;
    var __VLS_25;
    if (!__VLS_ctx.status?.hasScan) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "workspace-empty" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-empty']} */ ;
        let __VLS_29;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty | typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_30 = __VLS_asFunctionalComponent1(__VLS_29, new __VLS_29({
            description: "还没有建立手动翻译索引",
        }));
        const __VLS_31 = __VLS_30({
            description: "还没有建立手动翻译索引",
        }, ...__VLS_functionalComponentArgsRest(__VLS_30));
        const { default: __VLS_34 } = __VLS_32.slots;
        {
            const { extra: __VLS_35 } = __VLS_32.slots;
            let __VLS_36;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_37 = __VLS_asFunctionalComponent1(__VLS_36, new __VLS_36({
                ...{ 'onClick': {} },
                type: "primary",
                loading: (__VLS_ctx.scanning),
            }));
            const __VLS_38 = __VLS_37({
                ...{ 'onClick': {} },
                type: "primary",
                loading: (__VLS_ctx.scanning),
            }, ...__VLS_functionalComponentArgsRest(__VLS_37));
            let __VLS_41;
            const __VLS_42 = ({ click: {} },
                { onClick: (__VLS_ctx.handleScan) });
            const { default: __VLS_43 } = __VLS_39.slots;
            // @ts-ignore
            [status, scanning, handleScan,];
            var __VLS_39;
            var __VLS_40;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_32;
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "workspace-shell" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-shell']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.aside, __VLS_intrinsics.aside)({
            ...{ class: "workspace-rail" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-rail']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rail-section" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-section']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "rail-label" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rail-pills" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-pills']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!!(!__VLS_ctx.status?.hasScan))
                        return;
                    __VLS_ctx.scope = 'all';
                    // @ts-ignore
                    [scope,];
                } },
            ...{ class: "rail-pill" },
            ...{ class: ({ active: __VLS_ctx.scope === 'all' }) },
        });
        /** @type {__VLS_StyleScopedClasses['rail-pill']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!!(!__VLS_ctx.status?.hasScan))
                        return;
                    __VLS_ctx.scope = 'assets';
                    // @ts-ignore
                    [scope, scope,];
                } },
            ...{ class: "rail-pill" },
            ...{ class: ({ active: __VLS_ctx.scope === 'assets' }) },
        });
        /** @type {__VLS_StyleScopedClasses['rail-pill']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!!(!__VLS_ctx.status?.hasScan))
                        return;
                    __VLS_ctx.scope = 'code';
                    // @ts-ignore
                    [scope, scope,];
                } },
            ...{ class: "rail-pill" },
            ...{ class: ({ active: __VLS_ctx.scope === 'code' }) },
        });
        /** @type {__VLS_StyleScopedClasses['rail-pill']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rail-section" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-section']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "rail-label" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-label']} */ ;
        let __VLS_44;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({
            value: (__VLS_ctx.search),
            placeholder: "搜索名称、字段或内容",
        }));
        const __VLS_46 = __VLS_45({
            value: (__VLS_ctx.search),
            placeholder: "搜索名称、字段或内容",
        }, ...__VLS_functionalComponentArgsRest(__VLS_45));
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "rail-toggle" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-toggle']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        let __VLS_49;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_50 = __VLS_asFunctionalComponent1(__VLS_49, new __VLS_49({
            value: (__VLS_ctx.editableOnly),
        }));
        const __VLS_51 = __VLS_50({
            value: (__VLS_ctx.editableOnly),
        }, ...__VLS_functionalComponentArgsRest(__VLS_50));
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "rail-toggle" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-toggle']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        let __VLS_54;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_55 = __VLS_asFunctionalComponent1(__VLS_54, new __VLS_54({
            value: (__VLS_ctx.overriddenOnly),
        }));
        const __VLS_56 = __VLS_55({
            value: (__VLS_ctx.overriddenOnly),
        }, ...__VLS_functionalComponentArgsRest(__VLS_55));
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rail-section rail-summary" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-section']} */ ;
        /** @type {__VLS_StyleScopedClasses['rail-summary']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "rail-label" },
        });
        /** @type {__VLS_StyleScopedClasses['rail-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "summary-metric" },
        });
        /** @type {__VLS_StyleScopedClasses['summary-metric']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
        (__VLS_ctx.workspaceStats.total);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "summary-metric" },
        });
        /** @type {__VLS_StyleScopedClasses['summary-metric']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
        (__VLS_ctx.workspaceStats.editable);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "summary-metric" },
        });
        /** @type {__VLS_StyleScopedClasses['summary-metric']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
        (__VLS_ctx.workspaceStats.overridden);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.section, __VLS_intrinsics.section)({
            ...{ class: "workspace-results" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-results']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.header, __VLS_intrinsics.header)({
            ...{ class: "results-header" },
        });
        /** @type {__VLS_StyleScopedClasses['results-header']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "results-kicker" },
        });
        /** @type {__VLS_StyleScopedClasses['results-kicker']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "results-count" },
        });
        /** @type {__VLS_StyleScopedClasses['results-count']} */ ;
        (__VLS_ctx.filteredAssets.length);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "results-list" },
        });
        /** @type {__VLS_StyleScopedClasses['results-list']} */ ;
        for (const [asset] of __VLS_vFor((__VLS_ctx.filteredAssets))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!!(!__VLS_ctx.status?.hasScan))
                            return;
                        __VLS_ctx.selectAsset(asset);
                        // @ts-ignore
                        [scope, search, editableOnly, overriddenOnly, workspaceStats, workspaceStats, workspaceStats, filteredAssets, filteredAssets, selectAsset,];
                    } },
                key: (asset.assetId),
                ...{ class: "asset-row" },
                ...{ class: ({ active: __VLS_ctx.selectedAsset?.assetId === asset.assetId }) },
            });
            /** @type {__VLS_StyleScopedClasses['asset-row']} */ ;
            /** @type {__VLS_StyleScopedClasses['active']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "asset-row-copy" },
            });
            /** @type {__VLS_StyleScopedClasses['asset-row-copy']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
            (asset.displayName || asset.objectType);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (asset.objectType);
            (asset.assetFile);
            __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({});
            (asset.preview || asset.fieldPath || '无预览内容');
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "asset-row-tags" },
            });
            /** @type {__VLS_StyleScopedClasses['asset-row-tags']} */ ;
            let __VLS_59;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({
                size: "small",
                bordered: (false),
            }));
            const __VLS_61 = __VLS_60({
                size: "small",
                bordered: (false),
            }, ...__VLS_functionalComponentArgsRest(__VLS_60));
            const { default: __VLS_64 } = __VLS_62.slots;
            (asset.storageKind);
            // @ts-ignore
            [selectedAsset,];
            var __VLS_62;
            if (asset.editable) {
                let __VLS_65;
                /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
                NTag;
                // @ts-ignore
                const __VLS_66 = __VLS_asFunctionalComponent1(__VLS_65, new __VLS_65({
                    type: "success",
                    size: "small",
                    bordered: (false),
                }));
                const __VLS_67 = __VLS_66({
                    type: "success",
                    size: "small",
                    bordered: (false),
                }, ...__VLS_functionalComponentArgsRest(__VLS_66));
                const { default: __VLS_70 } = __VLS_68.slots;
                // @ts-ignore
                [];
                var __VLS_68;
            }
            if (asset.overridden) {
                let __VLS_71;
                /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
                NTag;
                // @ts-ignore
                const __VLS_72 = __VLS_asFunctionalComponent1(__VLS_71, new __VLS_71({
                    type: "warning",
                    size: "small",
                    bordered: (false),
                }));
                const __VLS_73 = __VLS_72({
                    type: "warning",
                    size: "small",
                    bordered: (false),
                }, ...__VLS_functionalComponentArgsRest(__VLS_72));
                const { default: __VLS_76 } = __VLS_74.slots;
                // @ts-ignore
                [];
                var __VLS_74;
            }
            // @ts-ignore
            [];
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.section, __VLS_intrinsics.section)({
            ...{ class: "workspace-inspector" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-inspector']} */ ;
        if (__VLS_ctx.selectedAsset && __VLS_ctx.selectedContent) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.header, __VLS_intrinsics.header)({
                ...{ class: "inspector-header" },
            });
            /** @type {__VLS_StyleScopedClasses['inspector-header']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "results-kicker" },
            });
            /** @type {__VLS_StyleScopedClasses['results-kicker']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({});
            (__VLS_ctx.selectedAsset.displayName || __VLS_ctx.selectedAsset.objectType);
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "inspector-actions" },
            });
            /** @type {__VLS_StyleScopedClasses['inspector-actions']} */ ;
            let __VLS_77;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_78 = __VLS_asFunctionalComponent1(__VLS_77, new __VLS_77({
                ...{ 'onClick': {} },
                secondary: true,
                size: "small",
            }));
            const __VLS_79 = __VLS_78({
                ...{ 'onClick': {} },
                secondary: true,
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_78));
            let __VLS_82;
            const __VLS_83 = ({ click: {} },
                { onClick: (__VLS_ctx.handleExportSelected) });
            const { default: __VLS_84 } = __VLS_80.slots;
            // @ts-ignore
            [selectedAsset, selectedAsset, selectedAsset, selectedContent, handleExportSelected,];
            var __VLS_80;
            var __VLS_81;
            if (__VLS_ctx.selectedAsset.overridden) {
                let __VLS_85;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_86 = __VLS_asFunctionalComponent1(__VLS_85, new __VLS_85({
                    ...{ 'onClick': {} },
                    tertiary: true,
                    size: "small",
                    loading: (__VLS_ctx.saving),
                }));
                const __VLS_87 = __VLS_86({
                    ...{ 'onClick': {} },
                    tertiary: true,
                    size: "small",
                    loading: (__VLS_ctx.saving),
                }, ...__VLS_functionalComponentArgsRest(__VLS_86));
                let __VLS_90;
                const __VLS_91 = ({ click: {} },
                    { onClick: (__VLS_ctx.handleDeleteOverride) });
                const { default: __VLS_92 } = __VLS_88.slots;
                // @ts-ignore
                [selectedAsset, saving, handleDeleteOverride,];
                var __VLS_88;
                var __VLS_89;
            }
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "inspector-meta" },
            });
            /** @type {__VLS_StyleScopedClasses['inspector-meta']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (__VLS_ctx.selectedAsset.assetFile);
            if (__VLS_ctx.selectedAsset.fieldPath) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
                (__VLS_ctx.selectedAsset.fieldPath);
            }
            if (__VLS_ctx.selectedAsset.editHint) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
                (__VLS_ctx.selectedAsset.editHint);
            }
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "inspector-panels" },
            });
            /** @type {__VLS_StyleScopedClasses['inspector-panels']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "text-panel" },
            });
            /** @type {__VLS_StyleScopedClasses['text-panel']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "panel-label" },
            });
            /** @type {__VLS_StyleScopedClasses['panel-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.pre, __VLS_intrinsics.pre)({});
            (__VLS_ctx.selectedContent.originalText || '该条目没有可直接渲染的文本内容。');
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "text-panel editable" },
            });
            /** @type {__VLS_StyleScopedClasses['text-panel']} */ ;
            /** @type {__VLS_StyleScopedClasses['editable']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "panel-label" },
            });
            /** @type {__VLS_StyleScopedClasses['panel-label']} */ ;
            let __VLS_93;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
                value: (__VLS_ctx.draftValue),
                type: "textarea",
                autosize: ({ minRows: 12, maxRows: 24 }),
                disabled: (!__VLS_ctx.selectedAsset.editable),
                placeholder: "这里会保存手动翻译覆盖文本",
            }));
            const __VLS_95 = __VLS_94({
                value: (__VLS_ctx.draftValue),
                type: "textarea",
                autosize: ({ minRows: 12, maxRows: 24 }),
                disabled: (!__VLS_ctx.selectedAsset.editable),
                placeholder: "这里会保存手动翻译覆盖文本",
            }, ...__VLS_functionalComponentArgsRest(__VLS_94));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "workspace-empty inline-empty" },
            });
            /** @type {__VLS_StyleScopedClasses['workspace-empty']} */ ;
            /** @type {__VLS_StyleScopedClasses['inline-empty']} */ ;
            let __VLS_98;
            /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
            NEmpty;
            // @ts-ignore
            const __VLS_99 = __VLS_asFunctionalComponent1(__VLS_98, new __VLS_98({
                description: "从左侧选择一项资源或代码字符串后，这里会显示差异编辑器。",
            }));
            const __VLS_100 = __VLS_99({
                description: "从左侧选择一项资源或代码字符串后，这里会显示差异编辑器。",
            }, ...__VLS_functionalComponentArgsRest(__VLS_99));
        }
    }
    if (__VLS_ctx.status?.hasScan) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.footer, __VLS_intrinsics.footer)({
            ...{ class: "workspace-dock" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-dock']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dock-copy" },
        });
        /** @type {__VLS_StyleScopedClasses['dock-copy']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dock-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['dock-actions']} */ ;
        let __VLS_103;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_104 = __VLS_asFunctionalComponent1(__VLS_103, new __VLS_103({
            ...{ 'onClick': {} },
            disabled: (!__VLS_ctx.selectedAsset?.editable || !__VLS_ctx.isSelectedDirty),
            loading: (__VLS_ctx.saving),
        }));
        const __VLS_105 = __VLS_104({
            ...{ 'onClick': {} },
            disabled: (!__VLS_ctx.selectedAsset?.editable || !__VLS_ctx.isSelectedDirty),
            loading: (__VLS_ctx.saving),
        }, ...__VLS_functionalComponentArgsRest(__VLS_104));
        let __VLS_108;
        const __VLS_109 = ({ click: {} },
            { onClick: (__VLS_ctx.handleSaveOverride) });
        const { default: __VLS_110 } = __VLS_106.slots;
        // @ts-ignore
        [status, selectedAsset, selectedAsset, selectedAsset, selectedAsset, selectedAsset, selectedAsset, selectedAsset, selectedContent, saving, draftValue, isSelectedDirty, handleSaveOverride,];
        var __VLS_106;
        var __VLS_107;
        let __VLS_111;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_112 = __VLS_asFunctionalComponent1(__VLS_111, new __VLS_111({
            ...{ 'onClick': {} },
            secondary: true,
            loading: (__VLS_ctx.restoring),
        }));
        const __VLS_113 = __VLS_112({
            ...{ 'onClick': {} },
            secondary: true,
            loading: (__VLS_ctx.restoring),
        }, ...__VLS_functionalComponentArgsRest(__VLS_112));
        let __VLS_116;
        const __VLS_117 = ({ click: {} },
            { onClick: (__VLS_ctx.handleRestore) });
        const { default: __VLS_118 } = __VLS_114.slots;
        // @ts-ignore
        [restoring, handleRestore,];
        var __VLS_114;
        var __VLS_115;
        let __VLS_119;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_120 = __VLS_asFunctionalComponent1(__VLS_119, new __VLS_119({
            ...{ 'onClick': {} },
            type: "primary",
            loading: (__VLS_ctx.applying),
        }));
        const __VLS_121 = __VLS_120({
            ...{ 'onClick': {} },
            type: "primary",
            loading: (__VLS_ctx.applying),
        }, ...__VLS_functionalComponentArgsRest(__VLS_120));
        let __VLS_124;
        const __VLS_125 = ({ click: {} },
            { onClick: (__VLS_ctx.handleApply) });
        const { default: __VLS_126 } = __VLS_122.slots;
        // @ts-ignore
        [applying, handleApply,];
        var __VLS_122;
        var __VLS_123;
    }
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
