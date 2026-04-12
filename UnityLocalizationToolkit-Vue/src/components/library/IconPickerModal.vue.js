import { ref, watch } from 'vue';
import { NModal, NTabs, NTabPane, NIcon, NButton, NInput, NSpin, useMessage } from 'naive-ui';
import { SearchRound, CloudUploadOutlined } from '@vicons/material';
import { gamesApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import WebImageSearchTab from './WebImageSearchTab.vue';
const props = defineProps();
const emit = defineEmits();
const message = useMessage();
const { selectFile } = useFileExplorer();
// SteamGridDB search state
const searchQuery = ref(props.game.name);
const searchResults = ref([]);
const searching = ref(false);
const selectedGameId = ref(props.game.steamGridDbGameId ?? null);
const iconImages = ref([]);
const loadingIcons = ref(false);
const savingIcon = ref(false);
// Upload state
const dragOver = ref(false);
const uploadPreview = ref(null);
const uploadFile = ref(null);
const uploading = ref(false);
watch(() => props.game, (game) => {
    searchQuery.value = game.name;
    selectedGameId.value = game.steamGridDbGameId ?? null;
    if (game.steamGridDbGameId) {
        loadIcons(game.steamGridDbGameId);
    }
});
function onWebSaved() {
    emit('saved');
    emit('update:show', false);
}
// SteamGridDB functions
async function searchSteamGridDb() {
    if (!searchQuery.value.trim())
        return;
    searching.value = true;
    searchResults.value = [];
    selectedGameId.value = null;
    iconImages.value = [];
    try {
        searchResults.value = await gamesApi.searchIconGames(props.game.id, searchQuery.value.trim());
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '搜索失败');
    }
    finally {
        searching.value = false;
    }
}
async function selectSearchResult(result) {
    selectedGameId.value = result.id;
    await loadIcons(result.id);
}
async function loadIcons(steamGridDbGameId) {
    loadingIcons.value = true;
    iconImages.value = [];
    try {
        iconImages.value = await gamesApi.getIconGrids(props.game.id, steamGridDbGameId);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '获取图标列表失败');
    }
    finally {
        loadingIcons.value = false;
    }
}
async function selectIconImage(image) {
    if (!selectedGameId.value)
        return;
    savingIcon.value = true;
    try {
        await gamesApi.selectSteamGridDbIcon(props.game.id, image.url, selectedGameId.value);
        message.success('图标已更新');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '保存图标失败');
    }
    finally {
        savingIcon.value = false;
    }
}
// Upload functions
async function handleSelectIconFile() {
    const path = await selectFile({
        title: '选择图标图片',
        filters: [{ label: '图片文件', extensions: ['.jpg', '.jpeg', '.png', '.webp'] }],
    });
    if (!path)
        return;
    uploading.value = true;
    try {
        await gamesApi.uploadIconFromPath(props.game.id, path);
        message.success('图标已更新');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '上传失败');
    }
    finally {
        uploading.value = false;
    }
}
function handleFileSelect(e) {
    const input = e.target;
    const file = input.files?.[0];
    if (file)
        prepareUpload(file);
}
function handleDrop(e) {
    dragOver.value = false;
    const file = e.dataTransfer?.files?.[0];
    if (file)
        prepareUpload(file);
}
function prepareUpload(file) {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
        message.error('仅支持 JPEG、PNG 或 WebP 格式');
        return;
    }
    if (file.size > 5 * 1024 * 1024) {
        message.error('图片文件不能超过 5 MB');
        return;
    }
    uploadFile.value = file;
    const reader = new FileReader();
    reader.onload = (e) => { uploadPreview.value = e.target?.result; };
    reader.readAsDataURL(file);
}
async function confirmUpload() {
    if (!uploadFile.value)
        return;
    uploading.value = true;
    try {
        await gamesApi.uploadIcon(props.game.id, uploadFile.value);
        message.success('图标已更新');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '上传失败');
    }
    finally {
        uploading.value = false;
    }
}
function clearUpload() {
    uploadFile.value = null;
    uploadPreview.value = null;
}
async function deleteIcon() {
    try {
        await gamesApi.deleteCustomIcon(props.game.id);
        message.success('自定义图标已删除');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '删除失败');
    }
}
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
/** @type {__VLS_StyleScopedClasses['search-result-item']} */ ;
/** @type {__VLS_StyleScopedClasses['icon-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['icon-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['icon-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['icon-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['icon-image-overlay']} */ ;
/** @type {__VLS_StyleScopedClasses['upload-zone']} */ ;
/** @type {__VLS_StyleScopedClasses['upload-zone']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NModal | typeof __VLS_components.NModal} */
NModal;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    ...{ 'onUpdate:show': {} },
    show: (__VLS_ctx.show),
    preset: "card",
    title: "更换游戏图标",
    ...{ style: {} },
    maskClosable: (true),
}));
const __VLS_2 = __VLS_1({
    ...{ 'onUpdate:show': {} },
    show: (__VLS_ctx.show),
    preset: "card",
    title: "更换游戏图标",
    ...{ style: {} },
    maskClosable: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
let __VLS_5;
const __VLS_6 = ({ 'update:show': {} },
    { 'onUpdate:show': (...[$event]) => {
            __VLS_ctx.emit('update:show', $event);
            // @ts-ignore
            [show, emit,];
        } });
var __VLS_7 = {};
const { default: __VLS_8 } = __VLS_3.slots;
let __VLS_9;
/** @ts-ignore @type {typeof __VLS_components.NTabs | typeof __VLS_components.NTabs} */
NTabs;
// @ts-ignore
const __VLS_10 = __VLS_asFunctionalComponent1(__VLS_9, new __VLS_9({
    type: "segment",
    animated: true,
}));
const __VLS_11 = __VLS_10({
    type: "segment",
    animated: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_10));
const { default: __VLS_14 } = __VLS_12.slots;
let __VLS_15;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_16 = __VLS_asFunctionalComponent1(__VLS_15, new __VLS_15({
    name: "steamgriddb",
    tab: "SteamGridDB",
}));
const __VLS_17 = __VLS_16({
    name: "steamgriddb",
    tab: "SteamGridDB",
}, ...__VLS_functionalComponentArgsRest(__VLS_16));
const { default: __VLS_20 } = __VLS_18.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-section" },
});
/** @type {__VLS_StyleScopedClasses['search-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-bar" },
});
/** @type {__VLS_StyleScopedClasses['search-bar']} */ ;
let __VLS_21;
/** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}));
const __VLS_23 = __VLS_22({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_22));
let __VLS_26;
const __VLS_27 = ({ keyup: {} },
    { onKeyup: (__VLS_ctx.searchSteamGridDb) });
const { default: __VLS_28 } = __VLS_24.slots;
{
    const { suffix: __VLS_29 } = __VLS_24.slots;
    let __VLS_30;
    /** @ts-ignore @type {typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_31 = __VLS_asFunctionalComponent1(__VLS_30, new __VLS_30({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }));
    const __VLS_32 = __VLS_31({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_31));
    let __VLS_35;
    const __VLS_36 = ({ click: {} },
        { onClick: (__VLS_ctx.searchSteamGridDb) });
    var __VLS_33;
    var __VLS_34;
    // @ts-ignore
    [searchQuery, searchSteamGridDb, searchSteamGridDb, SearchRound,];
}
// @ts-ignore
[];
var __VLS_24;
var __VLS_25;
if (__VLS_ctx.searching) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    let __VLS_37;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_38 = __VLS_asFunctionalComponent1(__VLS_37, new __VLS_37({
        size: "small",
    }));
    const __VLS_39 = __VLS_38({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_38));
}
else if (__VLS_ctx.searchResults.length > 0 && !__VLS_ctx.selectedGameId) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "search-results" },
    });
    /** @type {__VLS_StyleScopedClasses['search-results']} */ ;
    for (const [result] of __VLS_vFor((__VLS_ctx.searchResults))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.searching))
                        return;
                    if (!(__VLS_ctx.searchResults.length > 0 && !__VLS_ctx.selectedGameId))
                        return;
                    __VLS_ctx.selectSearchResult(result);
                    // @ts-ignore
                    [searching, searchResults, searchResults, selectedGameId, selectSearchResult,];
                } },
            key: (result.id),
            ...{ class: "search-result-item" },
        });
        /** @type {__VLS_StyleScopedClasses['search-result-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "result-name" },
        });
        /** @type {__VLS_StyleScopedClasses['result-name']} */ ;
        (result.name);
        if (result.verified) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "result-verified" },
            });
            /** @type {__VLS_StyleScopedClasses['result-verified']} */ ;
        }
        // @ts-ignore
        [];
    }
}
if (__VLS_ctx.selectedGameId) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "icons-section" },
    });
    /** @type {__VLS_StyleScopedClasses['icons-section']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "icons-header" },
    });
    /** @type {__VLS_StyleScopedClasses['icons-header']} */ ;
    let __VLS_42;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
    }));
    const __VLS_44 = __VLS_43({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_43));
    let __VLS_47;
    const __VLS_48 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.selectedGameId))
                    return;
                __VLS_ctx.selectedGameId = null;
                __VLS_ctx.iconImages = [];
                // @ts-ignore
                [selectedGameId, selectedGameId, iconImages,];
            } });
    const { default: __VLS_49 } = __VLS_45.slots;
    // @ts-ignore
    [];
    var __VLS_45;
    var __VLS_46;
    if (__VLS_ctx.loadingIcons) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "center-state" },
        });
        /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
        let __VLS_50;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_51 = __VLS_asFunctionalComponent1(__VLS_50, new __VLS_50({
            size: "small",
        }));
        const __VLS_52 = __VLS_51({
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_51));
    }
    else if (__VLS_ctx.iconImages.length === 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "center-state" },
        });
        /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "muted-text" },
        });
        /** @type {__VLS_StyleScopedClasses['muted-text']} */ ;
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "icons-grid" },
        });
        /** @type {__VLS_StyleScopedClasses['icons-grid']} */ ;
        for (const [image] of __VLS_vFor((__VLS_ctx.iconImages))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!(__VLS_ctx.selectedGameId))
                            return;
                        if (!!(__VLS_ctx.loadingIcons))
                            return;
                        if (!!(__VLS_ctx.iconImages.length === 0))
                            return;
                        __VLS_ctx.selectIconImage(image);
                        // @ts-ignore
                        [iconImages, iconImages, loadingIcons, selectIconImage,];
                    } },
                key: (image.id),
                ...{ class: "icon-image-item" },
                ...{ class: ({ saving: __VLS_ctx.savingIcon }) },
            });
            /** @type {__VLS_StyleScopedClasses['icon-image-item']} */ ;
            /** @type {__VLS_StyleScopedClasses['saving']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
                src: (image.thumb),
                alt: (`Icon ${image.id}`),
                loading: "lazy",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "icon-image-overlay" },
            });
            /** @type {__VLS_StyleScopedClasses['icon-image-overlay']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (image.width);
            (image.height);
            // @ts-ignore
            [savingIcon,];
        }
    }
}
if (!__VLS_ctx.searching && !__VLS_ctx.selectedGameId && __VLS_ctx.searchResults.length === 0 && __VLS_ctx.searchQuery.trim()) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "muted-text" },
    });
    /** @type {__VLS_StyleScopedClasses['muted-text']} */ ;
}
// @ts-ignore
[searchQuery, searching, searchResults, selectedGameId,];
var __VLS_18;
let __VLS_55;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_56 = __VLS_asFunctionalComponent1(__VLS_55, new __VLS_55({
    name: "web",
    tab: "网络搜索",
}));
const __VLS_57 = __VLS_56({
    name: "web",
    tab: "网络搜索",
}, ...__VLS_functionalComponentArgsRest(__VLS_56));
const { default: __VLS_60 } = __VLS_58.slots;
const __VLS_61 = WebImageSearchTab;
// @ts-ignore
const __VLS_62 = __VLS_asFunctionalComponent1(__VLS_61, new __VLS_61({
    ...{ 'onSaved': {} },
    game: (__VLS_ctx.game),
    mode: "icon",
}));
const __VLS_63 = __VLS_62({
    ...{ 'onSaved': {} },
    game: (__VLS_ctx.game),
    mode: "icon",
}, ...__VLS_functionalComponentArgsRest(__VLS_62));
let __VLS_66;
const __VLS_67 = ({ saved: {} },
    { onSaved: (__VLS_ctx.onWebSaved) });
