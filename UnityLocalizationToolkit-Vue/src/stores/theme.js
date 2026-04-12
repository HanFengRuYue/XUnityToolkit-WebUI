import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
/** Captured once at module load, before any CSS zoom is applied.
 *  CSS zoom does NOT change devicePixelRatio in Chromium. */
const baseDpr = window.devicePixelRatio || 1;
export const accentPresets = [
    { hex: '#3b82f6', name: '钴蓝' },
    { hex: '#06b6d4', name: '天蓝' },
    { hex: '#14b8a6', name: '青绿' },
    { hex: '#10b981', name: '翠绿' },
    { hex: '#f59e0b', name: '琥珀' },
    { hex: '#f97316', name: '橙色' },
    { hex: '#f43f5e', name: '玫红' },
    { hex: '#8b5cf6', name: '紫色' },
];
function hexToRgb(hex) {
    const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
    if (!result)
        return { r: 59, g: 130, b: 246 };
    return { r: parseInt(result[1], 16), g: parseInt(result[2], 16), b: parseInt(result[3], 16) };
}
function clamp(v) {
    return Math.min(255, Math.max(0, Math.round(v)));
}
function rgbToHex(r, g, b) {
    return '#' + [r, g, b].map(v => clamp(v).toString(16).padStart(2, '0')).join('');
}
function lightenColor(hex, amount) {
    const { r, g, b } = hexToRgb(hex);
    return rgbToHex(r + (255 - r) * amount, g + (255 - g) * amount, b + (255 - b) * amount);
}
function darkenColor(hex, amount) {
    const { r, g, b } = hexToRgb(hex);
    return rgbToHex(r * (1 - amount), g * (1 - amount), b * (1 - amount));
}
/** Generate Naive UI primary color variants from a single accent hex */
export function getAccentVariants(hex, mode) {
    if (mode === 'dark') {
        return {
            primary: hex,
            hover: lightenColor(hex, 0.25),
            pressed: darkenColor(hex, 0.2),
        };
    }
    else {
        const base = darkenColor(hex, 0.15);
        return {
            primary: base,
            hover: darkenColor(hex, 0.25),
            pressed: darkenColor(hex, 0.35),
        };
    }
}
export const useThemeStore = defineStore('theme', () => {
    const mode = ref(loadInitialTheme());
    const accentColor = ref(loadInitialAccent());
    const pageZoom = ref(loadInitialZoom());
    /** The resolved display theme (always 'dark' or 'light', never 'system') */
    const resolvedTheme = ref(resolveTheme(mode.value));
    /** Effective zoom percentage (resolves 0=auto to DPR-based default) */
    const effectiveZoom = computed(() => pageZoom.value === 0 ? getDefaultZoom() : pageZoom.value);
    function loadInitialTheme() {
        const saved = localStorage.getItem('theme');
        if (saved === 'light' || saved === 'dark' || saved === 'system')
            return saved;
        // Default to system (follow OS theme)
        return 'system';
    }
    function loadInitialAccent() {
        return localStorage.getItem('accentColor') || '#3b82f6';
    }
    function loadInitialZoom() {
        const saved = localStorage.getItem('pageZoom');
        if (saved !== null) {
            const parsed = parseInt(saved, 10);
            if (parsed === 0 || (parsed >= 50 && parsed <= 200))
                return parsed;
        }
        return 0;
    }
    function getDefaultZoom() {
        return Math.round(baseDpr * 100 / 5) * 5;
    }
    function setPageZoom(value) {
        pageZoom.value = value;
        localStorage.setItem('pageZoom', String(value));
        applyZoom();
    }
    function applyZoom() {
        const zoom = pageZoom.value === 0 ? getDefaultZoom() : pageZoom.value;
        const cssZoom = zoom / (baseDpr * 100);
        document.documentElement.style.zoom = String(cssZoom);
    }
    function getSystemTheme() {
        if (window.matchMedia?.('(prefers-color-scheme: light)').matches)
            return 'light';
        return 'dark';
    }
    function resolveTheme(theme) {
        if (theme === 'system')
            return getSystemTheme();
        return theme;
    }
    function applyCurrentTheme() {
        const resolved = resolveTheme(mode.value);
        resolvedTheme.value = resolved;
        applyTheme(resolved);
        applyAccentColor(accentColor.value, resolved);
    }
    function setTheme(theme) {
        mode.value = theme;
        localStorage.setItem('theme', theme);
        applyCurrentTheme();
    }
    function toggle() {
        const current = resolvedTheme.value;
        setTheme(current === 'dark' ? 'light' : 'dark');
    }
    function setAccentColor(hex) {
        accentColor.value = hex;
        localStorage.setItem('accentColor', hex);
        applyAccentColor(hex, resolvedTheme.value);
    }
    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
    }
    function applyAccentColor(hex, theme) {
        const { r, g, b } = hexToRgb(hex);
        const el = document.documentElement.style;
        const effectiveHex = theme === 'light' ? darkenColor(hex, 0.15) : hex;
        const { r: er, g: eg, b: eb } = hexToRgb(effectiveHex);
        el.setProperty('--accent', effectiveHex);
        el.setProperty('--accent-soft', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.10' : '0.08'})`);
        el.setProperty('--accent-glow', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.25' : '0.15'})`);
        el.setProperty('--accent-border', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.20' : '0.18'})`);
        el.setProperty('--ambient-1', `rgba(${r}, ${g}, ${b}, ${theme === 'dark' ? '0.025' : '0.015'})`);
        el.setProperty('--selection-bg', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.25' : '0.20'})`);
        el.setProperty('--logo-glow', `drop-shadow(0 0 8px rgba(${er}, ${eg}, ${eb}, 0.3))`);
        el.setProperty('--shadow-glow', `0 0 20px rgba(${er}, ${eg}, ${eb}, 0.12)`);
        el.setProperty('--shadow-card-hover', theme === 'dark'
            ? `0 12px 40px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(${er}, ${eg}, ${eb}, 0.08)`
            : `0 8px 32px rgba(0, 0, 0, 0.08), 0 0 0 1px rgba(${er}, ${eg}, ${eb}, 0.12)`);
    }
    // Listen for OS theme changes (for 'system' mode)
    const mediaQuery = window.matchMedia?.('(prefers-color-scheme: dark)');
    if (mediaQuery) {
        mediaQuery.addEventListener('change', () => {
            if (mode.value === 'system') {
                applyCurrentTheme();
            }
        });
    }
    // Apply on init
    applyCurrentTheme();
    applyZoom();
    return { mode, accentColor, resolvedTheme, pageZoom, effectiveZoom, setTheme, toggle, setAccentColor, setPageZoom };
});
