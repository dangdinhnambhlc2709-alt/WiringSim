using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

/// <summary>
/// Phần 2 của PlcLadderEditorControl:
/// - Thanh thuộc tính nhanh (Property Bar) có chọn DataType, Edit Equation &amp; + Input Pin.
/// - Bảng PLC Tags (12 kiểu dữ liệu + Address Picker + System/Clock Memory preview).
/// - Tab Watch Table (Theo dõi live theo nhiều định dạng DEC/HEX/BIN/FLOAT/TIME/BOOL &amp; Modify Now).
/// - Cấu hình System &amp; Clock Memory, Compile, Download, Zoom &amp; Branch actions.
/// </summary>
public partial class PlcLadderEditorControl
{
    // =========================================================================
    // 9. PROPERTY BAR (CHỈNH SỬA NHANH THAM SỐ + DATATYPE + EQUATION + INSERT PIN)
    // =========================================================================

    private void UpdatePropertyBarForSelectedNode()
    {
        PanelPropDataType.Visibility = Visibility.Collapsed;
        PanelPropAddress.Visibility = Visibility.Collapsed;
        PanelPropSymbol.Visibility = Visibility.Collapsed;
        PanelPropParam1.Visibility = Visibility.Collapsed;
        PanelPropParam2.Visibility = Visibility.Collapsed;
        PanelPropExtra.Visibility = Visibility.Collapsed;
        PanelPropOut.Visibility = Visibility.Collapsed;
        BtnEditEquation.Visibility = Visibility.Collapsed;
        BtnInsertPinInput.Visibility = Visibility.Collapsed;
        BtnInsertPinOutput.Visibility = Visibility.Collapsed;
        BtnApplyNodeProps.Visibility = Visibility.Collapsed;

        if (_plc is null || _selectedNode is null || _selectedNode.Kind == LadderElementKind.ParallelBlock)
        {
            TxtSelectedNodeSummary.Text = LocalizationManager.Get("Str.Plc.DoubleClickHint");
            return;
        }

        var n = _selectedNode;
        BtnApplyNodeProps.Visibility = Visibility.Visible;

        if (n.Kind == LadderElementKind.EmptyBox)
        {
            TxtSelectedNodeSummary.Text = "[Empty box]";
            PanelPropAddress.Visibility = Visibility.Visible;
            BtnPickPropAddress.Visibility = Visibility.Collapsed;
            LblPropAddress.Text = "Instruction:";
            TbPropAddress.Text = n.Address == "???" ? string.Empty : n.Address;
            return;
        }

        BtnPickPropAddress.Visibility = Visibility.Visible;
        var meta = PlcInstructionCatalog.Get(n.Kind);
        TxtSelectedNodeSummary.Text = $"[{meta?.Symbol ?? n.Kind.ToString()}]";

        if (PlcInstructionCatalog.SupportsDataTypeSelector(n.Kind))
        {
            _updatingPropDataType = true;
            PanelPropDataType.Visibility = Visibility.Visible;
            CbPropDataType.Items.Clear();
            var supported = PlcInstructionCatalog.GetSupportedDataTypesForInstruction(n.Kind);
            foreach (string dt in supported) CbPropDataType.Items.Add(dt);
            string curDt = string.IsNullOrWhiteSpace(n.DataType) ? supported[0] : n.DataType;
            CbPropDataType.SelectedItem = supported.FirstOrDefault(x => x.Equals(curDt, StringComparison.OrdinalIgnoreCase)) ?? supported[0];
            _updatingPropDataType = false;
        }

        if (meta?.AddrLabel is not null)
        {
            PanelPropAddress.Visibility = Visibility.Visible;
            LblPropAddress.Text = $"{meta.AddrLabel}:";
            TbPropAddress.Text = n.Address;

            if (n.Kind is not (LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.Label or LadderElementKind.JumpList or LadderElementKind.Switch or LadderElementKind.CallBlock))
            {
                PanelPropSymbol.Visibility = Visibility.Visible;
                TbPropSymbol.Text = _plc.DraftProgram.ResolveTagName(n.Address, n.SymbolName);
            }
        }
        if (meta?.Param1Label is not null)
        {
            PanelPropParam1.Visibility = Visibility.Visible;
            LblPropParam1.Text = $"{meta.Param1Label}:";
            TbPropParam1.Text = n.Param1;
        }
        if (meta?.Param2Label is not null)
        {
            PanelPropParam2.Visibility = Visibility.Visible;
            LblPropParam2.Text = $"{meta.Param2Label}:";
            TbPropParam2.Text = n.Param2;
        }
        if (meta?.ExtraLabel is not null)
        {
            PanelPropExtra.Visibility = Visibility.Visible;
            LblPropExtra.Text = $"{meta.ExtraLabel}:";
            TbPropExtra.Text = n.ExtraParam;
        }
        if (meta?.OutLabel is not null)
        {
            PanelPropOut.Visibility = Visibility.Visible;
            LblPropOut.Text = $"{meta.OutLabel}:";
            TbPropOut.Text = n.OutputAddress;
        }

        if (n.Kind == LadderElementKind.Calculate)
            BtnEditEquation.Visibility = Visibility.Visible;

        if (PlcInstructionCatalog.SupportsMultiInput(n.Kind) && n.Kind != LadderElementKind.Calculate)
            BtnInsertPinInput.Visibility = Visibility.Visible;

        if (PlcInstructionCatalog.SupportsMultiOutput(n.Kind))
            BtnInsertPinOutput.Visibility = Visibility.Visible;
    }

