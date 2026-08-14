namespace XUnityToolkit_WebUI.Models;

public sealed class InstallOptions
{
    public bool AutoInstallTmpFont { get; set; } = true;
    public string TmpFontApplicationMode { get; set; } = "fallback";
    public bool AutoDeployAiEndpoint { get; set; } = true;
    public bool AutoGenerateConfig { get; set; } = true;
    public bool AutoApplyOptimalConfig { get; set; } = true;
    public bool AutoVerifyHealth { get; set; } = true;
}
