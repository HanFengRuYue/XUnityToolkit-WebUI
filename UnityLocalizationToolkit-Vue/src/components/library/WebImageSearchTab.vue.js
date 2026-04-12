import { ref } from 'vue';
import { NInput, NIcon, NSelect, NSpin, useMessage } from 'naive-ui';
import { SearchRound } from '@vicons/material';
import { gamesApi } from '@/api/games';
const props = defineProps();
const emit = defineEmits();
const message = useMessage();
const query = ref(props.game.name);
const engine = ref('Bing');
const sizeFilter = ref('auto');
const results = ref([]);
const searching = ref(false);
const saving = ref(false);
const hasSearched = ref(false);
const error = ref(null);
const engineOptions = [
    { label: 'Bing', value: 'Bing' },
    { label: 'Google', value: 'Google' },
];
const sizeFilterOptions = [
    { label: '自动', value: 'auto' },
    { label: '大图', value: 'large' },
    { label: '中图', value: 'medium' },
    { label: '小图', value: 'small' },
    { label: '方形', value: 'square' },
    { label: '竖版', value: 'tall' },
    { label: '壁纸', value: 'wallpaper' },
];
async function doSearch() {
    if (!query.value.trim())
        return;
    searching.value = true;
    error.value = null;
    results.value = [];
    hasSearched.value = true;
    try {
        if (props.mode === 'cover') {
            results.value = await gamesApi.searchWebImages(props.game.id, query.value.trim(), engine.value, sizeFilter.value);
        }
        else if (props.mode === 'background') {
            results.value = await gamesApi.searchWebBackgroundImages(props.game.id, query.value.trim(), engine.value, sizeFilter.value);
        }
        else {
            results.value = await gamesApi.searchWebIconImages(props.game.id, query.value.trim(), engine.value, sizeFilter.value);
        }
    }
    catch (e) {
        error.value = e instanceof Error ? e.message : '搜索失败';
    }
    finally {
        searching.value = false;
    }
}
async function selectImage(result) {
    if (saving.value)
        return;
    saving.value = true;
    try {
        if (props.mode === 'cover') {
            await gamesApi.selectWebCover(props.game.id, result.fullUrl);
        }
        else if (props.mode === 'background') {
            await gamesApi.selectWebBackground(props.game.id, result.fullUrl);
        }
        else {
            await gamesApi.selectWebIcon(props.game.id, result.fullUrl);
        }
        const labels = { cover: '封面已更新', icon: '图标已更新', background: '背景已更新' };
        message.success(labels[props.mode]);
        emit('saved');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '保存失败');
    }
    finally {
        saving.value = false;
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
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['cover']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['icon']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['background']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['image-item']} */ ;
/** @type {__VLS_StyleScopedClasses['dim-overlay']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "web-search-section" },
});
/** @type {__VLS_StyleScopedClasses['web-search-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "search-controls" },
});
/** @type {__VLS_StyleScopedClasses['search-controls']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.query),
    placeholder: "搜索图片...",
    size: "small",
    ...{ style: {} },
}));
const __VLS_2 = __VLS_1({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.query),
    placeholder: "搜索图片...",
    size: "small",
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
let __VLS_5;
const __VLS_6 = ({ keyup: {} },
    { onKeyup: (__VLS_ctx.doSearch) });
const { default: __VLS_7 } = __VLS_3.slots;
{
    const { suffix: __VLS_8 } = __VLS_3.slots;
    let __VLS_9;
    /** @ts-ignore @type {typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_10 = __VLS_asFunctionalComponent1(__VLS_9, new __VLS_9({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }));
    const __VLS_11 = __VLS_10({
        ...{ 'onClick': {} },
        component: (__VLS_ctx.SearchRound),
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_10));
    let __VLS_14;
    const __VLS_15 = ({ click: {} },
        { onClick: (__VLS_ctx.doSearch) });
    var __VLS_12;
    var __VLS_13;
    // @ts-ignore
    [query, doSearch, doSearch, SearchRound,];
}
// @ts-ignore
[];
var __VLS_3;
var __VLS_4;
let __VLS_16;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_17 = __VLS_asFunctionalComponent1(__VLS_16, new __VLS_16({
    value: (__VLS_ctx.engine),
    options: (__VLS_ctx.engineOptions),
    size: "small",
    ...{ style: {} },
}));
const __VLS_18 = __VLS_17({
    value: (__VLS_ctx.engine),
    options: (__VLS_ctx.engineOptions),
    size: "small",
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_17));
let __VLS_21;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({
    value: (__VLS_ctx.sizeFilter),
    options: (__VLS_ctx.sizeFilterOptions),
    size: "small",
    ...{ style: {} },
}));
const __VLS_23 = __VLS_22({
    value: (__VLS_ctx.sizeFilter),
    options: (__VLS_ctx.sizeFilterOptions),
    size: "small",
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_22));
if (__VLS_ctx.searching) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    let __VLS_26;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({
        size: "small",
    }));
    const __VLS_28 = __VLS_27({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_27));
}
else if (__VLS_ctx.error) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-text" },
    });
    /** @type {__VLS_StyleScopedClasses['error-text']} */ ;
    (__VLS_ctx.error);
}
else if (__VLS_ctx.results.length === 0 && __VLS_ctx.hasSearched) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "center-state" },
    });
    /** @type {__VLS_StyleScopedClasses['center-state']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "muted-text" },
    });
    /** @type {__VLS_StyleScopedClasses['muted-text']} */ ;
}
else if (__VLS_ctx.results.length === 0) {
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
        ...{ class: "image-grid" },
        ...{ class: (__VLS_ctx.mode) },
        ...{ style: ({ pointerEvents: __VLS_ctx.saving ? 'none' : 'auto', opacity: __VLS_ctx.saving ? 0.5 : 1 }) },
    });
    /** @type {__VLS_StyleScopedClasses['image-grid']} */ ;
    for (const [r, i] of __VLS_vFor((__VLS_ctx.results))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.searching))
                        return;
                    if (!!(__VLS_ctx.error))
                        return;
                    if (!!(__VLS_ctx.results.length === 0 && __VLS_ctx.hasSearched))
                        return;
                    if (!!(__VLS_ctx.results.length === 0))
                        return;
                    __VLS_ctx.selectImage(r);
                    // @ts-ignore
                    [engine, engineOptions, sizeFilter, sizeFilterOptions, searching, error, error, results, results, results, hasSearched, mode, saving, saving, selectImage,];
                } },
            key: (i),
            ...{ class: "image-item" },
        });
        /** @type {__VLS_StyleScopedClasses['image-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
            src: (r.thumbUrl),
            alt: (r.title || ''),
            loading: "lazy",
        });
        if (r.width && r.height) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "dim-overlay" },
            });
            /** @type {__VLS_StyleScopedClasses['dim-overlay']} */ ;
            (r.width);
            (r.height);
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
