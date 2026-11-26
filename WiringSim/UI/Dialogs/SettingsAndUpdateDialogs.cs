using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Func<bool>? _prepareForUpdateExit;
    private readonly ComboBox _themeCombo;
    private readonly ComboBox _langCombo;
    private readonly TextBox _urlBox;
    private readonly TextBlock _versionLabel;
    private bool _suppressEvents;

    public SettingsDialog(Window? owner, UserSettingsModel settings, Func<bool>? prepareForUpdateExit = null)
    {
        Owner = owner;
        _settings = settings;
        _prepareForUpdateExit = prepareForUpdateExit;
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

        // 0. Hiển thị phiên bản chính thức hiện tại
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
            new UpdateDialog(this, _settings, preloadedResult: null, prepareForUpdateExit: _prepareForUpdateExit).ShowDialog();
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
/// Hộp thoại Tải về &amp; Cài đặt cập nhật trực tiếp trong phần mềm (v1.1.1_Release):
/// - Hiển thị phiên bản hiện tại, phiên bản mới, ghi chú phát hành (Release Notes) và chế độ Setup/Portable.
/// - Thanh tiến trình ProgressBar (0%–100%) kèm dung lượng đã tải (MB / MB).
/// - 3 nút chính: "Tải về &amp; Cài đặt ngay", "Bỏ qua bản này", "Để sau" + nút dự phòng "Mở trang tải thủ công".
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly UserSettingsModel _settings;
    private readonly Func<bool>? _prepareForUpdateExit;
    private readonly TextBlock _versionHeaderText;
    private readonly TextBlock _modeBadgeText;
    private readonly TextBlock _statusText;
    private readonly TextBox _notesBox;
    private readonly StackPanel _progressContainer;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _progressLabel;

    private readonly StackPanel _updateActionPanel;
    private readonly Button _installNowBtn;
    private readonly Button _skipVersionBtn;
    private readonly Button _laterBtn;

    private readonly DockPanel _standardFooterPanel;
    private readonly Button _recheckBtn;
    private readonly Button _manualDownloadBtn;
    private readonly Button _closeBtn;

    private UpdateCheckResult? _lastResult;
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading;

    public UpdateDialog(
        Window? owner,
        UserSettingsModel settings,
        UpdateCheckResult? preloadedResult = null,
        Func<bool>? prepareForUpdateExit = null)
    {
        Owner = owner;
        _settings = settings;
        _prepareForUpdateExit = prepareForUpdateExit;
        _lastResult = preloadedResult;

        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        SetResourceReference(TitleProperty, "Str.Update.Title");
        Icon = ThemeManager.GetAppIcon();
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };

        // Hàng tiêu đề phiên bản + huy hiệu chế độ cài đặt (Setup / Portable)
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _modeBadgeText = new TextBlock
        {
            Text = LocalizationManager.Get(UpdateService.IsInstalledMode() ? "Str.Update.ModeInstalled" : "Str.Update.ModePortable"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _modeBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var modePill = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Child = _modeBadgeText,
            VerticalAlignment = VerticalAlignment.Center
        };
        modePill.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        modePill.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        DockPanel.SetDock(modePill, Dock.Right);
        headerDock.Children.Add(modePill);

        string currentBare = UpdateService.GetCurrentInformationalVersion().TrimStart('v', 'V');
        _versionHeaderText = new TextBlock
        {
            Text = LocalizationManager.Format("Str.Update.CurrentVer", currentBare),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _versionHeaderText.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        headerDock.Children.Add(_versionHeaderText);
        root.Children.Add(headerDock);

        // Trạng thái kiểm tra / tải xuống
        _statusText = new TextBlock
        {
            Text = LocalizationManager.Get("Str.Update.Checking"),
            FontSize = 12.4,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(_statusText);

        // Ghi chú phát hành (Release Notes từ GitHub API body)
        _notesBox = new TextBox
        {
            Height = 145,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        _notesBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        _notesBox.SetResourceReference(TextBox.ForegroundProperty, "TextSecondaryBrush");
        _notesBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderSubtleBrush");
        root.Children.Add(_notesBox);

        // Khu vực thanh tiến trình tải xuống (0%–100% + MB / MB)
        _progressContainer = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
            Visibility = Visibility.Collapsed
        };
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 12,
            Margin = new Thickness(0, 0, 0, 5)
        };
        _progressBar.SetResourceReference(ProgressBar.BackgroundProperty, "InputBgBrush");
        _progressBar.SetResourceReference(ProgressBar.ForegroundProperty, "AccentCyanBrush");
        _progressBar.SetResourceReference(ProgressBar.BorderBrushProperty, "BorderSubtleBrush");

        _progressLabel = new TextBlock
        {
            FontSize = 11.4,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        _progressLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        _progressContainer.Children.Add(_progressBar);
        _progressContainer.Children.Add(_progressLabel);
        root.Children.Add(_progressContainer);

        // Hàng 3 nút chính khi phát hiện có bản cập nhật mới: Tải về & Cài đặt ngay | Bỏ qua bản này | Để sau
        _updateActionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };

        _installNowBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.InstallNowBtn"),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        _installNowBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _installNowBtn.SetResourceReference(BorderBrushProperty, "AccentGreenBrush");
        _installNowBtn.Click += async (_, _) => await DownloadAndInstallAsync();

        _skipVersionBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.SkipVersionBtn"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _skipVersionBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _skipVersionBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_lastResult?.LatestTagName))
            {
                _settings.SkippedUpdateVersion = _lastResult.LatestTagName;
                AppSettingsStore.Save(_settings);
            }
            Close();
        };

        _laterBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.LaterBtn"),
            MinWidth = 82,
            IsCancel = true
        };
        _laterBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _laterBtn.Click += (_, _) => Close();

        _updateActionPanel.Children.Add(_installNowBtn);
        _updateActionPanel.Children.Add(_skipVersionBtn);
        _updateActionPanel.Children.Add(_laterBtn);
        root.Children.Add(_updateActionPanel);

        // Thanh chân trang: Mở trang tải thủ công (dự phòng) | Kiểm tra ngay | Đóng
        _standardFooterPanel = new DockPanel();

        _manualDownloadBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.ManualDownloadBtn"),
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };
        _manualDownloadBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _manualDownloadBtn.SetResourceReference(BorderBrushProperty, "AccentBlueBrush");
        _manualDownloadBtn.Click += (_, _) => OpenManualReleasePage();
        DockPanel.SetDock(_manualDownloadBtn, Dock.Left);
        _standardFooterPanel.Children.Add(_manualDownloadBtn);

        _recheckBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Update.CheckBtn"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _recheckBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _recheckBtn.Click += async (_, _) => await CheckForUpdatesAsync();
        DockPanel.SetDock(_recheckBtn, Dock.Left);
        _standardFooterPanel.Children.Add(_recheckBtn);

        _closeBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Dialog.Close"),
            MinWidth = 86,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _closeBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        _closeBtn.Click += (_, _) => Close();
        _standardFooterPanel.Children.Add(_closeBtn);

        root.Children.Add(_standardFooterPanel);
        Content = root;

        Closed += (_, _) =>
        {
            try { _downloadCts?.Cancel(); } catch { }
            _downloadCts?.Dispose();
        };

        Loaded += async (_, _) =>
        {
            if (_lastResult is not null)
                ApplyCheckResult(_lastResult);
            else
                await CheckForUpdatesAsync();
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        _recheckBtn.IsEnabled = false;
        _updateActionPanel.Visibility = Visibility.Collapsed;
        _manualDownloadBtn.Visibility = Visibility.Collapsed;
        _notesBox.Visibility = Visibility.Collapsed;
        _progressContainer.Visibility = Visibility.Collapsed;
        _closeBtn.Visibility = Visibility.Visible;
        _statusText.Text = LocalizationManager.Get("Str.Update.Checking");

        var result = await UpdateService.CheckForUpdateAsync(_settings.UpdateFeedUrl);
        _lastResult = result;
        ApplyCheckResult(result);

        _recheckBtn.IsEnabled = true;
    }

    private void ApplyCheckResult(UpdateCheckResult result)
    {
        string currentBare = result.CurrentVersionLabel.TrimStart('v', 'V');

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _versionHeaderText.Text = LocalizationManager.Format(
                    "Str.Update.VersionCompare",
                    result.CurrentVersionLabel,
                    result.LatestTagName ?? "");
                _statusText.Text = LocalizationManager.Format("Str.Update.Available", result.LatestTagName ?? "");

                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                {
                    _notesBox.Text = $"{LocalizationManager.Get("Str.Update.NotesHeader")}\n{result.ReleaseNotes}";
                    _notesBox.Visibility = Visibility.Visible;
                }

                var (assetUrl, _, _) = UpdateService.SelectPreferredAsset(result);
                _installNowBtn.IsEnabled = !string.IsNullOrWhiteSpace(assetUrl);
                if (string.IsNullOrWhiteSpace(assetUrl))
                {
                    _statusText.Text = LocalizationManager.Get("Str.Update.NoDirectAsset");
                }

                _updateActionPanel.Visibility = Visibility.Visible;
                _manualDownloadBtn.Visibility = Visibility.Visible;
                _recheckBtn.Visibility = Visibility.Collapsed;
                _closeBtn.Visibility = Visibility.Collapsed;
                break;

            case UpdateCheckStatus.UpToDate:
                _versionHeaderText.Text = LocalizationManager.Format("Str.Update.CurrentVer", currentBare);
                _statusText.Text = LocalizationManager.Format("Str.Update.UpToDate", currentBare);
                _updateActionPanel.Visibility = Visibility.Collapsed;
                _manualDownloadBtn.Visibility = Visibility.Collapsed;
                _recheckBtn.Visibility = Visibility.Visible;
                _closeBtn.Visibility = Visibility.Visible;
                break;

            default:
                _versionHeaderText.Text = LocalizationManager.Format("Str.Update.CurrentVer", currentBare);
                _statusText.Text = LocalizationManager.Format("Str.Update.OfflineMsg", result.ErrorMessage ?? "Network Error", result.CurrentVersionLabel);
                _updateActionPanel.Visibility = Visibility.Collapsed;
                _manualDownloadBtn.Visibility = Visibility.Visible;
                _recheckBtn.Visibility = Visibility.Visible;
                _closeBtn.Visibility = Visibility.Visible;
                break;
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        if (_isDownloading || _lastResult is null) return;

        var (assetUrl, fileName, isSetupInstaller) = UpdateService.SelectPreferredAsset(_lastResult);
        if (string.IsNullOrWhiteSpace(assetUrl) || string.IsNullOrWhiteSpace(fileName))
        {
            _statusText.Text = LocalizationManager.Get("Str.Update.NoDirectAsset");
            return;
        }

        _isDownloading = true;
        _installNowBtn.IsEnabled = false;
        _skipVersionBtn.IsEnabled = false;
        _recheckBtn.IsEnabled = false;

        _progressBar.IsIndeterminate = false;
        _progressBar.Value = 0;
        _progressContainer.Visibility = Visibility.Visible;
        _statusText.Text = LocalizationManager.Get("Str.Update.Downloading");

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        try
        {
            string tempDir = UpdateService.GetTempUpdateDirectory();
            string destPath = Path.Combine(tempDir, fileName);

            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                if (p.TotalBytes is > 0)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = p.Percent;
                    _progressLabel.Text = LocalizationManager.Format(
                        "Str.Update.ProgressKnown",
                        p.Percent,
                        p.DownloadedMb,
                        p.TotalMb ?? 0.0);
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                    _progressLabel.Text = LocalizationManager.Format("Str.Update.ProgressUnknown", p.DownloadedMb);
                }
            });

            await UpdateService.DownloadFileWithProgressAsync(assetUrl, destPath, progress, ct);

            // Trước khi đóng ứng dụng để cài đặt: kiểm tra IsDirty và hỏi lưu nếu cần!
            if (_prepareForUpdateExit is not null && !_prepareForUpdateExit())
            {
                _statusText.Text = LocalizationManager.Get("Str.Update.InstallCanceled");
                return;
            }

            if (isSetupInstaller)
            {
                _statusText.Text = LocalizationManager.Get("Str.Update.LaunchingInstaller");
                UpdateService.LaunchSetupInstaller(destPath);
            }
            else
            {
                _progressBar.IsIndeterminate = true;
                _statusText.Text = LocalizationManager.Get("Str.Update.Extracting");
                string batPath = await Task.Run(() => UpdateService.PreparePortableUpdateScript(destPath), ct);

                _statusText.Text = LocalizationManager.Get("Str.Update.LaunchingInstaller");
                UpdateService.LaunchPortableUpdaterBatch(batPath);
            }

            // Đóng toàn bộ ứng dụng ngay lập tức để trình cài đặt / script .bat thay thế file
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            // Người dùng đã đóng hộp thoại trong khi đang tải
        }
        catch (Exception ex)
        {
            _progressBar.IsIndeterminate = false;
            _statusText.Text = LocalizationManager.Format("Str.Update.DownloadFailed", ex.Message);
        }
        finally
        {
            _isDownloading = false;
            _installNowBtn.IsEnabled = true;
            _skipVersionBtn.IsEnabled = true;
            _recheckBtn.IsEnabled = true;
        }
    }

    private void OpenManualReleasePage()
    {
        string url = _lastResult?.ReleasePageUrl ?? UpdateService.DefaultReleasesPageUrl;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
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
