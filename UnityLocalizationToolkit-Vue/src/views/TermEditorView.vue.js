import { ref, computed, h, onMounted, nextTick } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NIcon, NInput, NDataTable, NSwitch, NTag, NSpin, NEmpty, NSelect, NInputNumber, NPopover, NCheckbox, NModal, useMessage, useDialog, } from 'naive-ui';
import { ArrowBackOutlined, SearchOutlined, SaveOutlined, FileUploadOutlined, FileDownloadOutlined, AddOutlined, DeleteOutlined, DeleteSweepOutlined, MenuBookOutlined, SettingsOutlined, ContentCopyOutlined, } from '@vicons/material';
import { gamesApi } from '@/api/games';
import { filesystemApi } from '@/api/filesystem';
import { useFileExplorer } from '@/composables/useFileExplorer';
import { useAutoSave } from '@/composables/useAutoSave';
const route = useRoute();
const router = useRouter();
const message = useMessage();
const dialog = useDialog();
const gameId = route.params['id'];
const game = ref(null);
const loading = ref(true);
const manualSaving = ref(false);
// Editor state
const entries = ref([]);
let nextId = 1;
// Import
const { selectFile } = useFileExplorer();
// Add entry form
const newType = ref('translate');
const newOriginal = ref('');
const newTranslation = ref('');
const newCategory = ref('general');
// Search
const searchKeyword = ref('');
// Filters
const filterType = ref('all');
const filterCategory = ref(null);
// Column visibility
const COLUMN_VISIBILITY_KEY = 'term-editor-columns';
const defaultVisibleColumns = ['type', 'original', 'translation', 'category', 'isRegex', 'exactMatch', 'actions'];
const visibleColumns = ref(loadColumnVisibility());
function loadColumnVisibility() {
    try {
        const stored = localStorage.getItem(COLUMN_VISIBILITY_KEY);
        if (stored)
            return JSON.parse(stored);
    }
    catch { /* ignore */ }
    return [...defaultVisibleColumns];
}
function saveColumnVisibility() {
    localStorage.setItem(COLUMN_VISIBILITY_KEY, JSON.stringify(visibleColumns.value));
}
// Import from game modal
const showImportGameModal = ref(false);
const gameList = ref([]);
const loadingGames = ref(false);
const importingFromGame = ref(false);
// Category options
const categoryOptions = [
    { label: '角色名', value: 'character' },
    { label: '地名', value: 'location' },
    { label: '物品名', value: 'item' },
    { label: '技能名', value: 'skill' },
    { label: '组织名', value: 'organization' },
    { label: '通用', value: 'general' },
];
const categoryLabelMap = {
    character: '角色名',
    location: '地名',
    item: '物品名',
    skill: '技能名',
    organization: '组织名',
    general: '通用',
};
const typeOptions = [
    { label: '翻译', value: 'translate' },
    { label: '禁翻', value: 'doNotTranslate' },
];
// All optional column definitions for visibility control
const optionalColumns = [
    { key: 'description', label: '描述' },
    { key: 'caseSensitive', label: '大小写敏感' },
    { key: 'priority', label: '优先级' },
];
function toRows(items) {
    return items.map(e => ({ ...e, _id: nextId++ }));
}
function getValidEntries() {
    return entries.value
        .filter(e => e.original.trim())
        .map(({ type, original, translation, category, description, isRegex, caseSensitive, exactMatch, priority }) => ({
        type,
        original,
        translation: type === 'doNotTranslate' ? undefined : translation,
        category: category || undefined,
        description: description?.trim() || undefined,
        isRegex,
        caseSensitive,
        exactMatch,
        priority,
    }));
}
// Auto-save (2s debounce, deep watch)
const { saving: autoSaving, enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(() => entries.value, async () => {
    try {
        const valid = getValidEntries();
        await gamesApi.saveTerms(gameId, valid);
    }
    catch {
        message.error('自动保存术语库失败');
    }
}, { debounceMs: 2000, deep: true });
const aggregateCounts = computed(() => {
    const type = { all: entries.value.length, translate: 0, dnt: 0 };
    const category = {
        character: 0,
        location: 0,
        item: 0,
        skill: 0,
        organization: 0,
        general: 0,
    };
    for (const entry of entries.value) {
        if (entry.type === 'translate')
            type.translate++;
        else if (entry.type === 'doNotTranslate')
            type.dnt++;
        const key = entry.category || 'general';
        category[key] = (category[key] ?? 0) + 1;
    }
    return { type, category };
});
const typeCounts = computed(() => aggregateCounts.value.type);
const categoryCounts = computed(() => aggregateCounts.value.category);
const filteredEntries = computed(() => {
    let result = entries.value;
    // Type filter
    if (filterType.value !== 'all') {
        result = result.filter(e => e.type === filterType.value);
    }
    // Category filter
    if (filterCategory.value) {
        result = result.filter(e => (e.category || 'general') === filterCategory.value);
    }
    // Search
    const kw = searchKeyword.value.toLowerCase();
    if (kw) {
        result = result.filter(e => e.original.toLowerCase().includes(kw) ||
            (e.translation ?? '').toLowerCase().includes(kw) ||
            (e.description ?? '').toLowerCase().includes(kw));
    }
    return result;
});
// Table columns
const tableColumns = computed(() => {
    const cols = [];
    if (visibleColumns.value.includes('type')) {
        cols.push({
            title: '类型',
            key: 'type',
            width: 70,
            align: 'center',
            render(row) {
                return h(NTag, {
                    size: 'small',
                    type: row.type === 'translate' ? 'info' : 'warning',
                    bordered: false,
                    style: 'cursor: pointer',
                    onClick: () => {
                        if (row.type === 'translate') {
                            row.type = 'doNotTranslate';
                            row.translation = undefined;
                        }
                        else {
                            row.type = 'translate';
                        }
                    },
                }, () => row.type === 'translate' ? '翻译' : '禁翻');
            },
        });
    }
    if (visibleColumns.value.includes('original')) {
        cols.push({
            title: '原文',
            key: 'original',
            resizable: true,
            minWidth: 140,
            render(row) {
                return h(NInput, {
                    value: row.original,
                    size: 'small',
                    type: 'text',
                    placeholder: '原文 / 正则表达式',
                    'onUpdate:value': (v) => { row.original = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('translation')) {
        cols.push({
            title: '译文',
            key: 'translation',
            resizable: true,
            minWidth: 140,
            render(row) {
                if (row.type === 'doNotTranslate') {
                    return h('span', { style: 'color: var(--text-3); font-size: 13px;' }, '\u2014');
                }
                return h(NInput, {
                    value: row.translation ?? '',
                    size: 'small',
                    type: 'text',
                    placeholder: '译文',
                    'onUpdate:value': (v) => { row.translation = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('category')) {
        cols.push({
            title: '分类',
            key: 'category',
            width: 100,
            render(row) {
                return h(NSelect, {
                    value: row.category || 'general',
                    size: 'small',
                    options: categoryOptions,
                    'onUpdate:value': (v) => { row.category = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('description')) {
        cols.push({
            title: '描述',
            key: 'description',
            resizable: true,
            minWidth: 120,
            render(row) {
                return h(NInput, {
                    value: row.description ?? '',
                    size: 'small',
                    type: 'text',
                    placeholder: '术语说明',
                    'onUpdate:value': (v) => { row.description = v || undefined; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('isRegex')) {
        cols.push({
            title() {
                return h(NPopover, { trigger: 'hover', placement: 'top' }, {
                    trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '正则'),
                    default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
                        h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '正则表达式匹配'),
                        h('div', '开启后，原文字段将作为正则表达式来匹配游戏文本。适用于需要模糊匹配的场景。'),
                        h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '注意：正则术语不参与术语审查验证。'),
                    ]),
                });
            },
            key: 'isRegex',
            width: 55,
            align: 'center',
            render(row) {
                return h(NSwitch, {
                    value: row.isRegex,
                    size: 'small',
                    'onUpdate:value': (v) => { row.isRegex = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('exactMatch')) {
        cols.push({
            title() {
                return h(NPopover, { trigger: 'hover', placement: 'top' }, {
                    trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '精确'),
                    default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
                        h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '精确匹配（词边界）'),
                        h('div', '开启后，仅匹配完整的词，不匹配包含该词的更长文本。'),
                        h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '例如：开启精确匹配后，术语 "fire" 不会匹配 "firefox"。'),
                    ]),
                });
            },
            key: 'exactMatch',
            width: 55,
            align: 'center',
            render(row) {
                return h(NSwitch, {
                    value: row.exactMatch,
                    size: 'small',
                    'onUpdate:value': (v) => { row.exactMatch = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('caseSensitive')) {
        cols.push({
            title() {
                return h(NPopover, { trigger: 'hover', placement: 'top' }, {
                    trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '大小写'),
                    default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
                        h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '大小写敏感'),
                        h('div', '开启后，匹配时区分大小写。'),
                        h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '例如：开启后，术语 "Light" 不会匹配 "light" 或 "LIGHT"。'),
                    ]),
                });
            },
            key: 'caseSensitive',
            width: 60,
            align: 'center',
            render(row) {
                return h(NSwitch, {
                    value: row.caseSensitive,
                    size: 'small',
                    'onUpdate:value': (v) => { row.caseSensitive = v; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('priority')) {
        cols.push({
            title: '优先级',
            key: 'priority',
            width: 80,
            render(row) {
                return h(NInputNumber, {
                    value: row.priority,
                    size: 'small',
                    min: 0,
                    max: 9999,
                    showButton: false,
                    placeholder: '0',
                    'onUpdate:value': (v) => { row.priority = v ?? 0; },
                });
            },
        });
    }
    if (visibleColumns.value.includes('actions')) {
        cols.push({
            title: '',
            key: 'actions',
            width: 40,
            render(row) {
                return h(NButton, {
                    size: 'tiny',
                    quaternary: true,
                    type: 'error',
                    onClick: () => {
                        const idx = entries.value.findIndex(e => e._id === row._id);
                        if (idx >= 0)
                            entries.value.splice(idx, 1);
                    },
                }, {
                    icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
                });
            },
        });
    }
    return cols;
});
// ── Lifecycle ──
onMounted(async () => {
    disableAutoSave();
    try {
        const [gameData, termsData] = await Promise.all([
            gamesApi.get(gameId),
            gamesApi.getTerms(gameId),
        ]);
        game.value = gameData;
        entries.value = toRows(termsData);
    }
    catch {
        message.error('加载失败');
    }
    finally {
        loading.value = false;
    }
    await nextTick();
    enableAutoSave();
});
// ── Actions ──
async function handleManualSave() {
    manualSaving.value = true;
    disableAutoSave();
    try {
        const valid = getValidEntries();
        await gamesApi.saveTerms(gameId, valid);
        entries.value = toRows(valid);
        await nextTick();
        message.success('保存成功');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '保存失败');
    }
    finally {
        manualSaving.value = false;
        enableAutoSave();
    }
}
function handleAddEntry() {
    if (!newOriginal.value.trim()) {
        message.warning('请输入原文');
        return;
    }
    if (entries.value.some(e => e.original === newOriginal.value)) {
        message.warning('该原文已存在');
        return;
    }
    entries.value.unshift({
        _id: nextId++,
        type: newType.value,
        original: newOriginal.value,
        translation: newType.value === 'doNotTranslate' ? undefined : newTranslation.value,
        category: newCategory.value,
        isRegex: false,
        caseSensitive: true,
        exactMatch: false,
        priority: 0,
    });
    newOriginal.value = '';
    newTranslation.value = '';
}
function handleClearAll() {
    if (entries.value.length === 0)
        return;
    dialog.warning({
        title: '清空术语库',
        content: `确定要清空全部 ${entries.value.length} 条术语吗？此操作不可撤销。`,
        positiveText: '清空',
        negativeText: '取消',
        onPositiveClick: () => {
            entries.value = [];
            message.success('已清空术语库');
        },
    });
}
// ── Import / Export ──
async function handleImportClick() {
    const path = await selectFile({
        title: '导入术语文件',
        filters: [{ label: '术语文件', extensions: ['.json', '.csv', '.tsv'] }],
    });
    if (!path)
        return;
    try {
        const { content: text, fileName } = await filesystemApi.readText(path);
        const ext = fileName.toLowerCase().split('.').pop() ?? '';
        let imported;
        if (ext === 'json') {
            imported = parseJsonImport(text);
        }
        else if (ext === 'csv' || ext === 'tsv') {
            imported = parseCsvImport(text, ext === 'tsv' ? '\t' : ',');
        }
        else {
            // Try JSON first, then CSV
            try {
                imported = parseJsonImport(text);
            }
            catch {
                imported = parseCsvImport(text, text.includes('\t') ? '\t' : ',');
            }
        }
        // Merge: skip duplicates
        const existingOriginals = new Set(entries.value.map(e => e.original));
        let added = 0;
        for (const entry of imported) {
            if (!existingOriginals.has(entry.original)) {
                entries.value.push({ ...entry, _id: nextId++ });
                existingOriginals.add(entry.original);
                added++;
            }
        }
        message.success(`导入完成: 新增 ${added} 条，跳过 ${imported.length - added} 条重复`);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '导入失败');
    }
}
function parseJsonImport(text) {
    let parsed;
    try {
        parsed = JSON.parse(text);
    }
    catch {
        throw new Error('文件格式错误：不是有效的 JSON');
    }
    if (!Array.isArray(parsed)) {
        throw new Error('文件格式错误：应为 JSON 数组');
    }
    const result = [];
    for (const item of parsed) {
        if (typeof item.original !== 'string' || !item.original.trim())
            continue;
        // Detect format
        if ('type' in item) {
            // New TermEntry format
            result.push({
                type: item.type === 'doNotTranslate' ? 'doNotTranslate' : 'translate',
                original: item.original,
                translation: typeof item.translation === 'string' ? item.translation : undefined,
                category: typeof item.category === 'string' ? item.category : undefined,
                description: typeof item.description === 'string' && item.description.trim() ? item.description : undefined,
                isRegex: typeof item.isRegex === 'boolean' ? item.isRegex : false,
                caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
                exactMatch: typeof item.exactMatch === 'boolean' ? item.exactMatch : false,
                priority: typeof item.priority === 'number' ? item.priority : 0,
            });
        }
        else if ('isRegex' in item) {
            // Old GlossaryEntry format
            result.push({
                type: 'translate',
                original: item.original,
                translation: typeof item.translation === 'string' ? item.translation : '',
                description: typeof item.description === 'string' && item.description.trim() ? item.description : undefined,
                isRegex: typeof item.isRegex === 'boolean' ? item.isRegex : false,
                caseSensitive: true,
                exactMatch: false,
                priority: 0,
            });
        }
        else if ('caseSensitive' in item) {
            // Old DoNotTranslateEntry format
            result.push({
                type: 'doNotTranslate',
                original: item.original,
                isRegex: false,
                caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
                exactMatch: false,
                priority: 0,
            });
        }
        else {
            // Unknown format, default to translate
            result.push({
                type: 'translate',
                original: item.original,
                translation: typeof item.translation === 'string' ? item.translation : '',
                isRegex: false,
                caseSensitive: true,
                exactMatch: false,
                priority: 0,
            });
        }
    }
    return result;
}
function parseCsvImport(text, delimiter) {
    const lines = text.trim().split('\n').map(l => l.trim()).filter(Boolean);
    if (lines.length < 2)
        throw new Error('CSV 文件至少需要表头和一行数据');
    const headers = lines[0].split(delimiter).map(h => h.trim().toLowerCase().replace(/^"(.*)"$/, '$1'));
    const result = [];
    for (let i = 1; i < lines.length; i++) {
        const values = parseCsvLine(lines[i], delimiter);
        const row = {};
        headers.forEach((h, idx) => { row[h] = values[idx] ?? ''; });
        const original = (row['original'] || row['原文'] || '').trim();
        if (!original)
            continue;
        const typeStr = (row['type'] || row['类型'] || '').trim().toLowerCase();
        const type = typeStr === 'donotranslate' || typeStr === 'donottranslate' || typeStr === '禁翻'
            ? 'doNotTranslate' : 'translate';
        result.push({
            type,
            original,
            translation: row['translation'] || row['译文'] || undefined,
            category: normalizeCategoryInput(row['category'] || row['分类'] || '') || undefined,
            description: (row['description'] || row['描述'] || '').trim() || undefined,
            isRegex: (row['isregex'] || row['正则'] || '').toLowerCase() === 'true',
            caseSensitive: (row['casesensitive'] || row['大小写敏感'] || 'true').toLowerCase() !== 'false',
            exactMatch: (row['exactmatch'] || row['精确匹配'] || '').toLowerCase() === 'true',
            priority: parseInt(row['priority'] || row['优先级'] || '0') || 0,
        });
    }
    return result;
}
function parseCsvLine(line, delimiter) {
    const result = [];
    let current = '';
    let inQuotes = false;
    for (let i = 0; i < line.length; i++) {
        const ch = line[i];
        if (inQuotes) {
            if (ch === '"') {
                if (i + 1 < line.length && line[i + 1] === '"') {
                    current += '"';
                    i++;
                }
                else {
                    inQuotes = false;
                }
            }
            else {
                current += ch;
            }
        }
        else {
            if (ch === '"') {
                inQuotes = true;
            }
            else if (ch === delimiter) {
                result.push(current);
                current = '';
            }
            else {
                current += ch;
            }
        }
    }
    result.push(current);
    return result;
}
function normalizeCategoryInput(val) {
    const v = val.trim().toLowerCase();
    if (!v)
        return undefined;
    const map = {
        character: 'character', '角色名': 'character', '角色': 'character',
        location: 'location', '地名': 'location', '地点': 'location',
        item: 'item', '物品名': 'item', '物品': 'item',
        skill: 'skill', '技能名': 'skill', '技能': 'skill',
        organization: 'organization', '组织名': 'organization', '组织': 'organization',
        general: 'general', '通用': 'general',
    };
    return map[v];
}
function handleExportJson() {
    const data = getValidEntries();
    if (data.length === 0) {
        message.warning('术语库为空，无法导出');
        return;
    }
    const json = JSON.stringify(data, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    downloadBlob(blob, `${safeGameName()}_术语库.json`);
}
function handleExportCsv() {
    const data = getValidEntries();
    if (data.length === 0) {
        message.warning('术语库为空，无法导出');
        return;
    }
    const headers = ['type', 'original', 'translation', 'category', 'description', 'isRegex', 'caseSensitive', 'exactMatch', 'priority'];
    const lines = [headers.join(',')];
    for (const entry of data) {
        lines.push(headers.map(h => {
            const val = String(entry[h] ?? '');
            return val.includes(',') || val.includes('\n') || val.includes('"')
                ? `"${val.replace(/"/g, '""')}"`
                : val;
        }).join(','));
    }
    const csv = lines.join('\n');
    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' });
    downloadBlob(blob, `${safeGameName()}_术语库.csv`);
}
function safeGameName() {
    return game.value?.name?.replace(/[\\/:*?"<>|]/g, '_') ?? 'terms';
}
function downloadBlob(blob, filename) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = filename;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
// ── Import from Game ──
async function handleOpenImportGameModal() {
    showImportGameModal.value = true;
    loadingGames.value = true;
    try {
        gameList.value = (await gamesApi.list()).filter(g => g.id !== gameId);
    }
    catch {
        message.error('获取游戏列表失败');
    }
    finally {
        loadingGames.value = false;
    }
}
async function handleImportFromGame(sourceId) {
    importingFromGame.value = true;
    try {
        const result = await gamesApi.importTermsFromGame(gameId, sourceId);
        // Reload terms after import
        const termsData = await gamesApi.getTerms(gameId);
        disableAutoSave();
        entries.value = toRows(termsData);
        await nextTick();
        enableAutoSave();
        message.success(`导入完成: 新增 ${result.added} 条，跳过 ${result.skipped} 条重复`);
        showImportGameModal.value = false;
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '导入失败');
    }
    finally {
        importingFromGame.value = false;
    }
}
// ── Filter helpers ──
function toggleTypeFilter(type) {
    filterType.value = filterType.value === type ? 'all' : type;
}
function toggleCategoryFilter(cat) {
    filterCategory.value = filterCategory.value === cat ? null : cat;
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['chip']} */ ;
/** @type {__VLS_StyleScopedClasses['chip']} */ ;
/** @type {__VLS_StyleScopedClasses['game-import-item']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
/** @type {__VLS_StyleScopedClasses['filter-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['filter-separator']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
/** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
/** @type {__VLS_StyleScopedClasses['chip']} */ ;
/** @type {__VLS_StyleScopedClasses['game-import-item']} */ ;
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
else if (__VLS_ctx.game) {
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
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.router.push(`/games/${__VLS_ctx.gameId}`);
                // @ts-ignore
                [loading, game, router, gameId,];
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
    (__VLS_ctx.game.name);
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
    /** @ts-ignore @type {typeof __VLS_components.MenuBookOutlined} */
    MenuBookOutlined;
    // @ts-ignore
    const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({}));
    const __VLS_24 = __VLS_23({}, ...__VLS_functionalComponentArgsRest(__VLS_23));
    // @ts-ignore
    [game,];
    var __VLS_19;
    if (__VLS_ctx.autoSaving) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "auto-save-badge" },
        });
        /** @type {__VLS_StyleScopedClasses['auto-save-badge']} */ ;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar-left" },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar-left']} */ ;
    let __VLS_27;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_28 = __VLS_asFunctionalComponent1(__VLS_27, new __VLS_27({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        secondary: true,
        disabled: (__VLS_ctx.entries.length === 0),
    }));
    const __VLS_29 = __VLS_28({
        ...{ 'onClick': {} },
        size: "small",
        type: "error",
        secondary: true,
        disabled: (__VLS_ctx.entries.length === 0),
    }, ...__VLS_functionalComponentArgsRest(__VLS_28));
    let __VLS_32;
    const __VLS_33 = ({ click: {} },
        { onClick: (__VLS_ctx.handleClearAll) });
    const { default: __VLS_34 } = __VLS_30.slots;
    {
        const { icon: __VLS_35 } = __VLS_30.slots;
        let __VLS_36;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_37 = __VLS_asFunctionalComponent1(__VLS_36, new __VLS_36({
            size: (16),
        }));
        const __VLS_38 = __VLS_37({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_37));
        const { default: __VLS_41 } = __VLS_39.slots;
        let __VLS_42;
        /** @ts-ignore @type {typeof __VLS_components.DeleteSweepOutlined} */
        DeleteSweepOutlined;
        // @ts-ignore
        const __VLS_43 = __VLS_asFunctionalComponent1(__VLS_42, new __VLS_42({}));
        const __VLS_44 = __VLS_43({}, ...__VLS_functionalComponentArgsRest(__VLS_43));
        // @ts-ignore
        [autoSaving, entries, handleClearAll,];
        var __VLS_39;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_30;
    var __VLS_31;
    let __VLS_47;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_48 = __VLS_asFunctionalComponent1(__VLS_47, new __VLS_47({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_49 = __VLS_48({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_48));
    let __VLS_52;
    const __VLS_53 = ({ click: {} },
        { onClick: (__VLS_ctx.handleImportClick) });
    const { default: __VLS_54 } = __VLS_50.slots;
    {
        const { icon: __VLS_55 } = __VLS_50.slots;
        let __VLS_56;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_57 = __VLS_asFunctionalComponent1(__VLS_56, new __VLS_56({
            size: (16),
        }));
        const __VLS_58 = __VLS_57({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_57));
        const { default: __VLS_61 } = __VLS_59.slots;
        let __VLS_62;
        /** @ts-ignore @type {typeof __VLS_components.FileUploadOutlined} */
        FileUploadOutlined;
        // @ts-ignore
        const __VLS_63 = __VLS_asFunctionalComponent1(__VLS_62, new __VLS_62({}));
        const __VLS_64 = __VLS_63({}, ...__VLS_functionalComponentArgsRest(__VLS_63));
        // @ts-ignore
        [handleImportClick,];
        var __VLS_59;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_50;
    var __VLS_51;
    let __VLS_67;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_68 = __VLS_asFunctionalComponent1(__VLS_67, new __VLS_67({
        trigger: "click",
        placement: "bottom-start",
    }));
    const __VLS_69 = __VLS_68({
        trigger: "click",
        placement: "bottom-start",
    }, ...__VLS_functionalComponentArgsRest(__VLS_68));
    const { default: __VLS_72 } = __VLS_70.slots;
    {
        const { trigger: __VLS_73 } = __VLS_70.slots;
        let __VLS_74;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_75 = __VLS_asFunctionalComponent1(__VLS_74, new __VLS_74({
            size: "small",
            disabled: (__VLS_ctx.entries.length === 0),
        }));
        const __VLS_76 = __VLS_75({
            size: "small",
            disabled: (__VLS_ctx.entries.length === 0),
        }, ...__VLS_functionalComponentArgsRest(__VLS_75));
        const { default: __VLS_79 } = __VLS_77.slots;
        {
            const { icon: __VLS_80 } = __VLS_77.slots;
            let __VLS_81;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_82 = __VLS_asFunctionalComponent1(__VLS_81, new __VLS_81({
                size: (16),
            }));
            const __VLS_83 = __VLS_82({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_82));
            const { default: __VLS_86 } = __VLS_84.slots;
            let __VLS_87;
            /** @ts-ignore @type {typeof __VLS_components.FileDownloadOutlined} */
            FileDownloadOutlined;
            // @ts-ignore
            const __VLS_88 = __VLS_asFunctionalComponent1(__VLS_87, new __VLS_87({}));
            const __VLS_89 = __VLS_88({}, ...__VLS_functionalComponentArgsRest(__VLS_88));
            // @ts-ignore
            [entries,];
            var __VLS_84;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_77;
        // @ts-ignore
        [];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "export-options" },
    });
    /** @type {__VLS_StyleScopedClasses['export-options']} */ ;
    let __VLS_92;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_93 = __VLS_asFunctionalComponent1(__VLS_92, new __VLS_92({
        ...{ 'onClick': {} },
        size: "small",
        text: true,
    }));
    const __VLS_94 = __VLS_93({
        ...{ 'onClick': {} },
        size: "small",
        text: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_93));
    let __VLS_97;
    const __VLS_98 = ({ click: {} },
        { onClick: (__VLS_ctx.handleExportJson) });
    const { default: __VLS_99 } = __VLS_95.slots;
    // @ts-ignore
    [handleExportJson,];
    var __VLS_95;
    var __VLS_96;
    let __VLS_100;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_101 = __VLS_asFunctionalComponent1(__VLS_100, new __VLS_100({
        ...{ 'onClick': {} },
        size: "small",
        text: true,
    }));
    const __VLS_102 = __VLS_101({
        ...{ 'onClick': {} },
        size: "small",
        text: true,
    }, ...__VLS_functionalComponentArgsRest(__VLS_101));
    let __VLS_105;
    const __VLS_106 = ({ click: {} },
        { onClick: (__VLS_ctx.handleExportCsv) });
    const { default: __VLS_107 } = __VLS_103.slots;
    // @ts-ignore
    [handleExportCsv,];
    var __VLS_103;
    var __VLS_104;
    // @ts-ignore
    [];
    var __VLS_70;
    let __VLS_108;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_109 = __VLS_asFunctionalComponent1(__VLS_108, new __VLS_108({
        ...{ 'onClick': {} },
        size: "small",
    }));
    const __VLS_110 = __VLS_109({
        ...{ 'onClick': {} },
        size: "small",
    }, ...__VLS_functionalComponentArgsRest(__VLS_109));
    let __VLS_113;
    const __VLS_114 = ({ click: {} },
        { onClick: (__VLS_ctx.handleOpenImportGameModal) });
    const { default: __VLS_115 } = __VLS_111.slots;
    {
        const { icon: __VLS_116 } = __VLS_111.slots;
        let __VLS_117;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_118 = __VLS_asFunctionalComponent1(__VLS_117, new __VLS_117({
            size: (16),
        }));
        const __VLS_119 = __VLS_118({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_118));
        const { default: __VLS_122 } = __VLS_120.slots;
        let __VLS_123;
        /** @ts-ignore @type {typeof __VLS_components.ContentCopyOutlined} */
        ContentCopyOutlined;
        // @ts-ignore
        const __VLS_124 = __VLS_asFunctionalComponent1(__VLS_123, new __VLS_123({}));
        const __VLS_125 = __VLS_124({}, ...__VLS_functionalComponentArgsRest(__VLS_124));
        // @ts-ignore
        [handleOpenImportGameModal,];
        var __VLS_120;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_111;
    var __VLS_112;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "toolbar-right" },
    });
    /** @type {__VLS_StyleScopedClasses['toolbar-right']} */ ;
    let __VLS_128;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_129 = __VLS_asFunctionalComponent1(__VLS_128, new __VLS_128({
        trigger: "click",
        placement: "bottom-end",
    }));
    const __VLS_130 = __VLS_129({
        trigger: "click",
        placement: "bottom-end",
    }, ...__VLS_functionalComponentArgsRest(__VLS_129));
    const { default: __VLS_133 } = __VLS_131.slots;
    {
        const { trigger: __VLS_134 } = __VLS_131.slots;
        let __VLS_135;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_136 = __VLS_asFunctionalComponent1(__VLS_135, new __VLS_135({
            size: "small",
            quaternary: true,
        }));
        const __VLS_137 = __VLS_136({
            size: "small",
            quaternary: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_136));
        const { default: __VLS_140 } = __VLS_138.slots;
        {
            const { icon: __VLS_141 } = __VLS_138.slots;
            let __VLS_142;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_143 = __VLS_asFunctionalComponent1(__VLS_142, new __VLS_142({
                size: (16),
            }));
            const __VLS_144 = __VLS_143({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_143));
            const { default: __VLS_147 } = __VLS_145.slots;
            let __VLS_148;
            /** @ts-ignore @type {typeof __VLS_components.SettingsOutlined} */
            SettingsOutlined;
            // @ts-ignore
            const __VLS_149 = __VLS_asFunctionalComponent1(__VLS_148, new __VLS_148({}));
            const __VLS_150 = __VLS_149({}, ...__VLS_functionalComponentArgsRest(__VLS_149));
            // @ts-ignore
            [];
            var __VLS_145;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_138;
        // @ts-ignore
        [];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "column-visibility" },
    });
    /** @type {__VLS_StyleScopedClasses['column-visibility']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "column-visibility-title" },
    });
    /** @type {__VLS_StyleScopedClasses['column-visibility-title']} */ ;
    for (const [col] of __VLS_vFor((__VLS_ctx.optionalColumns))) {
        let __VLS_153;
        /** @ts-ignore @type {typeof __VLS_components.NCheckbox | typeof __VLS_components.NCheckbox} */
        NCheckbox;
        // @ts-ignore
        const __VLS_154 = __VLS_asFunctionalComponent1(__VLS_153, new __VLS_153({
            ...{ 'onUpdate:checked': {} },
            key: (col.key),
            checked: (__VLS_ctx.visibleColumns.includes(col.key)),
        }));
        const __VLS_155 = __VLS_154({
            ...{ 'onUpdate:checked': {} },
            key: (col.key),
            checked: (__VLS_ctx.visibleColumns.includes(col.key)),
        }, ...__VLS_functionalComponentArgsRest(__VLS_154));
        let __VLS_158;
        const __VLS_159 = ({ 'update:checked': {} },
            { 'onUpdate:checked': ((v) => {
                    if (v)
                        __VLS_ctx.visibleColumns.push(col.key);
                    else
                        __VLS_ctx.visibleColumns = __VLS_ctx.visibleColumns.filter(c => c !== col.key);
                    __VLS_ctx.saveColumnVisibility();
                }) });
        const { default: __VLS_160 } = __VLS_156.slots;
        (col.label);
        // @ts-ignore
        [optionalColumns, visibleColumns, visibleColumns, visibleColumns, visibleColumns, saveColumnVisibility,];
        var __VLS_156;
        var __VLS_157;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_131;
    let __VLS_161;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_162 = __VLS_asFunctionalComponent1(__VLS_161, new __VLS_161({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.manualSaving),
    }));
    const __VLS_163 = __VLS_162({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.manualSaving),
    }, ...__VLS_functionalComponentArgsRest(__VLS_162));
    let __VLS_166;
    const __VLS_167 = ({ click: {} },
        { onClick: (__VLS_ctx.handleManualSave) });
    const { default: __VLS_168 } = __VLS_164.slots;
    {
        const { icon: __VLS_169 } = __VLS_164.slots;
        let __VLS_170;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({
            size: (16),
        }));
        const __VLS_172 = __VLS_171({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_171));
        const { default: __VLS_175 } = __VLS_173.slots;
        let __VLS_176;
        /** @ts-ignore @type {typeof __VLS_components.SaveOutlined} */
        SaveOutlined;
        // @ts-ignore
        const __VLS_177 = __VLS_asFunctionalComponent1(__VLS_176, new __VLS_176({}));
        const __VLS_178 = __VLS_177({}, ...__VLS_functionalComponentArgsRest(__VLS_177));
        // @ts-ignore
        [manualSaving, handleManualSave,];
        var __VLS_173;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_164;
    var __VLS_165;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "filter-bar" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['filter-bar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "chip-group" },
    });
    /** @type {__VLS_StyleScopedClasses['chip-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.filterType = 'all';
                // @ts-ignore
                [filterType,];
            } },
        ...{ class: "chip" },
        ...{ class: ({ active: __VLS_ctx.filterType === 'all' }) },
    });
    /** @type {__VLS_StyleScopedClasses['chip']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    (__VLS_ctx.typeCounts.all);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.toggleTypeFilter('translate');
                // @ts-ignore
                [filterType, typeCounts, toggleTypeFilter,];
            } },
        ...{ class: "chip" },
        ...{ class: ({ active: __VLS_ctx.filterType === 'translate' }) },
    });
    /** @type {__VLS_StyleScopedClasses['chip']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    (__VLS_ctx.typeCounts.translate);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ onClick: (...[$event]) => {
                if (!!(__VLS_ctx.loading))
                    return;
                if (!(__VLS_ctx.game))
                    return;
                __VLS_ctx.toggleTypeFilter('doNotTranslate');
                // @ts-ignore
                [filterType, typeCounts, toggleTypeFilter,];
            } },
        ...{ class: "chip" },
        ...{ class: ({ active: __VLS_ctx.filterType === 'doNotTranslate' }) },
    });
    /** @type {__VLS_StyleScopedClasses['chip']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    (__VLS_ctx.typeCounts.dnt);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
        ...{ class: "filter-separator" },
    });
    /** @type {__VLS_StyleScopedClasses['filter-separator']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "chip-group" },
    });
    /** @type {__VLS_StyleScopedClasses['chip-group']} */ ;
    for (const [cat] of __VLS_vFor(['character', 'location', 'item', 'skill', 'organization', 'general'])) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    __VLS_ctx.toggleCategoryFilter(cat);
                    // @ts-ignore
                    [filterType, typeCounts, toggleCategoryFilter,];
                } },
            key: (cat),
            ...{ class: "chip" },
            ...{ class: ({ active: __VLS_ctx.filterCategory === cat }) },
        });
        /** @type {__VLS_StyleScopedClasses['chip']} */ ;
        /** @type {__VLS_StyleScopedClasses['active']} */ ;
        (__VLS_ctx.categoryLabelMap[cat]);
        (__VLS_ctx.categoryCounts[cat]);
        // @ts-ignore
        [filterCategory, categoryLabelMap, categoryCounts,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    let __VLS_181;
    /** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
    NInput;
    // @ts-ignore
    const __VLS_182 = __VLS_asFunctionalComponent1(__VLS_181, new __VLS_181({
        value: (__VLS_ctx.searchKeyword),
        placeholder: "搜索原文、译文或描述...",
        clearable: true,
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_183 = __VLS_182({
        value: (__VLS_ctx.searchKeyword),
        placeholder: "搜索原文、译文或描述...",
        clearable: true,
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_182));
    const { default: __VLS_186 } = __VLS_184.slots;
    {
        const { prefix: __VLS_187 } = __VLS_184.slots;
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
        /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
        SearchOutlined;
        // @ts-ignore
        const __VLS_195 = __VLS_asFunctionalComponent1(__VLS_194, new __VLS_194({}));
        const __VLS_196 = __VLS_195({}, ...__VLS_functionalComponentArgsRest(__VLS_195));
        // @ts-ignore
        [searchKeyword,];
        var __VLS_191;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_184;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "add-entry-row" },
    });
    /** @type {__VLS_StyleScopedClasses['add-entry-row']} */ ;
    let __VLS_199;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_200 = __VLS_asFunctionalComponent1(__VLS_199, new __VLS_199({
        value: (__VLS_ctx.newType),
        options: (__VLS_ctx.typeOptions),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_201 = __VLS_200({
        value: (__VLS_ctx.newType),
        options: (__VLS_ctx.typeOptions),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_200));
    let __VLS_204;
    /** @ts-ignore @type {typeof __VLS_components.NInput} */
    NInput;
    // @ts-ignore
    const __VLS_205 = __VLS_asFunctionalComponent1(__VLS_204, new __VLS_204({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.newOriginal),
        placeholder: "原文",
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_206 = __VLS_205({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.newOriginal),
        placeholder: "原文",
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_205));
    let __VLS_209;
    const __VLS_210 = ({ keyup: {} },
        { onKeyup: (__VLS_ctx.handleAddEntry) });
    var __VLS_207;
    var __VLS_208;
    let __VLS_211;
    /** @ts-ignore @type {typeof __VLS_components.NInput} */
    NInput;
    // @ts-ignore
    const __VLS_212 = __VLS_asFunctionalComponent1(__VLS_211, new __VLS_211({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.newTranslation),
        placeholder: (__VLS_ctx.newType === 'doNotTranslate' ? '—' : '译文'),
        disabled: (__VLS_ctx.newType === 'doNotTranslate'),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_213 = __VLS_212({
        ...{ 'onKeyup': {} },
        value: (__VLS_ctx.newTranslation),
        placeholder: (__VLS_ctx.newType === 'doNotTranslate' ? '—' : '译文'),
        disabled: (__VLS_ctx.newType === 'doNotTranslate'),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_212));
    let __VLS_216;
    const __VLS_217 = ({ keyup: {} },
        { onKeyup: (__VLS_ctx.handleAddEntry) });
    var __VLS_214;
    var __VLS_215;
    let __VLS_218;
    /** @ts-ignore @type {typeof __VLS_components.NSelect} */
    NSelect;
    // @ts-ignore
    const __VLS_219 = __VLS_asFunctionalComponent1(__VLS_218, new __VLS_218({
        value: (__VLS_ctx.newCategory),
        options: (__VLS_ctx.categoryOptions),
        size: "small",
        ...{ style: {} },
    }));
    const __VLS_220 = __VLS_219({
        value: (__VLS_ctx.newCategory),
        options: (__VLS_ctx.categoryOptions),
        size: "small",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_219));
    let __VLS_223;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_224 = __VLS_asFunctionalComponent1(__VLS_223, new __VLS_223({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        disabled: (!__VLS_ctx.newOriginal.trim()),
    }));
    const __VLS_225 = __VLS_224({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        disabled: (!__VLS_ctx.newOriginal.trim()),
    }, ...__VLS_functionalComponentArgsRest(__VLS_224));
    let __VLS_228;
    const __VLS_229 = ({ click: {} },
        { onClick: (__VLS_ctx.handleAddEntry) });
    const { default: __VLS_230 } = __VLS_226.slots;
    {
        const { icon: __VLS_231 } = __VLS_226.slots;
        let __VLS_232;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_233 = __VLS_asFunctionalComponent1(__VLS_232, new __VLS_232({
            size: (16),
        }));
        const __VLS_234 = __VLS_233({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_233));
        const { default: __VLS_237 } = __VLS_235.slots;
        let __VLS_238;
        /** @ts-ignore @type {typeof __VLS_components.AddOutlined} */
        AddOutlined;
        // @ts-ignore
        const __VLS_239 = __VLS_asFunctionalComponent1(__VLS_238, new __VLS_238({}));
        const __VLS_240 = __VLS_239({}, ...__VLS_functionalComponentArgsRest(__VLS_239));
        // @ts-ignore
        [newType, newType, newType, typeOptions, newOriginal, newOriginal, handleAddEntry, handleAddEntry, handleAddEntry, newTranslation, newCategory, categoryOptions,];
        var __VLS_235;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_226;
    var __VLS_227;
    if (__VLS_ctx.filteredEntries.length > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "table-container" },
        });
        /** @type {__VLS_StyleScopedClasses['table-container']} */ ;
        let __VLS_243;
        /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
        NDataTable;
        // @ts-ignore
        const __VLS_244 = __VLS_asFunctionalComponent1(__VLS_243, new __VLS_243({
            columns: (__VLS_ctx.tableColumns),
            data: (__VLS_ctx.filteredEntries),
            maxHeight: (560),
            itemSize: (40),
            rowKey: ((row) => row._id),
            virtualScroll: true,
            size: "small",
            striped: true,
        }));
        const __VLS_245 = __VLS_244({
            columns: (__VLS_ctx.tableColumns),
            data: (__VLS_ctx.filteredEntries),
            maxHeight: (560),
            itemSize: (40),
            rowKey: ((row) => row._id),
            virtualScroll: true,
            size: "small",
            striped: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_244));
    }
    else if (__VLS_ctx.entries.length > 0) {
        let __VLS_248;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_249 = __VLS_asFunctionalComponent1(__VLS_248, new __VLS_248({
            description: "没有匹配的术语",
            ...{ style: {} },
        }));
        const __VLS_250 = __VLS_249({
            description: "没有匹配的术语",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_249));
    }
    else {
        let __VLS_253;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_254 = __VLS_asFunctionalComponent1(__VLS_253, new __VLS_253({
            description: "暂无术语条目，点击添加或导入",
            ...{ style: {} },
        }));
        const __VLS_255 = __VLS_254({
            description: "暂无术语条目，点击添加或导入",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_254));
    }
    let __VLS_258;
    /** @ts-ignore @type {typeof __VLS_components.NModal | typeof __VLS_components.NModal} */
    NModal;
    // @ts-ignore
    const __VLS_259 = __VLS_asFunctionalComponent1(__VLS_258, new __VLS_258({
        show: (__VLS_ctx.showImportGameModal),
        preset: "card",
        title: "从其他游戏导入术语",
        ...{ style: {} },
    }));
    const __VLS_260 = __VLS_259({
        show: (__VLS_ctx.showImportGameModal),
        preset: "card",
        title: "从其他游戏导入术语",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_259));
    const { default: __VLS_263 } = __VLS_261.slots;
    if (__VLS_ctx.loadingGames) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "loading-state" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['loading-state']} */ ;
        let __VLS_264;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_265 = __VLS_asFunctionalComponent1(__VLS_264, new __VLS_264({
            size: "medium",
        }));
        const __VLS_266 = __VLS_265({
            size: "medium",
        }, ...__VLS_functionalComponentArgsRest(__VLS_265));
    }
    else if (__VLS_ctx.gameList.length === 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ style: {} },
        });
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "game-import-list" },
        });
        /** @type {__VLS_StyleScopedClasses['game-import-list']} */ ;
        for (const [g] of __VLS_vFor((__VLS_ctx.gameList))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!!(__VLS_ctx.loadingGames))
                            return;
                        if (!!(__VLS_ctx.gameList.length === 0))
                            return;
                        __VLS_ctx.handleImportFromGame(g.id);
                        // @ts-ignore
                        [entries, filteredEntries, filteredEntries, tableColumns, showImportGameModal, loadingGames, gameList, gameList, handleImportFromGame,];
                    } },
                key: (g.id),
                ...{ class: "game-import-item" },
            });
            /** @type {__VLS_StyleScopedClasses['game-import-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "game-import-name" },
            });
            /** @type {__VLS_StyleScopedClasses['game-import-name']} */ ;
            (g.name);
            let __VLS_269;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_270 = __VLS_asFunctionalComponent1(__VLS_269, new __VLS_269({
                size: "tiny",
                loading: (__VLS_ctx.importingFromGame),
                disabled: (__VLS_ctx.importingFromGame),
            }));
            const __VLS_271 = __VLS_270({
                size: "tiny",
                loading: (__VLS_ctx.importingFromGame),
                disabled: (__VLS_ctx.importingFromGame),
            }, ...__VLS_functionalComponentArgsRest(__VLS_270));
            const { default: __VLS_274 } = __VLS_272.slots;
            // @ts-ignore
            [importingFromGame, importingFromGame,];
            var __VLS_272;
            // @ts-ignore
            [];
        }
    }
    // @ts-ignore
    [];
    var __VLS_261;
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
