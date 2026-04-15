namespace UnityLocalizationToolkit_WebUI.Infrastructure;

public sealed class AppDataPaths(IConfiguration config)
{
    private readonly string _legacyRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XUnityToolkit");

    private readonly string _root = config["AppData:Root"]
        ?? Path.Combine(Environment.GetFolderPath(
               Environment.SpecialFolder.ApplicationData), "UnityLocalizationToolkit");

    public string Root => _root;
    public string LegacyRoot => _legacyRoot;
    public string LibraryFile => Path.Combine(_root, "library.json");
    public string BackupsDirectory => Path.Combine(_root, "backups");
    public string CacheDirectory => Path.Combine(_root, "cache");
    public string SettingsFile => Path.Combine(_root, "settings.json");
    public string GlossariesDirectory => Path.Combine(_root, "glossaries");

    public string CoversDirectory => Path.Combine(CacheDirectory, "covers");
    public string IconsDirectory => Path.Combine(CacheDirectory, "icons");
    public string BackgroundsDirectory => Path.Combine(CacheDirectory, "backgrounds");
    public string LogsDirectory => Path.Combine(_root, "logs");
    public string ExtractedTextsDirectory => Path.Combine(CacheDirectory, "extracted-texts");
    public string LlamaDirectory => Path.Combine(_root, "llama");
    public string LlamaLaunchCacheDirectory => Path.Combine(LlamaDirectory, "launch-cache");
    public string ModelsDirectory => Path.Combine(_root, "models");
    public string LocalLlmSettingsFile => Path.Combine(_root, "local-llm-settings.json");
    public string FontBackupsDirectory => Path.Combine(_root, "font-backups");
    public string CustomFontsDirectory => Path.Combine(_root, "custom-fonts");
    public string FontGenerationUploadsDirectory => Path.Combine(_root, "font-generation", "uploads");
    public string GeneratedFontsDirectory => Path.Combine(_root, "generated-fonts");
    [Obsolete("Migrated to glossaries/. Kept for migration path.")]
    public string DoNotTranslateDirectory => Path.Combine(_root, "do-not-translate");
    public string ScriptTagsDirectory => Path.Combine(_root, "script-tags");
    public string PreTranslationRegexDirectory => Path.Combine(CacheDirectory, "pre-translation-regex");
    public string PreTranslationSessionDirectory => Path.Combine(CacheDirectory, "pre-translation-sessions");
    public string TranslationMemoryDirectory => Path.Combine(_root, "translation-memory");
    public string DynamicPatternsDirectory => Path.Combine(_root, "dynamic-patterns");
    public string TermCandidatesDirectory => Path.Combine(_root, "term-candidates");
    public string ManualTranslationDirectory => Path.Combine(_root, "manual-translation");
    public string ManualTranslationProjectsDirectory => Path.Combine(ManualTranslationDirectory, "projects");
    public string ManualTranslationBackupsDirectory => Path.Combine(ManualTranslationDirectory, "backups");

    public string TranslationMemoryFile(string gameId) =>
        Path.Combine(TranslationMemoryDirectory, $"{gameId}.json");
    public string DynamicPatternsFile(string gameId) =>
        Path.Combine(DynamicPatternsDirectory, $"{gameId}.json");
    public string TermCandidatesFile(string gameId) =>
        Path.Combine(TermCandidatesDirectory, $"{gameId}.json");
    public string FontGenerationCharsetUploadsDirectory =>
        Path.Combine(_root, "font-generation", "uploads", "charset");
    public string FontGenerationTranslationUploadsDirectory =>
        Path.Combine(_root, "font-generation", "uploads", "translation");
    public string FontGenerationTempDirectory =>
        Path.Combine(_root, "font-generation", "temp");

    public string GetFontBackupDirectory(string gameId) =>
        Path.Combine(FontBackupsDirectory, gameId);

    public string GetCustomFontDirectory(string gameId) =>
        Path.Combine(CustomFontsDirectory, gameId);

    public string GetCustomTtfFontDirectory(string gameId) =>
        Path.Combine(GetCustomFontDirectory(gameId), "ttf");

    public string GetCustomTmpFontDirectory(string gameId) =>
        Path.Combine(GetCustomFontDirectory(gameId), "tmp");

    public string GlossaryFile(string gameId) =>
        Path.Combine(GlossariesDirectory, $"{gameId}.json");

    public string DoNotTranslateFile(string gameId) =>
#pragma warning disable CS0618 // Obsolete — migration path usage
        Path.Combine(DoNotTranslateDirectory, $"{gameId}.json");
#pragma warning restore CS0618

