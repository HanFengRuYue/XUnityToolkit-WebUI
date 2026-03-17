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
  aiDescription?: string
}

export interface AddGameResponse {
  needsExeSelection: boolean
  game?: Game
}

export type InstallStep =
  | 'Idle'
  | 'DetectingGame'
  | 'InstallingBepInEx'
  | 'InstallingXUnity'
  | 'InstallingTmpFont'
  | 'InstallingAiTranslation'
  | 'GeneratingConfig'
  | 'ApplyingConfig'
  | 'ExtractingAssets'
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
  cacheWhitespaceDifferences: boolean
  ignoreWhitespaceInDialogue: boolean
  minDialogueChars: number
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
  activeMode: 'cloud' | 'local'
  maxConcurrency: number
  port: number
  systemPrompt: string
  temperature: number
  contextSize: number
  localContextSize: number
  endpoints: ApiEndpointConfig[]
  glossaryExtractionEnabled: boolean
  glossaryExtractionEndpointId?: string
  enablePreTranslationCache: boolean
  termAuditEnabled: boolean
  naturalTranslationMode: boolean
}

export type ModelDownloadSource = 'HuggingFace' | 'ModelScope'

export interface AppSettings {
  hfMirrorUrl: string
  modelDownloadSource: ModelDownloadSource
  theme: string
  aiTranslation: AiTranslationSettings
  steamGridDbApiKey?: string
  libraryViewMode: string
  librarySortBy: string
  accentColor: string
  libraryCardSize: string
  libraryGap: string
  libraryShowLabels: boolean
  receivePreReleaseUpdates: boolean
}

export interface VersionInfo {
  version: string
}

export interface DataPathInfo {
  path: string
}

export interface RecentTranslation {
  original: string
  translated: string
  timestamp: string
  tokensUsed: number
  responseTimeMs: number
  endpointName: string
  gameId?: string
}

export interface TranslationError {
  message: string
  timestamp: string
  endpointName?: string
  gameId?: string
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
  currentGameId?: string
  termAuditPhase1PassCount: number
  termAuditPhase2PassCount: number
  termAuditForceCorrectedCount: number
}

export interface AiEndpointStatus {
  installed: boolean
}

export interface TmpFontStatus {
  installed: boolean
}

// ── Font Replacement ──

export interface FontInfo {
  name: string
  pathId: number
  assetFile: string
  isInBundle: boolean
  fontType: 'TMP' | 'TTF'
  isSupported: boolean
  // TMP-specific
  atlasCount: number
  glyphCount: number
  characterCount: number
  atlasWidth: number
  atlasHeight: number
  // TTF-specific
  fontDataSize: number
}

export interface FontTarget {
  pathId: number
  assetFile: string
}

export interface FontReplacementRequest {
  fonts: FontTarget[]
  customFontPath?: string
}

export interface FontReplacementStatus {
  isReplaced: boolean
  replacedFonts: FontInfo[]
  backupExists: boolean
  isExternallyRestored: boolean
  replacedAt?: string
  fontSource?: string
  customFontFileName?: string
}

export interface FontReplacementResult {
  successCount: number
  failedFonts: FailedFontEntry[]
}

export interface FailedFontEntry {
  pathId: number
  assetFile: string
  error: string
}

export interface FontReplacementProgress {
  phase: 'loading' | 'scanning' | 'replacing' | 'saving' | 'clearing-crc' | 'completed'
  current: number
  total: number
  currentFile?: string
}

export interface EndpointTestResult {
  endpointId: string
  endpointName: string
  success: boolean
  translations?: string[]
  error?: string
  responseTimeMs: number
}

// ── Unified Term Management ──

export type TermType = 'translate' | 'doNotTranslate'
export type TermCategory = 'character' | 'location' | 'item' | 'skill' | 'organization' | 'general'

export interface TermEntry {
  type: TermType
  original: string
  translation?: string
  category?: TermCategory
  description?: string
  isRegex: boolean
  caseSensitive: boolean
  exactMatch: boolean
  priority: number
}

/** @deprecated Use TermEntry instead */
export type GlossaryEntry = {
  original: string
  translation: string
  isRegex: boolean
  description?: string
}

