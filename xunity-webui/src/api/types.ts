export interface ApiResult<T> {
  success: boolean
  data?: T
  error?: string
}

export type InstallState = 'NotInstalled' | 'BepInExOnly' | 'FullyInstalled' | 'PartiallyInstalled'

export type UnityBackend = 'Mono' | 'IL2CPP'
export type Architecture = 'X86' | 'X64'

export interface UnityGameInfo {
  unityVersion: string
  backend: UnityBackend
  architecture: Architecture
  detectedExecutable: string
  detectedAt: string
}

export interface Game {
  id: string
  name: string
  gamePath: string
  executableName?: string
  addedAt: string
  updatedAt: string
  detectedInfo?: UnityGameInfo
  installState: InstallState
  installedBepInExVersion?: string
  installedXUnityVersion?: string
}

export type InstallStep =
  | 'Idle'
  | 'DetectingGame'
  | 'DownloadingBepInEx'
  | 'InstallingBepInEx'
  | 'DownloadingXUnity'
  | 'InstallingXUnity'
  | 'WritingConfig'
  | 'RemovingXUnity'
  | 'RemovingBepInEx'
  | 'Complete'
  | 'Failed'

export interface InstallationStatus {
  gameId: string
  step: InstallStep
  progressPercent: number
  message?: string
  error?: string
}

export interface XUnityConfig {
  sourceLanguage: string
  targetLanguage: string
  translationEngine: string
  fallbackEndpoint?: string
  enableUGUI: boolean
  enableNGUI: boolean
  enableTextMeshPro: boolean
  enableTextMesh: boolean
  enableIMGUI: boolean
  maxCharactersPerTranslation: number
  handleRichText: boolean
  enableUIResizing: boolean
  extra: Record<string, string>
}

export interface GitHubRelease {
  tagName: string
  name: string
  prerelease: boolean
  publishedAt: string
  assets: GitHubAsset[]
}

export interface GitHubAsset {
  name: string
  browserDownloadUrl: string
  size: number
}
