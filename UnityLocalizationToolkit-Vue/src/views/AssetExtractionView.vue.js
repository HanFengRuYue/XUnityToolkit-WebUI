import { ref, reactive, computed, onMounted, onBeforeUnmount, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { NButton, NIcon, NProgress, NAlert, NInput, NDataTable, NTag, NSpin, NEmpty, NSelect, NSwitch, NModal, NCheckbox, useMessage, } from 'naive-ui';
import { ArrowBackOutlined, SearchOutlined, TranslateOutlined, LanguageOutlined, DescriptionOutlined, PlayArrowFilled, StopOutlined, DeleteOutlined, RefreshOutlined, DataObjectOutlined, ExpandMoreOutlined, CheckCircleOutlined, } from '@vicons/material';
import { LockClosedOutline } from '@vicons/ionicons5';
import { useAssetExtractionStore } from '@/stores/assetExtraction';
import { gamesApi, settingsApi, scriptTagApi, termCandidatesApi } from '@/api/games';
const route = useRoute();
const router = useRouter();
const message = useMessage();
const store = useAssetExtractionStore();
const gameId = route.params['id'];
const game = ref(null);
const loading = ref(true);
const searchKeyword = ref('');
const fromLang = ref('ja');
const toLang = ref('zh');
const hasAiProvider = ref(false);
const enablePreTranslationCache = ref(true);
const enableLlmPatternAnalysis = ref(false);
const enableMultiRoundTranslation = ref(false);
const enableAutoTermExtraction = ref(false);
const autoApplyExtractedTerms = ref(false);
const collapsed = reactive({
    scriptTags: true,
});
// Script tag cleaning
const scriptTagRules = ref([]);
const scriptTagPresetVersion = ref(0);
const scriptTagSaving = ref(false);
const scriptTagDirty = ref(false);
const actionOptions = [
    { label: 'Extract (提取文本)', value: 'Extract' },
    { label: 'Exclude (排除)', value: 'Exclude' },
];
// ── Script tag cleaning ──
async function loadScriptTags() {
    try {
        const res = await scriptTagApi.get(gameId);
        scriptTagRules.value = res.rules;
        scriptTagPresetVersion.value = res.presetVersion;
        scriptTagDirty.value = false;
    }
    catch {
        // Script tag loading failed — non-critical, continue with empty rules
    }
}
async function handleSaveScriptTags() {
    scriptTagSaving.value = true;
    try {
        const config = {
            presetVersion: scriptTagPresetVersion.value,
            rules: scriptTagRules.value,
        };
        await scriptTagApi.save(gameId, config);
        scriptTagDirty.value = false;
        message.success('脚本指令规则已保存');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '保存失败');
    }
    finally {
        scriptTagSaving.value = false;
    }
}
async function importPresetRules() {
    try {
        const preset = await scriptTagApi.getPresets();
        scriptTagRules.value = scriptTagRules.value.filter(r => !r.isBuiltin);
        const builtinRules = preset.rules.map(r => ({
            pattern: r.pattern,
            action: r.action,
            description: r.description,
            isBuiltin: true,
        }));
        scriptTagRules.value.unshift(...builtinRules);
        scriptTagPresetVersion.value = preset.version;
        scriptTagDirty.value = true;
        message.success(`已导入 ${builtinRules.length} 条内置规则`);
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '导入失败');
    }
}
function addCustomRule() {
    scriptTagRules.value.push({
        pattern: '',
        action: 'Exclude',
        description: '',
        isBuiltin: false,
    });
    scriptTagDirty.value = true;
}
function removeScriptTagRule(index) {
    scriptTagRules.value.splice(index, 1);
    scriptTagDirty.value = true;
}
async function handleToggleCache(value) {
    try {
        const settings = await settingsApi.get();
        settings.aiTranslation.enablePreTranslationCache = value;
        await settingsApi.save(settings);
    }
    catch {
        message.error('保存缓存设置失败');
    }
}
async function handlePreTranslationSettingChange(field, value) {
    try {
        const settings = await settingsApi.get();
        settings.aiTranslation[field] = value;
        await settingsApi.save(settings);
    }
    catch {
        message.error('保存设置失败');
    }
}
const langOptions = [
    { label: '日语 (ja)', value: 'ja' },
    { label: '英语 (en)', value: 'en' },
    { label: '中文 (zh)', value: 'zh' },
    { label: '韩语 (ko)', value: 'ko' },
    { label: '俄语 (ru)', value: 'ru' },
];
// Term review
const showTermReview = ref(false);
const termCandidates = ref([]);
const termCandidateSelected = ref(new Set());
const termReviewLoading = ref(false);
// Phase tracking
const phases = [
    { key: 'round1', label: '第一轮翻译' },
    { key: 'patternAnalysis', label: '模式分析' },
    { key: 'termExtraction', label: '术语提取' },
    { key: 'termReview', label: '术语审核' },
    { key: 'round2', label: '第二轮润色' },
    { key: 'writeCache', label: '写入缓存' },
];
function getPhaseStatus(phaseKey) {
    const s = store.preTranslationStatus;
    if (!s || s.state === 'Idle')
        return 'pending';
    const currentPhase = s.currentPhase ?? '';
    const currentRound = s.currentRound ?? 1;
    const phaseOrder = phases.map(p => p.key);
    const currentIdx = phaseOrder.indexOf(currentPhase);
    const thisIdx = phaseOrder.indexOf(phaseKey);
    if (s.state === 'Completed')
        return 'done';
    if (currentIdx < 0) {
        // Fallback: infer from round
        if (phaseKey === 'round1' && currentRound === 1)
            return 'active';
        if (phaseKey === 'round2' && currentRound === 2)
            return 'active';
        return 'pending';
    }
    if (thisIdx < currentIdx)
        return 'done';
    if (thisIdx === currentIdx)
        return 'active';
    return 'pending';
}
const hasCheckpoint = computed(() => Boolean(store.preTranslationStatus?.checkpointUpdatedAt));
const hasResumableCheckpoint = computed(() => hasCheckpoint.value && store.preTranslationStatus?.canResume === true);
const hasBlockedCheckpoint = computed(() => hasCheckpoint.value
    && !hasResumableCheckpoint.value
    && Boolean(store.preTranslationStatus?.resumeBlockedReason));
const isPreTranslating = computed(() => store.preTranslationStatus?.state === 'Running'
    && !store.preTranslationStatus?.canResume);
const isAwaitingTermReview = computed(() => store.preTranslationStatus?.state === 'AwaitingTermReview'
    && !store.preTranslationStatus?.canResume);
