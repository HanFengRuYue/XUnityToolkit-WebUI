import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { gamesApi, settingsApi } from '@/api/games';
import { useThemeStore } from '@/stores/theme';
export const useGamesStore = defineStore('games', () => {
    const games = ref([]);
    const loading = ref(false);
    const viewMode = ref('grid');
    const sortBy = ref('name');
    const cardSize = ref('medium');
    const gap = ref('normal');
    const showLabels = ref(true);
    const sortedGames = computed(() => {
        const sorted = [...games.value];
        switch (sortBy.value) {
            case 'recent':
                return sorted.sort((a, b) => {
                    const aTime = a.lastPlayedAt ? new Date(a.lastPlayedAt).getTime() : 0;
                    const bTime = b.lastPlayedAt ? new Date(b.lastPlayedAt).getTime() : 0;
                    return bTime - aTime;
                });
            case 'added':
                return sorted.sort((a, b) => new Date(b.addedAt).getTime() - new Date(a.addedAt).getTime());
            default:
                return sorted.sort((a, b) => a.name.localeCompare(b.name));
        }
    });
    async function fetchGames() {
        loading.value = true;
        try {
            games.value = await gamesApi.list();
        }
        finally {
            loading.value = false;
        }
    }
    async function renameGame(id, name) {
        const game = await gamesApi.update(id, { name });
        const index = games.value.findIndex((g) => g.id === id);
        if (index >= 0)
            games.value[index] = game;
        return game;
    }
    async function removeGame(id) {
        await gamesApi.remove(id);
        games.value = games.value.filter((g) => g.id !== id);
    }
    async function refreshGame(id) {
        try {
            const game = await gamesApi.get(id);
            const index = games.value.findIndex((g) => g.id === id);
            if (index >= 0)
                games.value[index] = game;
            return game;
        }
        catch {
            return null;
        }
    }
    async function launchGame(id) {
        await gamesApi.launch(id);
        const game = games.value.find((g) => g.id === id);
        if (game)
            game.lastPlayedAt = new Date().toISOString();
    }
    async function loadPreferences() {
        try {
            const settings = await settingsApi.get();
            if (settings.libraryViewMode === 'grid' || settings.libraryViewMode === 'list')
                viewMode.value = settings.libraryViewMode;
            if (settings.librarySortBy === 'name' || settings.librarySortBy === 'recent' || settings.librarySortBy === 'added')
                sortBy.value = settings.librarySortBy;
            if (settings.libraryCardSize === 'small' || settings.libraryCardSize === 'medium' || settings.libraryCardSize === 'large' || settings.libraryCardSize === 'xlarge')
                cardSize.value = settings.libraryCardSize;
            if (settings.libraryGap === 'compact' || settings.libraryGap === 'normal' || settings.libraryGap === 'spacious')
                gap.value = settings.libraryGap;
            if (typeof settings.libraryShowLabels === 'boolean')
                showLabels.value = settings.libraryShowLabels;
            // Sync accent color and page zoom from backend (overrides localStorage if different)
            const themeStore = useThemeStore();
            if (settings.accentColor) {
                themeStore.setAccentColor(settings.accentColor);
            }
            if (typeof settings.pageZoom === 'number') {
                themeStore.setPageZoom(settings.pageZoom);
            }
        }
        catch { /* ignore */ }
    }
    async function savePreferences() {
        try {
            const settings = await settingsApi.get();
            settings.libraryViewMode = viewMode.value;
            settings.librarySortBy = sortBy.value;
            settings.libraryCardSize = cardSize.value;
            settings.libraryGap = gap.value;
            settings.libraryShowLabels = showLabels.value;
            await settingsApi.save(settings);
        }
        catch { /* ignore */ }
    }
    function setViewMode(mode) {
        viewMode.value = mode;
    }
    function setSortBy(value) {
        sortBy.value = value;
    }
    function setCardSize(value) {
        cardSize.value = value;
    }
    function setGap(value) {
        gap.value = value;
    }
    function setShowLabels(value) {
        showLabels.value = value;
    }
    function addGame(game) {
        games.value.push(game);
    }
    function addGames(newGames) {
        games.value.push(...newGames);
    }
    return {
        games,
        loading,
        viewMode,
        sortBy,
        cardSize,
        gap,
        showLabels,
        sortedGames,
        fetchGames,
        renameGame,
        removeGame,
        refreshGame,
        launchGame,
        loadPreferences,
        savePreferences,
        setViewMode,
        setSortBy,
        setCardSize,
        setGap,
        setShowLabels,
        addGame,
        addGames,
    };
});
