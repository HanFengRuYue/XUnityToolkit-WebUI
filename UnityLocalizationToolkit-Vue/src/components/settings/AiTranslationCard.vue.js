import { ref, computed } from 'vue';
import { NSelect, NInput, NInputNumber, NButton, NIcon, NSwitch, NTag, NPopconfirm, NCollapse, NCollapseItem, NSlider, useMessage, } from 'naive-ui';
import { SmartToyOutlined, RestoreOutlined, ScienceOutlined, AddOutlined, DeleteOutlined, SearchOutlined, } from '@vicons/material';
import { translateApi } from '@/api/games';
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts';
const props = withDefaults(defineProps(), {
    embedded: false,
});
const emit = defineEmits();
const message = useMessage();
const testing = ref(false);
const fetchingModels = ref({});
function update(patch) {
    emit('update:modelValue', { ...props.modelValue, ...patch });
}
function updateEndpoint(index, patch) {
    const endpoints = [...props.modelValue.endpoints];
    endpoints[index] = Object.assign({}, endpoints[index], patch);
    update({ endpoints });
}
function addEndpoint() {
    const id = Math.random().toString(36).substring(2, 10);
    const endpoints = [...props.modelValue.endpoints, {
            id,
            name: `提供商 ${props.modelValue.endpoints.length + 1}`,
            provider: 'OpenAI',
            apiBaseUrl: '',
            apiKey: '',
            modelName: '',
            priority: 5,
            enabled: true,
        }];
    update({ endpoints });
}
function removeEndpoint(index) {
    const endpoints = props.modelValue.endpoints.filter((_, i) => i !== index);
    update({ endpoints });
}
const providerOptions = [
    { label: 'OpenAI', value: 'OpenAI' },
    { label: 'Claude (Anthropic)', value: 'Claude' },
    { label: 'Google Gemini', value: 'Gemini' },
    { label: 'DeepSeek', value: 'DeepSeek' },
    { label: '通义千问 (Qwen)', value: 'Qwen' },
    { label: '智谱清言 (GLM)', value: 'GLM' },
    { label: 'Kimi (Moonshot)', value: 'Kimi' },
    { label: '自定义 (OpenAI 兼容)', value: 'Custom' },
];
const defaultBaseUrls = {
    OpenAI: 'https://api.openai.com/v1',
    Claude: 'https://api.anthropic.com/v1',
    Gemini: 'https://generativelanguage.googleapis.com/v1beta',
    DeepSeek: 'https://api.deepseek.com',
    Qwen: 'https://dashscope.aliyuncs.com/compatible-mode/v1',
    GLM: 'https://open.bigmodel.cn/api/paas/v4',
    Kimi: 'https://api.moonshot.cn/v1',
    Custom: '',
};
const defaultModels = {
    OpenAI: 'gpt-4o-mini',
    Claude: 'claude-haiku-4-5-20251001',
    Gemini: 'gemini-2.0-flash',
    DeepSeek: 'deepseek-chat',
    Qwen: 'qwen-plus',
    GLM: 'glm-4-flash',
    Kimi: 'moonshot-v1-auto',
    Custom: '',
};
const supportsModelList = {
    OpenAI: true,
    Claude: false,
    Gemini: true,
    DeepSeek: true,
    Qwen: true,
    GLM: false,
    Kimi: true,
    Custom: true,
};
function getBaseUrlPlaceholder(provider) {
    return defaultBaseUrls[provider] || '请输入 API 地址';
}
function getModelPlaceholder(provider) {
    return defaultModels[provider] || '请输入模型名称';
}
function handleRestorePrompt() {
    update({ systemPrompt: DEFAULT_SYSTEM_PROMPT });
}
const modelOptions = ref({});
async function handleFetchModels(endpoint) {
    if (!endpoint.apiKey) {
        message.warning('请先填写 API Key');
        return;
    }
    fetchingModels.value = { ...fetchingModels.value, [endpoint.id]: true };
    try {
        const models = await translateApi.fetchModels(endpoint.provider, endpoint.apiBaseUrl, endpoint.apiKey);
        if (models.length > 0) {
            modelOptions.value = {
                ...modelOptions.value,
                [endpoint.id]: models.map(m => ({ label: m, value: m })),
            };
            message.success(`获取到 ${models.length} 个模型`);
        }
        else {
            message.warning('未获取到模型列表，请手动输入模型名称');
        }
    }
    catch {
        message.error('获取模型列表失败');
    }
    finally {
        fetchingModels.value = { ...fetchingModels.value, [endpoint.id]: false };
    }
}
const testingEndpoint = ref({});
async function handleTestEndpoint(ep) {
    if (!ep.apiKey) {
        message.warning('请先填写 API Key');
        return;
    }
    testingEndpoint.value = { ...testingEndpoint.value, [ep.id]: true };
    try {
        const results = await translateApi.testTranslate([ep], props.modelValue.systemPrompt, props.modelValue.temperature);
        const r = results[0];
        if (r && r.success) {
            message.success(`${ep.name || '提供商'} 测试成功: ${r.translations?.join(' | ')} (${Math.round(r.responseTimeMs)}ms)`);
        }
        else {
            message.error(`${ep.name || '提供商'} 测试失败: ${r?.error || '未知错误'}`);
        }
    }
    catch (e) {
        const msg = e instanceof Error ? e.message : '未知错误';
        message.error(`测试失败: ${msg}`);
    }
    finally {
        testingEndpoint.value = { ...testingEndpoint.value, [ep.id]: false };
    }
}
async function handleTestAll() {
    const enabled = props.modelValue.endpoints.filter(e => e.enabled && e.apiKey);
    if (enabled.length === 0) {
        message.warning('请先配置至少一个可用的提供商');
        return;
    }
    testing.value = true;
    try {
        const results = await translateApi.testTranslate(enabled, props.modelValue.systemPrompt, props.modelValue.temperature);
        const successCount = results.filter(r => r.success).length;
        const totalCount = results.length;
        if (successCount === totalCount) {
            const details = results.map(r => `${r.endpointName}: ${r.translations?.join(' | ')} (${Math.round(r.responseTimeMs)}ms)`).join('\n');
            message.success(`全部 ${totalCount} 个提供商测试成功\n${details}`, { duration: 6000 });
        }
        else {
            const failed = results.filter(r => !r.success);
            const succeeded = results.filter(r => r.success);
            let msg = `${successCount}/${totalCount} 个提供商测试成功`;
            if (succeeded.length > 0)
                msg += `\n成功: ${succeeded.map(r => r.endpointName).join(', ')}`;
            msg += `\n失败: ${failed.map(r => `${r.endpointName}(${r.error})`).join(', ')}`;
            message.warning(msg, { duration: 8000 });
        }
    }
    catch (e) {
        const msg = e instanceof Error ? e.message : '未知错误';
        message.error(`测试失败: ${msg}`);
    }
    finally {
        testing.value = false;
    }
}
const priorityMarks = computed(() => {
    const marks = {};
    for (let i = 1; i <= 10; i++)
        marks[i] = String(i);
    return marks;
});
const __VLS_defaults = {
    embedded: false,
};
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
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['restore-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['empty-endpoints']} */ ;
/** @type {__VLS_StyleScopedClasses['model-row']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row-inline']} */ ;
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: ({ 'section-card': !__VLS_ctx.embedded }) },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
if (!__VLS_ctx.embedded) {
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
    /** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
    SmartToyOutlined;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
    const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
    // @ts-ignore
    [embedded, embedded,];
    var __VLS_3;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "ai-form" },
});
/** @type {__VLS_StyleScopedClasses['ai-form']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
(__VLS_ctx.modelValue.maxConcurrency);
let __VLS_11;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.maxConcurrency),
    min: (1),
    max: (100),
    step: (1),
    tooltip: (true),
}));
const __VLS_13 = __VLS_12({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.maxConcurrency),
    min: (1),
    max: (100),
    step: (1),
    tooltip: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_12));
let __VLS_16;
const __VLS_17 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.update({ maxConcurrency: v })) });
var __VLS_14;
var __VLS_15;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
let __VLS_18;
/** @ts-ignore @type {typeof __VLS_components.NInputNumber} */
NInputNumber;
// @ts-ignore
const __VLS_19 = __VLS_asFunctionalComponent1(__VLS_18, new __VLS_18({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.port),
    min: (1024),
    max: (65535),
    ...{ style: {} },
}));
const __VLS_20 = __VLS_19({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.port),
    min: (1024),
    max: (65535),
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_19));
let __VLS_23;
const __VLS_24 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.update({ port: v ?? 51821 })) });
var __VLS_21;
var __VLS_22;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "prompt-wrapper" },
});
/** @type {__VLS_StyleScopedClasses['prompt-wrapper']} */ ;
let __VLS_25;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_26 = __VLS_asFunctionalComponent1(__VLS_25, new __VLS_25({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.systemPrompt),
    type: "textarea",
    rows: (3),
    placeholder: "指导 LLM 如何翻译",
}));
const __VLS_27 = __VLS_26({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.systemPrompt),
    type: "textarea",
    rows: (3),
    placeholder: "指导 LLM 如何翻译",
}, ...__VLS_functionalComponentArgsRest(__VLS_26));
let __VLS_30;
const __VLS_31 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.update({ systemPrompt: v })) });
var __VLS_28;
var __VLS_29;
let __VLS_32;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_33 = __VLS_asFunctionalComponent1(__VLS_32, new __VLS_32({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    ...{ class: "restore-btn" },
}));
const __VLS_34 = __VLS_33({
    ...{ 'onClick': {} },
    size: "tiny",
    quaternary: true,
    ...{ class: "restore-btn" },
}, ...__VLS_functionalComponentArgsRest(__VLS_33));
let __VLS_37;
const __VLS_38 = ({ click: {} },
    { onClick: (__VLS_ctx.handleRestorePrompt) });
