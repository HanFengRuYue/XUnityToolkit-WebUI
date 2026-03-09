using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class BackupService(AppDataPaths paths, ILogger<BackupService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<BackupManifest> CreateBackupAsync(
        string gameId, string gamePath, IEnumerable<string> relativePathsToBackup,
        CancellationToken ct = default)
    {
        var backupDir = paths.BackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);

        var manifest = new BackupManifest { GameId = gameId };

        foreach (var relativePath in relativePathsToBackup)
        {
            var sourcePath = Path.Combine(gamePath, relativePath);
            if (!File.Exists(sourcePath)) continue;

            var backupFileName = relativePath.Replace(Path.DirectorySeparatorChar, '_')
                .Replace(Path.AltDirectorySeparatorChar, '_');
            var backupPath = Path.Combine(backupDir, backupFileName);

            File.Copy(sourcePath, backupPath, overwrite: true);
            manifest.BackedUpFiles.Add(new BackupEntry
            {
                OriginalRelativePath = relativePath,
                BackupFileName = backupFileName
            });
        }

        // Save manifest
        var manifestPath = Path.Combine(backupDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        logger.LogInformation("已创建备份: 游戏 {GameId}，共 {Count} 个文件",
            gameId, manifest.BackedUpFiles.Count);

        return Task.FromResult(manifest);
    }

    public Task<BackupManifest?> GetManifestAsync(string gameId, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(paths.BackupDirectory(gameId), "manifest.json");
        if (!File.Exists(manifestPath))
            return Task.FromResult<BackupManifest?>(null);

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions);
        return Task.FromResult(manifest);
    }

    public Task RestoreAsync(string gameId, string gamePath, CancellationToken ct = default)
    {
        var backupDir = paths.BackupDirectory(gameId);
        var manifestPath = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("未找到备份记录: 游戏 {GameId}", gameId);
            return Task.CompletedTask;
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions);

        if (manifest is null) return Task.CompletedTask;

        foreach (var entry in manifest.BackedUpFiles)
        {
            var backupPath = PathSecurity.SafeJoin(backupDir, entry.BackupFileName);
            var restorePath = PathSecurity.SafeJoin(gamePath, entry.OriginalRelativePath);

            if (!File.Exists(backupPath)) continue;

            var restoreDir = Path.GetDirectoryName(restorePath)!;
            Directory.CreateDirectory(restoreDir);
            File.Copy(backupPath, restorePath, overwrite: true);
        }

        logger.LogInformation("已恢复备份: 游戏 {GameId}", gameId);
        return Task.CompletedTask;
    }

    public Task DeleteBackupAsync(string gameId, CancellationToken ct = default)
    {
        var backupDir = paths.BackupDirectory(gameId);
        if (Directory.Exists(backupDir))
        {
            Directory.Delete(backupDir, recursive: true);
            logger.LogInformation("已删除备份: 游戏 {GameId}", gameId);
        }
        return Task.CompletedTask;
    }
}
