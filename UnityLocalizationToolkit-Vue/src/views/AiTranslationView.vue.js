import { ref, reactive, computed, onMounted, onActivated, onBeforeUnmount, onDeactivated, nextTick, watch } from 'vue';
import { NIcon, NButton, NSwitch, NTag, NProgress, NSlider, NCollapse, NCollapseItem, NPopover, useMessage, } from 'naive-ui';
import { SmartToyOutlined, TranslateOutlined, HourglassEmptyOutlined, SyncOutlined, DashboardOutlined, TokenOutlined, SpeedOutlined, HistoryOutlined, ErrorOutlineOutlined, MoveToInboxOutlined, AutoFixHighOutlined, CheckCircleOutlined, ArrowRightAltOutlined, CloudOutlined, ComputerOutlined, SportsEsportsOutlined, CachedOutlined, ExpandMoreOutlined, StorageOutlined, } from '@vicons/material';
import { useAiTranslationStore } from '@/stores/aiTranslation';
import { useGamesStore } from '@/stores/games';
import { settingsApi } from '@/api/games';
import AiTranslationCard from '@/components/settings/AiTranslationCard.vue';
import LocalAiPanel from '@/components/settings/LocalAiPanel.vue';
import { useAutoSave } from '@/composables/useAutoSave';
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts';
defineOptions({ name: 'AiTranslationView' });
const collapsed = reactive({
    settings: false,
    cache: true,
    recent: true,
    errors: true,
    termSettings: false,
    tmSettings: false,
});
const aiStore = useAiTranslationStore();
const gamesStore = useGamesStore();
const message = useMessage();
const DEFAULT_AI_TRANSLATION = {
    enabled: true,
    activeMode: 'cloud',
    maxConcurrency: 4,
    port: 51821,
    systemPrompt: DEFAULT_SYSTEM_PROMPT,
    temperature: 0.3,
    contextSize: 10,
    localContextSize: 3,
    localMinP: 0.05,
    localRepeatPenalty: 1.0,
    endpoints: [],
    glossaryExtractionEnabled: false,
    enablePreTranslationCache: true,
    termAuditEnabled: true,
    naturalTranslationMode: true,
    enableTranslationMemory: true,
    fuzzyMatchThreshold: 85,
    enableLlmPatternAnalysis: true,
    enableMultiRoundTranslation: true,
    enableAutoTermExtraction: true,
    autoApplyExtractedTerms: false,
};
const settings = ref(null);
const aiSettings = computed({
    get: () => settings.value?.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
    set: (val) => {
        if (settings.value) {
            settings.value = { ...settings.value, aiTranslation: val };
        }
    },
});
const connectionStatus = computed(() => {
    if (!aiStore.stats?.lastRequestAt)
        return 'never';
    const last = new Date(aiStore.stats.lastRequestAt).getTime();
    const diff = Date.now() - last;
    if (diff < 5 * 60 * 1000)
        return 'active';
    if (diff < 60 * 60 * 1000)
        return 'idle';
    return 'stale';
});
const connectionLabel = computed(() => {
    switch (connectionStatus.value) {
        case 'active': return '活跃';
        case 'idle': return '空闲';
        case 'stale': return '已断开';
        case 'never': return '无连接';
    }
});
const successRate = computed(() => {
    const received = aiStore.stats?.totalReceived ?? 0;
    const errors = aiStore.stats?.totalErrors ?? 0;
    if (received === 0)
        return null;
    return ((1 - errors / received) * 100).toFixed(1);
});
const successRateNumber = computed(() => {
    if (successRate.value === null)
        return 100;
    return Number(successRate.value);
});
const lastRequestFormatted = computed(() => {
    if (!aiStore.stats?.lastRequestAt)
        return '从未';
    const date = new Date(aiStore.stats.lastRequestAt);
    return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
});
const isEnabled = computed(() => aiStore.stats?.enabled ?? true);
const isActivelyTranslating = computed(() => (aiStore.stats?.translating ?? 0) > 0);
const tmTotalHits = computed(() => {
    const s = aiStore.stats;
    if (!s)
        return 0;
    return s.translationMemoryHits + s.translationMemoryFuzzyHits + s.translationMemoryPatternHits;
});
const llmCompleted = computed(() => {
    return Math.max(0, (aiStore.stats?.totalTranslated ?? 0) - tmTotalHits.value);
});
const hasTmActivity = computed(() => aiSettings.value.enableTranslationMemory || tmTotalHits.value > 0 || (aiStore.stats?.translationMemoryMisses ?? 0) > 0);
const extractionStats = computed(() => aiStore.extractionStats);
const termAuditTotal = computed(() => {
    const s = aiStore.stats;
    if (!s)
        return 0;
    return s.termAuditPhase1PassCount + s.termAuditPhase2PassCount + s.termAuditForceCorrectedCount;
});
const termAuditPassRate = computed(() => {
    const total = termAuditTotal.value;
    if (total === 0)
        return null;
    const s = aiStore.stats;
    return ((s.termAuditPhase1PassCount / total) * 100).toFixed(1);
});
const showTermAudit = computed(() => (aiStore.stats?.termMatchedTextCount ?? 0) > 0 || termAuditTotal.value > 0);
const showExtraction = computed(() => aiSettings.value.glossaryExtractionEnabled && !!extractionStats.value);
const allSettingsCollapsed = computed(() => collapsed.termSettings && collapsed.tmSettings);
const activeMode = computed(() => aiSettings.value.activeMode ?? 'cloud');
const isLocalMode = computed(() => activeMode.value === 'local');
function setMode(mode) {
    aiSettings.value = { ...aiSettings.value, activeMode: mode };
}
const { enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(() => settings.value, async () => {
    if (!settings.value)
        return;
    try {
        await settingsApi.save(settings.value);
    }
    catch {
        message.error('保存设置失败');
    }
}, { debounceMs: 1000, deep: true });
function getGameName(gameId) {
    const game = gamesStore.games.find(g => g.id === gameId);
    return game?.name ?? gameId;
}
function formatTokens(n) {
    if (n >= 1_000_000)
        return (n / 1_000_000).toFixed(1) + 'M';
    if (n >= 1_000)
        return (n / 1_000).toFixed(1) + 'K';
    return String(n);
}
function formatTime(ms) {
    if (ms >= 1000)
        return (ms / 1000).toFixed(1) + 's';
    return Math.round(ms) + 'ms';
}
function formatRelativeTime(timestamp) {
    const diff = Date.now() - new Date(timestamp).getTime();
    if (diff < 60_000)
        return '刚刚';
    if (diff < 3600_000)
        return Math.floor(diff / 60_000) + '分钟前';
    return Math.floor(diff / 3600_000) + '小时前';
}
async function handleToggle(enabled) {
    try {
        await aiStore.toggleEnabled(enabled);
        message.success(enabled ? 'XUnity AI 翻译已启用' : 'XUnity AI 翻译已停用');
    }
    catch {
        message.error('切换 XUnity AI 翻译状态失败');
    }
}
const isPipelineOverflowing = ref(false);
function trimCompactDecimal(value) {
    return value.toFixed(1).replace(/\.0$/, '');
}
function formatCompactChineseCount(value) {
    const abs = Math.abs(value);
    if (abs < 10_000)
        return String(value);
    if (abs < 100_000_000) {
        const wanValue = Number((value / 10_000).toFixed(1));
        if (Math.abs(wanValue) >= 10_000) {
            return `${trimCompactDecimal(value / 100_000_000)}亿`;
        }
        return `${trimCompactDecimal(wanValue)}万`;
    }
    return `${trimCompactDecimal(value / 100_000_000)}亿`;
}
function formatPipelineCount(value) {
    const resolved = value ?? 0;
    return isPipelineOverflowing.value ? formatCompactChineseCount(resolved) : String(resolved);
}
async function loadSettings() {
    disableAutoSave();
    try {
        const loaded = await settingsApi.get();
        settings.value = {
            ...loaded,
            aiTranslation: loaded.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
        };
    }
    catch {
        // Use defaults
    }
    await nextTick();
    enableAutoSave();
}
onMounted(async () => {
    await aiStore.connect();
    await Promise.all([
        aiStore.fetchStats(),
        aiStore.fetchCacheStats(),
        loadSettings(),
        gamesStore.games.length === 0 ? gamesStore.fetchGames() : Promise.resolve(),
    ]);
    await nextTick();
    await syncPipelineLayout();
    setupPipelineObserver();
});
onActivated(async () => {
    await aiStore.connect();
    await Promise.all([aiStore.fetchStats(), aiStore.fetchCacheStats(), loadSettings()]);
    await nextTick();
    await syncPipelineLayout();
    setupPipelineObserver();
});
onDeactivated(() => {
    aiStore.disconnect();
    cleanupPipeline();
});
onBeforeUnmount(() => {
    aiStore.disconnect();
    cleanupPipeline();
});
// ===== Pipeline SVG Connection Logic =====
const pipelineRef = ref(null);
const rootNodeRef = ref(null);
const queueNodeRef = ref(null);
const translatingNodeRef = ref(null);
const doneNodeRef = ref(null);
const extractionNodeRef = ref(null);
const tmChipsRef = ref(null);
const tmDoneRef = ref(null);
const connections = ref([]);
let pipelineLayoutFrame = null;
function getRelPos(el, container) {
    if (!el)
        return null;
    const r = el.getBoundingClientRect();
    return {
        cx: r.left + r.width / 2 - container.left,
        cy: r.top + r.height / 2 - container.top,
        top: r.top - container.top,
        bottom: r.top + r.height - container.top,
        left: r.left - container.left,
        right: r.left + r.width - container.left,
        w: r.width,
        h: r.height,
    };
}
function particleParams(volume) {
    if (volume <= 0)
        return { count: 0, duration: 5 };
    const count = Math.min(6, Math.max(1, Math.ceil(volume / 5)));
    const duration = Math.max(1.5, 5 - Math.log2(volume + 1) * 0.8);
    return { count, duration };
}
function isDesktopPipelineLayout() {
    return typeof window !== 'undefined' && window.innerWidth > 768;
}
function restoreMeasuredPipelineTexts(root) {
    root.querySelectorAll('[data-full-value]').forEach((el) => {
        el.textContent = el.dataset.fullValue ?? '';
    });
    root.querySelectorAll('[data-full-current]').forEach((el) => {
        const current = el.dataset.fullCurrent ?? '';
        const max = el.dataset.fullMax ?? '';
        let leadingTextNode = Array.from(el.childNodes).find((node) => node.nodeType === Node.TEXT_NODE) ?? null;
        if (!leadingTextNode) {
            leadingTextNode = document.createTextNode('');
            el.insertBefore(leadingTextNode, el.firstChild);
        }
        leadingTextNode.textContent = current;
        const small = el.querySelector('small');
        if (small) {
            small.textContent = max ? `/${max}` : '';
        }
    });
}
function measurePipelineOverflow() {
    if (!pipelineRef.value || !isDesktopPipelineLayout())
        return false;
    const source = pipelineRef.value.querySelector('.pipeline-hbox');
    if (!source)
        return false;
    const wrapper = document.createElement('div');
    Object.assign(wrapper.style, {
        position: 'absolute',
        left: '-100000px',
        top: '0',
        width: `${pipelineRef.value.clientWidth}px`,
        visibility: 'hidden',
        pointerEvents: 'none',
        overflow: 'hidden',
    });
    const clone = source.cloneNode(true);
    restoreMeasuredPipelineTexts(clone);
    wrapper.appendChild(clone);
    document.body.appendChild(wrapper);
    const overflowing = clone.scrollWidth > wrapper.clientWidth + 1;
    wrapper.remove();
    return overflowing;
}
function computeConnections() {
    if (!pipelineRef.value)
        return;
    const container = pipelineRef.value.getBoundingClientRect();
    const root = getRelPos(rootNodeRef.value, container);
    const queue = getRelPos(queueNodeRef.value, container);
    const translating = getRelPos(translatingNodeRef.value, container);
    const done = getRelPos(doneNodeRef.value, container);
    const extraction = getRelPos(extractionNodeRef.value, container);
    const tmChips = getRelPos(tmChipsRef.value, container);
    const tmDone = getRelPos(tmDoneRef.value, container);
    const result = [];
    const s = aiStore.stats;
    // Helper: right-angle path (horizontal then vertical then horizontal)
    function rightAngleH(x1, y1, x2, y2) {
        const midX = x1 + (x2 - x1) * 0.5;
        return `M ${x1} ${y1} L ${midX} ${y1} L ${midX} ${y2} L ${x2} ${y2}`;
    }
    // root → queue
    if (root && queue) {
        const pathD = rightAngleH(root.right, root.cy, queue.left, queue.cy);
        const vol = (s?.queued ?? 0) + (s?.translating ?? 0);
        const pp = particleParams(vol);
        result.push({ id: 'root-queue', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false });
    }
    // queue → translating
    if (queue && translating) {
        const pathD = rightAngleH(queue.right, queue.cy, translating.left, translating.cy);
        const vol = s?.translating ?? 0;
        const pp = particleParams(vol);
        result.push({ id: 'queue-translating', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false });
    }
    // translating → done
    if (translating && done) {
        const pathD = rightAngleH(translating.right, translating.cy, done.left, done.cy);
        const vol = s?.translating ?? 0;
        const pp = particleParams(vol);
        result.push({ id: 'translating-done', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false });
    }
    // done → extraction (horizontal inline)
    if (done && extraction) {
        const pathD = rightAngleH(done.right, done.cy, extraction.left, extraction.cy);
        const vol = extractionStats.value?.activeExtractions ?? 0;
        const pp = particleParams(vol);
        result.push({ id: 'done-extraction', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false });
    }
    // TM animation should only play while translation is actively flowing
    const tmActive = tmTotalHits.value > 0 && (s?.queued ?? 0) + (s?.translating ?? 0) > 0;
    // root → TM node (right-angle fork: right, down, right)
    if (root && tmChips) {
        const pathD = rightAngleH(root.right, root.cy, tmChips.left, tmChips.cy);
        const vol = Math.min(20, tmTotalHits.value);
        const pp = particleParams(vol);
        result.push({ id: 'tm-root-chips', pathD, active: tmActive, particleCount: pp.count, duration: pp.duration, isTm: true });
    }
    // TM node → TM done
    if (tmChips && tmDone) {
        const pathD = rightAngleH(tmChips.right, tmChips.cy, tmDone.left, tmDone.cy);
        const vol = Math.min(20, tmTotalHits.value);
        const pp = particleParams(vol);
        result.push({ id: 'tm-chips-done', pathD, active: tmActive, particleCount: pp.count, duration: pp.duration, isTm: true });
    }
    connections.value = result;
}
async function syncPipelineLayout() {
    const nextOverflowState = measurePipelineOverflow();
    if (nextOverflowState !== isPipelineOverflowing.value) {
        isPipelineOverflowing.value = nextOverflowState;
        await nextTick();
    }
    computeConnections();
}
function schedulePipelineLayout() {
    if (pipelineLayoutFrame !== null) {
        cancelAnimationFrame(pipelineLayoutFrame);
    }
    pipelineLayoutFrame = requestAnimationFrame(() => {
        pipelineLayoutFrame = null;
        void syncPipelineLayout();
    });
}
let pipelineResizeObserver = null;
function setupPipelineObserver() {
    if (!pipelineRef.value || pipelineResizeObserver)
        return;
    pipelineResizeObserver = new ResizeObserver(() => {
        schedulePipelineLayout();
    });
    pipelineResizeObserver.observe(pipelineRef.value);
}
function cleanupPipeline() {
    pipelineResizeObserver?.disconnect();
    pipelineResizeObserver = null;
    if (pipelineLayoutFrame !== null) {
        cancelAnimationFrame(pipelineLayoutFrame);
        pipelineLayoutFrame = null;
    }
}
watch(() => [
    hasTmActivity.value ? 1 : 0,
    showExtraction.value ? 1 : 0,
    aiStore.stats?.totalReceived ?? 0,
    aiStore.stats?.queued ?? 0,
    aiStore.stats?.translating ?? 0,
    aiStore.stats?.maxConcurrency ?? 0,
    llmCompleted.value,
    tmTotalHits.value,
    aiStore.stats?.translationMemoryHits ?? 0,
    aiStore.stats?.translationMemoryFuzzyHits ?? 0,
    aiStore.stats?.translationMemoryPatternHits ?? 0,
    extractionStats.value?.totalExtracted ?? 0,
    extractionStats.value?.totalExtractionCalls ?? 0,
    extractionStats.value?.activeExtractions ?? 0,
    extractionStats.value?.totalErrors ?? 0,
], () => {
    nextTick(schedulePipelineLayout);
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['has-error']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['has-error']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-translating']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['idle']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-dot']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-path']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-path']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-path']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-path']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['flow-particle']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['node-value']} */ ;
/** @type {__VLS_StyleScopedClasses['node-value']} */ ;
/** @type {__VLS_StyleScopedClasses['branch-nodes-llm']} */ ;
/** @type {__VLS_StyleScopedClasses['branch-nodes-llm']} */ ;
/** @type {__VLS_StyleScopedClasses['has-extraction']} */ ;
/** @type {__VLS_StyleScopedClasses['done-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['is-active']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['is-active']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-node']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-inline-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['error-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['success-fill']} */ ;
/** @type {__VLS_StyleScopedClasses['success-fill']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-rate']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-rate']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-rate']} */ ;
/** @type {__VLS_StyleScopedClasses['warn']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['good']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
/** @type {__VLS_StyleScopedClasses['warn']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-item']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-settings']} */ ;
/** @type {__VLS_StyleScopedClasses['settings-group']} */ ;
/** @type {__VLS_StyleScopedClasses['settings-group-header']} */ ;
/** @type {__VLS_StyleScopedClasses['settings-group-body']} */ ;
/** @type {__VLS_StyleScopedClasses['settings-group-body']} */ ;
/** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
/** @type {__VLS_StyleScopedClasses['settings-group-body-inner']} */ ;
/** @type {__VLS_StyleScopedClasses['mode-tab']} */ ;
/** @type {__VLS_StyleScopedClasses['mode-tab']} */ ;
/** @type {__VLS_StyleScopedClasses['active']} */ ;
/** @type {__VLS_StyleScopedClasses['cache-miss-item']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-svg']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-hbox']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-branches']} */ ;
/** @type {__VLS_StyleScopedClasses['branch-nodes']} */ ;
/** @type {__VLS_StyleScopedClasses['branch-nodes-llm']} */ ;
/** @type {__VLS_StyleScopedClasses['llm-node-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['queue-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['translating-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['done-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['extraction-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['root-node']} */ ;
/** @type {__VLS_StyleScopedClasses['stage-node']} */ ;
/** @type {__VLS_StyleScopedClasses['translating-node']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-node']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-done-node']} */ ;
/** @type {__VLS_StyleScopedClasses['extraction-node']} */ ;
/** @type {__VLS_StyleScopedClasses['tm-chips-inline']} */ ;
/** @type {__VLS_StyleScopedClasses['audit-chips']} */ ;
/** @type {__VLS_StyleScopedClasses['page-title-row']} */ ;
/** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
/** @type {__VLS_StyleScopedClasses['connection-time']} */ ;
/** @type {__VLS_StyleScopedClasses['error-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['error-bar-right']} */ ;
/** @type {__VLS_StyleScopedClasses['success-track']} */ ;
/** @type {__VLS_StyleScopedClasses['extraction-select']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-original']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-translated']} */ ;
/** @type {__VLS_StyleScopedClasses['metrics-strip']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
/** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
/** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['root-icon']} */ ;
/** @type {__VLS_StyleScopedClasses['node-value']} */ ;
/** @type {__VLS_StyleScopedClasses['node-value']} */ ;
/** @type {__VLS_StyleScopedClasses['root-value']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-texts']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-arrow']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-meta']} */ ;
/** @type {__VLS_StyleScopedClasses['recent-index']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "ai-page" },
});
/** @type {__VLS_StyleScopedClasses['ai-page']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "page-title-row" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['page-title-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "page-title" },
});
/** @type {__VLS_StyleScopedClasses['page-title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "page-title-icon" },
});
/** @type {__VLS_StyleScopedClasses['page-title-icon']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    size: (24),
}));
const __VLS_2 = __VLS_1({
    size: (24),
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
let __VLS_6;
/** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
SmartToyOutlined;
// @ts-ignore
const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({}));
const __VLS_8 = __VLS_7({}, ...__VLS_functionalComponentArgsRest(__VLS_7));
var __VLS_3;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "enable-toggle" },
});
/** @type {__VLS_StyleScopedClasses['enable-toggle']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "toggle-label" },
});
/** @type {__VLS_StyleScopedClasses['toggle-label']} */ ;
(__VLS_ctx.isEnabled ? '已启用' : '已停用');
let __VLS_11;
/** @ts-ignore @type {typeof __VLS_components.NSwitch} */
NSwitch;
// @ts-ignore
const __VLS_12 = __VLS_asFunctionalComponent1(__VLS_11, new __VLS_11({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.isEnabled),
}));
const __VLS_13 = __VLS_12({
    ...{ 'onUpdate:value': {} },
    value: (__VLS_ctx.isEnabled),
}, ...__VLS_functionalComponentArgsRest(__VLS_12));
let __VLS_16;
const __VLS_17 = ({ 'update:value': {} },
    { 'onUpdate:value': (__VLS_ctx.handleToggle) });
