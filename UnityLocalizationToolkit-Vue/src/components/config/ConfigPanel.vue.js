import { ref, watch, computed, onMounted, onBeforeUnmount, nextTick } from 'vue';
import { useRouter } from 'vue-router';
import { NForm, NFormItem, NSelect, NSwitch, NInputNumber, NInput, NCollapse, NCollapseItem, NButton, NIcon, NTooltip, useMessage, } from 'naive-ui';
import { EditNoteOutlined, DownloadOutlined, DeleteOutlineOutlined } from '@vicons/material';
import { gamesApi } from '@/api/games';
import { useAutoSave } from '@/composables/useAutoSave';
const props = defineProps();
const router = useRouter();
const message = useMessage();
const form = ref({
    translationEngine: 'GoogleTranslateV2',
    fallbackEndpoint: '',
    sourceLanguage: 'ja',
    targetLanguage: 'zh',
    outputFile: '',
    enableIMGUI: true,
    enableUGUI: true,
    enableNGUI: true,
    enableTextMeshPro: true,
    enableTextMesh: true,
    enableFairyGUI: true,
    maxCharactersPerTranslation: 2500,
    forceSplitTextAfterCharacters: 0,
    handleRichText: true,
    enableUIResizing: true,
    overrideFont: 'Microsoft YaHei',
    overrideFontSize: '',
    overrideFontTextMeshPro: '',
    fallbackFontTextMeshPro: '',
    resizeUILineSpacingScale: '',
    forceUIResizing: true,
    textGetterCompatibilityMode: false,
    maxTextParserRecursion: 4,
    enableTranslationHelper: false,
    templateAllNumberAway: false,
    reloadTranslationsOnFileChange: false,
    disableTextMeshProScrollInEffects: false,
    cacheParsedTranslations: true,
    cacheWhitespaceDifferences: false,
    ignoreWhitespaceInDialogue: true,
    minDialogueChars: 4,
    textureDirectory: '',
    enableTextureTranslation: false,
    enableTextureDumping: false,
    enableTextureToggling: false,
    enableTextureScanOnSceneLoad: false,
    loadUnmodifiedTextures: false,
    textureHashGenerationStrategy: 'FromImageName',
    enableSpriteHooking: false,
    extra: {},
    deepLLegitimateFree: false,
});
function getKeyFieldsForEngine(engine) {
    switch (engine) {
        case 'GoogleTranslateLegitimate':
            return [{ key: 'googleLegitimateApiKey', label: 'API Key', placeholder: 'Google Cloud API Key', type: 'password' }];
        case 'BingTranslateLegitimate':
            return [{ key: 'bingLegitimateSubscriptionKey', label: 'Subscription Key', placeholder: 'Azure 订阅密钥', type: 'password' }];
        case 'BaiduTranslate':
            return [
                { key: 'baiduAppId', label: 'App ID', placeholder: '百度翻译 AppId', type: 'text' },
                { key: 'baiduAppSecret', label: 'App Secret', placeholder: '百度翻译密钥', type: 'password' },
            ];
        case 'YandexTranslate':
            return [{ key: 'yandexApiKey', label: 'API Key', placeholder: 'Yandex API Key', type: 'password' }];
        case 'DeepLTranslateLegitimate':
            return [{ key: 'deepLLegitimateApiKey', label: 'API Key', placeholder: 'DeepL Auth Key', type: 'password' }];
        case 'PapagoTranslate':
            return [
                { key: 'papagoClientId', label: 'Client ID', placeholder: 'Papago Client ID', type: 'text' },
                { key: 'papagoClientSecret', label: 'Client Secret', placeholder: 'Papago 密钥', type: 'password' },
            ];
        case 'LingoCloudTranslate':
            return [{ key: 'lingoCloudToken', label: 'API Token', placeholder: '彩云小译 Token', type: 'password' }];
        case 'WatsonTranslate':
            return [
                { key: 'watsonUrl', label: '服务 URL', placeholder: 'Watson 翻译服务 URL', type: 'text' },
                { key: 'watsonKey', label: 'API Key', placeholder: 'Watson API Key', type: 'password' },
            ];
        case 'CustomTranslate':
            return [{ key: 'customTranslateUrl', label: '端点 URL', placeholder: '自定义翻译服务 URL', type: 'text' }];
        case 'LecPowerTranslator15':
            return [{ key: 'lecInstallPath', label: '安装路径', placeholder: 'LEC 安装目录路径', type: 'text' }];
        case 'ezTrans':
            return [{ key: 'ezTransInstallPath', label: '安装路径', placeholder: 'ezTrans XP 安装目录路径', type: 'text' }];
        default:
            return [];
    }
}
function getEngineName(engine) {
    return engineOptions.find(o => o.value === engine)?.label ?? engine;
}
const primaryKeyFields = computed(() => getKeyFieldsForEngine(form.value.translationEngine));
const fallbackKeyFields = computed(() => {
    const fallback = form.value.fallbackEndpoint;
    if (!fallback || fallback === form.value.translationEngine)
        return [];
    return getKeyFieldsForEngine(fallback);
});
const showPrimaryDeepLFreeToggle = computed(() => form.value.translationEngine === 'DeepLTranslateLegitimate');
const showFallbackDeepLFreeToggle = computed(() => {
    const fallback = form.value.fallbackEndpoint;
    return fallback === 'DeepLTranslateLegitimate' && fallback !== form.value.translationEngine;
});
const hasBothKeyGroups = computed(() => primaryKeyFields.value.length > 0 && fallbackKeyFields.value.length > 0);
const hasAnyKeyFields = computed(() => primaryKeyFields.value.length > 0 || fallbackKeyFields.value.length > 0 ||
    showPrimaryDeepLFreeToggle.value || showFallbackDeepLFreeToggle.value);
const { enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(() => form.value, async () => {
    try {
        await gamesApi.saveConfig(props.gameId, form.value);
    }
    catch {
        message.error('保存配置失败');
    }
}, { debounceMs: 2000, deep: true });
watch(() => props.config, async (cfg) => {
    disableAutoSave();
    if (cfg)
        form.value = { ...cfg };
    await nextTick();
    enableAutoSave();
}, { immediate: true });
const languageOptions = [
    { label: '日语 (ja)', value: 'ja' },
    { label: '英语 (en)', value: 'en' },
    { label: '中文 (zh)', value: 'zh' },
    { label: '简体中文 (zh_CN)', value: 'zh_CN' },
    { label: '繁体中文 (zh-TW)', value: 'zh-TW' },
    { label: '韩语 (ko)', value: 'ko' },
    { label: '法语 (fr)', value: 'fr' },
    { label: '德语 (de)', value: 'de' },
    { label: '西班牙语 (es)', value: 'es' },
    { label: '俄语 (ru)', value: 'ru' },
];
const engineOptions = [
    { label: 'Google Translate', value: 'GoogleTranslate' },
    { label: 'Google Translate V2', value: 'GoogleTranslateV2' },
    { label: 'Google Translate (兼容)', value: 'GoogleTranslateCompat' },
    { label: 'Google Translate (官方)', value: 'GoogleTranslateLegitimate' },
    { label: 'Bing Translate', value: 'BingTranslate' },
    { label: 'Bing Translate (官方)', value: 'BingTranslateLegitimate' },
    { label: 'DeepL Translate', value: 'DeepLTranslate' },
    { label: 'DeepL Translate (官方)', value: 'DeepLTranslateLegitimate' },
    { label: 'Baidu Translate', value: 'BaiduTranslate' },
    { label: 'Yandex Translate', value: 'YandexTranslate' },
    { label: 'Papago', value: 'PapagoTranslate' },
    { label: '彩云小译', value: 'LingoCloudTranslate' },
    { label: 'IBM Watson', value: 'WatsonTranslate' },
    { label: 'LEC Power Translator', value: 'LecPowerTranslator15' },
    { label: 'ezTrans XP', value: 'ezTrans' },
    { label: '自定义端点', value: 'CustomTranslate' },
    { label: 'AI 翻译 (工具箱)', value: 'LLMTranslate' },
];
const textureHashOptions = [
    { label: '按图片名称', value: 'FromImageName' },
    { label: '按图片数据', value: 'FromImageData' },
    { label: '按图片名称和场景', value: 'FromImageNameAndScene' },
];
function openConfigEditor() {
    router.push(`/games/${props.gameId}/config-editor`);
}
const isMobile = ref(false);
function checkMobile() {
    isMobile.value = window.innerWidth <= 768;
}
onMounted(() => {
    checkMobile();
    window.addEventListener('resize', checkMobile);
});
onBeforeUnmount(() => {
    window.removeEventListener('resize', checkMobile);
});
const labelPlacement = computed(() => isMobile.value ? 'top' : 'left');
const labelWidth = computed(() => isMobile.value ? undefined : '160');
// TMP font state
const tmpFontInstalled = ref(null);
const tmpFontLoading = ref(false);
async function loadTmpFontStatus() {
    try {
        const status = await gamesApi.getTmpFontStatus(props.gameId);
        tmpFontInstalled.value = status.installed;
    }
    catch {
        tmpFontInstalled.value = null;
    }
}
async function handleInstallTmpFont() {
    tmpFontLoading.value = true;
    try {
        const result = await gamesApi.installTmpFont(props.gameId);
        tmpFontInstalled.value = result.installed;
        // Reload config to reflect FallbackFontTextMeshPro written by backend
        disableAutoSave();
        const updatedConfig = await gamesApi.getConfig(props.gameId);
        form.value = { ...updatedConfig };
        await nextTick();
        enableAutoSave();
        message.success('TMP 字体已安装');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '安装失败');
    }
    finally {
        tmpFontLoading.value = false;
    }
}
async function handleUninstallTmpFont() {
    tmpFontLoading.value = true;
    try {
        const result = await gamesApi.uninstallTmpFont(props.gameId);
        tmpFontInstalled.value = result.installed;
        // Reload config to reflect font removal
        disableAutoSave();
        const updatedConfig = await gamesApi.getConfig(props.gameId);
        form.value = { ...updatedConfig };
        await nextTick();
        enableAutoSave();
        message.success('TMP 字体已卸载');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败');
    }
    finally {
        tmpFontLoading.value = false;
    }
}
watch(() => props.disabled, (disabled) => {
    if (!disabled) {
        loadTmpFontStatus();
    }
    else {
        tmpFontInstalled.value = null;
    }
}, { immediate: true });
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['tmp-font-row']} */ ;
/** @type {__VLS_StyleScopedClasses['n-input']} */ ;
/** @type {__VLS_StyleScopedClasses['n-base-selection']} */ ;
/** @type {__VLS_StyleScopedClasses['n-input']} */ ;
/** @type {__VLS_StyleScopedClasses['n-input-number']} */ ;
/** @type {__VLS_StyleScopedClasses['config-main-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['framework-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
/** @type {__VLS_StyleScopedClasses['config-footer']} */ ;
/** @type {__VLS_StyleScopedClasses['config-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-panel" },
});
/** @type {__VLS_StyleScopedClasses['config-panel']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NForm | typeof __VLS_components.NForm} */
NForm;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    disabled: (__VLS_ctx.disabled),
    labelPlacement: (__VLS_ctx.labelPlacement),
    labelWidth: (__VLS_ctx.labelWidth),
}));
const __VLS_2 = __VLS_1({
    disabled: (__VLS_ctx.disabled),
    labelPlacement: (__VLS_ctx.labelPlacement),
    labelWidth: (__VLS_ctx.labelWidth),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-main-grid" },
});
/** @type {__VLS_StyleScopedClasses['config-main-grid']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-column" },
});
/** @type {__VLS_StyleScopedClasses['config-column']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section" },
});
/** @type {__VLS_StyleScopedClasses['config-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section-label" },
});
/** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
let __VLS_6;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({
    label: "源语言",
}));
const __VLS_8 = __VLS_7({
    label: "源语言",
}, ...__VLS_functionalComponentArgsRest(__VLS_7));
const { default: __VLS_11 } = __VLS_9.slots;
let __VLS_12;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_13 = __VLS_asFunctionalComponent1(__VLS_12, new __VLS_12({
    value: (__VLS_ctx.form.sourceLanguage),
    options: (__VLS_ctx.languageOptions),
}));
const __VLS_14 = __VLS_13({
    value: (__VLS_ctx.form.sourceLanguage),
    options: (__VLS_ctx.languageOptions),
}, ...__VLS_functionalComponentArgsRest(__VLS_13));
// @ts-ignore
[disabled, labelPlacement, labelWidth, form, languageOptions,];
var __VLS_9;
let __VLS_17;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({
    label: "目标语言",
}));
const __VLS_19 = __VLS_18({
    label: "目标语言",
}, ...__VLS_functionalComponentArgsRest(__VLS_18));
const { default: __VLS_22 } = __VLS_20.slots;
let __VLS_23;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_24 = __VLS_asFunctionalComponent1(__VLS_23, new __VLS_23({
    value: (__VLS_ctx.form.targetLanguage),
    options: (__VLS_ctx.languageOptions),
}));
const __VLS_25 = __VLS_24({
    value: (__VLS_ctx.form.targetLanguage),
    options: (__VLS_ctx.languageOptions),
}, ...__VLS_functionalComponentArgsRest(__VLS_24));
// @ts-ignore
[form, languageOptions,];
var __VLS_20;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section" },
});
/** @type {__VLS_StyleScopedClasses['config-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section-label" },
});
/** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
let __VLS_28;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({
    label: "翻译引擎",
}));
const __VLS_30 = __VLS_29({
    label: "翻译引擎",
}, ...__VLS_functionalComponentArgsRest(__VLS_29));
const { default: __VLS_33 } = __VLS_31.slots;
let __VLS_34;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_35 = __VLS_asFunctionalComponent1(__VLS_34, new __VLS_34({
    value: (__VLS_ctx.form.translationEngine),
    options: (__VLS_ctx.engineOptions),
}));
const __VLS_36 = __VLS_35({
    value: (__VLS_ctx.form.translationEngine),
    options: (__VLS_ctx.engineOptions),
}, ...__VLS_functionalComponentArgsRest(__VLS_35));
// @ts-ignore
[form, engineOptions,];
var __VLS_31;
let __VLS_39;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({
    label: "备用引擎",
}));
const __VLS_41 = __VLS_40({
    label: "备用引擎",
}, ...__VLS_functionalComponentArgsRest(__VLS_40));
const { default: __VLS_44 } = __VLS_42.slots;
let __VLS_45;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_46 = __VLS_asFunctionalComponent1(__VLS_45, new __VLS_45({
    value: (__VLS_ctx.form.fallbackEndpoint),
    options: ([{ label: '(无)', value: '' }, ...__VLS_ctx.engineOptions]),
    clearable: true,
}));
const __VLS_47 = __VLS_46({
    value: (__VLS_ctx.form.fallbackEndpoint),
    options: ([{ label: '(无)', value: '' }, ...__VLS_ctx.engineOptions]),
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_46));
// @ts-ignore
[form, engineOptions,];
var __VLS_42;
if (__VLS_ctx.hasAnyKeyFields) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "config-section" },
    });
    /** @type {__VLS_StyleScopedClasses['config-section']} */ ;
    if (__VLS_ctx.primaryKeyFields.length > 0 || __VLS_ctx.showPrimaryDeepLFreeToggle) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "api-key-group" },
        });
        /** @type {__VLS_StyleScopedClasses['api-key-group']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "config-section-label" },
        });
        /** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
        (__VLS_ctx.fallbackKeyFields.length > 0 || __VLS_ctx.showFallbackDeepLFreeToggle ? `API 密钥 — ${__VLS_ctx.getEngineName(__VLS_ctx.form.translationEngine)}` : 'API 密钥');
        for (const [field] of __VLS_vFor((__VLS_ctx.primaryKeyFields))) {
            let __VLS_50;
            /** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
            NFormItem;
            // @ts-ignore
            const __VLS_51 = __VLS_asFunctionalComponent1(__VLS_50, new __VLS_50({
                key: (field.key),
                label: (field.label),
            }));
            const __VLS_52 = __VLS_51({
                key: (field.key),
                label: (field.label),
            }, ...__VLS_functionalComponentArgsRest(__VLS_51));
            const { default: __VLS_55 } = __VLS_53.slots;
            let __VLS_56;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_57 = __VLS_asFunctionalComponent1(__VLS_56, new __VLS_56({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.form[field.key] ?? ''),
                placeholder: (field.placeholder),
                type: (field.type),
                showPasswordOn: (field.type === 'password' ? 'click' : undefined),
                clearable: true,
            }));
            const __VLS_58 = __VLS_57({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.form[field.key] ?? ''),
                placeholder: (field.placeholder),
                type: (field.type),
                showPasswordOn: (field.type === 'password' ? 'click' : undefined),
                clearable: true,
            }, ...__VLS_functionalComponentArgsRest(__VLS_57));
            let __VLS_61;
            const __VLS_62 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => { __VLS_ctx.form[field.key] = v || undefined; }) });
            var __VLS_59;
            var __VLS_60;
            // @ts-ignore
            [form, form, form, hasAnyKeyFields, primaryKeyFields, primaryKeyFields, showPrimaryDeepLFreeToggle, fallbackKeyFields, showFallbackDeepLFreeToggle, getEngineName,];
            var __VLS_53;
            // @ts-ignore
            [];
        }
        if (__VLS_ctx.showPrimaryDeepLFreeToggle) {
            let __VLS_63;
            /** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
            NFormItem;
            // @ts-ignore
            const __VLS_64 = __VLS_asFunctionalComponent1(__VLS_63, new __VLS_63({
                label: "使用免费版 API",
            }));
            const __VLS_65 = __VLS_64({
                label: "使用免费版 API",
            }, ...__VLS_functionalComponentArgsRest(__VLS_64));
            const { default: __VLS_68 } = __VLS_66.slots;
            let __VLS_69;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_70 = __VLS_asFunctionalComponent1(__VLS_69, new __VLS_69({
                value: (__VLS_ctx.form.deepLLegitimateFree),
            }));
            const __VLS_71 = __VLS_70({
                value: (__VLS_ctx.form.deepLLegitimateFree),
            }, ...__VLS_functionalComponentArgsRest(__VLS_70));
            // @ts-ignore
            [form, showPrimaryDeepLFreeToggle,];
            var __VLS_66;
        }
    }
    if (__VLS_ctx.fallbackKeyFields.length > 0 || __VLS_ctx.showFallbackDeepLFreeToggle) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "api-key-group" },
        });
        /** @type {__VLS_StyleScopedClasses['api-key-group']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "config-section-label" },
        });
        /** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
        (__VLS_ctx.primaryKeyFields.length > 0 || __VLS_ctx.showPrimaryDeepLFreeToggle ? `API 密钥 — ${__VLS_ctx.getEngineName(__VLS_ctx.form.fallbackEndpoint ?? '')}` : 'API 密钥');
        for (const [field] of __VLS_vFor((__VLS_ctx.fallbackKeyFields))) {
            let __VLS_74;
            /** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
            NFormItem;
            // @ts-ignore
            const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({
                key: ('fallback-' + field.key),
                label: (field.label),
            }));
            const __VLS_76 = __VLS_75({
                key: ('fallback-' + field.key),
                label: (field.label),
            }, ...__VLS_functionalComponentArgsRest(__VLS_75));
            const { default: __VLS_79 } = __VLS_77.slots;
            let __VLS_80;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_81 = __VLS_asFunctionalComponent1(__VLS_80, new __VLS_80({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.form[field.key] ?? ''),
                placeholder: (field.placeholder),
                type: (field.type),
                showPasswordOn: (field.type === 'password' ? 'click' : undefined),
                clearable: true,
            }));
            const __VLS_82 = __VLS_81({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.form[field.key] ?? ''),
                placeholder: (field.placeholder),
                type: (field.type),
                showPasswordOn: (field.type === 'password' ? 'click' : undefined),
                clearable: true,
            }, ...__VLS_functionalComponentArgsRest(__VLS_81));
            let __VLS_85;
            const __VLS_86 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => { __VLS_ctx.form[field.key] = v || undefined; }) });
            var __VLS_83;
            var __VLS_84;
            // @ts-ignore
            [form, form, form, primaryKeyFields, showPrimaryDeepLFreeToggle, fallbackKeyFields, fallbackKeyFields, showFallbackDeepLFreeToggle, getEngineName,];
            var __VLS_77;
            // @ts-ignore
            [];
        }
        if (__VLS_ctx.showFallbackDeepLFreeToggle) {
            let __VLS_87;
            /** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
            NFormItem;
            // @ts-ignore
            const __VLS_88 = __VLS_asFunctionalComponent1(__VLS_87, new __VLS_87({
                label: "使用免费版 API",
            }));
            const __VLS_89 = __VLS_88({
                label: "使用免费版 API",
            }, ...__VLS_functionalComponentArgsRest(__VLS_88));
            const { default: __VLS_92 } = __VLS_90.slots;
            let __VLS_93;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
                value: (__VLS_ctx.form.deepLLegitimateFree),
            }));
            const __VLS_95 = __VLS_94({
                value: (__VLS_ctx.form.deepLLegitimateFree),
            }, ...__VLS_functionalComponentArgsRest(__VLS_94));
            // @ts-ignore
            [form, showFallbackDeepLFreeToggle,];
            var __VLS_90;
        }
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-column" },
});
/** @type {__VLS_StyleScopedClasses['config-column']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section" },
});
/** @type {__VLS_StyleScopedClasses['config-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section-label" },
});
/** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "framework-grid" },
});
/** @type {__VLS_StyleScopedClasses['framework-grid']} */ ;
let __VLS_98;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_99 = __VLS_asFunctionalComponent1(__VLS_98, new __VLS_98({
    label: "UGUI",
}));
const __VLS_100 = __VLS_99({
    label: "UGUI",
}, ...__VLS_functionalComponentArgsRest(__VLS_99));
const { default: __VLS_103 } = __VLS_101.slots;
let __VLS_104;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_105 = __VLS_asFunctionalComponent1(__VLS_104, new __VLS_104({
    value: (__VLS_ctx.form.enableUGUI),
}));
const __VLS_106 = __VLS_105({
    value: (__VLS_ctx.form.enableUGUI),
}, ...__VLS_functionalComponentArgsRest(__VLS_105));
// @ts-ignore
[form,];
var __VLS_101;
let __VLS_109;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_110 = __VLS_asFunctionalComponent1(__VLS_109, new __VLS_109({
    label: "NGUI",
}));
const __VLS_111 = __VLS_110({
    label: "NGUI",
}, ...__VLS_functionalComponentArgsRest(__VLS_110));
const { default: __VLS_114 } = __VLS_112.slots;
let __VLS_115;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_116 = __VLS_asFunctionalComponent1(__VLS_115, new __VLS_115({
    value: (__VLS_ctx.form.enableNGUI),
}));
const __VLS_117 = __VLS_116({
    value: (__VLS_ctx.form.enableNGUI),
}, ...__VLS_functionalComponentArgsRest(__VLS_116));
// @ts-ignore
[form,];
var __VLS_112;
let __VLS_120;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_121 = __VLS_asFunctionalComponent1(__VLS_120, new __VLS_120({
    label: "TextMeshPro",
}));
const __VLS_122 = __VLS_121({
    label: "TextMeshPro",
}, ...__VLS_functionalComponentArgsRest(__VLS_121));
const { default: __VLS_125 } = __VLS_123.slots;
let __VLS_126;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_127 = __VLS_asFunctionalComponent1(__VLS_126, new __VLS_126({
    value: (__VLS_ctx.form.enableTextMeshPro),
}));
const __VLS_128 = __VLS_127({
    value: (__VLS_ctx.form.enableTextMeshPro),
}, ...__VLS_functionalComponentArgsRest(__VLS_127));
// @ts-ignore
[form,];
var __VLS_123;
let __VLS_131;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_132 = __VLS_asFunctionalComponent1(__VLS_131, new __VLS_131({
    label: "TextMesh",
}));
const __VLS_133 = __VLS_132({
    label: "TextMesh",
}, ...__VLS_functionalComponentArgsRest(__VLS_132));
const { default: __VLS_136 } = __VLS_134.slots;
let __VLS_137;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_138 = __VLS_asFunctionalComponent1(__VLS_137, new __VLS_137({
    value: (__VLS_ctx.form.enableTextMesh),
}));
const __VLS_139 = __VLS_138({
    value: (__VLS_ctx.form.enableTextMesh),
}, ...__VLS_functionalComponentArgsRest(__VLS_138));
// @ts-ignore
[form,];
var __VLS_134;
let __VLS_142;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_143 = __VLS_asFunctionalComponent1(__VLS_142, new __VLS_142({
    label: "IMGUI",
}));
const __VLS_144 = __VLS_143({
    label: "IMGUI",
}, ...__VLS_functionalComponentArgsRest(__VLS_143));
const { default: __VLS_147 } = __VLS_145.slots;
let __VLS_148;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_149 = __VLS_asFunctionalComponent1(__VLS_148, new __VLS_148({
    value: (__VLS_ctx.form.enableIMGUI),
}));
const __VLS_150 = __VLS_149({
    value: (__VLS_ctx.form.enableIMGUI),
}, ...__VLS_functionalComponentArgsRest(__VLS_149));
// @ts-ignore
[form,];
var __VLS_145;
let __VLS_153;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_154 = __VLS_asFunctionalComponent1(__VLS_153, new __VLS_153({
    label: "FairyGUI",
}));
const __VLS_155 = __VLS_154({
    label: "FairyGUI",
}, ...__VLS_functionalComponentArgsRest(__VLS_154));
const { default: __VLS_158 } = __VLS_156.slots;
let __VLS_159;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_160 = __VLS_asFunctionalComponent1(__VLS_159, new __VLS_159({
    value: (__VLS_ctx.form.enableFairyGUI),
}));
const __VLS_161 = __VLS_160({
    value: (__VLS_ctx.form.enableFairyGUI),
}, ...__VLS_functionalComponentArgsRest(__VLS_160));
// @ts-ignore
[form,];
var __VLS_156;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section" },
});
/** @type {__VLS_StyleScopedClasses['config-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-section-label" },
});
/** @type {__VLS_StyleScopedClasses['config-section-label']} */ ;
let __VLS_164;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_165 = __VLS_asFunctionalComponent1(__VLS_164, new __VLS_164({
    label: "每次最大翻译字符数",
}));
const __VLS_166 = __VLS_165({
    label: "每次最大翻译字符数",
}, ...__VLS_functionalComponentArgsRest(__VLS_165));
const { default: __VLS_169 } = __VLS_167.slots;
let __VLS_170;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
    value: (__VLS_ctx.form.maxCharactersPerTranslation),
    min: (10),
    max: (5000),
}));
const __VLS_172 = __VLS_171({
    value: (__VLS_ctx.form.maxCharactersPerTranslation),
    min: (10),
    max: (5000),
}, ...__VLS_functionalComponentArgsRest(__VLS_171));
// @ts-ignore
[form,];
var __VLS_167;
let __VLS_175;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_176 = __VLS_asFunctionalComponent1(__VLS_175, new __VLS_175({
    label: "处理富文本",
}));
const __VLS_177 = __VLS_176({
    label: "处理富文本",
}, ...__VLS_functionalComponentArgsRest(__VLS_176));
const { default: __VLS_180 } = __VLS_178.slots;
let __VLS_181;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_182 = __VLS_asFunctionalComponent1(__VLS_181, new __VLS_181({
    value: (__VLS_ctx.form.handleRichText),
}));
const __VLS_183 = __VLS_182({
    value: (__VLS_ctx.form.handleRichText),
}, ...__VLS_functionalComponentArgsRest(__VLS_182));
// @ts-ignore
[form,];
var __VLS_178;
let __VLS_186;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_187 = __VLS_asFunctionalComponent1(__VLS_186, new __VLS_186({
    label: "启用 UI 缩放",
}));
const __VLS_188 = __VLS_187({
    label: "启用 UI 缩放",
}, ...__VLS_functionalComponentArgsRest(__VLS_187));
const { default: __VLS_191 } = __VLS_189.slots;
let __VLS_192;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_193 = __VLS_asFunctionalComponent1(__VLS_192, new __VLS_192({
    value: (__VLS_ctx.form.enableUIResizing),
}));
const __VLS_194 = __VLS_193({
    value: (__VLS_ctx.form.enableUIResizing),
}, ...__VLS_functionalComponentArgsRest(__VLS_193));
// @ts-ignore
[form,];
var __VLS_189;
let __VLS_197;
/** @ts-ignore @type {typeof __VLS_components.NCollapse | typeof __VLS_components.NCollapse} */
NCollapse;
// @ts-ignore
const __VLS_198 = __VLS_asFunctionalComponent1(__VLS_197, new __VLS_197({
    ...{ class: "config-collapse" },
}));
const __VLS_199 = __VLS_198({
    ...{ class: "config-collapse" },
}, ...__VLS_functionalComponentArgsRest(__VLS_198));
/** @type {__VLS_StyleScopedClasses['config-collapse']} */ ;
const { default: __VLS_202 } = __VLS_200.slots;
let __VLS_203;
/** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
NCollapseItem;
// @ts-ignore
const __VLS_204 = __VLS_asFunctionalComponent1(__VLS_203, new __VLS_203({
    title: "字体与排版",
    name: "font",
}));
const __VLS_205 = __VLS_204({
    title: "字体与排版",
    name: "font",
}, ...__VLS_functionalComponentArgsRest(__VLS_204));
const { default: __VLS_208 } = __VLS_206.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "collapse-grid" },
});
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
let __VLS_209;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_210 = __VLS_asFunctionalComponent1(__VLS_209, new __VLS_209({
    label: "覆盖字体",
}));
const __VLS_211 = __VLS_210({
    label: "覆盖字体",
}, ...__VLS_functionalComponentArgsRest(__VLS_210));
const { default: __VLS_214 } = __VLS_212.slots;
let __VLS_215;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_216 = __VLS_asFunctionalComponent1(__VLS_215, new __VLS_215({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFont ?? ''),
    placeholder: "UGUI 自定义字体名称",
    clearable: true,
}));
const __VLS_217 = __VLS_216({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFont ?? ''),
    placeholder: "UGUI 自定义字体名称",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_216));
let __VLS_220;
const __VLS_221 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.overrideFont = v; }) });
var __VLS_218;
var __VLS_219;
// @ts-ignore
[form, form,];
var __VLS_212;
let __VLS_222;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_223 = __VLS_asFunctionalComponent1(__VLS_222, new __VLS_222({
    label: "覆盖字体大小",
}));
const __VLS_224 = __VLS_223({
    label: "覆盖字体大小",
}, ...__VLS_functionalComponentArgsRest(__VLS_223));
const { default: __VLS_227 } = __VLS_225.slots;
let __VLS_228;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_229 = __VLS_asFunctionalComponent1(__VLS_228, new __VLS_228({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFontSize ?? ''),
    placeholder: "UGUI 自定义字体大小",
    clearable: true,
}));
const __VLS_230 = __VLS_229({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFontSize ?? ''),
    placeholder: "UGUI 自定义字体大小",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_229));
let __VLS_233;
const __VLS_234 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.overrideFontSize = v; }) });
var __VLS_231;
var __VLS_232;
// @ts-ignore
[form, form,];
var __VLS_225;
let __VLS_235;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_236 = __VLS_asFunctionalComponent1(__VLS_235, new __VLS_235({
    label: "TMP 覆盖字体",
}));
const __VLS_237 = __VLS_236({
    label: "TMP 覆盖字体",
}, ...__VLS_functionalComponentArgsRest(__VLS_236));
const { default: __VLS_240 } = __VLS_238.slots;
let __VLS_241;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_242 = __VLS_asFunctionalComponent1(__VLS_241, new __VLS_241({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFontTextMeshPro ?? ''),
    placeholder: "TextMeshPro 自定义字体",
    clearable: true,
}));
const __VLS_243 = __VLS_242({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.overrideFontTextMeshPro ?? ''),
    placeholder: "TextMeshPro 自定义字体",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_242));
let __VLS_246;
const __VLS_247 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.overrideFontTextMeshPro = v; }) });
var __VLS_244;
var __VLS_245;
// @ts-ignore
[form, form,];
var __VLS_238;
let __VLS_248;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_249 = __VLS_asFunctionalComponent1(__VLS_248, new __VLS_248({
    label: "TMP 后备字体",
}));
const __VLS_250 = __VLS_249({
    label: "TMP 后备字体",
}, ...__VLS_functionalComponentArgsRest(__VLS_249));
const { default: __VLS_253 } = __VLS_251.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "tmp-font-row" },
});
/** @type {__VLS_StyleScopedClasses['tmp-font-row']} */ ;
let __VLS_254;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_255 = __VLS_asFunctionalComponent1(__VLS_254, new __VLS_254({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.fallbackFontTextMeshPro ?? ''),
    placeholder: "TextMeshPro 后备字体",
    clearable: true,
}));
const __VLS_256 = __VLS_255({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.fallbackFontTextMeshPro ?? ''),
    placeholder: "TextMeshPro 后备字体",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_255));
