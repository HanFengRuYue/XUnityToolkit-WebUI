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
  steamAppId?: number
  steamGridDbGameId?: number
  lastPlayedAt?: string
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
  | 'InstallingAiTranslation'
  | 'GeneratingConfig'
  | 'ApplyingConfig'
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
  // [Service]
  translationEngine: string
  fallbackEndpoint?: string
  // [General]
  sourceLanguage: string
  targetLanguage: string
  // [Files]
  outputFile?: string
  // [TextFrameworks]
  enableIMGUI: boolean
  enableUGUI: boolean
  enableNGUI: boolean
  enableTextMeshPro: boolean
  enableTextMesh: boolean
  enableFairyGUI: boolean
  // [Behaviour]
  maxCharactersPerTranslation: number
  forceSplitTextAfterCharacters: number
  handleRichText: boolean
  enableUIResizing: boolean
  overrideFont?: string
  overrideFontSize?: string
  overrideFontTextMeshPro?: string
  fallbackFontTextMeshPro?: string
  resizeUILineSpacingScale?: string
  forceUIResizing: boolean
  textGetterCompatibilityMode: boolean
  maxTextParserRecursion: number
  enableTranslationHelper: boolean
  templateAllNumberAway: boolean
  disableTextMeshProScrollInEffects: boolean
  cacheParsedTranslations: boolean
  // [Texture]
  textureDirectory?: string
  enableTextureTranslation: boolean
  enableTextureDumping: boolean
  enableTextureToggling: boolean
  enableTextureScanOnSceneLoad: boolean
  loadUnmodifiedTextures: boolean
  textureHashGenerationStrategy: string
  enableSpriteHooking: boolean

  extra: Record<string, string>
  // Engine API credentials
  googleLegitimateApiKey?: string
  bingLegitimateSubscriptionKey?: string
  baiduAppId?: string
  baiduAppSecret?: string
  yandexApiKey?: string
  deepLLegitimateApiKey?: string
  deepLLegitimateFree: boolean
  papagoClientId?: string
  papagoClientSecret?: string
  lingoCloudToken?: string
  watsonUrl?: string
  watsonKey?: string
  customTranslateUrl?: string
  lecInstallPath?: string
  ezTransInstallPath?: string
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

export type LlmProvider = 'OpenAI' | 'Claude' | 'Gemini' | 'DeepSeek' | 'Qwen' | 'GLM' | 'Kimi' | 'Custom'

export interface ApiEndpointConfig {
  id: string
  name: string
  provider: LlmProvider
  apiBaseUrl: string
  apiKey: string
  modelName: string
  priority: number
  enabled: boolean
}

export interface AiTranslationSettings {
  enabled: boolean
  maxConcurrency: number
  port: number
  systemPrompt: string
  temperature: number
  endpoints: ApiEndpointConfig[]
  glossaryExtractionEnabled: boolean
  glossaryExtractionEndpointId?: string
}

export interface AppSettings {
  mirrorUrl: string
  theme: string
  aiTranslation: AiTranslationSettings
  steamGridDbApiKey?: string
  libraryViewMode: string
  librarySortBy: string
  accentColor: string
  libraryCardSize: string
  libraryGap: string
  libraryShowLabels: boolean
}

export interface VersionInfo {
  version: string
}

export interface RecentTranslation {
  original: string
  translated: string
  timestamp: string
  tokensUsed: number
  responseTimeMs: number
  endpointName: string
}

export interface TranslationError {
  message: string
  timestamp: string
  endpointName?: string
}

export interface TranslationStats {
  totalTranslated: number
  translating: number
  queued: number
  lastRequestAt?: string
  totalTokensUsed: number
  averageResponseTimeMs: number
  requestsPerMinute: number
  enabled: boolean
  recentTranslations: RecentTranslation[]
  totalReceived: number
  totalErrors: number
  recentErrors: TranslationError[]
}

export interface AiEndpointStatus {
  installed: boolean
}

export interface EndpointTestResult {
  endpointId: string
  endpointName: string
  success: boolean
  translations?: string[]
  error?: string
  responseTimeMs: number
}

export interface GlossaryEntry {
  original: string
  translation: string
  isRegex: boolean
}

export interface GlossaryExtractionStats {
  enabled: boolean
  totalExtracted: number
  activeExtractions: number
  totalExtractionCalls: number
  totalErrors: number
  recentExtractions: RecentExtractionItem[]
}

export interface RecentExtractionItem {
  gameId: string
  termsExtracted: number
  timestamp: string
}

export interface SteamGridDbSearchResult {
  id: number
  name: string
  verified: boolean
}

export interface SteamGridDbImage {
  id: number
  url: string
  thumb: string
  width: number
  height: number
  style: string
  mime: string
}

export interface CoverInfo {
  hasCover: boolean
  source?: string
  steamGridDbGameId?: number
}

export interface SteamStoreSearchResult {
  id: number
  name: string
  tinyImage: string
}

export interface LogEntry {
  timestamp: string
  level: string
  category: string
  message: string
}