    public string PreTranslationRegexFile(string gameId) =>
        Path.Combine(PreTranslationRegexDirectory, $"{gameId}.txt");

    public string PreTranslationSessionFile(string gameId) =>
        Path.Combine(PreTranslationSessionDirectory, $"{gameId}.json");

    public string ScriptTagFile(string gameId) =>
        Path.Combine(ScriptTagsDirectory, $"{gameId}.json");

    public string ExtractedTextsFile(string gameId) =>
        Path.Combine(ExtractedTextsDirectory, $"{gameId}.json");

    public string BackupDirectory(string gameId) =>
        Path.Combine(BackupsDirectory, gameId);

    public string ManualTranslationProjectDirectory(string gameId) =>
        Path.Combine(ManualTranslationProjectsDirectory, gameId);

    public string ManualTranslationManifestFile(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "manifest.json");

    public string ManualTranslationAssetIndexFile(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "asset-index.json");

    public string ManualTranslationOverridesDirectory(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "overrides");

    public string ManualTranslationOverrideMediaDirectory(string gameId) =>
        Path.Combine(ManualTranslationOverridesDirectory(gameId), "media");

    public string ManualTranslationExportsDirectory(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "exports");

    public string ManualTranslationBuildsDirectory(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "builds");

    public string ManualTranslationCodePatchesDirectory(string gameId) =>
        Path.Combine(ManualTranslationProjectDirectory(gameId), "code-patches");

    public string ManualTranslationBackupDirectory(string gameId) =>
        Path.Combine(ManualTranslationBackupsDirectory, gameId);

    public string ManualTranslationOverrideFile(string gameId, string overrideFileName) =>
        Path.Combine(ManualTranslationOverridesDirectory(gameId), overrideFileName);

    public string ManualTranslationOverrideMediaFile(string gameId, string fileName) =>
        Path.Combine(ManualTranslationOverrideMediaDirectory(gameId), fileName);

    public string CoverFile(string gameId) =>
        Path.Combine(CoversDirectory, $"{gameId}.img");

    public string CoverMetaFile(string gameId) =>
        Path.Combine(CoversDirectory, $"{gameId}.meta");

    public string CustomIconFile(string gameId) =>
        Path.Combine(IconsDirectory, $"{gameId}.custom.png");

    public string BackgroundFile(string gameId) =>
        Path.Combine(BackgroundsDirectory, $"{gameId}.img");

    public string BackgroundMetaFile(string gameId) =>
        Path.Combine(BackgroundsDirectory, $"{gameId}.meta");

    public void EnsureDirectoriesExist()
    {
        MigrateLegacyRootIfNeeded();
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(IconsDirectory);
        Directory.CreateDirectory(BackgroundsDirectory);
        Directory.CreateDirectory(GlossariesDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExtractedTextsDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(LlamaDirectory);
        Directory.CreateDirectory(LlamaLaunchCacheDirectory);
        Directory.CreateDirectory(FontBackupsDirectory);
        Directory.CreateDirectory(CustomFontsDirectory);
        Directory.CreateDirectory(FontGenerationUploadsDirectory);
        Directory.CreateDirectory(GeneratedFontsDirectory);
#pragma warning disable CS0618 // Obsolete — migration path usage
        Directory.CreateDirectory(DoNotTranslateDirectory);
#pragma warning restore CS0618
        Directory.CreateDirectory(ScriptTagsDirectory);
        Directory.CreateDirectory(PreTranslationRegexDirectory);
        Directory.CreateDirectory(PreTranslationSessionDirectory);
        Directory.CreateDirectory(FontGenerationCharsetUploadsDirectory);
        Directory.CreateDirectory(FontGenerationTranslationUploadsDirectory);
        Directory.CreateDirectory(FontGenerationTempDirectory);
        Directory.CreateDirectory(TranslationMemoryDirectory);
        Directory.CreateDirectory(DynamicPatternsDirectory);
        Directory.CreateDirectory(TermCandidatesDirectory);
        Directory.CreateDirectory(ManualTranslationDirectory);
        Directory.CreateDirectory(ManualTranslationProjectsDirectory);
        Directory.CreateDirectory(ManualTranslationBackupsDirectory);
    }

    private void MigrateLegacyRootIfNeeded()
    {
        if (!Directory.Exists(_legacyRoot) || Directory.Exists(_root))
            return;

        CopyDirectory(_legacyRoot, _root);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }
}
