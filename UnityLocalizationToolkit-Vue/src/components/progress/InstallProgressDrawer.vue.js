import { NDrawer, NDrawerContent, NProgress, NTimeline, NTimelineItem, NButton, NAlert, } from 'naive-ui';
import { computed, ref, onMounted, onBeforeUnmount } from 'vue';
import { useInstallStore } from '@/stores/install';
const installStore = useInstallStore();
const installSteps = [
    { key: 'DetectingGame', label: '检测游戏' },
    { key: 'InstallingBepInEx', label: '安装 BepInEx' },
    { key: 'InstallingXUnity', label: '安装 XUnity.AutoTranslator' },
    { key: 'InstallingTmpFont', label: '安装 TMP 字体' },
    { key: 'InstallingAiTranslation', label: '部署 AI 翻译引擎' },
    { key: 'GeneratingConfig', label: '生成配置' },
    { key: 'ApplyingConfig', label: '应用最佳配置' },
    { key: 'ExtractingAssets', label: '提取游戏资产' },
    { key: 'VerifyingHealth', label: '验证插件状态' },
];
const uninstallSteps = [
    { key: 'RemovingXUnity', label: '移除 XUnity.AutoTranslator' },
    { key: 'RemovingBepInEx', label: '移除 BepInEx' },
];
const isUninstalling = computed(() => installStore.operationType === 'uninstall');
const steps = computed(() => isUninstalling.value ? uninstallSteps : installSteps);
const stepOrder = computed(() => steps.value.map((s) => s.key));
function isStepSkipped(stepKey) {
    return installStore.skippedSteps.has(stepKey);
}
function getStepType(stepKey) {
    if (isStepSkipped(stepKey))
        return 'default';
    if (!installStore.status)
        return 'default';
    const current = installStore.status.step;
    if (current === 'Failed')
        return 'error';
    if (current === 'Complete')
        return 'success';
    const currentIndex = stepOrder.value.indexOf(current);
    const stepIndex = stepOrder.value.indexOf(stepKey);
    if (stepIndex < currentIndex)
        return 'success';
    if (stepIndex === currentIndex)
        return 'info';
    return 'default';
}
function getStepLabel(step) {
    return isStepSkipped(step.key) ? `${step.label}（已跳过）` : step.label;
}
const isComplete = computed(() => installStore.status?.step === 'Complete');
const isFailed = computed(() => installStore.status?.step === 'Failed');
const isRunning = computed(() => installStore.status && !isComplete.value && !isFailed.value && installStore.status.step !== 'Idle');
const title = computed(() => {
    if (isComplete.value)
        return isUninstalling.value ? '卸载完成' : '安装完成';
    if (isFailed.value)
        return '操作失败';
    if (isUninstalling.value)
        return '卸载中...';
    return '安装中...';
});
const drawerWidth = ref(420);
function updateDrawerWidth() {
    drawerWidth.value = window.innerWidth <= 480 ? window.innerWidth : 420;
}
onMounted(() => {
    updateDrawerWidth();
    window.addEventListener('resize', updateDrawerWidth);
});
onBeforeUnmount(() => {
    window.removeEventListener('resize', updateDrawerWidth);
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NDrawer | typeof __VLS_components.NDrawer} */
NDrawer;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    show: (__VLS_ctx.installStore.isDrawerOpen),
    width: (__VLS_ctx.drawerWidth),
    placement: "right",
}));
const __VLS_2 = __VLS_1({
    show: (__VLS_ctx.installStore.isDrawerOpen),
    width: (__VLS_ctx.drawerWidth),
    placement: "right",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
var __VLS_5 = {};
const { default: __VLS_6 } = __VLS_3.slots;
let __VLS_7;
/** @ts-ignore @type {typeof __VLS_components.NDrawerContent | typeof __VLS_components.NDrawerContent} */
NDrawerContent;
// @ts-ignore
const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
    ...{ 'onClose': {} },
    title: (__VLS_ctx.title),
    closable: true,
}));
const __VLS_9 = __VLS_8({
    ...{ 'onClose': {} },
    title: (__VLS_ctx.title),
    closable: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_8));
let __VLS_12;
const __VLS_13 = ({ close: {} },
    { onClose: (...[$event]) => {
            __VLS_ctx.installStore.closeDrawer();
            // @ts-ignore
            [installStore, installStore, drawerWidth, title,];
        } });
