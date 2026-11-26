using System.ComponentModel;
using System.Windows;
using WiringSim.Models.Components.Control;
using WiringSim.UI.Helpers;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Cửa sổ nổi tự do cho Trình lập trình PLC (Ladder OB1 &amp; PLC Tags):
/// - Hỗ trợ kéo di chuyển sang mọi màn hình, kéo giãn 4 cạnh/góc (MinWidth 680 x MinHeight 460).
/// - Đủ 3 nút điều khiển cửa sổ: Minimize (xuống Taskbar), Maximize/Restore (chuẩn WM_GETMINMAXINFO), Close (ẩn cửa sổ giữ nguyên chương trình).
/// </summary>
public partial class PlcLadderWindow : Window
{
    private bool _allowRealClose;

    public PlcLadderWindow()
    {
        InitializeComponent();

        LadderEditor.ShowEmbeddedHeader = false;
        LadderEditor.HideRequested += Hide;

        WindowChromeHelper.Attach(this, OuterWindowBorder, TitleBarBorder, statusBarBorder: null, BtnMaximizeRestore);

        ThemeManager.ThemeChanged += OnThemeChanged;
        LocalizationManager.LanguageChanged += UpdateWindowHeaderText;
        UpdateWindowHeaderText();
    }

    public void BindAndPresent(PlcControllerComponent? plc, bool openTagTable, bool isSimulating)
    {
        LadderEditor.BindPlc(plc, openTagTable);
        LadderEditor.SetSimulationMode(isSimulating);
        UpdateWindowHeaderText();

        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    public void UpdateWindowHeaderText()
    {
        var plc = LadderEditor.ActivePlc;
        string baseTitle = LocalizationManager.Get("Str.Plc.WindowTitle");
        string full = plc is not null ? $"{baseTitle}  —  {plc.DisplayName}" : baseTitle;
        Title = full;
        TxtWindowHeader.Text = full;
    }

    public void ForceClose()
    {
        _allowRealClose = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        LocalizationManager.LanguageChanged -= UpdateWindowHeaderText;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            // Đóng nút X chỉ ẩn cửa sổ để người dùng mở lại bất cứ lúc nào mà không mất chương trình đang soạn!
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnThemeChanged()
    {
        ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
