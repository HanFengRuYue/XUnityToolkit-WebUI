using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class BundledFontCatalog(BundledAssetPaths bundledPaths)
{
    public const string DefaultSourceId = "ttf-default";
    public const string DisplayName = "思源黑体 CN 2.005R";
    public const string Version = "2.005R";
    public const string LicenseName = "SIL Open Font License 1.1";
    public const string LicenseUrl = "https://github.com/adobe-fonts/source-han-sans/blob/2.005R/LICENSE.txt";
    public const string RuntimeFileName = "SourceHanSansCN-VF.ttf";
    public const string AssetReplacementFileName = "SourceHanSansCN-Regular.otf";
    public const string RuntimeSha256 = "25A01E41B5CC99893EB35A6CD2CC7611841DC19EB03CBAF7F0C1DE8210F2BA0B";
    public const string AssetReplacementSha256 = "E2BC8A2E7F37474B774FFF8DB758681ECE40BB6947A90D571BCE9DD60671A8E4";

    public string RuntimeFontPath => Path.Combine(bundledPaths.FontsDirectory, RuntimeFileName);
    public string AssetReplacementFontPath => Path.Combine(bundledPaths.FontsDirectory, AssetReplacementFileName);
    public string LicensePath => Path.Combine(bundledPaths.FontsDirectory, "licenses", "SourceHanSans-OFL-1.1.txt");

    public ReplacementSource CreateDefaultSource()
    {
        var path = RuntimeFontPath;
        return new ReplacementSource
        {
            Id = DefaultSourceId,
            Kind = "TTF",
            DisplayName = DisplayName,
            FileName = RuntimeFileName,
            Origin = "default",
            IsDefault = true,
            FileSize = File.Exists(path) ? new FileInfo(path).Length : 0,
            Version = Version,
            License = LicenseName,
            LicenseUrl = LicenseUrl,
            Sha256 = RuntimeSha256,
        };
    }
}
