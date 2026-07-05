using System;
using System.IO;

namespace RobustDownloader.Services;

public static class TaskFileNameHelper
{
    public static string GetFileName(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Path.GetFileName(url);

            var fileName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "downloaded_file";

            return fileName;
        }
        catch
        {
            return "unknown_file";
        }
    }
}
