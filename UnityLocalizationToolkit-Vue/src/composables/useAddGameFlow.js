import { ref } from 'vue';
import { gamesApi } from '@/api/games';
import { useGamesStore } from '@/stores/games';
import { useFileExplorer } from '@/composables/useFileExplorer';
export function useAddGameFlow(message) {
    const gamesStore = useGamesStore();
    const { selectFile, selectFolder } = useFileExplorer();
    const adding = ref(false);
    async function addGame() {
        // Step 1: select folder
        const folderPath = await selectFolder({ title: '选择游戏文件夹' });
        if (!folderPath)
            return null;
        adding.value = true;
        try {
            return await addGameCore(folderPath);
        }
        finally {
            adding.value = false;
        }
    }
    async function addGameCore(folderPath) {
        // Step 2: add with detection
        let result;
        try {
            result = await gamesApi.addWithDetection(folderPath);
        }
        catch (e) {
            message.error(e instanceof Error ? e.message : '添加游戏失败');
            return null;
        }
        // Branch A: no exe found — try batch add subdirectories first, then fallback to manual exe pick
        if (result.needsExeSelection) {
            try {
                const batchResult = await gamesApi.batchAdd(folderPath);
                if (batchResult.added.length > 0) {
                    gamesStore.addGames(batchResult.added);
                    const parts = [`批量添加了 ${batchResult.added.length} 个游戏`];
                    if (batchResult.skipped.length > 0) {
                        parts.push(`，跳过 ${batchResult.skipped.length} 个`);
                    }
                    message.success(parts.join(''));
                    return null;
                }
                // Subdirectories exist but all were skipped — show skip reasons, don't fall through to exe picker
                if (batchResult.skipped.length > 0) {
                    message.warning(`${batchResult.skipped.length} 个子文件夹均未能添加（${batchResult.skipped.map((s) => s.reason).filter((v, i, a) => a.indexOf(v) === i).join('、')}）`);
                    return null;
                }
            }
            catch {
                // Batch add failed or no subdirectories — fall through to manual exe selection
            }
            adding.value = false; // hide loading while user picks exe
            const exePath = await selectFile({
                title: '选择游戏可执行文件',
                filters: [{ label: '可执行文件', extensions: ['.exe'] }],
                initialPath: folderPath,
            });
            if (!exePath)
                return null;
            adding.value = true;
            // Re-call addWithDetection with the explicit exe path
            try {
                result = await gamesApi.addWithDetection(folderPath, exePath);
            }
            catch (e) {
                message.error(e instanceof Error ? e.message : '添加游戏失败');
                return null;
            }
        }
        const game = result.game;
        if (!game)
            return null;
        gamesStore.addGame(game);
        // Build success message
        const parts = [`已添加: ${game.name}`];
        if (!game.isUnityGame) {
            parts.push('（非 Unity 游戏）');
        }
        else if (game.installState !== 'NotInstalled') {
            parts.push('（检测到插件已安装）');
        }
        // Warn about detected non-BepInEx frameworks
        const otherFrameworks = game.detectedFrameworks?.filter((f) => f.framework !== 'BepInEx');
        if (otherFrameworks && otherFrameworks.length > 0) {
            const names = otherFrameworks.map((f) => f.framework).join(', ');
            parts.push(`\n检测到其他模组框架: ${names}`);
        }
        message.success(parts.join(''));
        return game;
    }
    return { addGame, adding };
}
