import { ref, watch, onMounted } from 'vue';
import { NModal, NInput, NButton, NIcon, NTabs, NTabPane, NSpin, useMessage } from 'naive-ui';
import { SearchRound, CloudUploadOutlined } from '@vicons/material';
import { gamesApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import WebImageSearchTab from './WebImageSearchTab.vue';
const props = defineProps();
const emit = defineEmits();
const message = useMessage();
const { selectFile } = useFileExplorer();
// Steam search state
const steamQuery = ref(props.game.name);
const steamResults = ref([]);
const steamSearching = ref(false);
const steamSaving = ref(false);
// SteamGridDB search state
const searchQuery = ref(props.game.name);
const searchResults = ref([]);
const searching = ref(false);
const selectedGameId = ref(props.game.steamGridDbGameId ?? null);
const gridImages = ref([]);
const loadingGrids = ref(false);
const savingCover = ref(false);
// Upload state
const dragOver = ref(false);
const uploadPreview = ref(null);
const uploadFile = ref(null);
const uploading = ref(false);
// Auto-search Steam on mount
onMounted(() => {
    if (steamQuery.value.trim()) {
        searchSteam();
    }
});
watch(() => props.game, (game) => {
    steamQuery.value = game.name;
    searchQuery.value = game.name;
    selectedGameId.value = game.steamGridDbGameId ?? null;
    if (game.steamGridDbGameId) {
        loadGrids(game.steamGridDbGameId);
    }
});
async function searchSteam() {
    if (!steamQuery.value.trim())
        return;
    steamSearching.value = true;
    steamResults.value = [];
    try {
        steamResults.value = await gamesApi.searchSteamGames(props.game.id, steamQuery.value.trim());
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '搜索失败');
    }
    finally {
        steamSearching.value = false;
    }
}
async function selectSteamGame(result) {
    steamSaving.value = true;
    try {
        await gamesApi.selectSteamCover(props.game.id, result.id);
        message.success('封面已更新');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '获取封面失败');
    }
    finally {
        steamSaving.value = false;
    }
}
async function searchSteamGridDb() {
    if (!searchQuery.value.trim())
        return;
    searching.value = true;
    searchResults.value = [];
    selectedGameId.value = null;
    gridImages.value = [];
    try {
        searchResults.value = await gamesApi.searchCovers(props.game.id, searchQuery.value.trim());
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
    await loadGrids(result.id);
}
async function loadGrids(steamGridDbGameId) {
    loadingGrids.value = true;
    gridImages.value = [];
    try {
        gridImages.value = await gamesApi.getCoverGrids(props.game.id, steamGridDbGameId);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '获取封面列表失败');
    }
    finally {
        loadingGrids.value = false;
    }
}
async function selectGridImage(image) {
    if (!selectedGameId.value)
        return;
    savingCover.value = true;
    try {
        await gamesApi.selectCover(props.game.id, image.url, selectedGameId.value);
        message.success('封面已更新');
        emit('saved');
        emit('update:show', false);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '保存封面失败');
    }
    finally {
        savingCover.value = false;
    }
}
async function handleSelectCoverFile() {
    const path = await selectFile({
        title: '选择封面图片',
        filters: [{ label: '图片文件', extensions: ['.jpg', '.jpeg', '.png', '.webp'] }],
    });
    if (!path)
        return;
    uploading.value = true;
    try {
        await gamesApi.uploadCoverFromPath(props.game.id, path);
        message.success('封面已更新');
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
        await gamesApi.uploadCover(props.game.id, uploadFile.value);
        message.success('封面已更新');
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
function onWebSaved() {
    emit('saved');
    emit('update:show', false);
}
async function deleteCover() {
    try {
        await gamesApi.deleteCover(props.game.id);
        message.success('封面已删除');
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
/** @type {__VLS_StyleScopedClasses['steam-result']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['saving']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-image-overlay']} */ ;
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
    title: "更换游戏封面",
    ...{ style: {} },
    maskClosable: (true),
}));
const __VLS_2 = __VLS_1({
    ...{ 'onUpdate:show': {} },
    show: (__VLS_ctx.show),
    preset: "card",
    title: "更换游戏封面",
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
    name: "steam",
    tab: "Steam 搜索",
}));
const __VLS_17 = __VLS_16({
    name: "steam",
    tab: "Steam 搜索",
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
    value: (__VLS_ctx.steamQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}));
