using System;
using System.IO;
using Avalonia.Platform.Storage;

namespace RobustDownloader.Services;

public static class FolderPathHelper
{
    public static string? GetLocalPath(IStorageFolder? folder)
    {
        var uri = folder?.Path;
        if (uri is null)
            return null;

        var path = uri.IsAbsoluteUri && uri.IsFile
            ? uri.LocalPath
            : uri.OriginalString;

        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Normalize(path);
    }

    public static string Normalize(string path)
    {
        path = path.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            path = uri.LocalPath;

        if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':')
            return path + Path.DirectorySeparatorChar;

        return path;
    }

    public static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(Normalize(path));
        }
        catch
        {
            return false;
        }
    }
}
