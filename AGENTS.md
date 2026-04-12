# AGENTS.md

鏈枃浠剁敤浜庣粰鍚庣画浠ｇ悊鎻愪緵鍙洿鎺ュ鐢ㄧ殑浠撳簱涓婁笅鏂囥€傚綋鍓嶄粨搴撳凡缁忕粺涓€鏀逛负鐢辨牴鐩綍 `AGENTS.md` 缁存姢鍏ㄩ儴椤圭洰绾с€佸悗绔骇銆佸墠绔骇璇存槑锛涘巻鍙?`CLAUDE.md` 宸插叏閮ㄥ垹闄わ紝涓嶅啀浣滀负缁存姢鍏ュ彛銆?

## 1. 鏉冨▉鏂囨。鍏ュ彛

寮€濮嬪姩鎵嬪墠锛屼紭鍏堝弬鑰冭繖浜涙枃浠讹細

- `AGENTS.md`
- `README.md`

瑙勫垯锛?

- `AGENTS.md` 鏄綋鍓嶅敮涓€缁存姢涓殑浠撳簱宸ヤ綔鎵嬪唽锛屾墍鏈夊師 `CLAUDE.md` 鍐呭宸插苟鍏ユ湰鏂囦欢銆?
- `README.md` 涓昏闈㈠悜浜у搧璇存槑銆佹瀯寤哄叆鍙ｅ拰鐢ㄦ埛渚т俊鎭€?
- 浠撳簱鍐呬笉鍐嶄繚鐣欎换浣?`CLAUDE.md`锛涘鍙戠幇閲嶆柊鍑虹幇锛屽簲瑙嗕负闇€瑕佸洖鏀惰繘 `AGENTS.md` 鐨勯噸澶嶆枃妗ｃ€?
- 鑻ユ枃妗ｄ笌婧愮爜鍐茬獊锛屼互婧愮爜涓哄噯锛屽苟鍦ㄦ敼鍔ㄥ悗鍚屾鏇存柊 `AGENTS.md`銆?

琛ュ厖璇存槑锛?

- 浠撳簱鍐?`.claude/` 鐩綍鍙湁 `scheduled_tasks.lock`锛屾病鏈夐澶栭」鐩蹇嗘枃浠躲€?
- 鍚庣涓庡墠绔師鍏堝垎鏁ｅ湪 `CLAUDE.md`銆乣UnityLocalizationToolkit-WebUI/CLAUDE.md`銆乣UnityLocalizationToolkit-Vue/CLAUDE.md` 鐨勫唴瀹癸紝鐜板凡缁熶竴鏁寸悊鍒版湰鏂囦欢鍚庡崐閮ㄥ垎鐨勪笓椤圭珷鑺備腑銆?

## 2. 椤圭洰姒傝

UnityLocalizationToolkit-WebUI 鏄竴涓潰鍚?Unity 娓告垙姹夊寲/缈昏瘧宸ヤ綔娴佺殑 Windows 妗岄潰宸ュ叿锛岃兘鍔涘寘鎷細

- 涓€閿畨瑁?BepInEx 涓?XUnity.AutoTranslator
- 閫氳繃 `LLMTranslate.dll` 灏嗘父鎴忔枃鏈浆鍙戝埌鏈湴 Web API 鍋?AI 缈昏瘧
- 浜戠 LLM 涓庢湰鍦?llama.cpp 妯″紡
- 璧勪骇鎻愬彇涓庨缈昏瘧
- TextMesh Pro 瀛椾綋鏇挎崲涓?SDF 瀛椾綋鐢熸垚
- 娓告垙搴撶鐞嗐€佸皝闈?鍥炬爣/鑳屾櫙鍥剧鐞?
- BepInEx 鏃ュ織鍒嗘瀽銆佹彃浠跺仴搴锋鏌?
- 鏇存柊鍣ㄤ笌 MSI 瀹夎鍖?

## 3. 浠撳簱缁撴瀯

椤跺眰鍏抽敭鐩綍锛?

- `UnityLocalizationToolkit-WebUI/`
  鍚庣涓荤▼搴忋€侫SP.NET Core Minimal API + WinForms/WebView2 瀹夸富銆?
- `UnityLocalizationToolkit-Vue/`
  鍓嶇銆俈ue 3 + TypeScript + Naive UI + Pinia + Vite銆?
- `UnityLocalizationToolkit-WebUI.Tests/`
  鍚庣娴嬭瘯宸ョ▼銆倄Unit锛屽綋鍓嶈鐩栫炕璇戝搷搴旇В鏋愪笌杩愯鏃跺崰浣嶇淇濇姢绛夊叧閿洖褰掋€?
- `TranslatorEndpoint/`
  `net35` 鐨?`LLMTranslate.dll`锛屼緵 XUnity.AutoTranslator 璋冪敤銆?
- `Updater/`
  AOT 鏇存柊鍣紝璐熻矗鏂囦欢鏇挎崲銆佸垹闄ゃ€佸洖婊氥€侀噸鍚€?
- `Installer/`
  WiX 瀹夎鍣ㄥ伐绋嬨€?
- `bundled/`
  鏋勫缓/鍙戝竷鏃堕檮甯︾殑瀛椾綋銆佽剼鏈璁俱€丅epInEx/XUnity/llama 璧勬簮銆?
- `.github/workflows/`
  CI/CD 宸ヤ綔娴併€?

## 4. 鎶€鏈爤

鍚庣锛?

- `.NET 10` `net10.0-windows`
- ASP.NET Core Minimal API
- SignalR
- WinForms + WebView2
- AssetsTools.NET
- FreeTypeSharp

鍓嶇锛?

- Vue 3
- TypeScript
- Naive UI
- Pinia
- Vite 8

鍏朵粬瀛愰」鐩細

- `TranslatorEndpoint`: `net35`, C# 7.3
- `Updater`: `net10.0`, `PublishAot=true`
- `Installer`: WixToolset v6锛堝綋鍓嶅伐绋嬩负 `WixToolset.Sdk/6.0.2`锛?

## 5. 甯哥敤鍛戒护

鍚庣鏋勫缓锛?

```bash
dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj
dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj -p:SkipFrontendBuild=true
dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj
```

鍓嶇锛?

```bash
cd UnityLocalizationToolkit-Vue
npm run dev
npm run build
npx vue-tsc --build
```

娴嬭瘯锛?

```bash
dotnet test
```

琛ュ厖绾︽潫锛?
- `dotnet build UnityLocalizationToolkit-WebUI/...` 涓?`dotnet test UnityLocalizationToolkit-WebUI.Tests/...` 涓嶈骞惰鎵ц锛沗StaticWebAssets` 浼氱珵浜?`obj/.../rpswa.dswa.cache.json`锛屽簲涓茶楠岃瘉

缈昏瘧绔偣锛?

```bash
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release
```

鏈湴瀹屾暣鏋勫缓锛?

```bash
.\build.ps1
.\build.ps1 -SkipDownload
```

閲嶈璇存槑锛?

- `UnityLocalizationToolkit-WebUI.csproj` 榛樿浼氬湪鏋勫缓鍓嶈嚜鍔ㄦ墽琛屽墠绔?`npm install` + `npm run build`銆?
- 鍓嶇寮€鍙戜唬鐞嗗埌 `http://127.0.0.1:51821`锛屼笉瑕佹敼鎴?`localhost`銆?
- 瀹屾暣 UI 棰勮浼樺厛鐪嬪悗绔鍙?`51821`锛屽洜涓哄畠鍚屾椂鎵胯浇闈欐€佸墠绔拰 API銆?
- 鍙戝竷娴佺▼褰撳墠涓嶄細鍦ㄦ瀯寤鸿剼鏈垨 GitHub Actions 涓嚜鍔ㄥ惎鍔?EXE 鍋氶椤?smoke check锛涜嫢闇€瑕佽繍琛屾€侀獙鏀讹紝璇峰崟鐙墽琛屻€?
## 6. 杩愯鏃舵灦鏋?

鍚庣鍚姩鍏ュ彛锛?

- `UnityLocalizationToolkit-WebUI/Program.cs`

涓昏鑱岃矗锛?

- 璇诲彇 `settings.json` 涓?`aiTranslation.port`锛屽姩鎬佸喅瀹氱洃鍚鍙ｏ紝榛樿 `51821`
- 寮哄埗缁戝畾 `http://127.0.0.1:{port}`
- `ContentRootPath` 涓?`WebRootPath` 蹇呴』鍥哄畾鍒?`AppContext.BaseDirectory`锛屼笉瑕佷緷璧栧綋鍓嶅伐浣滅洰褰曪紱鍚﹀垯鏇存柊鍣ㄣ€佸畨瑁呭櫒鎴栧閮ㄥ惎鍔ㄥ櫒浠庨敊璇洰褰曟媺璧锋椂浼氬嚭鐜伴椤?404 浣?API 浠嶅彲璁块棶
- 娉ㄥ唽鍚勭被鍛藉悕 `HttpClient`
- 娉ㄥ唽鎵€鏈夋牳蹇冩湇鍔′负鍗曚緥
- 閰嶇疆 SignalR
- 鎻愪緵闈欐€佹枃浠朵笌 SPA fallback
- 鍚姩鏃ュ織蹇呴』璁板綍 `CurrentDirectory`銆乣BaseDirectory`銆乣ContentRoot`銆乣WebRoot` 浠ュ強 `wwwroot/index.html` 鏄惁瀛樺湪锛涜嫢鍏ュ彛鏂囦欢缂哄け搴旇褰?`Critical`
- 娉ㄥ唽鍏ㄩ儴 Minimal API 绔偣
- 鍦?`ApplicationStopping` 鏃剁珛鍗抽殣钘?UI锛屽苟鍒锋柊鑴忕殑缈昏瘧璁板繂
- 鍦?`ApplicationStarted` 鏃跺紓姝ュ垵濮嬪寲 AI 缈昏瘧鐘舵€侊紝骞惰嚜鍔ㄦ鏌ユ洿鏂?

鍓嶇鍏ュ彛锛?

- `UnityLocalizationToolkit-Vue/src/main.ts`
- `UnityLocalizationToolkit-Vue/src/App.vue`
- `UnityLocalizationToolkit-Vue/src/components/layout/AppShell.vue`

鍓嶇楠ㄦ灦锛?

- `RouterView + KeepAlive + Pinia`
- 椤跺眰椤甸潰锛氭父鎴忓簱銆丄I 缈昏瘧銆佸瓧浣撶敓鎴愩€佽繍琛屾棩蹇椼€佽缃?
- 娓告垙瀛愰〉闈細閰嶇疆缂栬緫銆佽祫浜ф彁鍙栥€佺炕璇戠紪杈戙€佹湳璇紪杈戙€佸瓧浣撴浛鎹€丅epInEx 鏃ュ織銆佹彃浠剁鐞?

瀹炴椂閫氫俊锛?

- 鍗曚釜 SignalR Hub锛歚InstallProgressHub`
- 鍏抽敭鍒嗙粍锛?
  - `game-{id}`
  - `ai-translation`
  - `logs`
  - `pre-translation-{gameId}`
  - `local-llm`
  - `font-replacement-{gameId}`
  - `font-generation`
  - `update`

## 7. 杩愯鏃舵暟鎹竷灞€

杩愯鏃舵暟鎹牴鐩綍锛?