const __VLS_23 = __VLS_22({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.steamQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_22));
let __VLS_26;
const __VLS_27 = ({ keyup: {} },
    { onKeyup: (__VLS_ctx.searchSteam) });
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
        { onClick: (__VLS_ctx.searchSteam) });
    var __VLS_33;
    var __VLS_34;
    // @ts-ignore
    [steamQuery, searchSteam, searchSteam, SearchRound,];
}
// @ts-ignore
[];
var __VLS_24;
var __VLS_25;
if (__VLS_ctx.steamSearching) {
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
else if (__VLS_ctx.steamResults.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "search-results" },
    });
    /** @type {__VLS_StyleScopedClasses['search-results']} */ ;
    for (const [result] of __VLS_vFor((__VLS_ctx.steamResults))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.steamSearching))
                        return;
                    if (!(__VLS_ctx.steamResults.length > 0))
                        return;
                    __VLS_ctx.selectSteamGame(result);
                    // @ts-ignore
                    [steamSearching, steamResults, steamResults, selectSteamGame,];
                } },
            key: (result.id),
            ...{ class: "search-result-item steam-result" },
            ...{ class: ({ saving: __VLS_ctx.steamSaving }) },
        });
        /** @type {__VLS_StyleScopedClasses['search-result-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['steam-result']} */ ;
        /** @type {__VLS_StyleScopedClasses['saving']} */ ;
        if (result.tinyImage) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
                src: (result.tinyImage),
                ...{ class: "steam-thumb" },
                loading: "lazy",
            });
            /** @type {__VLS_StyleScopedClasses['steam-thumb']} */ ;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "result-name" },
        });
        /** @type {__VLS_StyleScopedClasses['result-name']} */ ;
        (result.name);
        // @ts-ignore
        [steamSaving,];
    }
}
else if (!__VLS_ctx.steamSearching && __VLS_ctx.steamQuery.trim()) {
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
[steamQuery, steamSearching,];
var __VLS_18;
let __VLS_42;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
    name: "search",
    tab: "SteamGridDB 搜索",
}));
const __VLS_44 = __VLS_43({
    name: "search",
    tab: "SteamGridDB 搜索",
}, ...__VLS_functionalComponentArgsRest(__VLS_43));
const { default: __VLS_47 } = __VLS_45.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-section" },
});
/** @type {__VLS_StyleScopedClasses['search-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-bar" },
});
/** @type {__VLS_StyleScopedClasses['search-bar']} */ ;
let __VLS_48;
/** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}));
const __VLS_50 = __VLS_49({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.searchQuery),
    placeholder: "搜索游戏名称...",
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_49));
let __VLS_53;
const __VLS_54 = ({ keyup: {} },
    { onKeyup: (__VLS_ctx.searchSteamGridDb) });
const { default: __VLS_55 } = __VLS_51.slots;
{
    const { suffix: __VLS_56 } = __VLS_51.slots;
    let __VLS_57;
    /** @ts-ignore @type {typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }));
    const __VLS_59 = __VLS_58({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_58));
    let __VLS_62;
    const __VLS_63 = ({ click: {} },
        { onClick: (__VLS_ctx.searchSteamGridDb) });
    var __VLS_60;
    var __VLS_61;
    // @ts-ignore
    [SearchRound, searchQuery, searchSteamGridDb, searchSteamGridDb,];
}
// @ts-ignore
[];
var __VLS_51;
var __VLS_52;
if (__VLS_ctx.searching) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        size: "small",
    }));
    const __VLS_66 = __VLS_65({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
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
        ...{ class: "grids-section" },
    });
    /** @type {__VLS_StyleScopedClasses['grids-section']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "grids-header" },
    });
    /** @type {__VLS_StyleScopedClasses['grids-header']} */ ;
    let __VLS_69;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_70 = __VLS_asFunctionalComponent1(__VLS_69, new __VLS_69({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
    }));
    const __VLS_71 = __VLS_70({
        ...{ 'onClick': {} },
        size: "tiny",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_70));
    let __VLS_74;
    const __VLS_75 = ({ click: {} },
        { onClick: (...[$event]) => {
                if (!(__VLS_ctx.selectedGameId))
                    return;
                __VLS_ctx.selectedGameId = null;
                __VLS_ctx.gridImages = [];
                // @ts-ignore
                [selectedGameId, selectedGameId, gridImages,];
            } });
    const { default: __VLS_76 } = __VLS_72.slots;
    // @ts-ignore
    [];
    var __VLS_72;
    var __VLS_73;
    if (__VLS_ctx.loadingGrids) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "center-state" },
        });
        /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
        let __VLS_77;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_78 = __VLS_asFunctionalComponent1(__VLS_77, new __VLS_77({
            size: "small",
        }));
        const __VLS_79 = __VLS_78({
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_78));
    }
    else if (__VLS_ctx.gridImages.length === 0) {
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
            ...{ class: "grids-grid" },
        });
        /** @type {__VLS_StyleScopedClasses['grids-grid']} */ ;
        for (const [image] of __VLS_vFor((__VLS_ctx.gridImages))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!(__VLS_ctx.selectedGameId))
                            return;
                        if (!!(__VLS_ctx.loadingGrids))
                            return;
                        if (!!(__VLS_ctx.gridImages.length === 0))
                            return;
                        __VLS_ctx.selectGridImage(image);
                        // @ts-ignore
                        [gridImages, gridImages, loadingGrids, selectGridImage,];
                    } },
                key: (image.id),
                ...{ class: "grid-image-item" },
                ...{ class: ({ saving: __VLS_ctx.savingCover }) },
            });
            /** @type {__VLS_StyleScopedClasses['grid-image-item']} */ ;
            /** @type {__VLS_StyleScopedClasses['saving']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
                src: (image.thumb),
                alt: (`Cover ${image.id}`),
                loading: "lazy",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "grid-image-overlay" },
            });
            /** @type {__VLS_StyleScopedClasses['grid-image-overlay']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (image.width);
            (image.height);
            // @ts-ignore
            [savingCover,];
        }
    }
}
// @ts-ignore
[];
var __VLS_45;
let __VLS_82;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_83 = __VLS_asFunctionalComponent1(__VLS_82, new __VLS_82({
    name: "web",
    tab: "网络搜索",
}));
const __VLS_84 = __VLS_83({
    name: "web",
    tab: "网络搜索",
}, ...__VLS_functionalComponentArgsRest(__VLS_83));
const { default: __VLS_87 } = __VLS_85.slots;
const __VLS_88 = WebImageSearchTab;
// @ts-ignore
const __VLS_89 = __VLS_asFunctionalComponent1(__VLS_88, new __VLS_88({
    ...{ 'onSaved': {} },
    game: (__VLS_ctx.game),
    mode: "cover",
}));
const __VLS_90 = __VLS_89({
    ...{ 'onSaved': {} },
    game: (__VLS_ctx.game),
    mode: "cover",
}, ...__VLS_functionalComponentArgsRest(__VLS_89));
let __VLS_93;
const __VLS_94 = ({ saved: {} },
    { onSaved: (__VLS_ctx.onWebSaved) });
var __VLS_91;
var __VLS_92;
// @ts-ignore
[game, onWebSaved,];
var __VLS_85;
let __VLS_95;
/** @ts-ignore @type {typeof __VLS_components.NTabPane | typeof __VLS_components.NTabPane} */
NTabPane;
// @ts-ignore
const __VLS_96 = __VLS_asFunctionalComponent1(__VLS_95, new __VLS_95({
    name: "upload",
    tab: "自定义上传",
}));
const __VLS_97 = __VLS_96({
    name: "upload",
    tab: "自定义上传",
}, ...__VLS_functionalComponentArgsRest(__VLS_96));
const { default: __VLS_100 } = __VLS_98.slots;
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
        ...{ onClick: (__VLS_ctx.handleSelectCoverFile) },
        ...{ class: "upload-zone" },
        ...{ class: ({ 'drag-over': __VLS_ctx.dragOver }) },
    });
    /** @type {__VLS_StyleScopedClasses['upload-zone']} */ ;
    /** @type {__VLS_StyleScopedClasses['drag-over']} */ ;
    let __VLS_101;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_102 = __VLS_asFunctionalComponent1(__VLS_101, new __VLS_101({
        size: (36),
        color: "var(--text-3)",
    }));
    const __VLS_103 = __VLS_102({
        size: (36),
        color: "var(--text-3)",
    }, ...__VLS_functionalComponentArgsRest(__VLS_102));
    const { default: __VLS_106 } = __VLS_104.slots;
    let __VLS_107;
    /** @ts-ignore @type {typeof __VLS_components.CloudUploadOutlined} */
    CloudUploadOutlined;
    // @ts-ignore
    const __VLS_108 = __VLS_asFunctionalComponent1(__VLS_107, new __VLS_107({}));
    const __VLS_109 = __VLS_108({}, ...__VLS_functionalComponentArgsRest(__VLS_108));
    // @ts-ignore
    [dragOver, handleDrop, handleSelectCoverFile,];
    var __VLS_104;
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
    let __VLS_112;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_113 = __VLS_asFunctionalComponent1(__VLS_112, new __VLS_112({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_114 = __VLS_113({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_113));
    let __VLS_117;
    const __VLS_118 = ({ click: {} },
        { onClick: (__VLS_ctx.clearUpload) });
    const { default: __VLS_119 } = __VLS_115.slots;
    // @ts-ignore
    [uploadPreview, clearUpload,];
    var __VLS_115;
    var __VLS_116;
    let __VLS_120;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_121 = __VLS_asFunctionalComponent1(__VLS_120, new __VLS_120({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.uploading),
    }));
    const __VLS_122 = __VLS_121({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.uploading),
    }, ...__VLS_functionalComponentArgsRest(__VLS_121));
    let __VLS_125;
    const __VLS_126 = ({ click: {} },
        { onClick: (__VLS_ctx.confirmUpload) });
    const { default: __VLS_127 } = __VLS_123.slots;
    // @ts-ignore
    [uploading, confirmUpload,];
    var __VLS_123;
    var __VLS_124;
}
// @ts-ignore
[];
var __VLS_98;
// @ts-ignore
[];
var __VLS_12;
{
    const { footer: __VLS_128 } = __VLS_3.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "modal-footer" },
    });
    /** @type {__VLS_StyleScopedClasses['modal-footer']} */ ;
    let __VLS_129;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_130 = __VLS_asFunctionalComponent1(__VLS_129, new __VLS_129({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        quaternary: true,
    }));
    const __VLS_131 = __VLS_130({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        quaternary: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_130));
    let __VLS_134;
    const __VLS_135 = ({ click: {} },
        { onClick: (__VLS_ctx.deleteCover) });
    const { default: __VLS_136 } = __VLS_132.slots;
    // @ts-ignore
    [deleteCover,];
    var __VLS_132;
    var __VLS_133;
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
