using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RobustDownloader.Services;

public static class UpdateService
{
    private const string LatestReleaseUrl = "https://github.com/nilaoda/RobustDownloader/releases/latest";
    private static readonly HttpClient HttpClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static string CurrentReleaseTag => ReleaseTagHolder.Value;

    public static bool IsLocalBuild => string.IsNullOrEmpty(CurrentReleaseTag);

    public static async Task<string?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (IsLocalBuild) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleaseUrl);
            using var response = await HttpClient.SendAsync(request, ct);

            if (response.StatusCode is not System.Net.HttpStatusCode.Redirect
                and not System.Net.HttpStatusCode.MovedPermanently)
                return null;

            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location)) return null;

            var tagName = ExtractTagName(location);
            return string.IsNullOrEmpty(tagName) || tagName == CurrentReleaseTag ? null : tagName;
        }
        catch
        {
            return null;
        }
    }

    public static void StartPeriodicCheck(Func<string, Task> onUpdateFound, CancellationToken ct)
    {
        if (IsLocalBuild) return;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var latestTag = await CheckForUpdateAsync(ct);
                    if (latestTag != null)
                        await onUpdateFound(latestTag);
                }
                catch
                {
                    // ignore periodic check errors
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(6), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    private static string ExtractTagName(string redirectUrl)
    {
        const string tagSuffix = "/releases/tag/";
        var index = redirectUrl.LastIndexOf(tagSuffix, StringComparison.Ordinal);
        return index >= 0 ? redirectUrl[(index + tagSuffix.Length)..] : "";
    }
}
