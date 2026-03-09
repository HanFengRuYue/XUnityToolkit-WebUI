using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Endpoints;

public static class CacheEndpoints
{
    public static void MapCacheEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cache");

        group.MapGet("/downloads", (AppDataPaths paths) =>
        {
            var info = GetCacheInfo(paths);
            return Results.Ok(ApiResult<CacheInfo>.Ok(info));
        });

        group.MapDelete("/downloads", (AppDataPaths paths) =>
        {
            var files = GetDownloadFiles(paths);
            long bytes = 0;
            int deleted = 0;
            foreach (var f in files)
            {
                try
                {
                    var size = new FileInfo(f).Length;
                    File.Delete(f);
                    bytes += size;
                    deleted++;
                }
                catch { /* best effort — file may be in use */ }
            }
            return Results.Ok(ApiResult<CacheInfo>.Ok(new CacheInfo(deleted, bytes)));
        });
    }

    private static string[] GetDownloadFiles(AppDataPaths paths) =>
        Directory.Exists(paths.CacheDirectory)
            ? Directory.GetFiles(paths.CacheDirectory, "*", SearchOption.TopDirectoryOnly)
                       .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                       .ToArray()
            : [];

    private static CacheInfo GetCacheInfo(AppDataPaths paths)
    {
        var files = GetDownloadFiles(paths);
        var totalBytes = files.Sum(f =>
        {
            try { return new FileInfo(f).Length; }
            catch { return 0L; }
        });
        return new CacheInfo(files.Length, totalBytes);
    }
}

public record CacheInfo(int FileCount, long TotalBytes);