var __VLS_14;
var __VLS_15;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metrics-strip" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['metrics-strip']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-pill" },
});
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-icon" },
});
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
let __VLS_18;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_19 = __VLS_asFunctionalComponent1(__VLS_18, new __VLS_18({
    size: (14),
}));
const __VLS_20 = __VLS_19({
    size: (14),
}, ...__VLS_functionalComponentArgsRest(__VLS_19));
const { default: __VLS_23 } = __VLS_21.slots;
let __VLS_24;
/** @ts-ignore @type {typeof __VLS_components.MoveToInboxOutlined} */
MoveToInboxOutlined;
// @ts-ignore
const __VLS_25 = __VLS_asFunctionalComponent1(__VLS_24, new __VLS_24({}));
const __VLS_26 = __VLS_25({}, ...__VLS_functionalComponentArgsRest(__VLS_25));
// @ts-ignore
[isEnabled, isEnabled, handleToggle,];
var __VLS_21;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-data" },
});
/** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-value" },
});
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
(__VLS_ctx.aiStore.stats?.totalReceived ?? 0);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-label" },
});
/** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-pill" },
    ...{ class: ({ 'rate-good': __VLS_ctx.successRateNumber >= 95, 'rate-warn': __VLS_ctx.successRateNumber < 95 && __VLS_ctx.successRateNumber >= 80, 'rate-bad': __VLS_ctx.successRateNumber < 80 }) },
});
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
/** @type {__VLS_StyleScopedClasses['rate-good']} */ ;
/** @type {__VLS_StyleScopedClasses['rate-warn']} */ ;
/** @type {__VLS_StyleScopedClasses['rate-bad']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-icon" },
});
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
let __VLS_29;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_30 = __VLS_asFunctionalComponent1(__VLS_29, new __VLS_29({
    size: (14),
}));
const __VLS_31 = __VLS_30({
    size: (14),
}, ...__VLS_functionalComponentArgsRest(__VLS_30));
const { default: __VLS_34 } = __VLS_32.slots;
let __VLS_35;
/** @ts-ignore @type {typeof __VLS_components.CheckCircleOutlined} */
CheckCircleOutlined;
// @ts-ignore
const __VLS_36 = __VLS_asFunctionalComponent1(__VLS_35, new __VLS_35({}));
const __VLS_37 = __VLS_36({}, ...__VLS_functionalComponentArgsRest(__VLS_36));
// @ts-ignore
[aiStore, successRateNumber, successRateNumber, successRateNumber, successRateNumber,];
var __VLS_32;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-data" },
});
/** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-value" },
});
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
(__VLS_ctx.successRate ?? '--');
__VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-label" },
});
/** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-pill" },
});
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-icon" },
});
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
let __VLS_40;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_41 = __VLS_asFunctionalComponent1(__VLS_40, new __VLS_40({
    size: (14),
}));
const __VLS_42 = __VLS_41({
    size: (14),
}, ...__VLS_functionalComponentArgsRest(__VLS_41));
const { default: __VLS_45 } = __VLS_43.slots;
let __VLS_46;
/** @ts-ignore @type {typeof __VLS_components.SpeedOutlined} */
SpeedOutlined;
// @ts-ignore
const __VLS_47 = __VLS_asFunctionalComponent1(__VLS_46, new __VLS_46({}));
const __VLS_48 = __VLS_47({}, ...__VLS_functionalComponentArgsRest(__VLS_47));
// @ts-ignore
[successRate,];
var __VLS_43;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-data" },
});
/** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-value" },
});
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
(__VLS_ctx.formatTime(__VLS_ctx.aiStore.stats?.averageResponseTimeMs ?? 0));
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-label" },
});
/** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
(__VLS_ctx.aiStore.stats?.requestsPerMinute ?? 0);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-pill" },
});
/** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-icon" },
});
/** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
let __VLS_51;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_52 = __VLS_asFunctionalComponent1(__VLS_51, new __VLS_51({
    size: (14),
}));
const __VLS_53 = __VLS_52({
    size: (14),
}, ...__VLS_functionalComponentArgsRest(__VLS_52));
const { default: __VLS_56 } = __VLS_54.slots;
let __VLS_57;
/** @ts-ignore @type {typeof __VLS_components.TokenOutlined} */
TokenOutlined;
// @ts-ignore
const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({}));
const __VLS_59 = __VLS_58({}, ...__VLS_functionalComponentArgsRest(__VLS_58));
// @ts-ignore
[aiStore, aiStore, formatTime,];
var __VLS_54;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "metric-data" },
});
/** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-value" },
});
/** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
(__VLS_ctx.formatTokens(__VLS_ctx.aiStore.stats?.totalTokensUsed ?? 0));
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "metric-label" },
});
/** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "section-card" },
    ...{ class: ({ 'is-translating': __VLS_ctx.isActivelyTranslating }) },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['section-card']} */ ;
