using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Endpoints;

public static class FileExplorerEndpoints
{
    public static void MapFileExplorerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/filesystem");

        group.MapGet("/drives", (ILogger<Program> logger) =>
        {
            var drives = new List<DriveEntry>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;

                    drives.Add(new DriveEntry(
                        Name: drive.Name.TrimEnd('\\'),
                        RootPath: drive.RootDirectory.FullName,
                        Label: string.IsNullOrEmpty(drive.VolumeLabel) ? null : drive.VolumeLabel,
                        TotalSize: drive.TotalSize,
                        FreeSpace: drive.AvailableFreeSpace));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Skipping inaccessible drive {Drive}", drive.Name);
                }
            }

            return Results.Ok(ApiResult<List<DriveEntry>>.Ok(drives));
        });

        group.MapPost("/list", (ListDirectoryRequest request, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path))
                return Results.BadRequest(ApiResult.Fail("路径不能为空"));

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(request.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Results.BadRequest(ApiResult.Fail("路径格式无效"));
            }

            if (!Directory.Exists(normalizedPath))
                return Results.BadRequest(ApiResult.Fail("目录不存在"));

            try
            {
                var dirInfo = new DirectoryInfo(normalizedPath);
                var entries = new List<FileSystemEntry>();

                // Enumerate directories
                try
                {
                    foreach (var dir in dirInfo.EnumerateDirectories())
                    {
                        try
                        {
                            entries.Add(new FileSystemEntry(
                                Name: dir.Name,
                                FullPath: dir.FullName,
                                IsDirectory: true,
                                Size: null,
                                LastModified: dir.LastWriteTime,
                                Extension: null));
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip inaccessible entries silently
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Cannot enumerate directories at all
                }

                // Enumerate files
                try
                {
                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        try
                        {
                            entries.Add(new FileSystemEntry(
                                Name: file.Name,
                                FullPath: file.FullName,
                                IsDirectory: false,
                                Size: file.Length,
                                LastModified: file.LastWriteTime,
                                Extension: file.Extension.ToLowerInvariant()));
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip inaccessible entries silently
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Cannot enumerate files at all
                }

                // Sort: directories first (by name), then files (by name)
                entries.Sort((a, b) =>
                {
                    if (a.IsDirectory != b.IsDirectory)
                        return a.IsDirectory ? -1 : 1;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                var parentPath = Directory.GetParent(normalizedPath)?.FullName;

                return Results.Ok(ApiResult<ListDirectoryResponse>.Ok(
                    new ListDirectoryResponse(normalizedPath, parentPath, entries)));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.BadRequest(ApiResult.Fail("无法访问该目录"));
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "IO error listing directory");
                return Results.BadRequest(ApiResult.Fail("无法读取目录"));
            }
        });
    }
}