/** @deprecated Use TermEntry instead */
export type DoNotTranslateEntry = {
  original: string
  caseSensitive: boolean
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

export interface WebImageResult {
  thumbUrl: string
  fullUrl: string
  width: number | null
  height: number | null
  title: string | null
}

export interface LogEntry {
  timestamp: string
  level: string
  category: string
  message: string
}

export interface ExtractedText {
  text: string
  source: string
  assetFile: string
}

export interface AssetExtractionResult {
  gameId: string
  texts: ExtractedText[]
  detectedLanguage?: string
  totalAssetsScanned: number
  totalTextsExtracted: number
  extractedAt: string
}

export type PreTranslationState = 'Idle' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'

export interface PreTranslationStatus {
  gameId: string
  state: PreTranslationState
  totalTexts: number
  translatedTexts: number
  failedTexts: number
  error?: string
}

export interface TranslationEntry {
  original: string
  translation: string
}

export interface TranslationEditorData {
  filePath: string
  fileExists: boolean
  entryCount: number
  entries: TranslationEntry[]
}

// ── Local LLM ──

export type GpuBackend = 'CUDA' | 'Vulkan' | 'CPU'

export type LocalLlmServerState = 'Idle' | 'Starting' | 'Running' | 'Stopping' | 'Failed'

export interface LocalLlmSettings {
  gpuLayers: number
  contextLength: number
  loadedModelPath?: string
  endpointId: string
  models: LocalModelEntry[]
  pausedDownloads: PausedDownload[]
}

export interface PausedDownload {
  catalogId: string
  bytesDownloaded: number
  totalBytes: number
}

export interface LocalModelEntry {
  id: string
  name: string
  filePath: string
  fileSizeBytes: number
  isBuiltIn: boolean
  catalogId?: string
  addedAt: string
}

export interface GpuInfo {
  name: string
  vendor: string
  vramBytes: number
  recommendedBackend: GpuBackend
}

export interface LocalLlmStatus {
  state: LocalLlmServerState
  loadedModelPath?: string
  loadedModelName?: string
  gpuBackendName?: string
  error?: string
  gpuUtilizationPercent?: number
  gpuVramUsedMb?: number
  gpuVramTotalMb?: number
}

export interface LlamaBackendInfo {
  backend: GpuBackend
  isInstalled: boolean
  llamaVersion: string
}

export interface LlamaStatus {
  bundledVersion: string
  backends: LlamaBackendInfo[]
  recommendedBackend: GpuBackend
}

export interface BuiltInModelInfo {
  id: string
  name: string
  description: string
  fileSizeBytes: number
  recommendedVramGb: number
  huggingFaceRepo: string
  huggingFaceFile: string
  tags: string[]
  modelScopeRepo?: string
  modelScopeFile?: string
}

export interface LocalLlmTestResult {
  success: boolean
  translations?: string[]
  error?: string
  responseTimeMs: number
}

export interface LocalLlmDownloadProgress {
  catalogId: string
  bytesDownloaded: number
  totalBytes: number
  speedBytesPerSec: number
  done: boolean
  error?: string
  paused?: boolean
  useMirror?: boolean
  useModelScope?: boolean
}

// Font Generation
export interface FontUploadInfo {
  fileName: string
  fontName: string
  fileSize: number
}

export interface FontGenerationStatus {
  isGenerating: boolean
  phase: string
  current: number
  total: number
}

export interface GeneratedFontInfo {
  fileName: string
  fontName: string
  glyphCount: number
  fileSize: number
  generatedAt: string
  hasReport: boolean
}

export interface FontGenerationProgress {
  phase: 'parsing' | 'sdf' | 'packing' | 'compositing' | 'serializing'
  current: number
  total: number
  message: string
}

export interface FontGenerationComplete {
  success: boolean
  fontName?: string
  glyphCount: number
  error?: string
  report?: FontGenerationReport
}

export interface CharacterSetConfig {
  builtinSets: string[]
  customCharsetFileName?: string
  translationGameId?: string
  translationFileName?: string
}

export interface CharacterSetPreview {
  totalCharacters: number
  sourceBreakdown: Record<string, number>
  estimatedAtlasCount: number
  exceedsSingleAtlas: boolean
  warnings: string[]
}

export interface FontGenerationReport {
  fontName: string
  totalCharacters: number
  successfulGlyphs: number
  missingGlyphs: number
  missingCharacters: string[]
  totalMissingCount: number
  atlasCount: number
  atlasWidth: number
  atlasHeight: number
  samplingSize: number
  sourceBreakdown: Record<string, number>
  elapsedMilliseconds: number
  renderMode: string
  samplingSizeMode: string
  actualSamplingSize: number
  padding: number
  gradientScale: number
}

export interface CharsetInfo {
  id: string
  name: string
  description: string
  characterCount: number
}

// ── BepInEx Log ──

export interface BepInExLogResponse {
  content: string
  fileSize: number
  lastModified: string
}

export interface BepInExLogAnalysis {
  report: string
  endpointName: string
  analyzedAt: string
}

// Online Update
export type UpdateState = 'None' | 'Checking' | 'Available' | 'Downloading' | 'Ready' | 'Applying' | 'Error'

export interface UpdateCheckResult {
  updateAvailable: boolean
  newVersion?: string
  changelog?: string
  downloadSize: number
  changedPackages: string[]
  changedFileCount: number
  deletedFileCount: number
}

export interface UpdateStatusInfo {
  state: UpdateState
  progress: number
  downloadedBytes: number
  totalBytes: number
  currentPackage?: string
  message?: string
  error?: string
  availableUpdate?: UpdateAvailableInfo
}

export interface UpdateAvailableInfo {
  version: string
  changelog?: string
  downloadSize: number
  changedPackages: string[]
}

export interface PreTranslationCacheStats {
  totalPreTranslated: number
  cacheHits: number
  cacheMisses: number
  newTexts: number
  hitRate: number
  recentMisses: CacheMissEntry[]
}

export interface CacheMissEntry {
  preTranslatedKey: string
  runtimeText: string
  timestamp: string
}

// Script Tag Cleaning
export type ScriptTagAction = 'Extract' | 'Exclude'

export interface ScriptTagRule {
  pattern: string
  action: ScriptTagAction
  description?: string
  isBuiltin: boolean
}

export interface ScriptTagConfig {
  presetVersion: number
  rules: ScriptTagRule[]
}

export interface ScriptTagPreset {
  version: number
  rules: Omit<ScriptTagRule, 'isBuiltin'>[]
}
