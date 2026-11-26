using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

/// <summary>
/// Phần 3 của PlcLadderEditorControl (Part 2 &amp; V21):
/// - Quản lý đa khối chương trình (OB1 Main, OB100 Startup, FC, FB).
/// - Tab Force Table tách biệt chuẩn :P (có khóa bảo vệ chân PWM/HSC).
/// - Tab Diagnostics Buffer (nhật ký sự kiện CPU thời gian thực).
/// - Xuất / Nhập chương trình dạng văn bản có cấu trúc (.plctxt).
/// </summary>
public partial class PlcLadderEditorControl
{
    private PlcCodeBlock? _activeBlock; // null = OB1 (DraftProgram.Networks)
    private bool _updatingBlockCombo;
    private PlcForceEntry? _selectedForceEntry;
    private readonly Dictionary<string, TextBlock> _forceLiveTextBlocks = new();
    private int _lastRenderedDiagCount = -1;

    /// <summary>Trả về danh sách Networks của khối đang mở (OB1 hoặc OB100 / FC / FB).</summary>
    private List<LadderNetwork> GetActiveNetworks()
    {
        if (_plc is null) return new List<LadderNetwork>();
        if (_activeBlock is not null && _plc.DraftProgram.AdditionalBlocks.Contains(_activeBlock))
            return _activeBlock.Networks;
        _activeBlock = null;
        return _plc.DraftProgram.Networks;
    }

    private void RebuildBlockSelectorCombo()
    {
        if (CbActiveBlock is null || _plc is null) return;
        _updatingBlockCombo = true;
        CbActiveBlock.Items.Clear();

        var ob1Item = new ComboBoxItem { Content = "OB1 (Main)", Tag = (PlcCodeBlock?)null };
        CbActiveBlock.Items.Add(ob1Item);
        ComboBoxItem selected = ob1Item;

        foreach (var blk in _plc.DraftProgram.AdditionalBlocks)
        {
            var item = new ComboBoxItem { Content = blk.DisplayLabel, Tag = blk };
            CbActiveBlock.Items.Add(item);
            if (blk == _activeBlock) selected = item;
        }

        if (selected == ob1Item) _activeBlock = null;
        CbActiveBlock.SelectedItem = selected;
        BtnDeleteActiveBlock.IsEnabled = _activeBlock is not null;
        _updatingBlockCombo = false;
    }

