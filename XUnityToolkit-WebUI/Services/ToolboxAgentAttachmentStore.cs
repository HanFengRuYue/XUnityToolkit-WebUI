using System.Collections.Concurrent;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

internal sealed record StoredToolboxAgentAttachment(
    string Id,
    string SessionId,
    string FileName,
    string Kind,
    string FullPath,
    long FileSize,
    DateTime CreatedAtUtc);

public sealed class ToolboxAgentAttachmentStore(
    AppDataPaths paths,
    ILogger<ToolboxAgentAttachmentStore> logger)
{
    private const long MaxAttachmentBytes = 50L * 1024 * 1024;
    private const long MaxSessionBytes = 150L * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(6);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".bundle", ".dll", ".zip",
        ".txt", ".ini", ".cfg", ".json", ".xml", ".yaml", ".yml",
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly ConcurrentDictionary<string, StoredToolboxAgentAttachment> _attachments =
        new(StringComparer.Ordinal);

    public async Task<ToolboxAgentAttachment> SaveAsync(
        string sessionId,
        IFormFile file,
        CancellationToken ct)
    {
        ValidateSessionId(sessionId);
        CleanupExpired();

        if (file.Length <= 0)
            throw new InvalidDataException("上传文件为空。");
        if (file.Length > MaxAttachmentBytes)
            throw new InvalidDataException("单个附件不能超过 50 MB。");

        var originalName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalName);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException("该文件类型不能交给工具箱智能体处理。");

        var existingBytes = _attachments.Values
            .Where(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal))
            .Sum(item => item.FileSize);
        if (existingBytes + file.Length > MaxSessionBytes)
            throw new InvalidDataException("当前对话的附件总大小不能超过 150 MB。");

        var directory = Path.Combine(paths.ToolboxAgentUploadsDirectory, sessionId);
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var storedName = $"{id}{extension.ToLowerInvariant()}";
        var fullPath = PathSecurity.SafeJoin(directory, storedName);

        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write,
                         FileShare.None, 81920, useAsync: true))
        {
            await file.CopyToAsync(stream, ct);
        }

        var kind = Classify(extension);
        var stored = new StoredToolboxAgentAttachment(
            id, sessionId, originalName, kind, fullPath, file.Length, DateTime.UtcNow);
        _attachments[id] = stored;
        return ToPublic(stored);
    }

    internal StoredToolboxAgentAttachment GetRequired(string sessionId, string attachmentId)
    {
        ValidateSessionId(sessionId);
        if (!_attachments.TryGetValue(attachmentId, out var attachment)
            || !string.Equals(attachment.SessionId, sessionId, StringComparison.Ordinal)
            || !File.Exists(attachment.FullPath))
        {
            throw new FileNotFoundException("智能体附件不存在或已过期。");
        }

        return attachment;
    }

    public IReadOnlyList<ToolboxAgentAttachment> GetMany(string sessionId, IEnumerable<string>? ids)
    {
        if (ids is null)
            return [];

        return ids.Distinct(StringComparer.Ordinal)
            .Take(8)
            .Select(id => ToPublic(GetRequired(sessionId, id)))
            .ToList();
    }

    public void ClearSession(string sessionId)
    {
        ValidateSessionId(sessionId);
        foreach (var pair in _attachments.Where(pair => pair.Value.SessionId == sessionId).ToList())
            _attachments.TryRemove(pair.Key, out _);

        var directory = Path.Combine(paths.ToolboxAgentUploadsDirectory, sessionId);
        TryDeleteDirectory(directory);
    }

    internal static void ValidateSessionId(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _))
            throw new InvalidDataException("无效的智能体会话 ID。");
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        var expiredSessions = _attachments.Values
            .Where(item => item.CreatedAtUtc < cutoff)
            .Select(item => item.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var sessionId in expiredSessions)
            ClearSession(sessionId);

        if (!Directory.Exists(paths.ToolboxAgentUploadsDirectory))
            return;
        foreach (var directory in Directory.EnumerateDirectories(paths.ToolboxAgentUploadsDirectory))
        {
            var name = Path.GetFileName(directory);
            if (Guid.TryParse(name, out _) && Directory.GetLastWriteTimeUtc(directory) < cutoff)
                TryDeleteDirectory(directory);
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            var root = Path.GetFullPath(paths.ToolboxAgentUploadsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(directory);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "清理工具箱智能体附件失败: {Directory}", directory);
        }
    }

    private static ToolboxAgentAttachment ToPublic(StoredToolboxAgentAttachment item) =>
        new(item.Id, item.FileName, item.Kind, item.FileSize);

    private static string Classify(string extension) => extension.ToLowerInvariant() switch
    {
        ".ttf" or ".otf" => "font",
        ".dll" or ".zip" => "plugin",
        ".png" or ".jpg" or ".jpeg" or ".webp" => "image",
        ".bundle" => "tmp-font",
        _ => "text"
    };
}
