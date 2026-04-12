import { ref, computed, onBeforeUnmount } from 'vue';
import { NIcon, NSwitch, NPopover, NColorPicker } from 'naive-ui';
import { TuneRound } from '@vicons/material';
import { useGamesStore } from '@/stores/games';
import { useThemeStore, accentPresets } from '@/stores/theme';
import { settingsApi } from '@/api/games';
const gamesStore = useGamesStore();
const themeStore = useThemeStore();
const showColorPicker = ref(false);
const isCustomAccent = computed(() => !accentPresets.some(p => p.hex === themeStore.accentColor));
let saveTimer = null;
function setAccentFromPicker(hex) {
    themeStore.setAccentColor(hex);
    if (saveTimer)
        clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
        settingsApi.get().then(s => {
            s.accentColor = hex;
            settingsApi.save(s);
        });
    }, 500);
}
onBeforeUnmount(() => {
    if (saveTimer)
        clearTimeout(saveTimer);
});
const cardSizeOptions = [
    { value: 'small', label: 'S' },
    { value: 'medium', label: 'M' },
    { value: 'large', label: 'L' },
    { value: 'xlarge', label: 'XL' },
];
const gapOptions = [
    { value: 'compact', label: '紧凑' },
    { value: 'normal', label: '标准' },
    { value: 'spacious', label: '宽松' },
];
function setCardSize(size) {
    gamesStore.setCardSize(size);
    gamesStore.savePreferences();
}
function setGap(gap) {
    gamesStore.setGap(gap);
    gamesStore.savePreferences();
}
function toggleLabels(val) {
    gamesStore.setShowLabels(val);
    gamesStore.savePreferences();
}
function setAccent(hex) {
    themeStore.setAccentColor(hex);
    // Also persist to backend
    settingsApi.get().then(settings => {
        settings.accentColor = hex;
        settingsApi.save(settings);
    });
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['customizer-trigger']} */ ;
/** @type {__VLS_StyleScopedClasses['option-group']} */ ;
/** @type {__VLS_StyleScopedClasses['option-row']} */ ;
/** @type {__VLS_StyleScopedClasses['option-label']} */ ;
/** @type {__VLS_StyleScopedClasses['seg-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['seg-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['color-swatch']} */ ;
/** @type {__VLS_StyleScopedClasses['color-swatch']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
NPopover;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    trigger: "click",
    placement: "bottom-end",
    width: (280),
}));
const __VLS_2 = __VLS_1({
    trigger: "click",
    placement: "bottom-end",
    width: (280),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
var __VLS_5 = {};
const { default: __VLS_6 } = __VLS_3.slots;
{
    const { trigger: __VLS_7 } = __VLS_3.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ class: "customizer-trigger" },
        title: "显示设置",
    });
    /** @type {__VLS_StyleScopedClasses['customizer-trigger']} */ ;
    let __VLS_8;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_9 = __VLS_asFunctionalComponent1(__VLS_8, new __VLS_8({
        size: (18),
    }));
    const __VLS_10 = __VLS_9({
        size: (18),
    }, ...__VLS_functionalComponentArgsRest(__VLS_9));
    const { default: __VLS_13 } = __VLS_11.slots;
    let __VLS_14;
    /** @ts-ignore @type {typeof __VLS_components.TuneRound} */
    TuneRound;
    // @ts-ignore
    const __VLS_15 = __VLS_asFunctionalComponent1(__VLS_14, new __VLS_14({}));
    const __VLS_16 = __VLS_15({}, ...__VLS_functionalComponentArgsRest(__VLS_15));
    var __VLS_11;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "customizer-panel" },
});
/** @type {__VLS_StyleScopedClasses['customizer-panel']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "panel-title" },
});
/** @type {__VLS_StyleScopedClasses['panel-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "option-group" },
});
/** @type {__VLS_StyleScopedClasses['option-group']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "option-label" },
});
/** @type {__VLS_StyleScopedClasses['option-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "segmented-control" },
});
/** @type {__VLS_StyleScopedClasses['segmented-control']} */ ;
for (const [opt] of __VLS_vFor((__VLS_ctx.cardSizeOptions))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.setCardSize(opt.value);
                // @ts-ignore
                [cardSizeOptions, setCardSize,];
            } },
        key: (opt.value),
        ...{ class: "seg-btn" },
        ...{ class: ({ active: __VLS_ctx.gamesStore.cardSize === opt.value }) },
    });
    /** @type {__VLS_StyleScopedClasses['seg-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    (opt.label);
    // @ts-ignore
    [gamesStore,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "option-group" },
});
/** @type {__VLS_StyleScopedClasses['option-group']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "option-label" },
});
/** @type {__VLS_StyleScopedClasses['option-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "segmented-control" },
});
/** @type {__VLS_StyleScopedClasses['segmented-control']} */ ;
for (const [opt] of __VLS_vFor((__VLS_ctx.gapOptions))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.setGap(opt.value);
                // @ts-ignore
                [gapOptions, setGap,];
            } },
        key: (opt.value),
        ...{ class: "seg-btn" },
        ...{ class: ({ active: __VLS_ctx.gamesStore.gap === opt.value }) },
    });
    /** @type {__VLS_StyleScopedClasses['seg-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    (opt.label);
    // @ts-ignore
    [gamesStore,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "option-group option-row" },
});
/** @type {__VLS_StyleScopedClasses['option-group']} */ ;
/** @type {__VLS_StyleScopedClasses['option-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "option-label" },
});
/** @type {__VLS_StyleScopedClasses['option-label']} */ ;
let __VLS_19;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_20 = __VLS_asFunctionalComponent1(__VLS_19, new __VLS_19({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.gamesStore.showLabels),
    size: "small",
}));
const __VLS_21 = __VLS_20({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.gamesStore.showLabels),
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_20));
let __VLS_24;
const __VLS_25 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.toggleLabels) });
var __VLS_22;
var __VLS_23;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "option-group" },
});
/** @type {__VLS_StyleScopedClasses['option-group']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "option-label" },
});
/** @type {__VLS_StyleScopedClasses['option-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "color-grid" },
});
/** @type {__VLS_StyleScopedClasses['color-grid']} */ ;
for (const [preset] of __VLS_vFor((__VLS_ctx.accentPresets))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.setAccent(preset.hex);
                // @ts-ignore
                [gamesStore, toggleLabels, accentPresets, setAccent,];
            } },
        key: (preset.hex),
        ...{ class: "color-swatch" },
        ...{ class: ({ active: __VLS_ctx.themeStore.accentColor === preset.hex }) },
        ...{ style: ({ '--swatch-color': preset.hex }) },
        title: (preset.name),
    });
    /** @type {__VLS_StyleScopedClasses['color-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    if (__VLS_ctx.themeStore.accentColor === preset.hex) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "check-icon" },
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M4 8.5L7 11.5L12 5",
            stroke: "white",
            'stroke-width': "2",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
    }
    // @ts-ignore
    [themeStore, themeStore,];
}
let __VLS_26;
/** @ts-ignore @type {typeof __VLS_components.NColorPicker | typeof __VLS_components.NColorPicker} */
NColorPicker;
// @ts-ignore
const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({
    ...{ 'onUpdate:show': {} },
    ...{ 'onUpdate:value': {} },
    show: (__VLS_ctx.showColorPicker),
    value: (__VLS_ctx.themeStore.accentColor),
    modes: (['hex']),
    showAlpha: (false),
    swatches: (__VLS_ctx.accentPresets.map(p => p.hex)),
    actions: ([]),
    placement: "bottom-end",
}));
const __VLS_28 = __VLS_27({
    ...{ 'onUpdate:show': {} },
    ...{ 'onUpdate:value': {} },
    show: (__VLS_ctx.showColorPicker),
    value: (__VLS_ctx.themeStore.accentColor),
    modes: (['hex']),
    showAlpha: (false),
    swatches: (__VLS_ctx.accentPresets.map(p => p.hex)),
    actions: ([]),
    placement: "bottom-end",
}, ...__VLS_functionalComponentArgsRest(__VLS_27));
let __VLS_31;
const __VLS_32 = ({ 'update:show': {} },
    { 'onUpdate:show': (...[$event]) => {
            __VLS_ctx.showColorPicker = $event;
            // @ts-ignore
            [accentPresets, themeStore, showColorPicker, showColorPicker,];
        } });