    private void OnPropDataTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPropDataType || _plc is null || _selectedNode is null) return;
        if (CbPropDataType.SelectedItem is string dt && !dt.Equals(_selectedNode.DataType, StringComparison.Ordinal))
        {
            PushUndoSnapshot();
            _selectedNode.DataType = dt;
            NotifyProgramChanged();
            RenderNetworks();
        }
    }

    private void OnEditEquationClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is { Kind: LadderElementKind.Calculate })
            OpenCalculateEquationDialog(_selectedNode);
    }

    private void OnInsertBoxInputPinClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not null && PlcInstructionCatalog.SupportsMultiInput(_selectedNode.Kind))
            InsertExtraInputOnNode(_selectedNode);
    }

    private void OnInsertBoxOutputPinClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not null && PlcInstructionCatalog.SupportsMultiOutput(_selectedNode.Kind))
            InsertExtraOutputOnNode(_selectedNode);
    }

    private void OnPropTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyNodeProperties();
            e.Handled = true;
        }
    }

    private void OnApplyNodePropsClick(object sender, RoutedEventArgs e) => ApplyNodeProperties();

    private void ApplyNodeProperties()
    {
        if (_plc is null || _selectedNode is null) return;

        if (_selectedNode.Kind == LadderElementKind.EmptyBox)
        {
            string typed = TbPropAddress.Text.Trim();
            if (PlcInstructionCatalog.TryResolveByName(typed, out var resolvedKind))
            {
                _emptyBoxTargetNode = _selectedNode;
                TransformEmptyBoxTo(resolvedKind);
            }
            else
            {
                _selectedNode.Address = string.IsNullOrEmpty(typed) ? "???" : typed;
                RenderNetworks();
            }
            return;
        }

        PushUndoSnapshot();

        if (PanelPropAddress.Visibility == Visibility.Visible)
        {
            string rawAddr = TbPropAddress.Text.Trim();
            _selectedNode.Address = _selectedNode.Kind is LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.Label or LadderElementKind.JumpList or LadderElementKind.Switch or LadderElementKind.CallBlock
                ? rawAddr
                : PlcProgramModel.NormalizeAddress(rawAddr);
        }

        if (PanelPropSymbol.Visibility == Visibility.Visible)
        {
            string sym = PlcAddressHelper.StripQuotes(TbPropSymbol.Text.Trim());
            _selectedNode.SymbolName = sym;
            if (!string.IsNullOrWhiteSpace(sym) && !string.IsNullOrWhiteSpace(_selectedNode.Address))
            {
                var resolved = OperandResolver.Resolve(_selectedNode.Address, _plc.DraftProgram);
                if (resolved.IsValid && !resolved.IsLiteral && !resolved.IsImmediate && !resolved.IsSystemOrClockBit &&
                    resolved.SliceKind == PlcSliceKind.None && resolved.Area != PlcMemoryArea.DbMember)
                {
                    string norm = resolved.NormalizedText;
                    string inferredType = _selectedNode.Kind switch
                    {
                        LadderElementKind.CtrlPwm => "IEC_PWM",
                        LadderElementKind.CtrlHsc => "IEC_HSC",
                        LadderElementKind.PidCompact => "PID_Compact",
                        LadderElementKind.ModbusCommLoad => "Modbus_Comm_Load",
                        LadderElementKind.ModbusMaster => "Modbus_Master",
                        LadderElementKind.ModbusTcpClient => "MB_CLIENT",
                        LadderElementKind.RTrig or LadderElementKind.FTrig => "IEC_Trig",
                        _ when _selectedNode.IsTimerInstance => "IEC_Timer",
                        _ when _selectedNode.IsCounter => "IEC_Counter",
                        _ => resolved.InferredType.ToString()
                    };
                    _plc.DraftProgram.EnsureTagExists(norm, sym, inferredType, overwriteDataType: false);
                }
            }
        }
        if (PanelPropParam1.Visibility == Visibility.Visible) _selectedNode.Param1 = TbPropParam1.Text.Trim();
        if (PanelPropParam2.Visibility == Visibility.Visible) _selectedNode.Param2 = TbPropParam2.Text.Trim();
        if (PanelPropExtra.Visibility == Visibility.Visible)
        {
            _selectedNode.ExtraParam = TbPropExtra.Text.Trim();
            if (_selectedNode.Kind == LadderElementKind.Calculate)
                PlcProgramModel.SyncCalculateExtraInputs(_selectedNode);
        }
        if (PanelPropOut.Visibility == Visibility.Visible) _selectedNode.OutputAddress = TbPropOut.Text.Trim();

        NotifyProgramChanged();
        RenderNetworks();
    }

    // =========================================================================
    // 10. BẢNG KÝ HIỆU BIẾN PLC TAGS (12 KIỂU DỮ LIỆU + SYSTEM/CLOCK PREVIEW)
    // =========================================================================

    private void RenderTagTable()
    {
        TagRowsStack.Children.Clear();
        if (_plc is null) return;

        TagRowsStack.Children.Add(BuildTagHeaderRow());

        var tags = _plc.DraftProgram.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            string liveVal = _isSimulating && _plc.IsPowered
                ? _plc.Runtime.FormatLiveValueForAddress(_plc.DraftProgram, tag.Address)
                : "—";
            TagRowsStack.Children.Add(BuildTagDataRow(i + 1, tag, liveVal));
        }

        TagRowsStack.Children.Add(BuildAddNewTagRow(tags.Count + 1));

        // Hiển thị phần System & Clock Memory (read-only) nếu đang bật
        var sysTags = _plc.DraftProgram.GetSystemAndClockTags();
        if (sysTags.Count > 0)
        {
            var secHdr = new TextBlock
            {
                Text = LocalizationManager.Get("Str.Plc.SysClkTagSection"),
                FontSize = 10.6,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush("#D97706"),
                Margin = new Thickness(4, 12, 0, 4)
            };
            TagRowsStack.Children.Add(secHdr);

            for (int i = 0; i < sysTags.Count; i++)
            {
                var st = sysTags[i];
                string liveVal = _isSimulating && _plc.IsPowered
                    ? _plc.Runtime.FormatLiveValueForAddress(_plc.DraftProgram, st.Address)
                    : "—";
                TagRowsStack.Children.Add(BuildReadOnlySystemTagRow(st, liveVal));
            }
        }
    }

    private Border BuildReadOnlySystemTagRow(PlcTagEntry tag, string liveVal)
    {
        bool isClock = tag.Name.StartsWith("Clock_", StringComparison.OrdinalIgnoreCase);
        var border = new Border
        {
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Opacity = 0.92
        };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateTagColumnsGrid();
        var badge = new Border
        {
            Width = 22,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = CreateBrush(isClock ? "#D97706" : "#0891B2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = isClock ? "CLK" : "SYS", FontFamily = new FontFamily("Consolas"), FontSize = 7.6, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(badge, 1);
        g.Children.Add(badge);

        var nameTb = new TextBlock { Text = $"\"{tag.Name}\"", FontFamily = new FontFamily("Consolas"), FontSize = 10.8, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        nameTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Grid.SetColumn(nameTb, 2);
        g.Children.Add(nameTb);

        var typeTb = new TextBlock { Text = tag.DataType, FontSize = 10.4, VerticalAlignment = VerticalAlignment.Center };
        typeTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(typeTb, 3);
        g.Children.Add(typeTb);

        var addrTb = new TextBlock { Text = tag.Address, FontFamily = new FontFamily("Consolas"), FontSize = 10.8, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#2DD4BF"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(addrTb, 4);
        g.Children.Add(addrTb);

        var liveTb = new TextBlock { Text = liveVal, FontFamily = new FontFamily("Consolas"), FontSize = 10.4, FontWeight = FontWeights.Bold, Foreground = CreateBrush(liveVal.StartsWith("TRUE") ? "#22C55E" : "#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        Grid.SetColumn(liveTb, 9);
        g.Children.Add(liveTb);

        var cmtTb = new TextBlock { Text = tag.Comment, FontSize = 10.2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        cmtTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(cmtTb, 10);
        g.Children.Add(cmtTb);

        border.Child = g;
        return border;
    }

    private static Grid CreateTagColumnsGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // #
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });   // Icon
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });  // Name
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });   // Data type
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });  // Address + picker
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });   // Retain
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });   // Accessible
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });   // Writable
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });   // Visible
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });   // Live
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 110 }); // Comment
        return g;
    }

    private Border BuildTagHeaderRow()
    {
        var border = new Border
        {
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateTagColumnsGrid();
        AddHeaderCell(g, 0, "#", center: true);
        AddHeaderCell(g, 1, "", center: true);
        AddHeaderCell(g, 2, LocalizationManager.Get("Str.Plc.TagColName"));
        AddHeaderCell(g, 3, LocalizationManager.Get("Str.Plc.TagColType"));
        AddHeaderCell(g, 4, LocalizationManager.Get("Str.Plc.TagColAddr"));
        AddHeaderCell(g, 5, LocalizationManager.Get("Str.Plc.TagColRetain"), center: true);
        AddHeaderCell(g, 6, LocalizationManager.Get("Str.Plc.TagColAccessible"), center: true);
        AddHeaderCell(g, 7, LocalizationManager.Get("Str.Plc.TagColWritable"), center: true);
        AddHeaderCell(g, 8, LocalizationManager.Get("Str.Plc.TagColVisible"), center: true);
        AddHeaderCell(g, 9, LocalizationManager.Get("Str.Plc.TagLive"));
        AddHeaderCell(g, 10, LocalizationManager.Get("Str.Plc.TagColComment"));

        border.Child = g;
        return border;
    }

    private Border BuildTagDataRow(int rowNum, PlcTagEntry tag, string liveVal)
    {
        bool isSelected = tag == _selectedTag;
        bool isDb = PlcProgramModel.NormalizeAddress(tag.Address).StartsWith("%DB", StringComparison.OrdinalIgnoreCase);

        var border = new Border
        {
            Padding = new Thickness(6, 3.5, 6, 3.5),
            Margin = new Thickness(0, 0, 0, 2.5),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, isSelected ? "MenuHoverBrush" : "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, isSelected ? "AccentCyanBrush" : "BorderSubtleBrush");

        var g = CreateTagColumnsGrid();

        var numTb = new TextBlock { Text = rowNum.ToString(CultureInfo.InvariantCulture), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        numTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(numTb, 0);
        g.Children.Add(numTb);

        var iconBadge = new Border
        {
            Width = 18,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = CreateBrush(isDb ? "#0F766E" : "#2563EB"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = isDb ? "DB" : "T", FontFamily = new FontFamily("Consolas"), FontSize = 8.4, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(iconBadge, 1);
        g.Children.Add(iconBadge);

        var tbName = CreateEditableCell(tag.Name, v =>
        {
            if (v != tag.Name)
            {
                PushUndoSnapshot();
                string oldName = tag.Name;
                tag.Name = v;
                if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(v))
                    _plc?.DraftProgram.RenameTagEverywhere(oldName, v);
                NotifyProgramChanged();
            }
        }, onEnter: () => HandleTagRowEnter(tag));
        Grid.SetColumn(tbName, 2);
        g.Children.Add(tbName);

        if (tag == _pendingFocusTag)
        {
            _pendingFocusTag = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { tbName.Focus(); tbName.SelectAll(); });
        }

        var cbType = new ComboBox { Margin = new Thickness(0, 0, 5, 0), FontSize = 10.8, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (string dt in PlcAddressHelper.SupportedDataTypes) cbType.Items.Add(dt);
        if (!PlcAddressHelper.SupportedDataTypes.Contains(tag.DataType, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tag.DataType))
            cbType.Items.Add(tag.DataType);
        cbType.SelectedItem = cbType.Items.Cast<string>().FirstOrDefault(x => x.Equals(tag.DataType, StringComparison.OrdinalIgnoreCase)) ?? "Bool";
        cbType.IsEnabled = !isDb;
        cbType.SelectionChanged += (_, _) =>
        {
            if (cbType.SelectedItem is string chosen && !chosen.Equals(tag.DataType, StringComparison.Ordinal))
            {
                PushUndoSnapshot();
                tag.DataType = chosen;
                NotifyProgramChanged();
            }
        };
        Grid.SetColumn(cbType, 3);
        g.Children.Add(cbType);

        var addrDock = new DockPanel { Margin = new Thickness(0, 0, 5, 0) };
        var btnPicker = new Button { Content = "▾", Width = 20, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), FontSize = 10, IsEnabled = !isDb, ToolTip = LocalizationManager.Get("Str.Plc.AddrPickerTitle") };
        btnPicker.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnPicker.Click += (_, e) => { e.Handled = true; OpenAddressPicker(addrDock, tag); };
        DockPanel.SetDock(btnPicker, Dock.Right);
        addrDock.Children.Add(btnPicker);

        TextBox tbAddr = null!;
        tbAddr = CreateEditableCell(tag.Address, v =>
        {
            string norm = PlcProgramModel.NormalizeAddress(v);
            if (norm != tag.Address)
            {
                PushUndoSnapshot();
                tag.Address = norm;
                NotifyProgramChanged();
            }
            if (tbAddr.Text != norm) tbAddr.Text = norm;
        }, mono: true, onEnter: () =>
        {
            string norm = PlcProgramModel.NormalizeAddress(tbAddr.Text.Trim());
            if (norm != tag.Address)
            {
                PushUndoSnapshot();
                tag.Address = norm;
                NotifyProgramChanged();
            }
            tbAddr.Text = norm;
            HandleTagRowEnter(tag);
        });
        tbAddr.Margin = new Thickness(0);
        addrDock.Children.Add(tbAddr);
        Grid.SetColumn(addrDock, 4);
        g.Children.Add(addrDock);

        AddTagCheckbox(g, 5, tag.Retain, v => { PushUndoSnapshot(); tag.Retain = v; NotifyProgramChanged(); });
        AddTagCheckbox(g, 6, tag.Accessible, v => { PushUndoSnapshot(); tag.Accessible = v; NotifyProgramChanged(); });
        AddTagCheckbox(g, 7, tag.Writable, v => { PushUndoSnapshot(); tag.Writable = v; NotifyProgramChanged(); });
        AddTagCheckbox(g, 8, tag.Visible, v => { PushUndoSnapshot(); tag.Visible = v; NotifyProgramChanged(); });

        var tbLive = new TextBlock
        {
            Text = liveVal,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.6,
            FontWeight = FontWeights.SemiBold,
            Foreground = CreateBrush(liveVal.StartsWith("TRUE") ? "#22C55E" : "#94A3B8"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(tbLive, 9);
        g.Children.Add(tbLive);

        var tbComment = CreateEditableCell(tag.Comment, v => { if (v != tag.Comment) { PushUndoSnapshot(); tag.Comment = v; NotifyProgramChanged(); } }, onEnter: () => HandleTagRowEnter(tag));
        tbComment.Margin = new Thickness(0);
        Grid.SetColumn(tbComment, 10);
        g.Children.Add(tbComment);

        border.PreviewMouseLeftButtonDown += (_, _) => { _selectedTag = tag; };
        border.Child = g;
        return border;
    }

    private Border BuildAddNewTagRow(int nextRowNum)
    {
        var border = new Border { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, 0, 4), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateTagColumnsGrid();

        var numTb = new TextBlock { Text = nextRowNum.ToString(CultureInfo.InvariantCulture), FontSize = 10.2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        numTb.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Grid.SetColumn(numTb, 0);
        g.Children.Add(numTb);

        var plusTb = new TextBlock { Text = "+", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#22C55E"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(plusTb, 1);
        g.Children.Add(plusTb);

        var inputGrid = new Grid { Margin = new Thickness(0, 0, 5, 0) };
        var tbNewName = new TextBox { Padding = new Thickness(4, 2, 4, 2), FontSize = 11 };
        tbNewName.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        tbNewName.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        tbNewName.SetResourceReference(TextBox.BorderBrushProperty, "BorderSubtleBrush");

        var phTb = new TextBlock { Text = LocalizationManager.Get("Str.Plc.TagAddNew"), FontSize = 10.8, FontStyle = FontStyles.Italic, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        phTb.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        tbNewName.TextChanged += (_, _) => phTb.Visibility = string.IsNullOrEmpty(tbNewName.Text) ? Visibility.Visible : Visibility.Collapsed;
        tbNewName.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddTagFromAddNewRow(tbNewName.Text.Trim());
            }
        };

        inputGrid.Children.Add(tbNewName);
        inputGrid.Children.Add(phTb);
        Grid.SetColumn(inputGrid, 2);
        g.Children.Add(inputGrid);

        string previewAddr = ComputeNextDefaultTagAddress(out string nextType);
        var nextTypeTb = new TextBlock { Text = nextType, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        nextTypeTb.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Grid.SetColumn(nextTypeTb, 3);
        g.Children.Add(nextTypeTb);

        var nextAddrTb = new TextBlock { Text = previewAddr, FontFamily = new FontFamily("Consolas"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        nextAddrTb.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Grid.SetColumn(nextAddrTb, 4);
        g.Children.Add(nextAddrTb);

        border.Child = g;
        return border;
    }

    private string ComputeNextDefaultTagAddress(out string dataType)
    {
        dataType = "Bool";
        if (_plc is null) return "%I0.0";
        var lastIoTag = _plc.DraftProgram.Tags.LastOrDefault(t =>
            !PlcProgramModel.NormalizeAddress(t.Address).StartsWith("%DB", StringComparison.OrdinalIgnoreCase));
        if (lastIoTag is null) return "%I0.0";

        dataType = PlcAddressHelper.SupportedDataTypes.Contains(lastIoTag.DataType, StringComparer.OrdinalIgnoreCase)
            ? lastIoTag.DataType
            : "Bool";
        return PlcAddressHelper.ComputeNextAddress(lastIoTag.Address, dataType);
    }

    private void HandleTagRowEnter(PlcTagEntry currentTag)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        var tags = _plc.DraftProgram.Tags;
        int idx = tags.IndexOf(currentTag);
        if (idx < 0) return;

        string dt = PlcAddressHelper.SupportedDataTypes.Contains(currentTag.DataType, StringComparer.OrdinalIgnoreCase)
            ? currentTag.DataType
            : "Bool";
        string nextAddr = PlcAddressHelper.ComputeNextAddress(currentTag.Address, dt);

        var nextEntry = new PlcTagEntry { Name = $"Tag_{tags.Count + 1}", Address = nextAddr, DataType = dt };
        tags.Insert(idx + 1, nextEntry);
        _selectedTag = nextEntry;
        _pendingFocusTag = nextEntry;
        NotifyProgramChanged();
        RenderTagTable();
    }

    private void AddTagFromAddNewRow(string typedName)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        string nextAddr = ComputeNextDefaultTagAddress(out string nextType);
        var entry = new PlcTagEntry
        {
            Name = string.IsNullOrWhiteSpace(typedName) ? $"Tag_{_plc.DraftProgram.Tags.Count + 1}" : typedName,
            Address = nextAddr,
            DataType = nextType
        };
        _plc.DraftProgram.Tags.Add(entry);
        _selectedTag = entry;
        _pendingFocusTag = string.IsNullOrWhiteSpace(typedName) ? entry : null;
        NotifyProgramChanged();
        RenderTagTable();
    }

    // =========================================================================
    // 11. TAB WATCH TABLE (THEO DÕI & ÉP GIÁ TRỊ TRỰC TUYẾN)
    // =========================================================================

    private void RenderWatchTable()
    {
        WatchRowsStack.Children.Clear();
        _watchLiveTextBlocks.Clear();
        _watchTypeTextBlocks.Clear();
        _watchAddrTextBlocks.Clear();
        if (_plc is null) return;

        // Tự khởi tạo danh sách Watch mặc định từ các Tag quan trọng nếu còn trống
        var watches = _plc.DraftProgram.WatchEntries;
        if (watches.Count == 0 && _plc.DraftProgram.Tags.Count > 0)
        {
            foreach (var t in _plc.DraftProgram.Tags.Take(6))
            {
                string expr = t.Address.StartsWith("%DB", StringComparison.OrdinalIgnoreCase)
                    ? (t.DataType.Contains("Counter", StringComparison.OrdinalIgnoreCase) ? $"\"{t.Name}\".CV" : $"\"{t.Name}\".ET")
                    : $"\"{t.Name}\"";
                watches.Add(new PlcWatchEntry { Expression = expr });
            }
        }

        WatchRowsStack.Children.Add(BuildWatchHeaderRow());
        for (int i = 0; i < watches.Count; i++)
            WatchRowsStack.Children.Add(BuildWatchDataRow(i + 1, watches[i]));

        RefreshWatchLiveValuesInPlace();
    }

    private static Grid CreateWatchColumnsGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // #
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });   // Select checkbox
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });  // Name / Expression + picker
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });   // Resolved Address
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });   // Data type
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });   // Format (DEC/HEX/BIN/FLOAT/TIME/BOOL)
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // Monitor value (Live)
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });  // Modify value
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });   // Modify Now button
        return g;
    }

    private Border BuildWatchHeaderRow()
    {
        var border = new Border { Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        var g = CreateWatchColumnsGrid();
        AddHeaderCell(g, 0, "#", center: true);
        AddHeaderCell(g, 1, "✓", center: true);
        AddHeaderCell(g, 2, LocalizationManager.Get("Str.Plc.WatchColExpr"));
        AddHeaderCell(g, 3, LocalizationManager.Get("Str.Plc.TagColAddr"));
        AddHeaderCell(g, 4, LocalizationManager.Get("Str.Plc.TagColType"));
        AddHeaderCell(g, 5, LocalizationManager.Get("Str.Plc.WatchColFmt"));
        AddHeaderCell(g, 6, LocalizationManager.Get("Str.Plc.WatchColMonitor"));
        AddHeaderCell(g, 7, LocalizationManager.Get("Str.Plc.WatchColModify"));
        AddHeaderCell(g, 8, LocalizationManager.Get("Str.Plc.WatchColAction"), center: true);
        border.Child = g;
        return border;
    }

    private Border BuildWatchDataRow(int rowNum, PlcWatchEntry entry)
    {
        bool isSelected = entry == _selectedWatch;
        var border = new Border { Padding = new Thickness(6, 3.5, 6, 3.5), Margin = new Thickness(0, 0, 0, 2.5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, isSelected ? "MenuHoverBrush" : "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, isSelected ? "AccentCyanBrush" : "BorderSubtleBrush");

        var g = CreateWatchColumnsGrid();

        var numTb = new TextBlock { Text = rowNum.ToString(CultureInfo.InvariantCulture), FontSize = 10.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        numTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(numTb, 0);
        g.Children.Add(numTb);

        AddTagCheckbox(g, 1, entry.IsSelectedForModify, v => { entry.IsSelectedForModify = v; });

        // Col 2: Expression + Autocomplete button
        var exprDock = new DockPanel { Margin = new Thickness(0, 0, 5, 0) };
        var btnPick = new Button { Content = "▾", Width = 20, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), FontSize = 10 };
        btnPick.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnPick.Click += (_, e) => { e.Handled = true; OpenTagAutocompletePopup(exprDock, null, "Watch", entry); };
        DockPanel.SetDock(btnPick, Dock.Right);
        exprDock.Children.Add(btnPick);

        var tbExpr = CreateEditableCell(entry.Expression, v =>
        {
            entry.Expression = v;
            NotifyProgramChanged();
            RefreshWatchLiveValuesInPlace();
        }, mono: true);
        tbExpr.Margin = new Thickness(0);
        exprDock.Children.Add(tbExpr);
        Grid.SetColumn(exprDock, 2);
        g.Children.Add(exprDock);

        // Col 3: Resolved Address
        var addrTb = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#2DD4BF"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(addrTb, 3);
        g.Children.Add(addrTb);
        _watchAddrTextBlocks[entry.Id] = addrTb;

        // Col 4: Type
        var typeTb = new TextBlock { FontSize = 10.2, VerticalAlignment = VerticalAlignment.Center };
        typeTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(typeTb, 4);
        g.Children.Add(typeTb);
        _watchTypeTextBlocks[entry.Id] = typeTb;

        // Col 5: Format ComboBox
        var cbFmt = new ComboBox { Margin = new Thickness(0, 0, 5, 0), FontSize = 10.4, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (PlcWatchFormat f in Enum.GetValues<PlcWatchFormat>())
            cbFmt.Items.Add(f);
        cbFmt.SelectedItem = entry.DisplayFormat;
        cbFmt.SelectionChanged += (_, _) =>
        {
            if (cbFmt.SelectedItem is PlcWatchFormat chosen)
            {
                entry.DisplayFormat = chosen;
                NotifyProgramChanged();
                RefreshWatchLiveValuesInPlace();
            }
        };
        Grid.SetColumn(cbFmt, 5);
        g.Children.Add(cbFmt);

        // Col 6: Monitor Live Value
        var liveTb = new TextBlock { Text = "—", FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = CreateBrush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(liveTb, 6);
        g.Children.Add(liveTb);
        _watchLiveTextBlocks[entry.Id] = liveTb;

        // Col 7: Modify Value TextBox
        var tbMod = CreateEditableCell(entry.ModifyValue, v => { entry.ModifyValue = v; }, mono: true, onEnter: () => ApplyWatchModify(entry));
        Grid.SetColumn(tbMod, 7);
        g.Children.Add(tbMod);

        // Col 8: Modify Now button
        var btnMod = new Button
        {
            Content = LocalizationManager.Get("Str.Plc.WatchModifyNow"),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 10.2,
            BorderBrush = CreateBrush("#10B981")
        };
        btnMod.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnMod.Click += (_, _) => ApplyWatchModify(entry);
        Grid.SetColumn(btnMod, 8);
        g.Children.Add(btnMod);

        border.PreviewMouseLeftButtonDown += (_, _) => { _selectedWatch = entry; };
        border.Child = g;
        return border;
    }

    private void RefreshWatchLiveValuesInPlace()
    {
        if (_plc is null) return;
        var prog = _plc.DraftProgram;
        bool canMonitor = _isSimulating && _plc.IsPowered;

        foreach (var entry in prog.WatchEntries)
        {
            var op = OperandResolver.Resolve(entry.Expression, prog);
            if (_watchAddrTextBlocks.TryGetValue(entry.Id, out var addrTb))
                addrTb.Text = op.IsValid ? op.NormalizedText : "<???>";

            if (canMonitor)
            {
                var (valText, ok, dtName) = _plc.Runtime.ReadWatchFormatted(prog, entry.Expression, entry.DisplayFormat);
                if (_watchTypeTextBlocks.TryGetValue(entry.Id, out var typeTb)) typeTb.Text = dtName;
                if (_watchLiveTextBlocks.TryGetValue(entry.Id, out var liveTb))
                {
                    liveTb.Text = valText;
                    liveTb.Foreground = CreateBrush(!ok ? "#EF4444" : (valText.StartsWith("TRUE") ? "#22C55E" : "#38BDF8"));
                }
            }
            else
            {
                if (_watchTypeTextBlocks.TryGetValue(entry.Id, out var typeTb)) typeTb.Text = op.IsValid ? op.InferredType.ToString() : "—";
                if (_watchLiveTextBlocks.TryGetValue(entry.Id, out var liveTb))
                {
                    liveTb.Text = "—";
                    liveTb.Foreground = CreateBrush("#94A3B8");
                }
            }
        }
    }

    private void ApplyWatchModify(PlcWatchEntry entry)
    {
        if (_plc is null) return;
        if (!_isSimulating || !_plc.IsPowered)
        {
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.WatchNeedSim"));
            return;
        }
        if (string.IsNullOrWhiteSpace(entry.ModifyValue)) return;

        if (_plc.Runtime.TryModifyFromWatch(_plc.DraftProgram, entry.Expression, entry.ModifyValue, out string err))
        {
            RefreshWatchLiveValuesInPlace();
            PlcHardwareVisualChanged?.Invoke();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.WatchModifiedOk", entry.Expression, entry.ModifyValue));
        }
        else
        {
            StatusMessageChanged?.Invoke("⚠ " + err);
        }
    }

    private void OnAddWatchRowClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var entry = new PlcWatchEntry { Expression = "%MW20", DisplayFormat = PlcWatchFormat.Dec };
        _plc.DraftProgram.WatchEntries.Add(entry);
        _selectedWatch = entry;
        NotifyProgramChanged();
        RenderWatchTable();
    }

    private void OnDeleteWatchRowClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedWatch is null) return;
        _plc.DraftProgram.WatchEntries.Remove(_selectedWatch);
        _selectedWatch = null;
        NotifyProgramChanged();
        RenderWatchTable();
    }

    private void OnModifyAllWatchClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        if (!_isSimulating || !_plc.IsPowered)
        {
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.WatchNeedSim"));
            return;
        }
        int modified = 0;
        foreach (var entry in _plc.DraftProgram.WatchEntries.Where(w => w.IsSelectedForModify && !string.IsNullOrWhiteSpace(w.ModifyValue)))
        {
            if (_plc.Runtime.TryModifyFromWatch(_plc.DraftProgram, entry.Expression, entry.ModifyValue, out _))
                modified++;
        }
        RefreshWatchLiveValuesInPlace();
        PlcHardwareVisualChanged?.Invoke();
        StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.WatchModifiedCount", modified));
    }

    private void OnClearWatchOverridesClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _plc.Runtime.ClearWatchInputOverrides();
        RefreshWatchLiveValuesInPlace();
        StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.WatchClearedForce"));
    }

    // =========================================================================
    // 12. CẤU HÌNH SYSTEM & CLOCK MEMORY POPUP
    // =========================================================================

    private void OnOpenMemConfigClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _updatingMemConfigUi = true;
        var prog = _plc.DraftProgram;
        CbEnableSysMem.IsChecked = prog.EnableSystemMemory;
        TbSysMemByte.Text = prog.SystemMemoryByte.ToString(CultureInfo.InvariantCulture);
        CbEnableClkMem.IsChecked = prog.EnableClockMemory;
        TbClkMemByte.Text = prog.ClockMemoryByte.ToString(CultureInfo.InvariantCulture);
        TbWatchdogMs.Text = prog.MaxCycleTimeMs.ToString(CultureInfo.InvariantCulture);
        CbStopOutputMode.SelectedIndex = prog.StopOutputMode == PlcStopOutputMode.FreezeLastValue ? 1 : 0;
        UpdateMemConfigPreviews();
        _updatingMemConfigUi = false;

        PlcMemConfigPopup.PlacementTarget = BtnPlcMemConfig;
        PlcMemConfigPopup.IsOpen = true;
    }

    private void OnMemConfigChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingMemConfigUi || _plc is null) return;
        ApplyMemConfigFromPopup();
    }

    private void OnMemConfigByteLostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingMemConfigUi || _plc is null) return;
        ApplyMemConfigFromPopup();
    }

    private void ApplyMemConfigFromPopup()
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        var prog = _plc.DraftProgram;
        prog.EnableSystemMemory = CbEnableSysMem.IsChecked == true;
        prog.EnableClockMemory = CbEnableClkMem.IsChecked == true;
        if (int.TryParse(TbSysMemByte.Text.Trim(), out int sb)) prog.SystemMemoryByte = Math.Clamp(sb, 0, OperandResolver.MaxMemoryBytes - 1);
        if (int.TryParse(TbClkMemByte.Text.Trim(), out int cb)) prog.ClockMemoryByte = Math.Clamp(cb, 0, OperandResolver.MaxMemoryBytes - 1);
        if (int.TryParse(TbWatchdogMs.Text.Trim(), out int wd)) prog.MaxCycleTimeMs = Math.Clamp(wd, 1, 6000);
        prog.StopOutputMode = CbStopOutputMode.SelectedIndex == 1
            ? PlcStopOutputMode.FreezeLastValue
            : PlcStopOutputMode.ResetToZero;

        // Đồng bộ trực tiếp sang LoadedProgram để phản hồi ngay lập tức khi đang chạy
        _plc.LoadedProgram.EnableSystemMemory = prog.EnableSystemMemory;
        _plc.LoadedProgram.SystemMemoryByte = prog.SystemMemoryByte;
        _plc.LoadedProgram.EnableClockMemory = prog.EnableClockMemory;
        _plc.LoadedProgram.ClockMemoryByte = prog.ClockMemoryByte;
        _plc.LoadedProgram.MaxCycleTimeMs = prog.MaxCycleTimeMs;
        _plc.LoadedProgram.StopOutputMode = prog.StopOutputMode;

        UpdateMemConfigPreviews();
        NotifyProgramChanged();
        RefreshAllViews();
        if (prog.EnableSystemMemory && prog.EnableClockMemory && prog.SystemMemoryByte == prog.ClockMemoryByte)
        {
            StatusMessageChanged?.Invoke("⚠ " + LocalizationManager.Format("Str.Plc.ErrSysClkOverlap", prog.SystemMemoryByte));
        }
    }

    private void UpdateMemConfigPreviews()
    {
        if (_plc is null) return;
        int sb = Math.Clamp(_plc.DraftProgram.SystemMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
        int cb = Math.Clamp(_plc.DraftProgram.ClockMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
        TxtSysMemPreview.Text = $"%M{sb}.0 FirstScan, %M{sb}.2 TRUE, %M{sb}.3 FALSE";
        TxtClkMemPreview.Text = $"%M{cb}.0 (10Hz) .. %M{cb}.7 (0.5Hz)";
    }

    // =========================================================================
    // 13. SHARED TABLE HELPERS & ADDRESS PICKER POPUP
    // =========================================================================

    private void AddHeaderCell(Grid g, int col, string text, bool center = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10.2,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private static void AddTagCheckbox(Grid g, int col, bool isChecked, Action<bool> onChanged)
    {
        var cb = new CheckBox { IsChecked = isChecked, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => onChanged(true);
        cb.Unchecked += (_, _) => onChanged(false);
        Grid.SetColumn(cb, col);
        g.Children.Add(cb);
    }

    private TextBox CreateEditableCell(string initial, Action<string> onChanged, bool mono = false, Action? onEnter = null)
    {
        var tb = new TextBox
        {
            Text = initial,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 11,
            FontFamily = mono ? new FontFamily("Consolas") : new FontFamily("Segoe UI")
        };
        tb.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        tb.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        tb.SetResourceReference(TextBox.BorderBrushProperty, "BorderSubtleBrush");
        tb.LostFocus += (_, _) => onChanged(tb.Text.Trim());
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                onChanged(tb.Text.Trim());
                onEnter?.Invoke();
                e.Handled = true;
            }
        };
        return tb;
    }

    private void OpenAddressPicker(UIElement anchor, PlcTagEntry tag)
    {
        _addrPickerTag = tag;
        _updatingAddrPickerUi = true;

        PlcAddressHelper.TryParseAddressParts(tag.Address, out string opId, out string opType, out int byteAddr, out int bitNum);

        CbAddrOpId.SelectedIndex = opId switch { "Q" => 1, "M" => 2, _ => 0 };
        CbAddrOpType.SelectedIndex = opType switch { "Byte" => 1, "Word" => 2, "DWord" => 3, _ => 0 };
        TbAddrByte.Text = byteAddr.ToString(CultureInfo.InvariantCulture);
        CbAddrBit.SelectedIndex = Math.Clamp(bitNum, 0, 7);

        _updatingAddrPickerUi = false;
        UpdateAddressPickerPreview();

        AddressPickerPopup.PlacementTarget = anchor;
        AddressPickerPopup.IsOpen = true;
    }

    private void OnAddressPickerFieldChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAddrPickerUi || !IsInitialized) return;
        UpdateAddressPickerPreview();
    }

    private void OnAddressPickerByteChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingAddrPickerUi || !IsInitialized) return;
        UpdateAddressPickerPreview();
    }

    private string BuildAddressFromPicker(out string opType)
    {
        string opId = (CbAddrOpId.SelectedItem as ComboBoxItem)?.Content as string ?? "I";
        opType = (CbAddrOpType.SelectedItem as ComboBoxItem)?.Content as string ?? "Bit";
        int byteAddr = int.TryParse(TbAddrByte.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ? b : 0;
        int bitNum = Math.Clamp(CbAddrBit.SelectedIndex, 0, 7);
        return PlcAddressHelper.BuildAddress(opId, opType, byteAddr, bitNum);
    }

    private void UpdateAddressPickerPreview()
    {
        string built = BuildAddressFromPicker(out string opType);
        CbAddrBit.IsEnabled = opType == "Bit";
        TxtAddrPickerPreview.Text = built;
    }

    private void OnAddressPickerConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_addrPickerTag is not null)
        {
            PushUndoSnapshot();
            string built = BuildAddressFromPicker(out string opType);
            _addrPickerTag.Address = built;
            if (!PlcAddressHelper.IsDataTypeCompatibleWithOperandType(_addrPickerTag.DataType, opType))
                _addrPickerTag.DataType = PlcAddressHelper.DefaultDataTypeForOperandType(opType);

            NotifyProgramChanged();
            RenderTagTable();
        }
        AddressPickerPopup.IsOpen = false;
    }

    private void OnAddressPickerCancelClick(object sender, RoutedEventArgs e) => AddressPickerPopup.IsOpen = false;

    private void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        string nextAddr = ComputeNextDefaultTagAddress(out string nextType);
        var entry = new PlcTagEntry { Name = $"Tag_{_plc.DraftProgram.Tags.Count + 1}", Address = nextAddr, DataType = nextType };
        _plc.DraftProgram.Tags.Add(entry);
        _selectedTag = entry;
        _pendingFocusTag = entry;
        NotifyProgramChanged();
        RenderTagTable();
    }

    private void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedTag is null) return;
        PushUndoSnapshot();
        _plc.DraftProgram.Tags.Remove(_selectedTag);
        _selectedTag = null;
        NotifyProgramChanged();
        RenderTagTable();
    }

    // =========================================================================
    // 14. COMPILE & DOWNLOAD (HIỆU ỨNG NẠP 1.5s + NHẤP NHÁY LED PLC)
    // =========================================================================

    public PlcCompileResult CompileCurrentProgram()
    {
        var empty = new PlcCompileResult();
        if (_plc is null) return empty;

        var res = PlcProgramCompiler.Compile(_plc.DraftProgram);
        _plc.HasCompileError = !res.Success;
        if (!res.Success)
        {
            _plc.CpuState = PlcCpuState.Error;
            _plc.Runtime.LogDiagnostic(0x8001, PlcDiagSeverity.Error, "Compile",
                $"Compile failed: {res.ErrorCount} error(s), {res.WarningCount} warning(s).");
        }

        DiagnosticsStack.Children.Clear();
        if (res.Success)
        {
            TxtCompileSummary.Text = LocalizationManager.Format("Str.Plc.CompileOk", res.WarningCount);
            TxtCompileSummary.Foreground = CreateBrush("#22C55E");
        }
        else
        {
            TxtCompileSummary.Text = LocalizationManager.Format("Str.Plc.CompileFail", res.ErrorCount, res.WarningCount);
            TxtCompileSummary.Foreground = CreateBrush("#EF4444");
        }

        if (res.Diagnostics.Count > 0)
        {
            DiagnosticsScroller.Visibility = Visibility.Visible;
            foreach (var diag in res.Diagnostics)
            {
                var dBtn = new Button
                {
                    Content = (diag.IsError ? "🔴 " : "🟡 ") + diag.Message,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = CreateBrush(diag.IsError ? "#FCA5A5" : "#FDE047"),
                    FontSize = 10.8,
                    Padding = new Thickness(2, 1.5, 2, 1.5),
                    Cursor = Cursors.Hand
                };
                dBtn.Click += (_, _) => JumpToDiagnostic(diag);
                DiagnosticsStack.Children.Add(dBtn);
            }
        }
        else
        {
            DiagnosticsScroller.Visibility = Visibility.Collapsed;
        }

        UpdateCpuStateBadges();
        PlcHardwareVisualChanged?.Invoke();
        StatusMessageChanged?.Invoke(TxtCompileSummary.Text);
        return res;
    }

    private void JumpToDiagnostic(PlcDiagnostic diag)
    {
        if (_plc is null) return;

        // Nếu có NodeId, tự động tìm đúng khối (OB1 hoặc OB100 / FC / FB) chứa phần tử lỗi
        if (!string.IsNullOrEmpty(diag.NodeId))
        {
            for (int i = 0; i < _plc.DraftProgram.Networks.Count; i++)
            {
                if (TryFindNodeById(_plc.DraftProgram.Networks[i].MainBranch, diag.NodeId, out var fb, out var fn))
                {
                    _activeBlock = null;
                    RebuildBlockSelectorCombo();
                    RbTabLadder.IsChecked = true;
                    _selectedNetworkIndex = i;
                    _plc.DraftProgram.Networks[i].IsCollapsed = false;
                    _selectedBranch = fb;
                    _selectedNode = fn;
                    SelectAndScrollNode();
                    return;
                }
            }
            foreach (var blk in _plc.DraftProgram.AdditionalBlocks)
            {
                for (int i = 0; i < blk.Networks.Count; i++)
                {
                    if (TryFindNodeById(blk.Networks[i].MainBranch, diag.NodeId, out var fb, out var fn))
                    {
                        _activeBlock = blk;
                        RebuildBlockSelectorCombo();
                        RbTabLadder.IsChecked = true;
                        _selectedNetworkIndex = i;
                        blk.Networks[i].IsCollapsed = false;
                        _selectedBranch = fb;
                        _selectedNode = fn;
                        SelectAndScrollNode();
                        return;
                    }
                }
            }
        }

        var nets = GetActiveNetworks();
        if (diag.NetworkNumber <= 0 || diag.NetworkNumber > nets.Count)
            return;

        RbTabLadder.IsChecked = true;
        _selectedNetworkIndex = diag.NetworkNumber - 1;
        var net = nets[_selectedNetworkIndex];
        net.IsCollapsed = false;
        _selectedBranch = net.MainBranch;
        _selectedNode = null;
        SelectAndScrollNode();
    }

    private static bool TryFindNodeById(LadderBranch branch, string nodeId, out LadderBranch foundBranch, out LadderNode foundNode)
    {
        foreach (var n in branch.Nodes)
        {
            if (n.Id == nodeId) { foundBranch = branch; foundNode = n; return true; }
            if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
            {
                foreach (var sub in n.Branches)
                    if (TryFindNodeById(sub, nodeId, out foundBranch, out foundNode)) return true;
            }
        }
        foundBranch = branch;
        foundNode = null!;
        return false;
    }

    private void OnCompileClick(object sender, RoutedEventArgs e) => CompileCurrentProgram();

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _plc.IsDownloading) return;

        var res = CompileCurrentProgram();
        if (!res.Success)
        {
            TxtCompileSummary.Text = LocalizationManager.Get("Str.Plc.DownloadBlocked");
            TxtCompileSummary.Foreground = CreateBrush("#EF4444");
            StatusMessageChanged?.Invoke(TxtCompileSummary.Text);
            return;
        }

        var targetPlc = _plc;
        BtnDownload.IsEnabled = false;
        BtnCompile.IsEnabled = false;
        targetPlc.IsDownloading = true;
        targetPlc.CpuState = PlcCpuState.Downloading;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;

        TxtCompileSummary.Text = LocalizationManager.Format("Str.Plc.Downloading", targetPlc.DisplayName);
        TxtCompileSummary.Foreground = CreateBrush("#F59E0B");
        UpdateCpuStateBadges();

        const int steps = 30;
        for (int s = 1; s <= steps; s++)
        {
            targetPlc.DownloadProgress = s / (double)steps;
            targetPlc.UpdateVisualAnimation(0.08, isSimulating: true);
            DownloadProgressBar.Value = targetPlc.DownloadProgress * 100.0;
            PlcHardwareVisualChanged?.Invoke();
            await Task.Delay(50);
        }

        targetPlc.IsDownloading = false;
        targetPlc.DownloadProgress = 0;
        targetPlc.CommitDownloadedProgram(targetPlc.DraftProgram);

        DownloadProgressBar.Visibility = Visibility.Collapsed;
        BtnDownload.IsEnabled = true;
        BtnCompile.IsEnabled = true;

        TxtCompileSummary.Text = LocalizationManager.Format("Str.Plc.DownloadDone", targetPlc.DisplayName);
        TxtCompileSummary.Foreground = CreateBrush("#22C55E");
        StatusMessageChanged?.Invoke(TxtCompileSummary.Text);

        NotifyProgramChanged(markPendingDownload: false);
        UpdateCpuStateBadges();
        UpdateOnlineOverlayInPlace();
        PlcHardwareVisualChanged?.Invoke();
    }

    // =========================================================================
    // 15. NETWORK, PARALLEL BRANCH, CPU, ZOOM & THU GỌN
    // =========================================================================

    private void OnAddNetworkClick(object sender, RoutedEventArgs e) => InsertNetworkBelowCurrent();

    private void OnOpenParallelBranchClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (nets.Count == 0) return;
        RbTabLadder.IsChecked = true;
        PushUndoSnapshot();

        var net = nets[Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1)];
        var branch = _selectedBranch ?? net.MainBranch;

        if (_selectedNode is not null && _selectedNode.Kind == LadderElementKind.ParallelBlock && _selectedNode.Branches is not null)
        {
            var newSub = new LadderBranch();
            _selectedNode.Branches.Add(newSub);
            _selectedBranch = newSub;
            _selectedNode = null;
        }
        else if (_selectedNode is not null && branch.Nodes.Contains(_selectedNode))
        {
            int idx = branch.Nodes.IndexOf(_selectedNode);
            var wrapped = _selectedNode;
            var newSub = new LadderBranch();
            var pBlock = new LadderNode
            {
                Kind = LadderElementKind.ParallelBlock,
                Branches = [new LadderBranch { Nodes = [wrapped] }, newSub]
            };
            branch.Nodes[idx] = pBlock;
            _selectedBranch = newSub;
            _selectedNode = null;
        }
        else
        {
            var sub1 = new LadderBranch();
            var sub2 = new LadderBranch();
            branch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ParallelBlock, Branches = [sub1, sub2] });
            _selectedBranch = sub1;
            _selectedNode = null;
        }

        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void OnCloseParallelBranchClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (nets.Count == 0) return;
        var net = nets[Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1)];
        var curBranch = _selectedBranch ?? net.MainBranch;

        // Thoát đúng 1 cấp ra nhánh cha trực tiếp (hỗ trợ nhánh song song lồng nhau)
        if (curBranch != net.MainBranch && TryFindParentParallel(net.MainBranch, curBranch, out var parentBranch, out var pBlock, out _))
        {
            _selectedBranch = parentBranch;
            _selectedNode = pBlock;
        }
        else
        {
            _selectedBranch = net.MainBranch;
            _selectedNode = net.MainBranch.Nodes.LastOrDefault();
        }
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void OnDeleteSelectedNodeClick(object sender, RoutedEventArgs e) => DeleteSelection();

    private void OnCpuRunClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _plc.SetRunMode();
        UpdateCpuStateBadges();
        PlcHardwareVisualChanged?.Invoke();
    }

    private void OnCpuStopClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        _plc.SetStopMode();
        UpdateCpuStateBadges();
        PlcHardwareVisualChanged?.Invoke();
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        CancelInlineEdit(rerender: false);
        CloseAllPopups();
        RefreshAllViews();
    }

    private void OnToggleInstructionsClick(object sender, RoutedEventArgs e)
    {
        _isInstructionsVisible = !_isInstructionsVisible;
        InstructionsPanelBorder.Visibility = _isInstructionsVisible ? Visibility.Visible : Visibility.Collapsed;
        InstructionsColumn.Width = _isInstructionsVisible ? new GridLength(264) : new GridLength(0);
    }

    private void OnHideEditorClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    public void SetZoomPercent(double percent)
    {
        double clamped = Math.Clamp(Math.Round(percent), 50, 200);
        _updatingZoomUi = true;
        LadderZoomTransform.ScaleX = clamped / 100.0;
        LadderZoomTransform.ScaleY = clamped / 100.0;
        if (SliderLadderZoom is not null) SliderLadderZoom.Value = clamped;
        if (CbLadderZoom is not null)
        {
            foreach (ComboBoxItem item in CbLadderZoom.Items)
            {
                if (item.Tag is string s && double.TryParse(s, out double v) && Math.Abs(v - clamped) < 1.0)
                {
                    CbLadderZoom.SelectedItem = item;
                    break;
                }
            }
        }
        _updatingZoomUi = false;
    }

    private void OnZoomSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingZoomUi || LadderZoomTransform is null) return;
        SetZoomPercent(e.NewValue);
    }

    private void OnZoomComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingZoomUi || LadderZoomTransform is null) return;
        if (CbLadderZoom.SelectedItem is ComboBoxItem item && item.Tag is string s && double.TryParse(s, out double v))
            SetZoomPercent(v);
    }

    private void OnLadderPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetZoomPercent(SliderLadderZoom.Value + (e.Delta > 0 ? 15.0 : -15.0));
            e.Handled = true;
        }
    }

    private void NotifyProgramChanged(bool markPendingDownload = true)
    {
        if (_plc is not null && markPendingDownload)
            _plc.HasPendingDownload = true;
        UpdateCpuStateBadges();
        ProgramEdited?.Invoke();
    }

    private static readonly Dictionary<string, SolidColorBrush> EditorBrushCache = new(StringComparer.OrdinalIgnoreCase);

    private static SolidColorBrush CreateBrush(string hex)
    {
        if (EditorBrushCache.TryGetValue(hex, out var cached))
            return cached;
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        EditorBrushCache[hex] = b;
        return b;
    }
}
