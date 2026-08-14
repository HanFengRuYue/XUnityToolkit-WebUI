using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class BepInExLogService
{
    private const int DefaultReadLines = 5000;

    public static string GetLogPath(Game game) =>
        Path.Combine(game.GamePath, "BepInEx", "LogOutput.log");

    public async Task<BepInExLogResponse> ReadLogAsync(Game game, int? tailLines = DefaultReadLines)
    {
        var logPath = GetLogPath(game);
        if (!File.Exists(logPath))
            throw new FileNotFoundException("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。", logPath);

        var info = new FileInfo(logPath);
        var content = tailLines is > 0
            ? await ReadTailAsync(logPath, tailLines.Value)
            : await ReadAllAsync(logPath);
        return new BepInExLogResponse(content, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>
    /// Compatibility adapter for the legacy log-analysis endpoint. The Markdown is generated from
    /// the unified structured diagnostic report and never invokes a second prompt.
    /// </summary>
    public static BepInExLogAnalysis FormatCompatibilityAnalysis(PluginHealthReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 总体状态");
        sb.AppendLine(StatusLabel(report.Overall));
        sb.AppendLine();
        sb.AppendLine("## 本地客观检查");
        foreach (var check in report.Checks)
            sb.AppendLine($"- **{check.Label}**：{StatusLabel(check.Status)}{(string.IsNullOrWhiteSpace(check.Detail) ? string.Empty : $" — {check.Detail}")}");

        sb.AppendLine();
        sb.AppendLine("## AI 智能诊断");
        if (report.Analysis is null)
        {
            sb.AppendLine(report.AnalysisMessage ?? $"当前状态：{report.AnalysisState}");
        }
        else
        {
            sb.AppendLine(report.Analysis.Summary);
            foreach (var finding in report.Analysis.Findings)
            {
                sb.AppendLine();
                sb.AppendLine($"### [{finding.Severity}] {finding.Title}");
                sb.AppendLine(finding.Explanation);
                foreach (var evidence in finding.Evidence)
                    sb.AppendLine($"- 证据：{evidence.Label} 资料第 {evidence.StartLine}-{evidence.EndLine} 行 — `{evidence.Excerpt.Replace('`', '\'')}`");
                foreach (var action in finding.SuggestedActions)
                    sb.AppendLine($"- 建议：{action}");
            }
        }

        return new BepInExLogAnalysis(
            sb.ToString().TrimEnd(),
            report.Analysis?.EndpointName ?? "未运行",
            report.Analysis?.AnalyzedAt ?? report.CheckedAt);
    }

    private static string StatusLabel(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "健康",
        HealthStatus.Warning => "需关注",
        HealthStatus.Error => "错误",
        _ => "未知"
    };

    private static async Task<string> ReadAllAsync(string logPath)
    {
        await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> ReadTailAsync(string logPath, int maxLines)
    {
        const int blockSize = 16 * 1024;
        const int maxTailBytes = 8 * 1024 * 1024;
        var buffer = new byte[blockSize];
        var chunks = new List<byte[]>();
        var newlineCount = 0;
        var bytesReadTotal = 0;

        await using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete, blockSize, useAsync: true))
        {
            var position = stream.Length;
            while (position > 0 && newlineCount <= maxLines && bytesReadTotal < maxTailBytes)
            {
                var toRead = (int)Math.Min(blockSize, position);
                position -= toRead;
                stream.Seek(position, SeekOrigin.Begin);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                if (read <= 0)
                    break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                chunks.Add(chunk);
                bytesReadTotal += read;
                newlineCount += chunk.Count(static value => value == (byte)'\n');
            }
        }

        if (chunks.Count == 0)
            return string.Empty;

        chunks.Reverse();
        var tail = new byte[chunks.Sum(static chunk => chunk.Length)];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, tail, offset, chunk.Length);
            offset += chunk.Length;
        }

        var lines = Encoding.UTF8.GetString(tail)
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        return lines.Length <= maxLines
            ? string.Join('\n', lines)
            : string.Join('\n', lines[^maxLines..]);
    }
}
