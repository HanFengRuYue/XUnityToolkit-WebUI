using System.Text.RegularExpressions;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class AddressablesCatalogService(ILogger<AddressablesCatalogService> logger)
{
    public List<string> ClearCrcForModifiedGame(string gamePath, Func<string, bool> ensureBackup)
    {
        var touched = new List<string>();

        foreach (var catalogPath in Directory.GetFiles(gamePath, "catalog.json", SearchOption.AllDirectories))
        {
            try
            {
                var content = File.ReadAllText(catalogPath);
                var updated = Regex.Replace(content, "\"Crc\"\\s*:\\s*\\d+", "\"Crc\":0");
                if (updated == content)
                    continue;

                if (!ensureBackup(catalogPath))
                    continue;

                File.WriteAllText(catalogPath, updated);
                touched.Add(Path.GetRelativePath(gamePath, catalogPath).Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clear Addressables CRC in {Catalog}", catalogPath);
            }
        }

        return touched;
    }
}