let __VLS_259;
const __VLS_260 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.fallbackFontTextMeshPro = v; }) });
var __VLS_257;
var __VLS_258;
if (__VLS_ctx.tmpFontInstalled !== null) {
    let __VLS_261;
    /** @ts-ignore @type {typeof __VLS_components.NTooltip | typeof __VLS_components.NTooltip} */
    NTooltip;
    // @ts-ignore
    const __VLS_262 = __VLS_asFunctionalComponent1(__VLS_261, new __VLS_261({}));
    const __VLS_263 = __VLS_262({}, ...__VLS_functionalComponentArgsRest(__VLS_262));
    const { default: __VLS_266 } = __VLS_264.slots;
    {
        const { trigger: __VLS_267 } = __VLS_264.slots;
        if (!__VLS_ctx.tmpFontInstalled) {
            let __VLS_268;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_269 = __VLS_asFunctionalComponent1(__VLS_268, new __VLS_268({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                loading: (__VLS_ctx.tmpFontLoading),
            }));
            const __VLS_270 = __VLS_269({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                loading: (__VLS_ctx.tmpFontLoading),
            }, ...__VLS_functionalComponentArgsRest(__VLS_269));
            let __VLS_273;
            const __VLS_274 = ({ click: {} },
                { onClick: (__VLS_ctx.handleInstallTmpFont) });
            const { default: __VLS_275 } = __VLS_271.slots;
            {
                const { icon: __VLS_276 } = __VLS_271.slots;
                let __VLS_277;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_278 = __VLS_asFunctionalComponent1(__VLS_277, new __VLS_277({
                    size: (14),
                }));
                const __VLS_279 = __VLS_278({
                    size: (14),
                }, ...__VLS_functionalComponentArgsRest(__VLS_278));
                const { default: __VLS_282 } = __VLS_280.slots;
                let __VLS_283;
                /** @ts-ignore @type {typeof __VLS_components.DownloadOutlined} */
                DownloadOutlined;
                // @ts-ignore
                const __VLS_284 = __VLS_asFunctionalComponent1(__VLS_283, new __VLS_283({}));
                const __VLS_285 = __VLS_284({}, ...__VLS_functionalComponentArgsRest(__VLS_284));
                // @ts-ignore
                [form, form, tmpFontInstalled, tmpFontInstalled, tmpFontLoading, handleInstallTmpFont,];
                var __VLS_280;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_271;
            var __VLS_272;
        }
        else {
            let __VLS_288;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_289 = __VLS_asFunctionalComponent1(__VLS_288, new __VLS_288({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.tmpFontLoading),
            }));
            const __VLS_290 = __VLS_289({
                ...{ 'onClick': {} },
                size: "small",
                type: "error",
                ghost: true,
                loading: (__VLS_ctx.tmpFontLoading),
            }, ...__VLS_functionalComponentArgsRest(__VLS_289));
            let __VLS_293;
            const __VLS_294 = ({ click: {} },
                { onClick: (__VLS_ctx.handleUninstallTmpFont) });
            const { default: __VLS_295 } = __VLS_291.slots;
            {
                const { icon: __VLS_296 } = __VLS_291.slots;
                let __VLS_297;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_298 = __VLS_asFunctionalComponent1(__VLS_297, new __VLS_297({
                    size: (14),
                }));
                const __VLS_299 = __VLS_298({
                    size: (14),
                }, ...__VLS_functionalComponentArgsRest(__VLS_298));
                const { default: __VLS_302 } = __VLS_300.slots;
                let __VLS_303;
                /** @ts-ignore @type {typeof __VLS_components.DeleteOutlineOutlined} */
                DeleteOutlineOutlined;
                // @ts-ignore
                const __VLS_304 = __VLS_asFunctionalComponent1(__VLS_303, new __VLS_303({}));
                const __VLS_305 = __VLS_304({}, ...__VLS_functionalComponentArgsRest(__VLS_304));
                // @ts-ignore
                [tmpFontLoading, handleUninstallTmpFont,];
                var __VLS_300;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_291;
            var __VLS_292;
        }
        // @ts-ignore
        [];
    }
    (__VLS_ctx.tmpFontInstalled ? 'TMP 字体资源已安装到游戏内' : '安装思源黑体 TMP 字体资源（自动匹配 Unity 版本）');
    // @ts-ignore
    [tmpFontInstalled,];
    var __VLS_264;
}
// @ts-ignore
[];
var __VLS_251;
let __VLS_308;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_309 = __VLS_asFunctionalComponent1(__VLS_308, new __VLS_308({
    label: "行间距缩放",
}));
const __VLS_310 = __VLS_309({
    label: "行间距缩放",
}, ...__VLS_functionalComponentArgsRest(__VLS_309));
const { default: __VLS_313 } = __VLS_311.slots;
let __VLS_314;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_315 = __VLS_asFunctionalComponent1(__VLS_314, new __VLS_314({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.resizeUILineSpacingScale ?? ''),
    placeholder: "例如 0.80",
    clearable: true,
}));
const __VLS_316 = __VLS_315({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.resizeUILineSpacingScale ?? ''),
    placeholder: "例如 0.80",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_315));
