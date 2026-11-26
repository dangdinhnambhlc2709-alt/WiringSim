using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WiringSim.UI.Services;

public enum AppThemePreference
{
    Dark = 0,
    Light = 1,
    System = 2
}

public sealed class UserSettingsModel
{
    public AppThemePreference Theme { get; set; } = AppThemePreference.Dark;
    public string Language { get; set; } = "vi";
    public string UpdateFeedUrl { get; set; } = UpdateService.DefaultApiUrl;
}

/// <summary>
/// Bảng màu động dành cho bộ dựng đồ họa Vector trên Canvas (đổi ngay lập tức theo Sáng / Tối).
/// </summary>
public sealed record CanvasThemeColors(
    bool IsDark,
    string CanvasBgHex,
    string GridDotHex,
    string MajorGridDotHex,
    string BodyTopHex,
    string BodyBottomHex,
    string InsetPanelHex,
    string BorderDefaultHex,
    string NameTagBgHex,
    string NameTagTextHex,
    string SubTextHex,
    string TooltipBgHex,
    string TooltipTextHex);

/// <summary>
/// Quản lý giao diện động (Tối / Sáng / Theo thiết bị Windows) và đồng bộ DWM Immersive Dark Mode cho cửa sổ.
/// </summary>
public static class ThemeManager
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;

    private static ResourceDictionary? _activeThemeDict;
    private static ImageSource? _cachedAppIcon;
    private static bool _systemHooked;

    public static AppThemePreference CurrentPreference { get; private set; } = AppThemePreference.Dark;
    public static bool IsEffectiveDark { get; private set; } = true;

    public static CanvasThemeColors CanvasColors { get; private set; } = CreateDarkCanvasColors();

    public static event Action? ThemeChanged;

    public static void Initialize(AppThemePreference preference)
    {
        if (!_systemHooked)
        {
            _systemHooked = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && CurrentPreference == AppThemePreference.System)
                {
                    Application.Current?.Dispatcher.Invoke(() => ApplyTheme(AppThemePreference.System));
                }
            };
        }

        ApplyTheme(preference);
    }

    public static void ApplyTheme(AppThemePreference preference)
    {
        CurrentPreference = preference;
        bool useDark = preference switch
        {
            AppThemePreference.Light => false,
            AppThemePreference.System => !IsWindowsUsingLightTheme(),
            _ => true
        };

        IsEffectiveDark = useDark;
        CanvasColors = useDark ? CreateDarkCanvasColors() : CreateLightCanvasColors();

        if (Application.Current is not null)
        {
            string uriStr = useDark ? "Themes/ThemeDark.xaml" : "Themes/ThemeLight.xaml";
            var newDict = new ResourceDictionary { Source = new Uri(uriStr, UriKind.Relative) };

            var merged = Application.Current.Resources.MergedDictionaries;
            int existingIdx = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                string src = merged[i].Source?.OriginalString ?? string.Empty;
                if (merged[i] == _activeThemeDict || src.Contains("Themes/Theme", StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx >= 0) merged[existingIdx] = newDict;
            else merged.Insert(0, newDict);
            _activeThemeDict = newDict;

            foreach (Window win in Application.Current.Windows)
            {
                ApplyWindowDwmDarkMode(win, useDark);
            }
        }

        ThemeChanged?.Invoke();
    }

    public static void ApplyWindowDwmDarkMode(Window window, bool isDark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int val = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref val, sizeof(int));
            }
        }
        catch
        {
            // Bỏ qua an toàn trên các phiên bản HĐH cũ
        }
    }

    public static void ApplyWindowCornerPreference(Window window, int cornerPreference)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int pref = cornerPreference;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Bỏ qua an toàn trên Windows 10 hoặc HĐH không hỗ trợ DWMWA_WINDOW_CORNER_PREFERENCE
        }
    }

    public static ImageSource? GetAppIcon()
    {
        if (_cachedAppIcon is not null)
            return _cachedAppIcon;

        try
        {
            var frame = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            frame.Freeze();
            _cachedAppIcon = frame;
            return _cachedAppIcon;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWindowsUsingLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
            {
                return v != 0;
            }
        }
        catch
        {
        }
        return false;
    }

    private static CanvasThemeColors CreateDarkCanvasColors() => new(
        IsDark: true,
        CanvasBgHex: "#0B0F17",
        GridDotHex: "#243044",
        MajorGridDotHex: "#374966",
        BodyTopHex: "#252D3D",
        BodyBottomHex: "#181E29",
        InsetPanelHex: "#0F151F",
        BorderDefaultHex: "#3E4C63",
        NameTagBgHex: "#DD111827",
        NameTagTextHex: "#E2E8F0",
        SubTextHex: "#94A3B8",
        TooltipBgHex: "#F20F172A",
        TooltipTextHex: "#F1F5F9");

    private static CanvasThemeColors CreateLightCanvasColors() => new(
        IsDark: false,
        CanvasBgHex: "#EDF2F7",
        GridDotHex: "#CBD5E1",
        MajorGridDotHex: "#94A3B8",
        BodyTopHex: "#FFFFFF",
        BodyBottomHex: "#E2E8F0",
        InsetPanelHex: "#1E293B",
        BorderDefaultHex: "#64748B",
        NameTagBgHex: "#F2FFFFFF",
        NameTagTextHex: "#0F172A",
        SubTextHex: "#475569",
        TooltipBgHex: "#F70F172A",
        TooltipTextHex: "#F8FAFC");
}