- 榛樿锛歚%AppData%\UnityLocalizationToolkit`
- 鍙€氳繃閰嶇疆閿?`AppData:Root` 瑕嗙洊
- 涓昏璺緞闆嗕腑瀹氫箟鍦?`UnityLocalizationToolkit-WebUI/Infrastructure/AppDataPaths.cs`
- 鏇存柊鏆傚瓨鐩綍 `update-staging/` 褰撳墠鐢?`UnityLocalizationToolkit-WebUI/Services/UpdateService.cs` 鐩存帴绠＄悊

鍏抽敭鏂囦欢/鐩綍锛?

- `library.json`
- `settings.json`
- `local-llm-settings.json`
- `glossaries/`
- `script-tags/`
- `translation-memory/`
- `dynamic-patterns/`
- `term-candidates/`
- `cache/covers`
- `cache/icons`
- `cache/backgrounds`
- `cache/extracted-texts`
- `cache/pre-translation-regex`
  褰撳墠 `cache/pre-translation-regex/<gameId>.txt` 鍙暅鍍?legacy compatibility 鎵€闇€鐨?custom 姝ｅ垯鍖哄潡锛涘畬鏁存墭绠℃枃浠朵綅浜庢父鎴忕洰褰?`BepInEx/Translation/<lang>/Text/_PreTranslated_Regex.txt`
- `cache/pre-translation-sessions`
- `models/`
- `llama/`
- `llama/launch-cache`
- `font-generation/uploads`
- `font-generation/temp`
- `generated-fonts/`
- `font-backups/`
- `custom-fonts/`
  褰撳墠瀛椾綋鏇挎崲鑷畾涔夋簮鎸?`custom-fonts/<gameId>/ttf/` 涓?`custom-fonts/<gameId>/tmp/` 鍒嗙洰褰曠鐞?
- `backups/`
- `logs/`
- `update-staging/`

瀹夊叏鐩稿叧锛?

- API Key 涓?SteamGridDB Key 浣跨敤 DPAPI 鍔犲瘑
- JSON 鍘熷瓙鍐欏叆缁熶竴璧?`FileHelper.WriteJsonAtomicAsync`
- 璺緞鎷兼帴缁熶竴浼樺厛浣跨敤 `PathSecurity.SafeJoin`
- 澶栭儴 URL 鏍￠獙浣跨敤 `PathSecurity.ValidateExternalUrl`

## 8. 鍚庣妯″潡鍦板浘

楂橀鍏抽敭鏈嶅姟锛?

- `GameLibraryService`
  娓告垙搴撳鍒犳敼鏌ワ紝钀界洏鍒?`library.json`
- `AppSettingsService`
  璁剧疆缂撳瓨銆丏PAPI 鍔犺В瀵嗐€佽鏀瑰啓
- `ConfigurationService`
  璇诲啓 `AutoTranslatorConfig.ini`
- `UnityDetectionService`
  妫€娴?Unity 鍚庣銆佹灦鏋勩€佸彲鎵ц鏂囦欢銆乀extMeshPro 鏀寔
- `InstallOrchestrator`
  瀹夎/鍗歌浇缂栨帓
- `LlmTranslationService`
  AI 缈昏瘧鎬诲叆鍙ｏ紝璐熻矗骞跺彂銆佺粺璁°€佹湳璇€乀M銆佺鐐硅皟搴?
- `TranslationMemoryService`
  姣忔父鎴忕炕璇戣蹇嗭紝绮剧‘/妯″紡/妯＄硦鍖归厤
- `PreTranslationService`
  璧勪骇鏂囨湰鎵归噺棰勭炕璇戙€佹湳璇彁鍙栥€佸杞祦绋?
- `LocalLlmService`
  绠＄悊 llama-server銆丟PU 妫€娴嬨€佹ā鍨嬩笅杞姐€乴lama 浜岃繘鍒朵笅杞?
- `AssetExtractionService`
  璧勪骇鎻愬彇
- `FontReplacementService`
  TMP/TTF 瀛椾綋鎵弿涓庢浛鎹?
- `TmpFontGeneratorService`
  SDF 瀛椾綋鐢熸垚
- `UpdateService`
  鏇存柊妫€鏌ャ€佷笅杞姐€佸簲鐢?
- `SystemTrayService`
  鎵樼洏銆佺獥鍙ｆ樉绀?闅愯棌銆侀€氱煡

鍏抽敭绔偣鐩綍锛?

- `UnityLocalizationToolkit-WebUI/Endpoints/`

楂橀绔偣鏂囦欢锛?

- `GameEndpoints.cs`
- `SettingsEndpoints.cs`
- `TranslateEndpoints.cs`
- `TranslationEditorEndpoints.cs`
- `FontGenerationEndpoints.cs`
- `FontReplacementEndpoints.cs`
- `ImageEndpoints.cs`
- `AssetEndpoints.cs`
- `LocalLlmEndpoints.cs`
- `UpdateEndpoints.cs`

## 9. 鍓嶇妯″潡鍦板浘

鏍稿績楠ㄦ灦锛?

- `src/components/layout/AppShell.vue`
- `src/router/index.ts`
- `src/api/client.ts`
- `src/api/games.ts`
- `src/api/types.ts`

楂橀椤甸潰锛?

- `src/views/GameDetailView.vue`
- `src/views/AiTranslationView.vue`
- `src/views/SettingsView.vue`
- `src/views/AssetExtractionView.vue`
- `src/views/FontGeneratorView.vue`
- `src/views/TermEditorView.vue`
- `src/views/TranslationEditorView.vue`
- `src/views/LibraryView.vue`

楂橀缁勪欢锛?

- `src/components/settings/LocalAiPanel.vue`
- `src/components/settings/AiTranslationCard.vue`
- `src/components/config/ConfigPanel.vue`
- `src/components/common/FileExplorerModal.vue`
- `src/components/translation/RegexRuleEditor.vue`

鏍稿績 store锛?

- `src/stores/games.ts`
- `src/stores/theme.ts`
- `src/stores/sidebar.ts`
- `src/stores/install.ts`
- `src/stores/assetExtraction.ts`
- `src/stores/update.ts`

鏍稿績 composable锛?

- `src/composables/useAddGameFlow.ts`
- `src/composables/useAutoSave.ts`
- `src/composables/useFileExplorer.ts`
- `src/composables/useWindowControls.ts`

## 10. 缈昏瘧閾捐矾閫熻

鍦ㄧ嚎缈昏瘧涓婚摼璺細

1. 娓告垙鍐?XUnity.AutoTranslator 鎹曡幏鏂囨湰
2. `LLMTranslate.dll` 灏嗘枃鏈彂鍒?`POST /api/translate`
3. `LlmTranslationService` 澶勭悊鏈銆乀M銆佺鐐归€夋嫨銆佸苟鍙戞帶鍒?
4. 缁撴灉杩斿洖 DLL锛屽啀鍥炴樉鍒版父鎴?

閲嶈浜嬪疄锛?

- `POST /api/translate` 鏄粰 DLL 鐩存帴璋冪敤鐨勶紝杩斿洖鏍煎紡涓嶆槸甯歌 `ApiResult<T>`
- `LLMTranslate.dll` 鐩爣妗嗘灦鏄?`net35`
- `LLMTranslate.dll` 閫氳繃 `[LLMTranslate]` INI 鍖烘璇诲彇 `ToolkitUrl`銆乣GameId` 绛夐厤缃?
- `Program.cs` 蹇呴』浣跨敤 `127.0.0.1`锛屼笉瑕佷娇鐢?`localhost`
- `LlmTranslationService.TranslateDetailedAsync(...)` 鏄炕璇戜富閾捐矾鐨勬潈濞佸疄鐜帮紱`TranslateAsync(...)` 鍙槸杩斿洖 `Translations` 鐨勮交鍖呰銆傚嚒鏄渶瑕佸喅瀹氣€滄槸鍚﹀厑璁稿啓鍏?TM / 鏈鎻愬彇 / 杩愯鏃朵笂涓嬫枃缂撳瓨鈥濈殑璋冪敤鐐癸紝閮藉繀椤讳娇鐢ㄨ缁嗙粨鏋滆€屼笉鏄彧鎷垮瓧绗︿覆鏁扮粍
- LLM 杩斿洖瑙ｆ瀽椤哄簭褰撳墠鍥哄畾涓猴細鍓ョ `<think>` / 浠ｇ爜鍧楀寘瑁?鈫?JSON 鏁扮粍 鈫?鍗曟潯 JSON 瀛楃涓诧紙浠呭崟鏉″満鏅級鈫?鍗曟潯绾枃鏈€欓€夛紙浠呭崟鏉″満鏅級锛涙壒閲忓満鏅笉鍐嶆帴鍙楅潪缁撴瀯鍖栧師濮嬭緭鍑轰綔涓鸿瘧鏂?- 鍗曟潯绾枃鏈€欓€夋ā寮忎細缁х画淇濈暀缁欐湰鍦?LLM 鍏煎浣跨敤锛屼絾鎵€鏈夎鎺ュ彈鐨勮瘧鏂囩幇鍦ㄩ兘蹇呴』棰濆缁忚繃 `TranslationOuterWrapperGuard` 妫€鏌ワ紱鑻ュ師鏂囨病鏈夋暣鍙ュ灞傚紩鍙?鎷彿锛岃€屽€欓€夋枃鏈柊澧炰簡 `鈥溾€漙銆乣銆屻€峘銆乣銆庛€廯銆乣銆愩€慲銆乣[]`銆乣""`銆乣''` 杩欑被鏁村彞鍖呰９锛屽垯浼氳嚜鍔ㄥ幓澹?- `POST /api/translate` 涓?`PreTranslationService` 鐜板湪閮戒細杩囨护 `Persistable == false` 鐨勭粨鏋滐細杩欎簺缁撴灉鍙互鍥炴樉缁欒皟鐢ㄦ柟锛屼絾涓嶈兘杩涘叆鑷姩鏈鎻愬彇銆佽繍琛屾椂涓婁笅鏂囩紦瀛樻垨缈昏瘧璁板繂

澶氶樁娈电炕璇戯細

- Phase 0锛歍M 鏌ユ壘
- Phase 1锛氳嚜鐒剁炕璇?
- Phase 2锛氭湳璇?DNT 鍗犱綅绗︽浛鎹?
- Phase 3锛氬己鍒朵慨姝?
- 鍦?Phase 1 / Phase 2 杩涘叆 LLM 涔嬪墠锛岃繍琛屾椂鍗犱綅绗︿細鍏堟浛鎹㈡垚鍐呴儴 `{{XU_RT_n}}` 鍗犱綅绗︼紱LLM 杩斿洖鍚庝細鍋氬鏉炬仮澶嶏紝浣嗘渶缁堝繀椤婚€愬瓧鍥炲埌婧愭枃鏈腑鐨勫師濮?token
- 杩愯鏃跺崰浣嶇褰撳墠鍚屾椂瑕嗙洊鍗婅/鍏ㄨ `SPECIAL_*`锛堝 `[SPECIAL_01]`銆乣銆怱PECIAL_01銆慲锛変互鍙婂畨鍏ㄧ櫧鍚嶅崟鍐呯殑鑺辨嫭鍙锋ā鏉垮彉閲忥紙濡?`{PLAYER}`銆乣{PC}`銆乣{Quest_Id}`锛夛紱鎷彿鏍峰紡銆佸ぇ灏忓啓銆佹暟閲忓拰浣嶇疆閮藉繀椤讳笌杈撳叆瀹屽叏涓€鑷淬€備换涓€鐜妭鏍￠獙澶辫触鏃讹紝鏁存瀹夊叏鍥為€€鍘熸枃
- Phase 0 鐨?TM 鍛戒腑涔熷繀椤荤粡杩囧悓鏍风殑杩愯鏃跺崰浣嶇 round-trip 鏍￠獙锛涘巻鍙插潖缂撳瓨鍛戒腑瑕佽涓?miss锛屼笉鑳界户缁鐢?- Phase 0 / Phase 1 / Phase 2 / Phase 3銆乀M 鍙寔涔呭寲杩囨护銆侀缈昏瘧鍔ㄦ€佹鍒欑敓鎴愪笌 `_PreTranslated.txt` 鍐欏洖锛岀幇鍦ㄩ兘蹇呴』澶嶇敤鍚屼竴濂椻€滃灞傚寘瑁瑰畧鍗€濓紱鍘诲３鍚庤嫢涓虹┖鎴栧叏绌虹櫧锛岃瑙嗕负鏃犳晥缁撴灉骞堕樆姝㈠啓鍏ョ紦瀛?TM

缈昏瘧璁板繂锛?

- 姣忔父鎴忔寔涔呭寲
- 椤哄簭锛氱簿纭?鈫?鍔ㄦ€佹ā寮?鈫?妯＄硦
- 鍐欏叆鏄悓姝ュ姞鍏ュ唴瀛橈紝鎸佷箙鍖栨湁 5 绉掗槻鎶?
- 鍏抽棴鏃朵細寮哄埗鍒锋柊鑴忔暟鎹?

鏈绯荤粺锛?

- 缁熶竴鏈琛ㄦ浛浠ｆ棫鐨?glossary/do-not-translate 鍒嗙璁捐
- `TermService` 绠＄悊姣忔父鎴忔湳璇?
- `TermMatchingService` 鍋氬懡涓笌鍗犱綅绗﹀鐞?
- `TermAuditService` 鍋氶樁娈靛悎瑙勫鏌?

鑴氭湰鏍囩锛?

- `ScriptTagService` 璐熻矗娓呯悊涓庣紦瀛樺綊涓€鍖?
- 涓庨缈昏瘧缂撳瓨銆佹湳璇拰缈昏瘧璁板繂鍏崇郴绱у瘑锛屼慨鏀规椂瑕佹敞鎰?`NormalizeForCache` 璋冪敤鐐逛竴鑷存€?

棰勭炕璇戞鏌ョ偣锛?

