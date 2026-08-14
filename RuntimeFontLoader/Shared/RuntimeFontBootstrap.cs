using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;

namespace XUnityToolkit.RuntimeFontLoader
{
    internal static class RuntimeFontBootstrap
    {
        internal const string PluginId = "com.xunitytoolkit.runtimefontloader";
        internal const string PluginName = "XUnityToolkit Runtime Font Loader";
        internal const string PluginVersion = "1.0.0";
        internal const string XUnityPluginId = "gravydevsupreme.xunity.autotranslator";
        internal const string OverrideSentinel = "XUnityToolkit.RuntimeFont";
        private const string ConfigFileName = PluginId + ".cfg";
        private const string StatusFileName = PluginId + ".status.json";
        private const string ProbeText = "中文测试，。！？ABC123";

        private static readonly object Gate = new object();
        private static object _activeFont;
        private static DynamicMethod _overridePrefix;
        private static bool _initialized;

        internal static void Initialize(
            string gameRoot,
            string configDirectory,
            Action<string> info,
            Action<string> warning,
            Action<string> error)
        {
            lock (Gate)
            {
                if (_initialized)
                    return;
                _initialized = true;
            }

            var status = new RuntimeFontStatus();
            var statusPath = Path.Combine(configDirectory, StatusFileName);
            try
            {
                var config = RuntimeFontConfig.Load(Path.Combine(configDirectory, ConfigFileName));
                status.SourceId = config.SourceId;
                status.RequestedApplicationMode = config.ApplicationMode;
                status.RequestedLoader = "direct-ttf";

                if (!config.Enabled)
                {
                    status.ActiveLoader = "none";
                    status.ActiveApplicationMode = "disabled";
                    status.Message = "运行时字体加载已禁用。";
                    WriteStatus(statusPath, status);
                    return;
                }

                object font = null;
                string directError = null;
                string resolvedSourcePath;
                if (RuntimeFontFileValidator.TryResolveManagedFontPath(gameRoot, config.FontPath, config.SourceSha256,
                    out resolvedSourcePath, out directError))
                {
                    font = TryCreateRuntimeFont(resolvedSourcePath, status, out directError);
                }

                if (font != null)
                {
                    status.ActiveLoader = "direct-ttf";
                    status.ProbeSucceeded = true;
                    info("已从 TTF/OTF 文件创建动态 TMP 字体。" );
                }
                else if (config.AllowLegacyFallback && !IsBlank(config.LegacyFallbackPath))
                {
                    string legacyError;
                    font = TryLoadLegacyFont(gameRoot, config.LegacyFallbackPath, out legacyError);
                    status.LegacyFallbackUsed = font != null;
                    status.ActiveLoader = font != null ? "legacy-bundle" : "none";
                    status.Message = font != null
                        ? "运行时 TTF 不可用，已自动回退到兼容 TMP 资产。"
                        : "运行时 TTF 与兼容 TMP 资产均加载失败。";
                    directError = JoinErrors(directError, legacyError);
                    if (font != null)
                        warning(status.Message);
                }
                else
                {
                    status.ActiveLoader = "none";
                }

                if (font == null)
                    throw new InvalidOperationException(directError ?? "没有可用的 TMP 字体。" );

                _activeFont = font;
                TryKeepAlive(font);
                string fallbackError;
                if (!TryRegisterGlobalFallback(font, out fallbackError))
                    throw new InvalidOperationException(fallbackError);

                status.ActiveApplicationMode = "fallback";
                if (string.Equals(config.ApplicationMode, "override", StringComparison.OrdinalIgnoreCase))
                {
                    string adapterError;
                    status.OverrideAdapterAvailable = TryInstallOverrideAdapter(out adapterError);
                    if (status.OverrideAdapterAvailable)
                    {
                        status.ActiveApplicationMode = "override";
                    }
                    else
                    {
                        status.Message = "全局替换适配不可用，已降级为回退字体。";
                        directError = JoinErrors(directError, adapterError);
                        warning(status.Message);
                    }
                }

                status.Success = true;
                status.Error = directError;
                if (IsBlank(status.Message))
                    status.Message = status.ActiveApplicationMode == "override"
                        ? "运行时字体已作为 TMP 全局替换启用。"
                        : "运行时字体已作为 TMP 全局回退启用。";
                WriteStatus(statusPath, status);
                info(status.Message);
            }
            catch (Exception ex)
            {
                status.Success = false;
                status.ActiveLoader = status.ActiveLoader ?? "none";
                status.ActiveApplicationMode = status.ActiveApplicationMode ?? "none";
                status.Error = ex.GetBaseException().Message;
                status.Message = "运行时 TMP 字体加载失败。";
                WriteStatus(statusPath, status);
                error(status.Message + " " + status.Error);
            }
        }

