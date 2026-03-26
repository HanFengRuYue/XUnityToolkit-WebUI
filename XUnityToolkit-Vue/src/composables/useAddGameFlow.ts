import type { MessageApiInjection } from 'naive-ui/es/message/src/MessageProvider'
import { gamesApi } from '@/api/games'
import type { Game } from '@/api/types'
import { useGamesStore } from '@/stores/games'
import { useFileExplorer } from '@/composables/useFileExplorer'

export function useAddGameFlow(message: MessageApiInjection) {
  const gamesStore = useGamesStore()
  const { selectFile, selectFolder } = useFileExplorer()

  async function addGame(): Promise<Game | null> {
    // Step 1: select folder
    const folderPath = await selectFolder({ title: '选择游戏文件夹' })
    if (!folderPath) return null

    // Step 2: add with detection
    let result
    try {
      result = await gamesApi.addWithDetection(folderPath)
    } catch (e) {
      message.error(e instanceof Error ? e.message : '添加游戏失败')
      return null
    }

    // Branch A: no exe found — ask user to pick one
    if (result.needsExeSelection) {
      const exePath = await selectFile({
        title: '选择游戏可执行文件',
        filters: [{ label: '可执行文件', extensions: ['.exe'] }],
        initialPath: folderPath,
      })
      if (!exePath) return null

      // Re-call addWithDetection with the explicit exe path
      try {
        result = await gamesApi.addWithDetection(folderPath, exePath)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '添加游戏失败')
        return null
      }
    }

    const game = result.game
    if (!game) return null

    gamesStore.addGame(game)

    // Build success message
    const parts: string[] = [`已添加: ${game.name}`]
    if (!game.isUnityGame) {
      parts.push('（非 Unity 游戏）')
    } else if (game.installState !== 'NotInstalled') {
      parts.push('（检测到插件已安装）')
    }

    // Warn about detected non-BepInEx frameworks
    const otherFrameworks = game.detectedFrameworks?.filter(
      (f) => f.framework !== 'BepInEx',
    )
    if (otherFrameworks && otherFrameworks.length > 0) {
      const names = otherFrameworks.map((f) => f.framework).join(', ')
      parts.push(`\n检测到其他模组框架: ${names}`)
    }

    message.success(parts.join(''))
    return game
  }

  return { addGame }
}
