import { computed } from 'vue';
import { NButton, NEmpty, NIcon, NInput, NSelect, NTag } from 'naive-ui';
import { AddOutlined, DeleteOutlined } from '@vicons/material';
const props = defineProps();
const emit = defineEmits();
const SECTION_ORDER = ['base', 'custom', 'dynamic'];
const SECTION_META = {
    base: {
        label: '基础规则',
        hint: '再次执行预翻译时会按当前系统规则重新生成。',
        persistent: false,
    },
    custom: {
        label: '自定义规则',
        hint: '再次执行预翻译时会保留这部分内容。',
        persistent: true,
    },
    dynamic: {
        label: '动态规则',
        hint: '再次执行预翻译时会按最新分析结果重新生成。',
        persistent: false,
    },
};
const kindOptions = [
    { label: 'sr', value: 'sr' },
    { label: 'r', value: 'r' },
];
const normalizedSearchKeyword = computed(() => props.searchKeyword?.trim().toLowerCase() ?? '');
const hasSearchKeyword = computed(() => normalizedSearchKeyword.value.length > 0);
const sections = computed(() => ({
    base: props.rules.filter(rule => rule.section === 'base'),
    custom: props.rules.filter(rule => rule.section === 'custom'),
    dynamic: props.rules.filter(rule => rule.section === 'dynamic'),
}));
const filteredSections = computed(() => ({
    base: sections.value.base.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
    custom: sections.value.custom.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
    dynamic: sections.value.dynamic.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
}));
const hasMatchedRules = computed(() => SECTION_ORDER.some(section => filteredSections.value[section].length > 0));
function updateRules(nextRules) {
    emit('update:rules', nextRules);
}
function matchesRule(rule, keyword) {
    if (!keyword)
        return true;
    return rule.pattern.toLowerCase().includes(keyword)
        || rule.replacement.toLowerCase().includes(keyword)
        || rule.kind.toLowerCase().includes(keyword);
}
function addCustomRule() {
    updateRules([
        ...props.rules,
        {
            id: `custom-${Date.now()}-${props.rules.length + 1}`,
            section: 'custom',
            kind: 'r',
            pattern: '',
            replacement: '',
        },
    ]);
}
function findRuleIndex(ruleId) {
    return props.rules.findIndex(rule => rule.id === ruleId);
}
function removeRule(ruleId) {
    const allRules = [...props.rules];
    const targetIndex = findRuleIndex(ruleId);
    if (targetIndex >= 0) {
        allRules.splice(targetIndex, 1);
        updateRules(allRules);
    }
}
function updateRule(ruleId, patch) {
    const allRules = [...props.rules];
    const targetIndex = findRuleIndex(ruleId);
    if (targetIndex < 0)
        return;
    const current = allRules[targetIndex];
    allRules[targetIndex] = {
        id: patch.id ?? current.id,
        section: patch.section ?? current.section,
        kind: patch.kind ?? current.kind,
        pattern: patch.pattern ?? current.pattern,
        replacement: patch.replacement ?? current.replacement,
    };
    updateRules(allRules);
}
function updateKind(ruleId, value) {
    updateRule(ruleId, { kind: value });
}
function getSectionEmptyDescription() {
    if (hasSearchKeyword.value) {
        return '当前区块没有匹配的规则';
    }
    return '当前区块暂无规则';
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
/** @type {__VLS_StyleScopedClasses['regex-section']} */ ;
/** @type {__VLS_StyleScopedClasses['regex-section']} */ ;
/** @type {__VLS_StyleScopedClasses['regex-rule-row']} */ ;
/** @type {__VLS_StyleScopedClasses['regex-kind']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "regex-toolbar" },
});
/** @type {__VLS_StyleScopedClasses['regex-toolbar']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    ...{ 'onClick': {} },
    size: "small",
    type: "primary",
}));
const __VLS_2 = __VLS_1({
    ...{ 'onClick': {} },
    size: "small",
    type: "primary",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
let __VLS_5;
const __VLS_6 = ({ click: {} },
    { onClick: (__VLS_ctx.addCustomRule) });
const { default: __VLS_7 } = __VLS_3.slots;
{
    const { icon: __VLS_8 } = __VLS_3.slots;
    let __VLS_9;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_10 = __VLS_asFunctionalComponent1(__VLS_9, new __VLS_9({
        size: (16),
    }));
    const __VLS_11 = __VLS_10({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_10));
    const { default: __VLS_14 } = __VLS_12.slots;
    let __VLS_15;
    /** @ts-ignore @type {typeof __VLS_components.AddOutlined} */
    AddOutlined;
    // @ts-ignore
    const __VLS_16 = __VLS_asFunctionalComponent1(__VLS_15, new __VLS_15({}));
    const __VLS_17 = __VLS_16({}, ...__VLS_functionalComponentArgsRest(__VLS_16));
    // @ts-ignore
    [addCustomRule,];
    var __VLS_12;
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_3;
var __VLS_4;
if (__VLS_ctx.hasSearchKeyword && !__VLS_ctx.hasMatchedRules) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "regex-empty" },
    });
    /** @type {__VLS_StyleScopedClasses['regex-empty']} */ ;
    let __VLS_20;
    /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
    NEmpty;
    // @ts-ignore
    const __VLS_21 = __VLS_asFunctionalComponent1(__VLS_20, new __VLS_20({
        description: "没有匹配的正则规则",
    }));
    const __VLS_22 = __VLS_21({
        description: "没有匹配的正则规则",
    }, ...__VLS_functionalComponentArgsRest(__VLS_21));
}
else {
    for (const [section] of __VLS_vFor((__VLS_ctx.SECTION_ORDER))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (section),
            ...{ class: "regex-section" },
        });
        /** @type {__VLS_StyleScopedClasses['regex-section']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "regex-section-header" },
        });
        /** @type {__VLS_StyleScopedClasses['regex-section-header']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "regex-section-title" },
        });
        /** @type {__VLS_StyleScopedClasses['regex-section-title']} */ ;
        (__VLS_ctx.SECTION_META[section].label);
        let __VLS_25;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_26 = __VLS_asFunctionalComponent1(__VLS_25, new __VLS_25({
            size: "small",
            bordered: (false),
            type: (__VLS_ctx.SECTION_META[section].persistent ? 'success' : 'warning'),
        }));
        const __VLS_27 = __VLS_26({
            size: "small",
            bordered: (false),
            type: (__VLS_ctx.SECTION_META[section].persistent ? 'success' : 'warning'),
        }, ...__VLS_functionalComponentArgsRest(__VLS_26));
        const { default: __VLS_30 } = __VLS_28.slots;
        (__VLS_ctx.hasSearchKeyword ? `${__VLS_ctx.filteredSections[section].length} / ${__VLS_ctx.sections[section].length}` : __VLS_ctx.sections[section].length);
        // @ts-ignore
        [hasSearchKeyword, hasSearchKeyword, hasMatchedRules, SECTION_ORDER, SECTION_META, SECTION_META, filteredSections, sections, sections,];
        var __VLS_28;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "regex-section-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['regex-section-hint']} */ ;
        (__VLS_ctx.SECTION_META[section].hint);
        if (__VLS_ctx.filteredSections[section].length === 0) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "regex-empty" },
            });
            /** @type {__VLS_StyleScopedClasses['regex-empty']} */ ;
            let __VLS_31;
            /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
            NEmpty;
            // @ts-ignore
            const __VLS_32 = __VLS_asFunctionalComponent1(__VLS_31, new __VLS_31({
                size: "small",
                description: (__VLS_ctx.getSectionEmptyDescription()),
            }));
            const __VLS_33 = __VLS_32({
                size: "small",
                description: (__VLS_ctx.getSectionEmptyDescription()),
            }, ...__VLS_functionalComponentArgsRest(__VLS_32));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "regex-rule-list" },
            });
            /** @type {__VLS_StyleScopedClasses['regex-rule-list']} */ ;
            for (const [rule] of __VLS_vFor((__VLS_ctx.filteredSections[section]))) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    key: (rule.id),
                    ...{ class: "regex-rule-row" },
                });
                /** @type {__VLS_StyleScopedClasses['regex-rule-row']} */ ;
                let __VLS_36;
                /** @ts-ignore @type {typeof __VLS_components.NSelect} */
                NSelect;
                // @ts-ignore
                const __VLS_37 = __VLS_asFunctionalComponent1(__VLS_36, new __VLS_36({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.kind),
                    options: (__VLS_ctx.kindOptions),
                    size: "small",
                    ...{ class: "regex-kind" },
                }));
                const __VLS_38 = __VLS_37({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.kind),
                    options: (__VLS_ctx.kindOptions),
                    size: "small",
                    ...{ class: "regex-kind" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_37));
                let __VLS_41;
                const __VLS_42 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.hasSearchKeyword && !__VLS_ctx.hasMatchedRules))
                                return;
                            if (!!(__VLS_ctx.filteredSections[section].length === 0))
                                return;
                            __VLS_ctx.updateKind(rule.id, $event);
                            // @ts-ignore
                            [SECTION_META, filteredSections, filteredSections, getSectionEmptyDescription, kindOptions, updateKind,];
                        } });
                /** @type {__VLS_StyleScopedClasses['regex-kind']} */ ;
                var __VLS_39;
                var __VLS_40;
                let __VLS_43;
                /** @ts-ignore @type {typeof __VLS_components.NInput} */
                NInput;
                // @ts-ignore
                const __VLS_44 = __VLS_asFunctionalComponent1(__VLS_43, new __VLS_43({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.pattern),
                    type: "textarea",
                    size: "small",
                    autosize: ({ minRows: 1, maxRows: 4 }),
                    placeholder: "正则表达式",
                    ...{ class: "regex-field" },
                }));
                const __VLS_45 = __VLS_44({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.pattern),
                    type: "textarea",
                    size: "small",
                    autosize: ({ minRows: 1, maxRows: 4 }),
                    placeholder: "正则表达式",
                    ...{ class: "regex-field" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_44));
                let __VLS_48;
                const __VLS_49 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.hasSearchKeyword && !__VLS_ctx.hasMatchedRules))
                                return;
                            if (!!(__VLS_ctx.filteredSections[section].length === 0))
                                return;
                            __VLS_ctx.updateRule(rule.id, { pattern: $event });
                            // @ts-ignore
                            [updateRule,];
                        } });
                /** @type {__VLS_StyleScopedClasses['regex-field']} */ ;
                var __VLS_46;
                var __VLS_47;
                let __VLS_50;
                /** @ts-ignore @type {typeof __VLS_components.NInput} */
                NInput;
                // @ts-ignore
                const __VLS_51 = __VLS_asFunctionalComponent1(__VLS_50, new __VLS_50({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.replacement),
                    type: "textarea",
                    size: "small",
                    autosize: ({ minRows: 1, maxRows: 4 }),
                    placeholder: "替换内容",
                    ...{ class: "regex-field" },
                }));
                const __VLS_52 = __VLS_51({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.replacement),
                    type: "textarea",
                    size: "small",
                    autosize: ({ minRows: 1, maxRows: 4 }),
                    placeholder: "替换内容",
                    ...{ class: "regex-field" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_51));
                let __VLS_55;
                const __VLS_56 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.hasSearchKeyword && !__VLS_ctx.hasMatchedRules))
                                return;
                            if (!!(__VLS_ctx.filteredSections[section].length === 0))
                                return;
                            __VLS_ctx.updateRule(rule.id, { replacement: $event });
                            // @ts-ignore
                            [updateRule,];
                        } });
                /** @type {__VLS_StyleScopedClasses['regex-field']} */ ;
                var __VLS_53;
                var __VLS_54;
                let __VLS_57;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({
                    ...{ 'onClick': {} },
                    size: "small",
                    quaternary: true,
                    type: "error",
                }));
                const __VLS_59 = __VLS_58({
                    ...{ 'onClick': {} },
                    size: "small",
                    quaternary: true,
                    type: "error",
                }, ...__VLS_functionalComponentArgsRest(__VLS_58));
                let __VLS_62;
                const __VLS_63 = ({ click: {} },
                    { onClick: (...[$event]) => {
                            if (!!(__VLS_ctx.hasSearchKeyword && !__VLS_ctx.hasMatchedRules))
                                return;
                            if (!!(__VLS_ctx.filteredSections[section].length === 0))
                                return;
                            __VLS_ctx.removeRule(rule.id);
                            // @ts-ignore
                            [removeRule,];
                        } });
                const { default: __VLS_64 } = __VLS_60.slots;
                {
                    const { icon: __VLS_65 } = __VLS_60.slots;
                    let __VLS_66;
                    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                    NIcon;
                    // @ts-ignore
                    const __VLS_67 = __VLS_asFunctionalComponent1(__VLS_66, new __VLS_66({
                        size: (16),
                    }));
                    const __VLS_68 = __VLS_67({
                        size: (16),
                    }, ...__VLS_functionalComponentArgsRest(__VLS_67));
                    const { default: __VLS_71 } = __VLS_69.slots;
                    let __VLS_72;
                    /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
                    DeleteOutlined;
                    // @ts-ignore
                    const __VLS_73 = __VLS_asFunctionalComponent1(__VLS_72, new __VLS_72({}));
                    const __VLS_74 = __VLS_73({}, ...__VLS_functionalComponentArgsRest(__VLS_73));
                    // @ts-ignore
                    [];
                    var __VLS_69;
                    // @ts-ignore
                    [];
                }
                // @ts-ignore
                [];
                var __VLS_60;
                var __VLS_61;
                // @ts-ignore
                [];
            }
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
