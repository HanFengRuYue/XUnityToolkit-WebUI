namespace XUnityToolkit_WebUI.Infrastructure;

public sealed class AppDataPaths(IConfiguration config)
{
    private readonly string _root = config["AppData:Root"]
        ?? AppContext.BaseDirectory;

    public string Root => _root;
    public string LibraryFile => Path.Combine(_root, "library.json");
    public string BackupsDirectory => Path.Combine(_root, "backups");
    public string CacheDirectory => Path.Combine(_root, "cache");
    public string SettingsFile => Path.Combine(_root, "settings.json");
    public string GlossariesDirectory => Path.Combine(_root, "glossaries");

    public string CoversDirectory => Path.Combine(CacheDirectory, "covers");
    public string IconsDirectory => Path.Combine(CacheDirectory, "icons");
    public string LogsDirectory => Path.Combine(_root, "logs");
    public string LogFile => Path.Combine(LogsDirectory, "app.log");

    public string GlossaryFile(string gameId) =>
        Path.Combine(GlossariesDirectory, $"{gameId}.json");

    public string BackupDirectory(string gameId) =>
        Path.Combine(BackupsDirectory, gameId);

    public string CoverFile(string gameId) =>
        Path.Combine(CoversDirectory, $"{gameId}.img");

    public string CoverMetaFile(string gameId) =>
        Path.Combine(CoversDirectory, $"{gameId}.meta");

    public string CustomIconFile(string gameId) =>
        Path.Combine(IconsDirectory, $"{gameId}.custom.png");

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(IconsDirectory);
        Directory.CreateDirectory(GlossariesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
