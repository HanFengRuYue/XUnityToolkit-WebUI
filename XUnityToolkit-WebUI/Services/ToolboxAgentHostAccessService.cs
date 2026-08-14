using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Generic host access for the Toolbox Agent. Trusted writes are confined to registered game roots
/// and AppData:Root. External access is read-only and always goes through the agent confirmation flow.
/// </summary>
public sealed class ToolboxAgentHostAccessService(
    GameLibraryService gameLibrary,
    AppDataPaths paths,
    ToolboxAgentAttachmentStore attachmentStore,
    ILogger<ToolboxAgentHostAccessService> logger)
{
    private const int MaxDirectoryEntries = 200;
    private const int MaxTextLines = 400;
    private const int MaxTextCharacters = 40_000;
    private const int MaxHexBytes = 16 * 1024;
    private const int MaxScriptCharacters = 20_000;
    private const int MaxScriptOutputCharacters = 40_000;
    private const int MaxFileOperations = 20;

    internal async Task<ToolboxAgentToolResult> ListPathAsync(
        JsonElement arguments,
        string? selectedGameId,
        bool confirmed,
        CancellationToken ct)
    {
        var location = await ResolveLocationAsync(arguments, selectedGameId, requireExisting: true, ct);
        if (!Directory.Exists(location.FullPath))
            throw new DirectoryNotFoundException("指定目录不存在。");
        EnsureReadableLocation(location);
        if (location.Scope == AgentPathScope.External && !confirmed)
        {
            return Confirmation(
                "读取外部目录",
                $"用途：{GetPurpose(arguments)}\n\n将列出电脑目录：\n{location.FullPath}\n\n目录名称和绝对路径会原样发送给所选云端提供商。是否继续？");
        }

        var entries = Directory.EnumerateFileSystemEntries(location.FullPath, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDirectoryEntries + 1)
            .ToList();
        var truncated = entries.Count > MaxDirectoryEntries;
        var payload = entries.Take(MaxDirectoryEntries).Select(item =>
        {
            var attributes = File.GetAttributes(item);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            return new
            {
                path = item,
                name = Path.GetFileName(item),
                type = isDirectory ? "directory" : "file",
                reparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint),
                size = !isDirectory ? new FileInfo(item).Length : (long?)null,
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(item)
            };
        });
        return Success("列出文件目录", new { location.FullPath, truncated, entries = payload },
            $"已列出 {Math.Min(entries.Count, MaxDirectoryEntries)} 项目录内容。 ");
    }

    internal async Task<ToolboxAgentToolResult> ReadFileAsync(
        JsonElement arguments,
        string? selectedGameId,
        bool confirmed,
        CancellationToken ct)
    {
        var location = await ResolveLocationAsync(arguments, selectedGameId, requireExisting: true, ct);
        if (!File.Exists(location.FullPath))
            throw new FileNotFoundException("指定文件不存在。");
        EnsureReadableLocation(location);
        if (location.Scope == AgentPathScope.External && !confirmed)
        {
            return Confirmation(
                "读取外部文件",
                $"用途：{GetPurpose(arguments)}\n\n将读取电脑文件：\n{location.FullPath}\n\n文件原文、绝对路径或所请求的二进制片段会原样发送给所选云端提供商。是否继续？");
        }

        var mode = GetOptionalString(arguments, "mode")?.ToLowerInvariant() ?? "auto";
        if (mode == "auto")
            mode = await IsProbablyTextFileAsync(location.FullPath, ct) ? "text" : "metadata";
        return mode switch
        {
            "text" => await ReadTextAsync(location.FullPath, arguments, ct),
            "hex" => await ReadHexAsync(location.FullPath, arguments, ct),
            "metadata" => await ReadMetadataAsync(location.FullPath, ct),
            _ => throw new InvalidDataException("读取模式只能是 auto、text、hex 或 metadata。")
        };
    }

    internal async Task<ToolboxAgentToolResult> ManageFilesAsync(
        string sessionId,
        JsonElement arguments,
        string? selectedGameId,
        bool confirmed,
        CancellationToken ct)
    {
        var operations = await ParseOperationsAsync(sessionId, arguments, selectedGameId, ct);
        if (operations.Count == 0)
            throw new InvalidDataException("文件变更批次不能为空。");
        if (!confirmed)
        {
            var preview = string.Join('\n', operations.Select((item, index) => $"{index + 1}. {item.Preview}"));
            return Confirmation(
                "批量变更可信目录文件",
                $"用途：{GetPurpose(arguments)}\n\n以下 {operations.Count} 项操作将修改已添加游戏目录或工具箱数据目录：\n{preview}\n\n这些通用文件操作不会自动备份。是否确认执行整个批次？");
        }

        var results = new List<object>();
        var succeeded = 0;
        foreach (var operation in operations)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ExecuteOperationAsync(operation, ct);
                succeeded++;
                results.Add(new { operation.Kind, operation.Preview, success = true, error = (string?)null });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "工具箱智能体文件操作失败: {Operation}", operation.Preview);
                results.Add(new { operation.Kind, operation.Preview, success = false, error = UserFacingError(ex) });
            }
        }

        return new ToolboxAgentToolResult(
            succeeded == operations.Count,
            "批量变更可信目录文件",
            Trim(JsonSerializer.Serialize(new { succeeded, failed = operations.Count - succeeded, results }, FileHelper.DataJsonOptions)),
            $"文件变更批次完成：成功 {succeeded} 项，失败 {operations.Count - succeeded} 项。 ");
    }

    internal async Task<ToolboxAgentToolResult> RunScriptAsync(
        JsonElement arguments,
        bool confirmed,
        CancellationToken ct)
    {
        var shell = GetRequiredString(arguments, "shell").ToLowerInvariant();
        if (shell is not ("powershell" or "cmd"))
            throw new InvalidDataException("脚本宿主只能是 powershell 或 cmd。");
        var script = GetRequiredString(arguments, "script");
        var purpose = GetPurpose(arguments);
        if (script.Length > MaxScriptCharacters)
            throw new InvalidDataException($"脚本不能超过 {MaxScriptCharacters:N0} 个字符。");
        var timeoutSeconds = Math.Clamp(GetOptionalInt(arguments, "timeoutSeconds") ?? 30, 1, 120);

        if (!confirmed)
        {
            return Confirmation(
                $"运行 {shell} 诊断脚本",
                $"用途：{purpose}\n\n宿主：{shell}\n超时：{timeoutSeconds} 秒\n\n完整脚本：\n{script}\n\n脚本仅受智能体提示词约束为读取用途，后端不能证明它绝对只读；确认后将以当前 Windows 用户权限运行，原始输出会发送给所选云端提供商。是否继续？");
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var executable = shell == "powershell"
            ? Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            : Path.Combine(systemDirectory, "cmd.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("找不到系统脚本宿主。", executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        if (shell == "powershell")
        {
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);
        }
        else
        {
            startInfo.ArgumentList.Add("/D");
            startInfo.ArgumentList.Add("/Q");
            startInfo.ArgumentList.Add("/S");
            startInfo.ArgumentList.Add("/C");
            startInfo.ArgumentList.Add(script);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动脚本宿主。");
        var stdoutTask = ReadLimitedAsync(process.StandardOutput, MaxScriptOutputCharacters, ct);
        var stderrTask = ReadLimitedAsync(process.StandardError, MaxScriptOutputCharacters, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            await process.WaitForExitAsync(CancellationToken.None);
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var payload = new
        {
            purpose,
            shell,
            script,
            exitCode = timedOut ? (int?)null : process.ExitCode,
            timedOut,
            stdout = stdout.Content,
            stdoutTruncated = stdout.Truncated,
            stderr = stderr.Content,
            stderrTruncated = stderr.Truncated
        };
        var success = !timedOut && process.ExitCode == 0;
        return new ToolboxAgentToolResult(
            success,
            $"运行 {shell} 诊断脚本",
            Trim(JsonSerializer.Serialize(payload, FileHelper.DataJsonOptions)),
            timedOut
                ? $"{shell} 脚本超过 {timeoutSeconds} 秒，已终止。"
                : $"{shell} 脚本已结束，退出码 {process.ExitCode}。 ");
    }

    private async Task<AgentPathLocation> ResolveLocationAsync(
        JsonElement arguments,
        string? selectedGameId,
        bool requireExisting,
        CancellationToken ct)
    {
        var scopeText = GetRequiredString(arguments, "scope").ToLowerInvariant();
        var path = GetOptionalString(arguments, "path") ?? string.Empty;
        switch (scopeText)
        {
            case "game":
            {
                var gameId = GetOptionalString(arguments, "gameId") ?? selectedGameId;
                if (string.IsNullOrWhiteSpace(gameId))
                    throw new InvalidDataException("game 作用域需要 gameId 或当前选中的游戏。");
                var game = await gameLibrary.GetByIdAsync(gameId, ct)
                           ?? throw new InvalidOperationException("指定游戏不存在。");
                return CreateTrustedLocation(AgentPathScope.Game, game.GamePath, path, requireExisting);
            }
            case "toolbox":
                return CreateTrustedLocation(AgentPathScope.Toolbox, paths.Root, path, requireExisting);
            case "external":
            {
                if (!Path.IsPathFullyQualified(path) || IsDevicePath(path))
                    throw new InvalidDataException("外部读取必须提供普通的绝对文件系统路径。");
                var fullPath = Path.GetFullPath(path);
                if (requireExisting && !File.Exists(fullPath) && !Directory.Exists(fullPath))
                    throw new FileNotFoundException("外部路径不存在。", fullPath);
                return new AgentPathLocation(AgentPathScope.External, string.Empty, fullPath);
            }
            default:
                throw new InvalidDataException("路径作用域只能是 game、toolbox 或 external。");
        }
    }

    private static AgentPathLocation CreateTrustedLocation(
        AgentPathScope scope,
        string root,
        string relativePath,
        bool requireExisting)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullRoot, Path.GetPathRoot(fullRoot)?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安全策略拒绝把磁盘根目录作为智能体可信根。");
        if (Path.IsPathFullyQualified(relativePath) || relativePath.Contains(':') || IsDevicePath(relativePath))
            throw new InvalidDataException("可信目录只能使用不含备用数据流的相对路径。");
        var fullPath = string.IsNullOrWhiteSpace(relativePath)
            ? fullRoot
            : PathSecurity.SafeJoin(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (requireExisting && !File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("可信目录内路径不存在。", fullPath);
        return new AgentPathLocation(scope, fullRoot, fullPath);
    }

    private static void EnsureReadableLocation(AgentPathLocation location)
    {
        if (location.Scope == AgentPathScope.External)
            return;
        EnsureNoReparseEscape(location.Root, location.FullPath, allowLeafReparse: false);
    }

    private static void EnsureWritableLocation(AgentPathLocation location, bool allowLeafReparse = false)
    {
        if (location.Scope == AgentPathScope.External)
            throw new InvalidOperationException("外部路径只能读取，不能修改。");
        if (string.Equals(location.Root, location.FullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("通用文件工具不能修改或删除可信根本身；清空工具箱数据必须使用专用工具。");
        EnsureNoReparseEscape(location.Root, location.FullPath, allowLeafReparse);
    }

    private static void EnsureNoReparseEscape(string root, string path, bool allowLeafReparse)
    {
        var current = path;
        var chain = new Stack<string>();
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            chain.Push(current);
            current = Path.GetDirectoryName(current)
                      ?? throw new InvalidOperationException("无法验证路径父目录。");
        }
        while (chain.Count > 0)
        {
            var candidate = chain.Pop();
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                continue;
            var isLeaf = chain.Count == 0;
            if (File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint)
                && !(allowLeafReparse && isLeaf))
                throw new InvalidOperationException("路径包含重解析点；请把真实目标作为外部路径单独确认读取。");
        }
    }

    private async Task<List<FileOperation>> ParseOperationsAsync(
        string sessionId,
        JsonElement arguments,
        string? selectedGameId,
        CancellationToken ct)
    {
        if (!arguments.TryGetProperty("operations", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("文件变更工具缺少 operations 数组。");
        var items = array.EnumerateArray().Take(MaxFileOperations + 1).ToList();
        if (items.Count > MaxFileOperations)
            throw new InvalidDataException($"单个文件变更批次最多 {MaxFileOperations} 项。");

        var result = new List<FileOperation>();
        foreach (var item in items)
        {
            var kind = GetRequiredString(item, "kind").ToLowerInvariant();
            var target = await ResolveLocationAsync(item, selectedGameId, requireExisting: kind is "delete" or "move", ct);
            EnsureWritableLocation(target, allowLeafReparse: kind == "delete");
            AgentPathLocation? source = null;
            StoredToolboxAgentAttachment? attachment = null;
            if (kind is "copy" or "move")
            {
                if (!item.TryGetProperty("source", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{kind} 操作缺少 source。");
                source = await ResolveLocationAsync(sourceElement, selectedGameId, requireExisting: true, ct);
                EnsureReadableLocation(source);
                if (source.Scope == AgentPathScope.External)
                    throw new InvalidOperationException("外部路径不能作为写入批次的数据源；请先由用户手工上传文件。");
                if (string.Equals(source.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("文件操作的源和目标不能相同。");
                if (Directory.Exists(source.FullPath)
                    && target.FullPath.StartsWith(
                        source.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("目录不能复制或移动到自身的子目录中。");
                }
            }
            else if (kind == "copy_attachment")
            {
                attachment = attachmentStore.GetRequired(sessionId, GetRequiredString(item, "attachmentId"));
            }
            else if (kind == "write_text")
            {
                var content = GetOptionalStringPreserveWhitespace(item, "content") ?? string.Empty;
                if (content.Length > 64_000)
                    throw new InvalidDataException("单次文本写入不能超过 64,000 个字符。");
            }
            else if (kind is not ("create_directory" or "delete"))
            {
                throw new InvalidDataException("文件操作只支持 create_directory、write_text、copy、move、delete、copy_attachment。");
            }

            var overwrite = GetOptionalBool(item, "overwrite") ?? false;
            var recursive = GetOptionalBool(item, "recursive") ?? false;
            var contentValue = GetOptionalStringPreserveWhitespace(item, "content");
            var preview = kind switch
            {
                "create_directory" => $"创建目录 {target.FullPath}",
                "write_text" => $"{(File.Exists(target.FullPath) ? "覆盖" : "创建")}文本文件 {target.FullPath}（{contentValue?.Length ?? 0} 字符）",
                "copy" => $"复制 {source!.FullPath} → {target.FullPath}{(overwrite ? "（允许覆盖文件）" : string.Empty)}",
                "move" => $"移动 {source!.FullPath} → {target.FullPath}{(overwrite ? "（允许覆盖文件）" : string.Empty)}",
                "delete" => $"删除 {(Directory.Exists(target.FullPath) ? "目录" : "文件")} {target.FullPath}{(recursive ? "（递归）" : string.Empty)}",
                "copy_attachment" => $"复制附件 {attachment!.FileName} → {target.FullPath}{(overwrite ? "（允许覆盖文件）" : string.Empty)}",
                _ => kind
            };
            result.Add(new FileOperation(kind, target, source, attachment, contentValue, overwrite, recursive, preview));
        }
        return result;
    }

    private static async Task ExecuteOperationAsync(FileOperation operation, CancellationToken ct)
    {
        switch (operation.Kind)
        {
            case "create_directory":
                Directory.CreateDirectory(operation.Target.FullPath);
                return;
            case "write_text":
                Directory.CreateDirectory(Path.GetDirectoryName(operation.Target.FullPath)!);
                await WriteTextAtomicAsync(operation.Target.FullPath, operation.Content ?? string.Empty, ct);
                return;
            case "copy_attachment":
                Directory.CreateDirectory(Path.GetDirectoryName(operation.Target.FullPath)!);
                File.Copy(operation.Attachment!.FullPath, operation.Target.FullPath, operation.Overwrite);
                return;
            case "copy":
                CopyPath(operation.Source!.FullPath, operation.Target.FullPath, operation.Overwrite);
                return;
            case "move":
                MovePath(operation.Source!.FullPath, operation.Target.FullPath, operation.Overwrite);
                return;
            case "delete":
                DeletePath(operation.Target.FullPath, operation.Recursive);
                return;
        }
    }

    private static void CopyPath(string source, string target, bool overwrite)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite);
            return;
        }
        if (!Directory.Exists(source))
            throw new FileNotFoundException("复制源不存在。", source);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("目录复制目标已经存在。");
        CopyDirectory(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        var attributes = File.GetAttributes(source);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("不能递归复制重解析点。");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("目录包含重解析点，复制已停止。");
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static void MovePath(string source, string target, bool overwrite)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, overwrite);
            return;
        }
        if (!Directory.Exists(source))
            throw new FileNotFoundException("移动源不存在。", source);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("目录移动目标已经存在。");
        Directory.Move(source, target);
    }

    private static void DeletePath(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }
        if (!Directory.Exists(path))
            throw new FileNotFoundException("删除目标不存在。", path);
        var isReparse = File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        Directory.Delete(path, recursive && !isReparse);
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temporary = path + $".agent-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task<ToolboxAgentToolResult> ReadTextAsync(
        string path,
        JsonElement arguments,
        CancellationToken ct)
    {
        var startLine = Math.Max(1, GetOptionalInt(arguments, "startLine") ?? 1);
        var maxLines = Math.Clamp(GetOptionalInt(arguments, "maxLines") ?? 200, 1, MaxTextLines);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        var lineNumber = 0;
        var characters = 0;
        var truncated = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            if (lineNumber < startLine)
                continue;
            if (lines.Count >= maxLines || characters + line.Length > MaxTextCharacters)
            {
                truncated = true;
                break;
            }
            lines.Add($"{lineNumber:D6}: {line}");
            characters += line.Length;
        }
        var content = string.Join('\n', lines);
        return Success("读取文件原文", new
        {
            path,
            startLine,
            endLine = lines.Count == 0 ? startLine - 1 : startLine + lines.Count - 1,
            truncated,
            content
        }, $"已读取 {lines.Count} 行文件原文。 ");
    }

    private static async Task<ToolboxAgentToolResult> ReadHexAsync(
        string path,
        JsonElement arguments,
        CancellationToken ct)
    {
        var offset = Math.Max(0, GetOptionalLong(arguments, "offset") ?? 0);
        var length = Math.Clamp(GetOptionalInt(arguments, "length") ?? 4096, 1, MaxHexBytes);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        if (offset > stream.Length)
            throw new InvalidDataException("十六进制读取偏移超过文件长度。");
        stream.Position = offset;
        var buffer = new byte[Math.Min(length, (int)Math.Min(int.MaxValue, stream.Length - offset))];
        var read = await stream.ReadAsync(buffer, ct);
        var hex = Convert.ToHexString(buffer.AsSpan(0, read));
        return Success("读取二进制十六进制块", new
        {
            path,
            offset,
            bytesRead = read,
            hasMore = offset + read < stream.Length,
            hex
        }, $"已读取 {read} 字节十六进制内容。 ");
    }

    private static async Task<ToolboxAgentToolResult> ReadMetadataAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var hash = await ComputeSha256Async(path, ct);
        string? signer = null;
        try
        {
#pragma warning disable SYSLIB0057 // Authenticode extraction has no X509CertificateLoader equivalent.
            var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            signer = new X509Certificate2(certificate).Subject;
        }
        catch (CryptographicException) { }

        var assembly = await TryReadAssemblyMetadataAsync(path, ct);
        var archive = await TryReadArchiveMetadataAsync(path, ct);
        return Success("读取文件被动元数据", new
        {
            path,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            sha256 = hash,
            signer,
            assembly,
            archive
        }, "已读取文件哈希和可用的被动元数据。 ");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task<object?> TryReadAssemblyMetadataAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata)
                return null;
            var metadata = pe.GetMetadataReader();
            var definition = metadata.GetAssemblyDefinition();
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetAssemblyReference(handle))
                .Select(reference => metadata.GetString(reference.Name))
                .Take(200)
                .ToList();
            return new
            {
                name = metadata.GetString(definition.Name),
                version = definition.Version.ToString(),
                references
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<object?> TryReadArchiveMetadataAsync(string path, CancellationToken ct)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entries = archive.Entries.Take(200).Select(entry => new
            {
                entry.FullName,
                entry.Length,
                entry.CompressedLength,
                entry.LastWriteTime
            }).ToList();
            return new { entries, truncated = archive.Entries.Count > entries.Count };
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<bool> IsProbablyTextFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        var buffer = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
        var read = await stream.ReadAsync(buffer, ct);
        if (read == 0)
            return true;
        var suspicious = 0;
        for (var index = 0; index < read; index++)
        {
            var value = buffer[index];
            if (value == 0)
                return false;
            if (value < 0x09 || value is > 0x0D and < 0x20)
                suspicious++;
        }
        return suspicious <= Math.Max(2, read / 100);
    }

    private static async Task<(string Content, bool Truncated)> ReadLimitedAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken ct)
    {
        var builder = new StringBuilder(Math.Min(maxCharacters, 4096));
        var buffer = new char[4096];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = maxCharacters - builder.Length;
            if (remaining > 0)
                builder.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
                truncated = true;
        }
        return (builder.ToString(), truncated);
    }

    private static string GetPurpose(JsonElement element)
    {
        var purpose = GetRequiredString(element, "purpose");
        if (purpose.Length > 500)
            throw new InvalidDataException("用途说明不能超过 500 个字符。");
        return purpose;
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetOptionalString(element, propertyName)
        ?? throw new InvalidDataException($"工具参数缺少 {propertyName}。");

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
    }

    private static string? GetOptionalStringPreserveWhitespace(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static int? GetOptionalInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? GetOptionalLong(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.TryGetInt64(out var result)
            ? result
            : null;

    private static bool? GetOptionalBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase);

    private static string UserFacingError(Exception ex) => ex switch
    {
        InvalidDataException or InvalidOperationException or FileNotFoundException
            or DirectoryNotFoundException or UnauthorizedAccessException or IOException => ex.Message,
        _ => "文件操作失败，请查看工具箱日志。"
    };

    private static ToolboxAgentToolResult Success(string description, object payload, string userMessage) =>
        new(true, description, Trim(JsonSerializer.Serialize(payload, FileHelper.DataJsonOptions)), userMessage);

    private static ToolboxAgentToolResult Confirmation(string description, string message) =>
        new(false, description, message, message, RequiresConfirmation: true);

    private static string Trim(string value) =>
        value.Length <= MaxTextCharacters ? value : value[..MaxTextCharacters] + "...";

    private enum AgentPathScope { Game, Toolbox, External }

    private sealed record AgentPathLocation(AgentPathScope Scope, string Root, string FullPath);

    private sealed record FileOperation(
        string Kind,
        AgentPathLocation Target,
        AgentPathLocation? Source,
        StoredToolboxAgentAttachment? Attachment,
        string? Content,
        bool Overwrite,
        bool Recursive,
        string Preview);
}
