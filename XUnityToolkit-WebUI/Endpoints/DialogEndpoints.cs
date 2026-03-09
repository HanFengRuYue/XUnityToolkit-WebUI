using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Endpoints;

public static class DialogEndpoints
{
    public static void MapDialogEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dialog/select-folder", () =>
        {
            string? selectedPath = null;

            var thread = new Thread(() =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select Game Directory",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                    selectedPath = dialog.SelectedPath;
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return selectedPath is not null
                ? Results.Ok(ApiResult<string>.Ok(selectedPath))
                : Results.Ok(ApiResult<string>.Fail("No folder selected."));
        });

        app.MapPost("/api/dialog/select-file", (SelectFileRequest? request) =>
        {
            string? selectedPath = null;

            var thread = new Thread(() =>
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Select Game Executable",
                    Filter = request?.Filter ?? "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                    selectedPath = dialog.FileName;
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return selectedPath is not null
                ? Results.Ok(ApiResult<string>.Ok(selectedPath))
                : Results.Ok(ApiResult<string>.Fail("No file selected."));
        });
    }
}

public record SelectFileRequest(string? Filter = null);