    private void OnActiveBlockSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingBlockCombo || _plc is null) return;
        if (CbActiveBlock.SelectedItem is ComboBoxItem item)
        {
            _activeBlock = item.Tag as PlcCodeBlock;
            BtnDeleteActiveBlock.IsEnabled = _activeBlock is not null;
            _selectedNetworkIndex = 0;
            _selectedNode = null;
            _selectedBranch = GetActiveNetworks().FirstOrDefault()?.MainBranch;
            CancelInlineEdit(rerender: false);
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
    }

    private void OnAddOb100BlockClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var existing = _plc.DraftProgram.AdditionalBlocks.FirstOrDefault(b => b.Kind == PlcBlockKind.OB100_Startup);
        if (existing is not null)
        {
            _activeBlock = existing;
            RebuildBlockSelectorCombo();
            RenderNetworks();
            return;
        }
        PushUndoSnapshot();
        var blk = new PlcCodeBlock
        {
            Kind = PlcBlockKind.OB100_Startup,
            Number = 100,
            Name = "Startup",
            Networks = [new LadderNetwork { Title = "Startup Init" }]
        };
        _plc.DraftProgram.AdditionalBlocks.Add(blk);
        _activeBlock = blk;
        _selectedNetworkIndex = 0;
        _selectedBranch = blk.Networks[0].MainBranch;
        _selectedNode = null;
        RebuildBlockSelectorCombo();
        NotifyProgramChanged();
        RenderNetworks();
    }

    private void OnAddFcBlockClick(object sender, RoutedEventArgs e) => CreateUserBlock(PlcBlockKind.FC);
    private void OnAddFbBlockClick(object sender, RoutedEventArgs e) => CreateUserBlock(PlcBlockKind.FB);

    private void CreateUserBlock(PlcBlockKind kind)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        var usedNums = _plc.DraftProgram.AdditionalBlocks
            .Where(b => b.Kind == kind)
            .Select(b => b.Number)
            .ToHashSet();
        int num = 1;
        while (usedNums.Contains(num)) num++;
        string prefix = kind == PlcBlockKind.FC ? "FC" : "FB";
        var blk = new PlcCodeBlock
        {
            Kind = kind,
            Number = num,
            Name = $"{prefix}_{num}",
            Networks = [new LadderNetwork { Title = $"{prefix}{num} Network 1" }]
        };
        _plc.DraftProgram.AdditionalBlocks.Add(blk);
        _activeBlock = blk;
        _selectedNetworkIndex = 0;
        _selectedBranch = blk.Networks[0].MainBranch;
        _selectedNode = null;
        RebuildBlockSelectorCombo();
        NotifyProgramChanged();
        RenderNetworks();
    }

    private void OnDeleteActiveBlockClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _activeBlock is null) return;
        PushUndoSnapshot();
        _plc.DraftProgram.AdditionalBlocks.Remove(_activeBlock);
        _activeBlock = null;
        _selectedNetworkIndex = 0;
        _selectedBranch = _plc.DraftProgram.Networks.FirstOrDefault()?.MainBranch;
        _selectedNode = null;
        RebuildBlockSelectorCombo();
        NotifyProgramChanged();
        RenderNetworks();
    }

    // =========================================================================
    // TAB 4: FORCE TABLE (BẢNG ÉP CỨNG NGOẠI VI :P)
    // =========================================================================

    private void RenderForceTable()
    {
        ForceRowsStack.Children.Clear();
        _forceLiveTextBlocks.Clear();
        if (_plc is null) return;

        ForceRowsStack.Children.Add(BuildForceHeaderRow());
        var entries = _plc.DraftProgram.ForceEntries;
        for (int i = 0; i < entries.Count; i++)
            ForceRowsStack.Children.Add(BuildForceDataRow(i + 1, entries[i]));

        RefreshForceLiveValuesInPlace();
    }

    private static Grid CreateForceColumnsGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // #
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // Enabled
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });  // Peripheral Addr (:P)
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });  // Force Value
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // Live Value
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 }); // Comment
        return g;
    }

    private Border BuildForceHeaderRow()
    {
        var border = new Border { Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateForceColumnsGrid();
        AddHeaderCell(g, 0, "#", center: true);
        AddHeaderCell(g, 1, "F", center: true);
        AddHeaderCell(g, 2, LocalizationManager.Get("Str.Plc.ForceColAddr"));
        AddHeaderCell(g, 3, LocalizationManager.Get("Str.Plc.ForceColVal"));
        AddHeaderCell(g, 4, LocalizationManager.Get("Str.Plc.ForceColLive"));
        AddHeaderCell(g, 5, LocalizationManager.Get("Str.Plc.ForceColComment"));
        border.Child = g;
        return border;
    }

    private Border BuildForceDataRow(int rowNum, PlcForceEntry entry)
    {
        bool isSelected = entry == _selectedForceEntry;
        var border = new Border { Padding = new Thickness(6, 3.5, 6, 3.5), Margin = new Thickness(0, 0, 0, 2.5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, isSelected ? "MenuHoverBrush" : "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, isSelected ? "AccentCyanBrush" : "BorderSubtleBrush");

        var g = CreateForceColumnsGrid();

        var numTb = new TextBlock { Text = rowNum.ToString(CultureInfo.InvariantCulture), FontSize = 10.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        numTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(numTb, 0);
        g.Children.Add(numTb);

        AddTagCheckbox(g, 1, entry.IsEnabled, v => { entry.IsEnabled = v; NotifyProgramChanged(); });

        var tbAddr = CreateEditableCell(entry.Address, v =>
        {
            string s = v.Trim();
            if (!string.IsNullOrEmpty(s) && !s.EndsWith(":P", StringComparison.OrdinalIgnoreCase))
                s += ":P";
            entry.Address = PlcProgramModel.NormalizeAddress(s);
            NotifyProgramChanged();
            RefreshForceLiveValuesInPlace();
        }, mono: true);
        Grid.SetColumn(tbAddr, 2);
        g.Children.Add(tbAddr);

        var tbVal = CreateEditableCell(entry.ForceValue, v =>
        {
            entry.ForceValue = v;
            NotifyProgramChanged();
        }, mono: true);
        Grid.SetColumn(tbVal, 3);
        g.Children.Add(tbVal);

        var liveTb = new TextBlock { Text = "—", FontFamily = new FontFamily("Consolas"), FontSize = 10.8, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        Grid.SetColumn(liveTb, 4);
        g.Children.Add(liveTb);
        _forceLiveTextBlocks[entry.Id] = liveTb;

        var tbCmt = CreateEditableCell(entry.Comment, v => { entry.Comment = v; NotifyProgramChanged(); });
        tbCmt.Margin = new Thickness(0);
        Grid.SetColumn(tbCmt, 5);
        g.Children.Add(tbCmt);

        border.PreviewMouseLeftButtonDown += (_, _) => { _selectedForceEntry = entry; };
        border.Child = g;
        return border;
    }

    private void RefreshForceLiveValuesInPlace()
    {
        if (_plc is null) return;
        bool canMonitor = _isSimulating && _plc.IsPowered;
        foreach (var entry in _plc.DraftProgram.ForceEntries)
        {
            if (!_forceLiveTextBlocks.TryGetValue(entry.Id, out var tb)) continue;
            if (!canMonitor)
            {
                tb.Text = "—";
                tb.Foreground = CreateBrush("#94A3B8");
                continue;
            }
            string readExpr = entry.Address.EndsWith(":P", StringComparison.OrdinalIgnoreCase)
                ? entry.Address[..^2]
                : entry.Address;
            var (valText, ok, _) = _plc.Runtime.ReadWatchFormatted(_plc.DraftProgram, readExpr, PlcWatchFormat.Auto);
            tb.Text = ok ? valText : "ERR";
            tb.Foreground = CreateBrush(!ok ? "#EF4444" : (valText.StartsWith("TRUE") ? "#22C55E" : "#38BDF8"));
        }
    }

    private void OnAddForceRowClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var entry = new PlcForceEntry { Address = "%I0.0:P", ForceValue = "TRUE", IsEnabled = true };
        _plc.DraftProgram.ForceEntries.Add(entry);
        _selectedForceEntry = entry;
        NotifyProgramChanged();
        RenderForceTable();
    }

    private void OnDeleteForceRowClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedForceEntry is null) return;
        _plc.DraftProgram.ForceEntries.Remove(_selectedForceEntry);
        _selectedForceEntry = null;
        NotifyProgramChanged();
        RenderForceTable();
    }

    private void OnStartForceClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        if (!_isSimulating || !_plc.IsPowered)
        {
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.WatchNeedSim"));
            return;
        }
        bool ok = _plc.Runtime.ApplyForceTable(_plc.DraftProgram, out var rejected);
        UpdateCpuStateBadges();
        RefreshForceLiveValuesInPlace();
        PlcHardwareVisualChanged?.Invoke();
        StatusMessageChanged?.Invoke(ok
            ? LocalizationManager.Get("Str.Plc.ForceAppliedOk")
            : "⚠ " + string.Join(" | ", rejected));
    }

    private void OnStopAllForceClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _plc.Runtime.StopAllForces();
        UpdateCpuStateBadges();
        RefreshForceLiveValuesInPlace();
        PlcHardwareVisualChanged?.Invoke();
        StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.ForceStoppedOk"));
    }

    // =========================================================================
    // TAB 5: DIAGNOSTICS BUFFER (NHẬT KÝ CHẨN ĐOÁN CPU)
    // =========================================================================

    private void RenderDiagnosticsBuffer(bool forceRebuild = false)
    {
        if (_plc is null) return;
        var events = _plc.Runtime.DiagnosticsBuffer;
        if (!forceRebuild && events.Count == _lastRenderedDiagCount)
            return;

        _lastRenderedDiagCount = events.Count;
        DiagRowsStack.Children.Clear();
        DiagRowsStack.Children.Add(BuildDiagHeaderRow());

        if (events.Count == 0)
        {
            var emptyTb = new TextBlock
            {
                Text = LocalizationManager.Get("Str.Plc.DiagEmpty"),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(12, 16, 12, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            DiagRowsStack.Children.Add(emptyTb);
            return;
        }

        // Hiển thị sự kiện mới nhất ở trên cùng (giống Diagnostics Buffer chuẩn công nghiệp)
        for (int i = events.Count - 1; i >= 0; i--)
            DiagRowsStack.Children.Add(BuildDiagRow(events.Count - i, events[i]));
    }

    private static Grid CreateDiagColumnsGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });   // #
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });   // Time
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });   // Event ID
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });   // Severity
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });   // Category
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 }); // Message
        return g;
    }

    private Border BuildDiagHeaderRow()
    {
        var border = new Border { Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateDiagColumnsGrid();
        AddHeaderCell(g, 0, "#", center: true);
        AddHeaderCell(g, 1, LocalizationManager.Get("Str.Plc.DiagColTime"));
        AddHeaderCell(g, 2, LocalizationManager.Get("Str.Plc.DiagColId"));
        AddHeaderCell(g, 3, LocalizationManager.Get("Str.Plc.DiagColSev"));
        AddHeaderCell(g, 4, LocalizationManager.Get("Str.Plc.DiagColCat"));
        AddHeaderCell(g, 5, LocalizationManager.Get("Str.Plc.DiagColMsg"));
        border.Child = g;
        return border;
    }

    private Border BuildDiagRow(int idx, PlcDiagnosticEvent ev)
    {
        var border = new Border { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        string sevHex = ev.Severity switch
        {
            PlcDiagSeverity.Error => "#EF4444",
            PlcDiagSeverity.Warning => "#F59E0B",
            _ => "#22C55E"
        };

        var g = CreateDiagColumnsGrid();

        var numTb = new TextBlock { Text = idx.ToString(CultureInfo.InvariantCulture), FontSize = 10.2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        numTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(numTb, 0);
        g.Children.Add(numTb);

        var timeTb = new TextBlock { Text = ev.TimeFormatted, FontFamily = new FontFamily("Consolas"), FontSize = 10.2, VerticalAlignment = VerticalAlignment.Center };
        timeTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(timeTb, 1);
        g.Children.Add(timeTb);

        var idTb = new TextBlock { Text = ev.EventIdHex, FontFamily = new FontFamily("Consolas"), FontSize = 10.4, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#38BDF8"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(idTb, 2);
        g.Children.Add(idTb);

        var sevBadge = new Border
        {
            Background = CreateBrush(sevHex),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = ev.Severity.ToString().ToUpperInvariant(), FontSize = 8.8, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
        };
        Grid.SetColumn(sevBadge, 3);
        g.Children.Add(sevBadge);

        var catTb = new TextBlock { Text = ev.Category, FontSize = 10.4, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        catTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Grid.SetColumn(catTb, 4);
        g.Children.Add(catTb);

        var msgTb = new TextBlock { Text = ev.Message, FontSize = 10.4, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        msgTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Grid.SetColumn(msgTb, 5);
        g.Children.Add(msgTb);

        border.Child = g;
        return border;
    }

    private void OnClearDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _plc.Runtime.ClearDiagnosticsBuffer();
        RenderDiagnosticsBuffer(forceRebuild: true);
    }

    // =========================================================================
    // XUẤT / NHẬP CHƯƠNG TRÌNH DẠNG VĂN BẢN CÓ CẤU TRÚC (.plctxt)
    // =========================================================================

    private void OnExportTextClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Str.Plc.ExportTextBtn"),
            Filter = "PLC Structured Text (*.plctxt)|*.plctxt|Text Files (*.txt)|*.txt",
            FileName = $"{_plc.DisplayName}_Program.plctxt"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        string content = PlcStructuredTextSerializer.ExportToText(_plc.DraftProgram);
        File.WriteAllText(dlg.FileName, content);
        StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.ExportTextOk", Path.GetFileName(dlg.FileName)));
    }

    private void OnImportTextClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Str.Plc.ImportTextBtn"),
            Filter = "PLC Structured Text (*.plctxt;*.txt)|*.plctxt;*.txt|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        string content = File.ReadAllText(dlg.FileName);
        if (!PlcStructuredTextSerializer.TryImportFromText(content, out var imported, out string err))
        {
            StatusMessageChanged?.Invoke("⚠ " + LocalizationManager.Format("Str.Plc.ImportTextFail", err));
            return;
        }

        PushUndoSnapshot();
        _plc.DraftProgram = imported;
        _activeBlock = null;
        _selectedNetworkIndex = 0;
        _selectedBranch = imported.Networks.FirstOrDefault()?.MainBranch;
        _selectedNode = null;
        RebuildBlockSelectorCombo();
        NotifyProgramChanged();
        RefreshAllViews();
        StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.ImportTextOk", imported.Networks.Count, imported.AdditionalBlocks.Count));
    }

    private void OnStopOutputModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMemConfigUi || _plc is null) return;
        ApplyMemConfigFromPopup();
    }
}