        private static object TryCreateRuntimeFont(string path, RuntimeFontStatus status, out string error)
        {
            error = null;
            try
            {
                var fontAssetType = FindType("TMPro.TMP_FontAsset");
                if (fontAssetType == null)
                {
                    error = "游戏未加载 TMPro.TMP_FontAsset。";
                    return null;
                }

                var createMethod = fontAssetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        var parameters = method.GetParameters();
                        return method.Name == "CreateFontAsset"
                            && parameters.Length == 7
                            && parameters[0].ParameterType == typeof(string)
                            && parameters[1].ParameterType == typeof(int)
                            && parameters[2].ParameterType == typeof(int)
                            && parameters[3].ParameterType == typeof(int)
                            && parameters[5].ParameterType == typeof(int)
                            && parameters[6].ParameterType == typeof(int);
                    });
                status.DirectTtfSupported = createMethod != null;
                if (createMethod == null)
                {
                    error = "TMP 不提供字体文件路径版 CreateFontAsset。";
                    return null;
                }

                var renderModeType = createMethod.GetParameters()[4].ParameterType;
                object renderMode;
                try
                {
                    renderMode = Enum.Parse(renderModeType, "SDFAA", true);
                }
                catch
                {
                    var name = Enum.GetNames(renderModeType).FirstOrDefault(value =>
                        value.IndexOf("SDFAA", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (name == null)
                        throw new MissingMemberException("GlyphRenderMode.SDFAA 不可用。" );
                    renderMode = Enum.Parse(renderModeType, name);
                }

                var font = createMethod.Invoke(null, new[] { (object)path, 0, 90, 9, renderMode, 1024, 1024 });
                if (font == null)
                {
                    error = "CreateFontAsset 返回空字体。";
                    return null;
                }

                var multiAtlas = fontAssetType.GetProperty("isMultiAtlasTexturesEnabled",
                    BindingFlags.Public | BindingFlags.Instance);
                if (multiAtlas != null && multiAtlas.CanWrite)
                    multiAtlas.SetValue(font, true, null);

                if (!ProbeFont(fontAssetType, font, out error))
                    return null;
                return font;
            }
            catch (Exception ex)
            {
                error = "直接创建 TMP 字体失败: " + ex.GetBaseException().Message;
                return null;
            }
        }

        private static bool ProbeFont(Type fontAssetType, object font, out string error)
        {
            error = null;
            var tryAdd = fontAssetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == "TryAddCharacters"
                        && method.ReturnType == typeof(bool)
                        && parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType.IsByRef;
                });
            if (tryAdd != null)
            {
                var args = new object[] { ProbeText, null };
                if ((bool)tryAdd.Invoke(font, args))
                    return true;
                error = "动态字体未能加入中文探针字符。";
                return false;
            }