var __VLS_64;
var __VLS_65;
// @ts-ignore
[game, onWebSaved,];
var __VLS_58;
let __VLS_68;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_69 = __VLS_asFunctionalComponent1(__VLS_68, new __VLS_68({
    name: "upload",
    tab: "本地上传",
}));
const __VLS_70 = __VLS_69({
    name: "upload",
    tab: "本地上传",
}, ...__VLS_functionalComponentArgsRest(__VLS_69));
const { default: __VLS_73 } = __VLS_71.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "upload-section" },
});
/** @type {__VLS_StyleScopedClasses['upload-section']} */ ;
if (!__VLS_ctx.uploadPreview) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onDragover: (...[$event]) => {
                if (!(!__VLS_ctx.uploadPreview))
                    return;
                __VLS_ctx.dragOver = true;
                // @ts-ignore
                [uploadPreview, dragOver,];
            } },
        ...{ onDragleave: (...[$event]) => {
                if (!(!__VLS_ctx.uploadPreview))
                    return;
                __VLS_ctx.dragOver = false;
                // @ts-ignore
                [dragOver,];
            } },
        ...{ onDrop: (__VLS_ctx.handleDrop) },
        ...{ onClick: (__VLS_ctx.handleSelectIconFile) },
        ...{ class: "upload-zone" },
        ...{ class: ({ 'drag-over': __VLS_ctx.dragOver }) },
    });
    /** @type {__VLS_StyleScopedClasses['upload-zone']} */ ;
    /** @type {__VLS_StyleScopedClasses['drag-over']} */ ;
    let __VLS_74;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({
        size: (36),
        color: "var(--text-3)",
    }));
    const __VLS_76 = __VLS_75({
        size: (36),
        color: "var(--text-3)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_75));
    const { default: __VLS_79 } = __VLS_77.slots;
    let __VLS_80;
    /** @ts-ignore @type {typeof __VLS_components.CloudUploadOutlined} */
    CloudUploadOutlined;
    // @ts-ignore
    const __VLS_81 = __VLS_asFunctionalComponent1(__VLS_80, new __VLS_80({}));
    const __VLS_82 = __VLS_81({}, ...__VLS_functionalComponentArgsRest(__VLS_81));
    // @ts-ignore
    [dragOver, handleDrop, handleSelectIconFile,];
    var __VLS_77;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "upload-hint" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-hint']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "upload-formats" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-formats']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "upload-preview" },
    });
    /** @type {__VLS_StyleScopedClasses['upload-preview']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
        src: (__VLS_ctx.uploadPreview),
        alt: "Preview",
        ...{ class: "preview-img" },
    });
    /** @type {__VLS_StyleScopedClasses['preview-img']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "preview-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['preview-actions']} */ ;
    let __VLS_85;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_86 = __VLS_asFunctionalComponent1(__VLS_85, new __VLS_85({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_87 = __VLS_86({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_86));
    let __VLS_90;
    const __VLS_91 = ({ click: {} },
        { onClick: (__VLS_ctx.clearUpload) });
    const { default: __VLS_92 } = __VLS_88.slots;
    // @ts-ignore
    [uploadPreview, clearUpload,];
    var __VLS_88;
    var __VLS_89;
    let __VLS_93;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.uploading),
    }));
    const __VLS_95 = __VLS_94({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.uploading),
    }, ...__VLS_functionalComponentArgsRest(__VLS_94));
    let __VLS_98;
    const __VLS_99 = ({ click: {} },
        { onClick: (__VLS_ctx.confirmUpload) });
    const { default: __VLS_100 } = __VLS_96.slots;
    // @ts-ignore
    [uploading, confirmUpload,];
    var __VLS_96;
    var __VLS_97;
}
// @ts-ignore
[];
var __VLS_71;
// @ts-ignore
[];
var __VLS_12;
{
    const { footer: __VLS_101 } = __VLS_3.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "modal-footer" },
    });
    /** @type {__VLS_StyleScopedClasses['modal-footer']} */ ;
    let __VLS_102;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_103 = __VLS_asFunctionalComponent1(__VLS_102, new __VLS_102({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        quaternary: true,
    }));
    const __VLS_104 = __VLS_103({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_103));
    let __VLS_107;
    const __VLS_108 = ({ click: {} },
        { onClick: (__VLS_ctx.deleteIcon) });
    const { default: __VLS_109 } = __VLS_105.slots;
    // @ts-ignore
    [deleteIcon,];
    var __VLS_105;
    var __VLS_106;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_3;
var __VLS_4;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __typeProps: {},
});
export default {};
