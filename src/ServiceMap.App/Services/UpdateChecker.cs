using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace ServiceMap.App.Services;

/// <summary>
/// Manual update check against the GitHub Releases API. Only runs when the
/// user clicks "Check for updates" — the app makes no network calls on its own
/// (a property the README promises, so keep it that way).
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/mathursunit/DependenSee/releases/latest";

    public const string ReleasesPage =
        "https://github.com/mathursunit/DependenSee/releases";

    public sealed record Result(bool UpdateAvailable, string LatestVersion, string Message);

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // GitHub's API requires a User-Agent.
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CarrierDependenSee", AppInfo.Version));
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = await http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t)
                ? t.GetString() ?? string.Empty
                : string.Empty;
            var latest = tag.TrimStart('v', 'V');

            if (!Version.TryParse(Pad(latest), out var latestVer) ||
                !Version.TryParse(Pad(AppInfo.Version), out var currentVer))
            {
                return new Result(false, latest,
                    $"Latest release is {tag}; unable to compare with {AppInfo.Version}.");
            }

            return latestVer > currentVer
                ? new Result(true, latest,
                    $"Update available: {latest} (you have {AppInfo.Version}).")
                : new Result(false, latest,
                    $"You're up to date ({AppInfo.Version}).");
        }
        catch (Exception ex)
        {
            return new Result(false, string.Empty,
                $"Could not check for updates: {Trim(ex.Message)}");
        }
    }

    /// <summary>"1.6" -> "1.6.0" so Version.Parse accepts 2- and 3-part strings.</summary>
    private static string Pad(string v)
    {
        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => v + ".0.0",
            2 => v + ".0",
            _ => v
        };
    }

    private static string Trim(string s) => s.Length > 120 ? s[..120] + "…" : s;
}
