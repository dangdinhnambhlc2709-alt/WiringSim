using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

/// <summary>
/// Trình lập trình PLC Ladder chuẩn Siemens TIA Portal (10 nhóm hàm/lệnh đầy đủ):
/// - Thanh General (Insert network, Empty box [F8], Insert input, Open/Close branch [Shift+F8/F9]).
/// - Cây Thư viện 10 nhóm hàm/lệnh PLC (2 cột: Ký hiệu chuyên ngành + Mô tả bản địa hóa + Ô tìm kiếm nhanh).
/// - Compile kiểm tra nhánh hở, địa chỉ, trùng %DB, nhảy tới LABEL không tồn tại &amp; vòng lặp vô hạn.
/// - Download 1.5s kèm nhấp nháy LED RUN/STOP &amp; MAINT + Online Monitoring xanh lá thời gian thực + Zoom 50%–200%.
/// </summary>
public partial class PlcLadderEditorControl : UserControl
{
    private PlcS71200Component? _plc;
    private bool _isInstructionsVisible = true;
    private bool _isSimulating;
    private bool _updatingZoomUi;

    private int _selectedNetworkIndex = 0;
    private LadderBranch? _selectedBranch;
    private LadderNode? _selectedNode;
    private PlcTagEntry? _selectedTag;

    public event Action? HideRequested;
    public event Action? ProgramEdited;
    public event Action? PlcHardwareVisualChanged;
    public event Action<string>? StatusMessageChanged;

    public PlcS71200Component? ActivePlc => _plc;

