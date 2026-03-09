namespace XUnityToolkit_WebUI.Infrastructure;

public sealed class AppDataPaths(IConfiguration config)
{
    private readonly string _root = config["AppData:Root"]
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnityToolkit");

    public string Root => _root;
    public string LibraryFile => Path.Combine(_root, "library.json");
    public string BackupsDirectory => Path.Combine(_root, "backups");
    public string CacheDirectory => Path.Combine(_root, "cache");
    public string SettingsFile => Path.Combine(_root, "settings.json");

    public string BackupDirectory(string gameId) =>
        Path.Combine(BackupsDirectory, gameId);

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