const { default: __VLS_14 } = __VLS_10.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "progress-content" },
});
/** @type {__VLS_StyleScopedClasses['progress-content']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "timeline-section" },
});
/** @type {__VLS_StyleScopedClasses['timeline-section']} */ ;
let __VLS_15;
/** @ts-ignore @type {typeof __VLS_components.NTimeline | typeof __VLS_components.NTimeline} */
NTimeline;
// @ts-ignore
const __VLS_16 = __VLS_asFunctionalComponent1(__VLS_15, new __VLS_15({}));
const __VLS_17 = __VLS_16({}, ...__VLS_functionalComponentArgsRest(__VLS_16));
const { default: __VLS_20 } = __VLS_18.slots;
for (const [step, index] of __VLS_vFor((__VLS_ctx.steps))) {
    let __VLS_21;
    /** @ts-ignore @type {typeof __VLS_components.NTimelineItem} */
    NTimelineItem;
    // @ts-ignore
    const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({
        key: (step.key),
        type: (__VLS_ctx.getStepType(step.key)),
        title: (__VLS_ctx.getStepLabel(step)),
        ...{ class: ({ 'step-skipped': __VLS_ctx.isStepSkipped(step.key) }) },
    }));
    const __VLS_23 = __VLS_22({
        key: (step.key),
        type: (__VLS_ctx.getStepType(step.key)),
        title: (__VLS_ctx.getStepLabel(step)),
        ...{ class: ({ 'step-skipped': __VLS_ctx.isStepSkipped(step.key) }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_22));
    /** @type {__VLS_StyleScopedClasses['step-skipped']} */ ;
    // @ts-ignore
    [steps, getStepType, getStepLabel, isStepSkipped,];
}
// @ts-ignore
[];
var __VLS_18;
if (__VLS_ctx.installStore.status) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "progress-section" },
    });
    /** @type {__VLS_StyleScopedClasses['progress-section']} */ ;
    let __VLS_26;
    /** @ts-ignore @type {typeof __VLS_components.NProgress} */
    NProgress;
    // @ts-ignore
    const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({
        type: "line",
        percentage: (__VLS_ctx.installStore.status.progressPercent),
        status: (__VLS_ctx.isFailed ? 'error' : __VLS_ctx.isComplete ? 'success' : 'default'),
        indicatorPlacement: ('inside'),
        height: (20),
        borderRadius: (10),
    }));
    const __VLS_28 = __VLS_27({
        type: "line",
        percentage: (__VLS_ctx.installStore.status.progressPercent),
        status: (__VLS_ctx.isFailed ? 'error' : __VLS_ctx.isComplete ? 'success' : 'default'),
        indicatorPlacement: ('inside'),
        height: (20),
        borderRadius: (10),
    }, ...__VLS_functionalComponentArgsRest(__VLS_27));
}
if (__VLS_ctx.installStore.status?.message) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-message" },
    });
    /** @type {__VLS_StyleScopedClasses['status-message']} */ ;
    (__VLS_ctx.installStore.status.message);
}
if (__VLS_ctx.isFailed && __VLS_ctx.installStore.status?.error) {
    let __VLS_31;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_32 = __VLS_asFunctionalComponent1(__VLS_31, new __VLS_31({
        type: "error",
        ...{ style: {} },
    }));
    const __VLS_33 = __VLS_32({
        type: "error",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_32));
    const { default: __VLS_36 } = __VLS_34.slots;
    (__VLS_ctx.installStore.status.error);
    // @ts-ignore
    [installStore, installStore, installStore, installStore, installStore, installStore, isFailed, isFailed, isComplete,];
    var __VLS_34;
}
if (__VLS_ctx.isComplete) {
    let __VLS_37;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_38 = __VLS_asFunctionalComponent1(__VLS_37, new __VLS_37({
        type: "success",
        ...{ style: {} },
    }));
    const __VLS_39 = __VLS_38({
        type: "success",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_38));
    const { default: __VLS_42 } = __VLS_40.slots;
    (__VLS_ctx.isUninstalling ? '已成功卸载插件。' : '插件已成功安装！启动游戏即可使用。');
    // @ts-ignore
    [isComplete, isUninstalling,];
    var __VLS_40;
}
{
    const { footer: __VLS_43 } = __VLS_10.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "drawer-footer" },
    });
    /** @type {__VLS_StyleScopedClasses['drawer-footer']} */ ;
    if (__VLS_ctx.isRunning) {
        let __VLS_44;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({
            ...{ 'onClick': {} },
            tertiary: true,
            type: "warning",
        }));
        const __VLS_46 = __VLS_45({
            ...{ 'onClick': {} },
            tertiary: true,
            type: "warning",
        }, ...__VLS_functionalComponentArgsRest(__VLS_45));
        let __VLS_49;
        const __VLS_50 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.isRunning))
                        return;
                    __VLS_ctx.installStore.cancel();
                    // @ts-ignore
                    [installStore, isRunning,];
                } });
        const { default: __VLS_51 } = __VLS_47.slots;
        // @ts-ignore
        [];
        var __VLS_47;
        var __VLS_48;
    }
    if (__VLS_ctx.isComplete || __VLS_ctx.isFailed) {
        let __VLS_52;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_53 = __VLS_asFunctionalComponent1(__VLS_52, new __VLS_52({
            ...{ 'onClick': {} },
            type: "primary",
        }));
        const __VLS_54 = __VLS_53({
            ...{ 'onClick': {} },
            type: "primary",
        }, ...__VLS_functionalComponentArgsRest(__VLS_53));
        let __VLS_57;
        const __VLS_58 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!(__VLS_ctx.isComplete || __VLS_ctx.isFailed))
                        return;
                    __VLS_ctx.installStore.closeDrawer();
                    // @ts-ignore
                    [installStore, isFailed, isComplete,];
                } });
        const { default: __VLS_59 } = __VLS_55.slots;
        // @ts-ignore
        [];
        var __VLS_55;
        var __VLS_56;
    }
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_10;
var __VLS_11;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
