using System.Runtime.InteropServices;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public static class QuickAccessHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHGetKnownFolderPath(
        ref Guid folderId, uint flags, IntPtr token, out string path);

    private static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    private static volatile List<QuickAccessEntry>? _cached;

    public static async Task<List<QuickAccessEntry>> GetQuickAccessEntriesAsync()
    {
        var cached = _cached;
        if (cached is not null) return cached;

        var entries = GetStandardFolders();
        var pinned = await GetPinnedFoldersAsync();

        var standardPaths = new HashSet<string>(
            entries.Select(e => e.FullPath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var p in pinned)
        {
            if (!standardPaths.Contains(p.FullPath))
                entries.Add(p);
        }

        _cached = entries;
        return entries;
    }

    private static List<QuickAccessEntry> GetStandardFolders()
    {
        var folders = new (string Name, Func<string?> GetPath)[]
        {
            ("桌面", () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("下载", GetDownloadsPath),
            ("文档", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("图片", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("音乐", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("视频", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };

        var result = new List<QuickAccessEntry>();
        foreach (var (name, getPath) in folders)
        {
            try
            {
                var path = getPath();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    result.Add(new QuickAccessEntry(name, path, "standard"));
            }
            catch
            {
                // Skip inaccessible folders
            }
        }
        return result;
    }

    private static string? GetDownloadsPath()
    {
        try
        {
            var folderId = FOLDERID_Downloads;
            SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var path);
            return path;
        }
        catch
        {
            // Fallback: combine UserProfile + Downloads
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(userProfile) ? null : Path.Combine(userProfile, "Downloads");
        }
    }

    private static Task<List<QuickAccessEntry>> GetPinnedFoldersAsync()
    {
        var tcs = new TaskCompletionSource<List<QuickAccessEntry>>();

        var thread = new Thread(() =>
        {
            try
            {
                var results = EnumerateQuickAccessFolders();
                tcs.SetResult(results);
            }
            catch
            {
                tcs.SetResult([]);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    private static List<QuickAccessEntry> EnumerateQuickAccessFolders()
    {
        var results = new List<QuickAccessEntry>();

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return results;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic? quickAccess = shell.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
            if (quickAccess is null) return results;

            try
            {
                dynamic items = quickAccess.Items();
                try
                {
                    foreach (dynamic item in items)
                    {
                        try
                        {
                            string path = item.Path;
                            string name = item.Name;

                            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            {
                                results.Add(new QuickAccessEntry(name, path, "pinned"));
                            }
                        }
                        catch
                        {
                            // Skip items that can't be accessed
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(items);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(quickAccess);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }

        return results;
    }
}
