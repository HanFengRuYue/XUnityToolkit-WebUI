import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue';
import { NModal, NInput, NIcon, NButton, NSpin, NSelect, NAlert, NEllipsis } from 'naive-ui';
import { FolderOutlined, InsertDriveFileOutlined, StorageOutlined, ChevronRightRound, ArrowUpwardRound, DesktopWindowsOutlined, FileDownloadOutlined, DescriptionOutlined, ImageOutlined, MusicNoteOutlined, VideocamOutlined, PushPinOutlined, } from '@vicons/material';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { filesystemApi } from '@/api/filesystem';
import { formatBytes } from '@/utils/format';
const { show, mode, options, confirm, cancel } = useFileExplorer();
// Template refs
const fileListRef = ref(null);
// Local state
const currentPath = ref('');
const entries = ref([]);
const drives = ref([]);
const quickAccessEntries = ref([]);
const loading = ref(false);
const error = ref(null);
const selectedFile = ref(null);
const activeFilterIndex = ref(0);
const pathInput = ref('');
const standardFolderIcons = {
    '桌面': DesktopWindowsOutlined,
    '下载': FileDownloadOutlined,
    '文档': DescriptionOutlined,
    '图片': ImageOutlined,
    '音乐': MusicNoteOutlined,
    '视频': VideocamOutlined,
};
function getQuickAccessIcon(entry) {
    if (entry.type === 'standard') {
        return standardFolderIcons[entry.name] ?? FolderOutlined;
    }
    return FolderOutlined;
}
const title = computed(() => {
    if (options.value.title)
        return options.value.title;
    return mode.value === 'folder' ? '选择文件夹' : '选择文件';
});
const breadcrumbs = computed(() => {
    if (!currentPath.value)
        return [];
    const parts = currentPath.value.split(/[\\/]/).filter(Boolean);
    const result = [];
    let accumulated = '';
    for (const part of parts) {
        accumulated = accumulated ? `${accumulated}\\${part}` : `${part}\\`;
        result.push({ label: part, path: accumulated });
    }
    return result;
});
const filterOptions = computed(() => {
    const filters = options.value.filters;
    if (!filters || filters.length === 0)
        return [];
    const opts = filters.map((f, i) => ({
        label: f.extensions.length > 0
            ? `${f.label} (${f.extensions.join(', ')})`
            : f.label,
        value: i,
    }));
    if (!filters.some((f) => f.extensions.length === 0)) {
        opts.push({ label: '所有文件', value: -1 });
    }
    return opts;
});
const filteredEntries = computed(() => {
    if (mode.value === 'folder') {
        return entries.value.filter((e) => e.isDirectory);
    }
    const filters = options.value.filters;
    if (!filters || filters.length === 0 || activeFilterIndex.value === -1) {
        return entries.value;
    }
    const activeFilter = filters[activeFilterIndex.value];
    if (!activeFilter || activeFilter.extensions.length === 0) {
        return entries.value;
    }
    const exts = new Set(activeFilter.extensions.map((e) => e.toLowerCase()));
    return entries.value.filter((e) => e.isDirectory || (e.extension && exts.has(e.extension)));
});
const parentPath = computed(() => {
    if (!currentPath.value)
        return null;
    const parent = currentPath.value.replace(/[\\/]$/, '');
    const lastSep = Math.max(parent.lastIndexOf('\\'), parent.lastIndexOf('/'));
    if (lastSep < 0)
        return null;
    return parent.substring(0, lastSep + 1);
});
const selectedPath = computed(() => {
    if (mode.value === 'folder')
        return currentPath.value;
    return selectedFile.value?.fullPath ?? '';
});
const canConfirm = computed(() => {
    if (mode.value === 'folder')
        return !!currentPath.value;
    return !!selectedFile.value;
});
const activeDrive = computed(() => {
    if (!currentPath.value)
        return null;
    return currentPath.value.split('\\')[0];
});
const activeQuickAccess = computed(() => {
    if (!currentPath.value)
        return null;
    const normalized = currentPath.value.replace(/[\\/]+$/, '').toLowerCase();
    return quickAccessEntries.value.find((e) => e.fullPath.replace(/[\\/]+$/, '').toLowerCase() === normalized) ?? null;
});
// Fetch drives and quick access on open
watch(show, async (visible) => {
    if (!visible) {
        currentPath.value = '';
        entries.value = [];
        selectedFile.value = null;
        error.value = null;
        pathInput.value = '';
        activeFilterIndex.value = 0;
        return;
    }
    // Fetch drives and quick access in parallel if not cached
    const promises = [];
    if (drives.value.length === 0) {
        promises.push(filesystemApi.getDrives().then((d) => { drives.value = d; }).catch(() => { drives.value = []; }));
    }
    if (quickAccessEntries.value.length === 0) {
        promises.push(filesystemApi.getQuickAccess().then((e) => { quickAccessEntries.value = e; }).catch(() => { quickAccessEntries.value = []; }));
    }
    await Promise.all(promises);
    // Navigate to initial path or first drive
    const initialPath = options.value.initialPath;
    if (initialPath) {
        await navigateTo(initialPath);
    }
    else {
        const firstDrive = drives.value[0];
        if (firstDrive)
            await navigateTo(firstDrive.rootPath);
    }
    const filters = options.value.filters;
    if (filters && filters.length > 0) {
        activeFilterIndex.value = 0;
    }
    else {
        activeFilterIndex.value = -1;
    }
});
async function navigateTo(path) {
    loading.value = true;
    error.value = null;
    selectedFile.value = null;
    try {
        const result = await filesystemApi.listDirectory(path);
        currentPath.value = result.currentPath;
        entries.value = result.entries;
        pathInput.value = result.currentPath;
    }
    catch (e) {
        error.value = e instanceof Error ? e.message : '无法访问该目录';
    }
    finally {
        loading.value = false;
    }
    await nextTick();
    if (fileListRef.value)
        fileListRef.value.scrollTop = 0;
}
function handleEntryClick(entry) {
    if (entry.isDirectory) {
        navigateTo(entry.fullPath);
    }
    else if (mode.value === 'file') {
        selectedFile.value = entry;
    }
}
function handleEntryDblClick(entry) {
    if (!entry.isDirectory && mode.value === 'file') {
        confirm(entry.fullPath);
    }
}
function handleDriveClick(drive) {
    navigateTo(drive.rootPath);
}
function handleQuickAccessClick(entry) {
    navigateTo(entry.fullPath);
}
function handleBreadcrumbClick(path) {
    navigateTo(path);
}
function handleGoUp() {
    if (parentPath.value) {
        navigateTo(parentPath.value);
    }
}
function handlePathSubmit() {
    const trimmed = pathInput.value.trim();
    if (trimmed) {
        navigateTo(trimmed);
    }
}
function handleConfirm() {
    if (!canConfirm.value)
        return;
    confirm(selectedPath.value);
}
function handleCancel() {
    cancel();
}
function formatDate(dateStr) {
    if (!dateStr)
        return '';
    const d = new Date(dateStr);
    return d.toLocaleDateString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
    });
}
// Resizable columns
const dateColWidth = ref(145);
const sizeColWidth = ref(80);
const dragging = ref(false);
let dragColumn = null;
let dragStartX = 0;
let dragStartWidth = 0;
function onResizeStart(e, column) {
    e.preventDefault();
    dragColumn = column;
    dragStartX = e.clientX;
    dragStartWidth = column === 'date' ? dateColWidth.value : sizeColWidth.value;
    dragging.value = true;
    document.addEventListener('mousemove', onResizeMove);
    document.addEventListener('mouseup', onResizeEnd);
}
function onResizeMove(e) {
    if (!dragColumn)
        return;
    // Drag left = increase width (columns are right-aligned)
    const delta = dragStartX - e.clientX;
    const newWidth = Math.max(60, Math.min(400, dragStartWidth + delta));
    if (dragColumn === 'date') {
        dateColWidth.value = newWidth;
    }
    else {
        sizeColWidth.value = newWidth;
    }
}
function onResizeEnd() {
    dragColumn = null;
    dragging.value = false;
    document.removeEventListener('mousemove', onResizeMove);
    document.removeEventListener('mouseup', onResizeEnd);
}
onBeforeUnmount(() => {
    document.removeEventListener('mousemove', onResizeMove);
    document.removeEventListener('mouseup', onResizeEnd);
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['sidebar-item']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-item']} */ ;
/** @type {__VLS_StyleScopedClasses['breadcrumb-item']} */ ;
/** @type {__VLS_StyleScopedClasses['breadcrumb-item']} */ ;
/** @type {__VLS_StyleScopedClasses['resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['resize-handle']} */ ;
/** @type {__VLS_StyleScopedClasses['is-resizing']} */ ;
/** @type {__VLS_StyleScopedClasses['list-state']} */ ;
/** @type {__VLS_StyleScopedClasses['file-row']} */ ;
/** @type {__VLS_StyleScopedClasses['file-row']} */ ;
/** @type {__VLS_StyleScopedClasses['entry-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['file-row']} */ ;
/** @type {__VLS_StyleScopedClasses['col-date']} */ ;
/** @type {__VLS_StyleScopedClasses['file-row']} */ ;
/** @type {__VLS_StyleScopedClasses['col-size']} */ ;
/** @type {__VLS_StyleScopedClasses['file-explorer-body']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-panel']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-title']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-divider']} */ ;
/** @type {__VLS_StyleScopedClasses['pin-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['sidebar-item']} */ ;
/** @type {__VLS_StyleScopedClasses['drive-label']} */ ;
/** @type {__VLS_StyleScopedClasses['col-date']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NModal | typeof __VLS_components.NModal} */
NModal;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    ...{ 'onUpdate:show': {} },
    show: (__VLS_ctx.show),
    preset: "card",
    title: (__VLS_ctx.title),
    ...{ style: {} },
    maskClosable: (true),
}));
const __VLS_2 = __VLS_1({
    ...{ 'onUpdate:show': {} },
    show: (__VLS_ctx.show),
    preset: "card",
    title: (__VLS_ctx.title),
    ...{ style: {} },
    maskClosable: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
let __VLS_5;
const __VLS_6 = ({ 'update:show': {} },
    { 'onUpdate:show': ((v) => { if (!v)
            __VLS_ctx.handleCancel(); }) });
var __VLS_7 = {};
const { default: __VLS_8 } = __VLS_3.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "file-explorer" },
    ...{ class: ({ 'is-resizing': __VLS_ctx.dragging }) },
    ...{ style: ({ '--date-col-w': __VLS_ctx.dateColWidth + 'px', '--size-col-w': __VLS_ctx.sizeColWidth + 'px' }) },
});
/** @type {__VLS_StyleScopedClasses['file-explorer']} */ ;
/** @type {__VLS_StyleScopedClasses['is-resizing']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "file-explorer-body" },
});
/** @type {__VLS_StyleScopedClasses['file-explorer-body']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-panel" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-panel']} */ ;
if (__VLS_ctx.quickAccessEntries.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "sidebar-title" },
    });
    /** @type {__VLS_StyleScopedClasses['sidebar-title']} */ ;
    for (const [entry] of __VLS_vFor((__VLS_ctx.quickAccessEntries))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!(__VLS_ctx.quickAccessEntries.length > 0))
                        return;
                    __VLS_ctx.handleQuickAccessClick(entry);
                    // @ts-ignore
                    [show, title, handleCancel, dragging, dateColWidth, sizeColWidth, quickAccessEntries, quickAccessEntries, handleQuickAccessClick,];
                } },
            key: (entry.fullPath),
            ...{ class: "sidebar-item" },
            ...{ class: ({ active: __VLS_ctx.activeQuickAccess?.fullPath === entry.fullPath }) },
        });
        /** @type {__VLS_StyleScopedClasses['sidebar-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        let __VLS_9;
        /** @ts-ignore @type {typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_10 = __VLS_asFunctionalComponent1(__VLS_9, new __VLS_9({
            component: (__VLS_ctx.getQuickAccessIcon(entry)),
            size: (16),
        }));
        const __VLS_11 = __VLS_10({
            component: (__VLS_ctx.getQuickAccessIcon(entry)),
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_10));
        let __VLS_14;
        /** @ts-ignore @type {typeof __VLS_components.NEllipsis | typeof __VLS_components.NEllipsis} */
        NEllipsis;
        // @ts-ignore
        const __VLS_15 = __VLS_asFunctionalComponent1(__VLS_14, new __VLS_14({
            ...{ class: "sidebar-item-name" },
            tooltip: ({ maxWidth: 300 }),
        }));
        const __VLS_16 = __VLS_15({
            ...{ class: "sidebar-item-name" },
            tooltip: ({ maxWidth: 300 }),
        }, ...__VLS_functionalComponentArgsRest(__VLS_15));
        /** @type {__VLS_StyleScopedClasses['sidebar-item-name']} */ ;
        const { default: __VLS_19 } = __VLS_17.slots;
        (entry.name);
        // @ts-ignore
        [activeQuickAccess, getQuickAccessIcon,];
        var __VLS_17;
        if (entry.type === 'pinned') {
            let __VLS_20;
            /** @ts-ignore @type {typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_21 = __VLS_asFunctionalComponent1(__VLS_20, new __VLS_20({
                component: (__VLS_ctx.PushPinOutlined),
                size: (12),
                ...{ class: "pin-icon" },
            }));
            const __VLS_22 = __VLS_21({
                component: (__VLS_ctx.PushPinOutlined),
                size: (12),
                ...{ class: "pin-icon" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_21));
            /** @type {__VLS_StyleScopedClasses['pin-icon']} */ ;
        }
        // @ts-ignore
        [PushPinOutlined,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "sidebar-divider" },
    });
    /** @type {__VLS_StyleScopedClasses['sidebar-divider']} */ ;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "sidebar-title" },
});
/** @type {__VLS_StyleScopedClasses['sidebar-title']} */ ;
for (const [drive] of __VLS_vFor((__VLS_ctx.drives))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.handleDriveClick(drive);
                // @ts-ignore
                [drives, handleDriveClick,];
            } },
        key: (drive.name),
        ...{ class: "sidebar-item" },
        ...{ class: ({ active: __VLS_ctx.activeDrive === drive.name && !__VLS_ctx.activeQuickAccess }) },
    });
    /** @type {__VLS_StyleScopedClasses['sidebar-item']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    let __VLS_25;
    /** @ts-ignore @type {typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_26 = __VLS_asFunctionalComponent1(__VLS_25, new __VLS_25({
        component: (__VLS_ctx.StorageOutlined),
        size: (16),
    }));
    const __VLS_27 = __VLS_26({
        component: (__VLS_ctx.StorageOutlined),
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_26));
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "drive-info" },
    });
    /** @type {__VLS_StyleScopedClasses['drive-info']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "drive-name" },
    });
    /** @type {__VLS_StyleScopedClasses['drive-name']} */ ;
    (drive.name);
    if (drive.label) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "drive-label" },
        });
        /** @type {__VLS_StyleScopedClasses['drive-label']} */ ;
        (drive.label);
    }
    // @ts-ignore
    [activeQuickAccess, activeDrive, StorageOutlined,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "content-area" },
});
/** @type {__VLS_StyleScopedClasses['content-area']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "path-bar" },
});
/** @type {__VLS_StyleScopedClasses['path-bar']} */ ;
let __VLS_30;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_31 = __VLS_asFunctionalComponent1(__VLS_30, new __VLS_30({
    ...{ 'onClick': {} },
    quaternary: true,
    size: "small",
    disabled: (!__VLS_ctx.parentPath),
    ...{ class: "up-btn" },
}));
const __VLS_32 = __VLS_31({
    ...{ 'onClick': {} },
    quaternary: true,
    size: "small",
    disabled: (!__VLS_ctx.parentPath),
    ...{ class: "up-btn" },
}, ...__VLS_functionalComponentArgsRest(__VLS_31));
let __VLS_35;
const __VLS_36 = ({ click: {} },
    { onClick: (__VLS_ctx.handleGoUp) });
