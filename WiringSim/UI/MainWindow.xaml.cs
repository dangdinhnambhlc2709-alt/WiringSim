using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Enums;
using WiringSim.Models.Presets;
using WiringSim.Models.Serialization;
using WiringSim.Simulation;
using WiringSim.UI.Dialogs;
using WiringSim.UI.Services;

namespace WiringSim.UI;

/// <summary>
/// Cửa sổ chính WiringSim (v3.0 Professional):
/// - Khung cửa sổ tùy biến WindowChrome + DwmSetWindowAttribute đồng bộ 100% Sáng/Tối.
/// - Thanh công cụ CAD/EDA (Mới, Mở, Lưu, Mạch mẫu | Undo, Redo, Xóa | Kiểu dây &amp; 5 màu | Thiết kế / Mô phỏng).
/// - Vùng vẽ trống hoàn toàn khi vừa khởi động (không tự nạp mạch mẫu).
/// - Thanh Thư viện bên trái có nút ẩn/hiện (◀/▶) và kéo thả có bóng mờ trực quan.
/// </summary>
public partial class MainWindow : Window
{
    private readonly CircuitSimulationEngine _simEngine = new();
    private readonly CircuitHistoryManager _history = new();
    private readonly UserSettingsModel _settings;
    private readonly Dictionary<string, VfdKeypadDialog> _openVfdDialogs = new();

    private Point _paletteDragStart;
    private bool _isUiInitialized;
    private bool _isPaletteCollapsed;
    private bool _isApplyingHistory;
    private bool _isDirty;
    private string? _openedFileName;

