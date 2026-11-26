using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

public enum UnsavedExitChoice
{
    Save,
    DontSave,
    Cancel
}

/// <summary>
/// Hộp thoại Cài đặt (Giao diện Tối / Sáng / Theo thiết bị, Ngôn ngữ Tiếng Việt / English, URL Cập nhật).
/// Mọi thay đổi áp dụng trực tiếp tức thời (Live Preview) lên toàn bộ ứng dụng.
/// </summary>
public sealed class SettingsDialog : Window
{
    private readonly UserSettingsModel _settings;
    private readonly ComboBox _themeCombo;
    private readonly ComboBox _langCombo;
    private readonly TextBox _urlBox;
    private readonly TextBlock _versionLabel;
    private bool _suppressEvents;

    public SettingsDialog(Window? owner, UserSettingsModel settings)
    {
        Owner = owner;
        _settings = settings;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        SetResourceReference(TitleProperty, "Str.Settings.Title");
        Icon = ThemeManager.GetAppIcon();
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");

        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };

        // 0. Hiển thị phiên bản chính thức hiện tại (v1.0_Release)
        _versionLabel = new TextBlock
        {
            Text = LocalizationManager.Format("Str.Settings.VersionInfo", UpdateService.GetCurrentInformationalVersion()),
            FontSize = 12.2,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _versionLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        root.Children.Add(_versionLabel);

        // 1. Chọn Giao diện (Theme)
        root.Children.Add(CreateFieldLabel("Str.Settings.ThemeLabel"));
        _themeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
        _themeCombo.SelectionChanged += OnThemeSelectionChanged;
        root.Children.Add(_themeCombo);

        // 2. Chọn Ngôn ngữ (Language)
        root.Children.Add(CreateFieldLabel("Str.Settings.LangLabel"));
        _langCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
        _langCombo.Items.Add(new ComboBoxItem { Content = "Tiếng Việt (Vietnamese)", Tag = "vi" });
        _langCombo.Items.Add(new ComboBoxItem { Content = "English (Tiếng Anh)", Tag = "en" });
        _langCombo.SelectionChanged += OnLanguageSelectionChanged;
        root.Children.Add(_langCombo);

        // 3. URL Kiểm tra cập nhật (GitHub Releases API)
        root.Children.Add(CreateFieldLabel("Str.Settings.UpdateUrlLabel"));
        _urlBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(settings.UpdateFeedUrl) ? UpdateService.DefaultApiUrl : settings.UpdateFeedUrl,
            FontSize = 12.0,
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1.2),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _urlBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        _urlBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _urlBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        _urlBox.SetResourceReference(TextBox.BorderBrushProperty, "ButtonBorderBrush");
        root.Children.Add(_urlBox);

        // Hàng nút phía dưới
        var footer = new DockPanel();
        var btnCheckUpdate = new Button { Margin = new Thickness(0, 0, 8, 0) };
        btnCheckUpdate.SetResourceReference(ContentProperty, "Str.Update.Title");
        btnCheckUpdate.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnCheckUpdate.Click += (_, _) =>
        {
            _settings.UpdateFeedUrl = _urlBox.Text.Trim();
            AppSettingsStore.Save(_settings);
            new UpdateDialog(this, _settings).ShowDialog();
        };
        DockPanel.SetDock(btnCheckUpdate, Dock.Left);
        footer.Children.Add(btnCheckUpdate);

        var btnClose = new Button { MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        btnClose.SetResourceReference(ContentProperty, "Str.Dialog.Close");
        btnClose.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnClose.SetResourceReference(BorderBrushProperty, "AccentCyanBrush");
        btnClose.Click += (_, _) =>
        {
            _settings.UpdateFeedUrl = _urlBox.Text.Trim();
            AppSettingsStore.Save(_settings);
            Close();
        };
        footer.Children.Add(btnClose);

        root.Children.Add(footer);
        Content = root;

        PopulateLocalizedThemeItems();
        SelectCurrentValues();

        LocalizationManager.LanguageChanged += OnExternalLanguageChanged;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnExternalLanguageChanged;
    }

    private void OnExternalLanguageChanged()
    {
        _versionLabel.Text = LocalizationManager.Format("Str.Settings.VersionInfo", UpdateService.GetCurrentInformationalVersion());
        PopulateLocalizedThemeItems();
        SelectCurrentValues();
    }

    private void PopulateLocalizedThemeItems()
    {
        _suppressEvents = true;
        _themeCombo.Items.Clear();
        _themeCombo.Items.Add(new ComboBoxItem { Content = LocalizationManager.Get("Str.Settings.ThemeDark"), Tag = AppThemePreference.Dark });
        _themeCombo.Items.Add(new ComboBoxItem { Content = LocalizationManager.Get("Str.Settings.ThemeLight"), Tag = AppThemePreference.Light });
        _themeCombo.Items.Add(new ComboBoxItem { Content = LocalizationManager.Get("Str.Settings.ThemeSystem"), Tag = AppThemePreference.System });
        _suppressEvents = false;
    }