/// <summary>
/// Quản lý Đa ngôn ngữ chuẩn hóa (Tiếng Việt / English) — 100% tra cứu qua key ResourceDictionary.
/// </summary>
public static class LocalizationManager
{
    private static ResourceDictionary? _activeLangDict;
    public static string CurrentLanguage { get; private set; } = "vi";

    public static event Action? LanguageChanged;

    public static void SetLanguage(string langCode)
    {
        string normalized = string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        CurrentLanguage = normalized;

        string uriStr = normalized == "en" ? "Resources/Strings.en.xaml" : "Resources/Strings.vi.xaml";
        var newDict = new ResourceDictionary { Source = new Uri(uriStr, UriKind.Relative) };

        if (Application.Current is not null)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            int existingIdx = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                string src = merged[i].Source?.OriginalString ?? string.Empty;
                if (merged[i] == _activeLangDict || src.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx >= 0) merged[existingIdx] = newDict;
            else merged.Add(newDict);
        }
        _activeLangDict = newDict;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s)
            return s;
        if (_activeLangDict is not null && _activeLangDict.Contains(key) && _activeLangDict[key] is string ds)
            return ds;
        return $"[{key}]";
    }

    public static string Format(string key, params object[] args)
    {
        string pattern = Get(key);
        return string.Format(CultureInfo.CurrentCulture, pattern, args);
    }
}

/// <summary>
/// Đọc / lưu cấu hình người dùng (Theme, Language, UpdateFeedUrl) gọn nhẹ.
/// </summary>
public static class AppSettingsStore
{
    private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiringsim.settings.json");

    public static UserSettingsModel Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<UserSettingsModel>(json) ?? new UserSettingsModel();
                // Tự động cập nhật URL mẫu cũ sang Repository chính thức dangdinhnambhlc2709-alt/WiringSim
                if (string.IsNullOrWhiteSpace(loaded.UpdateFeedUrl) ||
                    loaded.UpdateFeedUrl.Contains("wiringsim/wiringsim", StringComparison.OrdinalIgnoreCase))
                {
                    loaded.UpdateFeedUrl = UpdateService.DefaultApiUrl;
                }
                return loaded;
            }
        }
        catch
        {
        }
        return new UserSettingsModel();
    }

    public static void Save(UserSettingsModel settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }
}