const __VLS_33 = ({ 'update:value': {} },
    { 'onUpdate:value': (...[$event]) => {
            __VLS_ctx.setAccentFromPicker($event);
            // @ts-ignore
            [setAccentFromPicker,];
        } });
const { default: __VLS_34 } = __VLS_29.slots;
{
    const { trigger: __VLS_35 } = __VLS_29.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.showColorPicker = !__VLS_ctx.showColorPicker;
                // @ts-ignore
                [showColorPicker, showColorPicker,];
            } },
        ...{ class: "color-swatch custom-swatch" },
        ...{ class: ({ active: __VLS_ctx.isCustomAccent }) },
        ...{ style: (__VLS_ctx.isCustomAccent ? { '--swatch-color': __VLS_ctx.themeStore.accentColor } : {}) },
        title: "自定义颜色",
    });
    /** @type {__VLS_StyleScopedClasses['color-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['custom-swatch']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    if (__VLS_ctx.isCustomAccent) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
            ...{ class: "check-icon" },
            viewBox: "0 0 16 16",
            fill: "none",
        });
        /** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
            d: "M4 8.5L7 11.5L12 5",
            stroke: "white",
            'stroke-width': "2",
            'stroke-linecap': "round",
            'stroke-linejoin': "round",
        });
    }
    // @ts-ignore
    [themeStore, isCustomAccent, isCustomAccent, isCustomAccent,];
}
// @ts-ignore
[];
var __VLS_29;
var __VLS_30;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