/** @type {__VLS_StyleScopedClasses['is-translating']} */ ;
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
let __VLS_62;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_63 = __VLS_asFunctionalComponent1(__VLS_62, new __VLS_62({
    size: (16),
}));
const __VLS_64 = __VLS_63({
    size: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_63));
const { default: __VLS_67 } = __VLS_65.slots;
let __VLS_68;
/** @ts-ignore @type {typeof __VLS_components.DashboardOutlined} */
DashboardOutlined;
// @ts-ignore
const __VLS_69 = __VLS_asFunctionalComponent1(__VLS_68, new __VLS_68({}));
const __VLS_70 = __VLS_69({}, ...__VLS_functionalComponentArgsRest(__VLS_69));
// @ts-ignore
[aiStore, formatTokens, isActivelyTranslating,];
var __VLS_65;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "header-actions" },
});
/** @type {__VLS_StyleScopedClasses['header-actions']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "connection-badge" },
    ...{ class: (__VLS_ctx.connectionStatus) },
});
/** @type {__VLS_StyleScopedClasses['connection-badge']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "connection-dot" },
});
/** @type {__VLS_StyleScopedClasses['connection-dot']} */ ;
(__VLS_ctx.connectionLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "connection-time" },
});
/** @type {__VLS_StyleScopedClasses['connection-time']} */ ;
(__VLS_ctx.lastRequestFormatted);
let __VLS_73;
/** @ts-ignore @type {typeof __VLS_components.NButton | typeof __VLS_components.NButton} */
NButton;
// @ts-ignore
const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
    ...{ 'onClick': {} },
    size: "small",
    quaternary: true,
}));
const __VLS_75 = __VLS_74({
    ...{ 'onClick': {} },
    size: "small",
    quaternary: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_74));
let __VLS_78;
const __VLS_79 = ({ click: {} },
    { onClick: (...[$event]) => {
            __VLS_ctx.aiStore.fetchStats();
            // @ts-ignore
            [aiStore, connectionStatus, connectionLabel, lastRequestFormatted,];
        } });
const { default: __VLS_80 } = __VLS_76.slots;
// @ts-ignore
[];
var __VLS_76;
var __VLS_77;
if (__VLS_ctx.aiStore.stats?.currentGameId) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "current-game-indicator" },
    });
    /** @type {__VLS_StyleScopedClasses['current-game-indicator']} */ ;
    let __VLS_81;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_82 = __VLS_asFunctionalComponent1(__VLS_81, new __VLS_81({
        size: (14),
    }));
    const __VLS_83 = __VLS_82({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_82));
    const { default: __VLS_86 } = __VLS_84.slots;
    let __VLS_87;
    /** @ts-ignore @type {typeof __VLS_components.SportsEsportsOutlined} */
    SportsEsportsOutlined;
    // @ts-ignore
    const __VLS_88 = __VLS_asFunctionalComponent1(__VLS_87, new __VLS_87({}));
    const __VLS_89 = __VLS_88({}, ...__VLS_functionalComponentArgsRest(__VLS_88));
    // @ts-ignore
    [aiStore,];
    var __VLS_84;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "current-game-label" },
    });
    /** @type {__VLS_StyleScopedClasses['current-game-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "current-game-name" },
    });
    /** @type {__VLS_StyleScopedClasses['current-game-name']} */ ;
    (__VLS_ctx.getGameName(__VLS_ctx.aiStore.stats.currentGameId));
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-flow" },
    ref: "pipelineRef",
});
/** @type {__VLS_StyleScopedClasses['pipeline-flow']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    ...{ class: "pipeline-svg" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-svg']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.defs, __VLS_intrinsics.defs)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.filter, __VLS_intrinsics.filter)({
    id: "particle-glow",
    x: "-50%",
    y: "-50%",
    width: "200%",
    height: "200%",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.feGaussianBlur)({
    stdDeviation: "3",
    result: "blur",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.feMerge, __VLS_intrinsics.feMerge)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.feMergeNode)({
    in: "blur",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.feMergeNode)({
    in: "SourceGraphic",
});
for (const [conn] of __VLS_vFor((__VLS_ctx.connections))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        key: (conn.id),
        d: (conn.pathD),
        ...{ class: "connection-path" },
        ...{ class: ({ active: conn.active, 'tm-path': conn.isTm }) },
    });
    /** @type {__VLS_StyleScopedClasses['connection-path']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    /** @type {__VLS_StyleScopedClasses['tm-path']} */ ;
    // @ts-ignore
    [aiStore, getGameName, connections,];
}
for (const [conn] of __VLS_vFor((__VLS_ctx.connections))) {
    ('particles-' + conn.id);
    for (const [i] of __VLS_vFor(((conn.active ? conn.particleCount : 0)))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
            key: (conn.id + '-p-' + i),
            ...{ class: "flow-particle" },
            ...{ class: ({ 'tm-particle': conn.isTm }) },
            r: "3",
            filter: "url(#particle-glow)",
            ...{ style: ({
                    offsetPath: `path('${conn.pathD}')`,
                    animationDuration: conn.duration + 's',
                    animationDelay: ((i - 1) * (conn.duration / conn.particleCount)) + 's',
                }) },
        });
        /** @type {__VLS_StyleScopedClasses['flow-particle']} */ ;
        /** @type {__VLS_StyleScopedClasses['tm-particle']} */ ;
        // @ts-ignore
        [connections,];
    }
    // @ts-ignore
    [];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-hbox" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-hbox']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-root" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-root']} */ ;