/** @type {__VLS_StyleScopedClasses['restore-btn']} */ ;
const { default: __VLS_39 } = __VLS_35.slots;
{
    const { icon: __VLS_40 } = __VLS_35.slots;
    let __VLS_41;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_42 = __VLS_asFunctionalComponent1(__VLS_41, new __VLS_41({
        size: (14),
    }));
    const __VLS_43 = __VLS_42({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_42));
    const { default: __VLS_46 } = __VLS_44.slots;
    let __VLS_47;
    /** @ts-ignore @type {typeof __VLS_components.RestoreOutlined} */
    RestoreOutlined;
    // @ts-ignore
    const __VLS_48 = __VLS_asFunctionalComponent1(__VLS_47, new __VLS_47({}));
    const __VLS_49 = __VLS_48({}, ...__VLS_functionalComponentArgsRest(__VLS_48));
    // @ts-ignore
    [modelValue, modelValue, modelValue, modelValue, update, update, update, handleRestorePrompt,];
    var __VLS_44;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_35;
var __VLS_36;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
(__VLS_ctx.modelValue.temperature);
let __VLS_52;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_53 = __VLS_asFunctionalComponent1(__VLS_52, new __VLS_52({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.temperature),
    min: (0),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}));
const __VLS_54 = __VLS_53({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.temperature),
    min: (0),
    max: (2),
    step: (0.1),
    tooltip: (true),
    formatTooltip: ((v) => v.toFixed(1)),
}, ...__VLS_functionalComponentArgsRest(__VLS_53));
let __VLS_57;
const __VLS_58 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.update({ temperature: v })) });
var __VLS_55;
var __VLS_56;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "form-row" },
});
/** @type {__VLS_StyleScopedClasses['form-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
    ...{ class: "form-label" },
});
/** @type {__VLS_StyleScopedClasses['form-label']} */ ;
(__VLS_ctx.modelValue.contextSize);
let __VLS_59;
/** @ts-ignore @type {typeof __VLS_components.NSlider} */
NSlider;
// @ts-ignore
const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.contextSize),
    min: (0),
    max: (100),
    step: (1),
    tooltip: (true),
}));
const __VLS_61 = __VLS_60({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.modelValue.contextSize),
    min: (0),
    max: (100),
    step: (1),
    tooltip: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_60));
