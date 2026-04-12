import { ref, computed, onMounted, watch } from 'vue';
import { NIcon, NButton, NAlert, NSpin, useMessage } from 'naive-ui';
import { CheckCircleOutlined, ErrorOutlineOutlined, WarningAmberOutlined, HelpOutlineOutlined, PlayArrowFilled, MonitorHeartOutlined, } from '@vicons/material';
import { pluginHealthApi } from '@/api/games';
const props = defineProps();
const report = ref(null);
const loading = ref(false);
const verifying = ref(false);
const error = ref(null);
const message = useMessage();
function problemItemOrder(item) {
    switch (item.id) {
        case 'toolboxAiState':
            return 0;
        case 'logErrors':
            return 1;
        default:
            return 2;
    }
}
// Only show problematic items (non-Healthy)
const problemItems = computed(() => (report.value?.checks ?? [])
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => item.status !== 'Healthy')
    .sort((a, b) => {
    const orderDiff = problemItemOrder(a.item) - problemItemOrder(b.item);
    return orderDiff !== 0 ? orderDiff : a.index - b.index;
})
    .map(({ item }) => item));
const allHealthy = computed(() => report.value !== null && report.value.overall === 'Healthy');
async function loadPassiveCheck() {
    loading.value = true;
    try {
        report.value = await pluginHealthApi.check(props.gameId);
    }
    catch {
        // Silent fail for passive check — user can use verify button
    }
    finally {
        loading.value = false;
    }
}
async function verifyInstallation() {
    verifying.value = true;
    error.value = null;
    try {
        report.value = await pluginHealthApi.verify(props.gameId);
        if (report.value.overall === 'Healthy') {
            message.success('验证通过，所有检查项均正常');
        }
        else {
            message.warning('验证完成，发现问题');
        }
    }
    catch {
        error.value = '验证安装失败，请确认游戏可执行文件可以正常启动';
        message.error('验证安装失败');
    }
    finally {
        verifying.value = false;
    }
}
function statusIcon(status) {
    switch (status) {
        case 'Healthy': return CheckCircleOutlined;
        case 'Warning': return WarningAmberOutlined;
        case 'Error': return ErrorOutlineOutlined;
        default: return HelpOutlineOutlined;
    }
}
function statusClass(status) {
    switch (status) {
        case 'Healthy': return 'status-healthy';
        case 'Warning': return 'status-warning';
        case 'Error': return 'status-error';
        default: return 'status-unknown';
    }
}
onMounted(() => {
    if (props.initialReport) {
        report.value = props.initialReport;
    }
    else {
        loadPassiveCheck();
    }
});
watch(() => props.initialReport, (newReport) => {
    if (newReport) {
        report.value = newReport;
    }
    else {
        loadPassiveCheck();
    }
});
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['overall-status']} */ ;
/** @type {__VLS_StyleScopedClasses['overall-status']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['detail-entry']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['status-unknown']} */ ;
/** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['status-error']} */ ;
/** @type {__VLS_StyleScopedClasses['check-detail']} */ ;
/** @type {__VLS_StyleScopedClasses['check-item']} */ ;
/** @type {__VLS_StyleScopedClasses['status-warning']} */ ;
/** @type {__VLS_StyleScopedClasses['check-detail']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card health-card" },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['health-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-header" },
});
/** @type {__VLS_StyleScopedClasses['section-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "section-title" },
});
/** @type {__VLS_StyleScopedClasses['section-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "section-icon health" },
});
/** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['health']} */ ;
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
/** @ts-ignore @type {typeof __VLS_components.MonitorHeartOutlined} */
MonitorHeartOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "header-actions" },
});
/** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
let __VLS_11;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.verifying),
    disabled: (__VLS_ctx.loading),
}));
const __VLS_13 = __VLS_12({
    ...{ 'onClick': {} },
    size: "small",
    loading: (__VLS_ctx.verifying),
    disabled: (__VLS_ctx.loading),
}, ...__VLS_functionalComponentArgsRest(__VLS_12));
let __VLS_16;
const __VLS_17 = ({ click: {} },
    { onClick: (__VLS_ctx.verifyInstallation) });
const { default: __VLS_18 } = __VLS_14.slots;
{
    const { icon: __VLS_19 } = __VLS_14.slots;
    let __VLS_20;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_21 = __VLS_asFunctionalComponent1(__VLS_20, new __VLS_20({
        size: (14),
    }));
    const __VLS_22 = __VLS_21({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_21));
    const { default: __VLS_25 } = __VLS_23.slots;
    let __VLS_26;
    /** @ts-ignore @type {typeof __VLS_components.PlayArrowFilled} */
    PlayArrowFilled;
    // @ts-ignore
    const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({}));
    const __VLS_28 = __VLS_27({}, ...__VLS_functionalComponentArgsRest(__VLS_27));
    // @ts-ignore
    [verifying, loading, verifyInstallation,];
    var __VLS_23;
    // @ts-ignore
    [];
}
(__VLS_ctx.verifying ? '正在验证...' : '启动验证');
// @ts-ignore
[verifying,];
var __VLS_14;
var __VLS_15;
if (__VLS_ctx.loading && !__VLS_ctx.report) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "loading-state" },
    });
    /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
    let __VLS_31;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_32 = __VLS_asFunctionalComponent1(__VLS_31, new __VLS_31({
        size: "small",
    }));
    const __VLS_33 = __VLS_32({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_32));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
}
else if (__VLS_ctx.report) {
    let __VLS_36;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_37 = __VLS_asFunctionalComponent1(__VLS_36, new __VLS_36({
        type: "info",
        bordered: (false),
        ...{ class: "card-alert reference-hint" },
    }));
    const __VLS_38 = __VLS_37({
        type: "info",
        bordered: (false),
        ...{ class: "card-alert reference-hint" },
    }, ...__VLS_functionalComponentArgsRest(__VLS_37));
    /** @type {__VLS_StyleScopedClasses['card-alert']} */ ;
    /** @type {__VLS_StyleScopedClasses['reference-hint']} */ ;
    const { default: __VLS_41 } = __VLS_39.slots;
    // @ts-ignore
    [loading, report, report,];
    var __VLS_39;
    if (__VLS_ctx.error) {
        let __VLS_42;
        /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
        NAlert;
        // @ts-ignore
        const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
            ...{ 'onClose': {} },
            type: "error",
            closable: true,
            ...{ class: "card-alert" },
        }));
        const __VLS_44 = __VLS_43({
            ...{ 'onClose': {} },
            type: "error",
            closable: true,
            ...{ class: "card-alert" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_43));
        let __VLS_47;
        const __VLS_48 = ({ close: {} },
            { onClose: (...[$event]) => {
                    if (!!(__VLS_ctx.loading && !__VLS_ctx.report))
                        return;
                    if (!(__VLS_ctx.report))
                        return;
                    if (!(__VLS_ctx.error))
                        return;
                    __VLS_ctx.error = null;
                    // @ts-ignore
                    [error, error,];
                } });
        /** @type {__VLS_StyleScopedClasses['card-alert']} */ ;
        const { default: __VLS_49 } = __VLS_45.slots;
        (__VLS_ctx.error);
        // @ts-ignore
        [error,];
        var __VLS_45;
        var __VLS_46;
    }
    if (__VLS_ctx.verifying) {
        let __VLS_50;
        /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
        NAlert;
        // @ts-ignore
        const __VLS_51 = __VLS_asFunctionalComponent1(__VLS_50, new __VLS_50({
            type: "info",
            bordered: (false),
            ...{ class: "card-alert" },
        }));
        const __VLS_52 = __VLS_51({
            type: "info",
            bordered: (false),
            ...{ class: "card-alert" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_51));
        /** @type {__VLS_StyleScopedClasses['card-alert']} */ ;
        const { default: __VLS_55 } = __VLS_53.slots;
        // @ts-ignore
        [verifying,];
        var __VLS_53;
    }
    if (__VLS_ctx.allHealthy && !__VLS_ctx.verifying) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "overall-status status-healthy" },
        });
        /** @type {__VLS_StyleScopedClasses['overall-status']} */ ;
        /** @type {__VLS_StyleScopedClasses['status-healthy']} */ ;
        let __VLS_56;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_57 = __VLS_asFunctionalComponent1(__VLS_56, new __VLS_56({
            size: (18),
        }));
        const __VLS_58 = __VLS_57({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_57));
        const { default: __VLS_61 } = __VLS_59.slots;
        let __VLS_62;
        /** @ts-ignore @type {typeof __VLS_components.CheckCircleOutlined} */
        CheckCircleOutlined;
        // @ts-ignore
        const __VLS_63 = __VLS_asFunctionalComponent1(__VLS_62, new __VLS_62({}));
        const __VLS_64 = __VLS_63({}, ...__VLS_functionalComponentArgsRest(__VLS_63));
        // @ts-ignore
        [verifying, allHealthy,];
        var __VLS_59;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    }
    else if (__VLS_ctx.report.gameNeverRun && __VLS_ctx.problemItems.length === 0 && !__VLS_ctx.verifying) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "overall-status status-unknown" },
        });
        /** @type {__VLS_StyleScopedClasses['overall-status']} */ ;
        /** @type {__VLS_StyleScopedClasses['status-unknown']} */ ;
        let __VLS_67;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_68 = __VLS_asFunctionalComponent1(__VLS_67, new __VLS_67({
            size: (18),
        }));
        const __VLS_69 = __VLS_68({
            size: (18),
        }, ...__VLS_functionalComponentArgsRest(__VLS_68));
        const { default: __VLS_72 } = __VLS_70.slots;
        let __VLS_73;
        /** @ts-ignore @type {typeof __VLS_components.HelpOutlineOutlined} */
        HelpOutlineOutlined;
        // @ts-ignore
        const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({}));
        const __VLS_75 = __VLS_74({}, ...__VLS_functionalComponentArgsRest(__VLS_74));
        // @ts-ignore
        [verifying, report, problemItems,];
        var __VLS_70;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    }
    if (__VLS_ctx.problemItems.length > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "check-group" },
        });
        /** @type {__VLS_StyleScopedClasses['check-group']} */ ;
        for (const [item] of __VLS_vFor((__VLS_ctx.problemItems))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (item.id),
                ...{ class: "check-item" },
                ...{ class: ([__VLS_ctx.statusClass(item.status), { 'has-details': item.details?.length }]) },
            });
            /** @type {__VLS_StyleScopedClasses['check-item']} */ ;
            /** @type {__VLS_StyleScopedClasses['has-details']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "check-item-main" },
            });
            /** @type {__VLS_StyleScopedClasses['check-item-main']} */ ;
            let __VLS_78;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_79 = __VLS_asFunctionalComponent1(__VLS_78, new __VLS_78({
                size: (16),
                ...{ class: "check-icon" },
            }));
            const __VLS_80 = __VLS_79({
                size: (16),
                ...{ class: "check-icon" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_79));
            /** @type {__VLS_StyleScopedClasses['check-icon']} */ ;
            const { default: __VLS_83 } = __VLS_81.slots;
            const __VLS_84 = (__VLS_ctx.statusIcon(item.status));
            // @ts-ignore
            const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({}));
            const __VLS_86 = __VLS_85({}, ...__VLS_functionalComponentArgsRest(__VLS_85));
            // @ts-ignore
            [problemItems, problemItems, statusClass, statusIcon,];
            var __VLS_81;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "check-label" },
            });
            /** @type {__VLS_StyleScopedClasses['check-label']} */ ;
            (item.label);
            if (item.detail) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "check-detail" },
                });
                /** @type {__VLS_StyleScopedClasses['check-detail']} */ ;
                (item.detail);
            }
            if (item.details?.length) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.ul, __VLS_intrinsics.ul)({
                    ...{ class: "check-detail-list" },
                });
                /** @type {__VLS_StyleScopedClasses['check-detail-list']} */ ;
                for (const [d, i] of __VLS_vFor((item.details))) {
                    __VLS_asFunctionalElement1(__VLS_intrinsics.li, __VLS_intrinsics.li)({
                        key: (i),
                        ...{ class: "detail-entry" },
                    });
                    /** @type {__VLS_StyleScopedClasses['detail-entry']} */ ;
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "detail-category" },
                    });
                    /** @type {__VLS_StyleScopedClasses['detail-category']} */ ;
                    (d.category);
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "detail-excerpt" },
                    });
                    /** @type {__VLS_StyleScopedClasses['detail-excerpt']} */ ;
                    (d.excerpt);
                    if (d.suggestion) {
                        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                            ...{ class: "detail-suggestion" },
                        });
                        /** @type {__VLS_StyleScopedClasses['detail-suggestion']} */ ;
                        (d.suggestion);
                    }
                    // @ts-ignore
                    [];
                }
            }
            // @ts-ignore
            [];
        }
    }
}
else if (__VLS_ctx.error) {
    let __VLS_89;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_90 = __VLS_asFunctionalComponent1(__VLS_89, new __VLS_89({
        type: "error",
    }));
    const __VLS_91 = __VLS_90({
        type: "error",
    }, ...__VLS_functionalComponentArgsRest(__VLS_90));
    const { default: __VLS_94 } = __VLS_92.slots;
    (__VLS_ctx.error);
    // @ts-ignore
    [error, error,];
    var __VLS_92;
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
