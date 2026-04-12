import { ref, computed, watch } from 'vue';
import { NIcon } from 'naive-ui';
import { PlayArrowRound, PhotoCameraOutlined, GamepadFilled } from '@vicons/material';
import { gamesApi } from '@/api/games';
import { useGamesStore } from '@/stores/games';
const props = withDefaults(defineProps(), {
    showLabel: true,
});
const emit = defineEmits();
const coverLoaded = ref(false);
const coverError = ref(false);
const noCover = computed(() => props.game.hasCover === false);
const coverUrl = computed(() => noCover.value ? '' : `${gamesApi.getCoverUrl(props.game.id)}?t=${props.game.updatedAt}`);
const iconUrl = computed(() => `${gamesApi.getIconUrl(props.game.id)}?t=${props.game.updatedAt}`);
// Reset loading state when cover URL changes (e.g. after cover update, rename)
watch(coverUrl, () => {
    coverLoaded.value = false;
    coverError.value = false;
});
function onCoverLoad() {
    coverLoaded.value = true;
    coverError.value = false;
}
function onCoverError() {
    coverError.value = true;
}
function getStatusInfo(state) {
    switch (state) {
        case 'FullyInstalled':
            return { label: '已安装', cssClass: 'status-success' };
        case 'BepInExOnly':
            return { label: 'BepInEx', cssClass: 'status-warning' };
        case 'PartiallyInstalled':
            return { label: '部分安装', cssClass: 'status-warning' };
        default:
            return { label: '未安装', cssClass: 'status-default' };
    }
}
// Generate a deterministic gradient from game name
function getPlaceholderGradient(name) {
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
        hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    const hue1 = Math.abs(hash % 360);
    const hue2 = (hue1 + 40) % 360;
    return `linear-gradient(135deg, hsl(${hue1}, 35%, 18%) 0%, hsl(${hue2}, 45%, 12%) 100%)`;
}
const gamesStore = useGamesStore();
function handlePlay(e) {
    e.stopPropagation();
    gamesStore.launchGame(props.game.id);
}
function handleEditCover(e) {
    e.stopPropagation();
    emit('editCover', props.game);
}
function handleContextMenu(e) {
    e.preventDefault();
    e.stopPropagation();
    emit('contextMenu', e, props.game);
}
const statusInfo = computed(() => getStatusInfo(props.game.installState));
const hasExe = computed(() => !!(props.game.executableName || props.game.detectedInfo?.detectedExecutable));
const __VLS_defaults = {
    showLabel: true,
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
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['cover-img']} */ ;
/** @type {__VLS_StyleScopedClasses['fallback-exe-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['fallback-exe-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['fallback-gamepad']} */ ;
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['card-overlay']} */ ;
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['play-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['play-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['play-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
/** @type {__VLS_StyleScopedClasses['edit-cover-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['edit-cover-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['play-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['card-info']} */ ;
/** @type {__VLS_StyleScopedClasses['card-name']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.emit('navigate', __VLS_ctx.game.id);
            // @ts-ignore
            [emit, game,];
        } },
    ...{ onContextmenu: (__VLS_ctx.handleContextMenu) },
    ...{ class: "game-card" },
    ...{ style: ({ animationDelay: `${__VLS_ctx.index * 0.03}s` }) },
});
/** @type {__VLS_StyleScopedClasses['game-card']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "card-cover" },
});
/** @type {__VLS_StyleScopedClasses['card-cover']} */ ;
if (!__VLS_ctx.noCover && !__VLS_ctx.coverLoaded && !__VLS_ctx.coverError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cover-shimmer" },
    });
    /** @type {__VLS_StyleScopedClasses['cover-shimmer']} */ ;
}
if (!__VLS_ctx.noCover) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
        ...{ onLoad: (__VLS_ctx.onCoverLoad) },
        ...{ onError: (__VLS_ctx.onCoverError) },
        src: (__VLS_ctx.coverUrl),
        alt: (__VLS_ctx.game.name),
        ...{ class: "cover-img" },
        ...{ class: ({ 'cover-img-visible': __VLS_ctx.coverLoaded && !__VLS_ctx.coverError }) },
        loading: "lazy",
    });
    /** @type {__VLS_StyleScopedClasses['cover-img']} */ ;
    /** @type {__VLS_StyleScopedClasses['cover-img-visible']} */ ;
}
if (__VLS_ctx.noCover || __VLS_ctx.coverError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cover-fallback" },
        ...{ style: ({ background: __VLS_ctx.getPlaceholderGradient(__VLS_ctx.game.name) }) },
    });
    /** @type {__VLS_StyleScopedClasses['cover-fallback']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "fallback-icon-wrapper" },
    });
    /** @type {__VLS_StyleScopedClasses['fallback-icon-wrapper']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
        ...{ onError: (...[$event]) => {
                if (!(__VLS_ctx.noCover || __VLS_ctx.coverError))
                    return;
                $event.target.style.display = 'none';
                // @ts-ignore
                [game, game, handleContextMenu, index, noCover, noCover, noCover, coverLoaded, coverLoaded, coverError, coverError, coverError, onCoverLoad, onCoverError, coverUrl, getPlaceholderGradient,];
            } },
        src: (__VLS_ctx.iconUrl),
        alt: (__VLS_ctx.game.name),
        ...{ class: "fallback-exe-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['fallback-exe-icon']} */ ;
    let __VLS_0;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
        size: (48),
        color: "var(--text-3)",
        ...{ class: "fallback-gamepad" },
    }));
    const __VLS_2 = __VLS_1({
        size: (48),
        color: "var(--text-3)",
        ...{ class: "fallback-gamepad" },
    }, ...__VLS_functionalComponentArgsRest(__VLS_1));
    /** @type {__VLS_StyleScopedClasses['fallback-gamepad']} */ ;
    const { default: __VLS_5 } = __VLS_3.slots;
    let __VLS_6;
    /** @ts-ignore @type {typeof __VLS_components.GamepadFilled} */
    GamepadFilled;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
    const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
    // @ts-ignore
    [game, iconUrl,];
    var __VLS_3;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "fallback-name" },
    });
    /** @type {__VLS_StyleScopedClasses['fallback-name']} */ ;
    (__VLS_ctx.game.name);
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "card-overlay" },
});
/** @type {__VLS_StyleScopedClasses['card-overlay']} */ ;
if (__VLS_ctx.hasExe) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.handlePlay) },
        ...{ class: "play-btn" },
        title: "启动游戏",
    });
    /** @type {__VLS_StyleScopedClasses['play-btn']} */ ;
    let __VLS_11;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
        size: (28),
        color: "#fff",
    }));
    const __VLS_13 = __VLS_12({
        size: (28),
        color: "#fff",
    }, ...__VLS_functionalComponentArgsRest(__VLS_12));
    const { default: __VLS_16 } = __VLS_14.slots;
    let __VLS_17;
    /** @ts-ignore @type {typeof __VLS_components.PlayArrowRound} */
    PlayArrowRound;
    // @ts-ignore
    const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({}));
    const __VLS_19 = __VLS_18({}, ...__VLS_functionalComponentArgsRest(__VLS_18));
    // @ts-ignore
    [game, hasExe, handlePlay,];
    var __VLS_14;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (__VLS_ctx.handleEditCover) },
    ...{ class: "edit-cover-btn" },
    title: "更换封面",
});
/** @type {__VLS_StyleScopedClasses['edit-cover-btn']} */ ;
let __VLS_22;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    size: (14),
    color: "#fff",
}));
const __VLS_24 = __VLS_23({
    size: (14),
    color: "#fff",
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
const { default: __VLS_27 } = __VLS_25.slots;
let __VLS_28;
/** @ts-ignore @type {typeof __VLS_components.PhotoCameraOutlined} */
PhotoCameraOutlined;
// @ts-ignore
const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({}));
const __VLS_30 = __VLS_29({}, ...__VLS_functionalComponentArgsRest(__VLS_29));
// @ts-ignore
[handleEditCover,];
var __VLS_25;
if (__VLS_ctx.game.isUnityGame) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-badge" },
        ...{ class: (__VLS_ctx.statusInfo.cssClass) },
    });
    /** @type {__VLS_StyleScopedClasses['status-badge']} */ ;
    (__VLS_ctx.statusInfo.label);
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "status-badge status-non-unity" },
    });
    /** @type {__VLS_StyleScopedClasses['status-badge']} */ ;
    /** @type {__VLS_StyleScopedClasses['status-non-unity']} */ ;
}
if (__VLS_ctx.showLabel) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "card-info" },
    });
    /** @type {__VLS_StyleScopedClasses['card-info']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
        ...{ class: "card-name" },
        title: (__VLS_ctx.game.name),
    });
    /** @type {__VLS_StyleScopedClasses['card-name']} */ ;
    (__VLS_ctx.game.name);
    if (__VLS_ctx.game.isUnityGame) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "workspace-strip" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-strip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "workspace-pill workspace-pill-xunity" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-pill']} */ ;
        /** @type {__VLS_StyleScopedClasses['workspace-pill-xunity']} */ ;
        (__VLS_ctx.game.xUnityStatus.state);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "workspace-pill workspace-pill-manual" },
        });
        /** @type {__VLS_StyleScopedClasses['workspace-pill']} */ ;
        /** @type {__VLS_StyleScopedClasses['workspace-pill-manual']} */ ;
        (__VLS_ctx.game.manualTranslationStatus.overrideCount);
    }
}
// @ts-ignore
[game, game, game, game, game, game, statusInfo, statusInfo, showLabel,];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __defaults: __VLS_defaults,
    __typeProps: {},
});
export default {};
