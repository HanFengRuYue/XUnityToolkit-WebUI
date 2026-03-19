using System.Diagnostics;

// Show visible window title so antivirus heuristics recognize this as a user-facing updater
try { Console.Title = "XUnityToolkit Updater"; } catch { /* no console */ }
Console.WriteLine("XUnityToolkit 正在更新，请勿关闭此窗口...");
Console.WriteLine();

// Parse CLI arguments
string? pidArg = null;
string? appDir = null;
string? stagingDir = null;
string? deleteListPath = null;
string? exeName = null;
string? dataDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pid":         pidArg = args[++i]; break;
        case "--app-dir":     appDir = args[++i]; break;
        case "--staging-dir": stagingDir = args[++i]; break;
        case "--delete-list": deleteListPath = args[++i]; break;
        case "--exe-name":    exeName = args[++i]; break;
        case "--data-dir":    dataDir = args[++i]; break;
    }
}

if (appDir is null || stagingDir is null || exeName is null)
{
    Console.Error.WriteLine("Usage: Updater --pid <pid> --app-dir <dir> --staging-dir <dir> --delete-list <file> --exe-name <name>");
    return 1;
}

// Default data directory: --data-dir or %AppData%\XUnityToolkit\
var effectiveDataDir = dataDir ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XUnityToolkit");

// Ensure log directory exists and open log file
string logDir = Path.Combine(effectiveDataDir, "update-temp");
Directory.CreateDirectory(logDir);
string logPath = Path.Combine(logDir, "updater.log");

using StreamWriter log = new StreamWriter(logPath, append: true) { AutoFlush = true };

void Log(string message)
{
    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
    log.WriteLine(line);
    Console.WriteLine(line);
}

Log("Updater started.");
Log($"  app-dir:      {appDir}");
Log($"  staging-dir:  {stagingDir}");
Log($"  delete-list:  {deleteListPath ?? "(none)"}");
Log($"  exe-name:     {exeName}");
Log($"  data-dir:     {effectiveDataDir}");

// Wait for main process to exit
if (pidArg is not null && int.TryParse(pidArg, out int pid))
{
    Log($"Waiting for process PID={pid} to exit (timeout 30s)...");
    var deadline = DateTime.UtcNow.AddSeconds(30);
    bool exited = false;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
            {
                exited = true;
                break;
            }
        }
        catch (ArgumentException)
        {
            // Process no longer exists — it has exited
            exited = true;
            break;
        }
        Thread.Sleep(200);
    }
    if (exited)
        Log("Main process has exited.");
    else
        Log("Warning: main process did not exit within 30s; proceeding anyway.");
}
else
{
    Log("No PID specified or invalid; skipping wait.");
}

// Collect staging files (relative paths)
if (!Directory.Exists(stagingDir))
{
    Log($"Error: staging directory does not exist: {stagingDir}");
    return 2;
}

var stagingFiles = Directory
    .EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories)
    .Select(f => Path.GetRelativePath(stagingDir, f))
    .ToList();

Log($"Staging contains {stagingFiles.Count} file(s).");

string backupDir = Path.Combine(effectiveDataDir, "update-backup");

// ── Phase 1: BACKUP ALL ──────────────────────────────────────────────────────
Log("Phase 1: Backing up existing files...");
foreach (string rel in stagingFiles)
{
    string src = Path.Combine(appDir, rel);
    if (!File.Exists(src))
    {
        Log($"  [SKIP] {rel} (new file, no backup needed)");
        continue;
    }
    string dst = Path.Combine(backupDir, rel);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
        Log($"  [BACKUP] {rel}");
    }
    catch (Exception ex)
    {
        Log($"  [ERROR] Backup failed for {rel}: {ex.Message}");
        Log("Aborting — no files have been modified.");
        return 3;
    }
}
Log("Phase 1 complete.");

// ── Phase 2: REPLACE ALL ─────────────────────────────────────────────────────
Log("Phase 2: Replacing files...");
int replaced = 0;
string? replaceError = null;
foreach (string rel in stagingFiles)
{
    string src = Path.Combine(stagingDir, rel);
    string dst = Path.Combine(appDir, rel);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
        replaced++;
        Log($"  [REPLACE] {rel}");
    }
    catch (Exception ex)
    {
        replaceError = $"Replace failed for '{rel}': {ex.Message}";
        Log($"  [ERROR] {replaceError}");
        break;
    }
}

