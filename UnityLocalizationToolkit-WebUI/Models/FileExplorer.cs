namespace UnityLocalizationToolkit_WebUI.Models;

public record DriveEntry(string Name, string RootPath, string? Label, long? TotalSize, long? FreeSpace);

public record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTime? LastModified,
    string? Extension);

public record ListDirectoryRequest(string Path);

public record ListDirectoryResponse(
    string CurrentPath,
    string? ParentPath,
    List<FileSystemEntry> Entries);

public record QuickAccessEntry(string Name, string FullPath, string Type);

public record ReadTextRequest(string Path);

public record ReadTextResponse(string Content, string FileName, long FileSize);

public record UploadFromPathRequest(string FilePath);