    // --- Win32 WM_GETMINMAXINFO để Maximize không bị Windows Taskbar che mất thanh Status Bar ---
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WinPoint PtReserved;
        public WinPoint PtMaxSize;
        public WinPoint PtMaxPosition;
        public WinPoint PtMinTrackSize;
        public WinPoint PtMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int CbSize;
        public WinRect RcMonitor;
        public WinRect RcWork;
        public int DwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    public MainWindow()
    {
        _settings = AppSettingsStore.Load();

        InitializeComponent();
        _isUiInitialized = true;
        TxtAppVersionBadge.Text = UpdateService.GetCurrentInformationalVersion();

        // Khởi tạo giao diện & ngôn ngữ theo cấu hình người dùng đã lưu
        ThemeManager.Initialize(_settings.Theme);
        LocalizationManager.SetLanguage(_settings.Language);

        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);
            ApplyWindowCornerState();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hwnd)?.AddHook(WindowProc);
            }
        };
        StateChanged += OnWindowStateChanged;

        BuildPaletteItems();
        BuildPresetContextMenu();
        WireUpEvents();
        UpdateCircuitStats();

        // Khởi động với vùng Canvas TRỐNG HOÀN TOÀN để bắt đầu thiết kế ngay!
        _isDirty = false;
        UpdateProjectTitleDisplay();
        _history.Reset(WiringCanvas.Components, WiringCanvas.Wires);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    WinRect work = info.RcWork;
                    WinRect mon = info.RcMonitor;

                    mmi.PtMaxPosition.X = Math.Abs(work.Left - mon.Left);
                    mmi.PtMaxPosition.Y = Math.Abs(work.Top - mon.Top);
                    mmi.PtMaxSize.X = Math.Abs(work.Right - work.Left);
                    mmi.PtMaxSize.Y = Math.Abs(work.Bottom - work.Top);

                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    private void WireUpEvents()
    {
        WiringCanvas.StatusMessageChanged += msg => TxtStatusMessage.Text = msg;
        WiringCanvas.ZoomChanged += z => BtnZoomReset.Content = $"{Math.Round(z * 100)}%";
        WiringCanvas.SelectionChanged += () =>
        {
            BtnDeleteSelected.IsEnabled = WiringCanvas.CurrentMode == AppMode.Design && WiringCanvas.HasSelection;
        };

        WiringCanvas.CircuitModified += () =>
        {
            UpdateCircuitStats();
            if (!_isApplyingHistory)
            {
                _isDirty = true;
                UpdateProjectTitleDisplay();
                _history.RecordSnapshot(WiringCanvas.Components, WiringCanvas.Wires);
            }
        };

        WiringCanvas.OpenVfdKeypadRequested += OpenVfdKeypadDialog;

        _history.StateChanged += () =>
        {
            bool canEdit = WiringCanvas.CurrentMode == AppMode.Design;
            BtnUndo.IsEnabled = canEdit && _history.CanUndo;
            BtnRedo.IsEnabled = canEdit && _history.CanRedo;
        };

        WiringCanvas.UserInteractedInSimulation += () => _simEngine.StepSimulation(0.0);

        _simEngine.SimulationTickCompleted += () =>
        {
            WiringCanvas.SetFaultOverlay(_simEngine.ActiveFault);
            WiringCanvas.InvalidateVisual();
        };

        _simEngine.ShortCircuitDetected += ShowShortCircuitBanner;
        _simEngine.ComponentFaultAlert += reason =>
        {
            GuideBanner.Visibility = Visibility.Collapsed;
            TxtAlertBannerTitle.Text = LocalizationManager.Get("Str.Fault.BurnoutHeader");
            TxtFaultReason.Text = reason;
            TxtFaultLocation.Text = LocalizationManager.Get("Str.Fault.BurnoutHint");
            ShortCircuitBanner.Visibility = Visibility.Visible;
            TxtStatusMessage.Text = reason;
        };

        ThemeManager.ThemeChanged += () =>
        {
            ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);
            WiringCanvas.InvalidateVisual();
        };

        LocalizationManager.LanguageChanged += OnLanguageChanged;
        PreviewKeyDown += OnMainWindowPreviewKeyDown;
    }

    private void OnLanguageChanged()
    {
        BuildPaletteItems();
        BuildPresetContextMenu();
        UpdateWireStyleButtonText();
        UpdateProjectTitleDisplay();
        UpdateCircuitStats();

        TxtModeBadge.Text = WiringCanvas.CurrentMode == AppMode.Simulation
            ? LocalizationManager.Get("Str.Badge.Simulate")
            : LocalizationManager.Get("Str.Badge.Design");

        WiringCanvas.InvalidateVisual();
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z when WiringCanvas.CurrentMode == AppMode.Design:
                    PerformUndo();
                    e.Handled = true;
                    break;
                case Key.Y when WiringCanvas.CurrentMode == AppMode.Design:
                    PerformRedo();
                    e.Handled = true;
                    break;
                case Key.N:
                    OnNewCircuitClick(sender, e);
                    e.Handled = true;
                    break;
                case Key.O:
                    OnOpenJsonClick(sender, e);
                    e.Handled = true;
                    break;
                case Key.S:
                    OnSaveJsonClick(sender, e);
                    e.Handled = true;
                    break;
            }
        }
    }

    // =========================================================================
    // ĐIỀU KHIỂN CỬA SỔ TÙY BIẾN (CUSTOM WINDOWCHROME) & CÀI ĐẶT / CẬP NHẬT
    // =========================================================================

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        ApplyWindowCornerState();
    }

    private void ApplyWindowCornerState()
    {
        if (WindowState == WindowState.Minimized)
        {
            // Giữ nguyên khung viền khi thu nhỏ xuống Taskbar để tránh nháy khung khi khôi phục
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            ThemeManager.ApplyWindowCornerPreference(this, ThemeManager.DWMWCP_DONOTROUND);
            OuterWindowBorder.Margin = new Thickness(0);
            OuterWindowBorder.BorderThickness = new Thickness(0);
            OuterWindowBorder.CornerRadius = new CornerRadius(0);
            TitleBarBorder.CornerRadius = new CornerRadius(0);
            StatusBarBorder.CornerRadius = new CornerRadius(0);
            BtnMaximizeWindow.Content = "❐";
        }
        else
        {
            ThemeManager.ApplyWindowCornerPreference(this, ThemeManager.DWMWCP_ROUND);
            OuterWindowBorder.Margin = new Thickness(0);
            OuterWindowBorder.BorderThickness = new Thickness(1);
            OuterWindowBorder.CornerRadius = new CornerRadius(8);
            TitleBarBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            StatusBarBorder.CornerRadius = new CornerRadius(0, 0, 7, 7);
            BtnMaximizeWindow.Content = "☐";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isDirty)
        {
            var choice = ConfirmSaveDialog.ShowPrompt(this);
            if (choice == UnsavedExitChoice.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (choice == UnsavedExitChoice.Save && !TrySaveCircuit())
            {
                e.Cancel = true;
                return;
            }
        }

        foreach (var dlg in _openVfdDialogs.Values.ToList())
        {
            try { dlg.Close(); } catch { }
        }
        _openVfdDialogs.Clear();

        base.OnClosing(e);
    }

    private void OpenVfdKeypadDialog(VfdComponent vfd)
    {
        if (_openVfdDialogs.TryGetValue(vfd.Id, out var existing) && existing.IsLoaded)
        {
            existing.Activate();
            return;
        }

        var dlg = new VfdKeypadDialog(this, vfd, () =>
        {
            if (WiringCanvas.CurrentMode == AppMode.Simulation)
            {
                _simEngine.StepSimulation(0.0);
            }
            else
            {
                _isDirty = true;
                UpdateProjectTitleDisplay();
                _history.RecordSnapshot(WiringCanvas.Components, WiringCanvas.Wires);
            }
            WiringCanvas.InvalidateVisual();
        });

        dlg.Closed += (_, _) => _openVfdDialogs.Remove(vfd.Id);
        _openVfdDialogs[vfd.Id] = dlg;
        dlg.Show();
    }

    private void OnMinimizeWindowClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseWindowClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        new SettingsDialog(this, _settings).ShowDialog();
    }

    private void OnOpenUpdatesClick(object sender, RoutedEventArgs e)
    {
        new UpdateDialog(this, _settings).ShowDialog();
    }

    private void UpdateProjectTitleDisplay()
    {
        string baseTitle = string.IsNullOrWhiteSpace(_openedFileName)
            ? LocalizationManager.Get("Str.Project.Untitled")
            : _openedFileName;
        TxtProjectName.Text = _isDirty ? $"{baseTitle} *" : baseTitle;
    }

    // =========================================================================
    // THƯ VIỆN THIẾT BỊ BÊN TRÁI (6 NHÓM + NÚT THU GỌN ◀ / ▶)
    // =========================================================================

    private void OnTogglePaletteClick(object sender, RoutedEventArgs e)
    {
        _isPaletteCollapsed = !_isPaletteCollapsed;
        if (_isPaletteCollapsed)
        {
            PaletteColumn.Width = new GridLength(38);
            PaletteExpandedPanel.Visibility = Visibility.Collapsed;
            PaletteCollapsedRail.Visibility = Visibility.Visible;
        }
        else
        {
            PaletteColumn.Width = new GridLength(290);
            PaletteExpandedPanel.Visibility = Visibility.Visible;
            PaletteCollapsedRail.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildPaletteItems()
    {
        PaletteContainer.Children.Clear();

        (ComponentCategory Cat, string HeaderKey)[] groups =
        [
            (ComponentCategory.PowerSource, "Str.Cat.PowerSource"),
            (ComponentCategory.SwitchingAndControl, "Str.Cat.Switching"),
            (ComponentCategory.Relay, "Str.Cat.Relay"),
            (ComponentCategory.Sensor, "Str.Cat.Sensor"),
            (ComponentCategory.Meter, "Str.Cat.Meter"),
            (ComponentCategory.Load, "Str.Cat.Load")
        ];

        foreach (var (cat, headerKey) in groups)
        {
            var sectionHeader = new TextBlock
            {
                Text = LocalizationManager.Get(headerKey),
                FontSize = 10.6,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 10, 4, 5)
            };
            sectionHeader.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            PaletteContainer.Children.Add(sectionHeader);

            foreach (var item in ComponentRegistry.Items.Where(x => x.Category == cat))
            {
                PaletteContainer.Children.Add(CreatePaletteCard(item));
            }
        }
    }

    private UIElement CreatePaletteCard(PaletteItemDescriptor item)
    {
        var accentBrush = (Brush)new BrushConverter().ConvertFromString(item.AccentHex)!;

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand,
            Tag = item
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1.3),
            CornerRadius = new CornerRadius(5),
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.BadgeText,
                Foreground = accentBrush,
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        badge.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        Grid.SetColumn(badge, 0);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleTb = new TextBlock { Text = item.Title, FontSize = 11.6, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        titleTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var subTb = new TextBlock { Text = item.Subtitle, FontSize = 9.9, TextTrimming = TextTrimming.CharacterEllipsis };
        subTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        textStack.Children.Add(titleTb);
        textStack.Children.Add(subTb);
        Grid.SetColumn(textStack, 1);

        var quickAddBtn = new Button
        {
            Content = "+",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(5, 0, 0, 0),
            ToolTip = LocalizationManager.Get("Str.Palette.QuickAddTip")
        };
        quickAddBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        quickAddBtn.Click += (_, e) =>
        {
            e.Handled = true;
            PlaceComponentInCenter(item.TypeKey);
        };
        Grid.SetColumn(quickAddBtn, 2);

        grid.Children.Add(badge);
        grid.Children.Add(textStack);
        grid.Children.Add(quickAddBtn);
        card.Child = grid;

        card.MouseEnter += (_, _) =>
        {
            card.SetResourceReference(Border.BackgroundProperty, "CardHoverBrush");
            card.BorderBrush = accentBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        };

        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _paletteDragStart = e.GetPosition(this);
            if (e.ClickCount == 2)
            {
                PlaceComponentInCenter(item.TypeKey);
                e.Handled = true;
            }
        };

        card.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Vector diff = e.GetPosition(this) - _paletteDragStart;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var data = new DataObject("WiringSim.ComponentTypeKey", item.TypeKey);
                    DragDrop.DoDragDrop(card, data, DragDropEffects.Copy);
                }
            }
        };

        return card;
    }

    private void PlaceComponentInCenter(string typeKey)
    {
        if (WiringCanvas.CurrentMode != AppMode.Design)
            RbDesignMode.IsChecked = true;

        Point centerScreen = new(WiringCanvas.ActualWidth / 2.0, WiringCanvas.ActualHeight / 2.0);
        WiringCanvas.AddComponentAtScreenPoint(typeKey, centerScreen);
    }

    // =========================================================================
    // TỆP (.JSON), MẠCH MẪU, HOÀN TÁC (UNDO) / LÀM LẠI (REDO) & XÓA
    // =========================================================================

    private void BuildPresetContextMenu()
    {
        PresetContextMenu.Items.Clear();
        foreach (var preset in PresetCircuitFactory.Presets)
        {
            var menuItem = new MenuItem { Header = preset.Name };
            menuItem.Click += (_, _) => LoadPresetCircuit(preset);
            PresetContextMenu.Items.Add(menuItem);
        }
    }

    private void OnPresetMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void LoadPresetCircuit(PresetCircuitDefinition preset)
    {
        RbDesignMode.IsChecked = true;
        HideShortCircuitBanner();

        var (comps, wires) = preset.Builder();
        _isApplyingHistory = true;
        WiringCanvas.LoadCircuit(comps, wires, autoFit: true, raiseModified: false);
        _isApplyingHistory = false;

        _openedFileName = preset.Name;
        _isDirty = false;
        UpdateProjectTitleDisplay();
        UpdateCircuitStats();
        _history.Reset(WiringCanvas.Components, WiringCanvas.Wires);
        _simEngine.BindCircuit(WiringCanvas.Components, WiringCanvas.Wires);

        TxtGuideBanner.Text = $"📘 {preset.Name}: {preset.GuideSummary}";
        GuideBanner.Visibility = Visibility.Visible;
        TxtStatusMessage.Text = LocalizationManager.Format("Str.Status.PresetLoaded", preset.Name);
    }

    private void OnNewCircuitClick(object sender, RoutedEventArgs e)
    {
        if (WiringCanvas.Components.Count > 0 || WiringCanvas.Wires.Count > 0)
        {
            var confirm = MessageBox.Show(
                this,
                LocalizationManager.Get("Str.Dialog.ConfirmNew"),
                LocalizationManager.Get("Str.Dialog.ConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        RbDesignMode.IsChecked = true;
        HideShortCircuitBanner();
        GuideBanner.Visibility = Visibility.Collapsed;

        _isApplyingHistory = true;
        WiringCanvas.ClearAll(raiseModified: false);
        _isApplyingHistory = false;

        _openedFileName = null;
        _isDirty = false;
        UpdateProjectTitleDisplay();
        UpdateCircuitStats();
        _history.Reset(WiringCanvas.Components, WiringCanvas.Wires);
        TxtStatusMessage.Text = LocalizationManager.Get("Str.Status.NewCanvas");
    }

    private void OnSaveJsonClick(object sender, RoutedEventArgs e)
    {
        TrySaveCircuit();
    }

    private bool TrySaveCircuit()
    {
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Str.Dialog.SaveTitle"),
            Filter = "WiringSim Circuit (*.json)|*.json",
            FileName = string.IsNullOrWhiteSpace(_openedFileName) ? "Circuit_WiringSim.json" : _openedFileName
        };

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                string title = Path.GetFileNameWithoutExtension(dlg.FileName);
                CircuitSerializer.SaveToFile(dlg.FileName, WiringCanvas.Components, WiringCanvas.Wires, title);
                _openedFileName = Path.GetFileName(dlg.FileName);
                _isDirty = false;
                UpdateProjectTitleDisplay();
                TxtStatusMessage.Text = LocalizationManager.Format("Str.Status.SavedFile", dlg.FileName);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationManager.Format("Str.Dialog.FileError", ex.Message),
                    LocalizationManager.Get("Str.App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        return false;
    }

    private void OnOpenJsonClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Str.Dialog.OpenTitle"),
            Filter = "WiringSim Circuit (*.json)|*.json|*.*|*.*"
        };

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                RbDesignMode.IsChecked = true;
                HideShortCircuitBanner();
                GuideBanner.Visibility = Visibility.Collapsed;

                var (comps, wires, title) = CircuitSerializer.LoadFromFile(dlg.FileName);
                _isApplyingHistory = true;
                WiringCanvas.LoadCircuit(comps, wires, autoFit: true, raiseModified: false);
                _isApplyingHistory = false;

                _openedFileName = Path.GetFileName(dlg.FileName);
                _isDirty = false;
                UpdateProjectTitleDisplay();
                UpdateCircuitStats();
                _history.Reset(WiringCanvas.Components, WiringCanvas.Wires);
                _simEngine.BindCircuit(WiringCanvas.Components, WiringCanvas.Wires);

                TxtStatusMessage.Text = LocalizationManager.Format("Str.Status.LoadedFile", title, comps.Count, wires.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationManager.Format("Str.Dialog.FileError", ex.Message),
                    LocalizationManager.Get("Str.App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => PerformUndo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => PerformRedo();
    private void OnDeleteSelectedClick(object sender, RoutedEventArgs e) => WiringCanvas.DeleteSelected();

    private void PerformUndo()
    {
        if (WiringCanvas.CurrentMode != AppMode.Design) return;
        var prev = _history.Undo();
        if (prev is null) return;

        _isApplyingHistory = true;
        WiringCanvas.LoadCircuit(prev.Value.Components, prev.Value.Wires, autoFit: false, raiseModified: false);
        _isApplyingHistory = false;
        _isDirty = true;
        UpdateProjectTitleDisplay();
        UpdateCircuitStats();
        TxtStatusMessage.Text = LocalizationManager.Get("Str.Status.Undone");
    }

    private void PerformRedo()
    {
        if (WiringCanvas.CurrentMode != AppMode.Design) return;
        var next = _history.Redo();
        if (next is null) return;

        _isApplyingHistory = true;
        WiringCanvas.LoadCircuit(next.Value.Components, next.Value.Wires, autoFit: false, raiseModified: false);
        _isApplyingHistory = false;
        _isDirty = true;
        UpdateProjectTitleDisplay();
        UpdateCircuitStats();
        TxtStatusMessage.Text = LocalizationManager.Get("Str.Status.Redone");
    }

    // =========================================================================
    // CHẾ ĐỘ, MÀU DÂY, KIỂU DÂY & CẢNH BÁO
    // =========================================================================

    private void OnModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized) return;

        if (RbSimulationMode.IsChecked == true)
        {
            WiringCanvas.SetMode(AppMode.Simulation);
            _simEngine.BindCircuit(WiringCanvas.Components, WiringCanvas.Wires);
            _simEngine.Start();

            ModeStatusBadge.SetResourceReference(Border.BackgroundProperty, "AccentGreenBrush");
            TxtModeBadge.Text = LocalizationManager.Get("Str.Badge.Simulate");
            PaletteContainer.IsEnabled = false;
            PaletteContainer.Opacity = 0.45;
            BtnUndo.IsEnabled = false;
            BtnRedo.IsEnabled = false;
            BtnDeleteSelected.IsEnabled = false;
            TxtStatusMessage.Text = LocalizationManager.Get("Str.Status.ModeSim");
        }
        else
        {
            _simEngine.Stop();
            HideShortCircuitBanner();
            WiringCanvas.SetMode(AppMode.Design);

            ModeStatusBadge.SetResourceReference(Border.BackgroundProperty, "AccentBlueBrush");
            TxtModeBadge.Text = LocalizationManager.Get("Str.Badge.Design");
            PaletteContainer.IsEnabled = true;
            PaletteContainer.Opacity = 1.0;
            BtnUndo.IsEnabled = _history.CanUndo;
            BtnRedo.IsEnabled = _history.CanRedo;
            BtnDeleteSelected.IsEnabled = WiringCanvas.HasSelection;
            TxtStatusMessage.Text = LocalizationManager.Get("Str.Status.ModeDesign");
        }
    }

    private void OnWireColorChecked(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized) return;

        if (RbWireRed.IsChecked == true) WiringCanvas.ActiveWireColor = WireColorPreset.Red_L1_DCPlus;
        else if (RbWireYellow.IsChecked == true) WiringCanvas.ActiveWireColor = WireColorPreset.Yellow_L2;
        else if (RbWireBlue.IsChecked == true) WiringCanvas.ActiveWireColor = WireColorPreset.Blue_L3;
        else if (RbWireBlack.IsChecked == true) WiringCanvas.ActiveWireColor = WireColorPreset.Black_N_DCMinus;
        else if (RbWireGreen.IsChecked == true) WiringCanvas.ActiveWireColor = WireColorPreset.Green_PE;
    }

    private void OnToggleWireStyleClick(object sender, RoutedEventArgs e)
    {
        var next = WiringCanvas.RoutingStyle == WireRoutingStyle.Orthogonal
            ? WireRoutingStyle.Curved
            : WireRoutingStyle.Orthogonal;

        WiringCanvas.SetRoutingStyle(next);
        UpdateWireStyleButtonText();
    }

    private void UpdateWireStyleButtonText()
    {
        BtnWireStyle.Content = WiringCanvas.RoutingStyle == WireRoutingStyle.Orthogonal
            ? LocalizationManager.Get("Str.Toolbar.WireOrthogonal")
            : LocalizationManager.Get("Str.Toolbar.WireCurved");
    }

    private void ShowShortCircuitBanner(ShortCircuitFaultInfo fault)
    {
        GuideBanner.Visibility = Visibility.Collapsed;
        TxtAlertBannerTitle.Text = fault.Title;
        TxtFaultReason.Text = fault.DetailMessage;
        TxtFaultLocation.Text = fault.LocationDescription;
        ShortCircuitBanner.Visibility = Visibility.Visible;
        WiringCanvas.SetFaultOverlay(fault);
        TxtStatusMessage.Text = $"{fault.Title} {fault.DetailMessage}";
    }

    private void HideShortCircuitBanner()
    {
        ShortCircuitBanner.Visibility = Visibility.Collapsed;
        _simEngine.ClearFaultAlert();
        WiringCanvas.SetFaultOverlay(null);
    }

    private void OnDismissFaultClick(object sender, RoutedEventArgs e) => HideShortCircuitBanner();
    private void OnCloseGuideBannerClick(object sender, RoutedEventArgs e) => GuideBanner.Visibility = Visibility.Collapsed;

    private void OnSnapGridChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized) return;
        WiringCanvas.SnapToGridEnabled = ChkSnapGrid.IsChecked == true;
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => WiringCanvas.ZoomByStep(1.18);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => WiringCanvas.ZoomByStep(1.0 / 1.18);
    private void OnZoomFitClick(object sender, RoutedEventArgs e) => WiringCanvas.ResetOrFitView();

    private void UpdateCircuitStats()
    {
        TxtCircuitStats.Text = LocalizationManager.Format("Str.Status.StatsFormat", WiringCanvas.Components.Count, WiringCanvas.Wires.Count);
    }
}
