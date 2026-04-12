import { onMounted, ref, h, computed } from 'vue';
import { NButton, NIcon, NSelect, NButtonGroup, NDropdown, NModal, NInput, useMessage, useDialog } from 'naive-ui';
import { Add } from '@vicons/ionicons5';
import { GamepadFilled, GridViewRound, ViewListRound, PlayArrowRound, DriveFileRenameOutlineOutlined, PhotoCameraOutlined, WallpaperOutlined, ImageSearchOutlined, DeleteOutlineOutlined } from '@vicons/material';
import { useRouter } from 'vue-router';
import { useGamesStore } from '@/stores/games';
import { useAddGameFlow } from '@/composables/useAddGameFlow';
import GameCard from '@/components/library/GameCard.vue';
import LibraryCustomizer from '@/components/library/LibraryCustomizer.vue';
import { defineAsyncComponent } from 'vue';
const CoverPickerModal = defineAsyncComponent(() => import('@/components/library/CoverPickerModal.vue'));
const BackgroundPickerModal = defineAsyncComponent(() => import('@/components/library/BackgroundPickerModal.vue'));
const IconPickerModal = defineAsyncComponent(() => import('@/components/library/IconPickerModal.vue'));
export default {};
const __VLS_self = (await import('vue')).defineComponent({
    components: { CoverPickerModal, BackgroundPickerModal, IconPickerModal }
});
const __VLS_export = await (async () => {
    defineOptions({ name: 'LibraryView' });
    const gamesStore = useGamesStore();
    const router = useRouter();
    const message = useMessage();
    const dialog = useDialog();
    const { addGame, adding } = useAddGameFlow(message);
    const showCoverPicker = ref(false);
    const coverPickerGame = ref(null);
    const showBackgroundPicker = ref(false);
    const backgroundPickerGame = ref(null);
    const showIconPicker = ref(false);
    const iconPickerGame = ref(null);
    // Context menu state
    const showContextMenu = ref(false);
    const contextMenuX = ref(0);
    const contextMenuY = ref(0);
    const contextMenuGame = ref(null);
    const contextMenuOptions = [
        { label: '重命名', key: 'rename', icon: () => h(NIcon, { size: 16 }, { default: () => h(DriveFileRenameOutlineOutlined) }) },
        { label: '更换封面', key: 'cover', icon: () => h(NIcon, { size: 16 }, { default: () => h(PhotoCameraOutlined) }) },
        { label: '更换背景', key: 'background', icon: () => h(NIcon, { size: 16 }, { default: () => h(WallpaperOutlined) }) },
        { label: '更换图标', key: 'icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(ImageSearchOutlined) }) },
        { type: 'divider', key: 'd1' },
        { label: '从游戏库中删除', key: 'remove', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
    ];
    // Rename modal state
    const showRenameModal = ref(false);
    const renameValue = ref('');
    const renameGameId = ref('');
    const renameSaving = ref(false);
    onMounted(async () => {
        await gamesStore.loadPreferences();
        gamesStore.fetchGames();
    });
    async function handleAddGame() {
        await addGame();
    }
    function navigateToGame(id) {
        router.push(`/games/${id}`);
    }
    function setViewMode(mode) {
        gamesStore.setViewMode(mode);
        gamesStore.savePreferences();
    }
    function handleSortChange(value) {
        gamesStore.setSortBy(value);
        gamesStore.savePreferences();
    }
    function openCoverPicker(game) {
        coverPickerGame.value = game;
        showCoverPicker.value = true;
    }
    function handleCardContextMenu(e, game) {
        contextMenuGame.value = game;
        contextMenuX.value = e.clientX;
        contextMenuY.value = e.clientY;
        showContextMenu.value = true;
    }
    function handleContextMenuSelect(key) {
        showContextMenu.value = false;
        if (!contextMenuGame.value)
            return;
        const game = contextMenuGame.value;
        if (key === 'rename') {
            renameGameId.value = game.id;
            renameValue.value = game.name;
            showRenameModal.value = true;
        }
        else if (key === 'cover') {
            openCoverPicker(game);
        }
        else if (key === 'background') {
            backgroundPickerGame.value = game;
            showBackgroundPicker.value = true;
        }
        else if (key === 'icon') {
            iconPickerGame.value = game;
            showIconPicker.value = true;
        }
        else if (key === 'remove') {
            dialog.error({
                title: '移除游戏',
                content: `将从游戏库中移除「${game.name}」（不会删除游戏文件）。移除后该游戏的术语库、翻译缓存等数据将被清除，重新添加后无法恢复。确定吗？`,
                positiveText: '确认移除',
                negativeText: '取消',
                onPositiveClick: async () => {
                    try {
                        await gamesStore.removeGame(game.id);
                        message.success('已移除');
                    }
                    catch {
                        message.error('移除失败');
                    }
                },
            });
        }
    }
    async function handleRenameConfirm() {
        const name = renameValue.value.trim();
        if (!name || !renameGameId.value) {
            showRenameModal.value = false;
            return;
        }
        renameSaving.value = true;
        try {
            await gamesStore.renameGame(renameGameId.value, name);
            message.success('游戏名称已更新');
        }
        catch {
            message.error('重命名失败');
        }
        finally {
            renameSaving.value = false;
            showRenameModal.value = false;
        }
    }
    function getStatusInfo(state) {
        switch (state) {
            case 'FullyInstalled':
                return { label: '已安装', color: 'var(--success)', dotClass: 'dot-success' };
            case 'BepInExOnly':
                return { label: 'BepInEx', color: 'var(--warning)', dotClass: 'dot-warning' };
            case 'PartiallyInstalled':
                return { label: '部分安装', color: 'var(--warning)', dotClass: 'dot-warning' };
            default:
                return { label: '未安装', color: 'var(--text-3)', dotClass: 'dot-default' };
        }
    }
    async function handleLaunchFromList(e, id) {
        e.stopPropagation();
        await gamesStore.launchGame(id);
    }
    const sortOptions = [
        { label: '名称', value: 'name' },
        { label: '最近游玩', value: 'recent' },
        { label: '添加时间', value: 'added' },
    ];
    const cardSizeMap = { small: 120, medium: 160, large: 200, xlarge: 240 };
    const gapMap = { compact: 8, normal: 16, spacious: 24 };
    const gridStyle = computed(() => {
        const minWidth = cardSizeMap[gamesStore.cardSize] || 160;
        const gapPx = gapMap[gamesStore.gap] || 16;
        return {
            gridTemplateColumns: `repeat(auto-fill, minmax(${minWidth}px, 1fr))`,
            gap: `${gapPx}px`,
        };
    });
    const __VLS_ctx = {
        ...{},
        ...{},
    };
    const __VLS_componentsOption = { CoverPickerModal, BackgroundPickerModal, IconPickerModal };
    let __VLS_components;
    let __VLS_intrinsics;
    let __VLS_directives;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['icon-fallback']} */ ;
    /** @type {__VLS_StyleScopedClasses['icon-img']} */ ;
    /** @type {__VLS_StyleScopedClasses['icon-fallback']} */ ;
    /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-play-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-play-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-arrow']} */ ;
    /** @type {__VLS_StyleScopedClasses['library-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    /** @type {__VLS_StyleScopedClasses['library-title']} */ ;
    /** @type {__VLS_StyleScopedClasses['games-grid']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-tags']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-arrow']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-play-btn']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-status']} */ ;
    /** @type {__VLS_StyleScopedClasses['library-title']} */ ;
    /** @type {__VLS_StyleScopedClasses['games-grid']} */ ;
    /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-name']} */ ;
    /** @type {__VLS_StyleScopedClasses['row-path']} */ ;
    /** @type {__VLS_StyleScopedClasses['status-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "library-page" },
    });
    /** @type {__VLS_StyleScopedClasses['library-page']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "library-header" },
    });
    /** @type {__VLS_StyleScopedClasses['library-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-left" },
    });
    /** @type {__VLS_StyleScopedClasses['header-left']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
        ...{ class: "library-title" },
    });
    /** @type {__VLS_StyleScopedClasses['library-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "page-title-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['page-title-icon']} */ ;
    let __VLS_0;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
        size: (22),
    }));
    const __VLS_2 = __VLS_1({
        size: (22),
    }, ...__VLS_functionalComponentArgsRest(__VLS_1));
    const { default: __VLS_5 } = __VLS_3.slots;
    let __VLS_6;
    /** @ts-ignore @type {typeof __VLS_components.GamepadFilled} */
    GamepadFilled;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
    const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
    var __VLS_3;
    if (__VLS_ctx.gamesStore.games.length > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "game-count" },
        });
        /** @type {__VLS_StyleScopedClasses['game-count']} */ ;
        (__VLS_ctx.gamesStore.games.length);
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    let __VLS_11;
    /** @ts-ignore @type {typeof __VLS_components.NButtonGroup | typeof __VLS_components.NButtonGroup} */
    NButtonGroup;
    // @ts-ignore
    const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
        size: "small",
    }));
    const __VLS_13 = __VLS_12({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_12));
    const { default: __VLS_16 } = __VLS_14.slots;
    let __VLS_17;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_18 = __VLS_asFunctionalComponent1(__VLS_17, new __VLS_17({
        ...{ 'onClick': {} },
        type: (__VLS_ctx.gamesStore.viewMode === 'grid' ? 'primary' : 'default'),
        tertiary: (__VLS_ctx.gamesStore.viewMode !== 'grid'),
    }));
    const __VLS_19 = __VLS_18({
        ...{ 'onClick': {} },
        type: (__VLS_ctx.gamesStore.viewMode === 'grid' ? 'primary' : 'default'),
        tertiary: (__VLS_ctx.gamesStore.viewMode !== 'grid'),
    }, ...__VLS_functionalComponentArgsRest(__VLS_18));
    let __VLS_22;
    const __VLS_23 = ({ click: {} },
        { onClick: (...[$event]) => {
                __VLS_ctx.setViewMode('grid');
                // @ts-ignore
                [gamesStore, gamesStore, gamesStore, gamesStore, setViewMode,];
            } });
    const { default: __VLS_24 } = __VLS_20.slots;
    {
        const { icon: __VLS_25 } = __VLS_20.slots;
        let __VLS_26;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_27 = __VLS_asFunctionalComponent1(__VLS_26, new __VLS_26({}));
        const __VLS_28 = __VLS_27({}, ...__VLS_functionalComponentArgsRest(__VLS_27));
        const { default: __VLS_31 } = __VLS_29.slots;
        let __VLS_32;
        /** @ts-ignore @type {typeof __VLS_components.GridViewRound} */
        GridViewRound;
        // @ts-ignore
        const __VLS_33 = __VLS_asFunctionalComponent1(__VLS_32, new __VLS_32({}));
        const __VLS_34 = __VLS_33({}, ...__VLS_functionalComponentArgsRest(__VLS_33));
        // @ts-ignore
        [];
        var __VLS_29;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_20;
    var __VLS_21;
    let __VLS_37;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_38 = __VLS_asFunctionalComponent1(__VLS_37, new __VLS_37({
        ...{ 'onClick': {} },
        type: (__VLS_ctx.gamesStore.viewMode === 'list' ? 'primary' : 'default'),
        tertiary: (__VLS_ctx.gamesStore.viewMode !== 'list'),
    }));
    const __VLS_39 = __VLS_38({
        ...{ 'onClick': {} },
        type: (__VLS_ctx.gamesStore.viewMode === 'list' ? 'primary' : 'default'),
        tertiary: (__VLS_ctx.gamesStore.viewMode !== 'list'),
    }, ...__VLS_functionalComponentArgsRest(__VLS_38));
    let __VLS_42;
    const __VLS_43 = ({ click: {} },
        { onClick: (...[$event]) => {
                __VLS_ctx.setViewMode('list');
                // @ts-ignore
                [gamesStore, gamesStore, setViewMode,];
            } });
    const { default: __VLS_44 } = __VLS_40.slots;
    {
        const { icon: __VLS_45 } = __VLS_40.slots;
        let __VLS_46;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_47 = __VLS_asFunctionalComponent1(__VLS_46, new __VLS_46({}));
        const __VLS_48 = __VLS_47({}, ...__VLS_functionalComponentArgsRest(__VLS_47));
        const { default: __VLS_51 } = __VLS_49.slots;
        let __VLS_52;
        /** @ts-ignore @type {typeof __VLS_components.ViewListRound} */
        ViewListRound;
        // @ts-ignore
        const __VLS_53 = __VLS_asFunctionalComponent1(__VLS_52, new __VLS_52({}));
        const __VLS_54 = __VLS_53({}, ...__VLS_functionalComponentArgsRest(__VLS_53));
        // @ts-ignore
        [];
        var __VLS_49;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_40;
    var __VLS_41;
    // @ts-ignore
    [];
    var __VLS_14;
    let __VLS_57;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.gamesStore.sortBy),
        options: (__VLS_ctx.sortOptions),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_59 = __VLS_58({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.gamesStore.sortBy),
        options: (__VLS_ctx.sortOptions),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_58));
    let __VLS_62;
    const __VLS_63 = ({ 'update:value': {} },
        { 'onUpdate:value': (__VLS_ctx.handleSortChange) });
    var __VLS_60;
    var __VLS_61;
    const __VLS_64 = LibraryCustomizer;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({}));
    const __VLS_66 = __VLS_65({}, ...__VLS_functionalComponentArgsRest(__VLS_65));
    let __VLS_69;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_70 = __VLS_asFunctionalComponent1(__VLS_69, new __VLS_69({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.adding),
    }));
    const __VLS_71 = __VLS_70({
        ...{ 'onClick': {} },
        type: "primary",
        size: "small",
        loading: (__VLS_ctx.adding),
    }, ...__VLS_functionalComponentArgsRest(__VLS_70));
    let __VLS_74;
    const __VLS_75 = ({ click: {} },
        { onClick: (__VLS_ctx.handleAddGame) });
    const { default: __VLS_76 } = __VLS_72.slots;
    {
        const { icon: __VLS_77 } = __VLS_72.slots;
        let __VLS_78;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_79 = __VLS_asFunctionalComponent1(__VLS_78, new __VLS_78({}));
        const __VLS_80 = __VLS_79({}, ...__VLS_functionalComponentArgsRest(__VLS_79));
        const { default: __VLS_83 } = __VLS_81.slots;
        let __VLS_84;
        /** @ts-ignore @type {typeof __VLS_components.Add} */
        Add;
        // @ts-ignore
        const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({}));
        const __VLS_86 = __VLS_85({}, ...__VLS_functionalComponentArgsRest(__VLS_85));
        // @ts-ignore
        [gamesStore, sortOptions, handleSortChange, adding, handleAddGame,];
        var __VLS_81;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_72;
    var __VLS_73;
    if (__VLS_ctx.gamesStore.loading) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "loading-state" },
        });
        /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "loading-spinner" },
        });
        /** @type {__VLS_StyleScopedClasses['loading-spinner']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "loading-text" },
        });
        /** @type {__VLS_StyleScopedClasses['loading-text']} */ ;
    }
    else if (__VLS_ctx.gamesStore.games.length === 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "empty-state" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-state']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "empty-icon-wrapper" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-icon-wrapper']} */ ;
        let __VLS_89;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_90 = __VLS_asFunctionalComponent1(__VLS_89, new __VLS_89({
            size: (48),
            color: "var(--text-3)",
        }));
        const __VLS_91 = __VLS_90({
            size: (48),
            color: "var(--text-3)",
        }, ...__VLS_functionalComponentArgsRest(__VLS_90));
        const { default: __VLS_94 } = __VLS_92.slots;
        let __VLS_95;
        /** @ts-ignore @type {typeof __VLS_components.GamepadFilled} */
        GamepadFilled;
        // @ts-ignore
        const __VLS_96 = __VLS_asFunctionalComponent1(__VLS_95, new __VLS_95({}));
        const __VLS_97 = __VLS_96({}, ...__VLS_functionalComponentArgsRest(__VLS_96));
        // @ts-ignore
        [gamesStore, gamesStore,];
        var __VLS_92;
        __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
            ...{ class: "empty-title" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
            ...{ class: "empty-desc" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-desc']} */ ;
        let __VLS_100;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_101 = __VLS_asFunctionalComponent1(__VLS_100, new __VLS_100({
            ...{ 'onClick': {} },
            type: "primary",
            size: "large",
            loading: (__VLS_ctx.adding),
        }));
        const __VLS_102 = __VLS_101({
            ...{ 'onClick': {} },
            type: "primary",
            size: "large",
            loading: (__VLS_ctx.adding),
        }, ...__VLS_functionalComponentArgsRest(__VLS_101));
        let __VLS_105;
        const __VLS_106 = ({ click: {} },
            { onClick: (__VLS_ctx.handleAddGame) });
        const { default: __VLS_107 } = __VLS_103.slots;
        {
            const { icon: __VLS_108 } = __VLS_103.slots;
            let __VLS_109;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_110 = __VLS_asFunctionalComponent1(__VLS_109, new __VLS_109({}));
            const __VLS_111 = __VLS_110({}, ...__VLS_functionalComponentArgsRest(__VLS_110));
            const { default: __VLS_114 } = __VLS_112.slots;
            let __VLS_115;
            /** @ts-ignore @type {typeof __VLS_components.Add} */
            Add;
            // @ts-ignore
            const __VLS_116 = __VLS_asFunctionalComponent1(__VLS_115, new __VLS_115({}));
            const __VLS_117 = __VLS_116({}, ...__VLS_functionalComponentArgsRest(__VLS_116));
            // @ts-ignore
            [adding, handleAddGame,];
            var __VLS_112;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_103;
        var __VLS_104;
    }
    else if (__VLS_ctx.gamesStore.viewMode === 'grid') {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "games-grid" },
            ...{ style: (__VLS_ctx.gridStyle) },
        });
        /** @type {__VLS_StyleScopedClasses['games-grid']} */ ;
        for (const [game, index] of __VLS_vFor((__VLS_ctx.gamesStore.sortedGames))) {
            const __VLS_120 = GameCard;
            // @ts-ignore
            const __VLS_121 = __VLS_asFunctionalComponent1(__VLS_120, new __VLS_120({
                ...{ 'onNavigate': {} },
                ...{ 'onEditCover': {} },
                ...{ 'onContextMenu': {} },
                key: (game.id),
                game: (game),
                index: (index),
                showLabel: (__VLS_ctx.gamesStore.showLabels),
            }));
            const __VLS_122 = __VLS_121({
                ...{ 'onNavigate': {} },
                ...{ 'onEditCover': {} },
                ...{ 'onContextMenu': {} },
                key: (game.id),
                game: (game),
                index: (index),
                showLabel: (__VLS_ctx.gamesStore.showLabels),
            }, ...__VLS_functionalComponentArgsRest(__VLS_121));
            let __VLS_125;
            const __VLS_126 = ({ navigate: {} },
                { onNavigate: (__VLS_ctx.navigateToGame) });
            const __VLS_127 = ({ editCover: {} },
                { onEditCover: (__VLS_ctx.openCoverPicker) });
            const __VLS_128 = ({ contextMenu: {} },
                { onContextMenu: (__VLS_ctx.handleCardContextMenu) });
            var __VLS_123;
            var __VLS_124;
            // @ts-ignore
            [gamesStore, gamesStore, gamesStore, gridStyle, navigateToGame, openCoverPicker, handleCardContextMenu,];
        }
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "games-list" },
        });
        /** @type {__VLS_StyleScopedClasses['games-list']} */ ;
        for (const [game, index] of __VLS_vFor((__VLS_ctx.gamesStore.sortedGames))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.gamesStore.loading))
                            return;
                        if (!!(__VLS_ctx.gamesStore.games.length === 0))
                            return;
                        if (!!(__VLS_ctx.gamesStore.viewMode === 'grid'))
                            return;
                        __VLS_ctx.navigateToGame(game.id);
                        // @ts-ignore
                        [gamesStore, navigateToGame,];
                    } },
                ...{ onContextmenu: (...[$event]) => {
                        if (!!(__VLS_ctx.gamesStore.loading))
                            return;
                        if (!!(__VLS_ctx.gamesStore.games.length === 0))
                            return;
                        if (!!(__VLS_ctx.gamesStore.viewMode === 'grid'))
                            return;
                        __VLS_ctx.handleCardContextMenu($event, game);
                        // @ts-ignore
                        [handleCardContextMenu,];
                    } },
                key: (game.id),
                ...{ class: "game-row" },
                ...{ style: ({ animationDelay: `${index * 0.04}s` }) },
            });
            /** @type {__VLS_StyleScopedClasses['game-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "row-icon" },
            });
            /** @type {__VLS_StyleScopedClasses['row-icon']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
                ...{ onError: (...[$event]) => {
                        if (!!(__VLS_ctx.gamesStore.loading))
                            return;
                        if (!!(__VLS_ctx.gamesStore.games.length === 0))
                            return;
                        if (!!(__VLS_ctx.gamesStore.viewMode === 'grid'))
                            return;
                        $event.target.style.display = 'none';
                        $event.target.nextElementSibling?.classList.add('visible');
                        // @ts-ignore
                        [];
                    } },
                src: (`/api/games/${game.id}/icon?t=${game.updatedAt}`),
                alt: (game.name),
                ...{ class: "icon-img" },
            });
            /** @type {__VLS_StyleScopedClasses['icon-img']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "icon-fallback" },
            });
            /** @type {__VLS_StyleScopedClasses['icon-fallback']} */ ;
            let __VLS_129;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_130 = __VLS_asFunctionalComponent1(__VLS_129, new __VLS_129({
                size: (22),
                color: "var(--text-3)",
            }));
            const __VLS_131 = __VLS_130({
                size: (22),
                color: "var(--text-3)",
            }, ...__VLS_functionalComponentArgsRest(__VLS_130));
            const { default: __VLS_134 } = __VLS_132.slots;
            let __VLS_135;
            /** @ts-ignore @type {typeof __VLS_components.GamepadFilled} */
            GamepadFilled;
            // @ts-ignore
            const __VLS_136 = __VLS_asFunctionalComponent1(__VLS_135, new __VLS_135({}));
            const __VLS_137 = __VLS_136({}, ...__VLS_functionalComponentArgsRest(__VLS_136));
            // @ts-ignore
            [];
            var __VLS_132;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "row-info" },
            });
            /** @type {__VLS_StyleScopedClasses['row-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
                ...{ class: "row-name" },
            });
            /** @type {__VLS_StyleScopedClasses['row-name']} */ ;
            (game.name);
            __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
                ...{ class: "row-path" },
            });
            /** @type {__VLS_StyleScopedClasses['row-path']} */ ;
            (game.gamePath);
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "row-tags" },
            });
            /** @type {__VLS_StyleScopedClasses['row-tags']} */ ;
            if (!game.isUnityGame) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "info-pill non-unity" },
                });
                /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
                /** @type {__VLS_StyleScopedClasses['non-unity']} */ ;
            }
            else if (game.detectedInfo) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "info-pill" },
                });
                /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
                (game.detectedInfo.unityVersion);
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "info-pill" },
                });
                /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
                (game.detectedInfo.backend);
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "info-pill" },
                });
                /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
                (game.detectedInfo.architecture);
            }
            else {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "info-pill muted" },
                });
                /** @type {__VLS_StyleScopedClasses['info-pill']} */ ;
                /** @type {__VLS_StyleScopedClasses['muted']} */ ;
            }
            if (game.executableName || game.detectedInfo?.detectedExecutable) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
                    ...{ onClick: (...[$event]) => {
                            if (!!(__VLS_ctx.gamesStore.loading))
                                return;
                            if (!!(__VLS_ctx.gamesStore.games.length === 0))
                                return;
                            if (!!(__VLS_ctx.gamesStore.viewMode === 'grid'))
                                return;
                            if (!(game.executableName || game.detectedInfo?.detectedExecutable))
                                return;
                            __VLS_ctx.handleLaunchFromList($event, game.id);
                            // @ts-ignore
                            [handleLaunchFromList,];
                        } },
                    ...{ class: "row-play-btn" },
                    title: "启动游戏",
                });
                /** @type {__VLS_StyleScopedClasses['row-play-btn']} */ ;
                let __VLS_140;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_141 = __VLS_asFunctionalComponent1(__VLS_140, new __VLS_140({
                    size: (18),
                }));
                const __VLS_142 = __VLS_141({
                    size: (18),
                }, ...__VLS_functionalComponentArgsRest(__VLS_141));
                const { default: __VLS_145 } = __VLS_143.slots;
                let __VLS_146;
                /** @ts-ignore @type {typeof __VLS_components.PlayArrowRound} */
                PlayArrowRound;
                // @ts-ignore
                const __VLS_147 = __VLS_asFunctionalComponent1(__VLS_146, new __VLS_146({}));
                const __VLS_148 = __VLS_147({}, ...__VLS_functionalComponentArgsRest(__VLS_147));
                // @ts-ignore
                [];
                var __VLS_143;
            }
            if (game.isUnityGame) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "row-status" },
                });
                /** @type {__VLS_StyleScopedClasses['row-status']} */ ;
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "status-dot" },
                    ...{ class: (__VLS_ctx.getStatusInfo(game.installState).dotClass) },
                });
                /** @type {__VLS_StyleScopedClasses['status-dot']} */ ;
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "status-label" },
                    ...{ style: ({ color: __VLS_ctx.getStatusInfo(game.installState).color }) },
                });
                /** @type {__VLS_StyleScopedClasses['status-label']} */ ;
                (__VLS_ctx.getStatusInfo(game.installState).label);
            }
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "row-arrow" },
            });
            /** @type {__VLS_StyleScopedClasses['row-arrow']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
                width: "16",
                height: "16",
                viewBox: "0 0 16 16",
                fill: "none",
            });
            __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
                d: "M6 4L10 8L6 12",
                stroke: "currentColor",
                'stroke-width': "1.5",
                'stroke-linecap': "round",
                'stroke-linejoin': "round",
            });
            // @ts-ignore
            [getStatusInfo, getStatusInfo, getStatusInfo,];
        }
    }
    let __VLS_151;
    /** @ts-ignore @type {typeof __VLS_components.NDropdown} */
    NDropdown;
    // @ts-ignore
    const __VLS_152 = __VLS_asFunctionalComponent1(__VLS_151, new __VLS_151({
        ...{ 'onClickoutside': {} },
        ...{ 'onSelect': {} },
        trigger: "manual",
        show: (__VLS_ctx.showContextMenu),
        options: (__VLS_ctx.contextMenuOptions),
        x: (__VLS_ctx.contextMenuX),
        y: (__VLS_ctx.contextMenuY),
        placement: "bottom-start",
    }));
    const __VLS_153 = __VLS_152({
        ...{ 'onClickoutside': {} },
        ...{ 'onSelect': {} },
        trigger: "manual",
        show: (__VLS_ctx.showContextMenu),
        options: (__VLS_ctx.contextMenuOptions),
        x: (__VLS_ctx.contextMenuX),
        y: (__VLS_ctx.contextMenuY),
        placement: "bottom-start",
    }, ...__VLS_functionalComponentArgsRest(__VLS_152));
    let __VLS_156;
    const __VLS_157 = ({ clickoutside: {} },
        { onClickoutside: (...[$event]) => {
                __VLS_ctx.showContextMenu = false;
                // @ts-ignore
                [showContextMenu, showContextMenu, contextMenuOptions, contextMenuX, contextMenuY,];
            } });
    const __VLS_158 = ({ select: {} },
        { onSelect: (__VLS_ctx.handleContextMenuSelect) });
    var __VLS_154;
    var __VLS_155;
    let __VLS_159;
    /** @ts-ignore @type {typeof __VLS_components.NModal | typeof __VLS_components.NModal} */
    NModal;
    // @ts-ignore
    const __VLS_160 = __VLS_asFunctionalComponent1(__VLS_159, new __VLS_159({
        ...{ 'onPositiveClick': {} },
        ...{ 'onNegativeClick': {} },
        ...{ 'onClose': {} },
        ...{ 'onMaskClick': {} },
        show: (__VLS_ctx.showRenameModal),
        preset: "dialog",
        title: "重命名游戏",
        positiveText: "确定",
        negativeText: "取消",
        positiveButtonProps: ({ loading: __VLS_ctx.renameSaving }),
    }));
    const __VLS_161 = __VLS_160({
        ...{ 'onPositiveClick': {} },
        ...{ 'onNegativeClick': {} },
        ...{ 'onClose': {} },
        ...{ 'onMaskClick': {} },
        show: (__VLS_ctx.showRenameModal),
        preset: "dialog",
        title: "重命名游戏",
        positiveText: "确定",
        negativeText: "取消",
        positiveButtonProps: ({ loading: __VLS_ctx.renameSaving }),
    }, ...__VLS_functionalComponentArgsRest(__VLS_160));
    let __VLS_164;
    const __VLS_165 = ({ positiveClick: {} },
        { onPositiveClick: (__VLS_ctx.handleRenameConfirm) });
    const __VLS_166 = ({ negativeClick: {} },
        { onNegativeClick: (...[$event]) => {
                __VLS_ctx.showRenameModal = false;
                // @ts-ignore
                [handleContextMenuSelect, showRenameModal, showRenameModal, renameSaving, handleRenameConfirm,];
            } });
    const __VLS_167 = ({ close: {} },
        { onClose: (...[$event]) => {
                __VLS_ctx.showRenameModal = false;
                // @ts-ignore
                [showRenameModal,];
            } });
    const __VLS_168 = ({ maskClick: {} },
        { onMaskClick: (...[$event]) => {
                __VLS_ctx.showRenameModal = false;
                // @ts-ignore
                [showRenameModal,];
            } });
    const { default: __VLS_169 } = __VLS_162.slots;
    let __VLS_170;
    /** @ts-ignore @type {typeof __VLS_components.NInput} */
    NInput;
    // @ts-ignore
    const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.renameValue),
        placeholder: "输入新名称",
        autofocus: true,
    }));
    const __VLS_172 = __VLS_171({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.renameValue),
        placeholder: "输入新名称",
        autofocus: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_171));
    let __VLS_175;
    const __VLS_176 = ({ keyup: {} },
        { onKeyup: (__VLS_ctx.handleRenameConfirm) });
    var __VLS_173;
    var __VLS_174;
    // @ts-ignore
    [handleRenameConfirm, renameValue,];
    var __VLS_162;
    var __VLS_163;
    if (__VLS_ctx.showCoverPicker && __VLS_ctx.coverPickerGame) {
        let __VLS_177;
        /** @ts-ignore @type {typeof __VLS_components.CoverPickerModal} */
        CoverPickerModal;
        // @ts-ignore
        const __VLS_178 = __VLS_asFunctionalComponent1(__VLS_177, new __VLS_177({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showCoverPicker),
            game: (__VLS_ctx.coverPickerGame),
        }));
        const __VLS_179 = __VLS_178({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showCoverPicker),
            game: (__VLS_ctx.coverPickerGame),
        }, ...__VLS_functionalComponentArgsRest(__VLS_178));
        let __VLS_182;
        const __VLS_183 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!(__VLS_ctx.showCoverPicker && __VLS_ctx.coverPickerGame))
                        return;
                    __VLS_ctx.showCoverPicker = $event;
                    // @ts-ignore
                    [showCoverPicker, showCoverPicker, showCoverPicker, coverPickerGame, coverPickerGame,];
                } });
        const __VLS_184 = ({ saved: {} },
            { onSaved: (...[$event]) => {
                    if (!(__VLS_ctx.showCoverPicker && __VLS_ctx.coverPickerGame))
                        return;
                    __VLS_ctx.gamesStore.refreshGame(__VLS_ctx.coverPickerGame.id);
                    // @ts-ignore
                    [gamesStore, coverPickerGame,];
                } });
        var __VLS_180;
        var __VLS_181;
    }
    if (__VLS_ctx.showBackgroundPicker && __VLS_ctx.backgroundPickerGame) {
        let __VLS_185;
        /** @ts-ignore @type {typeof __VLS_components.BackgroundPickerModal} */
        BackgroundPickerModal;
        // @ts-ignore
        const __VLS_186 = __VLS_asFunctionalComponent1(__VLS_185, new __VLS_185({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showBackgroundPicker),
            game: (__VLS_ctx.backgroundPickerGame),
        }));
        const __VLS_187 = __VLS_186({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showBackgroundPicker),
            game: (__VLS_ctx.backgroundPickerGame),
        }, ...__VLS_functionalComponentArgsRest(__VLS_186));
        let __VLS_190;
        const __VLS_191 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!(__VLS_ctx.showBackgroundPicker && __VLS_ctx.backgroundPickerGame))
                        return;
                    __VLS_ctx.showBackgroundPicker = $event;
                    // @ts-ignore
                    [showBackgroundPicker, showBackgroundPicker, showBackgroundPicker, backgroundPickerGame, backgroundPickerGame,];
                } });
        const __VLS_192 = ({ saved: {} },
            { onSaved: (...[$event]) => {
                    if (!(__VLS_ctx.showBackgroundPicker && __VLS_ctx.backgroundPickerGame))
                        return;
                    __VLS_ctx.gamesStore.refreshGame(__VLS_ctx.backgroundPickerGame.id);
                    // @ts-ignore
                    [gamesStore, backgroundPickerGame,];
                } });
        var __VLS_188;
        var __VLS_189;
    }
    if (__VLS_ctx.showIconPicker && __VLS_ctx.iconPickerGame) {
        let __VLS_193;
        /** @ts-ignore @type {typeof __VLS_components.IconPickerModal} */
        IconPickerModal;
        // @ts-ignore
        const __VLS_194 = __VLS_asFunctionalComponent1(__VLS_193, new __VLS_193({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showIconPicker),
            game: (__VLS_ctx.iconPickerGame),
        }));
        const __VLS_195 = __VLS_194({
            ...{ 'onUpdate:show': {} },
            ...{ 'onSaved': {} },
            show: (__VLS_ctx.showIconPicker),
            game: (__VLS_ctx.iconPickerGame),
        }, ...__VLS_functionalComponentArgsRest(__VLS_194));
        let __VLS_198;
        const __VLS_199 = ({ 'update:show': {} },
            { 'onUpdate:show': (...[$event]) => {
                    if (!(__VLS_ctx.showIconPicker && __VLS_ctx.iconPickerGame))
                        return;
                    __VLS_ctx.showIconPicker = $event;
                    // @ts-ignore
                    [showIconPicker, showIconPicker, showIconPicker, iconPickerGame, iconPickerGame,];
                } });
        const __VLS_200 = ({ saved: {} },
            { onSaved: (...[$event]) => {
                    if (!(__VLS_ctx.showIconPicker && __VLS_ctx.iconPickerGame))
                        return;
                    __VLS_ctx.gamesStore.refreshGame(__VLS_ctx.iconPickerGame.id);
                    // @ts-ignore
                    [gamesStore, iconPickerGame,];
                } });
        var __VLS_196;
        var __VLS_197;
    }
    // @ts-ignore
    [];
    return (await import('vue')).defineComponent({});
})();