            var hasCharacters = fontAssetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == "HasCharacters" && method.ReturnType == typeof(bool)
                        && parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
                });
            if (hasCharacters != null && (bool)hasCharacters.Invoke(font, new object[] { ProbeText }))
                return true;

            error = "TMP 字体不支持中文探针验证。";
            return false;
        }

        private static object TryLoadLegacyFont(string gameRoot, string configuredPath, out string error)
        {
            error = null;
            try
            {
                var relativePath = configuredPath.Replace('\\', '/').TrimStart('/');
                var fullPath = Path.GetFullPath(Path.Combine(gameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                var fontRoot = Path.GetFullPath(Path.Combine(Path.Combine(gameRoot, "BepInEx"), "Font"));
                var prefix = fontRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)
                    || RuntimeFontFileValidator.ContainsReparsePoint(fontRoot, fullPath))
                {
                    error = "兼容 TMP 资产路径无效。";
                    return null;
                }

                var helperType = FindType("XUnity.AutoTranslator.Plugin.Core.Fonts.FontHelper");
                var helperMethod = helperType == null ? null : helperType.GetMethod("GetTextMeshProFont",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (helperMethod == null)
                {
                    error = "XUnity 兼容字体加载入口不可用。";
                    return null;
                }
                var result = helperMethod.Invoke(null, new object[] { relativePath });
                if (result == null)
                    error = "XUnity 未能从兼容资产中读取 TMP 字体。";
                return result;
            }
            catch (Exception ex)
            {
                error = "兼容 TMP 资产加载失败: " + ex.GetBaseException().Message;
                return null;
            }
        }

        private static void TryKeepAlive(object font)
        {
            try
            {
                var unityObjectType = FindType("UnityEngine.Object");
                var method = unityObjectType == null ? null : unityObjectType.GetMethod("DontDestroyOnLoad",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { unityObjectType }, null);
                if (method != null && unityObjectType.IsInstanceOfType(font))
                    method.Invoke(null, new[] { font });
            }
            catch
            {
                // The global fallback list retains the font even when this optional call is unavailable.
            }
        }

        private static bool TryRegisterGlobalFallback(object font, out string error)
        {
            error = null;
            try
            {
                var settingsType = FindType("TMPro.TMP_Settings");
                if (settingsType == null)
                {
                    error = "游戏未加载 TMPro.TMP_Settings。";
                    return false;
                }
                var property = settingsType.GetProperty("fallbackFontAssets",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property == null)
                {
                    error = "TMP 全局回退字体列表不可用。";
                    return false;
                }
                var list = property.GetValue(null, null);
                if (list == null)
                {
                    var loadSettings = settingsType.GetMethod("LoadDefaultSettings",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (loadSettings != null)
                        loadSettings.Invoke(null, null);
                    list = property.GetValue(null, null);
                }
                if (list == null)
                {
                    error = "TMP 全局回退字体列表为空。";
                    return false;
                }

                var contains = list.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "Contains" && method.GetParameters().Length == 1);
                if (contains != null && (bool)contains.Invoke(list, new[] { font }))
                    return true;
                var add = list.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "Add" && method.GetParameters().Length == 1);
                if (add == null)
                {
                    error = "TMP 全局回退字体列表不支持添加。";
                    return false;
                }
                add.Invoke(list, new[] { font });
                return true;
            }
            catch (Exception ex)
            {
                error = "注册 TMP 全局回退字体失败: " + ex.GetBaseException().Message;
                return false;
            }
        }

        private static bool TryInstallOverrideAdapter(out string error)
        {
            error = null;
            try
            {
                var helperType = FindType("XUnity.AutoTranslator.Plugin.Core.Fonts.FontHelper");
                var target = helperType == null ? null : helperType.GetMethod("GetTextMeshProFont",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (target == null)
                {
                    error = "XUnity FontHelper.GetTextMeshProFont 适配点不存在。";
                    return false;
                }

                var prefix = new DynamicMethod(
                    "XUnityToolkitRuntimeFontPrefix",
                    typeof(bool),
                    new[] { typeof(string), target.ReturnType.MakeByRefType() },
                    typeof(RuntimeFontBootstrap),
                    true);
                var il = prefix.GetILGenerator();
                var continueOriginal = il.DefineLabel();
                var fontLocal = il.DeclareLocal(typeof(object));

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, OverrideSentinel);
                il.Emit(OpCodes.Call, typeof(string).GetMethod("Equals", new[] { typeof(string), typeof(string) }));
                il.Emit(OpCodes.Brfalse_S, continueOriginal);
                il.Emit(OpCodes.Call, typeof(RuntimeFontBootstrap).GetMethod("GetActiveOverrideFont",
                    BindingFlags.NonPublic | BindingFlags.Static));
                il.Emit(OpCodes.Stloc, fontLocal);
                il.Emit(OpCodes.Ldloc, fontLocal);
                il.Emit(OpCodes.Brfalse_S, continueOriginal);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, fontLocal);
                il.Emit(OpCodes.Castclass, target.ReturnType);
                il.Emit(OpCodes.Stind_Ref);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(continueOriginal);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);

                _overridePrefix = prefix;
                new Harmony(PluginId).Patch(target, new HarmonyMethod(prefix));
                return true;
            }
            catch (Exception ex)
            {
                error = "安装 XUnity 全局替换适配失败: " + ex.GetBaseException().Message;
                return false;
            }
        }

        private static object GetActiveOverrideFont()
        {
            return _activeFont;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore dynamic or partially loaded assemblies.
                }
            }
            return null;
        }

        private static string JoinErrors(string first, string second)
        {
            if (IsBlank(first)) return second;
            if (IsBlank(second)) return first;
            return first + " " + second;
        }

        private static bool IsBlank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }

        private static void WriteStatus(string path, RuntimeFontStatus status)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = status.ToJson();
                var temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporaryPath, path);
            }
            catch
            {
                // Status reporting must never crash the game plugin.
            }
        }
    }

    internal sealed class RuntimeFontConfig
    {
        internal bool Enabled;
        internal string SourceId = "ttf-default";
        internal string FontPath = string.Empty;
        internal string SourceSha256 = string.Empty;
        internal string LegacyFallbackPath = string.Empty;
        internal string ApplicationMode = "fallback";
        internal bool AllowLegacyFallback = true;

        internal static RuntimeFontConfig Load(string path)
        {
            var config = new RuntimeFontConfig();
            if (!File.Exists(path))
                return config;
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                    continue;
                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;
                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.Enabled = ParseBoolean(value, false); break;
                    case "sourceid": config.SourceId = value; break;
                    case "fontpath": config.FontPath = value; break;
                    case "sourcesha256": config.SourceSha256 = value; break;
                    case "legacyfallbackpath": config.LegacyFallbackPath = value; break;
                    case "applicationmode": config.ApplicationMode = value; break;
                    case "allowlegacyfallback": config.AllowLegacyFallback = ParseBoolean(value, true); break;
                }
            }
            if (!string.Equals(config.ApplicationMode, "override", StringComparison.OrdinalIgnoreCase))
                config.ApplicationMode = "fallback";
            return config;
        }

        private static bool ParseBoolean(string value, bool fallback)
        {
            bool result;
            return bool.TryParse(value, out result) ? result : fallback;
        }
    }

    internal sealed class RuntimeFontStatus
    {
        internal bool Success;
        internal string SourceId = "ttf-default";
        internal string RequestedLoader = "direct-ttf";
        internal string ActiveLoader;
        internal string RequestedApplicationMode = "fallback";
        internal string ActiveApplicationMode;
        internal bool DirectTtfSupported;
        internal bool ProbeSucceeded;
        internal bool LegacyFallbackUsed;
        internal bool OverrideAdapterAvailable;
        internal string Message;
        internal string Error;

        internal string ToJson()
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            Append(builder, "protocolVersion", "1", false, true);
            Append(builder, "pluginVersion", RuntimeFontBootstrap.PluginVersion, true, true);
            Append(builder, "generatedAtUtc", DateTime.UtcNow.ToString("O"), true, true);
            Append(builder, "success", Success ? "true" : "false", false, true);
            Append(builder, "sourceId", SourceId, true, true);
            Append(builder, "requestedLoader", RequestedLoader, true, true);
            Append(builder, "activeLoader", ActiveLoader ?? "none", true, true);
            Append(builder, "requestedApplicationMode", RequestedApplicationMode, true, true);
            Append(builder, "activeApplicationMode", ActiveApplicationMode ?? "none", true, true);
            Append(builder, "directTtfSupported", DirectTtfSupported ? "true" : "false", false, true);
            Append(builder, "probeSucceeded", ProbeSucceeded ? "true" : "false", false, true);
            Append(builder, "legacyFallbackUsed", LegacyFallbackUsed ? "true" : "false", false, true);
            Append(builder, "overrideAdapterAvailable", OverrideAdapterAvailable ? "true" : "false", false, true);
            Append(builder, "message", Message, true, true);
            Append(builder, "error", Error, true, false);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string name, string value, bool quote, bool comma)
        {
            builder.Append("  \"").Append(Escape(name)).Append("\": ");
            if (value == null)
                builder.Append("null");
            else if (quote)
                builder.Append('"').Append(Escape(value)).Append('"');
            else
                builder.Append(value);
            if (comma) builder.Append(',');
            builder.AppendLine();
        }

        private static string Escape(string value)
        {
            if (value == null) return null;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