let __VLS_319;
const __VLS_320 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.resizeUILineSpacingScale = v; }) });
var __VLS_317;
var __VLS_318;
// @ts-ignore
[form, form,];
var __VLS_311;
let __VLS_321;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_322 = __VLS_asFunctionalComponent1(__VLS_321, new __VLS_321({
    label: "强制 UI 缩放",
}));
const __VLS_323 = __VLS_322({
    label: "强制 UI 缩放",
}, ...__VLS_functionalComponentArgsRest(__VLS_322));
const { default: __VLS_326 } = __VLS_324.slots;
let __VLS_327;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_328 = __VLS_asFunctionalComponent1(__VLS_327, new __VLS_327({
    value: (__VLS_ctx.form.forceUIResizing),
}));
const __VLS_329 = __VLS_328({
    value: (__VLS_ctx.form.forceUIResizing),
}, ...__VLS_functionalComponentArgsRest(__VLS_328));
// @ts-ignore
[form,];
var __VLS_324;
// @ts-ignore
[];
var __VLS_206;
let __VLS_332;
/** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
NCollapseItem;
// @ts-ignore
const __VLS_333 = __VLS_asFunctionalComponent1(__VLS_332, new __VLS_332({
    title: "翻译行为",
    name: "behaviour",
}));
const __VLS_334 = __VLS_333({
    title: "翻译行为",
    name: "behaviour",
}, ...__VLS_functionalComponentArgsRest(__VLS_333));
const { default: __VLS_337 } = __VLS_335.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "collapse-grid" },
});
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
let __VLS_338;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_339 = __VLS_asFunctionalComponent1(__VLS_338, new __VLS_338({
    label: "强制换行字符数",
}));
const __VLS_340 = __VLS_339({
    label: "强制换行字符数",
}, ...__VLS_functionalComponentArgsRest(__VLS_339));
const { default: __VLS_343 } = __VLS_341.slots;
let __VLS_344;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_345 = __VLS_asFunctionalComponent1(__VLS_344, new __VLS_344({
    value: (__VLS_ctx.form.forceSplitTextAfterCharacters),
    min: (0),
    max: (10000),
}));
const __VLS_346 = __VLS_345({
    value: (__VLS_ctx.form.forceSplitTextAfterCharacters),
    min: (0),
    max: (10000),
}, ...__VLS_functionalComponentArgsRest(__VLS_345));
// @ts-ignore
[form,];
var __VLS_341;
let __VLS_349;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_350 = __VLS_asFunctionalComponent1(__VLS_349, new __VLS_349({
    label: "文本解析最大递归",
}));
const __VLS_351 = __VLS_350({
    label: "文本解析最大递归",
}, ...__VLS_functionalComponentArgsRest(__VLS_350));
const { default: __VLS_354 } = __VLS_352.slots;
let __VLS_355;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_356 = __VLS_asFunctionalComponent1(__VLS_355, new __VLS_355({
    value: (__VLS_ctx.form.maxTextParserRecursion),
    min: (1),
    max: (100),
}));
const __VLS_357 = __VLS_356({
    value: (__VLS_ctx.form.maxTextParserRecursion),
    min: (1),
    max: (100),
}, ...__VLS_functionalComponentArgsRest(__VLS_356));
// @ts-ignore
[form,];
var __VLS_352;
let __VLS_360;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_361 = __VLS_asFunctionalComponent1(__VLS_360, new __VLS_360({
    label: "文本获取兼容模式",
}));
const __VLS_362 = __VLS_361({
    label: "文本获取兼容模式",
}, ...__VLS_functionalComponentArgsRest(__VLS_361));
const { default: __VLS_365 } = __VLS_363.slots;
let __VLS_366;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_367 = __VLS_asFunctionalComponent1(__VLS_366, new __VLS_366({
    value: (__VLS_ctx.form.textGetterCompatibilityMode),
}));
const __VLS_368 = __VLS_367({
    value: (__VLS_ctx.form.textGetterCompatibilityMode),
}, ...__VLS_functionalComponentArgsRest(__VLS_367));
// @ts-ignore
[form,];
var __VLS_363;
let __VLS_371;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_372 = __VLS_asFunctionalComponent1(__VLS_371, new __VLS_371({
    label: "翻译辅助器",
}));
const __VLS_373 = __VLS_372({
    label: "翻译辅助器",
}, ...__VLS_functionalComponentArgsRest(__VLS_372));
const { default: __VLS_376 } = __VLS_374.slots;
let __VLS_377;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_378 = __VLS_asFunctionalComponent1(__VLS_377, new __VLS_377({
    value: (__VLS_ctx.form.enableTranslationHelper),
}));
const __VLS_379 = __VLS_378({
    value: (__VLS_ctx.form.enableTranslationHelper),
}, ...__VLS_functionalComponentArgsRest(__VLS_378));
// @ts-ignore
[form,];
var __VLS_374;
let __VLS_382;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_383 = __VLS_asFunctionalComponent1(__VLS_382, new __VLS_382({
    label: "数字模板化",
}));
const __VLS_384 = __VLS_383({
    label: "数字模板化",
}, ...__VLS_functionalComponentArgsRest(__VLS_383));
const { default: __VLS_387 } = __VLS_385.slots;
let __VLS_388;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_389 = __VLS_asFunctionalComponent1(__VLS_388, new __VLS_388({
    value: (__VLS_ctx.form.templateAllNumberAway),
}));
const __VLS_390 = __VLS_389({
    value: (__VLS_ctx.form.templateAllNumberAway),
}, ...__VLS_functionalComponentArgsRest(__VLS_389));
// @ts-ignore
[form,];
var __VLS_385;
let __VLS_393;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_394 = __VLS_asFunctionalComponent1(__VLS_393, new __VLS_393({
    label: "禁用 TMP 滚动效果",
}));
const __VLS_395 = __VLS_394({
    label: "禁用 TMP 滚动效果",
}, ...__VLS_functionalComponentArgsRest(__VLS_394));
const { default: __VLS_398 } = __VLS_396.slots;
let __VLS_399;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_400 = __VLS_asFunctionalComponent1(__VLS_399, new __VLS_399({
    value: (__VLS_ctx.form.disableTextMeshProScrollInEffects),
}));
const __VLS_401 = __VLS_400({
    value: (__VLS_ctx.form.disableTextMeshProScrollInEffects),
}, ...__VLS_functionalComponentArgsRest(__VLS_400));
// @ts-ignore
[form,];
var __VLS_396;
let __VLS_404;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_405 = __VLS_asFunctionalComponent1(__VLS_404, new __VLS_404({
    label: "缓存已解析翻译",
}));
const __VLS_406 = __VLS_405({
    label: "缓存已解析翻译",
}, ...__VLS_functionalComponentArgsRest(__VLS_405));
const { default: __VLS_409 } = __VLS_407.slots;
let __VLS_410;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_411 = __VLS_asFunctionalComponent1(__VLS_410, new __VLS_410({
    value: (__VLS_ctx.form.cacheParsedTranslations),
}));
const __VLS_412 = __VLS_411({
    value: (__VLS_ctx.form.cacheParsedTranslations),
}, ...__VLS_functionalComponentArgsRest(__VLS_411));
// @ts-ignore
[form,];
var __VLS_407;
let __VLS_415;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_416 = __VLS_asFunctionalComponent1(__VLS_415, new __VLS_415({
    label: "缓存空白差异",
}));
const __VLS_417 = __VLS_416({
    label: "缓存空白差异",
}, ...__VLS_functionalComponentArgsRest(__VLS_416));
const { default: __VLS_420 } = __VLS_418.slots;
let __VLS_421;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_422 = __VLS_asFunctionalComponent1(__VLS_421, new __VLS_421({
    value: (__VLS_ctx.form.cacheWhitespaceDifferences),
}));
const __VLS_423 = __VLS_422({
    value: (__VLS_ctx.form.cacheWhitespaceDifferences),
}, ...__VLS_functionalComponentArgsRest(__VLS_422));
// @ts-ignore
[form,];
var __VLS_418;
let __VLS_426;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_427 = __VLS_asFunctionalComponent1(__VLS_426, new __VLS_426({
    label: "对话忽略空白",
}));
const __VLS_428 = __VLS_427({
    label: "对话忽略空白",
}, ...__VLS_functionalComponentArgsRest(__VLS_427));
const { default: __VLS_431 } = __VLS_429.slots;
let __VLS_432;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_433 = __VLS_asFunctionalComponent1(__VLS_432, new __VLS_432({
    value: (__VLS_ctx.form.ignoreWhitespaceInDialogue),
}));
const __VLS_434 = __VLS_433({
    value: (__VLS_ctx.form.ignoreWhitespaceInDialogue),
}, ...__VLS_functionalComponentArgsRest(__VLS_433));
// @ts-ignore
[form,];
var __VLS_429;
let __VLS_437;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_438 = __VLS_asFunctionalComponent1(__VLS_437, new __VLS_437({
    label: "最小对话字符数",
}));
const __VLS_439 = __VLS_438({
    label: "最小对话字符数",
}, ...__VLS_functionalComponentArgsRest(__VLS_438));
const { default: __VLS_442 } = __VLS_440.slots;
let __VLS_443;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_444 = __VLS_asFunctionalComponent1(__VLS_443, new __VLS_443({
    value: (__VLS_ctx.form.minDialogueChars),
    min: (1),
    max: (100),
    size: "small",
}));
const __VLS_445 = __VLS_444({
    value: (__VLS_ctx.form.minDialogueChars),
    min: (1),
    max: (100),
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_444));
// @ts-ignore
[form,];
var __VLS_440;
let __VLS_448;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_449 = __VLS_asFunctionalComponent1(__VLS_448, new __VLS_448({
    label: "翻译输出文件",
}));
const __VLS_450 = __VLS_449({
    label: "翻译输出文件",
}, ...__VLS_functionalComponentArgsRest(__VLS_449));
const { default: __VLS_453 } = __VLS_451.slots;
let __VLS_454;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_455 = __VLS_asFunctionalComponent1(__VLS_454, new __VLS_454({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.outputFile ?? ''),
    placeholder: "\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u005c\u007b\u004c\u0061\u006e\u0067\u007d\u005c\u0054\u0065\u0078\u0074\u005c\u005f\u0041\u0075\u0074\u006f\u0047\u0065\u006e\u0065\u0072\u0061\u0074\u0065\u0064\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u0073\u002e\u0074\u0078\u0074",
    clearable: true,
}));
const __VLS_456 = __VLS_455({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.outputFile ?? ''),
    placeholder: "\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u005c\u007b\u004c\u0061\u006e\u0067\u007d\u005c\u0054\u0065\u0078\u0074\u005c\u005f\u0041\u0075\u0074\u006f\u0047\u0065\u006e\u0065\u0072\u0061\u0074\u0065\u0064\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u0073\u002e\u0074\u0078\u0074",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_455));
let __VLS_459;
const __VLS_460 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.outputFile = v; }) });
var __VLS_457;
var __VLS_458;
// @ts-ignore
[form, form,];
var __VLS_451;
// @ts-ignore
[];
var __VLS_335;
let __VLS_461;
/** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
NCollapseItem;
// @ts-ignore
const __VLS_462 = __VLS_asFunctionalComponent1(__VLS_461, new __VLS_461({
    title: "插件设置",
    name: "plugin",
}));
const __VLS_463 = __VLS_462({
    title: "插件设置",
    name: "plugin",
}, ...__VLS_functionalComponentArgsRest(__VLS_462));
const { default: __VLS_466 } = __VLS_464.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "collapse-grid" },
});
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
let __VLS_467;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_468 = __VLS_asFunctionalComponent1(__VLS_467, new __VLS_467({
    label: "译文文件变更时自动重载",
}));
const __VLS_469 = __VLS_468({
    label: "译文文件变更时自动重载",
}, ...__VLS_functionalComponentArgsRest(__VLS_468));
const { default: __VLS_472 } = __VLS_470.slots;
let __VLS_473;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_474 = __VLS_asFunctionalComponent1(__VLS_473, new __VLS_473({
    value: (__VLS_ctx.form.reloadTranslationsOnFileChange),
}));
const __VLS_475 = __VLS_474({
    value: (__VLS_ctx.form.reloadTranslationsOnFileChange),
}, ...__VLS_functionalComponentArgsRest(__VLS_474));
// @ts-ignore
[form,];
var __VLS_470;
// @ts-ignore
[];
var __VLS_464;
let __VLS_478;
/** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
NCollapseItem;
// @ts-ignore
const __VLS_479 = __VLS_asFunctionalComponent1(__VLS_478, new __VLS_478({
    title: "贴图翻译",
    name: "texture",
}));
const __VLS_480 = __VLS_479({
    title: "贴图翻译",
    name: "texture",
}, ...__VLS_functionalComponentArgsRest(__VLS_479));
const { default: __VLS_483 } = __VLS_481.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "collapse-grid" },
});
/** @type {__VLS_StyleScopedClasses['collapse-grid']} */ ;
let __VLS_484;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_485 = __VLS_asFunctionalComponent1(__VLS_484, new __VLS_484({
    label: "启用贴图翻译",
}));
const __VLS_486 = __VLS_485({
    label: "启用贴图翻译",
}, ...__VLS_functionalComponentArgsRest(__VLS_485));
const { default: __VLS_489 } = __VLS_487.slots;
let __VLS_490;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_491 = __VLS_asFunctionalComponent1(__VLS_490, new __VLS_490({
    value: (__VLS_ctx.form.enableTextureTranslation),
}));
const __VLS_492 = __VLS_491({
    value: (__VLS_ctx.form.enableTextureTranslation),
}, ...__VLS_functionalComponentArgsRest(__VLS_491));
// @ts-ignore
[form,];
var __VLS_487;
let __VLS_495;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_496 = __VLS_asFunctionalComponent1(__VLS_495, new __VLS_495({
    label: "启用贴图导出",
}));
const __VLS_497 = __VLS_496({
    label: "启用贴图导出",
}, ...__VLS_functionalComponentArgsRest(__VLS_496));
const { default: __VLS_500 } = __VLS_498.slots;
let __VLS_501;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_502 = __VLS_asFunctionalComponent1(__VLS_501, new __VLS_501({
    value: (__VLS_ctx.form.enableTextureDumping),
}));
const __VLS_503 = __VLS_502({
    value: (__VLS_ctx.form.enableTextureDumping),
}, ...__VLS_functionalComponentArgsRest(__VLS_502));
// @ts-ignore
[form,];
var __VLS_498;
let __VLS_506;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_507 = __VLS_asFunctionalComponent1(__VLS_506, new __VLS_506({
    label: "启用贴图切换",
}));
const __VLS_508 = __VLS_507({
    label: "启用贴图切换",
}, ...__VLS_functionalComponentArgsRest(__VLS_507));
const { default: __VLS_511 } = __VLS_509.slots;
let __VLS_512;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_513 = __VLS_asFunctionalComponent1(__VLS_512, new __VLS_512({
    value: (__VLS_ctx.form.enableTextureToggling),
}));
const __VLS_514 = __VLS_513({
    value: (__VLS_ctx.form.enableTextureToggling),
}, ...__VLS_functionalComponentArgsRest(__VLS_513));
// @ts-ignore
[form,];
var __VLS_509;
let __VLS_517;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_518 = __VLS_asFunctionalComponent1(__VLS_517, new __VLS_517({
    label: "场景加载时扫描贴图",
}));
const __VLS_519 = __VLS_518({
    label: "场景加载时扫描贴图",
}, ...__VLS_functionalComponentArgsRest(__VLS_518));
const { default: __VLS_522 } = __VLS_520.slots;
let __VLS_523;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_524 = __VLS_asFunctionalComponent1(__VLS_523, new __VLS_523({
    value: (__VLS_ctx.form.enableTextureScanOnSceneLoad),
}));
const __VLS_525 = __VLS_524({
    value: (__VLS_ctx.form.enableTextureScanOnSceneLoad),
}, ...__VLS_functionalComponentArgsRest(__VLS_524));
// @ts-ignore
[form,];
var __VLS_520;
let __VLS_528;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_529 = __VLS_asFunctionalComponent1(__VLS_528, new __VLS_528({
    label: "加载未修改贴图",
}));
const __VLS_530 = __VLS_529({
    label: "加载未修改贴图",
}, ...__VLS_functionalComponentArgsRest(__VLS_529));
const { default: __VLS_533 } = __VLS_531.slots;
let __VLS_534;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_535 = __VLS_asFunctionalComponent1(__VLS_534, new __VLS_534({
    value: (__VLS_ctx.form.loadUnmodifiedTextures),
}));
const __VLS_536 = __VLS_535({
    value: (__VLS_ctx.form.loadUnmodifiedTextures),
}, ...__VLS_functionalComponentArgsRest(__VLS_535));
// @ts-ignore
[form,];
var __VLS_531;
let __VLS_539;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_540 = __VLS_asFunctionalComponent1(__VLS_539, new __VLS_539({
    label: "启用 Sprite 钩子",
}));
const __VLS_541 = __VLS_540({
    label: "启用 Sprite 钩子",
}, ...__VLS_functionalComponentArgsRest(__VLS_540));
const { default: __VLS_544 } = __VLS_542.slots;
let __VLS_545;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_546 = __VLS_asFunctionalComponent1(__VLS_545, new __VLS_545({
    value: (__VLS_ctx.form.enableSpriteHooking),
}));
const __VLS_547 = __VLS_546({
    value: (__VLS_ctx.form.enableSpriteHooking),
}, ...__VLS_functionalComponentArgsRest(__VLS_546));
// @ts-ignore
[form,];
var __VLS_542;
let __VLS_550;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_551 = __VLS_asFunctionalComponent1(__VLS_550, new __VLS_550({
    label: "贴图哈希策略",
}));
const __VLS_552 = __VLS_551({
    label: "贴图哈希策略",
}, ...__VLS_functionalComponentArgsRest(__VLS_551));
const { default: __VLS_555 } = __VLS_553.slots;
let __VLS_556;
/** @ts-ignore @type {typeof __VLS_components.NSelect} */
NSelect;
// @ts-ignore
const __VLS_557 = __VLS_asFunctionalComponent1(__VLS_556, new __VLS_556({
    value: (__VLS_ctx.form.textureHashGenerationStrategy),
    options: (__VLS_ctx.textureHashOptions),
}));
const __VLS_558 = __VLS_557({
    value: (__VLS_ctx.form.textureHashGenerationStrategy),
    options: (__VLS_ctx.textureHashOptions),
}, ...__VLS_functionalComponentArgsRest(__VLS_557));
// @ts-ignore
[form, textureHashOptions,];
var __VLS_553;
let __VLS_561;
/** @ts-ignore @type {typeof __VLS_components.NFormItem | typeof __VLS_components.NFormItem} */
NFormItem;
// @ts-ignore
const __VLS_562 = __VLS_asFunctionalComponent1(__VLS_561, new __VLS_561({
    label: "贴图目录",
}));
const __VLS_563 = __VLS_562({
    label: "贴图目录",
}, ...__VLS_functionalComponentArgsRest(__VLS_562));
const { default: __VLS_566 } = __VLS_564.slots;
let __VLS_567;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_568 = __VLS_asFunctionalComponent1(__VLS_567, new __VLS_567({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.textureDirectory ?? ''),
    placeholder: "\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u005c\u007b\u004c\u0061\u006e\u0067\u007d\u005c\u0054\u0065\u0078\u0074\u0075\u0072\u0065",
    clearable: true,
}));
const __VLS_569 = __VLS_568({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.form.textureDirectory ?? ''),
    placeholder: "\u0054\u0072\u0061\u006e\u0073\u006c\u0061\u0074\u0069\u006f\u006e\u005c\u007b\u004c\u0061\u006e\u0067\u007d\u005c\u0054\u0065\u0078\u0074\u0075\u0072\u0065",
    clearable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_568));