/** @type {__VLS_StyleScopedClasses['up-btn']} */ ;
const { default: __VLS_37 } = __VLS_33.slots;
{
    const { icon: __VLS_38 } = __VLS_33.slots;
    let __VLS_39;
    /** @ts-ignore @type {typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({
        component: (__VLS_ctx.ArrowUpwardRound),
    }));
    const __VLS_41 = __VLS_40({
        component: (__VLS_ctx.ArrowUpwardRound),
    }, ...__VLS_functionalComponentArgsRest(__VLS_40));
    // @ts-ignore
    [parentPath, handleGoUp, ArrowUpwardRound,];
}
// @ts-ignore
[];
var __VLS_33;
var __VLS_34;
let __VLS_44;
/** @ts-ignore @type {typeof __VLS_components.NInput} */
NInput;
// @ts-ignore
const __VLS_45 = __VLS_asFunctionalComponent1(__VLS_44, new __VLS_44({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.pathInput),
    size: "small",
    placeholder: "输入路径...",
}));
const __VLS_46 = __VLS_45({
    ...{ 'onKeyup': {} },
    value: (__VLS_ctx.pathInput),
    size: "small",
    placeholder: "输入路径...",
}, ...__VLS_functionalComponentArgsRest(__VLS_45));
let __VLS_49;
const __VLS_50 = ({ keyup: {} },
    { onKeyup: (__VLS_ctx.handlePathSubmit) });