- `PreTranslationService` 浼氭妸姣忔父鎴忔仮澶嶆鏌ョ偣鍐欏埌 `cache/pre-translation-sessions/<gameId>.json`
- 鎭㈠璧勬牸鍙栧喅浜庢彁鍙栨枃鏈粡杩?`ScriptTagService.FilterAndCleanAsync(...)` 鍚庡緱鍒扮殑鏂囨湰绛惧悕锛涙彁鍙栫紦瀛樻垨鑴氭湰鏍囩瑙勫垯鍙樺寲鏃跺繀椤婚樆姝?resume锛屽苟瑕佹眰閲嶆柊寮€濮?
- `GET /api/games/{id}/pre-translate/status` 鏄潪杩愯鎬?checkpoint 瑙ｆ瀽鍚庣殑鏉冨▉鐘舵€侊紱鍙栨秷鎴栧け璐ュ悗锛宍CanResume` / `ResumeBlockedReason` 蹇呴』浠ュ畠閲嶆柊鏍￠獙鍚庣殑缁撴灉涓哄噯锛岃€屼笉鏄洿鎺ュ鐢ㄧ粓鎬佸箍鎾噷鐨勫師濮嬪瓧娈?
- 鍒犻櫎 `extracted-texts` 缂撳瓨鎴栧垹闄ゆ父鎴忔椂锛屽繀椤诲悓鏃舵竻鐞嗗搴旂殑棰勭炕璇戞鏌ョ偣

棰勭炕璇戞枃鏈笌姝ｅ垯鏂囦欢锛?

- `TranslationEditorPathResolver` 鏄櫘閫氳瘧鏂囥€侀缈昏瘧鏂囨湰涓庨缈昏瘧姝ｅ垯鏂囦欢璺緞鐨勫敮涓€鏉冨▉鍏ュ彛锛涙櫘閫氳瘧鏂囨部鐢?`config.OutputFile`/`TargetLanguage`锛岄缈昏瘧鏂囨湰涓庢鍒欏浐瀹氳惤鍒?`BepInEx/Translation/<lang>/Text/_PreTranslated.txt` 涓?`_PreTranslated_Regex.txt`
- `lang` 瑙ｆ瀽椤哄簭鍥哄畾涓猴細鏄惧紡璇锋眰璇█ 鈫?`TargetLanguage` 鈫?娓告垙鐩綍閲屽凡瀛樺湪鐨勯缈昏瘧鏂囦欢鎵弿缁撴灉 鈫?`zh`锛涜瑷€浠ｇ爜涓庢渶缁堣矾寰勯兘蹇呴』缁忚繃 resolver 鏍￠獙锛屼笉鑳界粫寮€ `BepInEx` 鏍圭洰褰?
- `PreTranslationRegexFormat` 缁熶竴绠＄悊 `_PreTranslated_Regex.txt` 鐨?`base` / `custom` / `dynamic` 涓変釜鍖哄潡锛涘啀娆℃墽琛岄缈昏瘧鏃跺彧淇濈暀 `custom`锛宍base` 涓?`dynamic` 閮戒細琚郴缁熼噸寤?
- `GET/PUT /api/games/{id}/pre-translate/regex` 涓?`cache/pre-translation-regex/<gameId>.txt` 鐜板湪鍙壙鎷?custom 鍖哄潡鍏煎灞傦紱鏀瑰姩鎵樼鏍煎紡鏃跺繀椤诲悓鏃剁淮鎶ゅ吋瀹瑰眰鐨勮鍐?

## 11. 鏈湴 LLM 閫熻

涓绘湇鍔★細

- `UnityLocalizationToolkit-WebUI/Services/LocalLlmService.cs`

鍏抽敭鐐癸細

- GPU 妫€娴嬩紭鍏?DXGI锛學MI 鍏滃簳
- 鍚庣閫夋嫨閫昏緫锛歂VIDIA鈫扖UDA锛孉MD/Intel鈫扸ulkan锛屾棤鏄惧崱鈫扖PU
- 鏈湴妯″紡寮哄埗鏇翠繚瀹堢殑骞跺彂涓庢壒澶勭悊
- llama.cpp 鐗堟湰褰撳墠鍥哄畾涓?`b8756`
- 涓嬭浇妯″瀷鏀寔 HuggingFace 涓?ModelScope
- 妯″瀷鍚姩璺緞蹇呴』璧?`LocalLlmLaunchPathResolver`锛氬厛灏濊瘯鐩稿璺緞锛屽啀灏濊瘯 Windows 8.3 鐭矾寰勶紝鏈€鍚庢墠鍦?`llama/launch-cache/` 鍒涘缓 ASCII hard link / symbolic link 鍒悕锛涗笉瑕佺粫寮€杩欐潯鍏滃簳閾捐矾
- llama 浜岃繘鍒跺拰妯″瀷鏄袱涓蹇碉細
  - llama 杩愯鏃朵簩杩涘埗锛歚bundled/llama/` 鎴栫敤鎴蜂笅杞?
  - 妯″瀷鏂囦欢锛歚%AppData%\UnityLocalizationToolkit\models`

鐗堟湰鍚屾鐐癸細

- `build.ps1`
- `.github/workflows/build.yml`
- `LocalLlmService.LlamaVersion`
- 鏂囨。鎻忚堪

## 12. 瀛椾綋涓庤祫婧愬鐞嗛€熻

瀛椾綋鏇挎崲锛?

- `FontReplacementService`
- 鏀寔鎵弿骞舵浛鎹?TMP_FontAsset
- 涔熸敮鎸佹壂鎻?Unity Legacy `Font` 璧勬簮锛涙敮鎸佺洿鎺ユ浛鎹?`dynamicEmbedded` 绫诲瀷鐨?TTF/OTF锛屼篃鏀寔鎶婁緷璧?`FontNames` 鐨?`osFallback` / 鍚嶇О鏄犲皠鍔ㄦ€佸瓧浣撹浆涓哄唴宓屽瓧浣?
- 甯?`CharacterRects` 鐨勯潤鎬佸浘闆嗗瓧浣撲互鍙婃ā寮忎笉鏄庣殑 Legacy `Font` 浼氭槑纭爣璁颁负涓嶆敮鎸侊紱`osFallback` 杞唴宓屾椂榛樿淇濈暀鍘?`FontNames` 浣滀负缂哄瓧鍏滃簳
- Legacy `Font.m_FontData` 鍦ㄤ笉灏戞父鎴忛噷鏄?`vector -> Array -> char` 缁撴瀯锛屽瓧娈?`Value` 鍙兘涓?`null`锛涙壂鎻忓拰鏇挎崲鏃跺垽鏂瓧鑺傞暱搴﹁浼樺厛鐪?`m_FontData["Array"].Children.Count`锛屼笉鑳藉彧渚濊禆 `AsByteArray`
- TTF 鏇挎崲鍐欏洖鍚庡繀椤荤珛鍗抽噸鏂拌鍙栫洰鏍?`Font` 骞堕獙璇?`FontDataSize ==` 鐩爣瀛楄妭闀垮害涓?`TtfMode == dynamicEmbedded`锛涢獙璇佸け璐ヨ瑙嗕负鏇挎崲澶辫触骞跺洖婊氳瀛椾綋
- 鑷畾涔夋浛鎹㈡簮鎸夋父鎴忛殧绂伙紝鐩綍涓?`custom-fonts/<gameId>/ttf/` 涓?`custom-fonts/<gameId>/tmp/`锛屾敮鎸佺疮璁′笂浼犲涓簮锛屼笉鍐嶆寜绫诲瀷鏁寸被瑕嗙洊
- 鏇挎崲璇锋眰鎸夐€愬瓧浣?`sourceId` 浼犻€掞紝鍏佽鍚屼竴娆℃搷浣滈噷涓轰笉鍚?TMP / TTF 瀛椾綋閫夋嫨涓嶅悓榛樿婧愭垨鑷畾涔夋簮
- 鐘舵€佹帴鍙ｄ細杩斿洖 `availableSources` 涓?`usedSources`锛涘浠芥竻鍗曚腑鐨?`ReplacedFontEntry` 浼氳褰?`SourceId` 涓?`SourceDisplayName`
- `GET /font-replacement/status` 褰撳墠涓昏杩斿洖澶囦唤銆佹潵婧愬拰澶栭儴杩樺師鐘舵€侊紱鍏朵腑 `ReplacedFonts` 鏉ヨ嚜 `manifest.json` 鎽樿锛屼笉鏄疄鏃堕噸鎵粨鏋溿€傞渶瑕佸綋鍓?`ttfMode` / `fontDataSize` 鏃讹紝浠?`POST /font-replacement/scan` 涓哄噯
- 鏇挎崲鍓嶄細寤虹珛澶囦唤锛屾仮澶嶄緷璧栧浠芥竻鍗曞拰鍝堝笇

瀛椾綋鐢熸垚锛?

- `TmpFontGeneratorService`
- 鍩轰簬 `FreeTypeSharp + EDT`
- 杈撳嚭鐢ㄤ簬 TMP 鐨?SDF 瀛椾綋璧勬簮

璧勪骇鎻愬彇锛?

- `AssetExtractionService`
- 浣跨敤 AssetsTools.NET
- 鐩爣鏄彁鍙栧彲缈昏瘧鏂囨湰锛屼緵棰勭炕璇戜笌缂撳瓨鐢熸垚

## 13. 鏇存柊涓庡彂甯冮€熻

鏈湴鏋勫缓鑴氭湰锛?

- `build.ps1`

浣滅敤锛?

- 涓嬭浇 bundled 璧勬簮
- 鎻愬彇 XUnity 渚濊禆 DLL 鍒?`TranslatorEndpoint/libs`
- 鏋勫缓鍓嶇
- 鏋勫缓 `LLMTranslate.dll`
- 鏋勫缓 AOT `Updater`
- 鍙戝竷鍒?`Release/win-x64`

CI锛?

- `.github/workflows/build.yml`
- `.github/workflows/release.yml`
- `.github/workflows/dep-check.yml`

閲嶈浜嬪疄锛?

- CI 閫昏緫涓?`build.ps1` 鏄袱浠藉苟琛岀淮鎶ょ殑瀹炵幇锛屾敼鏋勫缓娴佺▼鏃跺繀椤诲弻鏀?
- CI 涓嶇洿鎺ヨ皟鐢?`build.ps1`
- 鏇存柊鍣ㄦ槸澧為噺鏇存柊鐨勫叧閿粍浠讹紝鍚浠姐€佹浛鎹€佸垹闄ゃ€佸洖婊氶€昏緫
- 鑻ヨ皟鏁村惎鍔ㄧ鍙ｈВ鏋愩€侀潤鎬佽祫婧愮洰褰曟垨鍚姩鏂瑰紡锛屾敞鎰忓垎鍒瘎浼版湰鍦版瀯寤鸿剼鏈笌 GitHub Actions 鐨勫彂甯冭涓猴紝浣嗗綋鍓嶆病鏈夊唴缃?smoke check 瀹堝崼
- MSI 鐢?WiX 鐢熸垚锛屼笖涓嶅悓 edition 鏋勫缓鏃跺繀椤绘敞鎰忔竻鐞?`Installer/obj/...`

## 14. 鍏抽敭绾︽潫涓庝笉鍙橀噺

閫氱敤锛?

