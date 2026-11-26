using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WiringSim.Models.Components.Control;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Hộp thoại lập trình &amp; bảng điều khiển Keypad Biến tần (VFD / Inverter):
/// - Màn hình LED/LCD lớn hiển thị tần số (Hz), dòng điện (A), trạng thái RUN/STOP, chiều quay FWD/REV và mã tham số P01–P06.
/// - Cụm phím cứng công nghiệp: RUN (Xanh), STOP/RESET (Đỏ), Đảo chiều FWD/REV, MODE/PRG, ▲, ▼, SET/ENTER.
/// - Núm chiết áp ảo (P02 = 1) và thanh giả lập chiết áp ngoài 10V-AI1-GND (P02 = 2).
/// - Bảng chọn nhanh thông số P01–P06 đồng bộ 100% Theme Sáng/Tối &amp; Đa ngôn ngữ.
/// </summary>
public sealed class VfdKeypadDialog : Window
{
    private enum KeypadDisplayMode
    {
        MonitorFreq,
        MonitorCurrent,
        ParamSelect,
        ParamEdit
    }

    private readonly VfdComponent _vfd;
    private readonly Action _onVfdChanged;
    private readonly DispatcherTimer _refreshTimer;

    private static readonly Dictionary<string, SolidColorBrush> CachedBrushes = new(StringComparer.OrdinalIgnoreCase);

    private static SolidColorBrush GetBrush(string hex)
    {
        if (CachedBrushes.TryGetValue(hex, out var b)) return b;
        b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        CachedBrushes[hex] = b;
        return b;
    }

    private KeypadDisplayMode _displayMode = KeypadDisplayMode.MonitorFreq;
    private int _selectedParamIndex = 1; // 1..6 (P01..P06), 7 = P06_DI2, 8 = P07 (Modbus Slave ID)
    private double _editingValue;

    private readonly TextBlock _ledMainText;
    private readonly TextBlock _ledSubText;
    private readonly Border _runBadge;
    private readonly TextBlock _runBadgeText;
    private readonly Border _dirBadge;
    private readonly TextBlock _dirBadgeText;
    private readonly TextBlock _powerStatusText;
    private readonly TextBlock _paramHintText;
    private readonly Slider _dialSlider;
    private readonly TextBlock _dialValueText;
    private readonly Slider _extPotSlider;
    private readonly TextBlock _extPotValueText;
    private readonly StackPanel _paramRowsPanel;

    private bool _suppressSliderEvents;

    public VfdComponent TargetVfd => _vfd;

    public VfdKeypadDialog(Window? owner, VfdComponent vfd, Action onVfdChanged)
    {
        Owner = owner;
        _vfd = vfd;
        _onVfdChanged = onVfdChanged;

        Width = 510;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Title = LocalizationManager.Format("Str.Vfd.DialogTitle", vfd.DisplayName);
        Icon = ThemeManager.GetAppIcon();
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };

        // 1. Màn hình LED/LCD lớn kiểu biến tần công nghiệp
        var screenBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#070B14")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")!),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var screenGrid = new Grid();
        screenGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        screenGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        screenGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Thanh LED báo trạng thái trên cùng màn hình
        var topIndicators = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var leftBadges = new StackPanel { Orientation = Orientation.Horizontal };

