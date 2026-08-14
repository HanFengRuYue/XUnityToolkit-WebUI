using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ToolboxAgentConversationStore(
    AppDataPaths paths,
    ILogger<ToolboxAgentConversationStore> logger)
{
    internal const int MaxConversations = 100;
    internal const int MaxVisibleMessages = 200;
    private const long MaxConversationFileBytes = 4L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<List<ToolboxAgentConversationSummary>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var documents = await ReadAllUnsafeAsync(ct);
            return documents
                .OrderByDescending(document => document.UpdatedAt)
                .Select(document => document.ToSummary())
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ToolboxAgentConversationDocument?> LoadAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ToolboxAgentAttachmentStore.ValidateSessionId(sessionId);
        await _gate.WaitAsync(ct);
        try
        {
            return await ReadUnsafeAsync(GetConversationFile(sessionId), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<IReadOnlyList<string>> SaveAsync(
        ToolboxAgentConversationDocument document,
        CancellationToken ct = default)
    {
        ToolboxAgentAttachmentStore.ValidateSessionId(document.SessionId);
        document.Messages = document.Messages.TakeLast(MaxVisibleMessages).ToList();

        await _gate.WaitAsync(ct);
        try
        {
            await FileHelper.WriteJsonAtomicAsync(
                GetConversationFile(document.SessionId),
                document,
                ct: ct);

            var documents = await ReadAllUnsafeAsync(ct);
            var evicted = documents
                .OrderByDescending(item => item.UpdatedAt)
                .Skip(MaxConversations)
                .Select(item => item.SessionId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var sessionId in evicted)
            {
                TryDeleteFile(GetConversationFile(sessionId));
            }

            return evicted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ToolboxAgentAttachmentStore.ValidateSessionId(sessionId);
        await _gate.WaitAsync(ct);
        try
        {
            var file = GetConversationFile(sessionId);
            if (!File.Exists(file))
                return false;
            File.Delete(file);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(paths.ToolboxAgentHistoryDirectory))
                return;
            foreach (var file in Directory.EnumerateFiles(paths.ToolboxAgentHistoryDirectory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                TryDeleteFile(file);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ToolboxAgentConversationDocument>> ReadAllUnsafeAsync(CancellationToken ct)
    {
        if (!Directory.Exists(paths.ToolboxAgentHistoryDirectory))
            return [];

        var documents = new List<ToolboxAgentConversationDocument>();
        foreach (var file in Directory.EnumerateFiles(paths.ToolboxAgentHistoryDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var document = await ReadUnsafeAsync(file, ct);
            if (document is not null)
                documents.Add(document);
        }

        return documents;
    }

    private async Task<ToolboxAgentConversationDocument?> ReadUnsafeAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file))
            return null;

        try
        {
            var info = new FileInfo(file);
            if (info.Length <= 0 || info.Length > MaxConversationFileBytes)
                throw new InvalidDataException("历史文件大小无效。");

            await using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ToolboxAgentConversationDocument>(
                stream,
                FileHelper.DataJsonOptions,
                ct);
            if (document is null
                || !Guid.TryParse(document.SessionId, out _)
                || document.Version != 1)
            {
                throw new InvalidDataException("历史文件结构无效。");
            }

            document.Messages ??= [];
            document.ContextMessages ??= [];
            document.Title = string.IsNullOrWhiteSpace(document.Title) ? "新对话" : document.Title;
            return document;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "忽略损坏的工具箱智能体历史文件: {File}", Path.GetFileName(file));
            return null;
        }
    }

    private string GetConversationFile(string sessionId) =>
        Path.Combine(paths.ToolboxAgentHistoryDirectory, $"{sessionId}.json");

    private void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "删除工具箱智能体历史文件失败: {File}", Path.GetFileName(file));
        }
    }
}
