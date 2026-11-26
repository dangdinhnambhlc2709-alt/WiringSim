using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
    string? SetupAssetUrl = null,
    string? SetupAssetFileName = null,
    string? PortableZipUrl = null,
    string? PortableZipFileName = null,
    string? ErrorMessage = null);

public readonly record struct UpdateDownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    public double DownloadedMb => BytesDownloaded / (1024.0 * 1024.0);
    public double? TotalMb => TotalBytes.HasValue ? TotalBytes.Value / (1024.0 * 1024.0) : null;
    public double Percent => TotalBytes is > 0 ? Math.Clamp(BytesDownloaded * 100.0 / TotalBytes.Value, 0.0, 100.0) : 0.0;
}

/// <summary>
/// Dịch vụ kiểm tra, tải xuống có thanh tiến trình và tự động cài đặt cập nhật WiringSim (v1.1.1_Release):
/// - Kết nối chính thức GitHub Releases API (dangdinhnambhlc2709-alt/WiringSim).
/// - Chuẩn hóa chuỗi phiên bản (v1.1.1_Release -> Version(1,1,1)) an toàn.
/// - Tự nhận diện chế độ Cài đặt (Setup .exe với /SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS)
///   hoặc Bản chạy ngay Portable (.zip giải nén + script .bat chép đè &amp; khởi động lại).
/// </summary>
public static class UpdateService
{
    public const string DefaultApiUrl = "https://api.github.com/repos/dangdinhnambhlc2709-alt/WiringSim/releases/latest";
    public const string DefaultReleasesPageUrl = "https://github.com/dangdinhnambhlc2709-alt/WiringSim/releases";

    private static readonly HttpClient ApiClient = CreateHttpClient(TimeSpan.FromSeconds(8));
    private static readonly HttpClient DownloadClient = CreateHttpClient(TimeSpan.FromMinutes(10));

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WiringSim-Updater/1.3.3");
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
        var v = asm.GetName().Version ?? new Version(1, 1, 1);
        return $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Chuẩn hóa chuỗi phiên bản: bỏ tiền tố v/V và hậu tố _Release, -beta... về System.Version(major, minor, build).
    /// Ví dụ: "v1.1.1_Release" -> 1.1.1 | "v1.2.0-rc1" -> 1.2.0
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

