using System.Reflection;
using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Services;

public sealed class UnityAssetWriteServiceTests : IDisposable
{
    private readonly string _root;

    public UnityAssetWriteServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ult-write-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void EnsureBackup_ShouldRefreshExistingBackupContent()
    {
        var gamePath = Path.Combine(_root, "game");
        var backupDirectory = Path.Combine(_root, "backups");
        Directory.CreateDirectory(gamePath);
        Directory.CreateDirectory(backupDirectory);

        var sourcePath = Path.Combine(gamePath, "global-metadata.dat");
        var manifest = new ManualTranslationBackupManifest { GameId = "game-1" };

        File.WriteAllText(sourcePath, "first version");
        Assert.True(InvokeEnsureBackup(gamePath, sourcePath, backupDirectory, manifest));

        var backupEntry = Assert.Single(manifest.BackedUpFiles);
        var backupPath = Path.Combine(backupDirectory, backupEntry.BackupFileName);
        Assert.Equal("first version", File.ReadAllText(backupPath));

        File.WriteAllText(sourcePath, "second version");
        Assert.True(InvokeEnsureBackup(gamePath, sourcePath, backupDirectory, manifest));

        Assert.Single(manifest.BackedUpFiles);
        Assert.Equal("second version", File.ReadAllText(backupPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static bool InvokeEnsureBackup(
        string gamePath,
        string sourcePath,
        string backupDirectory,
        ManualTranslationBackupManifest manifest)
    {
        var method = typeof(UnityAssetWriteService).GetMethod(
            "EnsureBackup",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (bool)method!.Invoke(null, [gamePath, sourcePath, backupDirectory, manifest])!;
    }
}