if (replaceError is not null)
{
    Log("Phase 2 failed — initiating rollback.");
    Rollback(appDir, effectiveDataDir, backupDir, stagingFiles, replaceError, "replace", replaced, stagingFiles.Count - replaced, exeName, log);
    return 4;
}
Log("Phase 2 complete.");

// ── Phase 3: DELETE ──────────────────────────────────────────────────────────
Log("Phase 3: Processing delete list...");
if (deleteListPath is not null && File.Exists(deleteListPath))
{
    var toDelete = File.ReadAllLines(deleteListPath)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .ToList();

    Log($"  Delete list has {toDelete.Count} entry/entries.");
    string normalizedAppDir = Path.GetFullPath(appDir) + Path.DirectorySeparatorChar;
    string? deleteError = null;
    foreach (string rel in toDelete)
    {
        string target = Path.GetFullPath(Path.Combine(appDir, rel));
        if (!target.StartsWith(normalizedAppDir, StringComparison.OrdinalIgnoreCase))
        {
            Log($"  [SKIP] {rel} (rejected: path outside app directory)");
            continue;
        }
        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
                Log($"  [DELETE] {rel}");
            }
            else
            {
                Log($"  [SKIP] {rel} (not found)");
            }
        }
        catch (Exception ex)
        {
            deleteError = $"Delete failed for '{rel}': {ex.Message}";
            Log($"  [ERROR] {deleteError}");
            break;
        }
    }

    if (deleteError is not null)
    {
        Log("Phase 3 failed — initiating rollback.");
        Rollback(appDir, effectiveDataDir, backupDir, stagingFiles, deleteError, "delete", replaced, 0, exeName, log);
        return 5;
    }
}
else
{
    Log("  No delete list file; skipping.");
}
Log("Phase 3 complete.");

// ── MSI Registry Sync ────────────────────────────────────────────────────────
Log("Syncing MSI registry info...");
try
{
    SyncMsiRegistryVersion(Log);
}
catch (Exception ex)
{
    Log($"Warning: MSI registry sync failed (non-critical): {ex.Message}");
}

// ── Cleanup ───────────────────────────────────────────────────────────────────
Log("Cleanup: removing staging, backup, and temp directories...");
TryDeleteDirectory(Path.Combine(effectiveDataDir, "update-staging"), log);
TryDeleteDirectory(backupDir, log);
TryDeleteDirectory(logDir, log);  // This also removes the log dir; log writes after this are best-effort

// ── Launch ────────────────────────────────────────────────────────────────────
string exePath = Path.Combine(appDir, exeName);
Log($"Launching: {exePath}");
try
{
    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
}
catch (Exception ex)
{
    Log($"Warning: Failed to launch main app: {ex.Message}");
}

Log("Update complete. Exiting with code 0.");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

static void Rollback(
    string appDir,
    string dataDir,
    string backupDir,
    List<string> stagingFiles,
    string errorMessage,
    string phase,
    int filesReplaced,
    int filesRemaining,
    string exeName,
    StreamWriter log)
{
    void Log(string msg)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
        log.WriteLine(line);
        Console.WriteLine(line);
    }

    Log("ROLLBACK: Restoring backed-up files...");
    foreach (string rel in stagingFiles)
    {
        string backupSrc = Path.Combine(backupDir, rel);
        string appDst = Path.Combine(appDir, rel);
        if (!File.Exists(backupSrc))
        {
            Log($"  [ROLLBACK SKIP] {rel} (no backup exists — was new file)");
            continue;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(appDst)!);
            File.Copy(backupSrc, appDst, overwrite: true);
            Log($"  [ROLLBACK RESTORE] {rel}");
        }
        catch (Exception ex)
        {
            Log($"  [ROLLBACK ERROR] Could not restore {rel}: {ex.Message}");
        }
    }

    // Write error JSON (manual string — AOT safe, no reflection)
    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    string safeError = errorMessage.Replace("\\", "\\\\").Replace("\"", "\\\"");
    string errorJson =
        $"{{\n" +
        $"  \"version\": \"unknown\",\n" +
        $"  \"error\": \"{safeError}\",\n" +
        $"  \"timestamp\": \"{timestamp}\",\n" +
        $"  \"phase\": \"{phase}\",\n" +
        $"  \"filesReplaced\": {filesReplaced},\n" +
        $"  \"filesRemaining\": {filesRemaining}\n" +
        $"}}";

    string errorJsonPath = Path.Combine(dataDir, "update-error.json");
    try
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(errorJsonPath, errorJson);
        Log($"Error details written to: {errorJsonPath}");
    }
    catch (Exception ex)
    {
        Log($"Warning: Could not write update-error.json: {ex.Message}");
    }

    // Launch old main app
    string exePath = Path.Combine(appDir, exeName);
    Log($"Launching original app: {exePath}");
    try
    {
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Log($"Warning: Failed to launch original app: {ex.Message}");
    }
}

