import { computed, h, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router';
import { NAlert, NButton, NDataTable, NEmpty, NIcon, NInput, NSelect, NSpin, NTag, useDialog, useMessage, } from 'naive-ui';
import { AddOutlined, ArrowBackOutlined, DeleteOutlined, DriveFileRenameOutlineOutlined, FileDownloadOutlined, FileUploadOutlined, FolderOutlined, RefreshOutlined, SaveOutlined, SearchOutlined, TranslateOutlined, } from '@vicons/material';
import { filesystemApi } from '@/api/filesystem';
import { gamesApi, translationEditorApi } from '@/api/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
import RegexRuleEditor from '@/components/translation/RegexRuleEditor.vue';
const SOURCE_OPTIONS = [
    { label: '普通译文', value: 'default' },
    { label: '预翻译文本', value: 'pretranslated' },
    { label: '预翻译正则', value: 'pretranslated-regex' },
];
const route = useRoute();
const router = useRouter();
const message = useMessage();
const dialog = useDialog();
const { selectFile } = useFileExplorer();
const gameId = route.params['id'];
const game = ref(null);
const loading = ref(true);
const saving = ref(false);
const importing = ref(false);
const filePath = ref('');
const fileExists = ref(false);
const currentSource = ref('default');
const currentLanguage = ref('');
const availableLanguages = ref([]);
const entries = ref([]);
const regexRules = ref([]);
const newOriginal = ref('');
const newTranslation = ref('');
const searchKeyword = ref('');
const loadError = ref('');
let savedTextSnapshot = '[]';
let savedRegexSnapshot = '[]';
let nextRowId = 1;
let loadingKey = '';
const isRegexMode = computed(() => currentSource.value === 'pretranslated-regex');
const entryCount = computed(() => (isRegexMode.value ? regexRules.value.length : entries.value.length));
const normalizedSearchKeyword = computed(() => searchKeyword.value.trim().toLowerCase());
const filteredEntries = computed(() => {
    const keyword = normalizedSearchKeyword.value;
    if (!keyword)
        return entries.value;
    return entries.value.filter(row => row.original.toLowerCase().includes(keyword)
        || row.translation.toLowerCase().includes(keyword));
});
const filteredCount = computed(() => {
    const keyword = normalizedSearchKeyword.value;
    if (!keyword)
        return entryCount.value;
    return isRegexMode.value
        ? regexRules.value.filter(rule => matchesRegexRule(rule, keyword)).length
        : filteredEntries.value.length;
});
const isDirty = computed(() => isRegexMode.value
    ? serializeRegexRules(regexRules.value) !== savedRegexSnapshot
    : serializeTextEntries(entries.value) !== savedTextSnapshot);
const languageOptions = computed(() => {
    const values = new Set(availableLanguages.value
        .map(value => value.trim())
        .filter(Boolean));
    if (currentLanguage.value.trim()) {
        values.add(currentLanguage.value.trim());
    }
    return [...values]
        .sort((left, right) => left.localeCompare(right))
        .map(value => ({
        label: value.toUpperCase(),
        value,
    }));
});
const tableColumns = computed(() => [
    {
        title: '原文',
        key: 'original',
        minWidth: 320,
        resizable: true,
        render(row) {
            return h(NInput, {
                value: row.original,
                size: 'small',
                type: 'textarea',
                autosize: { minRows: 1, maxRows: 4 },
                'onUpdate:value': (value) => {
                    row.original = value;
                },
            });
        },
    },
    {
        title: '译文',
        key: 'translation',
        minWidth: 360,
        resizable: true,
        render(row) {
            return h(NInput, {
                value: row.translation,
                size: 'small',
                type: 'textarea',
                autosize: { minRows: 1, maxRows: 4 },
                'onUpdate:value': (value) => {
                    row.translation = value;
                },
            });
        },
    },
    {
        title: '状态',
        key: 'status',
        width: 92,
        render(row) {
            return h(NTag, {
                size: 'small',
                bordered: false,
                type: row.translation.trim() ? 'success' : 'warning',
            }, {
                default: () => (row.translation.trim() ? '已翻译' : '未翻译'),
            });
        },
    },
    {
        title: '',
        key: 'actions',
        width: 72,
        render(row) {
            return h(NButton, {
                size: 'tiny',
                quaternary: true,
                type: 'error',
                onClick: () => {
                    entries.value = entries.value.filter(item => item._id !== row._id);
                },
            }, {
                icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
            });
        },
    },
]);
function normalizeSource(value) {
    return value === 'pretranslated' || value === 'pretranslated-regex' ? value : 'default';
}
function normalizeLang(value) {
    const text = Array.isArray(value) ? value[0] : value;
    return typeof text === 'string' && text.trim() ? text : undefined;
}
function getRouteSource() {
    return normalizeSource(route.query.source);
}
function getRouteLang() {
    return normalizeLang(route.query.lang);
}
function buildRouteQuery(source, lang) {
    const query = { ...route.query };
    if (source === 'default') {
        delete query.source;
        delete query.lang;
    }
    else {
        query.source = source;
        if (lang)
            query.lang = lang;
        else
            delete query.lang;
    }
    return query;
}
function createRow(entry) {
    return {
        ...entry,
        _id: nextRowId++,
    };
}
function serializeTextEntries(items) {
    return JSON.stringify(items.map(item => ({
        original: item.original,
        translation: item.translation,
    })));
}
function serializeRegexRules(items) {
    return JSON.stringify(items.map(item => ({
        section: item.section,
        kind: item.kind,
        pattern: item.pattern,
        replacement: item.replacement,
    })));
}
function matchesRegexRule(rule, keyword) {
    if (!keyword)
        return true;
    return rule.pattern.toLowerCase().includes(keyword)
        || rule.replacement.toLowerCase().includes(keyword)
        || rule.kind.toLowerCase().includes(keyword);
}
function captureSnapshots() {
    savedTextSnapshot = serializeTextEntries(entries.value);
    savedRegexSnapshot = serializeRegexRules(regexRules.value);
}
function buildTextOptions(source = currentSource.value, lang = currentLanguage.value) {
    const textSource = source === 'pretranslated' ? 'pretranslated' : 'default';
    if (textSource === 'pretranslated') {
        return {
            source: textSource,
            ...(lang ? { lang } : {}),
        };
    }
    return { source: textSource };
}
async function loadEditorForRoute() {
    const source = getRouteSource();
    const lang = getRouteLang();
    const key = `${source}|${lang ?? ''}`;
    loadingKey = key;
    loading.value = true;
    try {
        currentSource.value = source;
        if (source === 'pretranslated-regex') {
            const editorData = await translationEditorApi.getRegex(gameId, lang);
            if (loadingKey !== key)
                return;
            currentLanguage.value = editorData.language;
            availableLanguages.value = editorData.availablePreTranslationLanguages;
            filePath.value = editorData.filePath;
            fileExists.value = editorData.fileExists;
            regexRules.value = editorData.rules;
            entries.value = [];
            captureSnapshots();
            loadError.value = '';
            return;
        }
        const editorData = await translationEditorApi.getEntries(gameId, buildTextOptions(source, lang));
        if (loadingKey !== key)
            return;
        currentSource.value = editorData.source;
        currentLanguage.value = editorData.language;
        availableLanguages.value = editorData.availablePreTranslationLanguages;
        filePath.value = editorData.filePath;
        fileExists.value = editorData.fileExists;
        entries.value = editorData.entries.map(createRow);
        regexRules.value = [];
        captureSnapshots();
        loadError.value = '';
    }
    finally {
        if (loadingKey === key) {
            loading.value = false;
        }
    }
}
function getErrorMessage(error, fallback) {
    return error instanceof Error ? error.message : fallback;
}
async function loadPage() {
    loading.value = true;
    loadError.value = '';
    try {
        game.value = await gamesApi.get(gameId);
        await loadEditorForRoute();
    }
    catch (error) {
        const errorMessage = getErrorMessage(error, '加载译文编辑器失败');
        loadError.value = errorMessage;
        message.error(errorMessage);
    }
    finally {
        loading.value = false;
    }
}
function handleBeforeUnload(event) {
    if (!isDirty.value)
        return;
    event.preventDefault();
    event.returnValue = '';
}
async function confirmDiscardChanges(content) {
    if (!isDirty.value)
        return true;
    return await new Promise((resolve) => {
        dialog.warning({
            title: '存在未保存修改',
            content,
            positiveText: '继续',
            negativeText: '取消',
            onPositiveClick: () => resolve(true),
            onNegativeClick: () => resolve(false),
            onClose: () => resolve(false),
        });
    });
}
async function handleSave() {
    saving.value = true;
    try {
        if (isRegexMode.value) {
            for (const rule of regexRules.value) {
                if (!rule.pattern.trim()) {
                    message.error('存在空的正则表达式，请填写或删除');
                    return;
                }
            }
            await translationEditorApi.saveRegex(gameId, regexRules.value, currentLanguage.value);
            fileExists.value = true;
            captureSnapshots();
            message.success('预翻译正则已保存');
            return;
        }
        const seen = new Set();
        for (const row of entries.value) {
            const original = row.original.trim();
            if (!original) {
                message.error('存在空白原文，请填写或删除后再保存');
                return;
            }
            if (seen.has(original)) {
                message.error(`存在重复原文: ${original.slice(0, 60)}`);
                return;
            }
            seen.add(original);
        }
        await translationEditorApi.saveEntries(gameId, entries.value.map(row => ({
            original: row.original,
            translation: row.translation,
        })), buildTextOptions());
        fileExists.value = true;
        captureSnapshots();
        message.success(currentSource.value === 'pretranslated' ? '预翻译文本已保存' : '译文已保存');
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '保存失败');
    }
    finally {
        saving.value = false;
    }
}
function handleAddEntry() {
    const original = newOriginal.value.trim();
    if (!original) {
        message.warning('请输入原文');
        return;
    }
    if (entries.value.some(row => row.original === original)) {
        message.warning('该原文已存在');
        return;
    }
    entries.value = [
        createRow({
            original,
            translation: newTranslation.value,
        }),
        ...entries.value,
    ];
    newOriginal.value = '';
    newTranslation.value = '';
}
async function handleImport() {
    const path = await selectFile({
        title: isRegexMode.value ? '导入预翻译正则文件' : '导入翻译文件',
        filters: [{ label: '文本文件', extensions: ['.txt'] }],
    });
    if (!path)
        return;
    importing.value = true;
    try {
        const { content } = await filesystemApi.readText(path);
        if (isRegexMode.value) {
            regexRules.value = await translationEditorApi.importRegex(gameId, content, currentLanguage.value);
            message.success(`导入完成，共 ${regexRules.value.length} 条规则`);
            return;
        }
        const importedEntries = await translationEditorApi.parseImport(gameId, content);
        const existingOriginals = new Set(entries.value.map(row => row.original));
        let added = 0;
        for (const entry of importedEntries) {
            if (existingOriginals.has(entry.original))
                continue;
            entries.value = [...entries.value, createRow(entry)];
            existingOriginals.add(entry.original);
            added++;
        }
        message.success(`导入完成，新增 ${added} 条`);
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '导入失败');
    }
    finally {
        importing.value = false;
    }
}
function handleExport() {
    const url = isRegexMode.value
        ? translationEditorApi.getRegexExportUrl(gameId, currentLanguage.value)
        : translationEditorApi.getExportUrl(gameId, buildTextOptions());
    window.open(url, '_blank', 'noopener');
}
async function handleReload() {
    const confirmed = await confirmDiscardChanges('重新加载将丢失当前未保存修改，是否继续？');
    if (!confirmed)
        return;
    try {
        await loadEditorForRoute();
        message.success('已重新加载文件内容');
    }
    catch (error) {
        message.error(error instanceof Error ? error.message : '重新加载失败');
    }
}
async function handleSourceChange(value) {
    const nextSource = normalizeSource(value);
    if (nextSource === currentSource.value)
        return;
    const nextLanguage = nextSource === 'default'
        ? undefined
        : currentLanguage.value || availableLanguages.value[0];
    await router.replace({
        query: buildRouteQuery(nextSource, nextLanguage),
    });
}
async function handleLanguageChange(value) {
    const nextLanguage = value ?? '';
    if (!nextLanguage || nextLanguage === currentLanguage.value)
        return;
    await router.replace({
        query: buildRouteQuery(currentSource.value, nextLanguage),
    });
}
onMounted(async () => {
    window.addEventListener('beforeunload', handleBeforeUnload);
    await loadPage();
});
watch(() => `${getRouteSource()}|${getRouteLang() ?? ''}`, async () => {
    if (!game.value)
        return;
    try {
        await loadEditorForRoute();
    }
    catch (error) {
        const errorMessage = getErrorMessage(error, '加载译文编辑器失败');
        loadError.value = errorMessage;
        message.error(errorMessage);
    }
});
onBeforeUnmount(() => {
    window.removeEventListener('beforeunload', handleBeforeUnload);
});
onBeforeRouteUpdate(async (to, from) => {
    const nextKey = `${normalizeSource(to.query.source)}|${normalizeLang(to.query.lang) ?? ''}`;
    const currentKey = `${normalizeSource(from.query.source)}|${normalizeLang(from.query.lang) ?? ''}`;
    if (nextKey === currentKey)
        return true;
    return await confirmDiscardChanges('切换来源或语言会丢失当前未保存修改，是否继续？');
});
onBeforeRouteLeave(async () => {
    return await confirmDiscardChanges('离开页面会丢失未保存修改，是否继续？');
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['translation-search']} */ ;
/** @type {__VLS_StyleScopedClasses['search-input']} */ ;
/** @type {__VLS_StyleScopedClasses['add-entry-row']} */ ;
if (__VLS_ctx.loading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "loading-state" },
    });
    /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
    let __VLS_0;
    /** @ts-ignore @type {typeof __VLS_components.NSpin} */
    NSpin;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
        size: "large",
    }));
    const __VLS_2 = __VLS_1({
        size: "large",
    }, ...__VLS_functionalComponentArgsRest(__VLS_1));
}
else if (__VLS_ctx.game || __VLS_ctx.loadError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "sub-page" },
    });
    /** @type {__VLS_StyleScopedClasses['sub-page']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "sub-page-header" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['sub-page-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game || __VLS_ctx.loadError))
                    return;
                __VLS_ctx.router.push(__VLS_ctx.game ? `/games/${__VLS_ctx.gameId}` : '/');
                // @ts-ignore
                [loading, game, game, loadError, router, gameId,];
            } },
        ...{ class: "back-button" },
    });
    /** @type {__VLS_StyleScopedClasses['back-button']} */ ;
    let __VLS_5;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_6 = __VLS_asFunctionalComponent1(__VLS_5, new __VLS_5({
        size: (20),
    }));
    const __VLS_7 = __VLS_6({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_6));
    const { default: __VLS_10 } = __VLS_8.slots;
    let __VLS_11;
    /** @ts-ignore @type {typeof __VLS_components.ArrowBackOutlined} */
    ArrowBackOutlined;
    // @ts-ignore
    const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({}));
    const __VLS_13 = __VLS_12({}, ...__VLS_functionalComponentArgsRest(__VLS_12));
    // @ts-ignore
    [];
    var __VLS_8;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.game?.name ?? '返回游戏库');
    __VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
        ...{ class: "page-title" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['page-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "page-title-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['page-title-icon']} */ ;
    let __VLS_16;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_17 = __VLS_asFunctionalComponent1(__VLS_16, new __VLS_16({
        size: (24),
    }));
    const __VLS_18 = __VLS_17({
        size: (24),
    }, ...__VLS_functionalComponentArgsRest(__VLS_17));
    const { default: __VLS_21 } = __VLS_19.slots;
    let __VLS_22;
    /** @ts-ignore @type {typeof __VLS_components.DriveFileRenameOutlineOutlined} */
    DriveFileRenameOutlineOutlined;
    // @ts-ignore
    const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({}));
    const __VLS_24 = __VLS_23({}, ...__VLS_functionalComponentArgsRest(__VLS_23));
    // @ts-ignore
    [game,];
    var __VLS_19;
    if (__VLS_ctx.game && __VLS_ctx.isDirty) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "unsaved-badge" },
        });
        /** @type {__VLS_StyleScopedClasses['unsaved-badge']} */ ;
    }
    if (__VLS_ctx.loadError) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        let __VLS_27;
        /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
        NAlert;
        // @ts-ignore
        const __VLS_28 = __VLS_asFunctionalComponent1(__VLS_27, new __VLS_27({
            type: "error",
            title: "加载失败",
        }));
        const __VLS_29 = __VLS_28({
            type: "error",
            title: "加载失败",
        }, ...__VLS_functionalComponentArgsRest(__VLS_28));
        const { default: __VLS_32 } = __VLS_30.slots;
        (__VLS_ctx.loadError);
        // @ts-ignore
        [game, loadError, loadError, isDirty,];
        var __VLS_30;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "header-actions" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
        let __VLS_33;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
            ...{ 'onClick': {} },
            size: "small",
        }));
        const __VLS_35 = __VLS_34({
            ...{ 'onClick': {} },
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_34));
        let __VLS_38;
        const __VLS_39 = ({ click: {} },
            { onClick: (__VLS_ctx.loadPage) });
        const { default: __VLS_40 } = __VLS_36.slots;
        {
            const { icon: __VLS_41 } = __VLS_36.slots;
            let __VLS_42;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({
                size: (16),
            }));
            const __VLS_44 = __VLS_43({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_43));
            const { default: __VLS_47 } = __VLS_45.slots;
            let __VLS_48;
            /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
            RefreshOutlined;
            // @ts-ignore
            const __VLS_49 = __VLS_asFunctionalComponent1(__VLS_48, new __VLS_48({}));
            const __VLS_50 = __VLS_49({}, ...__VLS_functionalComponentArgsRest(__VLS_49));
            // @ts-ignore
            [loadPage,];
            var __VLS_45;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_36;
        var __VLS_37;
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-header" },
        });
        /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
            ...{ class: "section-title" },
        });
        /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "section-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
        let __VLS_53;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({
            size: (16),
        }));
        const __VLS_55 = __VLS_54({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_54));
        const { default: __VLS_58 } = __VLS_56.slots;
        let __VLS_59;
        /** @ts-ignore @type {typeof __VLS_components.FolderOutlined} */
        FolderOutlined;
        // @ts-ignore
        const __VLS_60 = __VLS_asFunctionalComponent1(__VLS_59, new __VLS_59({}));
        const __VLS_61 = __VLS_60({}, ...__VLS_functionalComponentArgsRest(__VLS_60));
        // @ts-ignore
        [];
        var __VLS_56;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "header-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
        let __VLS_64;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_65 = __VLS_asFunctionalComponent1(__VLS_64, new __VLS_64({
            ...{ 'onClick': {} },
            size: "small",
            loading: (__VLS_ctx.importing),
        }));
        const __VLS_66 = __VLS_65({
            ...{ 'onClick': {} },
            size: "small",
            loading: (__VLS_ctx.importing),
        }, ...__VLS_functionalComponentArgsRest(__VLS_65));
        let __VLS_69;
        const __VLS_70 = ({ click: {} },
            { onClick: (__VLS_ctx.handleImport) });
        const { default: __VLS_71 } = __VLS_67.slots;
        {
            const { icon: __VLS_72 } = __VLS_67.slots;
            let __VLS_73;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
                size: (16),
            }));
            const __VLS_75 = __VLS_74({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_74));
            const { default: __VLS_78 } = __VLS_76.slots;
            let __VLS_79;
            /** @ts-ignore @type {typeof __VLS_components.FileUploadOutlined} */
            FileUploadOutlined;
            // @ts-ignore
            const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({}));
            const __VLS_81 = __VLS_80({}, ...__VLS_functionalComponentArgsRest(__VLS_80));
            // @ts-ignore
            [importing, handleImport,];
            var __VLS_76;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_67;
        var __VLS_68;
        let __VLS_84;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (!__VLS_ctx.fileExists),
        }));
        const __VLS_86 = __VLS_85({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (!__VLS_ctx.fileExists),
        }, ...__VLS_functionalComponentArgsRest(__VLS_85));
        let __VLS_89;
        const __VLS_90 = ({ click: {} },
            { onClick: (__VLS_ctx.handleExport) });
        const { default: __VLS_91 } = __VLS_87.slots;
        {
            const { icon: __VLS_92 } = __VLS_87.slots;
            let __VLS_93;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_94 = __VLS_asFunctionalComponent1(__VLS_93, new __VLS_93({
                size: (16),
            }));
            const __VLS_95 = __VLS_94({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_94));
            const { default: __VLS_98 } = __VLS_96.slots;
            let __VLS_99;
            /** @ts-ignore @type {typeof __VLS_components.FileDownloadOutlined} */
            FileDownloadOutlined;
            // @ts-ignore
            const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({}));
            const __VLS_101 = __VLS_100({}, ...__VLS_functionalComponentArgsRest(__VLS_100));
            // @ts-ignore
            [fileExists, handleExport,];
            var __VLS_96;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_87;
        var __VLS_88;
        let __VLS_104;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_105 = __VLS_asFunctionalComponent1(__VLS_104, new __VLS_104({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.saving),
        }));
        const __VLS_106 = __VLS_105({
            ...{ 'onClick': {} },
            size: "small",
            disabled: (__VLS_ctx.saving),
        }, ...__VLS_functionalComponentArgsRest(__VLS_105));
        let __VLS_109;
        const __VLS_110 = ({ click: {} },
            { onClick: (__VLS_ctx.handleReload) });
        const { default: __VLS_111 } = __VLS_107.slots;
        {
            const { icon: __VLS_112 } = __VLS_107.slots;
            let __VLS_113;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_114 = __VLS_asFunctionalComponent1(__VLS_113, new __VLS_113({
                size: (16),
            }));
            const __VLS_115 = __VLS_114({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_114));
            const { default: __VLS_118 } = __VLS_116.slots;
            let __VLS_119;
            /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
            RefreshOutlined;
            // @ts-ignore
            const __VLS_120 = __VLS_asFunctionalComponent1(__VLS_119, new __VLS_119({}));
            const __VLS_121 = __VLS_120({}, ...__VLS_functionalComponentArgsRest(__VLS_120));
            // @ts-ignore
            [saving, handleReload,];
            var __VLS_116;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_107;
        var __VLS_108;
        let __VLS_124;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_125 = __VLS_asFunctionalComponent1(__VLS_124, new __VLS_124({
            ...{ 'onClick': {} },
            size: "small",
            type: "primary",
            loading: (__VLS_ctx.saving),
            disabled: (!__VLS_ctx.isDirty),
        }));
        const __VLS_126 = __VLS_125({
            ...{ 'onClick': {} },
            size: "small",
            type: "primary",
            loading: (__VLS_ctx.saving),
            disabled: (!__VLS_ctx.isDirty),
        }, ...__VLS_functionalComponentArgsRest(__VLS_125));
        let __VLS_129;
        const __VLS_130 = ({ click: {} },
            { onClick: (__VLS_ctx.handleSave) });
        const { default: __VLS_131 } = __VLS_127.slots;
        {
            const { icon: __VLS_132 } = __VLS_127.slots;
            let __VLS_133;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_134 = __VLS_asFunctionalComponent1(__VLS_133, new __VLS_133({
                size: (16),
            }));
            const __VLS_135 = __VLS_134({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_134));
            const { default: __VLS_138 } = __VLS_136.slots;
            let __VLS_139;
            /** @ts-ignore @type {typeof __VLS_components.SaveOutlined} */
            SaveOutlined;
            // @ts-ignore
            const __VLS_140 = __VLS_asFunctionalComponent1(__VLS_139, new __VLS_139({}));
            const __VLS_141 = __VLS_140({}, ...__VLS_functionalComponentArgsRest(__VLS_140));
            // @ts-ignore
            [isDirty, saving, handleSave,];
            var __VLS_136;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_127;
        var __VLS_128;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "toolbar-grid" },
        });
        /** @type {__VLS_StyleScopedClasses['toolbar-grid']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
            ...{ class: "toolbar-field" },
        });
        /** @type {__VLS_StyleScopedClasses['toolbar-field']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "toolbar-label" },
        });
        /** @type {__VLS_StyleScopedClasses['toolbar-label']} */ ;
        let __VLS_144;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_145 = __VLS_asFunctionalComponent1(__VLS_144, new __VLS_144({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.currentSource),
            options: (__VLS_ctx.SOURCE_OPTIONS),
            size: "small",
        }));
        const __VLS_146 = __VLS_145({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.currentSource),
            options: (__VLS_ctx.SOURCE_OPTIONS),
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_145));
        let __VLS_149;
        const __VLS_150 = ({ 'update:value': {} },
            { 'onUpdate:value': (__VLS_ctx.handleSourceChange) });
        var __VLS_147;
        var __VLS_148;
        if (__VLS_ctx.currentSource !== 'default') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({
                ...{ class: "toolbar-field" },
            });
            /** @type {__VLS_StyleScopedClasses['toolbar-field']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "toolbar-label" },
            });
            /** @type {__VLS_StyleScopedClasses['toolbar-label']} */ ;
            let __VLS_151;
            /** @ts-ignore @type {typeof __VLS_components.NSelect} */
            NSelect;
            // @ts-ignore
            const __VLS_152 = __VLS_asFunctionalComponent1(__VLS_151, new __VLS_151({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.currentLanguage),
                options: (__VLS_ctx.languageOptions),
                size: "small",
                disabled: (__VLS_ctx.languageOptions.length === 0),
            }));
            const __VLS_153 = __VLS_152({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.currentLanguage),
                options: (__VLS_ctx.languageOptions),
                size: "small",
                disabled: (__VLS_ctx.languageOptions.length === 0),
            }, ...__VLS_functionalComponentArgsRest(__VLS_152));
            let __VLS_156;
            const __VLS_157 = ({ 'update:value': {} },
                { 'onUpdate:value': (__VLS_ctx.handleLanguageChange) });
            var __VLS_154;
            var __VLS_155;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "file-info" },
        });
        /** @type {__VLS_StyleScopedClasses['file-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-row" },
        });
        /** @type {__VLS_StyleScopedClasses['info-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-item" },
        });
        /** @type {__VLS_StyleScopedClasses['info-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-label" },
        });
        /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.code, __VLS_intrinsics.code)({
            ...{ class: "info-value file-path" },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        /** @type {__VLS_StyleScopedClasses['file-path']} */ ;
        (__VLS_ctx.filePath);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-row" },
        });
        /** @type {__VLS_StyleScopedClasses['info-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-item" },
        });
        /** @type {__VLS_StyleScopedClasses['info-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-label" },
        });
        /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-value" },
        });
        /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
        (__VLS_ctx.entryCount);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-item" },
        });
        /** @type {__VLS_StyleScopedClasses['info-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-label" },
        });
        /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
        let __VLS_158;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_159 = __VLS_asFunctionalComponent1(__VLS_158, new __VLS_158({
            size: "small",
            bordered: (false),
        }));
        const __VLS_160 = __VLS_159({
            size: "small",
            bordered: (false),
        }, ...__VLS_functionalComponentArgsRest(__VLS_159));
        const { default: __VLS_163 } = __VLS_161.slots;
        (__VLS_ctx.currentLanguage.toUpperCase());
        // @ts-ignore
        [currentSource, currentSource, SOURCE_OPTIONS, handleSourceChange, currentLanguage, currentLanguage, languageOptions, languageOptions, handleLanguageChange, filePath, entryCount,];
        var __VLS_161;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "info-item" },
        });
        /** @type {__VLS_StyleScopedClasses['info-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "info-label" },
        });
        /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
        let __VLS_164;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_165 = __VLS_asFunctionalComponent1(__VLS_164, new __VLS_164({
            size: "small",
            bordered: (false),
            type: (__VLS_ctx.fileExists ? 'success' : 'warning'),
        }));
        const __VLS_166 = __VLS_165({
            size: "small",
            bordered: (false),
            type: (__VLS_ctx.fileExists ? 'success' : 'warning'),
        }, ...__VLS_functionalComponentArgsRest(__VLS_165));
        const { default: __VLS_169 } = __VLS_167.slots;
        (__VLS_ctx.fileExists ? '已存在' : '保存时创建');
        // @ts-ignore
        [fileExists, fileExists,];
        var __VLS_167;
        if (__VLS_ctx.currentSource === 'pretranslated') {
            let __VLS_170;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
                type: "info",
                ...{ style: {} },
            }));
            const __VLS_172 = __VLS_171({
                type: "info",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_171));
            const { default: __VLS_175 } = __VLS_173.slots;
            // @ts-ignore
            [currentSource,];
            var __VLS_173;
        }
        if (__VLS_ctx.currentSource !== 'default' && __VLS_ctx.languageOptions.length === 0) {
            let __VLS_176;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_177 = __VLS_asFunctionalComponent1(__VLS_176, new __VLS_176({
                type: "info",
                ...{ style: {} },
            }));
            const __VLS_178 = __VLS_177({
                type: "info",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_177));
            const { default: __VLS_181 } = __VLS_179.slots;
            // @ts-ignore
            [currentSource, languageOptions,];
            var __VLS_179;
        }
        if (__VLS_ctx.currentSource === 'pretranslated-regex') {
            let __VLS_182;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_183 = __VLS_asFunctionalComponent1(__VLS_182, new __VLS_182({
                type: "warning",
                ...{ style: {} },
            }));
            const __VLS_184 = __VLS_183({
                type: "warning",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_183));
            const { default: __VLS_187 } = __VLS_185.slots;
            // @ts-ignore
            [currentSource,];
            var __VLS_185;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-card" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "section-header" },
        });
        /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
            ...{ class: "section-title" },
        });
        /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "section-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
        let __VLS_188;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_189 = __VLS_asFunctionalComponent1(__VLS_188, new __VLS_188({
            size: (16),
        }));
        const __VLS_190 = __VLS_189({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_189));
        const { default: __VLS_193 } = __VLS_191.slots;
        let __VLS_194;
        /** @ts-ignore @type {typeof __VLS_components.TranslateOutlined} */
        TranslateOutlined;
        // @ts-ignore
        const __VLS_195 = __VLS_asFunctionalComponent1(__VLS_194, new __VLS_194({}));
        const __VLS_196 = __VLS_195({}, ...__VLS_functionalComponentArgsRest(__VLS_195));
        // @ts-ignore
        [];
        var __VLS_191;
        (__VLS_ctx.isRegexMode ? '正则规则' : '翻译条目');
        let __VLS_199;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_200 = __VLS_asFunctionalComponent1(__VLS_199, new __VLS_199({
            size: "small",
            bordered: (false),
            ...{ style: {} },
        }));
        const __VLS_201 = __VLS_200({
            size: "small",
            bordered: (false),
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_200));
        const { default: __VLS_204 } = __VLS_202.slots;
        (__VLS_ctx.normalizedSearchKeyword ? `${__VLS_ctx.filteredCount} / ${__VLS_ctx.entryCount}` : __VLS_ctx.entryCount);
        // @ts-ignore
        [entryCount, entryCount, isRegexMode, normalizedSearchKeyword, filteredCount,];
        var __VLS_202;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "header-actions translation-search" },
        });
        /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
        /** @type {__VLS_StyleScopedClasses['translation-search']} */ ;
        let __VLS_205;
        /** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_206 = __VLS_asFunctionalComponent1(__VLS_205, new __VLS_205({
            value: (__VLS_ctx.searchKeyword),
            placeholder: (__VLS_ctx.isRegexMode ? '搜索正则、替换内容或 kind...' : '搜索原文或译文...'),
            clearable: true,
            size: "small",
            ...{ class: "search-input" },
        }));
        const __VLS_207 = __VLS_206({
            value: (__VLS_ctx.searchKeyword),
            placeholder: (__VLS_ctx.isRegexMode ? '搜索正则、替换内容或 kind...' : '搜索原文或译文...'),
            clearable: true,
            size: "small",
            ...{ class: "search-input" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_206));
        /** @type {__VLS_StyleScopedClasses['search-input']} */ ;
        const { default: __VLS_210 } = __VLS_208.slots;
        {
            const { prefix: __VLS_211 } = __VLS_208.slots;
            let __VLS_212;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_213 = __VLS_asFunctionalComponent1(__VLS_212, new __VLS_212({
                size: (16),
            }));
            const __VLS_214 = __VLS_213({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_213));
            const { default: __VLS_217 } = __VLS_215.slots;
            let __VLS_218;
            /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
            SearchOutlined;
            // @ts-ignore
            const __VLS_219 = __VLS_asFunctionalComponent1(__VLS_218, new __VLS_218({}));
            const __VLS_220 = __VLS_219({}, ...__VLS_functionalComponentArgsRest(__VLS_219));
            // @ts-ignore
            [isRegexMode, searchKeyword,];
            var __VLS_215;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_208;
        if (__VLS_ctx.isRegexMode) {
            const __VLS_223 = RegexRuleEditor;
            // @ts-ignore
            const __VLS_224 = __VLS_asFunctionalComponent1(__VLS_223, new __VLS_223({
                rules: (__VLS_ctx.regexRules),
                searchKeyword: (__VLS_ctx.searchKeyword),
            }));
            const __VLS_225 = __VLS_224({
                rules: (__VLS_ctx.regexRules),
                searchKeyword: (__VLS_ctx.searchKeyword),
            }, ...__VLS_functionalComponentArgsRest(__VLS_224));
        }
        else {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "add-entry-row" },
            });
            /** @type {__VLS_StyleScopedClasses['add-entry-row']} */ ;
            let __VLS_228;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_229 = __VLS_asFunctionalComponent1(__VLS_228, new __VLS_228({
                value: (__VLS_ctx.newOriginal),
                placeholder: "原文",
                size: "small",
                type: "textarea",
                autosize: ({ minRows: 1, maxRows: 3 }),
            }));
            const __VLS_230 = __VLS_229({
                value: (__VLS_ctx.newOriginal),
                placeholder: "原文",
                size: "small",
                type: "textarea",
                autosize: ({ minRows: 1, maxRows: 3 }),
            }, ...__VLS_functionalComponentArgsRest(__VLS_229));
            let __VLS_233;
            /** @ts-ignore @type {typeof __VLS_components.NInput} */
            NInput;
            // @ts-ignore
            const __VLS_234 = __VLS_asFunctionalComponent1(__VLS_233, new __VLS_233({
                value: (__VLS_ctx.newTranslation),
                placeholder: "译文（可选）",
                size: "small",
                type: "textarea",
                autosize: ({ minRows: 1, maxRows: 3 }),
            }));
            const __VLS_235 = __VLS_234({
                value: (__VLS_ctx.newTranslation),
                placeholder: "译文（可选）",
                size: "small",
                type: "textarea",
                autosize: ({ minRows: 1, maxRows: 3 }),
            }, ...__VLS_functionalComponentArgsRest(__VLS_234));
            let __VLS_238;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_239 = __VLS_asFunctionalComponent1(__VLS_238, new __VLS_238({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
            }));
            const __VLS_240 = __VLS_239({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
            }, ...__VLS_functionalComponentArgsRest(__VLS_239));
            let __VLS_243;
            const __VLS_244 = ({ click: {} },
                { onClick: (__VLS_ctx.handleAddEntry) });
            const { default: __VLS_245 } = __VLS_241.slots;
            {
                const { icon: __VLS_246 } = __VLS_241.slots;
                let __VLS_247;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_248 = __VLS_asFunctionalComponent1(__VLS_247, new __VLS_247({
                    size: (16),
                }));
                const __VLS_249 = __VLS_248({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_248));
                const { default: __VLS_252 } = __VLS_250.slots;
                let __VLS_253;
                /** @ts-ignore @type {typeof __VLS_components.AddOutlined} */
                AddOutlined;
                // @ts-ignore
                const __VLS_254 = __VLS_asFunctionalComponent1(__VLS_253, new __VLS_253({}));
                const __VLS_255 = __VLS_254({}, ...__VLS_functionalComponentArgsRest(__VLS_254));
                // @ts-ignore
                [isRegexMode, searchKeyword, regexRules, newOriginal, newTranslation, handleAddEntry,];
                var __VLS_250;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_241;
            var __VLS_242;
            if (__VLS_ctx.filteredEntries.length > 0) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "table-container" },
                });
                /** @type {__VLS_StyleScopedClasses['table-container']} */ ;
                let __VLS_258;
                /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
                NDataTable;
                // @ts-ignore
                const __VLS_259 = __VLS_asFunctionalComponent1(__VLS_258, new __VLS_258({
                    columns: (__VLS_ctx.tableColumns),
                    data: (__VLS_ctx.filteredEntries),
                    rowKey: ((row) => row._id),
                    maxHeight: (640),
                    virtualScroll: true,
                    striped: true,
                    size: "small",
                }));
                const __VLS_260 = __VLS_259({
                    columns: (__VLS_ctx.tableColumns),
                    data: (__VLS_ctx.filteredEntries),
                    rowKey: ((row) => row._id),
                    maxHeight: (640),
                    virtualScroll: true,
                    striped: true,
                    size: "small",
                }, ...__VLS_functionalComponentArgsRest(__VLS_259));
            }
            else if (__VLS_ctx.entries.length > 0) {
                let __VLS_263;
                /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
                NEmpty;
                // @ts-ignore
                const __VLS_264 = __VLS_asFunctionalComponent1(__VLS_263, new __VLS_263({
                    description: "没有匹配的翻译条目",
                    ...{ style: {} },
                }));
                const __VLS_265 = __VLS_264({
                    description: "没有匹配的翻译条目",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_264));
            }
            else {
                let __VLS_268;
                /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
                NEmpty;
                // @ts-ignore
                const __VLS_269 = __VLS_asFunctionalComponent1(__VLS_268, new __VLS_268({
                    description: "暂无翻译条目",
                    ...{ style: {} },
                }));
                const __VLS_270 = __VLS_269({
                    description: "暂无翻译条目",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_269));
            }
        }
    }
}
// @ts-ignore
[filteredEntries, filteredEntries, tableColumns, entries,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
