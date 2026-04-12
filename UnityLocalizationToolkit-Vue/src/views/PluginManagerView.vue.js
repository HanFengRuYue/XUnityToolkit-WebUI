import { ref, computed, onMounted, h } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NDataTable, NIcon, NTag, NSwitch, NDrawer, NDrawerContent, NEmpty, NSpin, useMessage, useDialog } from 'naive-ui';
import { ArrowBackOutlined, WidgetsOutlined, DeleteOutlineOutlined, SettingsOutlined, FolderOpenOutlined, RefreshOutlined } from '@vicons/material';
import { gamesApi, bepinexPluginApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { formatBytes } from '@/utils/format';
const route = useRoute();
const router = useRouter();
const message = useMessage();
const dialog = useDialog();
const { selectFile } = useFileExplorer();
const gameId = computed(() => route.params.id);
const game = ref(null);
const plugins = ref([]);
const loading = ref(true);
const installing = ref(false);
// Config drawer state
const showConfigDrawer = ref(false);
const configContent = ref('');
const configFileName = ref('');
const configLoading = ref(false);
const columns = computed(() => [
    {
        title: '插件名称',
        key: 'name',
        minWidth: 180,
        render: (row) => {
            const name = row.pluginName ?? row.fileName;
            const children = [h('span', { class: 'plugin-name-text' }, name)];
            if (row.isToolkitManaged) {
                children.push(h(NTag, { size: 'tiny', bordered: false, type: 'info', style: 'margin-left: 8px' }, { default: () => '工具箱' }));
            }
            return h('div', { class: 'plugin-name-cell' }, children);
        }
    },
    {
        title: '大小',
        key: 'fileSize',
        width: 90,
        render: (row) => formatBytes(row.fileSize)
    },
    {
        title: '状态',
        key: 'enabled',
        width: 80,
        render: (row) => h(NSwitch, {
            value: row.enabled,
            disabled: row.isToolkitManaged,
            size: 'small',
            onUpdateValue: () => togglePlugin(row),
        })
    },
    {
        title: '操作',
        key: 'actions',
        width: 120,
        render: (row) => {
            const actions = [];
            if (row.configFileName) {
                actions.push(h(NButton, {
                    size: 'tiny',
                    quaternary: true,
                    onClick: () => viewConfig(row),
                }, { icon: () => h(NIcon, { size: 14 }, { default: () => h(SettingsOutlined) }) }));
            }
            if (!row.isToolkitManaged) {
                actions.push(h(NButton, {
                    size: 'tiny',
                    quaternary: true,
                    type: 'error',
                    onClick: () => confirmUninstall(row),
                }, { icon: () => h(NIcon, { size: 14 }, { default: () => h(DeleteOutlineOutlined) }) }));
            }
            return h('div', { class: 'action-btns' }, actions);
        }
    },
]);
async function loadGame() {
    loading.value = true;
    try {
        game.value = await gamesApi.get(gameId.value);
        await loadPlugins();
    }
    catch {
        message.error('加载游戏信息失败');
    }
    finally {
        loading.value = false;
    }
}
async function loadPlugins() {
    try {
        plugins.value = await bepinexPluginApi.list(gameId.value);
    }
    catch {
        message.error('加载插件列表失败');
    }
}
async function selectAndInstall() {
    try {
        const filePath = await selectFile({
            title: '选择插件文件',
            filters: [
                { label: 'DLL 文件', extensions: ['.dll'] },
                { label: 'ZIP 压缩包', extensions: ['.zip'] },
                { label: '所有文件', extensions: [] },
            ],
        });
        if (!filePath)
            return;
        installing.value = true;
        await bepinexPluginApi.install(gameId.value, filePath);
        message.success('插件安装成功');
        await loadPlugins();
    }
    catch (e) {
        message.error(e?.message || '安装插件失败');
    }
    finally {
        installing.value = false;
    }
}
function confirmUninstall(plugin) {
    dialog.warning({
        title: '确认卸载',
        content: `确定要卸载插件 "${plugin.pluginName ?? plugin.fileName}" 吗？此操作不可撤销。`,
        positiveText: '卸载',
        negativeText: '取消',
        onPositiveClick: () => {
            uninstallPlugin(plugin);
        }
    });
}
async function uninstallPlugin(plugin) {
    try {
        await bepinexPluginApi.uninstall(gameId.value, plugin.relativePath);
        message.success('插件已卸载');
        await loadPlugins();
    }
    catch (e) {
        message.error(e?.message || '卸载插件失败');
    }
}
async function togglePlugin(plugin) {
    try {
        const updated = await bepinexPluginApi.toggle(gameId.value, plugin.relativePath);
        // Update the local list
        const idx = plugins.value.findIndex(p => p.relativePath === plugin.relativePath);
        if (idx >= 0)
            plugins.value[idx] = updated;
        message.success(updated.enabled ? '插件已启用' : '插件已禁用');
    }
    catch (e) {
        message.error(e?.message || '切换插件状态失败');
    }
}
async function viewConfig(plugin) {
    if (!plugin.configFileName)
        return;
    configFileName.value = plugin.configFileName;
    configContent.value = '';
    showConfigDrawer.value = true;
    configLoading.value = true;
    try {
        const content = await bepinexPluginApi.getConfig(gameId.value, plugin.configFileName);
        configContent.value = content ?? '（配置文件为空）';
    }
    catch {
        configContent.value = '（无法读取配置文件）';
    }
    finally {
        configLoading.value = false;
    }
}
onMounted(loadGame);
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
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
    /** @ts-ignore @type {typeof __VLS_components.WidgetsOutlined} */
    WidgetsOutlined;
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
    /** @ts-ignore @type {typeof __VLS_components.WidgetsOutlined} */
    WidgetsOutlined;
    // @ts-ignore
    const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
    const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
    // @ts-ignore
    [];
    var __VLS_25;
    if (__VLS_ctx.plugins.length > 0) {
        let __VLS_33;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
            size: "small",
            bordered: (false),
            round: true,
            ...{ style: {} },
        }));
        const __VLS_35 = __VLS_34({
            size: "small",
            bordered: (false),
            round: true,
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_34));
        const { default: __VLS_38 } = __VLS_36.slots;
        (__VLS_ctx.plugins.length);
        // @ts-ignore
        [plugins, plugins,];
        var __VLS_36;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    let __VLS_39;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({
        ...{ 'onClick': {} },
        size: "small",
        loading: (__VLS_ctx.loading),
        quaternary: true,
    }));
    const __VLS_41 = __VLS_40({
        ...{ 'onClick': {} },
        size: "small",
        loading: (__VLS_ctx.loading),
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_40));
    let __VLS_44;
    const __VLS_45 = ({ click: {} },
        { onClick: (__VLS_ctx.loadPlugins) });
    const { default: __VLS_46 } = __VLS_42.slots;
    {
        const { icon: __VLS_47 } = __VLS_42.slots;
        let __VLS_48;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({
            size: (16),
        }));
        const __VLS_50 = __VLS_49({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_49));
        const { default: __VLS_53 } = __VLS_51.slots;
        let __VLS_54;
        /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
        RefreshOutlined;
        // @ts-ignore
        const __VLS_55 = __VLS_asFunctionalComponent1(__VLS_54, new __VLS_54({}));
        const __VLS_56 = __VLS_55({}, ...__VLS_functionalComponentArgsRest(__VLS_55));
        // @ts-ignore
        [loading, loadPlugins,];
        var __VLS_51;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_42;
    var __VLS_43;
    let __VLS_59;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({
        ...{ 'onClick': {} },
        size: "small",
        loading: (__VLS_ctx.installing),
    }));
    const __VLS_61 = __VLS_60({
        ...{ 'onClick': {} },
        size: "small",
        loading: (__VLS_ctx.installing),
    }, ...__VLS_functionalComponentArgsRest(__VLS_60));
    let __VLS_64;
    const __VLS_65 = ({ click: {} },
        { onClick: (__VLS_ctx.selectAndInstall) });
    const { default: __VLS_66 } = __VLS_62.slots;
    {
        const { icon: __VLS_67 } = __VLS_62.slots;
        let __VLS_68;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_69 = __VLS_asFunctionalComponent1(__VLS_68, new __VLS_68({
            size: (16),
        }));
        const __VLS_70 = __VLS_69({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_69));
        const { default: __VLS_73 } = __VLS_71.slots;
        let __VLS_74;
        /** @ts-ignore @type {typeof __VLS_components.FolderOpenOutlined} */
        FolderOpenOutlined;
        // @ts-ignore
        const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({}));
        const __VLS_76 = __VLS_75({}, ...__VLS_functionalComponentArgsRest(__VLS_75));
        // @ts-ignore
        [installing, selectAndInstall,];
        var __VLS_71;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_62;
    var __VLS_63;
    if (__VLS_ctx.loading) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "loading-state" },
        });
        /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
        let __VLS_79;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({
            size: "medium",
        }));
        const __VLS_81 = __VLS_80({
            size: "medium",
        }, ...__VLS_functionalComponentArgsRest(__VLS_80));
    }
    else {
        if (__VLS_ctx.plugins.length > 0) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "table-container" },
            });
            /** @type {__VLS_StyleScopedClasses['table-container']} */ ;
            let __VLS_84;
            /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
            NDataTable;
            // @ts-ignore
            const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({
                columns: (__VLS_ctx.columns),
                data: (__VLS_ctx.plugins),
                rowKey: ((row) => row.relativePath),
                size: "small",
                bordered: (false),
            }));
            const __VLS_86 = __VLS_85({
                columns: (__VLS_ctx.columns),
                data: (__VLS_ctx.plugins),
                rowKey: ((row) => row.relativePath),
                size: "small",
                bordered: (false),
            }, ...__VLS_functionalComponentArgsRest(__VLS_85));
        }
        else {
            let __VLS_89;
            /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
            NEmpty;
            // @ts-ignore
            const __VLS_90 = __VLS_asFunctionalComponent1(__VLS_89, new __VLS_89({
                description: "暂无已安装的插件",
                ...{ style: {} },
            }));
            const __VLS_91 = __VLS_90({
                description: "暂无已安装的插件",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_90));
        }
    }
    let __VLS_94;
    /** @ts-ignore @type {typeof __VLS_components.NDrawer | typeof __VLS_components.NDrawer} */
    NDrawer;
    // @ts-ignore
    const __VLS_95 = __VLS_asFunctionalComponent1(__VLS_94, new __VLS_94({
        show: (__VLS_ctx.showConfigDrawer),
        width: (520),
        placement: "right",
    }));
    const __VLS_96 = __VLS_95({
        show: (__VLS_ctx.showConfigDrawer),
        width: (520),
        placement: "right",
    }, ...__VLS_functionalComponentArgsRest(__VLS_95));
    const { default: __VLS_99 } = __VLS_97.slots;
    let __VLS_100;
    /** @ts-ignore @type {typeof __VLS_components.NDrawerContent | typeof __VLS_components.NDrawerContent} */
    NDrawerContent;
    // @ts-ignore
    const __VLS_101 = __VLS_asFunctionalComponent1(__VLS_100, new __VLS_100({
        title: (__VLS_ctx.configFileName),
    }));
    const __VLS_102 = __VLS_101({
        title: (__VLS_ctx.configFileName),
    }, ...__VLS_functionalComponentArgsRest(__VLS_101));
    const { default: __VLS_105 } = __VLS_103.slots;
    if (__VLS_ctx.configLoading) {
        let __VLS_106;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_107 = __VLS_asFunctionalComponent1(__VLS_106, new __VLS_106({
            size: "medium",
            ...{ style: {} },
        }));
        const __VLS_108 = __VLS_107({
            size: "medium",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_107));
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.pre, __VLS_intrinsics.pre)({
            ...{ class: "config-content" },
        });
        /** @type {__VLS_StyleScopedClasses['config-content']} */ ;
        (__VLS_ctx.configContent);
    }
    // @ts-ignore
    [plugins, plugins, loading, columns, showConfigDrawer, configFileName, configLoading, configContent,];
    var __VLS_103;
    // @ts-ignore
    [];
    var __VLS_97;
}
else if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "loading-state" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
    let __VLS_111;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_112 = __VLS_asFunctionalComponent1(__VLS_111, new __VLS_111({
        size: "large",
    }));
    const __VLS_113 = __VLS_112({
        size: "large",
    }, ...__VLS_functionalComponentArgsRest(__VLS_112));
}
// @ts-ignore
[loading,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
