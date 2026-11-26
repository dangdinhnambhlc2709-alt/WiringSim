using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WiringSim.UI.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Error
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersionLabel,
    string? LatestTagName = null,
    string? ReleaseNotes = null,
    string? ReleasePageUrl = null,
    string? DirectAssetDownloadUrl = null,
    string? DirectAssetFileName = null,
    string? ErrorMessage = null);

/// <summary>
/// Dịch vụ kiểm tra cập nhật tự động kết nối GitHub Releases API:
/// - Mặc định trỏ đúng Repository dangdinhnambhlc2709-alt/WiringSim.
/// - Chuẩn hóa chuỗi phiên bản (v1.0_Release -> Version(1,0,0)) an toàn không lỗi định dạng.
/// - Xử lý mượt mã HTTP 404 (chưa đăng bản phát hành nào) như trạng thái "Đang dùng bản mới nhất".
/// </summary>
public static class UpdateService
{
    public const string DefaultApiUrl = "https://api.github.com/repos/dangdinhnambhlc2709-alt/WiringSim/releases/latest";
    public const string DefaultReleasesPageUrl = "https://github.com/dangdinhnambhlc2709-alt/WiringSim/releases";

    private static readonly HttpClient Client = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WiringSim-Updater/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static string GetCurrentInformationalVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
        {
            string raw = infoAttr.InformationalVersion.Split('+')[0];
            return raw.StartsWith('v') || raw.StartsWith('V') ? raw : $"v{raw}";
        }
        var v = asm.GetName().Version ?? new Version(1, 0, 0);
        return $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Chuẩn hóa chuỗi phiên bản: bỏ tiền tố v/V và hậu tố _Release, -beta... về System.Version(major, minor, build).
    /// Ví dụ: "v1.0_Release" -> 1.0.0 | "v1.2.3-rc1" -> 1.2.3
    /// </summary>
    public static Version NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Version(1, 0, 0);

        string s = raw.Trim().TrimStart('v', 'V');
        var match = Regex.Match(s, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
        if (!match.Success)
            return new Version(1, 0, 0);

        int major = int.Parse(match.Groups[1].Value);
        int minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new Version(major, minor, build);
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(string? customApiUrl = null)
    {
        string currentLabel = GetCurrentInformationalVersion();
        Version currentVer = NormalizeVersion(currentLabel);
        string apiUrl = string.IsNullOrWhiteSpace(customApiUrl) ? DefaultApiUrl : customApiUrl.Trim();

        try
        {
            using var resp = await Client.GetAsync(apiUrl);

            // HTTP 404 Not Found: Repository chưa có bản Release chính thức nào -> Đang dùng bản mới nhất!
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentLabel,
                    ReleasePageUrl: DefaultReleasesPageUrl);
            }

            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tEl) ? (tEl.GetString() ?? string.Empty) : string.Empty;
            string body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? string.Empty) : string.Empty;
            string htmlUrl = root.TryGetProperty("html_url", out var uEl) ? (uEl.GetString() ?? DefaultReleasesPageUrl) : DefaultReleasesPageUrl;

            string? assetUrl = null;
            string? assetName = null;
            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
                    string dl = asset.TryGetProperty("browser_download_url", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(dl) &&
                        (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        assetUrl = dl;
                        assetName = name;
                        break;
                    }
                }
            }

            Version remoteVer = NormalizeVersion(tag);
            if (remoteVer > currentVer)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    currentLabel,
                    LatestTagName: string.IsNullOrWhiteSpace(tag) ? $"v{remoteVer}" : tag,
                    ReleaseNotes: body,
                    ReleasePageUrl: htmlUrl,
                    DirectAssetDownloadUrl: assetUrl,
                    DirectAssetFileName: assetName);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                currentLabel,
                LatestTagName: tag,
                ReleasePageUrl: htmlUrl);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Error,
                currentLabel,
                ReleasePageUrl: DefaultReleasesPageUrl,
                ErrorMessage: ex.Message);
        }
    }

    public static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        await using var fs = System.IO.File.Create(destinationPath);
        await stream.CopyToAsync(fs);
    }
}