        _runBadgeText = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        _runBadge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), Child = _runBadgeText };
        leftBadges.Children.Add(_runBadge);

        _dirBadgeText = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        _dirBadge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Child = _dirBadgeText };
        leftBadges.Children.Add(_dirBadge);

        DockPanel.SetDock(leftBadges, Dock.Left);
        topIndicators.Children.Add(leftBadges);

        _powerStatusText = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        topIndicators.Children.Add(_powerStatusText);
        Grid.SetRow(topIndicators, 0);
        screenGrid.Children.Add(topIndicators);

        // Chữ số LED 7 đoạn cỡ lớn
        _ledMainText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22D3EE")!),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetRow(_ledMainText, 1);
        screenGrid.Children.Add(_ledMainText);

        _ledSubText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")!),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(_ledSubText, 2);
        screenGrid.Children.Add(_ledSubText);

        screenBorder.Child = screenGrid;
        root.Children.Add(screenBorder);

        // Dòng mô tả nhanh thông số đang chọn
        _paramHintText = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 10)
        };
        _paramHintText.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        root.Children.Add(_paramHintText);

        // 2. Cụm phím cứng Keypad (MODE/PRG, ▲, ▼, SET/ENTER + RUN, STOP/RESET, FWD/REV)
        var keypadGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int c = 0; c < 4; c++) keypadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keypadGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        keypadGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var btnMode = CreateKeyButton(LocalizationManager.Get("Str.Vfd.ModeBtn"), "#334155", "#64748B", OnModePress);
        var btnUp = CreateKeyButton("▲", "#1E293B", "#38BDF8", () => AdjustValue(+1));
        var btnDown = CreateKeyButton("▼", "#1E293B", "#38BDF8", () => AdjustValue(-1));
        var btnSet = CreateKeyButton(LocalizationManager.Get("Str.Vfd.SetBtn"), "#1D4ED8", "#60A5FA", OnSetPress);

        Grid.SetRow(btnMode, 0); Grid.SetColumn(btnMode, 0);
        Grid.SetRow(btnUp, 0); Grid.SetColumn(btnUp, 1);
        Grid.SetRow(btnDown, 0); Grid.SetColumn(btnDown, 2);
        Grid.SetRow(btnSet, 0); Grid.SetColumn(btnSet, 3);

        var btnRun = CreateKeyButton("▶ RUN", "#15803D", "#4ADE80", OnRunPress);
        var btnStop = CreateKeyButton("■ STOP / RESET", "#B91C1C", "#F87171", OnStopPress);
        var btnDir = CreateKeyButton(LocalizationManager.Get("Str.Vfd.DirBtn"), "#9A3412", "#FB923C", OnToggleDirectionPress);

        Grid.SetRow(btnRun, 1); Grid.SetColumn(btnRun, 0);
        Grid.SetRow(btnStop, 1); Grid.SetColumn(btnStop, 1); Grid.SetColumnSpan(btnStop, 2);
        Grid.SetRow(btnDir, 1); Grid.SetColumn(btnDir, 3);

        keypadGrid.Children.Add(btnMode);
        keypadGrid.Children.Add(btnUp);
        keypadGrid.Children.Add(btnDown);
        keypadGrid.Children.Add(btnSet);
        keypadGrid.Children.Add(btnRun);
        keypadGrid.Children.Add(btnStop);
        keypadGrid.Children.Add(btnDir);
        root.Children.Add(keypadGrid);

        // 3. Núm chiết áp ảo trên Keypad (P02 = 1) & thanh chiết áp ngoài AI1 (P02 = 2)
        var potCard = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        potCard.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        potCard.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var potStack = new StackPanel();
        var dialHeader = new DockPanel();
        var dialLbl = new TextBlock { Text = LocalizationManager.Get("Str.Vfd.PotLabel"), FontSize = 11.2, FontWeight = FontWeights.SemiBold };
        dialLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _dialValueText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
        _dialValueText.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        DockPanel.SetDock(dialLbl, Dock.Left);
        dialHeader.Children.Add(dialLbl);
        dialHeader.Children.Add(_dialValueText);
        potStack.Children.Add(dialHeader);

        _dialSlider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(10.0, _vfd.P03_MaxFreqHz),
            Value = Math.Clamp(_vfd.DialFreqHz, 0, _vfd.P03_MaxFreqHz),
            TickFrequency = 1.0,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 4, 0, 8)
        };
        _dialSlider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents) return;
            _vfd.DialFreqHz = Math.Round(_dialSlider.Value, 1);
            _dialValueText.Text = $"{_vfd.DialFreqHz:0.0} Hz";
            NotifyUpdated();
        };
        potStack.Children.Add(_dialSlider);

        var extHeader = new DockPanel();
        var extLbl = new TextBlock { Text = LocalizationManager.Get("Str.Vfd.ExtPotLabel"), FontSize = 11.2, FontWeight = FontWeights.SemiBold };
        extLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _extPotValueText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
        _extPotValueText.SetResourceReference(TextBlock.ForegroundProperty, "AccentGreenBrush");
        DockPanel.SetDock(extLbl, Dock.Left);
        extHeader.Children.Add(extLbl);
        extHeader.Children.Add(_extPotValueText);
        potStack.Children.Add(extHeader);

        _extPotSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_vfd.ExternalAnalogPercent, 0, 100),
            Margin = new Thickness(0, 4, 0, 0)
        };
        _extPotSlider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents) return;
            _vfd.ExternalAnalogPercent = Math.Round(_extPotSlider.Value, 0);
            double hz = _vfd.P03_MaxFreqHz * (_vfd.ExternalAnalogPercent / 100.0);
            _extPotValueText.Text = $"{_vfd.ExternalAnalogPercent:0}% ({hz:0.0} Hz)";
            NotifyUpdated();
        };
        potStack.Children.Add(_extPotSlider);

        potCard.Child = potStack;
        root.Children.Add(potCard);

        // 4. Bảng thông số P01 – P06 (click trực tiếp để chọn & sửa nhanh)
        var paramHdr = new TextBlock
        {
            Text = LocalizationManager.Get("Str.Vfd.ParamTableTitle"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 2, 5)
        };
        paramHdr.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        root.Children.Add(paramHdr);

        var tableBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        tableBorder.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        tableBorder.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        _paramRowsPanel = new StackPanel();
        tableBorder.Child = _paramRowsPanel;
        root.Children.Add(tableBorder);

        // Nút Đóng
        var closeBtn = new Button
        {
            Content = LocalizationManager.Get("Str.Dialog.Close"),
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        closeBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        closeBtn.Click += (_, _) => Close();
        root.Children.Add(closeBtn);

        Content = root;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += (_, _) => RefreshDisplay();
        _refreshTimer.Start();

        Closed += (_, _) => _refreshTimer.Stop();

        RebuildParamRows();
        RefreshDisplay();
    }

    private Button CreateKeyButton(string label, string bgHex, string borderHex, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 36,
            Margin = new Thickness(3),
            FontSize = 11.8,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex)!),
            BorderThickness = new Thickness(1.3),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!);
        btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex)!);
        btn.Foreground = Brushes.White;
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void OnModePress()
    {
        _displayMode = _displayMode switch
        {
            KeypadDisplayMode.MonitorFreq => KeypadDisplayMode.MonitorCurrent,
            KeypadDisplayMode.MonitorCurrent => KeypadDisplayMode.ParamSelect,
            KeypadDisplayMode.ParamSelect => KeypadDisplayMode.MonitorFreq,
            KeypadDisplayMode.ParamEdit => KeypadDisplayMode.ParamSelect,
            _ => KeypadDisplayMode.MonitorFreq
        };
        RebuildParamRows();
        RefreshDisplay();
    }

    private void OnSetPress()
    {
        if (_displayMode == KeypadDisplayMode.ParamSelect)
        {
            _editingValue = ReadParamValue(_selectedParamIndex);
            _displayMode = KeypadDisplayMode.ParamEdit;
        }
        else if (_displayMode == KeypadDisplayMode.ParamEdit)
        {
            WriteParamValue(_selectedParamIndex, _editingValue);
            _displayMode = KeypadDisplayMode.ParamSelect;
            SyncSlidersFromVfd();
            NotifyUpdated();
        }
        else
        {
            _displayMode = KeypadDisplayMode.ParamSelect;
        }
        RebuildParamRows();
        RefreshDisplay();
    }

    private void AdjustValue(int deltaSign)
    {
        if (_displayMode is KeypadDisplayMode.MonitorFreq or KeypadDisplayMode.MonitorCurrent)
        {
            _vfd.KeypadTargetFreqHz = Math.Clamp(Math.Round(_vfd.KeypadTargetFreqHz + deltaSign * 1.0, 1), 0.0, _vfd.P03_MaxFreqHz);
            NotifyUpdated();
        }
        else if (_displayMode == KeypadDisplayMode.ParamSelect)
        {
            _selectedParamIndex += deltaSign;
            if (_selectedParamIndex > 8) _selectedParamIndex = 1;
            if (_selectedParamIndex < 1) _selectedParamIndex = 8;
            RebuildParamRows();
        }
        else if (_displayMode == KeypadDisplayMode.ParamEdit)
        {
            _editingValue = StepParamValue(_selectedParamIndex, _editingValue, deltaSign);
        }
        RefreshDisplay();
    }

    private void OnRunPress()
    {
        _vfd.KeypadRunCommand = true;
        NotifyUpdated();
        RefreshDisplay();
    }

    private void OnStopPress()
    {
        _vfd.StopAndResetFault();
        NotifyUpdated();
        RefreshDisplay();
    }

    private void OnToggleDirectionPress()
    {
        _vfd.KeypadReverse = !_vfd.KeypadReverse;
        NotifyUpdated();
        RefreshDisplay();
    }

    private double ReadParamValue(int idx) => idx switch
    {
        1 => _vfd.P01_RunSource,
        2 => _vfd.P02_FreqSource,
        3 => _vfd.P03_MaxFreqHz,
        4 => _vfd.P04_AccelSeconds,
        5 => _vfd.P05_DecelSeconds,
        6 => _vfd.P06_Di1FreqHz,
        7 => _vfd.P06_Di2FreqHz,
        8 => _vfd.ModbusSlaveId,
        _ => 0
    };

    private void WriteParamValue(int idx, double val)
    {
        switch (idx)
        {
            case 1: _vfd.P01_RunSource = Math.Clamp((int)Math.Round(val), 0, 2); break;
            case 2: _vfd.P02_FreqSource = Math.Clamp((int)Math.Round(val), 0, 3); break;
            case 3:
                _vfd.P03_MaxFreqHz = Math.Clamp(val, 10.0, 400.0);
                _vfd.KeypadTargetFreqHz = Math.Min(_vfd.KeypadTargetFreqHz, _vfd.P03_MaxFreqHz);
                _vfd.DialFreqHz = Math.Min(_vfd.DialFreqHz, _vfd.P03_MaxFreqHz);
                break;
            case 4: _vfd.P04_AccelSeconds = Math.Clamp(val, 0.5, 60.0); break;
            case 5: _vfd.P05_DecelSeconds = Math.Clamp(val, 0.5, 60.0); break;
            case 6: _vfd.P06_Di1FreqHz = Math.Clamp(val, 0.0, _vfd.P03_MaxFreqHz); break;
            case 7: _vfd.P06_Di2FreqHz = Math.Clamp(val, 0.0, _vfd.P03_MaxFreqHz); break;
            case 8: _vfd.ModbusSlaveId = Math.Clamp((int)Math.Round(val), 1, 247); break;
        }
    }

    private double StepParamValue(int idx, double current, int sign) => idx switch
    {
        1 => ((int)Math.Round(current) + sign + 3) % 3,
        2 => ((int)Math.Round(current) + sign + 4) % 4,
        3 => Math.Clamp(Math.Round(current + sign * 5.0, 1), 10.0, 400.0),
        4 => Math.Clamp(Math.Round(current + sign * 0.5, 1), 0.5, 60.0),
        5 => Math.Clamp(Math.Round(current + sign * 0.5, 1), 0.5, 60.0),
        6 => Math.Clamp(Math.Round(current + sign * 1.0, 1), 0.0, _vfd.P03_MaxFreqHz),
        7 => Math.Clamp(Math.Round(current + sign * 1.0, 1), 0.0, _vfd.P03_MaxFreqHz),
        8 => Math.Clamp((int)Math.Round(current) + sign, 1, 247),
        _ => current
    };

    private string FormatParamCode(int idx) => idx switch
    {
        1 => "P01",
        2 => "P02",
        3 => "P03",
        4 => "P04",
        5 => "P05",
        6 => "P06-1",
        7 => "P06-2",
        8 => "P07",
        _ => "P01"
    };

    private string FormatParamDisplayValue(int idx, double val) => idx switch
    {
        1 => (int)Math.Round(val) switch { 1 => "1 (EXT)", 2 => "2 (MODBUS)", _ => "0 (KEYPAD)" },
        2 => (int)Math.Round(val) switch { 1 => "1 (DIAL)", 2 => "2 (AI1)", 3 => "3 (MODBUS)", _ => "0 (KEY)" },
        3 => $"{val:0.0} Hz",
        4 => $"{val:0.0} s",
        5 => $"{val:0.0} s",
        6 => $"{val:0.0} Hz",
        7 => $"{val:0.0} Hz",
        8 => $"ID {(int)Math.Round(val)}",
        _ => val.ToString("0.0", CultureInfo.InvariantCulture)
    };

    private void RebuildParamRows()
    {
        _paramRowsPanel.Children.Clear();
        (int Idx, string LabelKey, string ValueText)[] rows =
        [
            (1, "Str.Vfd.P01", FormatParamDisplayValue(1, _vfd.P01_RunSource)),
            (2, "Str.Vfd.P02", FormatParamDisplayValue(2, _vfd.P02_FreqSource)),
            (3, "Str.Vfd.P03", FormatParamDisplayValue(3, _vfd.P03_MaxFreqHz)),
            (4, "Str.Vfd.P04", FormatParamDisplayValue(4, _vfd.P04_AccelSeconds)),
            (5, "Str.Vfd.P05", FormatParamDisplayValue(5, _vfd.P05_DecelSeconds)),
            (6, "Str.Vfd.P06", $"DI1={_vfd.P06_Di1FreqHz:0.#}Hz | DI2={_vfd.P06_Di2FreqHz:0.#}Hz"),
            (8, "Str.Vfd.P07", FormatParamDisplayValue(8, _vfd.ModbusSlaveId))
        ];

        foreach (var (idx, key, valText) in rows)
        {
            bool active = (_displayMode is KeypadDisplayMode.ParamSelect or KeypadDisplayMode.ParamEdit) &&
                          (_selectedParamIndex == idx || (idx == 6 && _selectedParamIndex == 7));

            var rowBtn = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(active ? 1.2 : 0),
                Background = active
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2238BDF8")!)
                    : Brushes.Transparent,
                BorderBrush = active
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")!)
                    : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var dock = new DockPanel();
            var rightVal = new TextBlock
            {
                Text = valText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.2,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            rightVal.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
            DockPanel.SetDock(rightVal, Dock.Right);
            dock.Children.Add(rightVal);

            var leftDesc = new TextBlock
            {
                Text = LocalizationManager.Get(key),
                FontSize = 10.8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftDesc.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            dock.Children.Add(leftDesc);

            rowBtn.Content = dock;
            int captureIdx = idx;
            rowBtn.Click += (_, _) =>
            {
                if (captureIdx == 6 && _selectedParamIndex == 6 && _displayMode == KeypadDisplayMode.ParamEdit)
                    _selectedParamIndex = 7; // bấm lần 2 vào P06 chuyển sang sửa DI2
                else
                    _selectedParamIndex = captureIdx;

                _editingValue = ReadParamValue(_selectedParamIndex);
                _displayMode = KeypadDisplayMode.ParamEdit;
                RebuildParamRows();
                RefreshDisplay();
            };

            _paramRowsPanel.Children.Add(rowBtn);
        }
    }

    private void SyncSlidersFromVfd()
    {
        _suppressSliderEvents = true;
        _dialSlider.Maximum = Math.Max(10.0, _vfd.P03_MaxFreqHz);
        _dialSlider.Value = Math.Clamp(_vfd.DialFreqHz, 0.0, _vfd.P03_MaxFreqHz);
        _extPotSlider.Value = Math.Clamp(_vfd.ExternalAnalogPercent, 0.0, 100.0);
        _suppressSliderEvents = false;
    }

    private void RefreshDisplay()
    {
        _dialValueText.Text = $"{_vfd.DialFreqHz:0.0} Hz";
        double extHz = _vfd.P03_MaxFreqHz * (_vfd.ExternalAnalogPercent / 100.0);
        _extPotValueText.Text = $"{_vfd.ExternalAnalogPercent:0}% ({extHz:0.0} Hz)";

        // Trạng thái RUN/STOP & nguồn (dùng frozen cached brushes tránh cấp phát GC 10Hz)
        bool isRunning = _vfd.IsRunning;
        _runBadge.Background = GetBrush(isRunning ? "#16A34A" : "#475569");
        _runBadgeText.Text = isRunning
            ? LocalizationManager.Get("Str.Vfd.StatusRun")
            : LocalizationManager.Get("Str.Vfd.StatusStop");

        bool isRev = _vfd.CurrentSignedFreqHz < -0.05
                     || ((_vfd.P01_RunSource == 2 || _vfd.ModbusControlActive) && _vfd.ModbusReverse)
                     || (_vfd.P01_RunSource == 0 && !_vfd.ModbusControlActive && _vfd.KeypadReverse)
                     || (_vfd.P01_RunSource == 1 && !_vfd.ModbusControlActive && _vfd.IsExternalRevActive);
        _dirBadge.Background = GetBrush(isRev ? "#EA580C" : "#0284C7");
        _dirBadgeText.Text = isRev ? "↺ REV" : "↻ FWD";

        _powerStatusText.Text = _vfd.IsPowered
            ? (_vfd.ModbusControlActive ? $"PWR OK • MB#{_vfd.ModbusSlaveId}" : "PWR OK")
            : LocalizationManager.Get("Str.Vfd.StatusNoPower");
        _powerStatusText.Foreground = GetBrush(_vfd.IsPowered ? "#4ADE80" : "#F87171");

        // Nội dung màn hình chính
        string paramHintKey = _selectedParamIndex switch
        {
            7 => "Str.Vfd.P06",
            8 => "Str.Vfd.P07",
            _ => $"Str.Vfd.P0{_selectedParamIndex}"
        };
        switch (_displayMode)
        {
            case KeypadDisplayMode.MonitorFreq:
                _ledMainText.Foreground = GetBrush(_vfd.IsPowered ? "#22D3EE" : "#64748B");
                _ledMainText.Text = $"{_vfd.CurrentOutputFreqHz:00.0} Hz";
                _ledSubText.Text = $"SET: {_vfd.ComputeTargetFrequencyHz():0.0} Hz  |  OUT: {_vfd.OutputCurrentAmps:0.0} A  |  P01={_vfd.P01_RunSource}  P02={_vfd.P02_FreqSource}  MB={_vfd.ModbusSlaveId}";
                _paramHintText.Text = $"▲/▼: {_vfd.KeypadTargetFreqHz:0.0} Hz  •  ACC={_vfd.P04_AccelSeconds:0.#}s  •  DEC={_vfd.P05_DecelSeconds:0.#}s";
                break;

            case KeypadDisplayMode.MonitorCurrent:
                _ledMainText.Foreground = GetBrush("#F472B6");
                _ledMainText.Text = $"{_vfd.OutputCurrentAmps:00.0} A";
                _ledSubText.Text = $"FREQ: {_vfd.CurrentOutputFreqHz:0.0} Hz  |  DI1:{(_vfd.IsExternalDi1Active ? "ON" : "OFF")}  DI2:{(_vfd.IsExternalDi2Active ? "ON" : "OFF")}";
                _paramHintText.Text = LocalizationManager.Get("Str.Vfd.ParamTableTitle");
                break;

            case KeypadDisplayMode.ParamSelect:
                _ledMainText.Foreground = GetBrush("#FACC15");
                _ledMainText.Text = FormatParamCode(_selectedParamIndex);
                _ledSubText.Text = $"VAL: {FormatParamDisplayValue(_selectedParamIndex, ReadParamValue(_selectedParamIndex))}  (SET / ENTER → EDIT)";
                _paramHintText.Text = LocalizationManager.Get(paramHintKey);
                break;

            case KeypadDisplayMode.ParamEdit:
                _ledMainText.Foreground = GetBrush("#4ADE80");
                _ledMainText.Text = FormatParamDisplayValue(_selectedParamIndex, _editingValue);
                _ledSubText.Text = $"EDIT {FormatParamCode(_selectedParamIndex)}  —  ▲/▼  →  SET / ENTER";
                _paramHintText.Text = LocalizationManager.Get(paramHintKey);
                break;
        }
    }

    private void NotifyUpdated()
    {
        _onVfdChanged();
    }
}