const isPhaseWithBatchProgress = computed(() => {
    const phase = store.preTranslationStatus?.currentPhase;
    return (phase === 'patternAnalysis' || phase === 'termExtraction')
        && (store.preTranslationStatus?.phaseTotal ?? 0) > 0;
});
const preTranslationProgress = computed(() => {
    const s = store.preTranslationStatus;
    if (!s || s.totalTexts === 0)
        return 0;
    const phase = s.currentPhase;
    // For LLM analysis phases, use phaseProgress/phaseTotal
    if ((phase === 'patternAnalysis' || phase === 'termExtraction') && s.phaseTotal > 0) {
        return Math.round(s.phaseProgress / s.phaseTotal * 100);
    }
    // For writeCache, show indeterminate (99%) while active
    if (phase === 'writeCache' && s.state === 'Running') {
        return 99;
    }
    // For translation rounds, use translatedTexts/totalTexts
    return Math.round((s.translatedTexts + s.failedTexts) / s.totalTexts * 100);
});
const searchableTexts = computed(() => (store.extractionResult?.texts ?? []).map(text => ({
    ...text,
    _textLower: text.text.toLowerCase(),
    _sourceLower: text.source.toLowerCase(),
})));
const filteredTexts = computed(() => {
    const texts = searchableTexts.value;
    const kw = searchKeyword.value.trim().toLowerCase();
    if (!kw)
        return texts;
    return texts.filter(t => t._textLower.includes(kw) || t._sourceLower.includes(kw));
});
const tableColumns = [
    {
        title: '文本',
        key: 'text',
        ellipsis: { tooltip: true },
        resizable: true,
    },
    {
        title: '来源',
        key: 'source',
        width: 200,
        ellipsis: { tooltip: true },
    },
    {
        title: '资产文件',
        key: 'assetFile',
        width: 180,
        ellipsis: { tooltip: true },
    },
];
onMounted(async () => {
    try {
        game.value = await gamesApi.get(gameId);
        await store.loadCachedResult(gameId);
        if (store.extractionResult?.detectedLanguage) {
            fromLang.value = store.extractionResult.detectedLanguage;
        }
        await store.fetchPreTranslationStatus(gameId);
        if (store.preTranslationStatus?.fromLang) {
            fromLang.value = store.preTranslationStatus.fromLang;
        }
        if (store.preTranslationStatus?.toLang) {
            toLang.value = store.preTranslationStatus.toLang;
        }
        if (isPreTranslating.value || isAwaitingTermReview.value) {
            await store.connect(gameId);
        }
        // Check if AI providers are configured
        try {
            const settings = await settingsApi.get();
            const endpoints = settings.aiTranslation?.endpoints ?? [];
            hasAiProvider.value = endpoints.some(e => e.enabled && e.apiKey);
            enablePreTranslationCache.value = settings.aiTranslation?.enablePreTranslationCache ?? false;
            enableLlmPatternAnalysis.value = settings.aiTranslation?.enableLlmPatternAnalysis ?? false;
            enableMultiRoundTranslation.value = settings.aiTranslation?.enableMultiRoundTranslation ?? false;
            enableAutoTermExtraction.value = settings.aiTranslation?.enableAutoTermExtraction ?? false;
            autoApplyExtractedTerms.value = settings.aiTranslation?.autoApplyExtractedTerms ?? false;
        }
        catch { /* ignore */ }
        await loadScriptTags();
    }
    catch {
        message.error('加载失败');
    }
    finally {
        loading.value = false;
    }
});
watch(() => store.preTranslationStatus?.state, (newState) => {
    if (newState === 'AwaitingTermReview' && !store.preTranslationStatus?.canResume) {
        loadTermCandidates();
    }
});
watch(() => store.termExtractionComplete, async (val) => {
    if (!val)
        return;
    const status = store.preTranslationStatus;
    if (status?.state === 'AwaitingTermReview' && !status.canResume) {
        await loadTermCandidates();
    }
    store.resetTermExtractionComplete();
});
onBeforeUnmount(async () => {
    await store.disconnect();
});
async function handleExtract() {
    try {
        await store.extractAssets(gameId);
        if (store.extractionResult?.detectedLanguage) {
            fromLang.value = store.extractionResult.detectedLanguage;
        }
        message.success(`提取完成: ${store.extractionResult?.totalTextsExtracted ?? 0} 条文本`);
    }
    catch {
        message.error(store.extractError ?? '提取失败');
    }
}
async function handleStartPreTranslation() {
    try {
        await store.startPreTranslation(gameId, fromLang.value, toLang.value);
        message.info('预翻译已开始');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '启动预翻译失败');
    }
}
async function handleCancelPreTranslation() {
    try {
        await store.cancelPreTranslation(gameId);
        message.info('已取消');
    }
    catch {
        message.error('取消失败');
    }
}
/*
async function handleResumePreTranslation() {
  try {
    await store.resumePreTranslation(gameId)
    message.info('棰勭炕璇戝凡鎭㈠')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '鎭㈠棰勭炕璇戝け璐?)
  }
}

async function handleRestartPreTranslation() {
  try {
    await store.startPreTranslation(gameId, fromLang.value, toLang.value, true)
    message.info('棰勭炕璇戝凡閲嶆柊寮€濮?)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '閲嶆柊寮€濮嬮缈昏瘧澶辫触')
  }
}

*/
async function handleResumePreTranslation() {
    try {
        await store.resumePreTranslation(gameId);
        message.info('预翻译已恢复');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '恢复预翻译失败');
    }
}
async function handleRestartPreTranslation() {
    try {
        await store.startPreTranslation(gameId, fromLang.value, toLang.value, true);
        message.info('预翻译已重新开始');
    }
    catch (e) {
        message.error(e instanceof Error ? e.message : '重新开始预翻译失败');
    }
}
async function handleClearCache() {
    try {
        await store.clearCache(gameId);
        message.success('缓存已清理');
    }
    catch {
        message.error('清理失败');
    }
}
function openPretranslatedTextEditor() {
    router.push({
        name: 'translation-editor',
        params: { id: gameId },
        query: {
            source: 'pretranslated',
            lang: toLang.value,
        },
    });
}
function openPretranslatedRegexEditor() {
    router.push({
        name: 'translation-editor',
        params: { id: gameId },
        query: {
            source: 'pretranslated-regex',
            lang: toLang.value,
        },
    });
}
async function loadTermCandidates() {
    try {
        const result = await termCandidatesApi.get(gameId);
        termCandidates.value = result.candidates;
        termCandidateSelected.value = new Set(result.candidates.map(c => c.original));
        showTermReview.value = true;
    }
    catch {
        message.error('加载术语候选项失败');
    }
}
async function handleApplyTerms(originals) {
    termReviewLoading.value = true;
    try {
        await termCandidatesApi.apply(gameId, originals);
        showTermReview.value = false;
        message.success(originals === null ? '已应用全部术语' : originals.length > 0 ? `已应用 ${originals.length} 条术语` : '已跳过术语应用');
    }
    catch {
        message.error('应用术语失败');
    }
    finally {
        termReviewLoading.value = false;
    }
}
async function handleApplySelected() {
    const selected = Array.from(termCandidateSelected.value);
    await handleApplyTerms(selected);
}
async function handleSkipTerms() {
    await handleApplyTerms([]);
}
function toggleTermCandidate(original, checked) {
    if (checked) {
        termCandidateSelected.value.add(original);
    }
    else {
        termCandidateSelected.value.delete(original);
    }
    // Force reactivity
    termCandidateSelected.value = new Set(termCandidateSelected.value);
}
function langLabel(code) {
    const map = {
        ja: '日语', en: '英语', zh: '中文', ko: '韩语', ru: '俄语',
        fr: '法语', de: '德语', es: '西班牙语', pt: '葡萄牙语',
    };
    return map[code] ?? code;
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['stat-card']} */ ;
/** @type {__VLS_StyleScopedClasses['stat-card']} */ ;
/** @type {__VLS_StyleScopedClasses['accent']} */ ;
/** @type {__VLS_StyleScopedClasses['stat-value']} */ ;
/** @type {__VLS_StyleScopedClasses['lang-select']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-body']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['phase-step']} */ ;
/** @type {__VLS_StyleScopedClasses['phase-step']} */ ;
/** @type {__VLS_StyleScopedClasses['stat-cards']} */ ;
/** @type {__VLS_StyleScopedClasses['lang-row']} */ ;
/** @type {__VLS_StyleScopedClasses['lang-select']} */ ;
/** @type {__VLS_StyleScopedClasses['lang-arrow']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-header']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
/** @type {__VLS_StyleScopedClasses['rule-row']} */ ;
/** @type {__VLS_StyleScopedClasses['rule-action']} */ ;
/** @type {__VLS_StyleScopedClasses['checkpoint-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['checkpoint-action-button']} */ ;
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
    /** @ts-ignore @type {typeof __VLS_components.DataObjectOutlined} */
    DataObjectOutlined;
    // @ts-ignore
    const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({}));
    const __VLS_24 = __VLS_23({}, ...__VLS_functionalComponentArgsRest(__VLS_23));
    // @ts-ignore
    [game,];
    var __VLS_19;
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
    let __VLS_27;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_28 = __VLS_asFunctionalComponent1(__VLS_27, new __VLS_27({
        size: (16),
    }));
    const __VLS_29 = __VLS_28({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_28));
    const { default: __VLS_32 } = __VLS_30.slots;
    let __VLS_33;
    /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
    SearchOutlined;
    // @ts-ignore
    const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({}));
    const __VLS_35 = __VLS_34({}, ...__VLS_functionalComponentArgsRest(__VLS_34));
    // @ts-ignore
    [];
    var __VLS_30;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header-actions" },
    });
    /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
    if (__VLS_ctx.store.extractionResult) {
        let __VLS_38;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_39 = __VLS_asFunctionalComponent1(__VLS_38, new __VLS_38({
            ...{ 'onClick': {} },
            size: "small",
            tertiary: true,
            type: "error",
        }));
        const __VLS_40 = __VLS_39({
            ...{ 'onClick': {} },
            size: "small",
            tertiary: true,
            type: "error",
        }, ...__VLS_functionalComponentArgsRest(__VLS_39));
        let __VLS_43;
        const __VLS_44 = ({ click: {} },
            { onClick: (__VLS_ctx.handleClearCache) });
        const { default: __VLS_45 } = __VLS_41.slots;
        {
            const { icon: __VLS_46 } = __VLS_41.slots;
            let __VLS_47;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_48 = __VLS_asFunctionalComponent1(__VLS_47, new __VLS_47({
                size: (16),
            }));
            const __VLS_49 = __VLS_48({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_48));
            const { default: __VLS_52 } = __VLS_50.slots;
            let __VLS_53;
            /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
            DeleteOutlined;
            // @ts-ignore
            const __VLS_54 = __VLS_asFunctionalComponent1(__VLS_53, new __VLS_53({}));
            const __VLS_55 = __VLS_54({}, ...__VLS_functionalComponentArgsRest(__VLS_54));
            // @ts-ignore
            [store, handleClearCache,];
            var __VLS_50;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_41;
        var __VLS_42;
    }
    let __VLS_58;
    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
    NButton;
    // @ts-ignore
    const __VLS_59 = __VLS_asFunctionalComponent1(__VLS_58, new __VLS_58({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.store.extracting),
    }));
    const __VLS_60 = __VLS_59({
        ...{ 'onClick': {} },
        size: "small",
        type: "primary",
        loading: (__VLS_ctx.store.extracting),
    }, ...__VLS_functionalComponentArgsRest(__VLS_59));
    let __VLS_63;
    const __VLS_64 = ({ click: {} },
        { onClick: (__VLS_ctx.handleExtract) });
    const { default: __VLS_65 } = __VLS_61.slots;
    {
        const { icon: __VLS_66 } = __VLS_61.slots;
        let __VLS_67;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_68 = __VLS_asFunctionalComponent1(__VLS_67, new __VLS_67({
            size: (16),
        }));
        const __VLS_69 = __VLS_68({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_68));
        const { default: __VLS_72 } = __VLS_70.slots;
        let __VLS_73;
        /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
        RefreshOutlined;
        // @ts-ignore
        const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({}));
        const __VLS_75 = __VLS_74({}, ...__VLS_functionalComponentArgsRest(__VLS_74));
        // @ts-ignore
        [store, handleExtract,];
        var __VLS_70;
        // @ts-ignore
        [];
    }
    (__VLS_ctx.store.extractionResult ? '重新提取' : '开始提取');
    // @ts-ignore
    [store,];
    var __VLS_61;
    var __VLS_62;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "section-desc" },
    });
    /** @type {__VLS_StyleScopedClasses['section-desc']} */ ;
    if (__VLS_ctx.store.extractionResult) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "result-summary" },
        });
        /** @type {__VLS_StyleScopedClasses['result-summary']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "stat-cards" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-cards']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "stat-card" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-value" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-value']} */ ;
        (__VLS_ctx.store.extractionResult.totalTextsExtracted);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-label" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "stat-card" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-card']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-value" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-value']} */ ;
        (__VLS_ctx.store.extractionResult.totalAssetsScanned);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-label" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "stat-card accent" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-card']} */ ;
        /** @type {__VLS_StyleScopedClasses['accent']} */ ;
        let __VLS_78;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_79 = __VLS_asFunctionalComponent1(__VLS_78, new __VLS_78({
            size: (16),
            ...{ style: {} },
        }));
        const __VLS_80 = __VLS_79({
            size: (16),
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_79));
        const { default: __VLS_83 } = __VLS_81.slots;
        let __VLS_84;
        /** @ts-ignore @type {typeof __VLS_components.LanguageOutlined} */
        LanguageOutlined;
        // @ts-ignore
        const __VLS_85 = __VLS_asFunctionalComponent1(__VLS_84, new __VLS_84({}));
        const __VLS_86 = __VLS_85({}, ...__VLS_functionalComponentArgsRest(__VLS_85));
        // @ts-ignore
        [store, store, store,];
        var __VLS_81;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-value" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-value']} */ ;
        (__VLS_ctx.langLabel(__VLS_ctx.store.extractionResult.detectedLanguage ?? '?'));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "stat-label" },
        });
        /** @type {__VLS_StyleScopedClasses['stat-label']} */ ;
    }
    if (__VLS_ctx.store.extracting) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "extracting-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['extracting-hint']} */ ;
        let __VLS_89;
        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
        NSpin;
        // @ts-ignore
        const __VLS_90 = __VLS_asFunctionalComponent1(__VLS_89, new __VLS_89({
            size: "small",
        }));
        const __VLS_91 = __VLS_90({
            size: "small",
        }, ...__VLS_functionalComponentArgsRest(__VLS_90));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
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
    let __VLS_94;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_95 = __VLS_asFunctionalComponent1(__VLS_94, new __VLS_94({
        size: (16),
    }));
    const __VLS_96 = __VLS_95({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_95));
    const { default: __VLS_99 } = __VLS_97.slots;
    let __VLS_100;
    /** @ts-ignore @type {typeof __VLS_components.TranslateOutlined} */
    TranslateOutlined;
    // @ts-ignore
    const __VLS_101 = __VLS_asFunctionalComponent1(__VLS_100, new __VLS_100({}));
    const __VLS_102 = __VLS_101({}, ...__VLS_functionalComponentArgsRest(__VLS_101));
    // @ts-ignore
    [store, store, langLabel,];
    var __VLS_97;
    let __VLS_105;
    /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
    NTag;
    // @ts-ignore
    const __VLS_106 = __VLS_asFunctionalComponent1(__VLS_105, new __VLS_105({
        size: "small",
        type: "warning",
        ...{ style: {} },
    }));
    const __VLS_107 = __VLS_106({
        size: "small",
        type: "warning",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_106));
    const { default: __VLS_110 } = __VLS_108.slots;
    // @ts-ignore
    [];
    var __VLS_108;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "section-desc" },
    });
    /** @type {__VLS_StyleScopedClasses['section-desc']} */ ;
    let __VLS_111;
    /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
    NAlert;
    // @ts-ignore
    const __VLS_112 = __VLS_asFunctionalComponent1(__VLS_111, new __VLS_111({
        type: "warning",
        bordered: (false),
        ...{ style: {} },
    }));
    const __VLS_113 = __VLS_112({
        type: "warning",
        bordered: (false),
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_112));
    const { default: __VLS_116 } = __VLS_114.slots;
    // @ts-ignore
    [];
    var __VLS_114;
    if (!__VLS_ctx.store.extractionResult) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "empty-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-hint']} */ ;
        let __VLS_117;
        /** @ts-ignore @type {typeof __VLS_components.NEmpty} */
        NEmpty;
        // @ts-ignore
        const __VLS_118 = __VLS_asFunctionalComponent1(__VLS_117, new __VLS_117({
            description: "请先提取游戏资产",
        }));
        const __VLS_119 = __VLS_118({
            description: "请先提取游戏资产",
        }, ...__VLS_functionalComponentArgsRest(__VLS_118));
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "lang-row" },
        });
        /** @type {__VLS_StyleScopedClasses['lang-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "lang-select" },
        });
        /** @type {__VLS_StyleScopedClasses['lang-select']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({});
        let __VLS_122;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_123 = __VLS_asFunctionalComponent1(__VLS_122, new __VLS_122({
            value: (__VLS_ctx.fromLang),
            options: (__VLS_ctx.langOptions),
            size: "small",
            disabled: (__VLS_ctx.isPreTranslating),
        }));
        const __VLS_124 = __VLS_123({
            value: (__VLS_ctx.fromLang),
            options: (__VLS_ctx.langOptions),
            size: "small",
            disabled: (__VLS_ctx.isPreTranslating),
        }, ...__VLS_functionalComponentArgsRest(__VLS_123));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "lang-arrow" },
        });
        /** @type {__VLS_StyleScopedClasses['lang-arrow']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "lang-select" },
        });
        /** @type {__VLS_StyleScopedClasses['lang-select']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.label, __VLS_intrinsics.label)({});
        let __VLS_127;
        /** @ts-ignore @type {typeof __VLS_components.NSelect} */
        NSelect;
        // @ts-ignore
        const __VLS_128 = __VLS_asFunctionalComponent1(__VLS_127, new __VLS_127({
            value: (__VLS_ctx.toLang),
            options: (__VLS_ctx.langOptions),
            size: "small",
            disabled: (__VLS_ctx.isPreTranslating),
        }));
        const __VLS_129 = __VLS_128({
            value: (__VLS_ctx.toLang),
            options: (__VLS_ctx.langOptions),
            size: "small",
            disabled: (__VLS_ctx.isPreTranslating),
        }, ...__VLS_functionalComponentArgsRest(__VLS_128));
        if (!__VLS_ctx.hasAiProvider) {
            let __VLS_132;
            /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
            NAlert;
            // @ts-ignore
            const __VLS_133 = __VLS_asFunctionalComponent1(__VLS_132, new __VLS_132({
                type: "warning",
                ...{ style: {} },
            }));
            const __VLS_134 = __VLS_133({
                type: "warning",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_133));
            const { default: __VLS_137 } = __VLS_135.slots;
            let __VLS_138;
            /** @ts-ignore @type {typeof __VLS_components.routerLink | typeof __VLS_components.RouterLink | typeof __VLS_components.routerLink | typeof __VLS_components.RouterLink} */
            routerLink;
            // @ts-ignore
            const __VLS_139 = __VLS_asFunctionalComponent1(__VLS_138, new __VLS_138({
                to: "/ai-translation",
                ...{ style: {} },
            }));
            const __VLS_140 = __VLS_139({
                to: "/ai-translation",
                ...{ style: {} },
            }, ...__VLS_functionalComponentArgsRest(__VLS_139));
            const { default: __VLS_143 } = __VLS_141.slots;
            // @ts-ignore
            [store, fromLang, langOptions, langOptions, isPreTranslating, isPreTranslating, toLang, hasAiProvider,];
            var __VLS_141;
            // @ts-ignore
            [];
            var __VLS_135;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "pre-translation-settings" },
        });
        /** @type {__VLS_StyleScopedClasses['pre-translation-settings']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-row" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-info" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-label" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-description" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
        let __VLS_144;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_145 = __VLS_asFunctionalComponent1(__VLS_144, new __VLS_144({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableLlmPatternAnalysis),
        }));
        const __VLS_146 = __VLS_145({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableLlmPatternAnalysis),
        }, ...__VLS_functionalComponentArgsRest(__VLS_145));
        let __VLS_149;
        const __VLS_150 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.handlePreTranslationSettingChange('enableLlmPatternAnalysis', v)) });
        var __VLS_147;
        var __VLS_148;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-row" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-info" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-label" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-description" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
        let __VLS_151;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_152 = __VLS_asFunctionalComponent1(__VLS_151, new __VLS_151({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableMultiRoundTranslation),
        }));
        const __VLS_153 = __VLS_152({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableMultiRoundTranslation),
        }, ...__VLS_functionalComponentArgsRest(__VLS_152));
        let __VLS_156;
        const __VLS_157 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.handlePreTranslationSettingChange('enableMultiRoundTranslation', v)) });
        var __VLS_154;
        var __VLS_155;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-row" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-info" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-label" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-description" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
        let __VLS_158;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_159 = __VLS_asFunctionalComponent1(__VLS_158, new __VLS_158({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableAutoTermExtraction),
        }));
        const __VLS_160 = __VLS_159({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enableAutoTermExtraction),
        }, ...__VLS_functionalComponentArgsRest(__VLS_159));
        let __VLS_163;
        const __VLS_164 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => __VLS_ctx.handlePreTranslationSettingChange('enableAutoTermExtraction', v)) });
        var __VLS_161;
        var __VLS_162;
        if (__VLS_ctx.enableAutoTermExtraction) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "setting-row sub-setting" },
            });
            /** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
            /** @type {__VLS_StyleScopedClasses['sub-setting']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "setting-info" },
            });
            /** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "setting-label" },
            });
            /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "setting-description" },
            });
            /** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
            let __VLS_165;
            /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
            NSwitch;
            // @ts-ignore
            const __VLS_166 = __VLS_asFunctionalComponent1(__VLS_165, new __VLS_165({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.autoApplyExtractedTerms),
            }));
            const __VLS_167 = __VLS_166({
                ...{ 'onUpdate:value': {} },
                value: (__VLS_ctx.autoApplyExtractedTerms),
            }, ...__VLS_functionalComponentArgsRest(__VLS_166));
            let __VLS_170;
            const __VLS_171 = ({ 'update:value': {} },
                { 'onUpdate:value': ((v) => __VLS_ctx.handlePreTranslationSettingChange('autoApplyExtractedTerms', v)) });
            var __VLS_168;
            var __VLS_169;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-row" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "setting-info" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-info']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-label" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-description" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-description']} */ ;
        let __VLS_172;
        /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
        NSwitch;
        // @ts-ignore
        const __VLS_173 = __VLS_asFunctionalComponent1(__VLS_172, new __VLS_172({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enablePreTranslationCache),
        }));
        const __VLS_174 = __VLS_173({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.enablePreTranslationCache),
        }, ...__VLS_functionalComponentArgsRest(__VLS_173));
        let __VLS_177;
        const __VLS_178 = ({ 'update:value': {} },
            { 'onUpdate:value': (__VLS_ctx.handleToggleCache) });
        var __VLS_175;
        var __VLS_176;
        if (__VLS_ctx.enablePreTranslationCache) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "script-tag-card" },
                ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.scriptTags }) },
            });
            /** @type {__VLS_StyleScopedClasses['script-tag-card']} */ ;
            /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.loading))
                            return;
                        if (!(__VLS_ctx.game))
                            return;
                        if (!!(!__VLS_ctx.store.extractionResult))
                            return;
                        if (!(__VLS_ctx.enablePreTranslationCache))
                            return;
                        __VLS_ctx.collapsed.scriptTags = !__VLS_ctx.collapsed.scriptTags;
                        // @ts-ignore
                        [enableLlmPatternAnalysis, handlePreTranslationSettingChange, handlePreTranslationSettingChange, handlePreTranslationSettingChange, handlePreTranslationSettingChange, enableMultiRoundTranslation, enableAutoTermExtraction, enableAutoTermExtraction, autoApplyExtractedTerms, enablePreTranslationCache, enablePreTranslationCache, handleToggleCache, collapsed, collapsed, collapsed,];
                    } },
                ...{ class: "section-header collapsible" },
            });
            /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
            /** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
                ...{ class: "section-title" },
            });
            /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "section-icon" },
            });
            /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
            let __VLS_179;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_180 = __VLS_asFunctionalComponent1(__VLS_179, new __VLS_179({
                size: (16),
            }));
            const __VLS_181 = __VLS_180({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_180));
            const { default: __VLS_184 } = __VLS_182.slots;
            let __VLS_185;
            /** @ts-ignore @type {typeof __VLS_components.DataObjectOutlined} */
            DataObjectOutlined;
            // @ts-ignore
            const __VLS_186 = __VLS_asFunctionalComponent1(__VLS_185, new __VLS_185({}));
            const __VLS_187 = __VLS_186({}, ...__VLS_functionalComponentArgsRest(__VLS_186));
            // @ts-ignore
            [];
            var __VLS_182;
            if (__VLS_ctx.scriptTagRules.length > 0) {
                let __VLS_190;
                /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
                NTag;
                // @ts-ignore
                const __VLS_191 = __VLS_asFunctionalComponent1(__VLS_190, new __VLS_190({
                    size: "small",
                    bordered: (false),
                    ...{ style: {} },
                }));
                const __VLS_192 = __VLS_191({
                    size: "small",
                    bordered: (false),
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_191));
                const { default: __VLS_195 } = __VLS_193.slots;
                (__VLS_ctx.scriptTagRules.length);
                // @ts-ignore
                [scriptTagRules, scriptTagRules,];
                var __VLS_193;
            }
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ onClick: () => { } },
                ...{ class: "header-actions" },
            });
            /** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
            let __VLS_196;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_197 = __VLS_asFunctionalComponent1(__VLS_196, new __VLS_196({
                ...{ 'onClick': {} },
                size: "small",
            }));
            const __VLS_198 = __VLS_197({
                ...{ 'onClick': {} },
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_197));
            let __VLS_201;
            const __VLS_202 = ({ click: {} },
                { onClick: (__VLS_ctx.importPresetRules) });
            const { default: __VLS_203 } = __VLS_199.slots;
            // @ts-ignore
            [importPresetRules,];
            var __VLS_199;
            var __VLS_200;
            let __VLS_204;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_205 = __VLS_asFunctionalComponent1(__VLS_204, new __VLS_204({
                ...{ 'onClick': {} },
                size: "small",
            }));
            const __VLS_206 = __VLS_205({
                ...{ 'onClick': {} },
                size: "small",
            }, ...__VLS_functionalComponentArgsRest(__VLS_205));
            let __VLS_209;
            const __VLS_210 = ({ click: {} },
                { onClick: (__VLS_ctx.addCustomRule) });
            const { default: __VLS_211 } = __VLS_207.slots;
            // @ts-ignore
            [addCustomRule,];
            var __VLS_207;
            var __VLS_208;
            let __VLS_212;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_213 = __VLS_asFunctionalComponent1(__VLS_212, new __VLS_212({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                loading: (__VLS_ctx.scriptTagSaving),
                disabled: (!__VLS_ctx.scriptTagDirty),
            }));
            const __VLS_214 = __VLS_213({
                ...{ 'onClick': {} },
                size: "small",
                type: "primary",
                loading: (__VLS_ctx.scriptTagSaving),
                disabled: (!__VLS_ctx.scriptTagDirty),
            }, ...__VLS_functionalComponentArgsRest(__VLS_213));
            let __VLS_217;
            const __VLS_218 = ({ click: {} },
                { onClick: (__VLS_ctx.handleSaveScriptTags) });
            const { default: __VLS_219 } = __VLS_215.slots;
            // @ts-ignore
            [scriptTagSaving, scriptTagDirty, handleSaveScriptTags,];
            var __VLS_215;
            var __VLS_216;
            let __VLS_220;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_221 = __VLS_asFunctionalComponent1(__VLS_220, new __VLS_220({
                size: (18),
                ...{ class: "collapse-chevron" },
                ...{ class: ({ expanded: !__VLS_ctx.collapsed.scriptTags }) },
            }));
            const __VLS_222 = __VLS_221({
                size: (18),
                ...{ class: "collapse-chevron" },
                ...{ class: ({ expanded: !__VLS_ctx.collapsed.scriptTags }) },
            }, ...__VLS_functionalComponentArgsRest(__VLS_221));
            /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
            /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
            const { default: __VLS_225 } = __VLS_223.slots;
            let __VLS_226;
            /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
            ExpandMoreOutlined;
            // @ts-ignore
            const __VLS_227 = __VLS_asFunctionalComponent1(__VLS_226, new __VLS_226({}));
            const __VLS_228 = __VLS_227({}, ...__VLS_functionalComponentArgsRest(__VLS_227));
            // @ts-ignore
            [collapsed,];
            var __VLS_223;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "section-body" },
                ...{ class: ({ collapsed: __VLS_ctx.collapsed.scriptTags }) },
            });
            /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
            /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "section-body-inner" },
            });
            /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
            if (__VLS_ctx.scriptTagRules.length === 0) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "empty-hint" },
                });
                /** @type {__VLS_StyleScopedClasses['empty-hint']} */ ;
            }
            for (const [rule, index] of __VLS_vFor((__VLS_ctx.scriptTagRules))) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    key: (index),
                    ...{ class: "rule-row" },
                });
                /** @type {__VLS_StyleScopedClasses['rule-row']} */ ;
                let __VLS_231;
                /** @ts-ignore @type {typeof __VLS_components.NInput} */
                NInput;
                // @ts-ignore
                const __VLS_232 = __VLS_asFunctionalComponent1(__VLS_231, new __VLS_231({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.pattern),
                    placeholder: "正则表达式",
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-pattern" },
                }));
                const __VLS_233 = __VLS_232({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.pattern),
                    placeholder: "正则表达式",
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-pattern" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_232));
                let __VLS_236;
                const __VLS_237 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.loading))
                                return;
                            if (!(__VLS_ctx.game))
                                return;
                            if (!!(!__VLS_ctx.store.extractionResult))
                                return;
                            if (!(__VLS_ctx.enablePreTranslationCache))
                                return;
                            __VLS_ctx.scriptTagDirty = true;
                            // @ts-ignore
                            [collapsed, scriptTagRules, scriptTagRules, scriptTagDirty,];
                        } });
                /** @type {__VLS_StyleScopedClasses['rule-pattern']} */ ;
                var __VLS_234;
                var __VLS_235;
                let __VLS_238;
                /** @ts-ignore @type {typeof __VLS_components.NSelect} */
                NSelect;
                // @ts-ignore
                const __VLS_239 = __VLS_asFunctionalComponent1(__VLS_238, new __VLS_238({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.action),
                    options: (__VLS_ctx.actionOptions),
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-action" },
                }));
                const __VLS_240 = __VLS_239({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.action),
                    options: (__VLS_ctx.actionOptions),
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-action" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_239));
                let __VLS_243;
                const __VLS_244 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.loading))
                                return;
                            if (!(__VLS_ctx.game))
                                return;
                            if (!!(!__VLS_ctx.store.extractionResult))
                                return;
                            if (!(__VLS_ctx.enablePreTranslationCache))
                                return;
                            __VLS_ctx.scriptTagDirty = true;
                            // @ts-ignore
                            [scriptTagDirty, actionOptions,];
                        } });
                /** @type {__VLS_StyleScopedClasses['rule-action']} */ ;
                var __VLS_241;
                var __VLS_242;
                let __VLS_245;
                /** @ts-ignore @type {typeof __VLS_components.NInput} */
                NInput;
                // @ts-ignore
                const __VLS_246 = __VLS_asFunctionalComponent1(__VLS_245, new __VLS_245({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.description),
                    placeholder: "说明",
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-desc" },
                }));
                const __VLS_247 = __VLS_246({
                    ...{ 'onUpdate:value': {} },
                    value: (rule.description),
                    placeholder: "说明",
                    disabled: (rule.isBuiltin),
                    ...{ class: "rule-desc" },
                }, ...__VLS_functionalComponentArgsRest(__VLS_246));
                let __VLS_250;
                const __VLS_251 = ({ 'update:value': {} },
                    { 'onUpdate:value': (...[$event]) => {
                            if (!!(__VLS_ctx.loading))
                                return;
                            if (!(__VLS_ctx.game))
                                return;
                            if (!!(!__VLS_ctx.store.extractionResult))
                                return;
                            if (!(__VLS_ctx.enablePreTranslationCache))
                                return;
                            __VLS_ctx.scriptTagDirty = true;
                            // @ts-ignore
                            [scriptTagDirty,];
                        } });
                /** @type {__VLS_StyleScopedClasses['rule-desc']} */ ;
                var __VLS_248;
                var __VLS_249;
                if (!rule.isBuiltin) {
                    let __VLS_252;
                    /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                    NButton;
                    // @ts-ignore
                    const __VLS_253 = __VLS_asFunctionalComponent1(__VLS_252, new __VLS_252({
                        ...{ 'onClick': {} },
                        size: "small",
                        quaternary: true,
                    }));
                    const __VLS_254 = __VLS_253({
                        ...{ 'onClick': {} },
                        size: "small",
                        quaternary: true,
                    }, ...__VLS_functionalComponentArgsRest(__VLS_253));
                    let __VLS_257;
                    const __VLS_258 = ({ click: {} },
                        { onClick: (...[$event]) => {
                                if (!!(__VLS_ctx.loading))
                                    return;
                                if (!(__VLS_ctx.game))
                                    return;
                                if (!!(!__VLS_ctx.store.extractionResult))
                                    return;
                                if (!(__VLS_ctx.enablePreTranslationCache))
                                    return;
                                if (!(!rule.isBuiltin))
                                    return;
                                __VLS_ctx.removeScriptTagRule(index);
                                // @ts-ignore
                                [removeScriptTagRule,];
                            } });
                    const { default: __VLS_259 } = __VLS_255.slots;
                    {
                        const { icon: __VLS_260 } = __VLS_255.slots;
                        let __VLS_261;
                        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                        NIcon;
                        // @ts-ignore
                        const __VLS_262 = __VLS_asFunctionalComponent1(__VLS_261, new __VLS_261({
                            size: (16),
                        }));
                        const __VLS_263 = __VLS_262({
                            size: (16),
                        }, ...__VLS_functionalComponentArgsRest(__VLS_262));
                        const { default: __VLS_266 } = __VLS_264.slots;
                        let __VLS_267;
                        /** @ts-ignore @type {typeof __VLS_components.DeleteOutlined} */
                        DeleteOutlined;
                        // @ts-ignore
                        const __VLS_268 = __VLS_asFunctionalComponent1(__VLS_267, new __VLS_267({}));
                        const __VLS_269 = __VLS_268({}, ...__VLS_functionalComponentArgsRest(__VLS_268));
                        // @ts-ignore
                        [];
                        var __VLS_264;
                        // @ts-ignore
                        [];
                    }
                    // @ts-ignore
                    [];
                    var __VLS_255;
                    var __VLS_256;
                }
                else {
                    let __VLS_272;
                    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                    NIcon;
                    // @ts-ignore
                    const __VLS_273 = __VLS_asFunctionalComponent1(__VLS_272, new __VLS_272({
                        size: (16),
                        ...{ class: "rule-lock-icon" },
                    }));
                    const __VLS_274 = __VLS_273({
                        size: (16),
                        ...{ class: "rule-lock-icon" },
                    }, ...__VLS_functionalComponentArgsRest(__VLS_273));
                    /** @type {__VLS_StyleScopedClasses['rule-lock-icon']} */ ;
                    const { default: __VLS_277 } = __VLS_275.slots;
                    let __VLS_278;
                    /** @ts-ignore @type {typeof __VLS_components.LockClosedOutline} */
                    LockClosedOutline;
                    // @ts-ignore
                    const __VLS_279 = __VLS_asFunctionalComponent1(__VLS_278, new __VLS_278({}));
                    const __VLS_280 = __VLS_279({}, ...__VLS_functionalComponentArgsRest(__VLS_279));
                    // @ts-ignore
                    [];
                    var __VLS_275;
                }
                // @ts-ignore
                [];
            }
            if (__VLS_ctx.scriptTagRules.length > 0) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
                    ...{ class: "rule-hint" },
                });
                /** @type {__VLS_StyleScopedClasses['rule-hint']} */ ;
            }
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "action-row" },
        });
        /** @type {__VLS_StyleScopedClasses['action-row']} */ ;
        if (!__VLS_ctx.isPreTranslating && !__VLS_ctx.isAwaitingTermReview && !__VLS_ctx.hasResumableCheckpoint && !__VLS_ctx.hasBlockedCheckpoint) {
            let __VLS_283;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_284 = __VLS_asFunctionalComponent1(__VLS_283, new __VLS_283({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }));
            const __VLS_285 = __VLS_284({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }, ...__VLS_functionalComponentArgsRest(__VLS_284));
            let __VLS_288;
            const __VLS_289 = ({ click: {} },
                { onClick: (__VLS_ctx.handleStartPreTranslation) });
            const { default: __VLS_290 } = __VLS_286.slots;
            {
                const { icon: __VLS_291 } = __VLS_286.slots;
                let __VLS_292;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_293 = __VLS_asFunctionalComponent1(__VLS_292, new __VLS_292({
                    size: (16),
                }));
                const __VLS_294 = __VLS_293({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_293));
                const { default: __VLS_297 } = __VLS_295.slots;
                let __VLS_298;
                /** @ts-ignore @type {typeof __VLS_components.PlayArrowFilled} */
                PlayArrowFilled;
                // @ts-ignore
                const __VLS_299 = __VLS_asFunctionalComponent1(__VLS_298, new __VLS_298({}));
                const __VLS_300 = __VLS_299({}, ...__VLS_functionalComponentArgsRest(__VLS_299));
                // @ts-ignore
                [isPreTranslating, hasAiProvider, scriptTagRules, isAwaitingTermReview, hasResumableCheckpoint, hasBlockedCheckpoint, handleStartPreTranslation,];
                var __VLS_295;
                // @ts-ignore
                [];
            }
            (__VLS_ctx.store.extractionResult.totalTextsExtracted);
            // @ts-ignore
            [store,];
            var __VLS_286;
            var __VLS_287;
        }
        if (__VLS_ctx.hasResumableCheckpoint) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "checkpoint-actions" },
            });
            /** @type {__VLS_StyleScopedClasses['checkpoint-actions']} */ ;
            let __VLS_303;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_304 = __VLS_asFunctionalComponent1(__VLS_303, new __VLS_303({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
                ...{ class: "checkpoint-action-button" },
            }));
            const __VLS_305 = __VLS_304({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
                ...{ class: "checkpoint-action-button" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_304));
            let __VLS_308;
            const __VLS_309 = ({ click: {} },
                { onClick: (__VLS_ctx.handleResumePreTranslation) });
            /** @type {__VLS_StyleScopedClasses['checkpoint-action-button']} */ ;
            const { default: __VLS_310 } = __VLS_306.slots;
            {
                const { icon: __VLS_311 } = __VLS_306.slots;
                let __VLS_312;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_313 = __VLS_asFunctionalComponent1(__VLS_312, new __VLS_312({
                    size: (16),
                }));
                const __VLS_314 = __VLS_313({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_313));
                const { default: __VLS_317 } = __VLS_315.slots;
                let __VLS_318;
                /** @ts-ignore @type {typeof __VLS_components.PlayArrowFilled} */
                PlayArrowFilled;
                // @ts-ignore
                const __VLS_319 = __VLS_asFunctionalComponent1(__VLS_318, new __VLS_318({}));
                const __VLS_320 = __VLS_319({}, ...__VLS_functionalComponentArgsRest(__VLS_319));
                // @ts-ignore
                [hasAiProvider, hasResumableCheckpoint, handleResumePreTranslation,];
                var __VLS_315;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_306;
            var __VLS_307;
            let __VLS_323;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_324 = __VLS_asFunctionalComponent1(__VLS_323, new __VLS_323({
                ...{ 'onClick': {} },
                disabled: (!__VLS_ctx.hasAiProvider),
                ...{ class: "checkpoint-action-button" },
            }));
            const __VLS_325 = __VLS_324({
                ...{ 'onClick': {} },
                disabled: (!__VLS_ctx.hasAiProvider),
                ...{ class: "checkpoint-action-button" },
            }, ...__VLS_functionalComponentArgsRest(__VLS_324));
            let __VLS_328;
            const __VLS_329 = ({ click: {} },
                { onClick: (__VLS_ctx.handleRestartPreTranslation) });
            /** @type {__VLS_StyleScopedClasses['checkpoint-action-button']} */ ;
            const { default: __VLS_330 } = __VLS_326.slots;
            {
                const { icon: __VLS_331 } = __VLS_326.slots;
                let __VLS_332;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_333 = __VLS_asFunctionalComponent1(__VLS_332, new __VLS_332({
                    size: (16),
                }));
                const __VLS_334 = __VLS_333({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_333));
                const { default: __VLS_337 } = __VLS_335.slots;
                let __VLS_338;
                /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
                RefreshOutlined;
                // @ts-ignore
                const __VLS_339 = __VLS_asFunctionalComponent1(__VLS_338, new __VLS_338({}));
                const __VLS_340 = __VLS_339({}, ...__VLS_functionalComponentArgsRest(__VLS_339));
                // @ts-ignore
                [hasAiProvider, handleRestartPreTranslation,];
                var __VLS_335;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_326;
            var __VLS_327;
        }
        if (false) {
            let __VLS_343;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_344 = __VLS_asFunctionalComponent1(__VLS_343, new __VLS_343({
                ...{ 'onClick': {} },
                type: "primary",
            }));
            const __VLS_345 = __VLS_344({
                ...{ 'onClick': {} },
                type: "primary",
            }, ...__VLS_functionalComponentArgsRest(__VLS_344));
            let __VLS_348;
            const __VLS_349 = ({ click: {} },
                { onClick: (__VLS_ctx.handleResumePreTranslation) });
            const { default: __VLS_350 } = __VLS_346.slots;
            {
                const { icon: __VLS_351 } = __VLS_346.slots;
                let __VLS_352;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_353 = __VLS_asFunctionalComponent1(__VLS_352, new __VLS_352({
                    size: (16),
                }));
                const __VLS_354 = __VLS_353({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_353));
                const { default: __VLS_357 } = __VLS_355.slots;
                let __VLS_358;
                /** @ts-ignore @type {typeof __VLS_components.PlayArrowFilled} */
                PlayArrowFilled;
                // @ts-ignore
                const __VLS_359 = __VLS_asFunctionalComponent1(__VLS_358, new __VLS_358({}));
                const __VLS_360 = __VLS_359({}, ...__VLS_functionalComponentArgsRest(__VLS_359));
                // @ts-ignore
                [handleResumePreTranslation,];
                var __VLS_355;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_346;
            var __VLS_347;
            let __VLS_363;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_364 = __VLS_asFunctionalComponent1(__VLS_363, new __VLS_363({
                ...{ 'onClick': {} },
                disabled: (!__VLS_ctx.hasAiProvider),
            }));
            const __VLS_365 = __VLS_364({
                ...{ 'onClick': {} },
                disabled: (!__VLS_ctx.hasAiProvider),
            }, ...__VLS_functionalComponentArgsRest(__VLS_364));
            let __VLS_368;
            const __VLS_369 = ({ click: {} },
                { onClick: (__VLS_ctx.handleRestartPreTranslation) });
            const { default: __VLS_370 } = __VLS_366.slots;
            // @ts-ignore
            [hasAiProvider, handleRestartPreTranslation,];
            var __VLS_366;
            var __VLS_367;
        }
        if (__VLS_ctx.hasBlockedCheckpoint) {
            let __VLS_371;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_372 = __VLS_asFunctionalComponent1(__VLS_371, new __VLS_371({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }));
            const __VLS_373 = __VLS_372({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }, ...__VLS_functionalComponentArgsRest(__VLS_372));
            let __VLS_376;
            const __VLS_377 = ({ click: {} },
                { onClick: (__VLS_ctx.handleRestartPreTranslation) });
            const { default: __VLS_378 } = __VLS_374.slots;
            {
                const { icon: __VLS_379 } = __VLS_374.slots;
                let __VLS_380;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_381 = __VLS_asFunctionalComponent1(__VLS_380, new __VLS_380({
                    size: (16),
                }));
                const __VLS_382 = __VLS_381({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_381));
                const { default: __VLS_385 } = __VLS_383.slots;
                let __VLS_386;
                /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
                RefreshOutlined;
                // @ts-ignore
                const __VLS_387 = __VLS_asFunctionalComponent1(__VLS_386, new __VLS_386({}));
                const __VLS_388 = __VLS_387({}, ...__VLS_functionalComponentArgsRest(__VLS_387));
                // @ts-ignore
                [hasAiProvider, hasBlockedCheckpoint, handleRestartPreTranslation,];
                var __VLS_383;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_374;
            var __VLS_375;
        }
        if (false) {
            let __VLS_391;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_392 = __VLS_asFunctionalComponent1(__VLS_391, new __VLS_391({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }));
            const __VLS_393 = __VLS_392({
                ...{ 'onClick': {} },
                type: "primary",
                disabled: (!__VLS_ctx.hasAiProvider),
            }, ...__VLS_functionalComponentArgsRest(__VLS_392));
            let __VLS_396;
            const __VLS_397 = ({ click: {} },
                { onClick: (__VLS_ctx.handleRestartPreTranslation) });
            const { default: __VLS_398 } = __VLS_394.slots;
            {
                const { icon: __VLS_399 } = __VLS_394.slots;
                let __VLS_400;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_401 = __VLS_asFunctionalComponent1(__VLS_400, new __VLS_400({
                    size: (16),
                }));
                const __VLS_402 = __VLS_401({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_401));
                const { default: __VLS_405 } = __VLS_403.slots;
                let __VLS_406;
                /** @ts-ignore @type {typeof __VLS_components.RefreshOutlined} */
                RefreshOutlined;
                // @ts-ignore
                const __VLS_407 = __VLS_asFunctionalComponent1(__VLS_406, new __VLS_406({}));
                const __VLS_408 = __VLS_407({}, ...__VLS_functionalComponentArgsRest(__VLS_407));
                // @ts-ignore
                [hasAiProvider, handleRestartPreTranslation,];
                var __VLS_403;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_394;
            var __VLS_395;
        }
        else if (__VLS_ctx.isPreTranslating || __VLS_ctx.isAwaitingTermReview) {
            let __VLS_411;
            /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
            NButton;
            // @ts-ignore
            const __VLS_412 = __VLS_asFunctionalComponent1(__VLS_411, new __VLS_411({
                ...{ 'onClick': {} },
                type: "warning",
            }));
            const __VLS_413 = __VLS_412({
                ...{ 'onClick': {} },
                type: "warning",
            }, ...__VLS_functionalComponentArgsRest(__VLS_412));
            let __VLS_416;
            const __VLS_417 = ({ click: {} },
                { onClick: (__VLS_ctx.handleCancelPreTranslation) });
            const { default: __VLS_418 } = __VLS_414.slots;
            {
                const { icon: __VLS_419 } = __VLS_414.slots;
                let __VLS_420;
                /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                NIcon;
                // @ts-ignore
                const __VLS_421 = __VLS_asFunctionalComponent1(__VLS_420, new __VLS_420({
                    size: (16),
                }));
                const __VLS_422 = __VLS_421({
                    size: (16),
                }, ...__VLS_functionalComponentArgsRest(__VLS_421));
                const { default: __VLS_425 } = __VLS_423.slots;
                let __VLS_426;
                /** @ts-ignore @type {typeof __VLS_components.StopOutlined} */
                StopOutlined;
                // @ts-ignore
                const __VLS_427 = __VLS_asFunctionalComponent1(__VLS_426, new __VLS_426({}));
                const __VLS_428 = __VLS_427({}, ...__VLS_functionalComponentArgsRest(__VLS_427));
                // @ts-ignore
                [isPreTranslating, isAwaitingTermReview, handleCancelPreTranslation,];
                var __VLS_423;
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
            var __VLS_414;
            var __VLS_415;
        }
        if (__VLS_ctx.store.preTranslationStatus && __VLS_ctx.store.preTranslationStatus.state !== 'Idle') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "progress-section" },
            });
            /** @type {__VLS_StyleScopedClasses['progress-section']} */ ;
            if (__VLS_ctx.isPreTranslating || __VLS_ctx.isAwaitingTermReview || __VLS_ctx.hasCheckpoint || __VLS_ctx.store.preTranslationStatus.state === 'Completed') {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "phase-steps" },
                });
                /** @type {__VLS_StyleScopedClasses['phase-steps']} */ ;
                for (const [phase, idx] of __VLS_vFor((__VLS_ctx.phases))) {
                    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                        key: (phase.key),
                        ...{ class: "phase-step" },
                        ...{ class: (__VLS_ctx.getPhaseStatus(phase.key)) },
                    });
                    /** @type {__VLS_StyleScopedClasses['phase-step']} */ ;
                    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                        ...{ class: "phase-indicator" },
                    });
                    /** @type {__VLS_StyleScopedClasses['phase-indicator']} */ ;
                    if (__VLS_ctx.getPhaseStatus(phase.key) === 'done') {
                        let __VLS_431;
                        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
                        NIcon;
                        // @ts-ignore
                        const __VLS_432 = __VLS_asFunctionalComponent1(__VLS_431, new __VLS_431({
                            size: (14),
                        }));
                        const __VLS_433 = __VLS_432({
                            size: (14),
                        }, ...__VLS_functionalComponentArgsRest(__VLS_432));
                        const { default: __VLS_436 } = __VLS_434.slots;
                        let __VLS_437;
                        /** @ts-ignore @type {typeof __VLS_components.CheckCircleOutlined} */
                        CheckCircleOutlined;
                        // @ts-ignore
                        const __VLS_438 = __VLS_asFunctionalComponent1(__VLS_437, new __VLS_437({}));
                        const __VLS_439 = __VLS_438({}, ...__VLS_functionalComponentArgsRest(__VLS_438));
                        // @ts-ignore
                        [store, store, store, isPreTranslating, isAwaitingTermReview, hasCheckpoint, phases, getPhaseStatus, getPhaseStatus,];
                        var __VLS_434;
                    }
                    else if (__VLS_ctx.getPhaseStatus(phase.key) === 'active') {
                        let __VLS_442;
                        /** @ts-ignore @type {typeof __VLS_components.NSpin} */
                        NSpin;
                        // @ts-ignore
                        const __VLS_443 = __VLS_asFunctionalComponent1(__VLS_442, new __VLS_442({
                            size: (14),
                        }));
                        const __VLS_444 = __VLS_443({
                            size: (14),
                        }, ...__VLS_functionalComponentArgsRest(__VLS_443));
                    }
                    else {
                        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                            ...{ class: "phase-dot" },
                        });
                        /** @type {__VLS_StyleScopedClasses['phase-dot']} */ ;
                    }
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "phase-label" },
                    });
                    /** @type {__VLS_StyleScopedClasses['phase-label']} */ ;
                    (phase.label);
                    if (idx < __VLS_ctx.phases.length - 1) {
                        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                            ...{ class: "phase-arrow" },
                        });
                        /** @type {__VLS_StyleScopedClasses['phase-arrow']} */ ;
                    }
                    // @ts-ignore
                    [phases, getPhaseStatus,];
                }
            }
            if ((__VLS_ctx.store.preTranslationStatus.currentRound ?? 0) > 0 && (__VLS_ctx.isPreTranslating || __VLS_ctx.hasCheckpoint)) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "round-indicator" },
                });
                /** @type {__VLS_StyleScopedClasses['round-indicator']} */ ;
                let __VLS_447;
                /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
                NTag;
                // @ts-ignore
                const __VLS_448 = __VLS_asFunctionalComponent1(__VLS_447, new __VLS_447({
                    size: "small",
                    bordered: (false),
                }));
                const __VLS_449 = __VLS_448({
                    size: "small",
                    bordered: (false),
                }, ...__VLS_functionalComponentArgsRest(__VLS_448));
                const { default: __VLS_452 } = __VLS_450.slots;
                (__VLS_ctx.store.preTranslationStatus.currentRound);
                // @ts-ignore
                [store, store, isPreTranslating, hasCheckpoint,];
                var __VLS_450;
                if ((__VLS_ctx.store.preTranslationStatus.dynamicPatternCount ?? 0) > 0) {
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "round-stat" },
                    });
                    /** @type {__VLS_StyleScopedClasses['round-stat']} */ ;
                    (__VLS_ctx.store.preTranslationStatus.dynamicPatternCount);
                }
                if ((__VLS_ctx.store.preTranslationStatus.extractedTermCount ?? 0) > 0) {
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "round-stat" },
                    });
                    /** @type {__VLS_StyleScopedClasses['round-stat']} */ ;
                    (__VLS_ctx.store.preTranslationStatus.extractedTermCount);
                }
            }
            let __VLS_453;
            /** @ts-ignore @type {typeof __VLS_components.NProgress} */
            NProgress;
            // @ts-ignore
            const __VLS_454 = __VLS_asFunctionalComponent1(__VLS_453, new __VLS_453({
                type: "line",
                percentage: (__VLS_ctx.preTranslationProgress),
                status: (__VLS_ctx.store.preTranslationStatus.state === 'Failed' ? 'error'
                    : __VLS_ctx.store.preTranslationStatus.state === 'Completed' ? 'success'
                        : 'default'),
                indicatorPlacement: ('inside'),
                height: (20),
                borderRadius: (10),
            }));
            const __VLS_455 = __VLS_454({
                type: "line",
                percentage: (__VLS_ctx.preTranslationProgress),
                status: (__VLS_ctx.store.preTranslationStatus.state === 'Failed' ? 'error'
                    : __VLS_ctx.store.preTranslationStatus.state === 'Completed' ? 'success'
                        : 'default'),
                indicatorPlacement: ('inside'),
                height: (20),
                borderRadius: (10),
            }, ...__VLS_functionalComponentArgsRest(__VLS_454));
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "progress-stats" },
            });
            /** @type {__VLS_StyleScopedClasses['progress-stats']} */ ;
            if (__VLS_ctx.isPhaseWithBatchProgress) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
                (__VLS_ctx.store.preTranslationStatus.phaseProgress);
                (__VLS_ctx.store.preTranslationStatus.phaseTotal);
            }
            else if (__VLS_ctx.store.preTranslationStatus.currentPhase === 'writeCache' && __VLS_ctx.store.preTranslationStatus.state === 'Running') {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            }
            else {
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
                (__VLS_ctx.store.preTranslationStatus.translatedTexts);
                (__VLS_ctx.store.preTranslationStatus.totalTexts);
                if (__VLS_ctx.store.preTranslationStatus.failedTexts > 0) {
                    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                        ...{ class: "failed-count" },
                    });
                    /** @type {__VLS_StyleScopedClasses['failed-count']} */ ;
                    (__VLS_ctx.store.preTranslationStatus.failedTexts);
                }
            }
            if (__VLS_ctx.store.preTranslationStatus.state === 'Completed') {
                let __VLS_458;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_459 = __VLS_asFunctionalComponent1(__VLS_458, new __VLS_458({
                    type: "success",
                    ...{ style: {} },
                }));
                const __VLS_460 = __VLS_459({
                    type: "success",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_459));
                const { default: __VLS_463 } = __VLS_461.slots;
                // @ts-ignore
                [store, store, store, store, store, store, store, store, store, store, store, store, store, store, store, preTranslationProgress, isPhaseWithBatchProgress,];
                var __VLS_461;
            }
            if (false) {
                let __VLS_464;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_465 = __VLS_asFunctionalComponent1(__VLS_464, new __VLS_464({
                    type: "info",
                    ...{ style: {} },
                }));
                const __VLS_466 = __VLS_465({
                    type: "info",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_465));
                const { default: __VLS_469 } = __VLS_467.slots;
                {
                    const { default: __VLS_470 } = __VLS_467.slots;
                    // @ts-ignore
                    [];
                }
                // @ts-ignore
                [];
                var __VLS_467;
            }
            if (__VLS_ctx.store.preTranslationStatus.state === 'Completed') {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "completed-editor-actions" },
                });
                /** @type {__VLS_StyleScopedClasses['completed-editor-actions']} */ ;
                let __VLS_471;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_472 = __VLS_asFunctionalComponent1(__VLS_471, new __VLS_471({
                    ...{ 'onClick': {} },
                    size: "small",
                    secondary: true,
                }));
                const __VLS_473 = __VLS_472({
                    ...{ 'onClick': {} },
                    size: "small",
                    secondary: true,
                }, ...__VLS_functionalComponentArgsRest(__VLS_472));
                let __VLS_476;
                const __VLS_477 = ({ click: {} },
                    { onClick: (__VLS_ctx.openPretranslatedTextEditor) });
                const { default: __VLS_478 } = __VLS_474.slots;
                // @ts-ignore
                [store, openPretranslatedTextEditor,];
                var __VLS_474;
                var __VLS_475;
                let __VLS_479;
                /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
                NButton;
                // @ts-ignore
                const __VLS_480 = __VLS_asFunctionalComponent1(__VLS_479, new __VLS_479({
                    ...{ 'onClick': {} },
                    size: "small",
                    secondary: true,
                }));
                const __VLS_481 = __VLS_480({
                    ...{ 'onClick': {} },
                    size: "small",
                    secondary: true,
                }, ...__VLS_functionalComponentArgsRest(__VLS_480));
                let __VLS_484;
                const __VLS_485 = ({ click: {} },
                    { onClick: (__VLS_ctx.openPretranslatedRegexEditor) });
                const { default: __VLS_486 } = __VLS_482.slots;
                // @ts-ignore
                [openPretranslatedRegexEditor,];
                var __VLS_482;
                var __VLS_483;
            }
            if (__VLS_ctx.hasResumableCheckpoint) {
                let __VLS_487;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_488 = __VLS_asFunctionalComponent1(__VLS_487, new __VLS_487({
                    type: "info",
                    ...{ style: {} },
                }));
                const __VLS_489 = __VLS_488({
                    type: "info",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_488));
                const { default: __VLS_492 } = __VLS_490.slots;
                // @ts-ignore
                [hasResumableCheckpoint,];
                var __VLS_490;
            }
            if (false) {
                let __VLS_493;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_494 = __VLS_asFunctionalComponent1(__VLS_493, new __VLS_493({
                    type: "info",
                    ...{ style: {} },
                }));
                const __VLS_495 = __VLS_494({
                    type: "info",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_494));
                const { default: __VLS_498 } = __VLS_496.slots;
                // @ts-ignore
                [];
                var __VLS_496;
            }
            if (__VLS_ctx.hasBlockedCheckpoint) {
                let __VLS_499;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_500 = __VLS_asFunctionalComponent1(__VLS_499, new __VLS_499({
                    type: "warning",
                    ...{ style: {} },
                }));
                const __VLS_501 = __VLS_500({
                    type: "warning",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_500));
                const { default: __VLS_504 } = __VLS_502.slots;
                (__VLS_ctx.store.preTranslationStatus.resumeBlockedReason);
                // @ts-ignore
                [store, hasBlockedCheckpoint,];
                var __VLS_502;
            }
            if (__VLS_ctx.isAwaitingTermReview) {
                let __VLS_505;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_506 = __VLS_asFunctionalComponent1(__VLS_505, new __VLS_505({
                    type: "info",
                    ...{ style: {} },
                }));
                const __VLS_507 = __VLS_506({
                    type: "info",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_506));
                const { default: __VLS_510 } = __VLS_508.slots;
                // @ts-ignore
                [isAwaitingTermReview,];
                var __VLS_508;
            }
            if (__VLS_ctx.store.preTranslationStatus.state === 'Failed') {
                let __VLS_511;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_512 = __VLS_asFunctionalComponent1(__VLS_511, new __VLS_511({
                    type: "error",
                    ...{ style: {} },
                }));
                const __VLS_513 = __VLS_512({
                    type: "error",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_512));
                const { default: __VLS_516 } = __VLS_514.slots;
                (__VLS_ctx.store.preTranslationStatus.error);
                // @ts-ignore
                [store, store,];
                var __VLS_514;
            }
            if (__VLS_ctx.store.preTranslationStatus.state === 'Cancelled') {
                let __VLS_517;
                /** @ts-ignore @type {typeof __VLS_components.NAlert | typeof __VLS_components.NAlert} */
                NAlert;
                // @ts-ignore
                const __VLS_518 = __VLS_asFunctionalComponent1(__VLS_517, new __VLS_517({
                    type: "warning",
                    ...{ style: {} },
                }));
                const __VLS_519 = __VLS_518({
                    type: "warning",
                    ...{ style: {} },
                }, ...__VLS_functionalComponentArgsRest(__VLS_518));
                const { default: __VLS_522 } = __VLS_520.slots;
                // @ts-ignore
                [store,];
                var __VLS_520;
            }
        }
    }
    if (__VLS_ctx.store.extractionResult && __VLS_ctx.store.extractionResult.texts.length > 0) {
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
        let __VLS_523;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_524 = __VLS_asFunctionalComponent1(__VLS_523, new __VLS_523({
            size: (16),
        }));
        const __VLS_525 = __VLS_524({
            size: (16),
        }, ...__VLS_functionalComponentArgsRest(__VLS_524));
        const { default: __VLS_528 } = __VLS_526.slots;
        let __VLS_529;
        /** @ts-ignore @type {typeof __VLS_components.DescriptionOutlined} */
        DescriptionOutlined;
        // @ts-ignore
        const __VLS_530 = __VLS_asFunctionalComponent1(__VLS_529, new __VLS_529({}));
        const __VLS_531 = __VLS_530({}, ...__VLS_functionalComponentArgsRest(__VLS_530));
        // @ts-ignore
        [store, store,];
        var __VLS_526;
        let __VLS_534;
        /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
        NTag;
        // @ts-ignore
        const __VLS_535 = __VLS_asFunctionalComponent1(__VLS_534, new __VLS_534({
            size: "small",
            bordered: (false),
            ...{ style: {} },
        }));
        const __VLS_536 = __VLS_535({
            size: "small",
            bordered: (false),
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_535));
        const { default: __VLS_539 } = __VLS_537.slots;
        (__VLS_ctx.filteredTexts.length);
        (__VLS_ctx.store.extractionResult.texts.length);
        // @ts-ignore
        [store, filteredTexts,];
        var __VLS_537;
        let __VLS_540;
        /** @ts-ignore @type {typeof __VLS_components.NInput | typeof __VLS_components.NInput} */
        NInput;
        // @ts-ignore
        const __VLS_541 = __VLS_asFunctionalComponent1(__VLS_540, new __VLS_540({
            value: (__VLS_ctx.searchKeyword),
            placeholder: "搜索文本...",
            clearable: true,
            size: "small",
            ...{ style: {} },
        }));
        const __VLS_542 = __VLS_541({
            value: (__VLS_ctx.searchKeyword),
            placeholder: "搜索文本...",
            clearable: true,
            size: "small",
            ...{ style: {} },
        }, ...__VLS_functionalComponentArgsRest(__VLS_541));
        const { default: __VLS_545 } = __VLS_543.slots;
        {
            const { prefix: __VLS_546 } = __VLS_543.slots;
            let __VLS_547;
            /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
            NIcon;
            // @ts-ignore
            const __VLS_548 = __VLS_asFunctionalComponent1(__VLS_547, new __VLS_547({
                size: (16),
            }));
            const __VLS_549 = __VLS_548({
                size: (16),
            }, ...__VLS_functionalComponentArgsRest(__VLS_548));
            const { default: __VLS_552 } = __VLS_550.slots;
            let __VLS_553;
            /** @ts-ignore @type {typeof __VLS_components.SearchOutlined} */
            SearchOutlined;
            // @ts-ignore
            const __VLS_554 = __VLS_asFunctionalComponent1(__VLS_553, new __VLS_553({}));
            const __VLS_555 = __VLS_554({}, ...__VLS_functionalComponentArgsRest(__VLS_554));
            // @ts-ignore
            [searchKeyword,];
            var __VLS_550;
            // @ts-ignore
            [];
        }
        // @ts-ignore
        [];
        var __VLS_543;
        let __VLS_558;
        /** @ts-ignore @type {typeof __VLS_components.NDataTable} */
        NDataTable;
        // @ts-ignore
        const __VLS_559 = __VLS_asFunctionalComponent1(__VLS_558, new __VLS_558({
            columns: (__VLS_ctx.tableColumns),
            data: (__VLS_ctx.filteredTexts),
            maxHeight: (480),
            pagination: ({ pageSize: 50 }),
            size: "small",
            striped: true,
        }));
        const __VLS_560 = __VLS_559({
            columns: (__VLS_ctx.tableColumns),
            data: (__VLS_ctx.filteredTexts),
            maxHeight: (480),
            pagination: ({ pageSize: 50 }),
            size: "small",
            striped: true,
        }, ...__VLS_functionalComponentArgsRest(__VLS_559));
    }
    let __VLS_563;
    /** @ts-ignore @type {typeof __VLS_components.NModal | typeof __VLS_components.NModal} */
    NModal;
    // @ts-ignore
    const __VLS_564 = __VLS_asFunctionalComponent1(__VLS_563, new __VLS_563({
        show: (__VLS_ctx.showTermReview),
        preset: "card",
        title: "术语候选项审核",
        ...{ style: {} },
        maskClosable: (false),
    }));
    const __VLS_565 = __VLS_564({
        show: (__VLS_ctx.showTermReview),
        preset: "card",
        title: "术语候选项审核",
        ...{ style: {} },
        maskClosable: (false),
    }, ...__VLS_functionalComponentArgsRest(__VLS_564));
    const { default: __VLS_568 } = __VLS_566.slots;
    if (__VLS_ctx.termCandidates.length === 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "empty-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['empty-hint']} */ ;
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "term-review-list" },
        });
        /** @type {__VLS_StyleScopedClasses['term-review-list']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "term-review-header" },
        });
        /** @type {__VLS_StyleScopedClasses['term-review-header']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.termCandidates.length);
        (__VLS_ctx.termCandidateSelected.size);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "term-review-items" },
        });
        /** @type {__VLS_StyleScopedClasses['term-review-items']} */ ;
        for (const [candidate] of __VLS_vFor((__VLS_ctx.termCandidates))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (candidate.original),
                ...{ class: "term-review-item" },
            });
            /** @type {__VLS_StyleScopedClasses['term-review-item']} */ ;
            let __VLS_569;
            /** @ts-ignore @type {typeof __VLS_components.NCheckbox} */
            NCheckbox;
            // @ts-ignore
            const __VLS_570 = __VLS_asFunctionalComponent1(__VLS_569, new __VLS_569({
                ...{ 'onUpdate:checked': {} },
                checked: (__VLS_ctx.termCandidateSelected.has(candidate.original)),
            }));
            const __VLS_571 = __VLS_570({
                ...{ 'onUpdate:checked': {} },
                checked: (__VLS_ctx.termCandidateSelected.has(candidate.original)),
            }, ...__VLS_functionalComponentArgsRest(__VLS_570));
            let __VLS_574;
            const __VLS_575 = ({ 'update:checked': {} },
                { 'onUpdate:checked': ((v) => __VLS_ctx.toggleTermCandidate(candidate.original, v)) });
            var __VLS_572;
            var __VLS_573;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "term-original" },
            });
            /** @type {__VLS_StyleScopedClasses['term-original']} */ ;
            (candidate.original);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "term-arrow" },
            });
            /** @type {__VLS_StyleScopedClasses['term-arrow']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "term-translation" },
            });
            /** @type {__VLS_StyleScopedClasses['term-translation']} */ ;
            (candidate.translation);
            let __VLS_576;
            /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
            NTag;
            // @ts-ignore
            const __VLS_577 = __VLS_asFunctionalComponent1(__VLS_576, new __VLS_576({
                size: "small",
                bordered: (false),
            }));
            const __VLS_578 = __VLS_577({
                size: "small",
                bordered: (false),
            }, ...__VLS_functionalComponentArgsRest(__VLS_577));
            const { default: __VLS_581 } = __VLS_579.slots;
            (candidate.category);
            // @ts-ignore
            [filteredTexts, tableColumns, showTermReview, termCandidates, termCandidates, termCandidates, termCandidateSelected, termCandidateSelected, toggleTermCandidate,];
            var __VLS_579;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "term-frequency" },
            });
            /** @type {__VLS_StyleScopedClasses['term-frequency']} */ ;
            (candidate.frequency);
            // @ts-ignore
            [];
        }
    }
    {
        const { action: __VLS_582 } = __VLS_566.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "term-review-actions" },
        });
        /** @type {__VLS_StyleScopedClasses['term-review-actions']} */ ;
        let __VLS_583;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_584 = __VLS_asFunctionalComponent1(__VLS_583, new __VLS_583({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
            type: "primary",
        }));
        const __VLS_585 = __VLS_584({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
            type: "primary",
        }, ...__VLS_functionalComponentArgsRest(__VLS_584));
        let __VLS_588;
        const __VLS_589 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.loading))
                        return;
                    if (!(__VLS_ctx.game))
                        return;
                    __VLS_ctx.handleApplyTerms(null);
                    // @ts-ignore
                    [termReviewLoading, handleApplyTerms,];
                } });
        const { default: __VLS_590 } = __VLS_586.slots;
        // @ts-ignore
        [];
        var __VLS_586;
        var __VLS_587;
        let __VLS_591;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_592 = __VLS_asFunctionalComponent1(__VLS_591, new __VLS_591({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
            disabled: (__VLS_ctx.termCandidateSelected.size === 0),
        }));
        const __VLS_593 = __VLS_592({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
            disabled: (__VLS_ctx.termCandidateSelected.size === 0),
        }, ...__VLS_functionalComponentArgsRest(__VLS_592));
        let __VLS_596;
        const __VLS_597 = ({ click: {} },
            { onClick: (__VLS_ctx.handleApplySelected) });
        const { default: __VLS_598 } = __VLS_594.slots;
        (__VLS_ctx.termCandidateSelected.size);
        // @ts-ignore
        [termCandidateSelected, termCandidateSelected, termReviewLoading, handleApplySelected,];
        var __VLS_594;
        var __VLS_595;
        let __VLS_599;
        /** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
        NButton;
        // @ts-ignore
        const __VLS_600 = __VLS_asFunctionalComponent1(__VLS_599, new __VLS_599({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
        }));
        const __VLS_601 = __VLS_600({
            ...{ 'onClick': {} },
            loading: (__VLS_ctx.termReviewLoading),
        }, ...__VLS_functionalComponentArgsRest(__VLS_600));
        let __VLS_604;
        const __VLS_605 = ({ click: {} },
            { onClick: (__VLS_ctx.handleSkipTerms) });
        const { default: __VLS_606 } = __VLS_602.slots;
        // @ts-ignore
        [termReviewLoading, handleSkipTerms,];
        var __VLS_602;
        var __VLS_603;
        // @ts-ignore
        [];
    }
    // @ts-ignore
    [];
    var __VLS_566;
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