    private void SelectCurrentValues()
    {
        _suppressEvents = true;
        _themeCombo.SelectedIndex = _settings.Theme switch
        {
            AppThemePreference.Light => 1,
            AppThemePreference.System => 2,
            _ => 0
        };
        _langCombo.SelectedIndex = string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _suppressEvents = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (_themeCombo.SelectedItem is ComboBoxItem { Tag: AppThemePreference pref })
        {
            _settings.Theme = pref;
            ThemeManager.ApplyTheme(pref);
            ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);
            AppSettingsStore.Save(_settings);
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (_langCombo.SelectedItem is ComboBoxItem { Tag: string code })
        {
            _settings.Language = code;
            LocalizationManager.SetLanguage(code);
            AppSettingsStore.Save(_settings);
        }
    }

    private static TextBlock CreateFieldLabel(string resourceKey)
    {
        var tb = new TextBlock
        {
            FontSize = 12.2,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        tb.SetResourceReference(TextBlock.TextProperty, resourceKey);
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return tb;
    }
}

/// <summary>
/// Hộp thoại Kiểm tra cập nhật phiên bản tích hợp UpdateService:
/// - Kết nối đúng repo dangdinhnambhlc2709-alt/WiringSim với User-Agent hợp lệ.
/// - Chuẩn hóa phiên bản (v1.0_Release -> 1.0.0) &amp; xử lý thân thiện HTTP 404 (chưa có Release).
/// - Hỗ trợ tải trực tiếp tệp đính kèm (.exe / .zip) hoặc mở trang GitHub Releases.
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly UserSettingsModel _settings;
    private readonly TextBlock _statusText;
    private readonly TextBox _notesBox;
    private readonly Button _directDownloadBtn;
    private readonly Button _openReleasesBtn;
    private readonly Button _recheckBtn;
    private UpdateCheckResult? _lastResult;

    public UpdateDialog(Window? owner, UserSettingsModel settings)
    {
        Owner = owner;
        _settings = settings;

        Width = 510;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        SetResourceReference(TitleProperty, "Str.Update.Title");
        Icon = ThemeManager.GetAppIcon();
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };

        string currentBare = UpdateService.GetCurrentInformationalVersion().TrimStart('v', 'V');
        var verText = new TextBlock
        {
            Text = LocalizationManager.Format("Str.Update.CurrentVer", currentBare),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        verText.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        root.Children.Add(verText);

        _statusText = new TextBlock
        {
            Text = LocalizationManager.Get("Str.Update.Checking"),
            FontSize = 12.4,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(_statusText);

        _notesBox = new TextBox
        {
            Height = 115,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 14),
            Visibility = Visibility.Collapsed
        };
        _notesBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        _notesBox.SetResourceReference(TextBox.ForegroundProperty, "TextSecondaryBrush");
        _notesBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderSubtleBrush");
        root.Children.Add(_notesBox);

        var actionWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        _directDownloadBtn = new Button
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 8, 6)
        };
        _directDownloadBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _directDownloadBtn.SetResourceReference(BorderBrushProperty, "AccentGreenBrush");
        _directDownloadBtn.Click += async (_, _) => await DownloadAssetDirectAsync();
        actionWrap.Children.Add(_directDownloadBtn);

        _openReleasesBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.DownloadBtn"),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 8, 6)
        };
        _openReleasesBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _openReleasesBtn.SetResourceReference(BorderBrushProperty, "AccentBlueBrush");
        _openReleasesBtn.Click += (_, _) =>
        {
            string url = _lastResult?.ReleasePageUrl ?? UpdateService.DefaultReleasesPageUrl;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        };
        actionWrap.Children.Add(_openReleasesBtn);
        root.Children.Add(actionWrap);

        var btnPanel = new DockPanel();
        _recheckBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.CheckBtn"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _recheckBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _recheckBtn.Click += async (_, _) => await CheckForUpdatesAsync();
        DockPanel.SetDock(_recheckBtn, Dock.Left);
        btnPanel.Children.Add(_recheckBtn);

        var closeBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Dialog.Close"),
            MinWidth = 86,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        closeBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        closeBtn.Click += (_, _) => Close();
        btnPanel.Children.Add(closeBtn);

        root.Children.Add(btnPanel);
        Content = root;

        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        _recheckBtn.IsEnabled = false;
        _directDownloadBtn.Visibility = Visibility.Collapsed;
        _openReleasesBtn.Visibility = Visibility.Collapsed;
        _notesBox.Visibility = Visibility.Collapsed;
        _statusText.Text = LocalizationManager.Get("Str.Update.Checking");

        var result = await UpdateService.CheckForUpdateAsync(_settings.UpdateFeedUrl);
        _lastResult = result;

        string currentBare = result.CurrentVersionLabel.TrimStart('v', 'V');

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _statusText.Text = LocalizationManager.Format("Str.Update.Available", result.LatestTagName ?? "");
                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                {
                    _notesBox.Text = $"{LocalizationManager.Get("Str.Update.NotesHeader")}\n{result.ReleaseNotes}";
                    _notesBox.Visibility = Visibility.Visible;
                }
                _openReleasesBtn.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(result.DirectAssetDownloadUrl) && !string.IsNullOrWhiteSpace(result.DirectAssetFileName))
                {
                    _directDownloadBtn.Content = LocalizationManager.Format("Str.Update.DirectDownloadBtn", result.DirectAssetFileName);
                    _directDownloadBtn.Visibility = Visibility.Visible;
                }
                break;

            case UpdateCheckStatus.UpToDate:
                _statusText.Text = LocalizationManager.Format("Str.Update.UpToDate", currentBare);
                break;

            default:
                _statusText.Text = LocalizationManager.Format("Str.Update.OfflineMsg", result.ErrorMessage ?? "Network Error", result.CurrentVersionLabel);
                _openReleasesBtn.Visibility = Visibility.Visible;
                break;
        }

        _recheckBtn.IsEnabled = true;
    }

    private async Task DownloadAssetDirectAsync()
    {
        if (_lastResult?.DirectAssetDownloadUrl is null || _lastResult.DirectAssetFileName is null)
            return;

        string ext = Path.GetExtension(_lastResult.DirectAssetFileName);
        var dlg = new SaveFileDialog
        {
            FileName = _lastResult.DirectAssetFileName,
            Filter = $"Installer / Archive (*{ext})|*{ext}|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) != true)
            return;

        _directDownloadBtn.IsEnabled = false;
        _recheckBtn.IsEnabled = false;
        _statusText.Text = LocalizationManager.Get("Str.Update.Downloading");

        try
        {
            await UpdateService.DownloadFileAsync(_lastResult.DirectAssetDownloadUrl, dlg.FileName);
            _statusText.Text = LocalizationManager.Format("Str.Update.DownloadSuccess", dlg.FileName);
        }
        catch (Exception ex)
        {
            _statusText.Text = LocalizationManager.Format("Str.Dialog.FileError", ex.Message);
        }
        finally
        {
            _directDownloadBtn.IsEnabled = true;
            _recheckBtn.IsEnabled = true;
        }
    }
}

/// <summary>
/// Hộp thoại 3 nút đồng bộ Theme Sáng/Tối &amp; Đa ngôn ngữ hỏi người dùng lưu thay đổi trước khi thoát ứng dụng.
/// </summary>
public sealed class ConfirmSaveDialog : Window
{
    public UnsavedExitChoice Choice { get; private set; } = UnsavedExitChoice.Cancel;

    private ConfirmSaveDialog(Window? owner)
    {
        Owner = owner;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Title = LocalizationManager.Get("Str.Exit.Title");
        Icon = ThemeManager.GetAppIcon();
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };

        var msg = new TextBlock
        {
            Text = LocalizationManager.Get("Str.Exit.Message"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        };
        msg.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(msg);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnSave = new Button
        {
            Content = LocalizationManager.Get("Str.Exit.Save"),
            MinWidth = 104,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        btnSave.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnSave.SetResourceReference(BorderBrushProperty, "AccentBlueBrush");
        btnSave.Click += (_, _) => { Choice = UnsavedExitChoice.Save; DialogResult = true; };

        var btnDontSave = new Button
        {
            Content = LocalizationManager.Get("Str.Exit.DontSave"),
            MinWidth = 94,
            Margin = new Thickness(0, 0, 8, 0)
        };
        btnDontSave.SetResourceReference(StyleProperty, "DangerToolbarButtonStyle");
        btnDontSave.Click += (_, _) => { Choice = UnsavedExitChoice.DontSave; DialogResult = true; };

        var btnCancel = new Button
        {
            Content = LocalizationManager.Get("Str.Exit.Cancel"),
            MinWidth = 80,
            IsCancel = true
        };
        btnCancel.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnCancel.Click += (_, _) => { Choice = UnsavedExitChoice.Cancel; DialogResult = false; };

        btnRow.Children.Add(btnSave);
        btnRow.Children.Add(btnDontSave);
        btnRow.Children.Add(btnCancel);
        root.Children.Add(btnRow);

        Content = root;
    }

    public static UnsavedExitChoice ShowPrompt(Window? owner)
    {
        var dlg = new ConfirmSaveDialog(owner);
        dlg.ShowDialog();
        return dlg.Choice;
    }
}
