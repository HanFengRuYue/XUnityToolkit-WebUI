import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NIcon, useMessage } from 'naive-ui';
import { SaveOutlined, ArrowBackOutlined } from '@vicons/material';
import { gamesApi } from '@/api/games';
const route = useRoute();
const router = useRouter();
const message = useMessage();
const gameId = route.params['id'];
const game = ref(null);
const content = ref('');
const originalContent = ref('');
const loading = ref(true);
const saving = ref(false);
const hasChanges = ref(false);
function updateContent(e) {
    content.value = e.target.value;
    hasChanges.value = content.value !== originalContent.value;
}
function handleBeforeUnload(e) {
    if (hasChanges.value) {
        e.preventDefault();
        e.returnValue = '';
    }
}
onMounted(async () => {
    window.addEventListener('beforeunload', handleBeforeUnload);
    try {
        const [gameData, rawConfig] = await Promise.all([
            gamesApi.get(gameId),
            gamesApi.getRawConfig(gameId),
        ]);
        game.value = gameData;
        content.value = rawConfig;
        originalContent.value = rawConfig;
        document.title = `配置编辑 - ${gameData.name}`;
    }
    catch {
        message.error('加载配置文件失败');
    }
    finally {
        loading.value = false;
    }
});
onBeforeUnmount(() => {
    window.removeEventListener('beforeunload', handleBeforeUnload);
});
async function handleSave() {
    saving.value = true;
    try {
        await gamesApi.saveRawConfig(gameId, content.value);
        originalContent.value = content.value;
        hasChanges.value = false;
        message.success('配置已保存');
    }
    catch {
        message.error('保存失败');
    }
    finally {
        saving.value = false;
    }
}
function handleBack() {
    router.back();
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['editor-page']} */ ;
/** @type {__VLS_StyleScopedClasses['editor-toolbar']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-title']} */ ;
/** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "editor-page" },
});
/** @type {__VLS_StyleScopedClasses['editor-page']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "editor-toolbar" },
});
/** @type {__VLS_StyleScopedClasses['editor-toolbar']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-left" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    ...{ 'onClick': {} },
    quaternary: true,
    size: "small",
}));
const __VLS_2 = __VLS_1({
    ...{ 'onClick': {} },
    quaternary: true,
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
let __VLS_5;
const __VLS_6 = ({ click: {} },
    { onClick: (__VLS_ctx.handleBack) });
const { default: __VLS_7 } = __VLS_3.slots;
{
    const { icon: __VLS_8 } = __VLS_3.slots;
    let __VLS_9;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_10 = __VLS_asFunctionalComponent1(__VLS_9, new __VLS_9({}));
    const __VLS_11 = __VLS_10({}, ...__VLS_functionalComponentArgsRest(__VLS_10));
    const { default: __VLS_14 } = __VLS_12.slots;
    let __VLS_15;
    /** @ts-ignore @type {typeof __VLS_components.ArrowBackOutlined} */
    ArrowBackOutlined;
    // @ts-ignore
    const __VLS_16 = __VLS_asFunctionalComponent1(__VLS_15, new __VLS_15({}));
    const __VLS_17 = __VLS_16({}, ...__VLS_functionalComponentArgsRest(__VLS_16));
    // @ts-ignore
    [handleBack,];
    var __VLS_12;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_3;
var __VLS_4;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "toolbar-title" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-title']} */ ;
(__VLS_ctx.game?.name ?? '...');
if (__VLS_ctx.hasChanges) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "unsaved-badge" },
    });
    /** @type {__VLS_StyleScopedClasses['unsaved-badge']} */ ;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "toolbar-right" },
});
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
let __VLS_20;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_21 = __VLS_asFunctionalComponent1(__VLS_20, new __VLS_20({
    ...{ 'onClick': {} },
    type: "primary",
    size: "small",
    disabled: (!__VLS_ctx.hasChanges),
    loading: (__VLS_ctx.saving),
}));
const __VLS_22 = __VLS_21({
    ...{ 'onClick': {} },
    type: "primary",
    size: "small",
    disabled: (!__VLS_ctx.hasChanges),
    loading: (__VLS_ctx.saving),
}, ...__VLS_functionalComponentArgsRest(__VLS_21));
let __VLS_25;
const __VLS_26 = ({ click: {} },
    { onClick: (__VLS_ctx.handleSave) });
const { default: __VLS_27 } = __VLS_23.slots;
{
    const { icon: __VLS_28 } = __VLS_23.slots;
    let __VLS_29;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_30 = __VLS_asFunctionalComponent1(__VLS_29, new __VLS_29({}));
    const __VLS_31 = __VLS_30({}, ...__VLS_functionalComponentArgsRest(__VLS_30));
    const { default: __VLS_34 } = __VLS_32.slots;
    let __VLS_35;
    /** @ts-ignore @type {typeof __VLS_components.SaveOutlined} */
    SaveOutlined;
    // @ts-ignore
    const __VLS_36 = __VLS_asFunctionalComponent1(__VLS_35, new __VLS_35({}));
    const __VLS_37 = __VLS_36({}, ...__VLS_functionalComponentArgsRest(__VLS_36));
    // @ts-ignore
    [game, hasChanges, hasChanges, saving, handleSave,];
    var __VLS_32;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_23;
var __VLS_24;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "editor-loading" },
    });
    /** @type {__VLS_StyleScopedClasses['editor-loading']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "loading-spinner" },
    });
    /** @type {__VLS_StyleScopedClasses['loading-spinner']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "editor-container" },
    });
    /** @type {__VLS_StyleScopedClasses['editor-container']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.textarea, __VLS_intrinsics.textarea)({
        ...{ onInput: (__VLS_ctx.updateContent) },
        ...{ class: "config-textarea" },
        value: (__VLS_ctx.content),
        spellcheck: "false",
        autocomplete: "off",
        autocorrect: "off",
        autocapitalize: "off",
    });
    /** @type {__VLS_StyleScopedClasses['config-textarea']} */ ;
}
// @ts-ignore
[loading, updateContent, content,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