    /// <summary>Kiểm tra xem phiên bản mới đã được người dùng chọn "Bỏ qua bản này" trước đó chưa.</summary>
    public static bool IsVersionSkipped(string? skippedSetting, string? latestTag)
    {
        if (string.IsNullOrWhiteSpace(skippedSetting) || string.IsNullOrWhiteSpace(latestTag))
            return false;

        if (string.Equals(skippedSetting.Trim(), latestTag.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return NormalizeVersion(skippedSetting) == NormalizeVersion(latestTag);
    }

    /// <summary>
    /// Nhận diện ứng dụng đang chạy ở chế độ cài đặt (có unins000.exe hoặc nằm trong Program Files) hay Portable.
    /// </summary>
    public static bool IsInstalledMode()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, "unins000.exe")))
                return true;

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrWhiteSpace(pf) && baseDir.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(pf86) && baseDir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// Chọn gói tải phù hợp nhất từ Release assets dựa trên chế độ cài đặt (Setup .exe) hay Portable (.zip).
    /// </summary>
    public static (string? Url, string? FileName, bool IsSetupInstaller) SelectPreferredAsset(UpdateCheckResult result)
    {
        bool installed = IsInstalledMode();
        if (installed)
        {
            if (!string.IsNullOrWhiteSpace(result.SetupAssetUrl))
                return (result.SetupAssetUrl, result.SetupAssetFileName, true);
            if (!string.IsNullOrWhiteSpace(result.PortableZipUrl))
                return (result.PortableZipUrl, result.PortableZipFileName, false);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(result.PortableZipUrl))
                return (result.PortableZipUrl, result.PortableZipFileName, false);
            if (!string.IsNullOrWhiteSpace(result.SetupAssetUrl))
                return (result.SetupAssetUrl, result.SetupAssetFileName, true);
        }

        return (null, null, installed);
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(string? customApiUrl = null, CancellationToken ct = default)
    {
        string currentLabel = GetCurrentInformationalVersion();
        Version currentVer = NormalizeVersion(currentLabel);
        string apiUrl = string.IsNullOrWhiteSpace(customApiUrl) ? DefaultApiUrl : customApiUrl.Trim();

        try
        {
            using var resp = await ApiClient.GetAsync(apiUrl, ct);

            // HTTP 404 Not Found: Repository chưa có Release chính thức nào -> Đang dùng bản mới nhất
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentLabel,
                    ReleasePageUrl: DefaultReleasesPageUrl);
            }

            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tEl) ? (tEl.GetString() ?? string.Empty) : string.Empty;
            string body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? string.Empty) : string.Empty;
            string htmlUrl = root.TryGetProperty("html_url", out var uEl) ? (uEl.GetString() ?? DefaultReleasesPageUrl) : DefaultReleasesPageUrl;

            string? setupUrl = null;
            string? setupName = null;
            string? fallbackExeUrl = null;
            string? fallbackExeName = null;
            string? zipUrl = null;
            string? zipName = null;

            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
                    string dl = asset.TryGetProperty("browser_download_url", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
                    if (string.IsNullOrWhiteSpace(dl)) continue;

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && setupUrl is null)
                        {
                            setupUrl = dl;
                            setupName = name;
                        }
                        else if (fallbackExeUrl is null)
                        {
                            fallbackExeUrl = dl;
                            fallbackExeName = name;
                        }
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && zipUrl is null)
                    {
                        zipUrl = dl;
                        zipName = name;
                    }
                }
            }

            setupUrl ??= fallbackExeUrl;
            setupName ??= fallbackExeName;

            Version remoteVer = NormalizeVersion(tag);
            if (remoteVer > currentVer)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    currentLabel,
                    LatestTagName: string.IsNullOrWhiteSpace(tag) ? $"v{remoteVer}" : tag,
                    ReleaseNotes: body,
                    ReleasePageUrl: htmlUrl,
                    SetupAssetUrl: setupUrl,
                    SetupAssetFileName: setupName,
                    PortableZipUrl: zipUrl,
                    PortableZipFileName: zipName);
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

    public static string GetTempUpdateDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WiringSimUpdate");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static async Task DownloadFileWithProgressAsync(
        string url,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var resp = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long? totalBytes = resp.Content.Headers.ContentLength;
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destinationPath);

        byte[] buffer = new byte[81920];
        long downloaded = 0;
        int read;
        long lastReported = 0;

        progress?.Report(new UpdateDownloadProgress(0, totalBytes));

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;

            if (downloaded - lastReported >= 65536 || (totalBytes.HasValue && downloaded >= totalBytes.Value))
            {
                lastReported = downloaded;
                progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes));
            }
        }

        progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes ?? downloaded));
    }

    /// <summary>
    /// Chuẩn bị gói cài đặt đã tải:
    /// - Với Setup .exe: trả về đường dẫn trình cài đặt để chạy với /SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS.
    /// - Với Portable .zip: giải nén vào %TEMP%\WiringSimUpdate\extracted và sinh script .bat chép đè + khởi động lại.
    /// </summary>
    public static string PreparePortableUpdateScript(string zipFilePath)
    {
        string updateRoot = GetTempUpdateDirectory();
        string extractDir = Path.Combine(updateRoot, "extracted");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);

        ZipFile.ExtractToDirectory(zipFilePath, extractDir, overwriteFiles: true);

        string? newExePath = Directory
            .EnumerateFiles(extractDir, "WiringSim.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
            throw new FileNotFoundException("WiringSim.exe not found inside update archive.");

        string currentExePath = Environment.ProcessPath
                                ?? Path.Combine(AppContext.BaseDirectory, "WiringSim.exe");
        int currentPid = Environment.ProcessId;

        string batPath = Path.Combine(updateRoot, "apply_update.bat");
        string batContent = $"""
            @echo off
            chcp 65001 >nul
            setlocal
            set "PID={currentPid}"
            set "SRC={newExePath}"
            set "DST={currentExePath}"
            set "UPD={updateRoot}"
            :waitloop
            tasklist /FI "PID eq %PID%" 2>NUL | find /I "%PID%" >NUL
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto waitloop
            )
            timeout /t 1 /nobreak >nul
            copy /Y "%SRC%" "%DST%" >nul
            if exist "%DST%" start "" "%DST%"
            start "" /b cmd /c "timeout /t 2 /nobreak >nul & rmdir /S /Q \"%UPD%\""
            exit /b 0
            """;

        File.WriteAllText(batPath, batContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return batPath;
    }

    public static void LaunchSetupInstaller(string setupExePath)
    {
        Process.Start(new ProcessStartInfo(setupExePath)
        {
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        });
    }

    public static void LaunchPortableUpdaterBatch(string batPath)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        });
    }
}
