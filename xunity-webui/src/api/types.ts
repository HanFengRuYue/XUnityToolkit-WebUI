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

export type ModFrameworkType =
  | 'BepInEx'
  | 'MelonLoader'
  | 'IPA'
  | 'ReiPatcher'
  | 'Sybaris'
  | 'UnityInjector'
  | 'Standalone'

export interface DetectedModFramework {
  framework: ModFrameworkType
  version?: string
  hasXUnityPlugin: boolean
  xUnityVersion?: string
}

export interface Game {
  id: string
  name: string
  gamePath: string
  executableName?: string
  addedAt: string
  updatedAt: string
  isUnityGame: boolean
  detectedInfo?: UnityGameInfo
  installState: InstallState
  detectedFrameworks?: DetectedModFramework[]
  installedBepInExVersion?: string
  installedXUnityVersion?: string
}

export interface AddGameResponse {
  needsExeSelection: boolean
  game?: Game
}

export type InstallStep =
  | 'Idle'
  | 'DetectingGame'
  | 'DownloadingBepInEx'
  | 'InstallingBepInEx'
  | 'DownloadingXUnity'
  | 'InstallingXUnity'
  | 'GeneratingConfig'
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
  downloadSpeed?: string
  retryMessage?: string
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
  // Engine API credentials
  googleTranslateV2ApiKey?: string
  bingTranslateOcpApimSubscriptionKey?: string
  baiduTranslateAppId?: string
  baiduTranslateAppSecret?: string
  yandexTranslateApiKey?: string
  deepLTranslateApiKey?: string
  papagoTranslateClientId?: string
  papagoTranslateClientSecret?: string
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

export interface CacheInfo {
  fileCount: number
  totalBytes: number
}

export interface AppSettings {
  mirrorUrl: string
  theme: string
}

export interface VersionInfo {
  version: string
}
