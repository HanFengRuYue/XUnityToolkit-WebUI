using System.Text.Json.Serialization;

namespace XUnityToolkit_WebUI.Models;

public enum GpuBackend { CUDA, Vulkan, CPU }

public enum LocalLlmServerState { Idle, Starting, Running, Stopping, Failed }

public sealed class LocalLlmSettings
{
    /// <summary>GPU layers to offload. -1 = all layers (full GPU offload).</summary>
    public int GpuLayers { get; set; } = -1;

    /// <summary>Context window size for llama-server.</summary>
    public int ContextLength { get; set; } = 4096;

    /// <summary>Absolute path of the currently loaded/selected model GGUF file.</summary>
    public string? LoadedModelPath { get; set; }

    /// <summary>Stable ID of the auto-registered ApiEndpointConfig in AiTranslationSettings.</summary>
    public string EndpointId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>User's model inventory (downloaded + manually added).</summary>
    public List<LocalModelEntry> Models { get; set; } = [];

    /// <summary>Downloads that were paused and can be resumed.</summary>
    public List<PausedDownload> PausedDownloads { get; set; } = [];
}

public sealed class PausedDownload
{
    public string CatalogId { get; set; } = "";
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}

public sealed class LocalModelEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }

    /// <summary>True if downloaded from built-in catalog; false if user-added path.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Links back to BuiltInModelCatalog entry ID.</summary>
    public string? CatalogId { get; set; }

    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

public sealed record GpuInfo(
    string Name,
    string Vendor,
    long VramBytes,
    GpuBackend RecommendedBackend);

public sealed record LocalLlmStatus(
    LocalLlmServerState State,
    string? LoadedModelPath,
    string? LoadedModelName,
    string? GpuBackendName,
    string? Error,
    int? GpuUtilizationPercent = null,
    int? GpuVramUsedMb = null,
    int? GpuVramTotalMb = null);

public sealed record LlamaBackendInfo(
    string Backend,
    bool IsInstalled,
    string LlamaVersion);

public sealed record LlamaStatus(
    string BundledVersion,
    IReadOnlyList<LlamaBackendInfo> Backends,
    GpuBackend RecommendedBackend);

public sealed record BuiltInModelInfo(
    string Id,
    string Name,
    string Description,
    long FileSizeBytes,
    int RecommendedVramGb,
    string HuggingFaceRepo,
    string HuggingFaceFile,
    string[] Tags);

public sealed record LocalLlmDownloadProgress(
    string CatalogId,
    long BytesDownloaded,
    long TotalBytes,
    long SpeedBytesPerSec,
    bool Done,
    string? Error,
    bool Paused = false,
    bool UseMirror = false);

/// <summary>
/// Static catalog of recommended models. Updated with app releases, not stored on disk.
/// </summary>
public static class BuiltInModelCatalog
{
    public static readonly IReadOnlyList<BuiltInModelInfo> Models =
    [
        new("murasaki-8b-q4",
            "Murasaki 8B Q4_K_M",
            "基于 SakuraLLM+Qwen3 的 ACGN 翻译模型，8B 参数轻量版，适合 6-8GB 显存",
            (long)(4.9 * 1024 * 1024 * 1024),
            6,
            "Murasaki-Project/Murasaki-8B-v0.2-GGUF",
            "Murasaki-8B-v0.2-Q4_K_M.gguf",
            ["ACGN", "日→中", "轻量"]),

        new("murasaki-14b-q4",
            "Murasaki 14B Q4_K_M",
            "ACGN 翻译 Benchmark 排名第一，14B 参数，适合 10-12GB 显存",
            (long)(8.7 * 1024 * 1024 * 1024),
            10,
            "Murasaki-Project/Murasaki-14B-v0.2-GGUF",
            "Murasaki-14B-v0.2-Q4_K_M.gguf",
            ["ACGN", "日→中", "推荐"]),

        new("qwen3.5-4b-q4",
            "Qwen3.5 4B Q4_K_M",
            "通义千问 3.5 轻量版，通用多语言翻译，适合 4-6GB 显存",
            (long)(2.7 * 1024 * 1024 * 1024),
            4,
            "unsloth/Qwen3.5-4B-GGUF",
            "Qwen3.5-4B-Q4_K_M.gguf",
            ["通用", "多语言", "轻量"]),

        new("qwen3.5-9b-q4",
            "Qwen3.5 9B Q4_K_M",
            "通义千问 3.5 中等版，201 种语言支持，适合 8-10GB 显存",
            (long)(5.6 * 1024 * 1024 * 1024),
            8,
            "unsloth/Qwen3.5-9B-GGUF",
            "Qwen3.5-9B-Q4_K_M.gguf",
            ["通用", "多语言"]),

        new("qwen3.5-27b-q4",
            "Qwen3.5 27B Q4_K_M",
            "通义千问 3.5 大型版，翻译质量最佳，需 20GB+ 显存",
            (long)(16.7 * 1024 * 1024 * 1024),
            20,
            "unsloth/Qwen3.5-27B-GGUF",
            "Qwen3.5-27B-Q4_K_M.gguf",
            ["通用", "多语言", "高质量"]),
    ];
}