let __VLS_572;
const __VLS_573 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => { __VLS_ctx.form.textureDirectory = v; }) });
var __VLS_570;
var __VLS_571;
// @ts-ignore
[form, form,];
var __VLS_564;
// @ts-ignore
[];
var __VLS_481;
// @ts-ignore
[];
var __VLS_200;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "config-footer" },
});
/** @type {__VLS_StyleScopedClasses['config-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
let __VLS_574;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_575 = __VLS_asFunctionalComponent1(__VLS_574, new __VLS_574({
    ...{ 'onClick': {} },
    disabled: (__VLS_ctx.disabled),
}));
const __VLS_576 = __VLS_575({
    ...{ 'onClick': {} },
    disabled: (__VLS_ctx.disabled),
}, ...__VLS_functionalComponentArgsRest(__VLS_575));
let __VLS_579;
const __VLS_580 = ({ click: {} },
    { onClick: (__VLS_ctx.openConfigEditor) });
const { default: __VLS_581 } = __VLS_577.slots;
{
    const { icon: __VLS_582 } = __VLS_577.slots;
    let __VLS_583;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_584 = __VLS_asFunctionalComponent1(__VLS_583, new __VLS_583({}));
    const __VLS_585 = __VLS_584({}, ...__VLS_functionalComponentArgsRest(__VLS_584));
    const { default: __VLS_588 } = __VLS_586.slots;
    let __VLS_589;
    /** @ts-ignore @type {typeof __VLS_components.EditNoteOutlined} */
    EditNoteOutlined;
    // @ts-ignore
    const __VLS_590 = __VLS_asFunctionalComponent1(__VLS_589, new __VLS_589({}));
    const __VLS_591 = __VLS_590({}, ...__VLS_functionalComponentArgsRest(__VLS_590));
    // @ts-ignore
    [disabled, openConfigEditor,];
    var __VLS_586;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_577;
var __VLS_578;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
