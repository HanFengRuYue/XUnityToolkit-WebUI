namespace UnityLocalizationToolkit_WebUI.Models;

public enum ManualTranslationAssetStorageKind
{
    LooseAssetFile,
    BundleEntry,
    ManagedAssembly,
    ManagedResource,
    Il2CppMetadata,
    NativeBinary
}

public enum ManualTranslationAssetValueKind
{
    Text,
    Image,
    Font,
    Binary,
    Code
}

public enum ManualTranslationAssetFilterValueKind
{
    All,
    Text,
    Code,
    Image,
    Font,
    Binary
}

public enum ManualTranslationOverrideKind
{
    Text,
    Image
}

public enum ManualTranslationImagePreviewSource
{
    Original,
    Override
}

public enum ManualTranslationImagePreviewSize
{
    Thumb,
    Full
}

public sealed class ManualTranslationProjectManifest
{
    public required string GameId { get; init; }
    public string ToolkitVersion { get; set; } = "5.0";
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
    public int TotalAssets { get; set; }
    public int EditableAssets { get; set; }
    public int OverridesApplied { get; set; }
    public string State { get; set; } = "ready";
}

public sealed class ManualTranslationProjectIndex
{
    public required string GameId { get; init; }
    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;
    public List<ManualTranslationAssetEntry> Assets { get; init; } = [];
}

public sealed class ManualTranslationAssetEntry
{
    public required string AssetId { get; init; }
    public required ManualTranslationAssetStorageKind StorageKind { get; init; }
    public required ManualTranslationAssetValueKind ValueKind { get; init; }
    public required string ObjectType { get; init; }
    public required string AssetFile { get; init; }
    public string? BundleEntry { get; init; }
    public string? RelativePath { get; init; }
    public long? PathId { get; init; }
    public string? DisplayName { get; init; }
    public string? FieldPath { get; init; }
    public string? CodeLocation { get; init; }
    public string? Preview { get; set; }
    public string? OriginalText { get; set; }
    public string? ListTitle { get; init; }
    public string? ListSubtitle { get; init; }
    public string? ListMeta { get; init; }
    public string? IconKey { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool Editable { get; init; }
    public bool Exportable { get; init; } = true;
    public bool Overridden { get; set; }
    public DateTime? OverrideUpdatedAt { get; set; }
    public string? EditHint { get; init; }
}

public sealed class ManualTranslationStatus
{
    public required string GameId { get; init; }
    public bool HasScan { get; set; }
    public string State { get; set; } = "notScanned";
    public int AssetCount { get; set; }
    public int EditableAssetCount { get; set; }
    public int OverrideCount { get; set; }
    public bool HasBackups { get; set; }
    public DateTime? ScannedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? LastPackagePath { get; set; }
    public ManualTranslationKindCounts KindCounts { get; set; } = new();
}

public sealed class ManualTranslationAssetListResponse
{
    public required string GameId { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<ManualTranslationAssetEntry> Assets { get; init; } = [];
}

public sealed class ManualTranslationKindCounts
{
    public int All { get; set; }
    public int Text { get; set; }
    public int Code { get; set; }
    public int Image { get; set; }
    public int Font { get; set; }
    public int Binary { get; set; }

    public static ManualTranslationKindCounts FromAssets(IEnumerable<ManualTranslationAssetEntry> assets)
    {
        var counts = new ManualTranslationKindCounts();
        foreach (var asset in assets)
        {
            counts.All++;
            switch (asset.ValueKind)
            {
                case ManualTranslationAssetValueKind.Text:
                    counts.Text++;
                    break;
                case ManualTranslationAssetValueKind.Code:
                    counts.Code++;
                    break;
                case ManualTranslationAssetValueKind.Image:
                    counts.Image++;
                    break;
                case ManualTranslationAssetValueKind.Font:
                    counts.Font++;
                    break;
                case ManualTranslationAssetValueKind.Binary:
                    counts.Binary++;
                    break;
            }
        }

        return counts;
    }
}

public sealed class ManualTranslationAssetContent
{
    public required string AssetId { get; init; }
    public required ManualTranslationAssetEntry Asset { get; init; }
    public string? OriginalText { get; init; }
    public string? OverrideText { get; init; }
    public string? EffectiveText { get; init; }
}

public sealed class ManualTranslationOverrideEntry
{
    public required string AssetId { get; init; }
    public ManualTranslationOverrideKind Kind { get; set; } = ManualTranslationOverrideKind.Text;
    public string? Value { get; set; }
    public string? ImageFileName { get; set; }
    public string? ContentType { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string Source { get; set; } = "manual";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ManualTranslationBackupManifest
{
    public required string GameId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<BackupEntry> BackedUpFiles { get; init; } = [];
}

public sealed class ManualTranslationApplyResult
{
    public int ModifiedFiles { get; set; }
    public int AppliedOverrides { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class ManualTranslationPackageManifest
{
    public required string GameId { get; init; }
    public string ToolkitVersion { get; set; } = "5.0";
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;
    public int OverrideCount { get; set; }
}

public sealed record ManualTranslationSaveOverrideRequest(string Value, string? Source);
public sealed record ManualTranslationImportPackageRequest(string ZipPath);
public sealed class ManualTranslationAssetsQuery
{
    public string? Search { get; init; }
    public bool? EditableOnly { get; init; }
    public bool? OverriddenOnly { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public ManualTranslationAssetFilterValueKind? ValueKind { get; init; }
}