    public bool ShowEmbeddedHeader
    {
        get => EmbeddedHeaderBar.Visibility == Visibility.Visible;
        set => EmbeddedHeaderBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public PlcLadderEditorControl()
    {
        InitializeComponent();
        BuildInstructionsTree(string.Empty);

        LocalizationManager.LanguageChanged += () =>
        {
            BuildInstructionsTree(TbInstructionSearch?.Text ?? string.Empty);
            RefreshAllViews();
        };
        ThemeManager.ThemeChanged += RefreshAllViews;
    }

    public void BindPlc(PlcS71200Component? plc, bool openTagTable = false)
    {
        _plc = plc;
        _selectedNetworkIndex = 0;
        _selectedNode = null;
        _selectedBranch = plc?.DraftProgram.Networks.FirstOrDefault()?.MainBranch;
        _selectedTag = null;

        if (openTagTable)
            RbTabTags.IsChecked = true;
        else
            RbTabLadder.IsChecked = true;

        DiagnosticsStack.Children.Clear();
        DiagnosticsScroller.Visibility = Visibility.Collapsed;
        TxtCompileSummary.Text = string.Empty;

        RefreshAllViews();
    }

    public void SetSimulationMode(bool isSimulating)
    {
        _isSimulating = isSimulating;
        RefreshOnlineMonitoring();
    }

    public void RefreshOnlineMonitoring()
    {
        if (_plc is null) return;
        UpdateCpuStateBadges();

        if (RbTabLadder.IsChecked == true)
            RenderNetworks();
        else
            RenderTagTable();
    }

    private void RefreshAllViews()
    {
        bool hasPlc = _plc is not null;
        TxtNoPlcPlaceholder.Visibility = hasPlc ? Visibility.Collapsed : Visibility.Visible;
        LadderScrollViewer.Visibility = hasPlc && RbTabLadder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TagTableContainer.Visibility = hasPlc && RbTabTags.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PropertyEditBar.Visibility = hasPlc && RbTabLadder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TxtPlcHeader.Text = hasPlc
            ? $"{_plc!.DisplayName} — {LocalizationManager.Get("Str.Plc.TabLadder")}"
            : LocalizationManager.Get("Str.Plc.EditorTitle");

        UpdateCpuStateBadges();
        if (!hasPlc) return;

        if (RbTabLadder.IsChecked == true)
        {
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
        else
        {
            RenderTagTable();
        }
    }

    private void UpdateCpuStateBadges()
    {
        if (_plc is null)
        {
            CpuStatePill.Visibility = Visibility.Collapsed;
            OnlinePill.Visibility = Visibility.Collapsed;
            return;
        }

        CpuStatePill.Visibility = Visibility.Visible;
        if (_plc.IsDownloading)
        {
            TxtCpuState.Text = "LOADING";
            CpuStatePill.Background = CreateBrush("#D97706");
        }
        else if (_plc.HasOutputShortFault || _plc.HasCompileError || _plc.CpuState == PlcCpuState.Error)
        {
            TxtCpuState.Text = "ERROR";
            CpuStatePill.Background = CreateBrush("#DC2626");
        }
        else if (_plc.CpuState == PlcCpuState.Run)
        {
            TxtCpuState.Text = "RUN";
            CpuStatePill.Background = CreateBrush("#059669");
        }
        else
        {
            TxtCpuState.Text = "STOP";
            CpuStatePill.Background = CreateBrush("#D97706");
        }

        OnlinePill.Visibility = _isSimulating && _plc.IsPowered && _plc.CpuState == PlcCpuState.Run && !_plc.IsDownloading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // =========================================================================
    // 1. CÂY THƯ VIỆN 10 NHÓM HÀM/LỆNH PLC (2 CỘT + TÌM KIẾM NHANH)
    // =========================================================================

    private void OnInstructionSearchChanged(object sender, TextChangedEventArgs e)
    {
        string q = TbInstructionSearch.Text ?? string.Empty;
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        BuildInstructionsTree(q);
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        TbInstructionSearch.Text = string.Empty;
        TbInstructionSearch.Focus();
    }

    private void BuildInstructionsTree(string searchFilter)
    {
        InstructionsStack.Children.Clear();
        string q = searchFilter.Trim();
        bool isFiltering = !string.IsNullOrEmpty(q);

        foreach (var (grpIdx, titleKey, accentHex) in PlcInstructionCatalog.Groups)
        {
            var itemsInGroup = PlcInstructionCatalog.All
                .Where(m => m.GroupIndex == grpIdx)
                .Where(m => !isFiltering ||
                            m.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            m.BoxHeader.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            PlcInstructionCatalog.GetDescription(m).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            (m.SubGroupName is not null && m.SubGroupName.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (itemsInGroup.Count == 0) continue;

            var panel = new StackPanel { Margin = new Thickness(2, 2, 0, 4) };
            string? currentSub = null;

            foreach (var meta in itemsInGroup)
            {
                if (meta.SubGroupName != currentSub && !string.IsNullOrEmpty(meta.SubGroupName))
                {
                    currentSub = meta.SubGroupName;
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"▸ {currentSub}",
                        FontSize = 9.8,
                        FontWeight = FontWeights.Bold,
                        Foreground = CreateBrush(accentHex),
                        Margin = new Thickness(4, 5, 2, 2)
                    });
                }

                panel.Children.Add(CreateInstructionRowButton(meta));
            }

            var exp = new Expander
            {
                Header = new TextBlock
                {
                    Text = LocalizationManager.Get(titleKey),
                    FontSize = 10.6,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                IsExpanded = isFiltering || grpIdx <= 2,
                Margin = new Thickness(0, 1, 0, 3),
                Content = panel
            };
            exp.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
            InstructionsStack.Children.Add(exp);
        }
    }

    private Button CreateInstructionRowButton(PlcInstructionMeta meta)
    {
        string desc = PlcInstructionCatalog.GetDescription(meta);

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var symTb = new TextBlock
        {
            Text = meta.Symbol,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.4,
            FontWeight = FontWeights.Bold,
            Foreground = CreateBrush(meta.AccentHex),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(symTb, 0);

        var descTb = new TextBlock
        {
            Text = desc,
            FontSize = 9.8,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0)
        };
        descTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(descTb, 1);

        rowGrid.Children.Add(symTb);
        rowGrid.Children.Add(descTb);

        var btn = new Button
        {
            Content = rowGrid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(5, 3.5, 5, 3.5),
            Margin = new Thickness(0, 0, 0, 2.5),
            ToolTip = $"{meta.Symbol} — {desc}"
        };
        btn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btn.Click += (_, _) => InsertInstruction(meta.Kind);
        return btn;
    }

    private void InsertInstruction(LadderElementKind kind)
    {
        if (_plc is null) return;
        if (RbTabLadder.IsChecked != true) RbTabLadder.IsChecked = true;

        var prog = _plc.DraftProgram;
        if (prog.Networks.Count == 0)
        {
            prog.Networks.Add(new LadderNetwork { Title = "Network 1" });
            _selectedNetworkIndex = 0;
        }
        _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, prog.Networks.Count - 1);
        var targetNet = prog.Networks[_selectedNetworkIndex];
        var branch = _selectedBranch ?? targetNet.MainBranch;

        var node = PlcInstructionCatalog.CreateDefaultNode(kind, prog);

        int insertIdx = _selectedNode is not null ? branch.Nodes.IndexOf(_selectedNode) + 1 : branch.Nodes.Count;
        if (insertIdx <= 0 || insertIdx > branch.Nodes.Count)
            insertIdx = branch.Nodes.Count;

        branch.Nodes.Insert(insertIdx, node);
        _selectedBranch = branch;
        _selectedNode = node;

        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();

        if (kind == LadderElementKind.EmptyBox)
        {
            TbPropAddress.Focus();
            TbPropAddress.SelectAll();
        }
    }

    // --- Phím tắt chuẩn TIA Portal: F8 (Empty box), Shift+F8 (Open branch), Shift+F9 (Close branch) ---
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_plc is null || RbTabLadder.IsChecked != true) return;
        if (e.Key == Key.F8 && Keyboard.Modifiers == ModifierKeys.None)
        {
            InsertInstruction(LadderElementKind.EmptyBox);
            e.Handled = true;
        }
        else if (e.Key == Key.F8 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            OnOpenParallelBranchClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            OnCloseParallelBranchClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnInsertEmptyBoxClick(object sender, RoutedEventArgs e) => InsertInstruction(LadderElementKind.EmptyBox);
    private void OnInsertInputClick(object sender, RoutedEventArgs e) => InsertInstruction(LadderElementKind.ContactNO);

    // =========================================================================
    // 2. KẾT XUẤT TIA PORTAL NETWORKS + PARALLEL BRANCHES + ONLINE MONITORING
    // =========================================================================

    private void RenderNetworks()
    {
        NetworksContainer.Children.Clear();
        if (_plc is null) return;

        bool isOnline = _isSimulating && _plc.IsPowered && _plc.CpuState == PlcCpuState.Run && !_plc.IsDownloading;
        var prog = _plc.DraftProgram;

        for (int i = 0; i < prog.Networks.Count; i++)
        {
            int netIdx = i;
            var net = prog.Networks[i];
            bool isSelectedNet = netIdx == _selectedNetworkIndex;

            var card = new Border
            {
                BorderThickness = new Thickness(isSelectedNet ? 1.5 : 1.0),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 10),
                MinWidth = 520
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, isSelectedNet ? "AccentCyanBrush" : "BorderSubtleBrush");

            var cardStack = new StackPanel();

            var hdrBorder = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                BorderThickness = new Thickness(0, 0, 0, net.IsCollapsed ? 0 : 1),
                Cursor = Cursors.Hand
            };
            hdrBorder.SetResourceReference(Border.BackgroundProperty, "SubtleSurfaceBrush");
            hdrBorder.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

            var hdrDock = new DockPanel();
            var btnDelNet = new Button { Content = "✕", Width = 22, Height = 20, Padding = new Thickness(0), FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            btnDelNet.SetResourceReference(StyleProperty, "DangerToolbarButtonStyle");
            btnDelNet.Click += (_, e) =>
            {
                e.Handled = true;
                prog.Networks.RemoveAt(netIdx);
                _selectedNetworkIndex = Math.Max(0, Math.Min(_selectedNetworkIndex, prog.Networks.Count - 1));
                _selectedNode = null;
                _selectedBranch = prog.Networks.ElementAtOrDefault(_selectedNetworkIndex)?.MainBranch;
                NotifyProgramChanged();
                RenderNetworks();
                UpdatePropertyBarForSelectedNode();
            };
            DockPanel.SetDock(btnDelNet, Dock.Right);
            hdrDock.Children.Add(btnDelNet);

            var collapseBtn = new Button
            {
                Content = net.IsCollapsed ? "▶" : "▼",
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 10
            };
            collapseBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
            collapseBtn.Click += (_, e) =>
            {
                e.Handled = true;
                net.IsCollapsed = !net.IsCollapsed;
                RenderNetworks();
            };
            DockPanel.SetDock(collapseBtn, Dock.Left);
            hdrDock.Children.Add(collapseBtn);

            var titleTb = new TextBlock
            {
                Text = $"Network {netIdx + 1}: {net.Title}",
                FontSize = 11.6,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleTb.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
            hdrDock.Children.Add(titleTb);

            hdrBorder.Child = hdrDock;
            hdrBorder.MouseLeftButtonDown += (_, _) =>
            {
                _selectedNetworkIndex = netIdx;
                _selectedBranch = net.MainBranch;
                _selectedNode = null;
                RenderNetworks();
                UpdatePropertyBarForSelectedNode();
            };
            cardStack.Children.Add(hdrBorder);

            if (!net.IsCollapsed)
            {
                var bodyGrid = new Grid { Margin = new Thickness(10, 10, 12, 12) };
                bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var powerRail = new Border
                {
                    Width = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = CreateBrush(isOnline ? "#22C55E" : "#3B82F6"),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(powerRail, 0);
                bodyGrid.Children.Add(powerRail);

                var branchView = BuildBranchVisual(net.MainBranch, netIdx, isOnline);
                Grid.SetColumn(branchView, 1);
                bodyGrid.Children.Add(branchView);

                cardStack.Children.Add(bodyGrid);
            }

            card.Child = cardStack;
            NetworksContainer.Children.Add(card);
        }
    }

    private UIElement BuildBranchVisual(LadderBranch branch, int netIdx, bool isOnline)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (branch.Nodes.Count == 0)
        {
            var emptySlot = new Border
            {
                MinWidth = 90,
                Height = 42,
                Margin = new Thickness(4, 6, 4, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = CreateBrush(branch == _selectedBranch ? "#38BDF8" : "#475569"),
                Background = CreateBrush("#1438BDF8"),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "— (Empty) —",
                    FontSize = 10,
                    Foreground = CreateBrush("#94A3B8"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            emptySlot.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _selectedNetworkIndex = netIdx;
                _selectedBranch = branch;
                _selectedNode = null;
                RenderNetworks();
                UpdatePropertyBarForSelectedNode();
            };
            row.Children.Add(emptySlot);
            return row;
        }

        foreach (var node in branch.Nodes)
        {
            if (node.Kind == LadderElementKind.ParallelBlock && node.Branches is not null)
                row.Children.Add(BuildParallelBlockVisual(node, branch, netIdx, isOnline));
            else
                row.Children.Add(BuildElementVisual(node, branch, netIdx, isOnline));
        }

        return row;
    }

    private UIElement BuildParallelBlockVisual(LadderNode blockNode, LadderBranch parentBranch, int netIdx, bool isOnline)
    {
        bool powerIn = isOnline && _plc!.Runtime.LiveNodePowerIn.GetValueOrDefault(blockNode.Id);
        bool powerOut = isOnline && _plc!.Runtime.LiveNodePowerOut.GetValueOrDefault(blockNode.Id);
        bool isSelected = blockNode == _selectedNode;

        var outer = new Grid { Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });

        var leftHost = new Grid();
        leftHost.Children.Add(new Border { Height = 2.5, Width = 9, Background = CreateBrush(powerIn ? "#22C55E" : "#64748B"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        leftHost.Children.Add(new Border { Width = 3, Margin = new Thickness(8, 22, 0, 22), Background = CreateBrush(powerIn ? "#22C55E" : (isSelected ? "#38BDF8" : "#64748B")), HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(leftHost, 0);
        outer.Children.Add(leftHost);

        var subStack = new StackPanel();
        foreach (var sub in blockNode.Branches!)
        {
            var subRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            bool subActive = isOnline && sub.Nodes.Count > 0 && _plc!.Runtime.LiveNodePowerOut.GetValueOrDefault(sub.Nodes[^1].Id);
            var subLead = new Border { Height = 2.2, Width = 8, Background = CreateBrush(powerIn ? "#22C55E" : "#64748B"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(subLead, Dock.Left);
            subRow.Children.Add(subLead);

            var subContent = BuildBranchVisual(sub, netIdx, isOnline);
            DockPanel.SetDock(subContent, Dock.Left);
            subRow.Children.Add(subContent);

            subRow.Children.Add(new Border { Height = 2.2, MinWidth = 8, Background = CreateBrush(subActive ? "#22C55E" : "#64748B"), VerticalAlignment = VerticalAlignment.Center });
            subStack.Children.Add(subRow);
        }
        Grid.SetColumn(subStack, 1);
        outer.Children.Add(subStack);

        var rightHost = new Grid();
        rightHost.Children.Add(new Border { Width = 3, Margin = new Thickness(0, 22, 8, 22), Background = CreateBrush(powerOut ? "#22C55E" : (isSelected ? "#38BDF8" : "#64748B")), HorizontalAlignment = HorizontalAlignment.Right });
        rightHost.Children.Add(new Border { Height = 2.5, Width = 9, Background = CreateBrush(powerOut ? "#22C55E" : "#64748B"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(rightHost, 2);
        outer.Children.Add(rightHost);

        outer.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            _selectedNetworkIndex = netIdx;
            _selectedBranch = parentBranch;
            _selectedNode = blockNode;
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        };

        return outer;
    }

    private UIElement BuildElementVisual(LadderNode node, LadderBranch ownerBranch, int netIdx, bool isOnline)
    {
        bool powerIn = isOnline && _plc!.Runtime.LiveNodePowerIn.GetValueOrDefault(node.Id);
        bool powerOut = isOnline && _plc!.Runtime.LiveNodePowerOut.GetValueOrDefault(node.Id);
        bool isSelected = node == _selectedNode;
        string liveText = isOnline ? _plc!.Runtime.LiveNodeStatusText.GetValueOrDefault(node.Id, string.Empty) : string.Empty;

        string wireInHex = powerIn ? "#22C55E" : "#64748B";
        string wireOutHex = powerOut ? "#22C55E" : "#64748B";
        string addrTeal = ThemeManager.IsEffectiveDark ? "#2DD4BF" : "#0F766E";
        string paramBlue = ThemeManager.IsEffectiveDark ? "#60A5FA" : "#1D4ED8";

        var meta = PlcInstructionCatalog.Get(node.Kind);

        var container = new Border
        {
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(isSelected ? 1.6 : 0),
            BorderBrush = CreateBrush("#38BDF8"),
            Background = isSelected ? CreateBrush("#2238BDF8") : Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        container.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _selectedNetworkIndex = netIdx;
            _selectedBranch = ownerBranch;
            _selectedNode = node;
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        };

        // --- Ký hiệu tiếp điểm / cuộn hút / nhãn nhảy inline ---
        if (meta is { IsInlineGlyph: true })
        {
            string displayAddr = node.Kind is LadderElementKind.Jump or LadderElementKind.JumpNot
                ? node.Address
                : PlcProgramModel.NormalizeAddress(node.Address);
            string sym = node.Kind is LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.InvertRLO or LadderElementKind.Return
                ? string.Empty
                : _plc!.DraftProgram.ResolveTagName(displayAddr, node.SymbolName);

            var vStack = new StackPanel { Width = 98, HorizontalAlignment = HorizontalAlignment.Center };

            vStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(displayAddr) ? " " : displayAddr,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.0,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(addrTeal),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            vStack.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(sym) ? $"\"{sym}\"" : (!string.IsNullOrEmpty(node.Param1) ? node.Param1 : " "),
                FontSize = 9.2,
                FontWeight = FontWeights.SemiBold,
                Foreground = !string.IsNullOrEmpty(sym) ? (Brush)FindResource("TextPrimaryBrush") : CreateBrush(paramBlue),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var glyphGrid = new Grid { Height = 26, Margin = new Thickness(0, 2, 0, 2) };
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftLine = new Border { Height = 2.4, Background = CreateBrush(wireInHex), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(leftLine, 0);
            glyphGrid.Children.Add(leftLine);

            bool activeGlow = isOnline && powerOut;
            var glyphBorder = new Border
            {
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(3),
                Background = activeGlow ? CreateBrush("#3322C55E") : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = meta.BoxHeader,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12.6,
                    FontWeight = FontWeights.Bold,
                    Foreground = CreateBrush(activeGlow ? "#22C55E" : (ThemeManager.IsEffectiveDark ? "#F8FAFC" : "#0F172A")),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(glyphBorder, 1);
            glyphGrid.Children.Add(glyphBorder);

            var rightLine = new Border { Height = 2.4, Background = CreateBrush(wireOutHex), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rightLine, 2);
            glyphGrid.Children.Add(rightLine);

            vStack.Children.Add(glyphGrid);
            container.Child = vStack;
            return container;
        }

        // --- Khối hàm chuẩn TIA Portal (hỗ trợ đồng bộ tất cả 10 nhóm + Empty box) ---
        var boxGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

        var inWire = new Border { Height = 2.4, Background = CreateBrush(wireInHex), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(inWire, 0);
        boxGrid.Children.Add(inWire);

        var outWire = new Border { Height = 2.4, Background = CreateBrush(wireOutHex), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(outWire, 2);
        boxGrid.Children.Add(outWire);

        var blockOuter = new StackPanel { MinWidth = 148 };

        // Địa chỉ DB hoặc địa chỉ chính ở trên hộp (chuẩn TIA Portal)
        if (meta is { UsesDbBlock: true } || node.Kind is LadderElementKind.FlipFlopSR or LadderElementKind.FlipFlopRS or LadderElementKind.SetBitField or LadderElementKind.ResetBitField or LadderElementKind.PTrig or LadderElementKind.NTrig)
        {
            string normA = PlcProgramModel.NormalizeAddress(node.Address);
            string sym = _plc!.DraftProgram.ResolveTagName(normA, node.SymbolName);
            blockOuter.Children.Add(new TextBlock
            {
                Text = normA,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(addrTeal),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            if (!string.IsNullOrEmpty(sym))
            {
                blockOuter.Children.Add(new TextBlock
                {
                    Text = $"\"{sym}\"",
                    FontSize = 9.1,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }

        bool boxActive = isOnline && powerOut;
        var boxBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(boxActive ? 2.0 : 1.3),
            BorderBrush = CreateBrush(boxActive ? "#22C55E" : (node.Kind == LadderElementKind.EmptyBox ? "#F59E0B" : (powerIn ? "#38BDF8" : "#64748B"))),
            Margin = new Thickness(0, 2, 0, 2)
        };
        boxBorder.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        var boxStack = new StackPanel();
        string headerTitle = node.Kind == LadderElementKind.EmptyBox
            ? $"?? {node.Address}"
            : (meta?.BoxHeader ?? node.Kind.ToString());
        string headerHex = node.Kind == LadderElementKind.EmptyBox ? "#B45309" : (meta?.AccentHex ?? "#0284C7");

        boxStack.Children.Add(new Border
        {
            Background = CreateBrush(boxActive ? "#15803D" : headerHex),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Padding = new Thickness(8, 2.5, 8, 2.5),
            Child = new TextBlock
            {
                Text = headerTitle,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.6,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        });

        var bodyStack = new StackPanel { Margin = new Thickness(7, 4, 7, 5) };
        if (node.Kind == LadderElementKind.EmptyBox)
        {
            AddBoxParamRow(bodyStack, "CMD", "Type name (TON, NORM_X...)", "#F59E0B", "", "", "#94A3B8");
        }
        else if (node.IsTimer)
        {
            AddBoxParamRow(bodyStack, "PT", node.Param1, paramBlue, "Q", boxActive ? "1" : "0", boxActive ? "#22C55E" : "#94A3B8");
            if (node.Kind == LadderElementKind.TimerTONR)
                AddBoxParamRow(bodyStack, "R", node.Param2, addrTeal, "ET", string.IsNullOrEmpty(liveText) ? "T#0ms" : liveText, "#22C55E");
            else
                AddBoxParamRow(bodyStack, "ET", string.IsNullOrEmpty(liveText) ? "T#0ms" : liveText, "#22C55E", "", "", "#94A3B8");
        }
        else if (node.IsCounter)
        {
            string rLabel = node.Kind == LadderElementKind.CounterCTD ? "LD" : "R";
            AddBoxParamRow(bodyStack, "PV", node.Param1, paramBlue, "Q", boxActive ? "1" : "0", boxActive ? "#22C55E" : "#94A3B8");
            AddBoxParamRow(bodyStack, rLabel, string.IsNullOrWhiteSpace(node.Param2) ? "-" : node.Param2, addrTeal, "CV", string.IsNullOrEmpty(liveText) ? "0" : liveText.Replace("CV: ", ""), "#22C55E");
        }
        else if (node.Kind == LadderElementKind.Label)
        {
            AddBoxParamRow(bodyStack, "LABEL", node.Address, "#F97316", "", "", "#94A3B8");
        }
        else if (meta is not null)
        {
            if (meta.AddrLabel is not null && !meta.UsesDbBlock &&
                node.Kind is not (LadderElementKind.FlipFlopSR or LadderElementKind.FlipFlopRS or LadderElementKind.SetBitField or LadderElementKind.ResetBitField or LadderElementKind.PTrig or LadderElementKind.NTrig))
                AddBoxParamRow(bodyStack, meta.AddrLabel, node.Address, addrTeal, "", "", "#94A3B8");

            if (meta.Param1Label is not null || meta.OutLabel is not null)
                AddBoxParamRow(bodyStack,
                    meta.Param1Label ?? "", node.Param1, node.Param1.StartsWith('%') ? addrTeal : paramBlue,
                    meta.OutLabel ?? "", node.OutputAddress, addrTeal);

            if (meta.Param2Label is not null)
                AddBoxParamRow(bodyStack, meta.Param2Label, node.Param2, node.Param2.StartsWith('%') ? addrTeal : paramBlue,
                    "", string.IsNullOrEmpty(meta.ExtraLabel) ? liveText : "", "#22C55E");

            if (meta.ExtraLabel is not null)
                AddBoxParamRow(bodyStack, meta.ExtraLabel, node.ExtraParam, paramBlue, "", liveText, "#22C55E");
            else if (meta.Param2Label is null && !string.IsNullOrEmpty(liveText))
                AddBoxParamRow(bodyStack, "VAL", liveText, "#22C55E", "", "", "#94A3B8");
        }

        boxStack.Children.Add(bodyStack);
        boxBorder.Child = boxStack;
        blockOuter.Children.Add(boxBorder);

        Grid.SetColumn(blockOuter, 1);
        boxGrid.Children.Add(blockOuter);
        container.Child = boxGrid;
        return container;
    }

    private static void AddBoxParamRow(StackPanel host, string leftKey, string leftVal, string leftHex, string rightKey, string rightVal, string rightHex)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lStack = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(leftKey))
            lStack.Children.Add(new TextBlock { Text = $"{leftKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8") });
        if (!string.IsNullOrEmpty(leftVal))
            lStack.Children.Add(new TextBlock { Text = leftVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(leftHex) });
        Grid.SetColumn(lStack, 0);
        g.Children.Add(lStack);

        if (!string.IsNullOrEmpty(rightKey) || !string.IsNullOrEmpty(rightVal))
        {
            var rStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            if (!string.IsNullOrEmpty(rightKey))
                rStack.Children.Add(new TextBlock { Text = $"{rightKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8") });
            rStack.Children.Add(new TextBlock { Text = rightVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(rightHex) });
            Grid.SetColumn(rStack, 2);
            g.Children.Add(rStack);
        }

        host.Children.Add(g);
    }

    // =========================================================================
    // 3. THANH CHỈNH SỬA NHANH THÔNG SỐ (HỖ TRỢ ĐẦY ĐỦ 10 NHÓM + EMPTY BOX)
    // =========================================================================

    private void UpdatePropertyBarForSelectedNode()
    {
        PanelPropAddress.Visibility = Visibility.Collapsed;
        PanelPropSymbol.Visibility = Visibility.Collapsed;
        PanelPropParam1.Visibility = Visibility.Collapsed;
        PanelPropParam2.Visibility = Visibility.Collapsed;
        PanelPropExtra.Visibility = Visibility.Collapsed;
        PanelPropOut.Visibility = Visibility.Collapsed;
        BtnApplyNodeProps.Visibility = Visibility.Collapsed;

        if (_plc is null || _selectedNode is null || _selectedNode.Kind == LadderElementKind.ParallelBlock)
        {
            TxtSelectedNodeSummary.Text = LocalizationManager.Get("Str.Plc.PropBarHint");
            return;
        }

        var n = _selectedNode;
        BtnApplyNodeProps.Visibility = Visibility.Visible;

        if (n.Kind == LadderElementKind.EmptyBox)
        {
            TxtSelectedNodeSummary.Text = "[Empty box]";
            PanelPropAddress.Visibility = Visibility.Visible;
            LblPropAddress.Text = "Instruction:";
            TbPropAddress.Text = n.Address == "???" ? string.Empty : n.Address;
            return;
        }

        var meta = PlcInstructionCatalog.Get(n.Kind);
        TxtSelectedNodeSummary.Text = $"[{meta?.Symbol ?? n.Kind.ToString()}]";

        if (meta?.AddrLabel is not null)
        {
            PanelPropAddress.Visibility = Visibility.Visible;
            LblPropAddress.Text = $"{meta.AddrLabel}:";
            TbPropAddress.Text = n.Address;

            if (n.Kind is not (LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.Label or LadderElementKind.JumpList or LadderElementKind.Switch))
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

        // Nếu đang ở Empty box [F8] -> chuyển đổi thành hàm/lệnh thực tế theo tên người dùng gõ!
        if (_selectedNode.Kind == LadderElementKind.EmptyBox)
        {
            string typed = TbPropAddress.Text.Trim();
            if (PlcInstructionCatalog.TryResolveByName(typed, out var resolvedKind))
            {
                var replacement = PlcInstructionCatalog.CreateDefaultNode(resolvedKind, _plc.DraftProgram);
                _selectedNode.Kind = replacement.Kind;
                _selectedNode.Address = replacement.Address;
                _selectedNode.SymbolName = replacement.SymbolName;
                _selectedNode.Param1 = replacement.Param1;
                _selectedNode.Param2 = replacement.Param2;
                _selectedNode.ExtraParam = replacement.ExtraParam;
                _selectedNode.OutputAddress = replacement.OutputAddress;
                NotifyProgramChanged();
                RenderNetworks();
                UpdatePropertyBarForSelectedNode();
            }
            else
            {
                _selectedNode.Address = string.IsNullOrEmpty(typed) ? "???" : typed;
                RenderNetworks();
            }
            return;
        }

        if (PanelPropAddress.Visibility == Visibility.Visible)
            _selectedNode.Address = TbPropAddress.Text.Trim();
        if (PanelPropSymbol.Visibility == Visibility.Visible)
        {
            string sym = TbPropSymbol.Text.Trim();
            _selectedNode.SymbolName = sym;
            if (!string.IsNullOrWhiteSpace(sym) && !string.IsNullOrWhiteSpace(_selectedNode.Address))
            {
                string norm = PlcProgramModel.NormalizeAddress(_selectedNode.Address);
                var existing = _plc.DraftProgram.Tags.FirstOrDefault(t => PlcProgramModel.NormalizeAddress(t.Address) == norm);
                if (existing is not null)
                    existing.Name = sym;
                else
                    _plc.DraftProgram.Tags.Add(new PlcTagEntry
                    {
                        Name = sym,
                        Address = norm,
                        DataType = _selectedNode.IsTimer ? "IEC_Timer" : (_selectedNode.IsCounter ? "IEC_Counter" : (norm.StartsWith("%MD") ? "Real" : (norm.StartsWith("%MW") ? "Int" : "Bool")))
                    });
            }
        }
        if (PanelPropParam1.Visibility == Visibility.Visible) _selectedNode.Param1 = TbPropParam1.Text.Trim();
        if (PanelPropParam2.Visibility == Visibility.Visible) _selectedNode.Param2 = TbPropParam2.Text.Trim();
        if (PanelPropExtra.Visibility == Visibility.Visible) _selectedNode.ExtraParam = TbPropExtra.Text.Trim();
        if (PanelPropOut.Visibility == Visibility.Visible) _selectedNode.OutputAddress = TbPropOut.Text.Trim();

        NotifyProgramChanged();
        RenderNetworks();
    }

    // =========================================================================
    // 4. BẢNG KÝ HIỆU BIẾN (PLC TAG TABLE)
    // =========================================================================

    private void RenderTagTable()
    {
        TagRowsStack.Children.Clear();
        if (_plc is null) return;

        TagRowsStack.Children.Add(CreateTagRowGrid(
            LocalizationManager.Get("Str.Plc.TagName"),
            LocalizationManager.Get("Str.Plc.TagAddr"),
            LocalizationManager.Get("Str.Plc.TagType"),
            LocalizationManager.Get("Str.Plc.TagLive"),
            isHeader: true,
            tagEntry: null));

        foreach (var tag in _plc.DraftProgram.Tags)
        {
            string liveVal = _isSimulating && _plc.IsPowered ? _plc.Runtime.FormatLiveValueForAddress(tag.Address) : "—";
            TagRowsStack.Children.Add(CreateTagRowGrid(tag.Name, tag.Address, tag.DataType, liveVal, isHeader: false, tagEntry: tag));
        }
    }

    private UIElement CreateTagRowGrid(string name, string addr, string type, string liveVal, bool isHeader, PlcTagEntry? tagEntry)
    {
        var border = new Border
        {
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, isHeader ? "SubtleSurfaceBrush" : (tagEntry == _selectedTag ? "MenuHoverBrush" : "CardBgBrush"));
        border.SetResourceReference(Border.BorderBrushProperty, tagEntry == _selectedTag && !isHeader ? "AccentCyanBrush" : "BorderSubtleBrush");

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

        if (isHeader)
        {
            AddHeaderCell(g, 0, name);
            AddHeaderCell(g, 1, addr);
            AddHeaderCell(g, 2, type);
            AddHeaderCell(g, 3, liveVal);
        }
        else if (tagEntry is not null)
        {
            var tbName = CreateEditableCell(tagEntry.Name, v => { tagEntry.Name = v; NotifyProgramChanged(); });
            var tbAddr = CreateEditableCell(tagEntry.Address, v => { tagEntry.Address = PlcProgramModel.NormalizeAddress(v); NotifyProgramChanged(); }, mono: true);
            var tbType = CreateEditableCell(tagEntry.DataType, v => { tagEntry.DataType = v; NotifyProgramChanged(); });
            var tbLive = new TextBlock
            {
                Text = liveVal,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = CreateBrush(liveVal.StartsWith("TRUE") ? "#22C55E" : "#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };

            Grid.SetColumn(tbName, 0);
            Grid.SetColumn(tbAddr, 1);
            Grid.SetColumn(tbType, 2);
            Grid.SetColumn(tbLive, 3);
            g.Children.Add(tbName);
            g.Children.Add(tbAddr);
            g.Children.Add(tbType);
            g.Children.Add(tbLive);

            border.MouseLeftButtonDown += (_, _) => { _selectedTag = tagEntry; RenderTagTable(); };
        }

        border.Child = g;
        return border;
    }

    private void AddHeaderCell(Grid g, int col, string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private TextBox CreateEditableCell(string initial, Action<string> onChanged, bool mono = false)
    {
        var tb = new TextBox
        {
            Text = initial,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 11,
            FontFamily = mono ? new FontFamily("Consolas") : new FontFamily("Segoe UI")
        };
        tb.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        tb.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        tb.SetResourceReference(TextBox.BorderBrushProperty, "BorderSubtleBrush");
        tb.LostFocus += (_, _) => onChanged(tb.Text.Trim());
        return tb;
    }

    private void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        int n = _plc.DraftProgram.Tags.Count + 1;
        var entry = new PlcTagEntry { Name = $"TAG_{n}", Address = $"%M{n}.0", DataType = "Bool" };
        _plc.DraftProgram.Tags.Add(entry);
        _selectedTag = entry;
        NotifyProgramChanged();
        RenderTagTable();
    }

    private void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedTag is null) return;
        _plc.DraftProgram.Tags.Remove(_selectedTag);
        _selectedTag = null;
        NotifyProgramChanged();
        RenderTagTable();
    }

    // =========================================================================
    // 5. COMPILE & DOWNLOAD (HIỆU ỨNG NẠP 1.5s + NHẤP NHÁY LED PLC)
    // =========================================================================

    public PlcCompileResult CompileCurrentProgram()
    {
        var empty = new PlcCompileResult();
        if (_plc is null) return empty;

        var res = PlcProgramCompiler.Compile(_plc.DraftProgram);
        _plc.HasCompileError = !res.Success;
        if (!res.Success)
            _plc.CpuState = PlcCpuState.Error;

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
        if (_plc is null || diag.NetworkNumber <= 0 || diag.NetworkNumber > _plc.DraftProgram.Networks.Count)
            return;

        RbTabLadder.IsChecked = true;
        _selectedNetworkIndex = diag.NetworkNumber - 1;
        var net = _plc.DraftProgram.Networks[_selectedNetworkIndex];
        net.IsCollapsed = false;
        _selectedBranch = net.MainBranch;
        _selectedNode = null;

        if (!string.IsNullOrEmpty(diag.NodeId) && TryFindNodeById(net.MainBranch, diag.NodeId, out var foundBranch, out var foundNode))
        {
            _selectedBranch = foundBranch;
            _selectedNode = foundNode;
        }

        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
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

        NotifyProgramChanged();
        UpdateCpuStateBadges();
        PlcHardwareVisualChanged?.Invoke();
    }

    // =========================================================================
    // 6. NETWORK, PARALLEL BRANCH, CPU, ZOOM & THU GỌN
    // =========================================================================

    private void OnAddNetworkClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null) return;
        RbTabLadder.IsChecked = true;
        int n = _plc.DraftProgram.Networks.Count + 1;
        var net = new LadderNetwork { Title = $"Network {n}" };
        _plc.DraftProgram.Networks.Add(net);
        _selectedNetworkIndex = _plc.DraftProgram.Networks.Count - 1;
        _selectedBranch = net.MainBranch;
        _selectedNode = null;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void OnOpenParallelBranchClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _plc.DraftProgram.Networks.Count == 0) return;
        RbTabLadder.IsChecked = true;

        var net = _plc.DraftProgram.Networks[Math.Clamp(_selectedNetworkIndex, 0, _plc.DraftProgram.Networks.Count - 1)];
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
        if (_plc is null || _plc.DraftProgram.Networks.Count == 0) return;
        var net = _plc.DraftProgram.Networks[Math.Clamp(_selectedNetworkIndex, 0, _plc.DraftProgram.Networks.Count - 1)];
        _selectedBranch = net.MainBranch;
        _selectedNode = net.MainBranch.Nodes.LastOrDefault();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void OnDeleteSelectedNodeClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedNode is null || _selectedBranch is null) return;
        _selectedBranch.Nodes.Remove(_selectedNode);
        _selectedNode = _selectedBranch.Nodes.LastOrDefault();
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

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

    private void NotifyProgramChanged() => ProgramEdited?.Invoke();

    private static SolidColorBrush CreateBrush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