let __VLS_64;
const __VLS_65 = ({ 'update:value': {} },
    { 'onUpdate:value': ((v) => __VLS_ctx.update({ contextSize: v })) });
var __VLS_62;
var __VLS_63;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "form-hint" },
});
/** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "endpoints-section" },
});
/** @type {__VLS_StyleScopedClasses['endpoints-section']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "endpoints-header" },
});
/** @type {__VLS_StyleScopedClasses['endpoints-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "subsection-title" },
});
/** @type {__VLS_StyleScopedClasses['subsection-title']} */ ;
let __VLS_66;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_67 = __VLS_asFunctionalComponent1(__VLS_66, new __VLS_66({
    ...{ 'onClick': {} },
    size: "small",
}));
const __VLS_68 = __VLS_67({
    ...{ 'onClick': {} },
    size: "small",
}, ...__VLS_functionalComponentArgsRest(__VLS_67));
let __VLS_71;
const __VLS_72 = ({ click: {} },
    { onClick: (__VLS_ctx.addEndpoint) });
const { default: __VLS_73 } = __VLS_69.slots;
{
    const { icon: __VLS_74 } = __VLS_69.slots;
    let __VLS_75;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_76 = __VLS_asFunctionalComponent1(__VLS_75, new __VLS_75({}));
    const __VLS_77 = __VLS_76({}, ...__VLS_functionalComponentArgsRest(__VLS_76));
    const { default: __VLS_80 } = __VLS_78.slots;
    let __VLS_81;
    /** @ts-ignore @type {typeof __VLS_components.AddOutlined} */
    AddOutlined;
    // @ts-ignore
    const __VLS_82 = __VLS_asFunctionalComponent1(__VLS_81, new __VLS_81({}));
    const __VLS_83 = __VLS_82({}, ...__VLS_functionalComponentArgsRest(__VLS_82));
    // @ts-ignore
    [modelValue, modelValue, modelValue, modelValue, update, update, addEndpoint,];
    var __VLS_78;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_69;
var __VLS_70;
if (__VLS_ctx.modelValue.endpoints.length === 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "empty-endpoints" },
    });
    /** @type {__VLS_StyleScopedClasses['empty-endpoints']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({});
}
else {
    let __VLS_86;
    /** @ts-ignore @type {typeof __VLS_components.NCollapse | typeof __VLS_components.NCollapse} */
    NCollapse;
    // @ts-ignore
    const __VLS_87 = __VLS_asFunctionalComponent1(__VLS_86, new __VLS_86({}));
    const __VLS_88 = __VLS_87({}, ...__VLS_functionalComponentArgsRest(__VLS_87));
    const { default: __VLS_91 } = __VLS_89.slots;
    for (const [ep, index] of __VLS_vFor((__VLS_ctx.modelValue.endpoints))) {
        let __VLS_92;
        /** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
        NCollapseItem;
        // @ts-ignore
        const __VLS_93 = __VLS_asFunctionalComponent1(__VLS_92, new __VLS_92({
            key: (ep.id),
            name: (ep.id),
        }));
        const __VLS_94 = __VLS_93({
            key: (ep.id),
            name: (ep.id),
        }, ...__VLS_functionalComponentArgsRest(__VLS_93));
        const { default: __VLS_97 } = __VLS_95.slots;
        {
            const { header: __VLS_98 } = __VLS_95.slots;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "endpoint-header" },
            });
            /** @type {__VLS_StyleScopedClasses['endpoint-header']} */ ;
            let __VLS_99;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({
                ...{ 'onUpdate:value': {} },
                ...{ 'onClick': {} },
                value: (ep.enabled),
                size: "small",
            }));
            const __VLS_101 = __VLS_100({
                ...{ 'onUpdate:value': {} },
                ...{ 'onClick': {} },
                value: (ep.enabled),
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_100));
            let __VLS_104;
            const __VLS_105 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { enabled: v })) });
            const __VLS_106 = ({ click: {} },
                { onClick: () => { } });
            var __VLS_102;
            var __VLS_103;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "endpoint-name" },
            });
            /** @type {__VLS_StyleScopedClasses['endpoint-name']} */ ;
            (ep.name || `提供商 ${index + 1}`);
            let __VLS_107;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_108 = __VLS_asFunctionalComponent1(__VLS_107, new __VLS_107({
                size: "small",
                type: (ep.enabled ? 'success' : 'default'),
            }));
            const __VLS_109 = __VLS_108({
                size: "small",
                type: (ep.enabled ? 'success' : 'default'),
            }, ...__VLS_functionalComponentArgsRest(__VLS_108));
            const { default: __VLS_112 } = __VLS_110.slots;
            (__VLS_ctx.providerOptions.find(p => p.value === ep.provider)?.label || ep.provider);
            // @ts-ignore
            [modelValue, modelValue, updateEndpoint, providerOptions,];
            var __VLS_110;
            if (ep.enabled && ep.apiKey) {
                let __VLS_113;
                /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
                NTag;
                // @ts-ignore
                const __VLS_114 = __VLS_asFunctionalComponent1(__VLS_113, new __VLS_113({
                    size: "small",
                    type: "info",
                }));
                const __VLS_115 = __VLS_114({
                    size: "small",
                    type: "info",
                }, ...__VLS_functionalComponentArgsRest(__VLS_114));
                const { default: __VLS_118 } = __VLS_116.slots;
                (ep.priority);
                // @ts-ignore
                [];
                var __VLS_116;
            }
            // @ts-ignore
            [];
        }
        {
            const { 'header-extra': __VLS_119 } = __VLS_95.slots;
            let __VLS_120;
            /** @ts-ignore @type {typeof __VLS_components.NPopconfirm | typeof __VLS_components.NPopconfirm} */
            NPopconfirm;
            // @ts-ignore
            const __VLS_121 = __VLS_asFunctionalComponent1(__VLS_120, new __VLS_120({
                ...{ 'onPositiveClick': {} },
            }));
            const __VLS_122 = __VLS_121({
                ...{ 'onPositiveClick': {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_121));
            let __VLS_125;
            const __VLS_126 = ({ positiveClick: {} },
                { onPositiveClick: (...[$event]) => {
                        if (!!(__VLS_ctx.modelValue.endpoints.length === 0))
                            return;
                        __VLS_ctx.removeEndpoint(index);
                        // @ts-ignore
                        [removeEndpoint,];
                    } });
            const { default: __VLS_127 } = __VLS_123.slots;
            {
                const { trigger: __VLS_128 } = __VLS_123.slots;
                let __VLS_129;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_130 = __VLS_asFunctionalComponent1(__VLS_129, new __VLS_129({
                    ...{ 'onClick': {} },
                    size: "tiny",
                    quaternary: true,
                    type: "error",
                }));
                const __VLS_131 = __VLS_130({
                    ...{ 'onClick': {} },
                    size: "tiny",
                    quaternary: true,
                    type: "error",
                }, ...__VLS_functionalComponentArgsRest(__VLS_130));
                let __VLS_134;
                const __VLS_135 = ({ click: {} },
                    { onClick: () => { } });
                const { default: __VLS_136 } = __VLS_132.slots;
                {
                    const { icon: __VLS_137 } = __VLS_132.slots;
                    let __VLS_138;
                    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                    NIcon;
                    // @ts-ignore
                    const __VLS_139 = __VLS_asFunctionalComponent1(__VLS_138, new __VLS_138({}));
                    const __VLS_140 = __VLS_139({}, ...__VLS_functionalComponentArgsRest(__VLS_139));
                    const { default: __VLS_143 } = __VLS_141.slots;
                    let __VLS_144;
                    /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
                    DeleteOutlined;
                    // @ts-ignore
                    const __VLS_145 = __VLS_asFunctionalComponent1(__VLS_144, new __VLS_144({}));
                    const __VLS_146 = __VLS_145({}, ...__VLS_functionalComponentArgsRest(__VLS_145));
                    // @ts-ignore
                    [];
                    var __VLS_141;
                    // @ts-ignore
                    [];
                }
                // @ts-ignore
                [];
                var __VLS_132;
                var __VLS_133;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_123;
            var __VLS_124;
            // @ts-ignore
            [];
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "endpoint-form" },
        });
        /** @type {__VLS_StyleScopedClasses['endpoint-form']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        let __VLS_149;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_150 = __VLS_asFunctionalComponent1(__VLS_149, new __VLS_149({
            ...{ 'onUpdate:value': {} },
            value: (ep.name),
            placeholder: "提供商名称",
        }));
        const __VLS_151 = __VLS_150({
            ...{ 'onUpdate:value': {} },
            value: (ep.name),
            placeholder: "提供商名称",
        }, ...__VLS_functionalComponentArgsRest(__VLS_150));
        let __VLS_154;
        const __VLS_155 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { name: v })) });
        var __VLS_152;
        var __VLS_153;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        let __VLS_156;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_157 = __VLS_asFunctionalComponent1(__VLS_156, new __VLS_156({
            ...{ 'onUpdate:value': {} },
            value: (ep.provider),
            options: (__VLS_ctx.providerOptions),
        }));
        const __VLS_158 = __VLS_157({
            ...{ 'onUpdate:value': {} },
            value: (ep.provider),
            options: (__VLS_ctx.providerOptions),
        }, ...__VLS_functionalComponentArgsRest(__VLS_157));
        let __VLS_161;
        const __VLS_162 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { provider: v })) });
        var __VLS_159;
        var __VLS_160;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        let __VLS_163;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_164 = __VLS_asFunctionalComponent1(__VLS_163, new __VLS_163({
            ...{ 'onUpdate:value': {} },
            value: (ep.apiBaseUrl),
            placeholder: (__VLS_ctx.getBaseUrlPlaceholder(ep.provider)),
            clearable: true,
        }));
        const __VLS_165 = __VLS_164({
            ...{ 'onUpdate:value': {} },
            value: (ep.apiBaseUrl),
            placeholder: (__VLS_ctx.getBaseUrlPlaceholder(ep.provider)),
            clearable: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_164));
        let __VLS_168;
        const __VLS_169 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { apiBaseUrl: v })) });
        var __VLS_166;
        var __VLS_167;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "form-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        let __VLS_170;
        /** @ts-ignore @type {typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
            ...{ 'onUpdate:value': {} },
            value: (ep.apiKey),
            placeholder: "输入 API Key",
            type: "password",
            showPasswordOn: "click",
            clearable: true,
        }));
        const __VLS_172 = __VLS_171({
            ...{ 'onUpdate:value': {} },
            value: (ep.apiKey),
            placeholder: "输入 API Key",
            type: "password",
            showPasswordOn: "click",
            clearable: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_171));
        let __VLS_175;
        const __VLS_176 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { apiKey: v })) });
        var __VLS_173;
        var __VLS_174;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "model-row" },
        });
        /** @type {__VLS_StyleScopedClasses['model-row']} */ ;
        if (__VLS_ctx.modelOptions[ep.id]?.length) {
            let __VLS_177;
            /** @ts-ignore @type {typeof __VLS_components.NSelect} */
            NSelect;
            // @ts-ignore
            const __VLS_178 = __VLS_asFunctionalComponent1(__VLS_177, new __VLS_177({
                ...{ 'onUpdate:value': {} },
                value: (ep.modelName || undefined),
                options: (__VLS_ctx.modelOptions[ep.id]),
                filterable: true,
                clearable: true,
                tag: true,
                placeholder: (__VLS_ctx.getModelPlaceholder(ep.provider)),
            }));
            const __VLS_179 = __VLS_178({
                ...{ 'onUpdate:value': {} },
                value: (ep.modelName || undefined),
                options: (__VLS_ctx.modelOptions[ep.id]),
                filterable: true,
                clearable: true,
                tag: true,
                placeholder: (__VLS_ctx.getModelPlaceholder(ep.provider)),
            }, ...__VLS_functionalComponentArgsRest(__VLS_178));
            let __VLS_182;
            const __VLS_183 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { modelName: v })) });
            var __VLS_180;
            var __VLS_181;
        }
        else {
            let __VLS_184;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_185 = __VLS_asFunctionalComponent1(__VLS_184, new __VLS_184({
                ...{ 'onUpdate:value': {} },
                value: (ep.modelName),
                placeholder: (__VLS_ctx.getModelPlaceholder(ep.provider)),
                clearable: true,
            }));
            const __VLS_186 = __VLS_185({
                ...{ 'onUpdate:value': {} },
                value: (ep.modelName),
                placeholder: (__VLS_ctx.getModelPlaceholder(ep.provider)),
                clearable: true,
            }, ...__VLS_functionalComponentArgsRest(__VLS_185));
            let __VLS_189;
            const __VLS_190 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { modelName: v })) });
            var __VLS_187;
            var __VLS_188;
        }
        if (__VLS_ctx.supportsModelList[ep.provider]) {
            let __VLS_191;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_192 = __VLS_asFunctionalComponent1(__VLS_191, new __VLS_191({
                ...{ 'onClick': {} },
                size: "small",
                loading: (__VLS_ctx.fetchingModels[ep.id]),
            }));
            const __VLS_193 = __VLS_192({
                ...{ 'onClick': {} },
                size: "small",
                loading: (__VLS_ctx.fetchingModels[ep.id]),
            }, ...__VLS_functionalComponentArgsRest(__VLS_192));
            let __VLS_196;
            const __VLS_197 = ({ click: {} },
                { onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.modelValue.endpoints.length === 0))
                            return;
                        if (!(__VLS_ctx.supportsModelList[ep.provider]))
                            return;
                        __VLS_ctx.handleFetchModels(ep);
                        // @ts-ignore
                        [updateEndpoint, updateEndpoint, updateEndpoint, updateEndpoint, updateEndpoint, updateEndpoint, providerOptions, getBaseUrlPlaceholder, modelOptions, modelOptions, getModelPlaceholder, getModelPlaceholder, supportsModelList, fetchingModels, handleFetchModels,];
                    } });
            const { default: __VLS_198 } = __VLS_194.slots;
            {
                const { icon: __VLS_199 } = __VLS_194.slots;
                let __VLS_200;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_201 = __VLS_asFunctionalComponent1(__VLS_200, new __VLS_200({}));
                const __VLS_202 = __VLS_201({}, ...__VLS_functionalComponentArgsRest(__VLS_201));
                const { default: __VLS_205 } = __VLS_203.slots;
                let __VLS_206;
                /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
                SearchOutlined;
                // @ts-ignore
                const __VLS_207 = __VLS_asFunctionalComponent1(__VLS_206, new __VLS_206({}));
                const __VLS_208 = __VLS_207({}, ...__VLS_functionalComponentArgsRest(__VLS_207));
                // @ts-ignore
                [];
                var __VLS_203;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_194;
            var __VLS_195;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "form-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
        (__VLS_ctx.supportsModelList[ep.provider] ? '，点击搜索图标获取模型列表' : '');
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "form-label" },
        });
        /** @type {__VLS_StyleScopedClasses['form-label']} */ ;
        (ep.priority);
        let __VLS_211;
        /** @ts-ignore @type {typeof __VLS_components.NSlider} */
        NSlider;
        // @ts-ignore
        const __VLS_212 = __VLS_asFunctionalComponent1(__VLS_211, new __VLS_211({
            ...{ 'onUpdate:value': {} },
            value: (ep.priority),
            min: (1),
            max: (10),
            step: (1),
            marks: (__VLS_ctx.priorityMarks),
        }));
        const __VLS_213 = __VLS_212({
            ...{ 'onUpdate:value': {} },
            value: (ep.priority),
            min: (1),
            max: (10),
            step: (1),
            marks: (__VLS_ctx.priorityMarks),
        }, ...__VLS_functionalComponentArgsRest(__VLS_212));
        let __VLS_216;
        const __VLS_217 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.updateEndpoint(index, { priority: v })) });
        var __VLS_214;
        var __VLS_215;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "form-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['form-hint']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "form-row" },
        });
        /** @type {__VLS_StyleScopedClasses['form-row']} */ ;
        let __VLS_218;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_219 = __VLS_asFunctionalComponent1(__VLS_218, new __VLS_218({
            ...{ 'onClick': {} },
            size: "small",
            loading: (__VLS_ctx.testingEndpoint[ep.id]),
            ghost: true,
        }));
        const __VLS_220 = __VLS_219({
            ...{ 'onClick': {} },
            size: "small",
            loading: (__VLS_ctx.testingEndpoint[ep.id]),
            ghost: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_219));
        let __VLS_223;
        const __VLS_224 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.modelValue.endpoints.length === 0))
                        return;
                    __VLS_ctx.handleTestEndpoint(ep);
                    // @ts-ignore
                    [updateEndpoint, supportsModelList, priorityMarks, testingEndpoint, handleTestEndpoint,];
                } });
        const { default: __VLS_225 } = __VLS_221.slots;
        {
            const { icon: __VLS_226 } = __VLS_221.slots;
            let __VLS_227;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_228 = __VLS_asFunctionalComponent1(__VLS_227, new __VLS_227({}));
            const __VLS_229 = __VLS_228({}, ...__VLS_functionalComponentArgsRest(__VLS_228));
            const { default: __VLS_232 } = __VLS_230.slots;
            let __VLS_233;
            /** @ts-ignore @type {typeof __VLS_components.ScienceOutlined} */
            ScienceOutlined;
            // @ts-ignore
            const __VLS_234 = __VLS_asFunctionalComponent1(__VLS_233, new __VLS_233({}));
            const __VLS_235 = __VLS_234({}, ...__VLS_functionalComponentArgsRest(__VLS_234));
            // @ts-ignore
            [];
            var __VLS_230;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_221;
        var __VLS_222;
        // @ts-ignore
        [];
        var __VLS_95;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_89;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-footer" },
});
/** @type {__VLS_StyleScopedClasses['section-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
let __VLS_238;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_239 = __VLS_asFunctionalComponent1(__VLS_238, new __VLS_238({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.testing),
    ghost: true,
}));
const __VLS_240 = __VLS_239({
    ...{ 'onClick': {} },
    loading: (__VLS_ctx.testing),
    ghost: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_239));
let __VLS_243;
const __VLS_244 = ({ click: {} },
    { onClick: (__VLS_ctx.handleTestAll) });
const { default: __VLS_245 } = __VLS_241.slots;
{
    const { icon: __VLS_246 } = __VLS_241.slots;
    let __VLS_247;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_248 = __VLS_asFunctionalComponent1(__VLS_247, new __VLS_247({}));
    const __VLS_249 = __VLS_248({}, ...__VLS_functionalComponentArgsRest(__VLS_248));
    const { default: __VLS_252 } = __VLS_250.slots;
    let __VLS_253;
    /** @ts-ignore @type {typeof __VLS_components.ScienceOutlined} */
    ScienceOutlined;
    // @ts-ignore
    const __VLS_254 = __VLS_asFunctionalComponent1(__VLS_253, new __VLS_253({}));
    const __VLS_255 = __VLS_254({}, ...__VLS_functionalComponentArgsRest(__VLS_254));
    // @ts-ignore
    [testing, handleTestAll,];
    var __VLS_250;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_241;
var __VLS_242;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __defaults: __VLS_defaults,
    __typeProps: {},
});
export default {};
