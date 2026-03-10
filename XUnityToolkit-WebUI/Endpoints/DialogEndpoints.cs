using System.Runtime.InteropServices;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Endpoints;

public static class DialogEndpoints
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 在 STA 线程上使用 TopMost 隐藏窗体作为 owner 运行对话框，确保对话框出现在前台。
    /// </summary>
    private static T? ShowDialogOnSta<T>(Func<Form, T?> showDialog)
    {
        T? result = default;

        var thread = new Thread(() =>
        {
            using var owner = new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                Size = new System.Drawing.Size(1, 1),
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-9999, -9999)
            };
            owner.Show();
            SetForegroundWindow(owner.Handle);

            result = showDialog(owner);

            owner.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    public static void MapDialogEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dialog/select-folder", () =>
        {
            var selectedPath = ShowDialogOnSta(owner =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select Game Directory",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true
                };

                return dialog.ShowDialog(owner) == DialogResult.OK
                    ? dialog.SelectedPath
                    : null;
            });

            return selectedPath is not null
                ? Results.Ok(ApiResult<string>.Ok(selectedPath))
                : Results.Ok(ApiResult<string>.Fail("No folder selected."));
        });

        app.MapPost("/api/dialog/select-file", (SelectFileRequest? request) =>
        {
            var selectedPath = ShowDialogOnSta(owner =>
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Select Game Executable",
                    Filter = request?.Filter ?? "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true
                };

                return dialog.ShowDialog(owner) == DialogResult.OK
                    ? dialog.FileName
                    : null;
            });

            return selectedPath is not null
                ? Results.Ok(ApiResult<string>.Ok(selectedPath))
                : Results.Ok(ApiResult<string>.Fail("No file selected."));
        });
    }
}

public record SelectFileRequest(string? Filter = null);