static void TryDeleteDirectory(string path, StreamWriter log)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            log.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]   [CLEANUP] Deleted: {path}");
        }
    }
    catch (Exception ex)
    {
        log.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]   [CLEANUP WARN] Could not delete {path}: {ex.Message}");
    }
}

// ── Win32 Registry P/Invoke (AOT-safe) ──────────────────────────────────────

partial class Program
{
    static void SyncMsiRegistryVersion(Action<string> log)
    {
        nint hKey;
        int result = RegOpenKeyEx(
            HKEY_CURRENT_USER,
            @"Software\XUnityToolkit",
            0,
            KEY_READ,
            out hKey);

        if (result != 0)
        {
            log("  No MSI registration found. Skipping.");
            return;
        }

        try
        {
            string? productCode = RegGetString(hKey, "MsiProductCode");
            if (string.IsNullOrEmpty(productCode))
            {
                log("  MsiProductCode not found. Skipping.");
                return;
            }

            string? installDir = RegGetString(hKey, "InstallDir");
            if (string.IsNullOrEmpty(installDir))
            {
                log("  InstallDir not found. Skipping.");
                return;
            }

            string exePath = Path.Combine(installDir, "XUnityToolkit-WebUI.exe");
            string? newVersion = null;
            if (File.Exists(exePath))
            {
                try
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    newVersion = versionInfo.ProductVersion?.Split('+')[0];
                }
                catch { /* ignore */ }
            }

            if (string.IsNullOrEmpty(newVersion))
            {
                log("  Could not determine new version. Skipping.");
                return;
            }

            string uninstallKey = @$"Software\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}";
            nint hUninstall;
            result = RegOpenKeyEx(HKEY_CURRENT_USER, uninstallKey, 0, KEY_WRITE, out hUninstall);
            if (result != 0)
            {
                log($"  Could not open uninstall key: {uninstallKey}");
                return;
            }

            try
            {
                RegSetString(hUninstall, "DisplayVersion", newVersion);
                RegSetString(hUninstall, "InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                log($"  Updated DisplayVersion to {newVersion}");
            }
            finally
            {
                RegCloseKey(hUninstall);
            }
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);
    const int KEY_READ = 0x20019;
    const int KEY_WRITE = 0x20006;
    const int REG_SZ = 1;

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegOpenKeyEx(nint hKey, string subKey, int options, int samDesired, out nint phkResult);

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    static extern int RegCloseKey(nint hKey);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegQueryValueEx(nint hKey, string valueName, nint reserved, out int type, char[] data, ref int dataSize);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegSetValueEx(nint hKey, string valueName, int reserved, int type, string data, int dataSize);

    static string? RegGetString(nint hKey, string name)
    {
        int type;
        int size = 520;
        char[] buffer = new char[260];
        int result = RegQueryValueEx(hKey, name, nint.Zero, out type, buffer, ref size);
        if (result != 0 || type != REG_SZ || size < 2) return null;
        return new string(buffer, 0, (size / 2) - 1);
    }

    static void RegSetString(nint hKey, string name, string value)
    {
        int dataSize = (value.Length + 1) * 2;
        RegSetValueEx(hKey, name, 0, REG_SZ, value, dataSize);
    }
}