- 杩斿洖 `ApiResult` 鐨勭鐐硅鐢?`Results.Ok(ApiResult<T>.Ok(...))`
- 杈撳叆楠岃瘉澶辫触鏃跺簲杩斿洖 `BadRequest`锛屼笉瑕佽繑鍥?200 + fail body
- 鏂板姣忔父鎴忔暟鎹枃浠舵椂锛屽繀椤诲悓姝ヨˉ鍏呭垹闄ら€昏緫涓庣紦瀛樻竻鐞?
- 鏂板璁剧疆瀛楁鏃讹紝瑕佹鏌ュ悗绔粯璁ゅ€笺€乀S 绫诲瀷銆佸墠绔粯璁ゅ€笺€佷繚瀛橀€昏緫鏄惁鍚屾
- 鎻愪氦鍒?Git 涓?GitHub 鐨勬彁浜ゆ爣棰樸€佹彁浜ゆ鏂囥€丳R 鏍囬銆丳R 鎻忚堪銆佽瘎瀹″洖澶嶇瓑鍗忎綔鏂囨缁熶竴浣跨敤涓枃锛屼繚鎸佷笌浠撳簱鏃㈡湁鍘嗗彶椋庢牸涓€鑷达紱浠呭湪寮曠敤澶栭儴涓撴湁鍚嶈瘝銆佹帴鍙ｅ悕鎴栧懡浠ゆ椂淇濈暀蹇呰鑻辨枃
- Git 鎻愪氦鏍囬榛樿浣跨敤 `type: 涓枃鎽樿` 鍗曡鏍煎紡锛宍type` 缁熶竴浣跨敤浠撳簱鍜屾洿鏂伴潰鏉垮凡璇嗗埆鐨勮嫳鏂囧皬鍐欑被鍨嬶細`feat`銆乣fix`銆乣docs`銆乣refactor`銆乣perf`銆乣ci`銆乣chore`銆乣style`銆乣test`锛涗笉瑕佸啓鎴愪腑鏂囩被鍨嬨€乪moji銆佹棤绫诲瀷鏍囬銆乣[鏍囩] 鏍囬`銆乣type(scope):` 鎴栧叾浠栬嚜閫犲墠缂€
- Git 鎻愪氦鏍囬涓殑鍐掑彿蹇呴』浣跨敤鍗婅 `:` 涓斿悗璺熶竴涓┖鏍硷紱鎽樿瑕佺洿鎺ユ弿杩版湰娆′富瑕佹敼鍔ㄧ粨鏋滐紝淇濇寔涓枃銆佸叿浣撱€佸彲璇伙紝閬垮厤鈥滄洿鏂颁竴涓嬧€濃€滀慨涓€鐐归棶棰樷€濃€滆嫢骞茶皟鏁粹€濊繖绫荤┖娉涜〃杩?
- 宸ュ叿绠卞唴鏇存柊鍐呭褰撳墠鐢?`.github/workflows/build.yml` 閫氳繃 `git log --pretty=format:"- %s (\`%h\`)" --no-merges` 鐢熸垚锛屽啀鐢?`UnityLocalizationToolkit-Vue/src/views/SettingsView.vue` 鎸?`- type: description (hash)` 瑙ｆ瀽锛涙兂璁╂洿鏂伴潰鏉挎纭樉绀虹被鍨嬪窘鏍囧拰鏂囨湰鏃讹紝鎻愪氦鏍囬蹇呴』閬靛惊涓婅堪鏍煎紡锛屼笖涓嶈鎶婂叧閿彉鏇翠俊鎭彧鍐欒繘鎻愪氦姝ｆ枃鎴?merge commit 鏍囬
- Git 鎻愪氦姝ｆ枃鐢ㄤ簬琛ュ厖鑳屾櫙銆佺害鏉熴€侀闄╀笌楠岃瘉缁撹锛岀粺涓€涓枃涔﹀啓锛涙鏂囧簲鍥寸粫鈥滀负浠€涔堟敼 / 鏀逛簡浠€涔?/ 闇€瑕佹敞鎰忎粈涔堚€濆睍寮€锛屼笉瑕佸鑻辨枃妯℃澘銆丄I 濂楄瘽鎴栦笌瀹為檯鏀瑰姩涓嶇鐨勬硾鍖栨€荤粨
- 鐗堟湰鐩稿叧鎻愪氦娌跨敤浠撳簱鏃㈡湁椋庢牸锛氭寮忓彂甯冧娇鐢?`feat: 鍙戝竷 vX.Y`锛岀函鐗堟湰鍙锋姮鍗囦娇鐢?`chore: 鐗堟湰鍙锋彁鍗囪嚦 vX.Y`
- 鑻ユ彁浜ゅ寘鍚?`.github/workflows/*`锛岀敤浜?`git push` / `gh` 鐨?GitHub 鍑嵁蹇呴』鍏峰 `workflow` scope锛涘彧鏈?`repo`/`read:org`/`gist` 鏃讹紝鏈湴鎻愪氦浼氭垚鍔燂紝浣嗚繙绔細鎷掔粷鏇存柊 workflow 鏂囦欢

鍚庣锛?

- 涓嶈浠庡ご閲嶅缓 `AutoTranslatorConfig.ini`锛岃€屾槸浣跨敤琛ヤ竵寮忎慨鏀?
- `AppSettingsService.GetAsync()` 杩斿洖缂撳瓨瀵硅薄锛屼笉瑕佸師鍦颁贡鏀癸紝浼樺厛 `UpdateAsync`/`SaveAsync`
- 鐑矾寰勯伩鍏嶇鐩?I/O锛屽挨鍏?`POST /api/translate`
- `GameId` 鐢ㄤ綔鏂囦欢璺緞鏃跺繀椤绘牎楠?GUID
- 鐢ㄦ埛鎻愪緵鐨?URL 鍦ㄧ湡姝ｈ姹傚墠蹇呴』璧?SSRF 鏍￠獙
- `Program.cs` 涓椤甸潤鎬佽祫婧愭牴鐩綍蹇呴』閿氬畾鍒?`AppContext.BaseDirectory`锛屼笉瑕佽 `wwwroot` 璺熼殢 `Environment.CurrentDirectory`

鍓嶇锛?

- 椤跺眰缂撳瓨椤甸潰渚濊禆 `KeepAlive`锛屾秹鍙?SignalR銆佸畾鏃跺櫒銆亀indow 鐩戝惉鍣ㄦ椂蹇呴』鍚屾椂澶勭悊 `onActivated`/`onDeactivated`/`onBeforeUnmount`
- 涓嶈鐩存帴浠庨〉闈慨鏀?store 鐨勫唴閮ㄧ姸鎬侊紝浼樺厛璧?actions
- 鑷姩淇濆瓨椤甸潰鍒囨崲/鍒锋柊鏁版嵁鏃讹紝閬靛惊锛?
  `disable -> load/assign -> nextTick -> enable`
- UI 鏀瑰姩鍚庯紝鑷冲皯鎵ц锛?
  - `npx vue-tsc --build`
  - `npm run build`
- 鑻ユ槸鏄庢樉瑙嗚鏀瑰姩锛屼紭鍏堣ˉ鍋氫竴娆″疄闄?UI 楠岃瘉

## 15. 楂橀鍚屾鐐?

浠ヤ笅鏀瑰姩闈炲父瀹规槗婕忥細

- `InstallStep`
  闇€瑕佸悓姝ュ悗绔ā鍨嬨€乀S 绫诲瀷銆佸畨瑁呰繘搴?UI銆佺姸鎬佹枃妗堟槧灏?
- `AppSettings`
  闇€瑕佸悓姝ワ細
  - `Models/AppSettings.cs`
  - `src/api/types.ts`
  - 鐩稿叧 store 鐨勮鍐?
  - `SettingsView.vue`
- `AiTranslationSettings`
  闇€瑕佸悓姝ワ細
  - C# 妯″瀷
  - TS 绫诲瀷
  - AI 缈昏瘧椤?  - 璁剧疆椤甸粯璁ゅ€?  - 鍚庣 `Math.Clamp`
  - 榛樿绯荤粺鎻愮ず璇嶆枃妗堬紱褰撳墠鍚庣榛樿鍊间笌 `UnityLocalizationToolkit-Vue/src/constants/prompts.ts` 蹇呴』鍚屾椂瑕佹眰 `[SPECIAL_01]` / `銆怱PECIAL_01銆慲 / `{PLAYER}` 杩欑被鍗犱綅绗︽寜杈撳叆鍘熸牱淇濈暀锛屽苟鏄庣‘绂佹妯″瀷鎿呰嚜鏂板鏁村彞澶栧眰寮曞彿/鎷彿銆佽璇濅汉鍓嶇紑銆佽В閲婃€ф枃鏈?- `LocalLlmSettings`
  闇€瑕佸悓姝ワ細
  - C# 妯″瀷
  - `LocalLlmEndpoints`
  - TS 绫诲瀷
  - `LocalAiPanel.vue`
- `TranslationEditor`
  闇€瑕佸悓姝ワ細
  - `TranslationEditorEndpoints.cs`
  - `TranslationEditorPathResolver.cs`
  - `PreTranslationRegexFormat.cs`
  - `src/api/types.ts`
  - `src/api/games.ts`
  - `TranslationEditorView.vue`
  - `RegexRuleEditor.vue`
  - `AssetExtractionView.vue` 閲岀殑璺宠浆鍏ュ彛涓?query 鍙傛暟
- 鏂?per-game 缂撳瓨/鐩綍
  闇€瑕佸悓姝ワ細
  - `AppDataPaths.cs`
  - 鍒犻櫎閫昏緫
  - 瀵煎嚭鎺掗櫎鍒楄〃
  - 娓呯紦瀛橀€昏緫

## 16. 鎺ㄨ崘闃呰椤哄簭

褰撻渶瑕佹帴鎵嬫煇涓€鍧楁椂锛屽缓璁紭鍏堢湅锛?

瀹夎涓庢父鎴忔帴鍏ワ細

- `UnityLocalizationToolkit-WebUI/Endpoints/GameEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Services/InstallOrchestrator.cs`
- `UnityLocalizationToolkit-WebUI/Services/UnityDetectionService.cs`
- `UnityLocalizationToolkit-WebUI/Services/BepInExInstallerService.cs`
- `UnityLocalizationToolkit-WebUI/Services/XUnityInstallerService.cs`

鍦ㄧ嚎缈昏瘧涓庨缈昏瘧锛?

- `UnityLocalizationToolkit-WebUI/Endpoints/TranslateEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Endpoints/TranslationEditorEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Services/LlmTranslationService.cs`
- `UnityLocalizationToolkit-WebUI/Services/TranslationMemoryService.cs`
- `UnityLocalizationToolkit-WebUI/Services/PreTranslationService.cs`
- `UnityLocalizationToolkit-WebUI/Services/TranslationEditorPathResolver.cs`
- `UnityLocalizationToolkit-WebUI/Services/PreTranslationRegexFormat.cs`
- `UnityLocalizationToolkit-WebUI/Services/TermService.cs`

鏈湴妯″瀷锛?

- `UnityLocalizationToolkit-WebUI/Endpoints/LocalLlmEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Services/LocalLlmService.cs`
- `UnityLocalizationToolkit-Vue/src/components/settings/LocalAiPanel.vue`

瀛椾綋鐩稿叧锛?

- `UnityLocalizationToolkit-WebUI/Endpoints/FontReplacementEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Endpoints/FontGenerationEndpoints.cs`
- `UnityLocalizationToolkit-WebUI/Services/FontReplacementService.cs`
- `UnityLocalizationToolkit-WebUI/Services/TmpFontGeneratorService.cs`

鍓嶇涓讳氦浜掞細

- `UnityLocalizationToolkit-Vue/src/components/layout/AppShell.vue`
- `UnityLocalizationToolkit-Vue/src/views/GameDetailView.vue`
- `UnityLocalizationToolkit-Vue/src/views/AiTranslationView.vue`
- `UnityLocalizationToolkit-Vue/src/views/SettingsView.vue`
- `UnityLocalizationToolkit-Vue/src/api/games.ts`
- `UnityLocalizationToolkit-Vue/src/api/types.ts`

鏋勫缓/鍙戝竷/鏇存柊锛?

- `build.ps1`
- `.github/workflows/build.yml`
- `Updater/Program.cs`
- `Installer/Package.wxs`

## 17. 鏈浠撳簱妫€鏌ョ粨璁?

鎴嚦鏈鏁寸悊鏃讹紝宸茬‘璁わ細

- 浠撳簱褰撳墠瀛樺湪瀹屾暣鐨勯」鐩骇璇存槑鏂囨。锛屼笖涓庢簮鐮佷富骞插ぇ浣撲竴鑷?
- 鏍圭洰褰?`AGENTS.md` 宸叉垚涓虹粺涓€缁存姢鍏ュ彛锛屽師涓変唤 `CLAUDE.md` 鐨勫唴瀹瑰凡瀹屾垚鍚堝苟骞跺垹闄ゆ棫鏂囦欢
- `.claude/` 涓病鏈夐澶栭」鐩鏄?
- 鏋勫缓閾捐矾銆佸墠鍚庣鍏ュ彛銆佽繍琛屾椂鏁版嵁鐩綍銆佹洿鏂板櫒銆佺炕璇戠鐐瑰潎宸插仛杩囬潤鎬佹牳瀵?

## 18. 缁存姢寤鸿

鍚庣画濡傛湁涓嬪垪鍙樻洿锛岃鍚屾鏇存柊鏈枃浠讹細

- 鏂板椤跺眰瀛愰」鐩?
- 璋冩暣鏋勫缓/鍙戝竷娴佺▼
- 淇敼杩愯鏃舵暟鎹洰褰曞竷灞€
- 澶у箙閲嶆瀯缈昏瘧閾捐矾銆佹湰鍦?LLM銆佸瓧浣撻摼璺€佹洿鏂伴摼璺?
- 寮曞叆鏂扮殑楂橀鍚屾鐐规垨鏂扮殑鍏ㄥ眬涓嶅彉閲?

## 19. 鎺ュ彛鐭╅樀琛ュ厖

浠ヤ笅鎺ュ彛鏃忓師鍏堝垎鏁ｈ褰曞湪鍘嗗彶 `CLAUDE.md` 涓紝鐜扮粺涓€鏀舵暃鍒版锛?

- 娓告垙绠＄悊锛歚GET/POST /api/games`銆乣GET/DELETE /api/games/{id}`銆乣POST /api/games/add-with-detection`銆乣POST /api/games/batch-add`銆乣PUT /api/games/{id}`銆乣POST /api/games/{id}/detect`銆乣POST /api/games/{id}/open-folder`銆乣POST /api/games/{id}/launch`
- TMP 瀛椾綋锛歚GET/POST/DELETE /api/games/{id}/tmp-font`
- 瀹夎涓庣姸鎬侊細`POST /api/games/{id}/install`銆乣DELETE /api/games/{id}/install`銆乣GET /api/games/{id}/status`銆乣POST /api/games/{id}/cancel`
- 鍥炬爣銆佸皝闈€佽儗鏅細鍧囨彁渚?`upload`銆乣*-from-path`銆丼teamGridDB銆佺綉椤垫悳绱€侀€夋嫨銆佸垹闄ょ瓑閰嶅绔偣
- 閰嶇疆锛歚GET/PUT /api/games/{id}/config`銆乣GET/PUT /api/games/{id}/config/raw`
- 搴旂敤璁剧疆锛歚GET/PUT /api/settings`銆乣GET /api/settings/version`銆乣POST /api/settings/reset`銆乣POST /api/settings/export`銆乣POST /api/settings/import`銆乣POST /api/settings/import-from-path`銆乣POST /api/settings/open-data-folder`
- 鏂囦欢娴忚鍣細`GET /api/filesystem/drives`銆乣GET /api/filesystem/quick-access`銆乣POST /api/filesystem/list`銆乣POST /api/filesystem/read-text`
- AI 缈昏瘧锛歚POST /api/translate`銆乣GET /api/translate/stats`銆乣GET /api/translate/cache-stats`銆乣POST /api/translate/test`銆乣GET /api/translate/ping`
- AI 鎺у埗涓庢ā鍨嬶細`POST /api/ai/toggle`銆乣GET /api/ai/models`銆乣GET /api/ai/extraction/stats`
- 鏈湴 LLM锛歚GET/PUT /api/local-llm/settings`銆乣GET /api/local-llm/status`銆乣GET /api/local-llm/gpus`銆乣POST /api/local-llm/gpus/refresh`銆乣GET /api/local-llm/catalog`銆乣GET /api/local-llm/llama-status`銆乣POST /api/local-llm/test`銆乣POST /api/local-llm/start`銆乣POST /api/local-llm/stop`銆佷笅杞?鏆傚仠/鍙栨秷妯″瀷銆佷笅杞?鍙栨秷 llama 杩愯鏃?
- AI 绔偣銆佹湳璇€佹弿杩帮細`/api/games/{id}/ai-endpoint`銆乣/api/games/{id}/terms`銆乣/api/games/{id}/description`
- 鍏煎灞傦細`/api/games/{id}/glossary`銆乣/api/games/{id}/do-not-translate` 淇濈暀鍏煎鏃ц皟鐢紝浣嗗簳灞傜粺涓€璧?`TermService`
- 缈昏瘧璁板繂涓庡姩鎬佹ā寮忥細`/api/games/{id}/translation-memory`銆乣/api/games/{id}/dynamic-patterns`銆乣/api/games/{id}/term-candidates`
- 鑴氭湰鏍囩锛歚GET /api/script-tag-presets`銆乣GET/PUT /api/games/{id}/script-tags`
- 璧勬簮鎻愬彇涓庨缈昏瘧锛歚POST /api/games/{id}/extract-assets`銆乣GET/DELETE /api/games/{id}/extracted-texts`銆乣POST /api/games/{id}/pre-translate`銆乣POST /api/games/{id}/pre-translate/resume`銆乣GET /api/games/{id}/pre-translate/status`銆乣POST /api/games/{id}/pre-translate/cancel`銆乣GET/PUT /api/games/{id}/pre-translate/regex`
- `GET/PUT /api/games/{id}/pre-translate/regex` 褰撳墠鏄?legacy compatibility 绔偣锛屽彧璇诲啓 custom 姝ｅ垯鍖哄潡锛涘畬鏁村鍖哄潡缂栬緫缁熶竴璧?`translation-editor/regex`
- 缈昏瘧缂栬緫鍣細`GET/PUT /api/games/{id}/translation-editor?source={default|pretranslated}&lang={lang}`銆乣POST /api/games/{id}/translation-editor/import`銆乣GET /api/games/{id}/translation-editor/export`銆乣GET/PUT /api/games/{id}/translation-editor/regex?lang={lang}`銆乣POST /api/games/{id}/translation-editor/regex/import`銆乣GET /api/games/{id}/translation-editor/regex/export`
- 瀛椾綋鏇挎崲锛歚POST /api/games/{id}/font-replacement/scan`銆乣POST /api/games/{id}/font-replacement/replace`銆乣POST /api/games/{id}/font-replacement/restore`銆乣GET /api/games/{id}/font-replacement/status`銆乣POST /api/games/{id}/font-replacement/upload`銆乣POST /api/games/{id}/font-replacement/upload-from-path`銆乣POST /api/games/{id}/font-replacement/cancel`銆乣DELETE /api/games/{id}/font-replacement/custom-fonts/{sourceId}`
- 瀛椾綋鏇挎崲涓婁紶绔偣鐜板湪瑕佹眰鏄惧紡鍖哄垎 `kind={ttf|tmp}`锛涚姸鎬佺鐐逛細杩斿洖榛樿婧?鑷畾涔夋簮鍒楄〃涓庡凡浣跨敤婧愭憳瑕侊紱鏇挎崲璇锋眰涓殑 `fonts[]` 闇€瑕佹惡甯﹂€愬瓧浣?`sourceId`
- `POST /api/games/{id}/font-replacement/scan` 鏄瓧浣撳綋鍓嶈祫婧愮姸鎬佺殑鏉冨▉鏉ユ簮锛沗GET /api/games/{id}/font-replacement/status` 涓昏鍩轰簬 `manifest.json` 姹囨€绘浛鎹㈢姸鎬侊紝涓嶈繑鍥炲疄鏃堕噸鎵悗鐨?`ttfMode` / `fontDataSize`
- 瀛椾綋鐢熸垚锛氫笂浼犮€佺敓鎴愩€佺姸鎬併€佸彇娑堛€佷笅杞姐€佸巻鍙层€佸垹闄ゃ€佸畨瑁?TMP 瀛椾綋銆佸瓧绗﹂泦棰勮/涓婁紶銆佹姤鍛婃煡璇㈠潎鐢?`/api/font-generation/*` 鎻愪緵
- BepInEx 鏃ュ織涓庡仴搴凤細`/api/games/{id}/bepinex-log`銆乣/api/games/{id}/health-check`
- 鎻掍欢绠＄悊涓庢彃浠跺寘锛歚/api/games/{id}/plugins`銆乣/api/games/{id}/plugin-package/export`銆乣/api/games/{id}/plugin-package/import`
- 鏃ュ織涓庢洿鏂帮細`GET /api/logs`銆乣GET /api/logs/history`銆乣GET /api/logs/download`銆乣/api/update/*`
- 鎵€鏈?`multipart/form-data` 涓婁紶绔偣閮藉繀椤绘樉寮?`.DisableAntiforgery()`
- 鎵€鏈?`*-from-path` 绔偣閮藉繀椤讳笌瀵瑰簲 multipart 绔偣淇濇寔鐩稿悓涓氬姟鏍￠獙涓庡壇浣滅敤锛屼笉鍏佽鍙仛鈥滅畝鍖栫増瀹炵幇鈥?
- `POST /api/translate` 鐩存帴鏈嶅姟浜?`LLMTranslate.dll`锛屼笉鏄?`ApiResult<T>`锛屽墠绔嫢鐩存帴璋冪敤蹇呴』浣跨敤鍘熷 `fetch` 澶勭悊鍝嶅簲
- 甯歌 JSON 绔偣缁熶竴浣跨敤 `Results.Ok(ApiResult<T>.Ok(...))` 鎴?`Results.BadRequest(ApiResult.Fail(...))`锛屼笉瑕佺洿鎺ヨ繑鍥炶８瀵硅薄

## 20. 鍚屾鐐逛笌妯″瀷琛ュ厖

- `InstallStep`銆乣UpdateInfo`銆乣VersionInfo`銆乣DataPathInfo`銆乣BatchAddResult`銆乣UnityGameInfo`銆乣FileExplorer`銆乣FontReplacement`銆乣FontGeneration`銆乣PluginHealth`銆乣BepInExPlugin`銆乣LocalLlmSettings`銆乣BuiltInModelInfo`銆乣LlamaStatus` 绛夋ā鍨嬶紝鏂板瀛楁鏃堕兘蹇呴』鍚屾椂鍚屾 C# 妯″瀷銆乀S 绫诲瀷銆佺浉鍏?API銆佸搴斿墠绔〉闈?
- 瀛椾綋鏇挎崲閾捐矾鏀瑰姩鏃讹紝瑕佷竴璧锋牳瀵?`FontReplacementRequest.Fonts[].SourceId`銆乣ReplacementSource` / `ReplacementSourceSet`銆乣FontReplacementStatus.AvailableSources` / `UsedSources`銆乣ReplacedFontEntry.SourceId` / `SourceDisplayName`锛屽苟鍚屾 `FontReplacement.cs`銆乣src/api/types.ts`銆乣FontReplacementView.vue`銆乣FontReplacementEndpoints.cs`銆乣FontReplacementService.cs`
- 娑夊強 Legacy `Font` 鐨?TTF 鍒嗘瀽鎴栧啓鍥炴椂锛岃繕瑕佷竴璧锋牳瀵?`AnalyzeTtfFont`銆乣GetByteArrayLength`銆乣SetByteArrayContents`銆佸啓鍚庨噸璇婚獙璇佹棩蹇椼€乣GetStatusAsync` 鍜屽墠绔姸鎬佹枃妗堬紱`scan` 涓?`status` 鐨勮涔変笉瑕佹贩鐢?
- `SettingsView.vue` 鐨勯粯璁?`AppSettings`銆乣AiTranslationView.vue` 鐨?`DEFAULT_AI_TRANSLATION`銆佸悗绔?`AppSettings`/`AiTranslationSettings` 榛樿鍊煎繀椤讳繚鎸佷竴鑷?
- 鏁板€煎瀷璁剧疆鏂板瀛楁鏃讹紝瑕佸悓姝ュ悗绔殑 `Math.Clamp` 閫昏緫锛屽惁鍒欏墠绔笌鍚庣浼氬嚭鐜拌竟鐣屼笉涓€鑷?
- `TermEntry` 鐨?`Type`/`Category`/`Source`銆乣ScriptTagRule`/`ScriptTagConfig`銆乣TranslationStats`/`RecentTranslation`/`TranslationError`銆乣PreTranslationStatus`/`PreTranslationCacheStats` 閮藉睘浜庡鏄撴紡鍚屾鐨勯珮棰戞ā鍨?
- `PreTranslationStatus` 鐨?`CanResume` / `CheckpointUpdatedAt` / `ResumeBlockedReason` 涓?`POST /api/games/{id}/pre-translate/resume` 灞炰簬涓€缁勮仈鍔ㄧ偣锛涙敼鍔ㄦ椂瑕佸悓鏃舵牳瀵?`Models/AssetExtraction.cs`銆乣src/api/types.ts`銆乣src/api/games.ts`銆乣src/stores/assetExtraction.ts`銆乣AssetExtractionView.vue`锛屽苟纭杩愯涓缁?`CanResume=false`锛屽彇娑?澶辫触鍚庝細鍩轰簬 checkpoint 閲嶆柊瑙ｆ瀽鎭㈠璧勬牸
- `TranslationEditorData.Source` / `Language` / `AvailablePreTranslationLanguages`銆乣TranslationRegexEditorData`銆乣RegexTranslationRule`銆乣TranslationEditorSource` / `TranslationEditorTextSource` 灞炰簬涓€缁勮仈鍔ㄧ偣锛涙敼鍔ㄦ椂瑕佸悓鏃舵牳瀵?`TranslationEditorEndpoints.cs`銆乣TranslationEditorPathResolver.cs`銆乣src/api/types.ts`銆乣src/api/games.ts`銆乣TranslationEditorView.vue`銆乣RegexRuleEditor.vue`
- `PreTranslationRegexFormat` 鐨?`base` / `custom` / `dynamic` 鍒嗗尯銆乣AssetEndpoints.cs` 鐨勫吋瀹规帴鍙ｃ€乣PreTranslationService.cs` 鐨勬墭绠℃枃浠堕噸寤恒€乣AppDataPaths.PreTranslationRegexFile(...)` 鐨?legacy 闀滃儚灞炰簬鍙︿竴缁勮仈鍔ㄧ偣锛沗custom` 蹇呴』淇濈暀锛宍base` 涓?`dynamic` 鍙互閲嶅缓
- 鏂板姣忔父鎴忕洰褰曟椂锛岄櫎浜?`AppDataPaths.cs`锛岃繕瑕佸悓姝?`DELETE /api/games/{id}` 娓呯悊閫昏緫銆佺紦瀛橀┍閫愩€佽缃鍑烘帓闄ゅ垪琛ㄣ€佸繀瑕佹椂鐨勮缃鍏ラ噸寤洪€昏緫
- `RecordError`銆乣NormalizeForCache`銆乣ApplicationStopping` 鍥炶皟銆佹棩蹇楃骇鍒繃婊ゃ€丼ignalR 浜嬩欢鍚嶄笌闃舵鍚嶏紝閮藉睘浜庘€滄敼涓€澶勫繀椤诲叏閾捐矾鏍稿鈥濈殑鍚屾鐐?
- 缈昏瘧瑙ｆ瀽濂戠害銆佽繍琛屾椂鍗犱綅绗︿繚鎶や笌 `Persistable` 杩囨护灞炰簬鏂扮殑楂橀鍚屾鐐癸紱鍑℃槸鏂板缈昏瘧璋冪敤鏂规垨缂撳瓨鍐欏叆鐐癸紝閮借鏍稿鏄惁閿欒鎺ユ敹浜嗛潪缁撴瀯鍖栧洖閫€缁撴灉
- `TranslationOuterWrapperGuard` 灞炰簬缈昏瘧閾捐矾鏂扮殑鍏ㄥ眬瀹堝崼锛涘嚒鏄柊澧?TM 鍛戒腑澶嶇敤銆侀缈昏瘧缂撳瓨鍐欏叆銆佸姩鎬佹鍒欑敓鎴愭垨鍏朵粬鎸佷箙鍖栧嚭鍙ｏ紝閮借鏍稿鏄惁鍚屾鍋氫簡鈥滃師鏂囨棤澶栧眰鍖呰９鏃剁姝㈣瘧鏂囨柊澧炴暣鍙ュ灞傚寘瑁光€濈殑褰掍竴鍖?鎷︽埅
- `build.ps1`銆乣.github/workflows/build.yml` 涓?`.github/workflows/dep-check.yml` 閮藉寘鍚増鏈墠缂€/鍙戠増鍋囪锛涙祦绋嬨€佺増鏈彿銆佽祫婧愭潵婧愩€佹瀯寤?edition 鎴栬嚜鍔ㄤ緷璧栨瀯寤虹増鏈瓥鐣ュ彂鐢熷彉鍖栨椂蹇呴』涓€璧锋牳瀵?- 鑻ュ彉鏇撮椤靛彲鐢ㄦ€с€侀潤鎬佽祫婧愮洰褰曘€佸惎鍔ㄧ鍙ｆ垨鍚姩鏂瑰紡锛岄渶瑕佸垎鍒牳瀵?`build.ps1` 涓?`.github/workflows/build.yml` 鐨勫彂甯冩祦绋嬶紝浣嗗綋鍓嶄笉鍐嶇淮鎶?`Test-FrontendSmoke` 鍥炲綊瀹堝崼
- Git 鎻愪氦鏍囬瑙勮寖銆乣.github/workflows/build.yml` 涓?`### Changelog` 鐨勭敓鎴愰€昏緫锛屼互鍙?`UnityLocalizationToolkit-Vue/src/views/SettingsView.vue` 鐨?`typeLabels` / 姝ｅ垯瑙ｆ瀽灞炰簬鑱斿姩鐐癸紱鑻ヨ皟鏁存彁浜ゆ牸寮忋€佹洿鏂板唴瀹瑰睍绀烘牱寮忔垨 changelog 鐢熸垚鏂瑰紡锛屽繀椤诲悓鏃舵牳瀵硅繖涓夊锛屼笖娉ㄦ剰 `--no-merges` 浼氳 merge commit 涓嶈繘鍏ュ伐鍏风鏇存柊鍒楄〃
- `llama.cpp` 鐗堟湰鏇存柊闇€瑕佸悓鏃跺悓姝?`build.ps1`銆乣build.yml`銆乣LocalLlmService.LlamaVersion`銆佷笅杞借祫婧愬懡鍚嶆ā寮忋€丷EADME/鏈墜鍐岃鏄?

## 21. 鍚庣涓撻」琛ュ厖

### 21.1 杩愯涓庢灦鏋勭粏鑺?

- 椤圭洰褰撳墠涓嶅仛鍚戝悗鍏煎杩佺Щ鎴栨棫鏍煎紡鑷姩杞崲锛涢粯璁ゆ寜鈥滈绋冲畾闃舵銆佸彲浠ユ竻鏅版柇浠ｂ€濈殑鎬濊矾缁存姢
- `Program.cs` 蹇呴』鍦?DI 涔嬪墠璇诲彇 `AppData:Root` 骞跺洖鍐欏埌 `builder.Configuration["AppData:Root"]`锛屽惁鍒?`AppDataPaths` 鐪嬪埌鐨勬槸鏃у€?
- 闈欐€佽祫婧愮紦瀛樼瓥鐣ュ繀椤诲尯鍒?`/assets/*` 涓?`index.html`/`favicon.ico`锛汼PA fallback 涔熷繀椤绘樉寮忚缃?`no-cache`
- 鍛藉悕 `HttpClient` 宸茬粡鎵胯浇瓒呮椂銆佽繛鎺ユ暟銆乁A 鍜岀敤閫斿亣璁撅紱鏂板澶栭儴缃戠粶璋冪敤浼樺厛澶嶇敤鍛藉悕瀹㈡埛绔紝涓嶈闅忓 `new HttpClient()`
- `Updater/` 鏄?AOT 宸ョ▼锛屼笉鍏佽渚濊禆 WinForms銆佸弽灏勫紡 JSON 搴忓垪鍖栨垨 `Microsoft.Win32.Registry` 鐨勫父瑙勬墭绠″皝瑁咃紱娑夊強娉ㄥ唽琛ㄦ椂搴斾娇鐢?P/Invoke

### 21.2 TranslatorEndpoint 涓庨厤缃摼璺?

- `TranslatorEndpoint` 鐩爣涓?`net35`锛屽苟渚濊禆 `build.ps1` 浠?XUnity 鍖呴噷鎻愬彇 `libs/` 寮曠敤 DLL
- `[LLMTranslate]` INI 鍖烘鐨?`ToolkitUrl`銆乣GameId` 绛夊€肩敱 `POST /api/games/{id}/ai-endpoint`銆乣InstallOrchestrator` 鍜?DLL 鍒濆鍖栧叡鍚岀淮鎶わ紝淇敼鍏剁害瀹氬繀椤讳笁澶勫悓鏀?
- 涓嶈浠庨浂閲嶅啓 `AutoTranslatorConfig.ini`锛涚粺涓€閫氳繃 `ConfigurationService.PatchAsync` 鍋氳ˉ涓佸紡淇敼
- `PatchAsync` 涓?`null` 琛ㄧず璺宠繃瀛楁锛岀┖瀛楃涓茶〃绀烘竻绌哄瓧娈碉紝杩欎釜璇箟涓嶈兘鏀?
- 榛樿鏈€浼橀厤缃細鍐欏叆 `Language=zh`銆乣OverrideFont=Microsoft YaHei`銆乣Endpoint=LLMTranslate` 绛夊€硷紱鑻ヨ皟鏁撮粯璁ら厤缃紝蹇呴』鍚屾椂鏍稿瀹夎閾捐矾鍜屾枃妗ｈ鏄?
- `LLMTranslate.dll` 鐨勬棩蹇楀垎涓哄缁堣緭鍑虹殑 `Log()` 鍜屼粎鍦?`DebugMode` 涓嬭緭鍑虹殑 `DebugLog()`锛屼笉瑕佹妸鍏抽敭鍒濆鍖栧拰閿欒淇℃伅鏀捐繘 `DebugLog()`

### 21.3 AI 缈昏瘧銆佹湳璇笌缂撳瓨

- 鍦ㄧ嚎缈昏瘧涓婚摼璺粛鐒舵槸 Phase 0 TM 鏌ユ壘銆丳hase 1 鑷劧缈昏瘧銆丳hase 2 鏈/DNT 鍗犱綅绗︽浛鎹€丳hase 3 寮哄埗淇
- `TranslationMemoryService` 鐨勫啓鍏ュ厛钀藉唴瀛橈紝鎸佷箙鍖栬蛋闃叉姈锛涚儹璺緞涓婁笉瑕佸紩鍏ラ澶栫鐩?I/O
- `GlossaryExtractionService` 涓?`TermExtractionService` 鍏变韩瑙ｆ瀽涓庡垎绫婚€昏緫锛屼絾鏁呮剰淇濇寔涓や釜鐙珛鏈嶅姟锛涗笉瑕佸洜涓衡€滅湅璧锋潵閲嶅鈥濊€屽己琛屽悎骞?
- 鎵€鏈夌炕璇戣矾寰勯兘蹇呴』鍦ㄦ弧瓒虫潯浠舵椂璋冪敤 `BufferTranslation` + `TryTriggerExtraction`锛屽惁鍒欐湳璇彁鍙栫粺璁′細澶辩湡
- `ScriptTagService.NormalizeForCache` 鏄紦瀛樺綊涓€鍖栫殑鍞竴鍏ュ彛锛涙秹鍙婇缈昏瘧缂撳瓨銆佸姩鎬佹ā寮忔垨鑴氭湰鏍囩鐨勫彉鏇撮兘瑕佹牳瀵硅皟鐢ㄧ偣
- `TranslationStats.Queued` 鏄帹瀵煎€硷紝涓嶇瓑浜庡唴閮?`_queued`锛汿M 鍛戒腑鍜屽け璐ユ枃鏈粺璁′篃鏈夊悇鑷嫭绔嬪惈涔夛紝涓嶈兘娣风敤
- `RecentTranslation.EndpointName` 鍦?TM 鍛戒腑鍦烘櫙涓嬮渶瑕佹樉寮忓啓鎴愨€滅炕璇戣蹇嗏€濓紝鍚﹀垯鍓嶇鏈€杩戠炕璇戝垪琛ㄤ細鍑虹幇绌虹櫧绔偣鍚?
- `PreTranslationRegexFormat` 璐熻矗鎵樼 `_PreTranslated_Regex.txt` 鐨?`base` / `custom` / `dynamic` 鍖哄潡锛沗PreTranslationService` 閲嶅缓鎵樼鏂囦欢鏃跺繀椤讳繚鐣?`custom`锛屽苟鍚屾鍥炲啓 `AppDataPaths.PreTranslationRegexFile(gameId)` 鍏煎闀滃儚
- `TranslationEditorPathResolver` 鏄?`translation-editor`銆乣translation-editor/regex` 涓?legacy `/pre-translate/regex` 鍏辩敤鐨勫敮涓€璺緞瑙ｆ瀽鍏ュ彛锛涜瑷€閫夋嫨銆佺洰褰曟壂鎻忓拰璺緞闃茬┛瓒婇兘涓嶈鏁ｈ惤閲嶅啓

### 21.4 鎬ц兘銆佸苟鍙戜笌 SignalR

- 鐑矾寰勯伩鍏?`GameLibraryService.GetByIdAsync` 绛夌鐩樻垨澶у璞¤闂紝浼樺厛浣跨敤缂撳瓨
- `SemaphoreSlim` 鐩稿叧閫昏緫瑕佸尯鍒嗏€滄槸鍚︽垚鍔熻幏鍙栦俊鍙烽噺鈥濆啀閲婃斁锛岄伩鍏嶈秴鏃跺悗璇?`Release()`
- `BroadcastStats` 鐨勮妭娴佷笌 `force: true` 璇箟鏄墠绔疄鏃舵祦姘寸嚎鏄剧ず鐨勫墠鎻愶紝涓嶈兘闅忔剰鍒犲噺
- KeepAlive 鎴栬Е鍙戝嵆蹇樼殑鍚庡彴浠诲姟锛屽鏋滀緷璧?SignalR 缁撴潫浜嬩欢鏉ュ浣嶅墠绔姸鎬侊紝閭ｄ箞澶辫触銆佸彇娑堣矾寰勪篃蹇呴』骞挎挱瀹屾垚/閿欒浜嬩欢
- `FileLoggerProvider` 鐨勫唴瀛?ring buffer 鍙敤浜庤繍琛屼腑鏃ュ織椤靛睍绀猴紱`GET /api/logs/download` 蹇呴』瀵煎嚭褰撳墠 session 鐨勭鐩樻棩蹇楀揩鐓э紝涓嶈兘閫€鍖栨垚鍙鍑?ring buffer 鎴柇缁撴灉
- 鍚戝墠绔繑鍥炵殑閿欒娑堟伅瑕侀伩鍏嶆硠闇插唴閮ㄧ粷瀵硅矾寰勩€佸紓甯稿爢鏍堟垨鏁忔劅閰嶇疆锛涜缁嗕俊鎭彧鍐欐湇鍔″櫒鏃ュ織

### 21.5 璧勬簮銆佸瓧浣撱€乄ebView2 涓庡懆杈规湇鍔?

- `AssetExtractionService` 浣跨敤 AssetsTools.NET锛屾暟缁勫瓧娈佃闂粺涓€閬靛惊 `field -> "Array" -> elements` 妯″紡
- TTF 瀛椾綋鏇挎崲鏀寔 `dynamicEmbedded` 鐨?Unity Legacy `Font`锛屼篃鏀寔灏?`osFallback` / 鍚嶇О鏄犲皠鍔ㄦ€佸瓧浣撳師浣嶈浆鎴愬唴宓屽瓧浣擄紱`staticAtlas` 涓?`unknown` 浠嶇粺涓€鎵弿浣嗘嫆缁濇浛鎹?
- Legacy `Font.m_FontData` 鐨勫崟瀛楄妭鍏冪礌鏃㈠彲鑳芥槸 `UInt8` 涔熷彲鑳芥槸 `Int8/char`锛涘啓鍥炴暟缁勯」鏃惰鎸?`AssetValueType` 閫夋嫨 `AsByte` 鎴?`AsSByte`锛屽惁鍒欎細鍦?`SetNewData` 鏃惰Е鍙戞湁绗﹀彿婧㈠嚭
- `TmpFontGeneratorService` 鍩轰簬 FreeTypeSharp 涓?Felzenszwalb EDT 鐢熸垚 SDF锛涚敓鎴愬嚭鐨?atlas銆乸adding銆乬radient scale銆乺ender mode 涔嬮棿鏈夊己鑰﹀悎锛屼笉瑕佸眬閮ㄦ敼涓€涓瓧娈?- `WebImageSearchService` 閫氳繃缃戦〉鎶撳彇鎻愪緵鍥剧墖鎼滅储锛涙墍鏈?URL 鍦ㄧ湡姝ｈ姹備箣鍓嶅繀椤诲厛璧?SSRF 鏍￠獙锛屼繚瀛樺墠杩樿鏍￠獙鍐呭绫诲瀷
- 鍥炬爣銆佸皝闈€佽儗鏅浘杩欑被澶栭摼涓嬭浇鐜板湪缁熶竴閫氳繃绂佺敤鑷姩閲嶅畾鍚戠殑 `HttpClient` + `PathSecurity.SendWithValidatedRedirectsAsync(...)` 閫愯烦鏍￠獙锛涗笉瑕佸啀鐩存帴 `GetAsync(url)` 鍚庝俊浠绘鏋惰嚜鍔ㄨ窡闅?30x
- 澶栭摼鍥剧墖涓嬭浇蹇呴』棰濆闄愬埗鍝嶅簲浣撶Н锛堝綋鍓嶄笂闄?10 MB锛夛紝閬垮厤缃戦〉鎼滅储缁撴灉鎴栫涓夋柟 CDN 鎶婅秴澶ф枃浠剁洿鎺ヨ杩涘唴瀛?- `WebViewWindow`銆乣SystemTrayService`銆乄ebView2 棰勭儹銆佸姞杞?overlay銆佸揩閫熼殣钘?UI銆佸叧闂秴鏃剁瓑鏈哄埗閮藉睘浜庢闈㈠涓诲眰涓嶅彉閲忥紝鏀瑰姩鍓嶈瀹屾暣鍥炵湅鍘嗗彶瀹炵幇
- `WebViewWindow.InitializeAsync()` 鐜板湪浼氬厛鎺㈡祴 `GET /`锛屽苟涓斿彧鍦ㄩ椤甸娆?`NavigationCompleted` 鎴愬姛鍚庢墠闅愯棌鍘熺敓 loading overlay锛涢椤垫帰娴嬫垨棣栧睆瀵艰埅澶辫触鏃跺繀椤讳繚鐣?overlay 骞剁粰鍑烘槑纭敊璇紝涓嶈兘鐩存帴鏆撮湶绯荤粺 404 椤甸潰
- `Updater/Program.cs` 鍦ㄦ垚鍔熼噸鍚拰鍥炴粴閲嶅惎涓ゆ潯璺緞閲岄兘蹇呴』淇濇寔 `WorkingDirectory = appDir`锛屽惁鍒欏彲鑳藉嚭鐜?API 姝ｅ父浣嗛椤靛洜 `wwwroot` 瑙ｆ瀽鍒伴敊璇洰褰曡€?404
- `QuickAccessHelper` 浣跨敤 Shell COM 涓旇姹?STA 绾跨▼锛涚浉鍏?COM 瀵硅薄蹇呴』閫愮骇 `Marshal.ReleaseComObject`
- `BepInExLogService`銆乣PluginHealthCheckService`銆乣BepInExPluginService` 閮戒緷璧栨枃浠跺叡浜鎴栬鍔ㄥ垎鏋愭ā寮忥紝涓嶈鍦ㄨ繖浜涜矾寰勯噷寮曞叆鈥滃姞杞界敤鎴?DLL 鍒板綋鍓嶈繘绋嬧€濊繖绫婚珮椋庨櫓鎿嶄綔

### 21.6 瀹夊叏绾﹀畾琛ュ厖

- `gameId`銆佽瑷€浠ｇ爜銆佺敤鎴蜂笂浼犳枃浠跺悕銆佸彲鎵ц鏂囦欢鍚嶃€佸鍏?ZIP 鍐呴儴璺緞閮藉繀椤诲仛璺緞绌胯秺鍜屾牸寮忔牎楠?- `PathSecurity.SafeJoin` 涓?`PathSecurity.ValidateExternalUrl` 鏄矾寰勫畨鍏ㄥ拰 SSRF 闃叉姢鐨勭粺涓€鍏ュ彛锛涗笉瑕佽嚜琛屽鍒朵竴濂楄繎浼煎疄鐜?- 澶栭儴 URL 鐨?SSRF 鏍￠獙涓嶈兘鍙湅鍘熷涓绘満鍚嶏紱鑻ヨ緭鍏ユ槸鍩熷悕锛岃繕蹇呴』妫€鏌?DNS 瑙ｆ瀽鍚庣殑 IP 鏄惁钀藉埌鍥炵幆銆侀摼璺湰鍦版垨绉佺綉缃戞
- 浠讳綍鍏佽閲嶅畾鍚戠殑澶栭摼涓嬭浇閮藉繀椤诲姣忎竴璺崇洰鏍囬噸澶嶅仛鍚屾牱鐨?SSRF 鏍￠獙锛涢璺冲畨鍏ㄤ笉浠ｈ〃鍚庣画璺宠浆瀹夊叏
- 鐢ㄦ埛鎻愪緵鎴栨湰鍦伴€夋嫨鐨?ZIP 瀵煎叆锛堣缃鍏ャ€佹彃浠?ZIP銆佹眽鍖栧寘瀵煎叆绛夛級蹇呴』鍚屾椂闄愬埗鍘嬬缉鍖呭師濮嬪ぇ灏忋€佸崟鏂囦欢瑙ｅ帇澶у皬涓庢€昏В鍘嬪ぇ灏忥紝骞剁粺涓€璧?`PathSecurity.PrepareZipExtractionPath(...)` / `ExtractZipEntryAsync(...)`
- `POST /api/settings/reset`銆乣POST /api/settings/import` 浼氬紩鍙戣法鏈嶅姟缂撳瓨澶辨晥锛岀浉鍏虫湇鍔℃柊澧炵紦瀛樺悗蹇呴』骞跺叆杩欎袱鏉¤矾寰?- JSON 鏁版嵁鏂囦欢缁熶竴璧?`FileHelper.WriteJsonAtomicAsync`锛涢潪 JSON 鍏抽敭鏂囦欢涔熷簲閲囩敤 `.tmp + move` 鐨勫師瀛愯惤鐩樻ā寮?
## 22. 鍓嶇涓撻」琛ュ厖

### 22.1 鍩虹绾﹀畾

- 浣跨敤 Vue 3 Composition API 鍜?`<script setup lang="ts">`
- API 璋冪敤缁熶竴浠?`@/api/client` 鎴?`@/api/games` 杩涘叆锛屼笉瑕佺洿鎺ユ暎钀?axios 瀹炵幇
- 鐢熶骇璺緞涓嶈鐣欎笅 `console.*`
- 鍏变韩宸ュ叿浼樺厛鏀惧湪 `src/composables/`銆乣src/utils/`銆乣src/constants/`锛屼笉瑕佸湪椤甸潰鍐呴噸澶嶅畾涔変竴浠藉嚑涔庣浉鍚岀殑 helper
- `gamesStore.launchGame(id)` 鏄墍鏈夊惎鍔ㄦ父鎴忓叆鍙ｇ殑鍞竴璋冪敤鐐癸紝涓嶈缁曞紑瀹冪洿璋?API

### 22.2 璁捐绯荤粺涓庢牱寮?

- 椤圭洰浣跨敤鑷墭绠″瓧浣擄紝绂佹寮曞叆 Google Fonts CDN
- 涓婚妯″紡鐢?`useThemeStore` 鎺у埗锛宍resolvedTheme` 鎵嶆槸娓叉煋渚濇嵁锛沗mode` 鍙兘鏄?`system`
- 鍏变韩甯冨眬鍜屽崱鐗囩被鍚嶄互 `main.css` 涓哄噯锛屼緥濡?`.page-title`銆乣.section-card`銆乣.section-header`銆乣.header-actions`銆乣.table-container`
- 椤跺眰椤甸潰鍜屾父鎴忓瓙椤甸潰鐨勬爣棰樸€佸洖閫€鎸夐挳銆乻ection card 鏍峰紡鏈夋槑纭害瀹氾紝涓嶈鍦ㄦ瘡涓〉闈㈤噸鏂板彂鏄庝竴濂?
- 杩欐槸妗岄潰搴旂敤鑰屼笉鏄叏瀹界綉椤碉紝榛樿绐楀彛瀹藉害鎵ｆ帀渚ц竟鏍忓拰鍗＄墖鍐呰竟璺濆悗锛岀粡甯稿彧鍓┾€滀腑绛夊唴瀹瑰搴︹€濓紱鍙屽垪鍗＄墖銆佽〃鍗曞拰璁剧疆闈㈡澘涓嶈鍙緷璧?`768px` 杩欑被绉诲姩绔柇鐐癸紝浼樺厛浣跨敤 `repeat(auto-fit, minmax(...))`銆乣minmax(0, 1fr)` 鎴栬ˉ鍏呬腑闂存柇鐐癸紝纭繚榛樿绐楀彛澶у皬涓嬩笉瑁佸垏
- `section-card`銆佹姌鍙犲尯姝ｆ枃鍜屽眬閮?grid/flex 鍒楀湪闇€瑕佹敹缂╂椂閫氬父閮借鏄惧紡 `min-width: 0`锛沗src/assets/main.css` 宸蹭负鍏变韩鍗＄墖瀹瑰櫒琛ヤ簡杩欏眰绾︽潫锛屾柊澧炵被浼?`ConfigPanel.vue`銆乣FontGeneratorView.vue` 鐨勫弻鍒楀竷灞€鏃惰涓€璧锋鏌?Naive UI 杈撳叆鎺т欢鍜屽灞傚鍣ㄦ槸鍚︾湡姝ｅ厑璁告敹缂?
- 閬垮厤鏃犲繀瑕佺殑鍐呰仈鏍峰紡锛涢櫎涓€娆℃€х殑 `animation-delay` 绛夋瀬灏戞暟鎯呭喌澶栵紝缁熶竴钀藉埌浣滅敤鍩?CSS
- 棰滆壊鍜岃竟妗嗕竴寰嬩娇鐢ㄨ璁＄郴缁熷彉閲忥紝涓嶈鍙戞槑涓嶅瓨鍦ㄧ殑 CSS 鍙橀噺鍚?

### 22.3 KeepAlive銆丼ignalR 涓庤祫婧愮敓鍛藉懆鏈?

- 椤跺眰椤甸潰閫氳繃 `KeepAlive` 缂撳瓨锛屽洜姝ゆ秹鍙?SignalR銆亀indow 鐩戝惉鍣ㄣ€佸畾鏃跺櫒鐨勯〉闈笌瀛愮粍浠堕兘瑕佸悓鏃跺鐞?`onActivated`銆乣onDeactivated`銆乣onBeforeUnmount`
- `HubConnection` 涓嶈鍦ㄦā鍧楅《灞傚垱寤猴紝缁熶竴鍦ㄧ敓鍛藉懆鏈熼挬瀛愬唴鍒涘缓鍜岄攢姣?
- `onBeforeUnmount` 鏄粯璁ゆ竻鐞嗛挬瀛愶紝涓嶈鏀规垚 `onUnmounted`
- 鑷姩淇濆瓨椤甸潰鍦ㄥ姞杞藉閮ㄦ暟鎹椂缁熶竴閲囩敤 `disable -> load/assign -> nextTick -> enable`
- `AiTranslationView`銆乣SettingsView` 杩欑被鍏变韩 `AppSettings` 鐨?KeepAlive 椤甸潰锛岄噸鏂版縺娲绘椂蹇呴』閲嶆柊浠庡悗绔姞杞斤紝閬垮厤鏃у壇鏈鐩栨柊鏀瑰姩
- `AssetExtractionView` 鐨勯缈昏瘧鎸夐挳鏄剧ず渚濊禆 `src/stores/assetExtraction.ts` 涓殑鐘舵€佸厹搴曪細鏀跺埌甯?`CheckpointUpdatedAt` 鐨勭粓鎬?`preTranslationUpdate`锛屾垨鈥滃紑濮嬮缈昏瘧鈥濆洜鏃?checkpoint 琚嫆缁濇椂锛岄兘蹇呴』涓诲姩鍒锋柊 `GET /api/games/{id}/pre-translate/status`锛屼笉瑕佸彧渚濊禆涓€娆?SignalR 缁堟€佸寘
- `TranslationEditorView` 閫氳繃 `route.query.source` / `route.query.lang` 鍒囨崲鏅€氳瘧鏂囥€侀缈昏瘧鏂囨湰涓庨缈昏瘧姝ｅ垯锛涗粠 `AssetExtractionView` 璺宠浆杩涘叆棰勭炕璇戠紪杈戞椂蹇呴』甯︿笂杩欎簺 query锛屼笉瑕佸啀娲剧敓涓€濂楅〉闈㈠唴鏉ユ簮鐘舵€?

### 22.4 Naive UI 涓庡父瑙佸疄鐜伴櫡闃?

- `NInputNumber` 鐨?`@update:value` 浼氬彂鍑?`number | null`锛屼繚瀛樻椂瑕佹樉寮忓鐞?`null`
- `NDialogOptions.onPositiveClick` 杩斿洖 Promise 浼氶樆濉炲璇濇鍏抽棴锛涜€楁椂鎿嶄綔閫氬父鐢?fire-and-forget 鏇村悎閫?
- `NDataTable` 鐨勬帓搴忋€佽櫄鎷熸粴鍔ㄣ€乣row-key` 鍞竴鎬с€佸脊鎬у垪 `minWidth` 閮芥湁鍘嗗彶鍧戯紝琛ㄦ牸鍙樻洿鍓嶈鏍稿鐜版湁妯″紡
- `NColorPicker` 浣跨敤鑷畾涔夎Е鍙戝櫒鏃跺簲璧?`#trigger` 妲斤紝骞堕攣瀹?`hex` 妯″紡
- `NUpload`銆乣NEllipsis` 鍦?flex 瀹瑰櫒閲屾湁棰濆鍖呰９灞傛椂锛岄渶瑕佺敤 `:deep()` 鎴栧灞傚鍣ㄤ慨姝ｅ竷灞€
- `NTabs type="segment"`銆佹姌鍙犻潰鏉裤€佹寜閽笎鍙樿竟妗嗙瓑瑙嗚妯″紡宸叉湁鍘嗗彶瀹炵幇锛屾敼鍔ㄥ墠鍏堝鐢ㄧ幇鏈夋牱寮忕粨鏋?

### 22.5 椤甸潰缁勭粐涓庝氦浜掓ā寮?

- 椤跺眰椤甸潰鍔犺矾鐢便€佸鑸€佺紦瀛樺悕锛涙父鎴忓瓙椤甸潰鍔?`/games/:id/...` 璺敱鍜?`GameDetailView` 鍏ュ彛鎸夐挳锛屼笉瑕佸杩涗富瀵艰埅
- `TermEditorView` 鏄粺涓€鏈缂栬緫椤碉紝鏇夸唬鏃╂湡鐙珛 glossary/do-not-translate 椤甸潰锛涚浉鍏虫柊鑳藉姏浼樺厛鎺ュ埌杩欓噷
- 鏂囦欢娴忚鍣ㄧ敱 `FileExplorerModal.vue` 鍏ㄥ眬鎸傝浇涓€娆★紝`useFileExplorer()` 閫氳繃 Promise 杩斿洖閫変腑鐨勬湇鍔″櫒璺緞
- 鑳屾櫙鍥俱€佸皝闈㈠浘銆佸浘鏍囥€佺綉椤靛浘鐗囨悳绱€佹父鎴忚鎯?hero 瑙嗚銆佽宸粴鍔ㄣ€佺紦瀛樺け鏁堟椂闂存埑锛岄兘鏈夌幇鎴愭ā寮忥紝涓嶈灞€閮ㄩ噸鍐?
- 娑夊強澶嶆潅澶氭楠や氦浜掓椂锛屼紭鍏堟娊鎴?composable锛屼緥濡?`useAddGameFlow`銆乣useAutoSave`銆乣useWindowControls`

## 23. 鏋勫缓銆佸彂甯冦€丆I/CD 琛ュ厖

- `dotnet build` 浼氳嚜鍔ㄨЕ鍙戝墠绔瀯寤猴紱浠呮瀯寤哄悗绔椂鐢?`-p:SkipFrontendBuild=true`
- `build.ps1` 璐熻矗鏈湴瀹屾暣鏋勫缓锛屼絾涓嶈礋璐ｇ敓鎴愭墍鏈?CI 浜х墿锛汣I 閫昏緫鍦?workflow 鍐呯嫭绔嬪疄鐜?
- 鐗堟湰鍙峰簲閫氳繃 `InformationalVersion` 浼犻€掞紝涓嶈婊ョ敤 `Version` 瀵艰嚧 `AssemblyVersion` 婧㈠嚭闂
- 褰撳墠鍙戝竷鏄鏂囦欢妯″紡锛屼笉鍐嶄娇鐢ㄥ崟鏂囦欢鍙戝竷锛涚浉鍏?`ExcludeFromSingleFile` 鍘嗗彶閫昏緫宸插け鏁?
- `SatelliteResourceLanguages=en`銆乄iX 鐨?`obj` 娓呯悊銆丳owerShell ZIP銆佺粍浠?ZIP銆乵anifest 鍛藉悕銆乪dition 鏋勫缓鍙傛暟绛夐兘宸叉垚涓烘棦瀹氱害鏉?
- WiX/MSI 缁嗚妭鍖呮嫭锛氭瘡鐢ㄦ埛瀹夎銆乣MajorUpgrade` 璋冨害銆佸彲閫夊垹闄?`%AppData%\UnityLocalizationToolkit`銆佹敞鍐岃〃鍚屾銆佽鍙瘉鏂囨湰涓€鑷存€с€佷腑鏂囧瓧绗︿覆鏈湴鍖栨柟寮忋€乣SuppressValidation=true`
- CI 鍖呭惈鍙鐢ㄧ殑 `build.yml`銆佸彂甯?`release.yml`銆佷緷璧栧贰妫€ `dep-check.yml`
- GitHub Actions 涓殑 AOT 鍙戝竷銆乣$GITHUB_OUTPUT` 澶氳鍐欐硶銆乣gh release create --notes-file`銆乨etached HEAD 鍦烘櫙涓嬪洖鍐?`main` 绛夐棶棰橀兘灞炰簬鏃㈡湁鍧戜綅
- `dep-check.yml` 鍙窡韪?BepInEx 涓?XUnity锛沗llama.cpp` 鐗堟湰鐩墠浠嶇劧鍥哄畾浜哄伐缁存姢

## 24. 鏃ф枃妗ｇ姸鎬?

- 鍘嗗彶 `CLAUDE.md` 宸插叏閮ㄥ垹闄?
- 鍘熷厛涓変唤 `CLAUDE.md` 鐨勬湁鏁堝唴瀹瑰凡骞跺叆鏈?`AGENTS.md`
- 鍚庣画鑻ユ湁浜洪噸鏂版坊鍔?`CLAUDE.md`锛屽簲榛樿瑙嗕负閲嶅鏂囨。骞跺洖鏀惰嚦 `AGENTS.md`

## 25. 鎻掍欢鍋ュ悍鐘舵€佽ˉ鍏?

- `PluginHealthCheckService` 鐜板凡鏀逛负 settings-aware 鐨勫紓姝ユ鏌ユ祦绋嬶細琚姩妫€鏌?`GET /api/games/{id}/health-check` 涓庝富鍔ㄩ獙璇?`POST /api/games/{id}/health-check/verify` 閮界粺涓€璧?`CheckAsync(...)`
- 鎻掍欢鍋ュ悍鐘舵€佷笉鍐嶅彧闈?BepInEx 鏃ュ織鐚滄祴鍘熷洜锛涙鏌ラ『搴忚皟鏁翠负锛氭枃浠跺畬鏁存€?-> 宸ュ叿绠?AI 鐘舵€?-> 鏃ュ織褰掔被 -> 楠岃瘉鍚庣殑宸ュ叿绠辫繛閫氭€?
- 褰撳伐鍏风渚у瓨鍦ㄥ彲鏄庣‘璇嗗埆鐨勯棶棰樻椂锛屽悗绔細鏂板 `toolboxAiState` 鍋ュ悍椤癸紝鍓嶇灞曠ず鍚嶇О涓衡€滃伐鍏风 AI 缈昏瘧鈥濓紝鐘舵€佸浐瀹氫负 `Warning`
- `toolboxAiState` 褰撳墠瑕嗙洊涓夌被鍦烘櫙锛?
  - `AiTranslation.Enabled == false`
  - 褰撳墠娌℃湁浠讳綍鍙敤绔偣锛屽垽瀹氳鍒欎笌 `LlmTranslationService` 淇濇寔涓€鑷达細`Enabled && ApiKey 闈炵┖`
  - 褰撳墠 `ActiveMode == "local"` 涓?`LocalLlmService.IsRunning == false`
- 瀵逛簬 `toolboxAiState`锛岃鎯呮枃妗堝繀椤荤洿鎺ユ寚鍑哄伐鍏风渚ч棶棰橈紝涓嶅厑璁稿啀浣跨敤鈥滃彲鑳戒笉瀹屽叏鍏煎 XUnity鈥濊繖绫诲吋瀹规€ф弿杩?
- 鏃ュ織閿欒褰掔被宸叉媶鍒嗕负涓ゅ眰锛?
  - 鐪熸鐨?XUnity 鍏煎鎬?Hook 寮傚父锛氬彧鍦ㄦ病鏈夋槑纭伐鍏风 AI 闃诲璇佹嵁鏃舵墠缁欏嚭鈥滃綋鍓嶆父鎴忕増鏈彲鑳戒笉瀹屽叏鍏煎 XUnity鈥?
  - 娉涘寲鐨勭炕璇戝け璐ワ細濡?`Failed: 'Continue'`銆乣Failed: 'Credits'`銆乣Cannot translate`銆乣AutoTranslator failed`锛屽湪瀛樺湪宸ュ叿绠?AI 闂鏃跺繀椤诲綊鍥犲埌宸ュ叿绠变晶锛屼笉寰楄鏍囦负 XUnity 鍏煎鎬ч棶棰?
- `PluginHealthReport`銆乣HealthCheckItem`銆乣HealthCheckDetail` 鐨?JSON 缁撴瀯鏈鏈墿灞曪紱鍏煎鎬ц姹傛槸鍙柊澧?`checks[].id = toolboxAiState`锛屼笉瑕佸啀棰濆娣诲姞鏂板瓧娈?
- `PluginHealthCard.vue` 鐩墠瀵瑰紓甯搁」浣跨敤鍥哄畾鎺掑簭锛?
  - `toolboxAiState` 鏈€鍏堟樉绀?
  - `logErrors` 娆′箣
  - 鍏朵粬寮傚父椤逛繚鎸佸師濮嬮『搴?
- 鎻掍欢鍋ュ悍鐘舵€佸睘浜庢枃浠跺叡浜 + 琚姩鍒嗘瀽璺緞锛涘厑璁歌鍙?`settings.json`銆佹湰鍦?LLM 杩愯鐘舵€佸拰 `BepInEx/LogOutput.log`锛屼絾涓嶈寮曞叆浠讳綍浼氬姞杞界敤鎴锋彃浠?DLL銆佷慨鏀规父鎴忔枃浠躲€佹垨涓哄垎鏋愯€岄噸鍐欓厤缃枃浠剁殑瀹炵幇

## 26. v5.0 手动翻译工作台

- `UnityLocalizationToolkit-WebUI v5.0` 在保留原 `XUnity` 工作流不变的前提下，新增并行的“手动翻译”工作台；界面入口位于游戏详情页的 `XUnity / 手动翻译` 切换和路由 `src/views/ManualTranslationWorkspaceView.vue`
- 顶层导航中的原 `AI 翻译` 现已明确为 `XUnity AI 翻译`；手动翻译里的 AI 候选生成属于工作台内部能力，不复用原页面入口
- 手动翻译运行时数据根目录固定落在 `%AppData%\UnityLocalizationToolkit\manual-translation`，每游戏目录结构至少包含：`projects/<gameId>/manifest.json`、`asset-index.json`、`overrides/`、`exports/`、`builds/`、`code-patches/`，以及 `backups/<gameId>/`
- 新增每游戏手动翻译缓存或目录时，除了 `AppDataPaths.cs`，还要同步删除游戏时的清理逻辑、设置导出排除列表以及必要的导入重建逻辑
- 手动翻译后端接口统一挂在 `/api/games/{id}/manual-translation/*`，当前至少包含：`scan`、`status`、`assets`、`asset-detail`、`asset-content`、`save-override`、`delete-override`、`export-asset`、`apply`、`restore`、`build-package`、`import-package`
- `ManualTranslationService` 负责扫描索引、override 管理、应用、回滚、打包与导入；`UnityAssetCatalogService` 负责建立稳定资产索引；`UnityAssetWriteService` 负责文件备份、写回与回滚；`AddressablesCatalogService` 负责 Addressables CRC 清理
- 手动翻译索引模型必须使用稳定主键：`assetId + assetFile + pathId + objectType + fieldPath + valueKind`；不要复用 XUnity 预翻译那套按文本去重的结果结构
- 首版可编辑对象覆盖 `TextAsset`、`MonoBehaviour` 字符串字段、`Texture2D/Sprite`、`Font/TMP`、Mono 托管程序集字符串、`global-metadata.dat` 与 `GameAssembly.dll` 的安全文本承载补丁；无法安全定位或无法稳定回滚的对象必须降级为只读/仅导出
- 手动翻译的默认交付方式是直接修改游戏文件，但 `apply` 前必须先建立可恢复备份；`restore` 必须能基于 `manual-translation/backups/<gameId>/` 完整回滚
- 手动翻译中的 AI 候选生成应复用 `LlmTranslationService.TranslateDetailedAsync(...)`、术语、TM、占位符保护与脚本标签处理，但结果只能写入 manual override，不能回写 XUnity 缓存或运行时 TM
- 游戏库与详情页的状态展示必须并列区分 `XUnity` 和 `手动翻译`；删除游戏时，两套工作流的数据都要同步清理，但状态字段不能互相覆盖
