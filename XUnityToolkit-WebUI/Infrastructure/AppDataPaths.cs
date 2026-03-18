namespace XUnityToolkit_WebUI.Infrastructure;

public sealed class AppDataPaths(IConfiguration config)
{
    private readonly string _root = config["AppData:Root"]
        ?? Path.Combine(Environment.GetFolderPath(
               Environment.SpecialFolder.ApplicationData), "XUnityToolkit");

    public string Root => _root;
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
    public string TranslationMemoryDirectory => Path.Combine(_root, "translation-memory");
    public string DynamicPatternsDirectory => Path.Combine(_root, "dynamic-patterns");
    public string TermCandidatesDirectory => Path.Combine(_root, "term-candidates");

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

    public string GlossaryFile(string gameId) =>
        Path.Combine(GlossariesDirectory, $"{gameId}.json");

    public string DoNotTranslateFile(string gameId) =>
        Path.Combine(DoNotTranslateDirectory, $"{gameId}.json");

    public string PreTranslationRegexFile(string gameId) =>
        Path.Combine(PreTranslationRegexDirectory, $"{gameId}.txt");

    public string ScriptTagFile(string gameId) =>
        Path.Combine(ScriptTagsDirectory, $"{gameId}.json");

    public string ExtractedTextsFile(string gameId) =>
        Path.Combine(ExtractedTextsDirectory, $"{gameId}.json");

    public string BackupDirectory(string gameId) =>
        Path.Combine(BackupsDirectory, gameId);

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
        Directory.CreateDirectory(FontBackupsDirectory);
        Directory.CreateDirectory(CustomFontsDirectory);
        Directory.CreateDirectory(FontGenerationUploadsDirectory);
        Directory.CreateDirectory(GeneratedFontsDirectory);
        Directory.CreateDirectory(DoNotTranslateDirectory);
        Directory.CreateDirectory(ScriptTagsDirectory);
        Directory.CreateDirectory(PreTranslationRegexDirectory);
        Directory.CreateDirectory(FontGenerationCharsetUploadsDirectory);
        Directory.CreateDirectory(FontGenerationTranslationUploadsDirectory);
        Directory.CreateDirectory(FontGenerationTempDirectory);
        Directory.CreateDirectory(TranslationMemoryDirectory);
        Directory.CreateDirectory(DynamicPatternsDirectory);
        Directory.CreateDirectory(TermCandidatesDirectory);
    }
}