var __VLS_47;
var __VLS_48;
if (__VLS_ctx.breadcrumbs.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "breadcrumbs" },
    });
    /** @type {__VLS_StyleScopedClasses['breadcrumbs']} */ ;
    for (const [crumb, i] of __VLS_vFor((__VLS_ctx.breadcrumbs))) {
        (crumb.path);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ onClick: (...[$event]) => {
                    if (!(__VLS_ctx.breadcrumbs.length > 0))
                        return;
                    __VLS_ctx.handleBreadcrumbClick(crumb.path);
                    // @ts-ignore
                    [pathInput, handlePathSubmit, breadcrumbs, breadcrumbs, handleBreadcrumbClick,];
                } },
            ...{ class: "breadcrumb-item" },
            ...{ class: ({ current: i === __VLS_ctx.breadcrumbs.length - 1 }) },
        });
        /** @type {__VLS_StyleScopedClasses['breadcrumb-item']} */ ;
        /** @type {__VLS_StyleScopedClasses['current']} */ ;
        (crumb.label);
        if (i < __VLS_ctx.breadcrumbs.length - 1) {
            let __VLS_51;
            /** @ts-ignore @type {typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_52 = __VLS_asFunctionalComponent1(__VLS_51, new __VLS_51({
                component: (__VLS_ctx.ChevronRightRound),
                size: (16),
                ...{ class: "breadcrumb-sep" },
            }));
            const __VLS_53 = __VLS_52({
                component: (__VLS_ctx.ChevronRightRound),
                size: (16),
                ...{ class: "breadcrumb-sep" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_52));
            /** @type {__VLS_StyleScopedClasses['breadcrumb-sep']} */ ;
        }
        // @ts-ignore
        [breadcrumbs, breadcrumbs, ChevronRightRound,];
    }
}
if (__VLS_ctx.error) {
    let __VLS_56;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_57 = __VLS_asFunctionalComponent1(__VLS_56, new __VLS_56({
        ...{ 'onClose': {} },
        type: "warning",
        ...{ style: {} },
        closable: (true),
    }));
    const __VLS_58 = __VLS_57({
        ...{ 'onClose': {} },
        type: "warning",
        ...{ style: {} },
        closable: (true),
    }, ...__VLS_functionalComponentArgsRest(__VLS_57));
    let __VLS_61;
    const __VLS_62 = ({ close: {} },
        { onClose: (...[$event]) => {
                if (!(__VLS_ctx.error))
                    return;
                __VLS_ctx.error = null;
                // @ts-ignore
                [error, error,];
            } });
    const { default: __VLS_63 } = __VLS_59.slots;
    (__VLS_ctx.error);
    // @ts-ignore
    [error,];
    var __VLS_59;
    var __VLS_60;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "column-header" },
});
/** @type {__VLS_StyleScopedClasses['column-header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span)({
    ...{ class: "col-icon" },
});
/** @type {__VLS_StyleScopedClasses['col-icon']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "col-name" },
});
/** @type {__VLS_StyleScopedClasses['col-name']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "col-date col-header-cell" },
});
/** @type {__VLS_StyleScopedClasses['col-date']} */ ;
/** @type {__VLS_StyleScopedClasses['col-header-cell']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span)({
    ...{ onMousedown: (...[$event]) => {
            __VLS_ctx.onResizeStart($event, 'date');
            // @ts-ignore
            [onResizeStart,];
        } },
    ...{ class: "resize-handle" },
});
/** @type {__VLS_StyleScopedClasses['resize-handle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "col-size col-header-cell" },
});
/** @type {__VLS_StyleScopedClasses['col-size']} */ ;
/** @type {__VLS_StyleScopedClasses['col-header-cell']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span)({
    ...{ onMousedown: (...[$event]) => {
            __VLS_ctx.onResizeStart($event, 'size');
            // @ts-ignore
            [onResizeStart,];
        } },
    ...{ class: "resize-handle" },
});
/** @type {__VLS_StyleScopedClasses['resize-handle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ref: "fileListRef",
    ...{ class: "file-explorer-list" },
});
/** @type {__VLS_StyleScopedClasses['file-explorer-list']} */ ;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "list-state" },
    });
    /** @type {__VLS_StyleScopedClasses['list-state']} */ ;
    let __VLS_64;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
        size: "small",
    }));
    const __VLS_66 = __VLS_65({
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_65));
}
else {
    if (__VLS_ctx.parentPath) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (__VLS_ctx.handleGoUp) },
            ...{ class: "file-row dir-row" },
        });
        /** @type {__VLS_StyleScopedClasses['file-row']} */ ;
        /** @type {__VLS_StyleScopedClasses['dir-row']} */ ;
        let __VLS_69;
        /** @ts-ignore @type {typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_70 = __VLS_asFunctionalComponent1(__VLS_69, new __VLS_69({
            component: (__VLS_ctx.FolderOutlined),
            size: (18),
            ...{ class: "entry-icon col-icon" },
        }));
        const __VLS_71 = __VLS_70({
            component: (__VLS_ctx.FolderOutlined),
            size: (18),
            ...{ class: "entry-icon col-icon" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_70));
        /** @type {__VLS_StyleScopedClasses['entry-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['col-icon']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "col-name" },
        });
        /** @type {__VLS_StyleScopedClasses['col-name']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
            ...{ class: "col-date" },
        });
        /** @type {__VLS_StyleScopedClasses['col-date']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
            ...{ class: "col-size" },
        });
        /** @type {__VLS_StyleScopedClasses['col-size']} */ ;
    }
    if (__VLS_ctx.filteredEntries.length === 0 && !__VLS_ctx.loading) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "list-state empty" },
        });
        /** @type {__VLS_StyleScopedClasses['list-state']} */ ;
        /** @type {__VLS_StyleScopedClasses['empty']} */ ;
    }
    for (const [entry] of __VLS_vFor((__VLS_ctx.filteredEntries))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    __VLS_ctx.handleEntryClick(entry);
                    // @ts-ignore
                    [parentPath, handleGoUp, loading, loading, FolderOutlined, filteredEntries, filteredEntries, handleEntryClick,];
                } },
            ...{ onDblclick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    __VLS_ctx.handleEntryDblClick(entry);
                    // @ts-ignore
                    [handleEntryDblClick,];
                } },
            key: (entry.fullPath),
            ...{ class: "file-row" },
            ...{ class: ({
                    'dir-row': entry.isDirectory,
                    selected: !entry.isDirectory && __VLS_ctx.selectedFile?.fullPath === entry.fullPath,
                }) },
        });
        /** @type {__VLS_StyleScopedClasses['file-row']} */ ;
        /** @type {__VLS_StyleScopedClasses['selected']} */ ;
        /** @type {__VLS_StyleScopedClasses['dir-row']} */ ;
        let __VLS_74;
        /** @ts-ignore @type {typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({
            component: (entry.isDirectory ? __VLS_ctx.FolderOutlined : __VLS_ctx.InsertDriveFileOutlined),
            size: (18),
            ...{ class: "entry-icon col-icon" },
        }));
        const __VLS_76 = __VLS_75({
            component: (entry.isDirectory ? __VLS_ctx.FolderOutlined : __VLS_ctx.InsertDriveFileOutlined),
            size: (18),
            ...{ class: "entry-icon col-icon" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_75));
        /** @type {__VLS_StyleScopedClasses['entry-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['col-icon']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "col-name" },
        });
        /** @type {__VLS_StyleScopedClasses['col-name']} */ ;
        let __VLS_79;
        /** @ts-ignore @type {typeof __VLS_components.NEllipsis | typeof __VLS_components.NEllipsis} */
        NEllipsis;
        // @ts-ignore
        const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({
            tooltip: ({ maxWidth: 400 }),
        }));
        const __VLS_81 = __VLS_80({
            tooltip: ({ maxWidth: 400 }),
        }, ...__VLS_functionalComponentArgsRest(__VLS_80));
        const { default: __VLS_84 } = __VLS_82.slots;
        (entry.name);
        // @ts-ignore
        [FolderOutlined, selectedFile, InsertDriveFileOutlined,];
        var __VLS_82;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "col-date" },
        });
        /** @type {__VLS_StyleScopedClasses['col-date']} */ ;
        (entry.lastModified ? __VLS_ctx.formatDate(entry.lastModified) : '');
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "col-size" },
        });
        /** @type {__VLS_StyleScopedClasses['col-size']} */ ;
        (!entry.isDirectory && entry.size != null ? __VLS_ctx.formatBytes(entry.size) : '');
        // @ts-ignore
        [formatDate, formatBytes,];
    }
}
if (__VLS_ctx.mode === 'file' && __VLS_ctx.filterOptions.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "filter-bar" },
    });
    /** @type {__VLS_StyleScopedClasses['filter-bar']} */ ;
    let __VLS_85;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_86 = __VLS_asFunctionalComponent1(__VLS_85, new __VLS_85({
        value: (__VLS_ctx.activeFilterIndex),
        options: (__VLS_ctx.filterOptions),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_87 = __VLS_86({
        value: (__VLS_ctx.activeFilterIndex),
        options: (__VLS_ctx.filterOptions),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_86));
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "file-explorer-footer" },
});
/** @type {__VLS_StyleScopedClasses['file-explorer-footer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "selected-display" },
});
/** @type {__VLS_StyleScopedClasses['selected-display']} */ ;
if (__VLS_ctx.selectedPath) {
    let __VLS_90;
    /** @ts-ignore @type {typeof __VLS_components.NEllipsis | typeof __VLS_components.NEllipsis} */
    NEllipsis;
    // @ts-ignore
    const __VLS_91 = __VLS_asFunctionalComponent1(__VLS_90, new __VLS_90({
        tooltip: ({ maxWidth: 500 }),
    }));
    const __VLS_92 = __VLS_91({
        tooltip: ({ maxWidth: 500 }),
    }, ...__VLS_functionalComponentArgsRest(__VLS_91));
    const { default: __VLS_95 } = __VLS_93.slots;
    (__VLS_ctx.selectedPath);
    // @ts-ignore
    [mode, filterOptions, filterOptions, activeFilterIndex, selectedPath, selectedPath,];
    var __VLS_93;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "no-selection" },
    });
    /** @type {__VLS_StyleScopedClasses['no-selection']} */ ;
    (__VLS_ctx.mode === 'folder' ? '当前目录即为选中' : '请选择文件');
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "footer-actions" },
});
/** @type {__VLS_StyleScopedClasses['footer-actions']} */ ;
let __VLS_96;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_97 = __VLS_asFunctionalComponent1(__VLS_96, new __VLS_96({
    ...{ 'onClick': {} },
}));
const __VLS_98 = __VLS_97({
    ...{ 'onClick': {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_97));
let __VLS_101;
const __VLS_102 = ({ click: {} },
    { onClick: (__VLS_ctx.handleCancel) });
const { default: __VLS_103 } = __VLS_99.slots;
// @ts-ignore
[handleCancel, mode,];
var __VLS_99;
var __VLS_100;
let __VLS_104;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_105 = __VLS_asFunctionalComponent1(__VLS_104, new __VLS_104({
    ...{ 'onClick': {} },
    type: "primary",
    disabled: (!__VLS_ctx.canConfirm),
}));
const __VLS_106 = __VLS_105({
    ...{ 'onClick': {} },
    type: "primary",
    disabled: (!__VLS_ctx.canConfirm),
}, ...__VLS_functionalComponentArgsRest(__VLS_105));
let __VLS_109;
const __VLS_110 = ({ click: {} },
    { onClick: (__VLS_ctx.handleConfirm) });
const { default: __VLS_111 } = __VLS_107.slots;
// @ts-ignore
[canConfirm, handleConfirm,];
var __VLS_107;
var __VLS_108;
// @ts-ignore
[];
var __VLS_3;
var __VLS_4;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