let __VLS_92;
/** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
NPopover;
// @ts-ignore
const __VLS_93 = __VLS_asFunctionalComponent1(__VLS_92, new __VLS_92({
    trigger: "hover",
    placement: "bottom",
}));
const __VLS_94 = __VLS_93({
    trigger: "hover",
    placement: "bottom",
}, ...__VLS_functionalComponentArgsRest(__VLS_93));
const { default: __VLS_97 } = __VLS_95.slots;
{
    const { trigger: __VLS_98 } = __VLS_95.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ref: "rootNodeRef",
        ...{ class: "pipeline-node root-node" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['root-node']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-icon root-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['root-icon']} */ ;
    let __VLS_99;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_100 = __VLS_asFunctionalComponent1(__VLS_99, new __VLS_99({
        size: (20),
    }));
    const __VLS_101 = __VLS_100({
        size: (20),
    }, ...__VLS_functionalComponentArgsRest(__VLS_100));
    const { default: __VLS_104 } = __VLS_102.slots;
    let __VLS_105;
    /** @ts-ignore @type {typeof __VLS_components.MoveToInboxOutlined} */
    MoveToInboxOutlined;
    // @ts-ignore
    const __VLS_106 = __VLS_asFunctionalComponent1(__VLS_105, new __VLS_105({}));
    const __VLS_107 = __VLS_106({}, ...__VLS_functionalComponentArgsRest(__VLS_106));
    // @ts-ignore
    [];
    var __VLS_102;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-data" },
    });
    /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-value root-value" },
        'data-full-value': (String(__VLS_ctx.aiStore.stats?.totalReceived ?? 0)),
    });
    /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
    /** @type {__VLS_StyleScopedClasses['root-value']} */ ;
    (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.totalReceived ?? 0));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-label" },
    });
    /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
    // @ts-ignore
    [aiStore, aiStore, formatPipelineCount,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-tooltip" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
// @ts-ignore
[];
var __VLS_95;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-branches" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-branches']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-branch llm-branch" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-branch']} */ ;
/** @type {__VLS_StyleScopedClasses['llm-branch']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "branch-header" },
});
/** @type {__VLS_StyleScopedClasses['branch-header']} */ ;
let __VLS_110;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_111 = __VLS_asFunctionalComponent1(__VLS_110, new __VLS_110({
    size: (13),
}));
const __VLS_112 = __VLS_111({
    size: (13),
}, ...__VLS_functionalComponentArgsRest(__VLS_111));
const { default: __VLS_115 } = __VLS_113.slots;
let __VLS_116;
/** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
SmartToyOutlined;
// @ts-ignore
const __VLS_117 = __VLS_asFunctionalComponent1(__VLS_116, new __VLS_116({}));
const __VLS_118 = __VLS_117({}, ...__VLS_functionalComponentArgsRest(__VLS_117));
// @ts-ignore
[];
var __VLS_113;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "branch-nodes branch-nodes-llm" },
    ...{ class: ({ 'has-extraction': __VLS_ctx.showExtraction }) },
});
/** @type {__VLS_StyleScopedClasses['branch-nodes']} */ ;
/** @type {__VLS_StyleScopedClasses['branch-nodes-llm']} */ ;
/** @type {__VLS_StyleScopedClasses['has-extraction']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "llm-node-slot queue-slot" },
});
/** @type {__VLS_StyleScopedClasses['llm-node-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['queue-slot']} */ ;
let __VLS_121;
/** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
NPopover;
// @ts-ignore
const __VLS_122 = __VLS_asFunctionalComponent1(__VLS_121, new __VLS_121({
    trigger: "hover",
    placement: "top",
}));
const __VLS_123 = __VLS_122({
    trigger: "hover",
    placement: "top",
}, ...__VLS_functionalComponentArgsRest(__VLS_122));
const { default: __VLS_126 } = __VLS_124.slots;
{
    const { trigger: __VLS_127 } = __VLS_124.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ref: "queueNodeRef",
        ...{ class: "pipeline-node stage-node queue-node" },
        ...{ class: ({ dimmed: (__VLS_ctx.aiStore.stats?.queued ?? 0) === 0 }) },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['stage-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['queue-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['dimmed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
    let __VLS_128;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_129 = __VLS_asFunctionalComponent1(__VLS_128, new __VLS_128({
        size: (14),
    }));
    const __VLS_130 = __VLS_129({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_129));
    const { default: __VLS_133 } = __VLS_131.slots;
    let __VLS_134;
    /** @ts-ignore @type {typeof __VLS_components.HourglassEmptyOutlined} */
    HourglassEmptyOutlined;
    // @ts-ignore
    const __VLS_135 = __VLS_asFunctionalComponent1(__VLS_134, new __VLS_134({}));
    const __VLS_136 = __VLS_135({}, ...__VLS_functionalComponentArgsRest(__VLS_135));
    // @ts-ignore
    [aiStore, showExtraction,];
    var __VLS_131;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-data" },
    });
    /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-value" },
        'data-full-value': (String(__VLS_ctx.aiStore.stats?.queued ?? 0)),
    });
    /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
    (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.queued ?? 0));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-label" },
    });
    /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
    // @ts-ignore
    [aiStore, aiStore, formatPipelineCount,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-tooltip" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
// @ts-ignore
[];
var __VLS_124;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "llm-node-slot translating-slot" },
});
/** @type {__VLS_StyleScopedClasses['llm-node-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['translating-slot']} */ ;
let __VLS_139;
/** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
NPopover;
// @ts-ignore
const __VLS_140 = __VLS_asFunctionalComponent1(__VLS_139, new __VLS_139({
    trigger: "hover",
    placement: "top",
}));
const __VLS_141 = __VLS_140({
    trigger: "hover",
    placement: "top",
}, ...__VLS_functionalComponentArgsRest(__VLS_140));
const { default: __VLS_144 } = __VLS_142.slots;
{
    const { trigger: __VLS_145 } = __VLS_142.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ref: "translatingNodeRef",
        ...{ class: "pipeline-node stage-node translating-node" },
        ...{ class: ({ 'is-active': __VLS_ctx.isActivelyTranslating }) },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['stage-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['translating-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['is-active']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-icon translating-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['translating-icon']} */ ;
    let __VLS_146;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_147 = __VLS_asFunctionalComponent1(__VLS_146, new __VLS_146({
        size: (14),
    }));
    const __VLS_148 = __VLS_147({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_147));
    const { default: __VLS_151 } = __VLS_149.slots;
    let __VLS_152;
    /** @ts-ignore @type {typeof __VLS_components.SyncOutlined} */
    SyncOutlined;
    // @ts-ignore
    const __VLS_153 = __VLS_asFunctionalComponent1(__VLS_152, new __VLS_152({}));
    const __VLS_154 = __VLS_153({}, ...__VLS_functionalComponentArgsRest(__VLS_153));
    // @ts-ignore
    [isActivelyTranslating,];
    var __VLS_149;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-data" },
    });
    /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-value" },
        'data-full-current': (String(__VLS_ctx.aiStore.stats?.translating ?? 0)),
        'data-full-max': (__VLS_ctx.aiStore.stats?.maxConcurrency ? String(__VLS_ctx.aiStore.stats.maxConcurrency) : ''),
    });
    /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
    (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.translating ?? 0));
    if (__VLS_ctx.aiStore.stats?.maxConcurrency) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats.maxConcurrency));
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-label" },
    });
    /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
    // @ts-ignore
    [aiStore, aiStore, aiStore, aiStore, aiStore, aiStore, formatPipelineCount, formatPipelineCount,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-tooltip" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
// @ts-ignore
[];
var __VLS_142;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "llm-node-slot done-slot" },
});
/** @type {__VLS_StyleScopedClasses['llm-node-slot']} */ ;
/** @type {__VLS_StyleScopedClasses['done-slot']} */ ;
let __VLS_157;
/** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
NPopover;
// @ts-ignore
const __VLS_158 = __VLS_asFunctionalComponent1(__VLS_157, new __VLS_157({
    trigger: "hover",
    placement: "top",
}));
const __VLS_159 = __VLS_158({
    trigger: "hover",
    placement: "top",
}, ...__VLS_functionalComponentArgsRest(__VLS_158));
const { default: __VLS_162 } = __VLS_160.slots;
{
    const { trigger: __VLS_163 } = __VLS_160.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ref: "doneNodeRef",
        ...{ class: "pipeline-node stage-node done-node" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['stage-node']} */ ;
    /** @type {__VLS_StyleScopedClasses['done-node']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-icon done-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['done-icon']} */ ;
    let __VLS_164;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_165 = __VLS_asFunctionalComponent1(__VLS_164, new __VLS_164({
        size: (14),
    }));
    const __VLS_166 = __VLS_165({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_165));
    const { default: __VLS_169 } = __VLS_167.slots;
    let __VLS_170;
    /** @ts-ignore @type {typeof __VLS_components.TranslateOutlined} */
    TranslateOutlined;
    // @ts-ignore
    const __VLS_171 = __VLS_asFunctionalComponent1(__VLS_170, new __VLS_170({}));
    const __VLS_172 = __VLS_171({}, ...__VLS_functionalComponentArgsRest(__VLS_171));
    // @ts-ignore
    [];
    var __VLS_167;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "node-data" },
    });
    /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-value" },
        'data-full-value': (String(__VLS_ctx.llmCompleted)),
    });
    /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
    (__VLS_ctx.formatPipelineCount(__VLS_ctx.llmCompleted));
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "node-label" },
    });
    /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
    // @ts-ignore
    [formatPipelineCount, llmCompleted, llmCompleted,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pipeline-tooltip" },
});
/** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
// @ts-ignore
[];
var __VLS_160;
if (__VLS_ctx.showExtraction) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "llm-node-slot extraction-slot" },
    });
    /** @type {__VLS_StyleScopedClasses['llm-node-slot']} */ ;
    /** @type {__VLS_StyleScopedClasses['extraction-slot']} */ ;
    let __VLS_175;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_176 = __VLS_asFunctionalComponent1(__VLS_175, new __VLS_175({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_177 = __VLS_176({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_176));
    const { default: __VLS_180 } = __VLS_178.slots;
    {
        const { trigger: __VLS_181 } = __VLS_178.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ref: "extractionNodeRef",
            ...{ class: "pipeline-node stage-node extraction-node" },
        });
        /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
        /** @type {__VLS_StyleScopedClasses['stage-node']} */ ;
        /** @type {__VLS_StyleScopedClasses['extraction-node']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "node-icon extraction-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['extraction-icon']} */ ;
        let __VLS_182;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_183 = __VLS_asFunctionalComponent1(__VLS_182, new __VLS_182({
            size: (14),
        }));
        const __VLS_184 = __VLS_183({
            size: (14),
        }, ...__VLS_functionalComponentArgsRest(__VLS_183));
        const { default: __VLS_187 } = __VLS_185.slots;
        let __VLS_188;
        /** @ts-ignore @type {typeof __VLS_components.AutoFixHighOutlined} */
        AutoFixHighOutlined;
        // @ts-ignore
        const __VLS_189 = __VLS_asFunctionalComponent1(__VLS_188, new __VLS_188({}));
        const __VLS_190 = __VLS_189({}, ...__VLS_functionalComponentArgsRest(__VLS_189));
        // @ts-ignore
        [showExtraction,];
        var __VLS_185;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "node-data" },
        });
        /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "node-value" },
            'data-full-value': (String(__VLS_ctx.extractionStats.totalExtracted)),
        });
        /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.extractionStats.totalExtracted));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "node-label" },
        });
        /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
        // @ts-ignore
        [formatPipelineCount, extractionStats, extractionStats,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "pipeline-tooltip" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
    // @ts-ignore
    [];
    var __VLS_178;
}
if (__VLS_ctx.hasTmActivity) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "pipeline-branch tm-branch" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-branch']} */ ;
    /** @type {__VLS_StyleScopedClasses['tm-branch']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "branch-header tm-header" },
    });
    /** @type {__VLS_StyleScopedClasses['branch-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['tm-header']} */ ;
    let __VLS_193;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_194 = __VLS_asFunctionalComponent1(__VLS_193, new __VLS_193({
        size: (13),
    }));
    const __VLS_195 = __VLS_194({
        size: (13),
    }, ...__VLS_functionalComponentArgsRest(__VLS_194));
    const { default: __VLS_198 } = __VLS_196.slots;
    let __VLS_199;
    /** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
    StorageOutlined;
    // @ts-ignore
    const __VLS_200 = __VLS_asFunctionalComponent1(__VLS_199, new __VLS_199({}));
    const __VLS_201 = __VLS_200({}, ...__VLS_functionalComponentArgsRest(__VLS_200));
    // @ts-ignore
    [hasTmActivity,];
    var __VLS_196;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "branch-nodes branch-nodes-tm" },
    });
    /** @type {__VLS_StyleScopedClasses['branch-nodes']} */ ;
    /** @type {__VLS_StyleScopedClasses['branch-nodes-tm']} */ ;
    let __VLS_204;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_205 = __VLS_asFunctionalComponent1(__VLS_204, new __VLS_204({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_206 = __VLS_205({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_205));
    const { default: __VLS_209 } = __VLS_207.slots;
    {
        const { trigger: __VLS_210 } = __VLS_207.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ref: "tmChipsRef",
            ...{ class: "pipeline-node tm-node" },
        });
        /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
        /** @type {__VLS_StyleScopedClasses['tm-node']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "node-icon tm-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['tm-icon']} */ ;
        let __VLS_211;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_212 = __VLS_asFunctionalComponent1(__VLS_211, new __VLS_211({
            size: (14),
        }));
        const __VLS_213 = __VLS_212({
            size: (14),
        }, ...__VLS_functionalComponentArgsRest(__VLS_212));
        const { default: __VLS_216 } = __VLS_214.slots;
        let __VLS_217;
        /** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
        StorageOutlined;
        // @ts-ignore
        const __VLS_218 = __VLS_asFunctionalComponent1(__VLS_217, new __VLS_217({}));
        const __VLS_219 = __VLS_218({}, ...__VLS_functionalComponentArgsRest(__VLS_218));
        // @ts-ignore
        [];
        var __VLS_214;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "tm-chips-inline" },
        });
        /** @type {__VLS_StyleScopedClasses['tm-chips-inline']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tm-inline-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['tm-inline-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({
            'data-full-value': (String(__VLS_ctx.aiStore.stats?.translationMemoryHits ?? 0)),
        });
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.translationMemoryHits ?? 0));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tm-inline-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['tm-inline-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({
            'data-full-value': (String(__VLS_ctx.aiStore.stats?.translationMemoryFuzzyHits ?? 0)),
        });
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.translationMemoryFuzzyHits ?? 0));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "tm-inline-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['tm-inline-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({
            'data-full-value': (String(__VLS_ctx.aiStore.stats?.translationMemoryPatternHits ?? 0)),
        });
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.aiStore.stats?.translationMemoryPatternHits ?? 0));
        // @ts-ignore
        [aiStore, aiStore, aiStore, aiStore, aiStore, aiStore, formatPipelineCount, formatPipelineCount, formatPipelineCount,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "pipeline-tooltip" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
    // @ts-ignore
    [];
    var __VLS_207;
    let __VLS_222;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_223 = __VLS_asFunctionalComponent1(__VLS_222, new __VLS_222({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_224 = __VLS_223({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_223));
    const { default: __VLS_227 } = __VLS_225.slots;
    {
        const { trigger: __VLS_228 } = __VLS_225.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ref: "tmDoneRef",
            ...{ class: "pipeline-node tm-done-node" },
        });
        /** @type {__VLS_StyleScopedClasses['pipeline-node']} */ ;
        /** @type {__VLS_StyleScopedClasses['tm-done-node']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "node-icon tm-icon" },
        });
        /** @type {__VLS_StyleScopedClasses['node-icon']} */ ;
        /** @type {__VLS_StyleScopedClasses['tm-icon']} */ ;
        let __VLS_229;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_230 = __VLS_asFunctionalComponent1(__VLS_229, new __VLS_229({
            size: (14),
        }));
        const __VLS_231 = __VLS_230({
            size: (14),
        }, ...__VLS_functionalComponentArgsRest(__VLS_230));
        const { default: __VLS_234 } = __VLS_232.slots;
        let __VLS_235;
        /** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
        StorageOutlined;
        // @ts-ignore
        const __VLS_236 = __VLS_asFunctionalComponent1(__VLS_235, new __VLS_235({}));
        const __VLS_237 = __VLS_236({}, ...__VLS_functionalComponentArgsRest(__VLS_236));
        // @ts-ignore
        [];
        var __VLS_232;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "node-data" },
        });
        /** @type {__VLS_StyleScopedClasses['node-data']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "node-value" },
            'data-full-value': (String(__VLS_ctx.tmTotalHits)),
        });
        /** @type {__VLS_StyleScopedClasses['node-value']} */ ;
        (__VLS_ctx.formatPipelineCount(__VLS_ctx.tmTotalHits));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "node-label" },
        });
        /** @type {__VLS_StyleScopedClasses['node-label']} */ ;
        // @ts-ignore
        [formatPipelineCount, tmTotalHits, tmTotalHits,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "pipeline-tooltip" },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-tooltip']} */ ;
    // @ts-ignore
    [];
    var __VLS_225;
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "error-bar" },
    ...{ class: ({ 'has-errors': (__VLS_ctx.aiStore.stats?.totalErrors ?? 0) > 0 }) },
});
/** @type {__VLS_StyleScopedClasses['error-bar']} */ ;
/** @type {__VLS_StyleScopedClasses['has-errors']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "error-bar-left" },
});
/** @type {__VLS_StyleScopedClasses['error-bar-left']} */ ;
let __VLS_240;
/** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
NIcon;
// @ts-ignore
const __VLS_241 = __VLS_asFunctionalComponent1(__VLS_240, new __VLS_240({
    size: (15),
}));
const __VLS_242 = __VLS_241({
    size: (15),
}, ...__VLS_functionalComponentArgsRest(__VLS_241));
const { default: __VLS_245 } = __VLS_243.slots;
let __VLS_246;
/** @ts-ignore @type {typeof __VLS_components.ErrorOutlineOutlined} */
ErrorOutlineOutlined;
// @ts-ignore
const __VLS_247 = __VLS_asFunctionalComponent1(__VLS_246, new __VLS_246({}));
const __VLS_248 = __VLS_247({}, ...__VLS_functionalComponentArgsRest(__VLS_247));
// @ts-ignore
[aiStore,];
var __VLS_243;
if ((__VLS_ctx.aiStore.stats?.totalErrors ?? 0) > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-bar-text" },
    });
    /** @type {__VLS_StyleScopedClasses['error-bar-text']} */ ;
    (__VLS_ctx.aiStore.stats.totalErrors);
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-bar-text" },
    });
    /** @type {__VLS_StyleScopedClasses['error-bar-text']} */ ;
}
if (__VLS_ctx.successRate !== null) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "error-bar-right" },
    });
    /** @type {__VLS_StyleScopedClasses['error-bar-right']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "success-track" },
    });
    /** @type {__VLS_StyleScopedClasses['success-track']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "success-fill" },
        ...{ class: ({ warn: __VLS_ctx.successRateNumber < 95, bad: __VLS_ctx.successRateNumber < 80 }) },
        ...{ style: ({ width: __VLS_ctx.successRate + '%' }) },
    });
    /** @type {__VLS_StyleScopedClasses['success-fill']} */ ;
    /** @type {__VLS_StyleScopedClasses['warn']} */ ;
    /** @type {__VLS_StyleScopedClasses['bad']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-bar-rate" },
    });
    /** @type {__VLS_StyleScopedClasses['error-bar-rate']} */ ;
    (__VLS_ctx.successRate);
}
if (__VLS_ctx.showTermAudit) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "audit-strip" },
    });
    /** @type {__VLS_StyleScopedClasses['audit-strip']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "audit-header" },
    });
    /** @type {__VLS_StyleScopedClasses['audit-header']} */ ;
    let __VLS_251;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_252 = __VLS_asFunctionalComponent1(__VLS_251, new __VLS_251({
        size: (13),
    }));
    const __VLS_253 = __VLS_252({
        size: (13),
    }, ...__VLS_functionalComponentArgsRest(__VLS_252));
    const { default: __VLS_256 } = __VLS_254.slots;
    let __VLS_257;
    /** @ts-ignore @type {typeof __VLS_components.AutoFixHighOutlined} */
    AutoFixHighOutlined;
    // @ts-ignore
    const __VLS_258 = __VLS_asFunctionalComponent1(__VLS_257, new __VLS_257({}));
    const __VLS_259 = __VLS_258({}, ...__VLS_functionalComponentArgsRest(__VLS_258));
    // @ts-ignore
    [aiStore, aiStore, successRateNumber, successRateNumber, successRate, successRate, successRate, showTermAudit,];
    var __VLS_254;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    if (__VLS_ctx.termAuditPassRate !== null) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-rate" },
            ...{ class: ({ good: Number(__VLS_ctx.termAuditPassRate) >= 80, warn: Number(__VLS_ctx.termAuditPassRate) < 80 }) },
        });
        /** @type {__VLS_StyleScopedClasses['audit-rate']} */ ;
        /** @type {__VLS_StyleScopedClasses['good']} */ ;
        /** @type {__VLS_StyleScopedClasses['warn']} */ ;
        (__VLS_ctx.termAuditPassRate);
        __VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "audit-chips" },
    });
    /** @type {__VLS_StyleScopedClasses['audit-chips']} */ ;
    let __VLS_262;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_263 = __VLS_asFunctionalComponent1(__VLS_262, new __VLS_262({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_264 = __VLS_263({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_263));
    const { default: __VLS_267 } = __VLS_265.slots;
    {
        const { trigger: __VLS_268 } = __VLS_265.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-value" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
        (__VLS_ctx.aiStore.stats?.termMatchedTextCount ?? 0);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-label" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-label']} */ ;
        // @ts-ignore
        [aiStore, termAuditPassRate, termAuditPassRate, termAuditPassRate, termAuditPassRate,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    // @ts-ignore
    [];
    var __VLS_265;
    let __VLS_269;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_270 = __VLS_asFunctionalComponent1(__VLS_269, new __VLS_269({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_271 = __VLS_270({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_270));
    const { default: __VLS_274 } = __VLS_272.slots;
    {
        const { trigger: __VLS_275 } = __VLS_272.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-chip good" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
        /** @type {__VLS_StyleScopedClasses['good']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-value" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
        (__VLS_ctx.aiStore.stats?.termAuditPhase1PassCount ?? 0);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-label" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-label']} */ ;
        // @ts-ignore
        [aiStore,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    // @ts-ignore
    [];
    var __VLS_272;
    let __VLS_276;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_277 = __VLS_asFunctionalComponent1(__VLS_276, new __VLS_276({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_278 = __VLS_277({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_277));
    const { default: __VLS_281 } = __VLS_279.slots;
    {
        const { trigger: __VLS_282 } = __VLS_279.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-chip" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-value" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
        (__VLS_ctx.aiStore.stats?.termAuditPhase2PassCount ?? 0);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-label" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-label']} */ ;
        // @ts-ignore
        [aiStore,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    // @ts-ignore
    [];
    var __VLS_279;
    let __VLS_283;
    /** @ts-ignore @type {typeof __VLS_components.NPopover | typeof __VLS_components.NPopover} */
    NPopover;
    // @ts-ignore
    const __VLS_284 = __VLS_asFunctionalComponent1(__VLS_283, new __VLS_283({
        trigger: "hover",
        placement: "top",
    }));
    const __VLS_285 = __VLS_284({
        trigger: "hover",
        placement: "top",
    }, ...__VLS_functionalComponentArgsRest(__VLS_284));
    const { default: __VLS_288 } = __VLS_286.slots;
    {
        const { trigger: __VLS_289 } = __VLS_286.slots;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-chip" },
            ...{ class: ({ warn: (__VLS_ctx.aiStore.stats?.termAuditForceCorrectedCount ?? 0) > 0 }) },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip']} */ ;
        /** @type {__VLS_StyleScopedClasses['warn']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-value" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-value']} */ ;
        (__VLS_ctx.aiStore.stats?.termAuditForceCorrectedCount ?? 0);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "audit-chip-label" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-chip-label']} */ ;
        // @ts-ignore
        [aiStore, aiStore,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ style: {} },
    });
    // @ts-ignore
    [];
    var __VLS_286;
    if (__VLS_ctx.termAuditTotal > 0) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-bar" },
        });
        /** @type {__VLS_StyleScopedClasses['audit-bar']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-seg phase1" },
            ...{ style: ({ flex: __VLS_ctx.aiStore.stats?.termAuditPhase1PassCount ?? 0 }) },
        });
        /** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
        /** @type {__VLS_StyleScopedClasses['phase1']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-seg phase2" },
            ...{ style: ({ flex: __VLS_ctx.aiStore.stats?.termAuditPhase2PassCount ?? 0 }) },
        });
        /** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
        /** @type {__VLS_StyleScopedClasses['phase2']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "audit-seg forced" },
            ...{ style: ({ flex: __VLS_ctx.aiStore.stats?.termAuditForceCorrectedCount ?? 0 }) },
        });
        /** @type {__VLS_StyleScopedClasses['audit-seg']} */ ;
        /** @type {__VLS_StyleScopedClasses['forced']} */ ;
    }
}
if (__VLS_ctx.aiStore.cacheStats && __VLS_ctx.aiStore.cacheStats.totalPreTranslated > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.cache }) },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.aiStore.cacheStats && __VLS_ctx.aiStore.cacheStats.totalPreTranslated > 0))
                    return;
                __VLS_ctx.collapsed.cache = !__VLS_ctx.collapsed.cache;
                // @ts-ignore
                [aiStore, aiStore, aiStore, aiStore, aiStore, termAuditTotal, collapsed, collapsed, collapsed,];
            } },
        ...{ class: "section-header collapsible" },
    });
    /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
        ...{ class: "section-title" },
    });
    /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "section-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
    let __VLS_290;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_291 = __VLS_asFunctionalComponent1(__VLS_290, new __VLS_290({
        size: (16),
    }));
    const __VLS_292 = __VLS_291({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_291));
    const { default: __VLS_295 } = __VLS_293.slots;
    let __VLS_296;
    /** @ts-ignore @type {typeof __VLS_components.CachedOutlined} */
    CachedOutlined;
    // @ts-ignore
    const __VLS_297 = __VLS_asFunctionalComponent1(__VLS_296, new __VLS_296({}));
    const __VLS_298 = __VLS_297({}, ...__VLS_functionalComponentArgsRest(__VLS_297));
    // @ts-ignore
    [];
    var __VLS_293;
    let __VLS_301;
    /** @ts-ignore @type {typeof __VLS_components.NTag | typeof __VLS_components.NTag} */
    NTag;
    // @ts-ignore
    const __VLS_302 = __VLS_asFunctionalComponent1(__VLS_301, new __VLS_301({
        size: "small",
        type: "warning",
        ...{ style: {} },
    }));
    const __VLS_303 = __VLS_302({
        size: "small",
        type: "warning",
        ...{ style: {} },
    }, ...__VLS_functionalComponentArgsRest(__VLS_302));
    const { default: __VLS_306 } = __VLS_304.slots;
    // @ts-ignore
    [];
    var __VLS_304;
    let __VLS_307;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_308 = __VLS_asFunctionalComponent1(__VLS_307, new __VLS_307({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.cache }) },
    }));
    const __VLS_309 = __VLS_308({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.cache }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_308));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_312 } = __VLS_310.slots;
    let __VLS_313;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_314 = __VLS_asFunctionalComponent1(__VLS_313, new __VLS_313({}));
    const __VLS_315 = __VLS_314({}, ...__VLS_functionalComponentArgsRest(__VLS_314));
    // @ts-ignore
    [collapsed,];
    var __VLS_310;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.cache }) },
    });
    /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metrics-strip compact-metrics" },
    });
    /** @type {__VLS_StyleScopedClasses['metrics-strip']} */ ;
    /** @type {__VLS_StyleScopedClasses['compact-metrics']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-pill" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
    let __VLS_318;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_319 = __VLS_asFunctionalComponent1(__VLS_318, new __VLS_318({
        size: (14),
    }));
    const __VLS_320 = __VLS_319({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_319));
    const { default: __VLS_323 } = __VLS_321.slots;
    let __VLS_324;
    /** @ts-ignore @type {typeof __VLS_components.CachedOutlined} */
    CachedOutlined;
    // @ts-ignore
    const __VLS_325 = __VLS_asFunctionalComponent1(__VLS_324, new __VLS_324({}));
    const __VLS_326 = __VLS_325({}, ...__VLS_functionalComponentArgsRest(__VLS_325));
    // @ts-ignore
    [collapsed,];
    var __VLS_321;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-data" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-value" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
    (__VLS_ctx.aiStore.cacheStats.totalPreTranslated);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-label" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-pill rate-good" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['rate-good']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
    let __VLS_329;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_330 = __VLS_asFunctionalComponent1(__VLS_329, new __VLS_329({
        size: (14),
    }));
    const __VLS_331 = __VLS_330({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_330));
    const { default: __VLS_334 } = __VLS_332.slots;
    let __VLS_335;
    /** @ts-ignore @type {typeof __VLS_components.CheckCircleOutlined} */
    CheckCircleOutlined;
    // @ts-ignore
    const __VLS_336 = __VLS_asFunctionalComponent1(__VLS_335, new __VLS_335({}));
    const __VLS_337 = __VLS_336({}, ...__VLS_functionalComponentArgsRest(__VLS_336));
    // @ts-ignore
    [aiStore,];
    var __VLS_332;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-data" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-value" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
    (__VLS_ctx.aiStore.cacheStats.cacheHits);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-label" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-pill" },
        ...{ class: ({ 'rate-bad': __VLS_ctx.aiStore.cacheStats.cacheMisses > 0 }) },
    });
    /** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['rate-bad']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
    let __VLS_340;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_341 = __VLS_asFunctionalComponent1(__VLS_340, new __VLS_340({
        size: (14),
    }));
    const __VLS_342 = __VLS_341({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_341));
    const { default: __VLS_345 } = __VLS_343.slots;
    let __VLS_346;
    /** @ts-ignore @type {typeof __VLS_components.ErrorOutlineOutlined} */
    ErrorOutlineOutlined;
    // @ts-ignore
    const __VLS_347 = __VLS_asFunctionalComponent1(__VLS_346, new __VLS_346({}));
    const __VLS_348 = __VLS_347({}, ...__VLS_functionalComponentArgsRest(__VLS_347));
    // @ts-ignore
    [aiStore, aiStore,];
    var __VLS_343;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-data" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-value" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
    (__VLS_ctx.aiStore.cacheStats.cacheMisses);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-label" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-pill" },
        ...{ class: ({ 'rate-good': __VLS_ctx.aiStore.cacheStats.hitRate > 80, 'rate-warn': __VLS_ctx.aiStore.cacheStats.hitRate <= 80 && __VLS_ctx.aiStore.cacheStats.hitRate > 50, 'rate-bad': __VLS_ctx.aiStore.cacheStats.hitRate <= 50 }) },
    });
    /** @type {__VLS_StyleScopedClasses['metric-pill']} */ ;
    /** @type {__VLS_StyleScopedClasses['rate-good']} */ ;
    /** @type {__VLS_StyleScopedClasses['rate-warn']} */ ;
    /** @type {__VLS_StyleScopedClasses['rate-bad']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-icon']} */ ;
    let __VLS_351;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_352 = __VLS_asFunctionalComponent1(__VLS_351, new __VLS_351({
        size: (14),
    }));
    const __VLS_353 = __VLS_352({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_352));
    const { default: __VLS_356 } = __VLS_354.slots;
    let __VLS_357;
    /** @ts-ignore @type {typeof __VLS_components.SpeedOutlined} */
    SpeedOutlined;
    // @ts-ignore
    const __VLS_358 = __VLS_asFunctionalComponent1(__VLS_357, new __VLS_357({}));
    const __VLS_359 = __VLS_358({}, ...__VLS_functionalComponentArgsRest(__VLS_358));
    // @ts-ignore
    [aiStore, aiStore, aiStore, aiStore, aiStore,];
    var __VLS_354;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "metric-data" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-data']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-value" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-value']} */ ;
    (__VLS_ctx.aiStore.cacheStats.hitRate);
    __VLS_asFunctionalElement1(__VLS_intrinsics.small, __VLS_intrinsics.small)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "metric-label" },
    });
    /** @type {__VLS_StyleScopedClasses['metric-label']} */ ;
    let __VLS_362;
    /** @ts-ignore @type {typeof __VLS_components.NProgress} */
    NProgress;
    // @ts-ignore
    const __VLS_363 = __VLS_asFunctionalComponent1(__VLS_362, new __VLS_362({
        type: "line",
        percentage: (__VLS_ctx.aiStore.cacheStats.hitRate),
        color: (__VLS_ctx.aiStore.cacheStats.hitRate > 50 ? 'var(--success)' : 'var(--danger)'),
        ...{ class: "cache-progress" },
    }));
    const __VLS_364 = __VLS_363({
        type: "line",
        percentage: (__VLS_ctx.aiStore.cacheStats.hitRate),
        color: (__VLS_ctx.aiStore.cacheStats.hitRate > 50 ? 'var(--success)' : 'var(--danger)'),
        ...{ class: "cache-progress" },
    }, ...__VLS_functionalComponentArgsRest(__VLS_363));
    /** @type {__VLS_StyleScopedClasses['cache-progress']} */ ;
    if (__VLS_ctx.aiStore.cacheStats.recentMisses.length > 0) {
        let __VLS_367;
        /** @ts-ignore @type {typeof __VLS_components.NCollapse | typeof __VLS_components.NCollapse} */
        NCollapse;
        // @ts-ignore
        const __VLS_368 = __VLS_asFunctionalComponent1(__VLS_367, new __VLS_367({}));
        const __VLS_369 = __VLS_368({}, ...__VLS_functionalComponentArgsRest(__VLS_368));
        const { default: __VLS_372 } = __VLS_370.slots;
        let __VLS_373;
        /** @ts-ignore @type {typeof __VLS_components.NCollapseItem | typeof __VLS_components.NCollapseItem} */
        NCollapseItem;
        // @ts-ignore
        const __VLS_374 = __VLS_asFunctionalComponent1(__VLS_373, new __VLS_373({
            title: "最近未命中详情",
            name: "misses",
        }));
        const __VLS_375 = __VLS_374({
            title: "最近未命中详情",
            name: "misses",
        }, ...__VLS_functionalComponentArgsRest(__VLS_374));
        const { default: __VLS_378 } = __VLS_376.slots;
        for (const [miss, idx] of __VLS_vFor((__VLS_ctx.aiStore.cacheStats.recentMisses))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (idx),
                ...{ class: "cache-miss-item" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-item']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cache-miss-row" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cache-miss-label" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.code, __VLS_intrinsics.code)({
                ...{ class: "cache-miss-code" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-code']} */ ;
            (miss.preTranslatedKey);
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "cache-miss-row" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-row']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "cache-miss-label" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-label']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.code, __VLS_intrinsics.code)({
                ...{ class: "cache-miss-code" },
            });
            /** @type {__VLS_StyleScopedClasses['cache-miss-code']} */ ;
            (miss.runtimeText);
            // @ts-ignore
            [aiStore, aiStore, aiStore, aiStore, aiStore,];
        }
        // @ts-ignore
        [];
        var __VLS_376;
        // @ts-ignore
        [];
        var __VLS_370;
    }
}
if (__VLS_ctx.settings) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.settings }) },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.settings))
                    return;
                __VLS_ctx.collapsed.settings = !__VLS_ctx.collapsed.settings;
                // @ts-ignore
                [collapsed, collapsed, collapsed, settings,];
            } },
        ...{ class: "section-header collapsible" },
    });
    /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
        ...{ class: "section-title" },
    });
    /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "section-icon" },
    });
    /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
    let __VLS_379;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_380 = __VLS_asFunctionalComponent1(__VLS_379, new __VLS_379({
        size: (16),
    }));
    const __VLS_381 = __VLS_380({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_380));
    const { default: __VLS_384 } = __VLS_382.slots;
    let __VLS_385;
    /** @ts-ignore @type {typeof __VLS_components.SmartToyOutlined} */
    SmartToyOutlined;
    // @ts-ignore
    const __VLS_386 = __VLS_asFunctionalComponent1(__VLS_385, new __VLS_385({}));
    const __VLS_387 = __VLS_386({}, ...__VLS_functionalComponentArgsRest(__VLS_386));
    // @ts-ignore
    [];
    var __VLS_382;
    let __VLS_390;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_391 = __VLS_asFunctionalComponent1(__VLS_390, new __VLS_390({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.settings }) },
    }));
    const __VLS_392 = __VLS_391({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.settings }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_391));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_395 } = __VLS_393.slots;
    let __VLS_396;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_397 = __VLS_asFunctionalComponent1(__VLS_396, new __VLS_396({}));
    const __VLS_398 = __VLS_397({}, ...__VLS_functionalComponentArgsRest(__VLS_397));
    // @ts-ignore
    [collapsed,];
    var __VLS_393;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.settings }) },
    });
    /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "pipeline-settings" },
        ...{ class: ({ 'all-collapsed': __VLS_ctx.allSettingsCollapsed }) },
    });
    /** @type {__VLS_StyleScopedClasses['pipeline-settings']} */ ;
    /** @type {__VLS_StyleScopedClasses['all-collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.settings))
                    return;
                __VLS_ctx.collapsed.termSettings = !__VLS_ctx.collapsed.termSettings;
                // @ts-ignore
                [collapsed, collapsed, collapsed, allSettingsCollapsed,];
            } },
        ...{ class: "settings-group-header" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-title" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-title']} */ ;
    let __VLS_401;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_402 = __VLS_asFunctionalComponent1(__VLS_401, new __VLS_401({
        size: (14),
    }));
    const __VLS_403 = __VLS_402({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_402));
    const { default: __VLS_406 } = __VLS_404.slots;
    let __VLS_407;
    /** @ts-ignore @type {typeof __VLS_components.AutoFixHighOutlined} */
    AutoFixHighOutlined;
    // @ts-ignore
    const __VLS_408 = __VLS_asFunctionalComponent1(__VLS_407, new __VLS_407({}));
    const __VLS_409 = __VLS_408({}, ...__VLS_functionalComponentArgsRest(__VLS_408));
    // @ts-ignore
    [];
    var __VLS_404;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    let __VLS_412;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_413 = __VLS_asFunctionalComponent1(__VLS_412, new __VLS_412({
        size: (16),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.termSettings }) },
    }));
    const __VLS_414 = __VLS_413({
        size: (16),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.termSettings }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_413));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_417 } = __VLS_415.slots;
    let __VLS_418;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_419 = __VLS_asFunctionalComponent1(__VLS_418, new __VLS_418({}));
    const __VLS_420 = __VLS_419({}, ...__VLS_functionalComponentArgsRest(__VLS_419));
    // @ts-ignore
    [collapsed,];
    var __VLS_415;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.termSettings }) },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-body-inner']} */ ;
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
    let __VLS_423;
    /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
    NSwitch;
    // @ts-ignore
    const __VLS_424 = __VLS_asFunctionalComponent1(__VLS_423, new __VLS_423({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.termAuditEnabled),
    }));
    const __VLS_425 = __VLS_424({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.termAuditEnabled),
    }, ...__VLS_functionalComponentArgsRest(__VLS_424));
    let __VLS_428;
    const __VLS_429 = ({ 'update:value': {} },
        { 'onUpdate:value': ((v) => { __VLS_ctx.aiSettings = { ...__VLS_ctx.aiSettings, termAuditEnabled: v }; }) });
    var __VLS_426;
    var __VLS_427;
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
    let __VLS_430;
    /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
    NSwitch;
    // @ts-ignore
    const __VLS_431 = __VLS_asFunctionalComponent1(__VLS_430, new __VLS_430({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.naturalTranslationMode),
    }));
    const __VLS_432 = __VLS_431({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.naturalTranslationMode),
    }, ...__VLS_functionalComponentArgsRest(__VLS_431));
    let __VLS_435;
    const __VLS_436 = ({ 'update:value': {} },
        { 'onUpdate:value': ((v) => { __VLS_ctx.aiSettings = { ...__VLS_ctx.aiSettings, naturalTranslationMode: v }; }) });
    var __VLS_433;
    var __VLS_434;
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
    let __VLS_437;
    /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
    NSwitch;
    // @ts-ignore
    const __VLS_438 = __VLS_asFunctionalComponent1(__VLS_437, new __VLS_437({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.glossaryExtractionEnabled),
    }));
    const __VLS_439 = __VLS_438({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.glossaryExtractionEnabled),
    }, ...__VLS_functionalComponentArgsRest(__VLS_438));
    let __VLS_442;
    const __VLS_443 = ({ 'update:value': {} },
        { 'onUpdate:value': ((v) => { __VLS_ctx.aiSettings = { ...__VLS_ctx.aiSettings, glossaryExtractionEnabled: v }; }) });
    var __VLS_440;
    var __VLS_441;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.settings))
                    return;
                __VLS_ctx.collapsed.tmSettings = !__VLS_ctx.collapsed.tmSettings;
                // @ts-ignore
                [collapsed, collapsed, collapsed, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings,];
            } },
        ...{ class: "settings-group-header" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-title" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-title']} */ ;
    let __VLS_444;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_445 = __VLS_asFunctionalComponent1(__VLS_444, new __VLS_444({
        size: (14),
    }));
    const __VLS_446 = __VLS_445({
        size: (14),
    }, ...__VLS_functionalComponentArgsRest(__VLS_445));
    const { default: __VLS_449 } = __VLS_447.slots;
    let __VLS_450;
    /** @ts-ignore @type {typeof __VLS_components.StorageOutlined} */
    StorageOutlined;
    // @ts-ignore
    const __VLS_451 = __VLS_asFunctionalComponent1(__VLS_450, new __VLS_450({}));
    const __VLS_452 = __VLS_451({}, ...__VLS_functionalComponentArgsRest(__VLS_451));
    // @ts-ignore
    [];
    var __VLS_447;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    let __VLS_455;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_456 = __VLS_asFunctionalComponent1(__VLS_455, new __VLS_455({
        size: (16),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.tmSettings }) },
    }));
    const __VLS_457 = __VLS_456({
        size: (16),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.tmSettings }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_456));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_460 } = __VLS_458.slots;
    let __VLS_461;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_462 = __VLS_asFunctionalComponent1(__VLS_461, new __VLS_461({}));
    const __VLS_463 = __VLS_462({}, ...__VLS_functionalComponentArgsRest(__VLS_462));
    // @ts-ignore
    [collapsed,];
    var __VLS_458;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.tmSettings }) },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "settings-group-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['settings-group-body-inner']} */ ;
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
    let __VLS_466;
    /** @ts-ignore @type {typeof __VLS_components.NSwitch} */
    NSwitch;
    // @ts-ignore
    const __VLS_467 = __VLS_asFunctionalComponent1(__VLS_466, new __VLS_466({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.enableTranslationMemory),
    }));
    const __VLS_468 = __VLS_467({
        ...{ 'onUpdate:value': {} },
        value: (__VLS_ctx.aiSettings.enableTranslationMemory),
    }, ...__VLS_functionalComponentArgsRest(__VLS_467));
    let __VLS_471;
    const __VLS_472 = ({ 'update:value': {} },
        { 'onUpdate:value': ((v) => { __VLS_ctx.aiSettings = { ...__VLS_ctx.aiSettings, enableTranslationMemory: v }; }) });
    var __VLS_469;
    var __VLS_470;
    if (__VLS_ctx.aiSettings.enableTranslationMemory) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "sub-setting" },
        });
        /** @type {__VLS_StyleScopedClasses['sub-setting']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "setting-label" },
        });
        /** @type {__VLS_StyleScopedClasses['setting-label']} */ ;
        let __VLS_473;
        /** @ts-ignore @type {typeof __VLS_components.NSlider} */
        NSlider;
        // @ts-ignore
        const __VLS_474 = __VLS_asFunctionalComponent1(__VLS_473, new __VLS_473({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.aiSettings.fuzzyMatchThreshold),
            min: (0),
            max: (100),
            step: (1),
            tooltip: (true),
            formatTooltip: ((v) => v + '%'),
            ...{ class: "threshold-slider" },
        }));
        const __VLS_475 = __VLS_474({
            ...{ 'onUpdate:value': {} },
            value: (__VLS_ctx.aiSettings.fuzzyMatchThreshold),
            min: (0),
            max: (100),
            step: (1),
            tooltip: (true),
            formatTooltip: ((v) => v + '%'),
            ...{ class: "threshold-slider" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_474));
        let __VLS_478;
        const __VLS_479 = ({ 'update:value': {} },
            { 'onUpdate:value': ((v) => { __VLS_ctx.aiSettings = { ...__VLS_ctx.aiSettings, fuzzyMatchThreshold: v }; }) });
        /** @type {__VLS_StyleScopedClasses['threshold-slider']} */ ;
        var __VLS_476;
        var __VLS_477;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "sub-setting-hint" },
        });
        /** @type {__VLS_StyleScopedClasses['sub-setting-hint']} */ ;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "mode-tabs" },
    });
    /** @type {__VLS_StyleScopedClasses['mode-tabs']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.settings))
                    return;
                __VLS_ctx.setMode('cloud');
                // @ts-ignore
                [collapsed, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, aiSettings, setMode,];
            } },
        ...{ class: "mode-tab" },
        ...{ class: ({ active: __VLS_ctx.activeMode === 'cloud' }) },
    });
    /** @type {__VLS_StyleScopedClasses['mode-tab']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    let __VLS_480;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_481 = __VLS_asFunctionalComponent1(__VLS_480, new __VLS_480({
        size: (16),
    }));
    const __VLS_482 = __VLS_481({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_481));
    const { default: __VLS_485 } = __VLS_483.slots;
    let __VLS_486;
    /** @ts-ignore @type {typeof __VLS_components.CloudOutlined} */
    CloudOutlined;
    // @ts-ignore
    const __VLS_487 = __VLS_asFunctionalComponent1(__VLS_486, new __VLS_486({}));
    const __VLS_488 = __VLS_487({}, ...__VLS_functionalComponentArgsRest(__VLS_487));
    // @ts-ignore
    [activeMode,];
    var __VLS_483;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.settings))
                    return;
                __VLS_ctx.setMode('local');
                // @ts-ignore
                [setMode,];
            } },
        ...{ class: "mode-tab" },
        ...{ class: ({ active: __VLS_ctx.activeMode === 'local' }) },
    });
    /** @type {__VLS_StyleScopedClasses['mode-tab']} */ ;
    /** @type {__VLS_StyleScopedClasses['active']} */ ;
    let __VLS_491;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_492 = __VLS_asFunctionalComponent1(__VLS_491, new __VLS_491({
        size: (16),
    }));
    const __VLS_493 = __VLS_492({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_492));
    const { default: __VLS_496 } = __VLS_494.slots;
    let __VLS_497;
    /** @ts-ignore @type {typeof __VLS_components.ComputerOutlined} */
    ComputerOutlined;
    // @ts-ignore
    const __VLS_498 = __VLS_asFunctionalComponent1(__VLS_497, new __VLS_497({}));
    const __VLS_499 = __VLS_498({}, ...__VLS_functionalComponentArgsRest(__VLS_498));
    // @ts-ignore
    [activeMode,];
    var __VLS_494;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    let __VLS_502;
    /** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
    Transition;
    // @ts-ignore
    const __VLS_503 = __VLS_asFunctionalComponent1(__VLS_502, new __VLS_502({
        name: "mode-switch",
        mode: "out-in",
    }));
    const __VLS_504 = __VLS_503({
        name: "mode-switch",
        mode: "out-in",
    }, ...__VLS_functionalComponentArgsRest(__VLS_503));
    const { default: __VLS_507 } = __VLS_505.slots;
    if (__VLS_ctx.activeMode === 'cloud') {
        const __VLS_508 = AiTranslationCard;
        // @ts-ignore
        const __VLS_509 = __VLS_asFunctionalComponent1(__VLS_508, new __VLS_508({
            key: "cloud",
            modelValue: (__VLS_ctx.aiSettings),
            embedded: (true),
        }));
        const __VLS_510 = __VLS_509({
            key: "cloud",
            modelValue: (__VLS_ctx.aiSettings),
            embedded: (true),
        }, ...__VLS_functionalComponentArgsRest(__VLS_509));
    }
    else {
        const __VLS_513 = LocalAiPanel;
        // @ts-ignore
        const __VLS_514 = __VLS_asFunctionalComponent1(__VLS_513, new __VLS_513({
            key: "local",
            modelValue: (__VLS_ctx.aiSettings),
        }));
        const __VLS_515 = __VLS_514({
            key: "local",
            modelValue: (__VLS_ctx.aiSettings),
        }, ...__VLS_functionalComponentArgsRest(__VLS_514));
    }
    // @ts-ignore
    [aiSettings, aiSettings, activeMode,];
    var __VLS_505;
}
if ((__VLS_ctx.aiStore.stats?.recentTranslations?.length ?? 0) > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.recent }) },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!((__VLS_ctx.aiStore.stats?.recentTranslations?.length ?? 0) > 0))
                    return;
                __VLS_ctx.collapsed.recent = !__VLS_ctx.collapsed.recent;
                // @ts-ignore
                [aiStore, collapsed, collapsed, collapsed,];
            } },
        ...{ class: "section-header collapsible" },
    });
    /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
        ...{ class: "section-title" },
    });
    /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "section-icon history" },
    });
    /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['history']} */ ;
    let __VLS_518;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_519 = __VLS_asFunctionalComponent1(__VLS_518, new __VLS_518({
        size: (16),
    }));
    const __VLS_520 = __VLS_519({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_519));
    const { default: __VLS_523 } = __VLS_521.slots;
    let __VLS_524;
    /** @ts-ignore @type {typeof __VLS_components.HistoryOutlined} */
    HistoryOutlined;
    // @ts-ignore
    const __VLS_525 = __VLS_asFunctionalComponent1(__VLS_524, new __VLS_524({}));
    const __VLS_526 = __VLS_525({}, ...__VLS_functionalComponentArgsRest(__VLS_525));
    // @ts-ignore
    [];
    var __VLS_521;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "recent-count-badge" },
    });
    /** @type {__VLS_StyleScopedClasses['recent-count-badge']} */ ;
    (__VLS_ctx.aiStore.stats.recentTranslations.length);
    let __VLS_529;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_530 = __VLS_asFunctionalComponent1(__VLS_529, new __VLS_529({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.recent }) },
    }));
    const __VLS_531 = __VLS_530({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.recent }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_530));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_534 } = __VLS_532.slots;
    let __VLS_535;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_536 = __VLS_asFunctionalComponent1(__VLS_535, new __VLS_535({}));
    const __VLS_537 = __VLS_536({}, ...__VLS_functionalComponentArgsRest(__VLS_536));
    // @ts-ignore
    [aiStore, collapsed,];
    var __VLS_532;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.recent }) },
    });
    /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "recent-list" },
    });
    /** @type {__VLS_StyleScopedClasses['recent-list']} */ ;
    for (const [item, index] of __VLS_vFor((__VLS_ctx.aiStore.stats.recentTranslations))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (index),
            ...{ class: "recent-item" },
            ...{ style: ({ animationDelay: index * 0.03 + 's' }) },
        });
        /** @type {__VLS_StyleScopedClasses['recent-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "recent-index" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-index']} */ ;
        (index + 1);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "recent-body" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-body']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "recent-texts" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-texts']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "recent-original" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-original']} */ ;
        (item.original);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "recent-arrow" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-arrow']} */ ;
        let __VLS_540;
        /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
        NIcon;
        // @ts-ignore
        const __VLS_541 = __VLS_asFunctionalComponent1(__VLS_540, new __VLS_540({
            size: (12),
        }));
        const __VLS_542 = __VLS_541({
            size: (12),
        }, ...__VLS_functionalComponentArgsRest(__VLS_541));
        const { default: __VLS_545 } = __VLS_543.slots;
        let __VLS_546;
        /** @ts-ignore @type {typeof __VLS_components.ArrowRightAltOutlined} */
        ArrowRightAltOutlined;
        // @ts-ignore
        const __VLS_547 = __VLS_asFunctionalComponent1(__VLS_546, new __VLS_546({}));
        const __VLS_548 = __VLS_547({}, ...__VLS_functionalComponentArgsRest(__VLS_547));
        // @ts-ignore
        [aiStore, collapsed,];
        var __VLS_543;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "recent-translated" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-translated']} */ ;
        (item.translated);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "recent-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['recent-meta']} */ ;
        if (item.gameId) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag game" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['game']} */ ;
            (__VLS_ctx.getGameName(item.gameId));
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "meta-tag endpoint" },
        });
        /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
        /** @type {__VLS_StyleScopedClasses['endpoint']} */ ;
        (item.endpointName);
        if (item.hasTerms) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag term-tag" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['term-tag']} */ ;
        }
        if (item.hasDnt) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag dnt-tag" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['dnt-tag']} */ ;
        }
        if (item.termAuditResult === 'phase1Pass') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag audit-pass" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['audit-pass']} */ ;
        }
        else if (item.termAuditResult === 'phase2Pass') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag audit-pass" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['audit-pass']} */ ;
        }
        else if (item.termAuditResult === 'forceCorrected') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag audit-warn" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['audit-warn']} */ ;
        }
        else if (item.termAuditResult === 'failed') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag audit-fail" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['audit-fail']} */ ;
        }
        if (item.translationSource === 'tmExact') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag tm-exact" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['tm-exact']} */ ;
        }
        else if (item.translationSource === 'tmFuzzy') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag tm-fuzzy" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['tm-fuzzy']} */ ;
        }
        else if (item.translationSource === 'tmPattern') {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "meta-tag tm-pattern" },
            });
            /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
            /** @type {__VLS_StyleScopedClasses['tm-pattern']} */ ;
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "meta-tag" },
        });
        /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
        (item.tokensUsed);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "meta-tag" },
        });
        /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
        (__VLS_ctx.formatTime(item.responseTimeMs));
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "meta-tag time" },
        });
        /** @type {__VLS_StyleScopedClasses['meta-tag']} */ ;
        /** @type {__VLS_StyleScopedClasses['time']} */ ;
        (__VLS_ctx.formatRelativeTime(item.timestamp));
        // @ts-ignore
        [formatTime, getGameName, formatRelativeTime,];
    }
}
if ((__VLS_ctx.aiStore.stats?.recentErrors?.length ?? 0) > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-card" },
        ...{ class: ({ 'is-collapsed': __VLS_ctx.collapsed.errors }) },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['section-card']} */ ;
    /** @type {__VLS_StyleScopedClasses['is-collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                if (!((__VLS_ctx.aiStore.stats?.recentErrors?.length ?? 0) > 0))
                    return;
                __VLS_ctx.collapsed.errors = !__VLS_ctx.collapsed.errors;
                // @ts-ignore
                [aiStore, collapsed, collapsed, collapsed,];
            } },
        ...{ class: "section-header collapsible" },
    });
    /** @type {__VLS_StyleScopedClasses['section-header']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsible']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
        ...{ class: "section-title" },
    });
    /** @type {__VLS_StyleScopedClasses['section-title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "section-icon error-log" },
    });
    /** @type {__VLS_StyleScopedClasses['section-icon']} */ ;
    /** @type {__VLS_StyleScopedClasses['error-log']} */ ;
    let __VLS_551;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_552 = __VLS_asFunctionalComponent1(__VLS_551, new __VLS_551({
        size: (16),
    }));
    const __VLS_553 = __VLS_552({
        size: (16),
    }, ...__VLS_functionalComponentArgsRest(__VLS_552));
    const { default: __VLS_556 } = __VLS_554.slots;
    let __VLS_557;
    /** @ts-ignore @type {typeof __VLS_components.ErrorOutlineOutlined} */
    ErrorOutlineOutlined;
    // @ts-ignore
    const __VLS_558 = __VLS_asFunctionalComponent1(__VLS_557, new __VLS_557({}));
    const __VLS_559 = __VLS_558({}, ...__VLS_functionalComponentArgsRest(__VLS_558));
    // @ts-ignore
    [];
    var __VLS_554;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "error-count-badge" },
    });
    /** @type {__VLS_StyleScopedClasses['error-count-badge']} */ ;
    (__VLS_ctx.aiStore.stats.recentErrors.length);
    let __VLS_562;
    /** @ts-ignore @type {typeof __VLS_components.NIcon | typeof __VLS_components.NIcon} */
    NIcon;
    // @ts-ignore
    const __VLS_563 = __VLS_asFunctionalComponent1(__VLS_562, new __VLS_562({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.errors }) },
    }));
    const __VLS_564 = __VLS_563({
        size: (18),
        ...{ class: "collapse-chevron" },
        ...{ class: ({ expanded: !__VLS_ctx.collapsed.errors }) },
    }, ...__VLS_functionalComponentArgsRest(__VLS_563));
    /** @type {__VLS_StyleScopedClasses['collapse-chevron']} */ ;
    /** @type {__VLS_StyleScopedClasses['expanded']} */ ;
    const { default: __VLS_567 } = __VLS_565.slots;
    let __VLS_568;
    /** @ts-ignore @type {typeof __VLS_components.ExpandMoreOutlined} */
    ExpandMoreOutlined;
    // @ts-ignore
    const __VLS_569 = __VLS_asFunctionalComponent1(__VLS_568, new __VLS_568({}));
    const __VLS_570 = __VLS_569({}, ...__VLS_functionalComponentArgsRest(__VLS_569));
    // @ts-ignore
    [aiStore, collapsed,];
    var __VLS_565;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body" },
        ...{ class: ({ collapsed: __VLS_ctx.collapsed.errors }) },
    });
    /** @type {__VLS_StyleScopedClasses['section-body']} */ ;
    /** @type {__VLS_StyleScopedClasses['collapsed']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "section-body-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['section-body-inner']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "error-list" },
    });
    /** @type {__VLS_StyleScopedClasses['error-list']} */ ;
    for (const [err] of __VLS_vFor(([...__VLS_ctx.aiStore.stats.recentErrors].reverse()))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (err.timestamp + err.message),
            ...{ class: "error-item" },
        });
        /** @type {__VLS_StyleScopedClasses['error-item']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "error-message" },
        });
        /** @type {__VLS_StyleScopedClasses['error-message']} */ ;
        (err.message);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "error-meta" },
        });
        /** @type {__VLS_StyleScopedClasses['error-meta']} */ ;
        if (err.gameId) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "error-game-tag" },
            });
            /** @type {__VLS_StyleScopedClasses['error-game-tag']} */ ;
            (__VLS_ctx.getGameName(err.gameId));
        }
        if (err.endpointName) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
            (err.endpointName);
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
        (__VLS_ctx.formatRelativeTime(err.timestamp));
        // @ts-ignore
        [aiStore, getGameName, collapsed, formatRelativeTime,];
    }
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
