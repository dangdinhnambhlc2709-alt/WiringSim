using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Dialogs;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

/// <summary>
/// Trình soạn thảo Ladder PLC chuẩn IEC 61131-3:
/// - Phím tắt đầy đủ: Ctrl+Z/Y (60 bước), Ctrl+C/X/V/Delete, mũi tên điều hướng, Enter/F2 Inline Edit + Tab nhảy chân, Esc,
///   Shift+F2 (Insert Network), Shift+F7 (Coil), Shift+F8/F9 (Open/Close branch), F8 (Empty Box + gợi ý lệnh), Alt+Shift+D/N.
/// - Thanh Favorites kéo thả tùy chỉnh + 3 chế độ hiển thị địa chỉ + Menu chuột phải thông minh + Watch Table.
/// </summary>
public partial class PlcLadderEditorControl : UserControl
{
    private const int MaxUndoSteps = 60;
    private const string FavoriteDragFormat = "WiringSim.PlcInstructionKind";

    private PlcControllerComponent? _plc;
    private bool _isInstructionsVisible = true;
    private bool _isSimulating;
    private bool _updatingZoomUi;
    private bool _updatingPropDataType;
    private bool _updatingMemConfigUi;

    private int _selectedNetworkIndex = 0;
    private LadderBranch? _selectedBranch;
    private LadderNode? _selectedNode;
    private PlcTagEntry? _selectedTag;
    private PlcWatchEntry? _selectedWatch;
    private PlcAddressDisplayMode _displayMode = PlcAddressDisplayMode.SymbolicAndAbsolute;

    // Undo / Redo Stack (tối thiểu 50 bước — hỗ trợ 60 bước)
    private readonly LinkedList<PlcProgramModel> _undoStack = new();
    private readonly Stack<PlcProgramModel> _redoStack = new();

    // Clipboard nội bộ (hỗ trợ cả phần tử Ladder lẫn nguyên Network)
    private LadderNode? _clipboardNode;
    private LadderNetwork? _clipboardNetwork;

    // Trạng thái Inline Edit (Enter / F2 + Tab nhảy qua các chân)
    private LadderNode? _inlineEditNode;
    private string? _inlineEditField; // "Address", "Param1", "Param2", "ExtraInput:0", "ExtraOutput:0", "ExtraParam", "OutputAddress"
    private readonly Dictionary<string, FrameworkElement> _renderedNodeVisuals = new();

    // Danh sách callback cập nhật Online Monitoring tại chỗ (không phá Visual Tree khi đang chạy 10Hz)
    private readonly List<Action<bool>> _liveOnlineUpdaters = new();
    private static readonly DoubleCollection DashedFalseWire = CreateFrozenDashArray();

    private static DoubleCollection CreateFrozenDashArray()
    {
        var dc = new DoubleCollection { 3.0, 2.0 };
        dc.Freeze();
        return dc;
    }

    // Trạng thái Drag-to-Favorites từ cây thư viện lệnh
    private Point _instrDragStart;
    private LadderElementKind? _instrDragKind;

    // Trạng thái Address Picker Popup (bảng PLC Tags)
    private PlcTagEntry? _addrPickerTag;
    private bool _updatingAddrPickerUi;

    // Trạng thái Autocomplete Popup 3 cột (Ladder, Property Bar & Watch Table)
    private LadderNode? _autoTargetNode;
    private PlcWatchEntry? _autoTargetWatch;
    private string _autoTargetField = "Address";
    private PlcTagEntry? _autoExpandedTag;
    private bool _autoExpandedIsSlice;
    private bool _updatingAutoSearch;
    private readonly List<Action> _autoRowActions = new();

    // Trạng thái Empty Box [F8] Command Autocomplete Popup
    private LadderNode? _emptyBoxTargetNode;
    private List<PlcInstructionMeta> _emptyBoxMatches = new();
    private int _emptyBoxSelectedIndex;

    // Hỗ trợ tự focus ô Name của dòng mới tạo khi nhấn Enter trong bảng PLC Tags
    private PlcTagEntry? _pendingFocusTag;

    // Trạng thái Watch Table live updates (cập nhật tại chỗ, không phá focus TextBox)
    private readonly Dictionary<string, TextBlock> _watchLiveTextBlocks = new();
    private readonly Dictionary<string, TextBlock> _watchTypeTextBlocks = new();
    private readonly Dictionary<string, TextBlock> _watchAddrTextBlocks = new();

    public event Action? HideRequested;
    public event Action? ProgramEdited;
    public event Action? PlcHardwareVisualChanged;
    public event Action<string>? StatusMessageChanged;

    public PlcControllerComponent? ActivePlc => _plc;

    public bool ShowEmbeddedHeader
    {
        get => EmbeddedHeaderBar.Visibility == Visibility.Visible;
        set => EmbeddedHeaderBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public PlcLadderEditorControl()
    {
        InitializeComponent();
        BuildInstructionsTree(string.Empty);
        UpdateDisplayModeButtonText();

        LocalizationManager.LanguageChanged += () =>
        {
            BuildInstructionsTree(TbInstructionSearch?.Text ?? string.Empty);
            UpdateDisplayModeButtonText();
            RenderFavoritesBar();
            RefreshAllViews();
        };
        ThemeManager.ThemeChanged += RefreshAllViews;
    }

    public void BindPlc(PlcControllerComponent? plc, bool openTagTable = false)
    {
        _plc = plc;
        _activeBlock = null;
        _selectedNetworkIndex = 0;
        _selectedNode = null;
        _selectedBranch = plc?.DraftProgram.Networks.FirstOrDefault()?.MainBranch;
        _selectedTag = null;
        _selectedWatch = null;
        _selectedForceEntry = null;
        _lastRenderedDiagCount = -1;
        CancelInlineEdit(rerender: false);
        CloseAllPopups();
        ClearUndoRedo();

        if (openTagTable)
            RbTabTags.IsChecked = true;
        else
            RbTabLadder.IsChecked = true;

        DiagnosticsStack.Children.Clear();
        DiagnosticsScroller.Visibility = Visibility.Collapsed;
        TxtCompileSummary.Text = string.Empty;

        RebuildBlockSelectorCombo();
        RenderFavoritesBar();
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
        {
            // Cập nhật màu dây (xanh liền = TRUE, lam nét đứt = FALSE) & giá trị live tại chỗ, không dựng lại Visual Tree
            if (_inlineEditNode is null && !AnyPopupOpen())
                UpdateOnlineOverlayInPlace();
        }
        else if (RbTabTags.IsChecked == true)
        {
            if (!IsKeyboardFocusWithinTagInputs())
                RenderTagTable();
        }
        else if (RbTabWatch.IsChecked == true)
        {
            RefreshWatchLiveValuesInPlace();
        }
        else if (RbTabForce.IsChecked == true)
        {
            RefreshForceLiveValuesInPlace();
        }
        else if (RbTabDiag.IsChecked == true)
        {
            RenderDiagnosticsBuffer();
        }
    }

    private void UpdateOnlineOverlayInPlace()
    {
        if (_plc is null) return;
        bool isOnline = _isSimulating && _plc.IsPowered && _plc.CpuState == PlcCpuState.Run && !_plc.IsDownloading;
        foreach (var updater in _liveOnlineUpdaters)
            updater(isOnline);
    }

    private bool AnyPopupOpen() =>
        TagAutocompletePopup.IsOpen || AddressPickerPopup.IsOpen || EmptyBoxPopup.IsOpen || PlcMemConfigPopup.IsOpen;

    private void CloseAllPopups()
    {
        AddressPickerPopup.IsOpen = false;
        TagAutocompletePopup.IsOpen = false;
        EmptyBoxPopup.IsOpen = false;
        PlcMemConfigPopup.IsOpen = false;
    }

    private bool IsKeyboardFocusWithinTagInputs() =>
        TagTableContainer.Visibility == Visibility.Visible &&
        (TagTableContainer.IsKeyboardFocusWithin || AddressPickerPopup.IsOpen);

    private void RefreshAllViews()
    {
        bool hasPlc = _plc is not null;
        TxtNoPlcPlaceholder.Visibility = hasPlc ? Visibility.Collapsed : Visibility.Visible;
        LadderWorkspaceGrid.Visibility = hasPlc && RbTabLadder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TagTableContainer.Visibility = hasPlc && RbTabTags.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WatchTableContainer.Visibility = hasPlc && RbTabWatch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ForceTableContainer.Visibility = hasPlc && RbTabForce.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsContainer.Visibility = hasPlc && RbTabDiag.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PropertyEditBar.Visibility = hasPlc && RbTabLadder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FavoritesToolbarBorder.Visibility = hasPlc && RbTabLadder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TxtPlcHeader.Text = hasPlc
            ? $"{_plc!.DisplayName} — {LocalizationManager.Get("Str.Plc.TabLadder")}"
            : LocalizationManager.Get("Str.Plc.EditorTitle");

        UpdateCpuStateBadges();
        UpdateUndoRedoButtons();
        if (!hasPlc) return;

        if (RbTabLadder.IsChecked == true)
        {
            RebuildBlockSelectorCombo();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
        else if (RbTabTags.IsChecked == true)
        {
            RenderTagTable();
        }
        else if (RbTabWatch.IsChecked == true)
        {
            RenderWatchTable();
        }
        else if (RbTabForce.IsChecked == true)
        {
            RenderForceTable();
        }
        else if (RbTabDiag.IsChecked == true)
        {
            RenderDiagnosticsBuffer(forceRebuild: true);
        }
    }

    private void UpdateCpuStateBadges()
    {
        if (_plc is null)
        {
            CpuStatePill.Visibility = Visibility.Collapsed;
            OnlinePill.Visibility = Visibility.Collapsed;
            ForcePill.Visibility = Visibility.Collapsed;
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

        ForcePill.Visibility = _plc.Runtime.HasActiveForces ? Visibility.Visible : Visibility.Collapsed;

        bool isOnline = _isSimulating && _plc.IsPowered && _plc.CpuState == PlcCpuState.Run && !_plc.IsDownloading;
        OnlinePill.Visibility = isOnline ? Visibility.Visible : Visibility.Collapsed;
        if (isOnline)
        {
            if (_plc.HasPendingDownload)
            {
                OnlinePill.Background = CreateBrush("#B45309");
                OnlinePill.BorderBrush = CreateBrush("#FBBF24");
                TxtOnlinePill.Text = LocalizationManager.Get("Str.Plc.OnlineOutOfSync");
                TxtOnlinePill.Foreground = CreateBrush("#FEF3C7");
                OnlinePill.ToolTip = LocalizationManager.Get("Str.Plc.OnlineOutOfSyncTip");
            }
            else
            {
                OnlinePill.Background = CreateBrush("#15803D");
                OnlinePill.BorderBrush = CreateBrush("#4ADE80");
                TxtOnlinePill.Text = "● ONLINE";
                TxtOnlinePill.Foreground = CreateBrush("#DCFCE7");
                OnlinePill.ToolTip = null;
            }
        }
    }

    // =========================================================================
    // 0. UNDO / REDO (60 BƯỚC) & CLIPBOARD (CUT / COPY / PASTE)
    // =========================================================================

    private void PushUndoSnapshot()
    {
        if (_plc is null) return;
        _undoStack.AddLast(_plc.DraftProgram.Clone());
        while (_undoStack.Count > MaxUndoSteps)
            _undoStack.RemoveFirst();
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void ClearUndoRedo()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        if (BtnUndo is not null) BtnUndo.IsEnabled = _undoStack.Count > 0;
        if (BtnRedo is not null) BtnRedo.IsEnabled = _redoStack.Count > 0;
    }

    private void PerformUndo()
    {
        if (_plc is null || _undoStack.Count == 0) return;
        CancelInlineEdit(rerender: false);
        _redoStack.Push(_plc.DraftProgram.Clone());
        _plc.DraftProgram = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        ResyncSelectionAfterModelReplace();
        NotifyProgramChanged();
        RenderFavoritesBar();
        RefreshAllViews();
        StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.Undone"));
    }

    private void PerformRedo()
    {
        if (_plc is null || _redoStack.Count == 0) return;
        CancelInlineEdit(rerender: false);
        _undoStack.AddLast(_plc.DraftProgram.Clone());
        _plc.DraftProgram = _redoStack.Pop();
        ResyncSelectionAfterModelReplace();
        NotifyProgramChanged();
        RenderFavoritesBar();
        RefreshAllViews();
        StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.Redone"));
    }

    private void ResyncSelectionAfterModelReplace()
    {
        if (_plc is null) return;
        RebuildBlockSelectorCombo();
        var nets = GetActiveNetworks();
        if (nets.Count == 0)
        {
            _selectedNetworkIndex = 0;
            _selectedBranch = null;
            _selectedNode = null;
            return;
        }
        _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1);
        _selectedBranch = nets[_selectedNetworkIndex].MainBranch;
        _selectedNode = _selectedBranch.Nodes.FirstOrDefault();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => PerformUndo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => PerformRedo();

    private void CopySelection()
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (_selectedNode is not null)
        {
            _clipboardNode = _selectedNode.Clone(regenerateIds: true);
            _clipboardNetwork = null;
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.StatusCopiedNode"));
        }
        else if (_selectedNetworkIndex >= 0 && _selectedNetworkIndex < nets.Count)
        {
            _clipboardNetwork = nets[_selectedNetworkIndex].Clone(regenerateIds: true);
            _clipboardNode = null;
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.StatusCopiedNet"));
        }
    }

    private void CutSelection()
    {
        if (_plc is null) return;
        CopySelection();
        DeleteSelection();
    }

    private void PasteSelection()
    {
        if (_plc is null) return;
        if (_clipboardNode is not null)
        {
            PushUndoSnapshot();
            var nets = GetActiveNetworks();
            if (nets.Count == 0)
            {
                nets.Add(new LadderNetwork { Title = "Network 1" });
                _selectedNetworkIndex = 0;
            }
            _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1);
            var branch = _selectedBranch ?? nets[_selectedNetworkIndex].MainBranch;
            var pasted = _clipboardNode.Clone(regenerateIds: true);

            int idx = _selectedNode is not null ? branch.Nodes.IndexOf(_selectedNode) + 1 : branch.Nodes.Count;
            if (idx <= 0 || idx > branch.Nodes.Count) idx = branch.Nodes.Count;
            branch.Nodes.Insert(idx, pasted);
            _selectedBranch = branch;
            _selectedNode = pasted;

            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.StatusPasted"));
        }
        else if (_clipboardNetwork is not null)
        {
            PushUndoSnapshot();
            var nets = GetActiveNetworks();
            var pastedNet = _clipboardNetwork.Clone(regenerateIds: true);
            int insertIdx = Math.Clamp(_selectedNetworkIndex + 1, 0, nets.Count);
            nets.Insert(insertIdx, pastedNet);
            _selectedNetworkIndex = insertIdx;
            _selectedBranch = pastedNet.MainBranch;
            _selectedNode = null;
            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Plc.StatusPasted"));
        }
    }

    private void DeleteSelection()
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (_selectedNode is not null && _selectedBranch is not null)
        {
            PushUndoSnapshot();
            int idx = _selectedBranch.Nodes.IndexOf(_selectedNode);
            _selectedBranch.Nodes.Remove(_selectedNode);

            // Nếu xóa phần tử trong nhánh song song khiến nhánh con trống, dọn nhánh nếu còn < 2 nhánh
            if (nets.Count > 0)
            {
                var net = nets[Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1)];
                CleanupCollapsedParallelBlocks(net.MainBranch);
            }

            _selectedNode = idx >= 0 && _selectedBranch.Nodes.Count > 0
                ? _selectedBranch.Nodes[Math.Min(idx, _selectedBranch.Nodes.Count - 1)]
                : null;
            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
        else if (_selectedNetworkIndex >= 0 && _selectedNetworkIndex < nets.Count)
        {
            PushUndoSnapshot();
            nets.RemoveAt(_selectedNetworkIndex);
            _selectedNetworkIndex = Math.Max(0, Math.Min(_selectedNetworkIndex, nets.Count - 1));
            _selectedBranch = nets.ElementAtOrDefault(_selectedNetworkIndex)?.MainBranch;
            _selectedNode = null;
            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
    }

    private static void CleanupCollapsedParallelBlocks(LadderBranch branch)
    {
        for (int i = branch.Nodes.Count - 1; i >= 0; i--)
        {
            var n = branch.Nodes[i];
            if (n.Kind != LadderElementKind.ParallelBlock || n.Branches is null) continue;
            foreach (var sub in n.Branches) CleanupCollapsedParallelBlocks(sub);
            if (n.Branches.All(b => b.Nodes.Count == 0))
            {
                branch.Nodes.RemoveAt(i);
            }
        }
    }

    // =========================================================================
    // 1. FAVORITES TOOLBAR (KÉO THẢ GHIM / BỎ GHIM) & 3 CHẾ ĐỘ HIỂN THỊ ĐỊA CHỈ
    // =========================================================================

    private List<LadderElementKind> GetActiveFavorites()
    {
        if (_plc is null)
            return new List<LadderElementKind>(PlcInstructionCatalog.DefaultFavoriteInstructions);

        if (_plc.DraftProgram.FavoriteInstructions is null || _plc.DraftProgram.FavoriteInstructions.Count == 0)
            _plc.DraftProgram.FavoriteInstructions = new List<LadderElementKind>(PlcInstructionCatalog.DefaultFavoriteInstructions);

        return _plc.DraftProgram.FavoriteInstructions;
    }

    private void RenderFavoritesBar()
    {
        if (FavoritesItemsPanel is null) return;
        FavoritesItemsPanel.Children.Clear();

        var favs = GetActiveFavorites();
        foreach (var kind in favs)
        {
            var meta = PlcInstructionCatalog.Get(kind);
            if (meta is null) continue;

            var capturedKind = kind;
            var btn = new Button
            {
                Content = meta.Symbol,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.6,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(meta.AccentHex),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = $"{meta.Symbol} — {PlcInstructionCatalog.GetDescription(meta)}\n({LocalizationManager.Get("Str.Plc.FavRightClickUnpin")})"
            };
            btn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
            btn.Click += (_, _) => InsertInstruction(capturedKind);

            var cm = new ContextMenu();
            var unpinItem = new MenuItem { Header = LocalizationManager.Format("Str.Plc.FavUnpin", meta.Symbol) };
            unpinItem.Click += (_, _) =>
            {
                var list = GetActiveFavorites();
                list.Remove(capturedKind);
                NotifyProgramChanged();
                RenderFavoritesBar();
            };
            var resetItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.FavResetDefault") };
            resetItem.Click += (_, _) =>
            {
                if (_plc is not null)
                    _plc.DraftProgram.FavoriteInstructions = new List<LadderElementKind>(PlcInstructionCatalog.DefaultFavoriteInstructions);
                NotifyProgramChanged();
                RenderFavoritesBar();
            };
            cm.Items.Add(unpinItem);
            cm.Items.Add(new Separator());
            cm.Items.Add(resetItem);
            btn.ContextMenu = cm;

            FavoritesItemsPanel.Children.Add(btn);
        }
    }

    private void OnFavoritesDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FavoriteDragFormat))
        {
            e.Effects = DragDropEffects.Copy;
            FavoritesToolbarBorder.BorderBrush = CreateBrush("#38BDF8");
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnFavoritesDrop(object sender, DragEventArgs e)
    {
        FavoritesToolbarBorder.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        if (e.Data.GetDataPresent(FavoriteDragFormat) && e.Data.GetData(FavoriteDragFormat) is LadderElementKind kind)
        {
            var favs = GetActiveFavorites();
            if (!favs.Contains(kind))
            {
                favs.Add(kind);
                NotifyProgramChanged();
                RenderFavoritesBar();
                var meta = PlcInstructionCatalog.Get(kind);
                StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.FavPinnedMsg", meta?.Symbol ?? kind.ToString()));
            }
        }
        e.Handled = true;
    }

    private void OnCycleDisplayModeClick(object sender, RoutedEventArgs e)
    {
        _displayMode = _displayMode switch
        {
            PlcAddressDisplayMode.SymbolicAndAbsolute => PlcAddressDisplayMode.SymbolicOnly,
            PlcAddressDisplayMode.SymbolicOnly => PlcAddressDisplayMode.AbsoluteOnly,
            _ => PlcAddressDisplayMode.SymbolicAndAbsolute
        };
        UpdateDisplayModeButtonText();
        RenderNetworks();
    }

    private void UpdateDisplayModeButtonText()
    {
        if (BtnDisplayMode is null) return;
        BtnDisplayMode.Content = _displayMode switch
        {
            PlcAddressDisplayMode.SymbolicOnly => LocalizationManager.Get("Str.Plc.DispModeSym"),
            PlcAddressDisplayMode.AbsoluteOnly => LocalizationManager.Get("Str.Plc.DispModeAbs"),
            _ => LocalizationManager.Get("Str.Plc.DispModeBoth")
        };
    }

    // =========================================================================
    // 2. CÂY THƯ VIỆN 10 NHÓM LỆNH PLC (CLICK CHÈN + KÉO THẢ VÀO FAVORITES)
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
            ToolTip = $"{meta.Symbol} — {desc}\n({LocalizationManager.Get("Str.Plc.DragToFavTip")})"
        };
        btn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btn.Click += (_, _) => InsertInstruction(meta.Kind);

        btn.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _instrDragStart = e.GetPosition(null);
            _instrDragKind = meta.Kind;
        };
        btn.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _instrDragKind == meta.Kind)
            {
                Vector diff = _instrDragStart - e.GetPosition(null);
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _instrDragKind = null;
                    var data = new DataObject(FavoriteDragFormat, meta.Kind);
                    DragDrop.DoDragDrop(btn, data, DragDropEffects.Copy);
                }
            }
        };

        return btn;
    }

    private void InsertInstruction(LadderElementKind kind)
    {
        if (_plc is null) return;
        if (RbTabLadder.IsChecked != true) RbTabLadder.IsChecked = true;
        CancelInlineEdit(rerender: false);

        PushUndoSnapshot();
        var prog = _plc.DraftProgram;
        var nets = GetActiveNetworks();
        if (nets.Count == 0)
        {
            nets.Add(new LadderNetwork { Title = "Network 1" });
            _selectedNetworkIndex = 0;
        }
        _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1);
        var targetNet = nets[_selectedNetworkIndex];
        var branch = _selectedBranch ?? targetNet.MainBranch;

        var node = PlcInstructionCatalog.CreateDefaultNode(kind, prog, allocateDbTag: false);

        if (node.RequiresCallOptions)
            PromptCallOptionsForNode(node, isNewInsert: true);

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
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_renderedNodeVisuals.TryGetValue(node.Id, out var vis))
                    OpenEmptyBoxPopup(vis, node);
                else
                    OpenEmptyBoxPopup(BtnEmptyBox, node);
            });
        }
    }

    private bool PromptCallOptionsForNode(LadderNode node, bool isNewInsert, bool pushUndoOnSuccess = false)
    {
        if (_plc is null) return false;
        var prog = _plc.DraftProgram;
        bool isTrig = node.Kind is LadderElementKind.RTrig or LadderElementKind.FTrig;
        bool isTimer = node.IsTimer || node.IsTimerCoil;
        var meta = PlcInstructionCatalog.Get(node.Kind);
        string instrName = meta?.BoxHeader ?? node.Kind.ToString();

        int autoNum = prog.AllocateNextDbNumber();
        string suggestedName = !isNewInsert && !string.IsNullOrWhiteSpace(node.SymbolName)
            ? PlcAddressHelper.StripQuotes(node.SymbolName)
            : node.Kind switch
            {
                LadderElementKind.CtrlPwm => $"CTRL_PWM_DB_{autoNum}",
                LadderElementKind.CtrlHsc => $"CTRL_HSC_DB_{autoNum}",
                LadderElementKind.PidCompact => $"PID_Compact_{autoNum}",
                LadderElementKind.ModbusCommLoad => $"Modbus_Comm_Load_DB_{autoNum}",
                LadderElementKind.ModbusMaster => $"Modbus_Master_DB_{autoNum}",
                LadderElementKind.ModbusTcpClient => $"MB_CLIENT_DB_{autoNum}",
                _ => prog.SuggestNextDbName(isTimer, isTrig)
            };

        var dlg = new PlcCallOptionsDialog(instrName, suggestedName, autoNum);
        var owner = Window.GetWindow(this);
        if (owner is { IsVisible: true })
            dlg.Owner = owner;

        if (dlg.ShowDialog() == true)
        {
            if (pushUndoOnSuccess)
                PushUndoSnapshot();

            string oldName = PlcAddressHelper.StripQuotes(node.SymbolName);
            string dbAddr = $"%DB{dlg.SelectedDbNumber}";
            string dbName = dlg.SelectedDbName;
            node.Address = dbAddr;
            node.SymbolName = dbName;
            string dbType = node.Kind switch
            {
                LadderElementKind.CtrlPwm => "IEC_PWM",
                LadderElementKind.CtrlHsc => "IEC_HSC",
                LadderElementKind.PidCompact => "PID_Compact",
                LadderElementKind.ModbusCommLoad => "Modbus_Comm_Load",
                LadderElementKind.ModbusMaster => "Modbus_Master",
                LadderElementKind.ModbusTcpClient => "MB_CLIENT",
                _ => isTrig ? "IEC_Trig" : (isTimer ? "IEC_Timer" : "IEC_Counter")
            };
            prog.EnsureTagExists(dbAddr, dbName, dbType, overwriteDataType: true);
            if (!isNewInsert && !string.IsNullOrWhiteSpace(oldName) && !oldName.Equals(dbName, StringComparison.OrdinalIgnoreCase))
                prog.RenameTagEverywhere(oldName, dbName);
            return true;
        }

        if (isNewInsert)
        {
            node.Address = "<???>";
            node.SymbolName = string.Empty;
        }
        return false;
    }

    // =========================================================================
    // 3. HỆ THỐNG PHÍM TẮT & ĐIỀU HƯỚNG BÀN PHÍM THÔNG MINH
    // =========================================================================

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_plc is null) return;

        // Esc luôn đóng popup hoặc hủy Inline Edit
        if (e.Key == Key.Escape)
        {
            if (AnyPopupOpen())
            {
                CloseAllPopups();
                e.Handled = true;
                return;
            }
            if (_inlineEditNode is not null)
            {
                CancelInlineEdit(rerender: true);
                Focus();
                e.Handled = true;
                return;
            }
        }

        if (AnyPopupOpen()) return;

        bool focusInTextBox = Keyboard.FocusedElement is TextBox;

        // Undo / Redo (hoạt động cả trên Ladder & PLC Tags khi không gõ trong TextBox)
        if (!focusInTextBox && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z) { PerformUndo(); e.Handled = true; return; }
            if (e.Key == Key.Y) { PerformRedo(); e.Handled = true; return; }
        }

        if (RbTabLadder.IsChecked != true) return;
        if (focusInTextBox) return;

        // Cut / Copy / Paste / Delete
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.C) { CopySelection(); e.Handled = true; return; }
            if (e.Key == Key.X) { CutSelection(); e.Handled = true; return; }
            if (e.Key == Key.V) { PasteSelection(); e.Handled = true; return; }
        }

        // Alt + Shift + D (Define tag) | Alt + Shift + N (Rename tag)
        Key realKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
        {
            if (realKey == Key.D && _selectedNode is not null)
            {
                OpenDefineTagForNode(_selectedNode);
                e.Handled = true;
                return;
            }
            if (realKey == Key.N && _selectedNode is not null)
            {
                OpenRenameTagForNode(_selectedNode);
                e.Handled = true;
                return;
            }
        }

        // Phím tắt chèn nhanh phần tử
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.F8:
                    InsertInstruction(LadderElementKind.EmptyBox);
                    e.Handled = true;
                    return;
                case Key.Delete:
                    DeleteSelection();
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.F2:
                    if (_selectedNode is not null)
                    {
                        if (_selectedNode.Kind == LadderElementKind.EmptyBox)
                        {
                            var anchor = _renderedNodeVisuals.GetValueOrDefault(_selectedNode.Id) ?? (UIElement)BtnEmptyBox;
                            OpenEmptyBoxPopup(anchor, _selectedNode);
                        }
                        else if (_selectedNode.Kind != LadderElementKind.ParallelBlock)
                        {
                            StartInlineEdit(_selectedNode, GetOrderedEditableFields(_selectedNode).FirstOrDefault());
                        }
                        e.Handled = true;
                    }
                    return;
                case Key.Left:
                    NavigateHorizontal(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    NavigateHorizontal(+1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    NavigateVertical(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    NavigateVertical(+1);
                    e.Handled = true;
                    return;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            switch (e.Key)
            {
                case Key.F2:
                    InsertNetworkBelowCurrent();
                    e.Handled = true;
                    return;
                case Key.F7:
                    InsertInstruction(LadderElementKind.CoilOut);
                    e.Handled = true;
                    return;
                case Key.F8:
                    OnOpenParallelBranchClick(sender, e);
                    e.Handled = true;
                    return;
                case Key.F9:
                    OnCloseParallelBranchClick(sender, e);
                    e.Handled = true;
                    return;
            }
        }
    }

    private void NavigateHorizontal(int dir)
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (nets.Count == 0) return;
        _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1);
        var net = nets[_selectedNetworkIndex];
        var branch = _selectedBranch ?? net.MainBranch;

        if (branch.Nodes.Count == 0)
        {
            // Nếu nhánh rỗng nằm trong ParallelBlock -> thoát ra nhánh cha
            if (TryFindParentParallel(net.MainBranch, branch, out var parentBranch, out var pBlock, out _))
            {
                int pIdx = parentBranch.Nodes.IndexOf(pBlock);
                int nextIdx = pIdx + dir;
                if (nextIdx >= 0 && nextIdx < parentBranch.Nodes.Count)
                {
                    _selectedBranch = parentBranch;
                    _selectedNode = parentBranch.Nodes[nextIdx];
                    SelectAndScrollNode();
                }
            }
            return;
        }

        if (_selectedNode is null)
        {
            _selectedBranch = branch;
            _selectedNode = dir > 0 ? branch.Nodes[0] : branch.Nodes[^1];
            EnterParallelIfNeeded(dir);
            SelectAndScrollNode();
            return;
        }

        int curIdx = branch.Nodes.IndexOf(_selectedNode);
        if (curIdx < 0)
        {
            _selectedBranch = net.MainBranch;
            _selectedNode = net.MainBranch.Nodes.FirstOrDefault();
            SelectAndScrollNode();
            return;
        }

        int targetIdx = curIdx + dir;
        if (targetIdx >= 0 && targetIdx < branch.Nodes.Count)
        {
            _selectedNode = branch.Nodes[targetIdx];
            EnterParallelIfNeeded(dir);
            SelectAndScrollNode();
        }
        else if (TryFindParentParallel(net.MainBranch, branch, out var parentBranch, out var pBlock, out _))
        {
            // Thoát ra nhánh cha tiếp theo bên trái / phải khối song song
            int pIdx = parentBranch.Nodes.IndexOf(pBlock);
            int nextIdx = pIdx + dir;
            if (nextIdx >= 0 && nextIdx < parentBranch.Nodes.Count)
            {
                _selectedBranch = parentBranch;
                _selectedNode = parentBranch.Nodes[nextIdx];
                EnterParallelIfNeeded(dir);
                SelectAndScrollNode();
            }
        }
    }

    private void EnterParallelIfNeeded(int dir)
    {
        if (_selectedNode is { Kind: LadderElementKind.ParallelBlock, Branches.Count: > 0 })
        {
            var firstSub = _selectedNode.Branches[0];
            _selectedBranch = firstSub;
            _selectedNode = firstSub.Nodes.Count > 0
                ? (dir >= 0 ? firstSub.Nodes[0] : firstSub.Nodes[^1])
                : null;
        }
    }

    private void NavigateVertical(int dir)
    {
        if (_plc is null) return;
        var nets = GetActiveNetworks();
        if (nets.Count == 0) return;
        _selectedNetworkIndex = Math.Clamp(_selectedNetworkIndex, 0, nets.Count - 1);
        var net = nets[_selectedNetworkIndex];
        var branch = _selectedBranch ?? net.MainBranch;

        // 1. Nếu đang ở trong một nhánh song song -> di chuyển giữa các nhánh song song trước
        if (TryFindParentParallel(net.MainBranch, branch, out _, out var pBlock, out int subIdx) && pBlock.Branches is not null)
        {
            int nextSub = subIdx + dir;
            if (nextSub >= 0 && nextSub < pBlock.Branches.Count)
            {
                _selectedBranch = pBlock.Branches[nextSub];
                _selectedNode = _selectedBranch.Nodes.FirstOrDefault();
                SelectAndScrollNode();
                return;
            }
        }

        // 2. Chuyển sang Network trên / dưới
        int nextNetIdx = _selectedNetworkIndex + dir;
        if (nextNetIdx >= 0 && nextNetIdx < nets.Count)
        {
            _selectedNetworkIndex = nextNetIdx;
            var nextNet = nets[nextNetIdx];
            nextNet.IsCollapsed = false;
            _selectedBranch = nextNet.MainBranch;
            _selectedNode = nextNet.MainBranch.Nodes.FirstOrDefault();
            EnterParallelIfNeeded(+1);
            SelectAndScrollNode();
        }
    }

    private static bool TryFindParentParallel(
        LadderBranch root,
        LadderBranch target,
        out LadderBranch parentBranch,
        out LadderNode parallelNode,
        out int subIndex)
    {
        foreach (var n in root.Nodes)
        {
            if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
            {
                for (int i = 0; i < n.Branches.Count; i++)
                {
                    if (n.Branches[i] == target)
                    {
                        parentBranch = root;
                        parallelNode = n;
                        subIndex = i;
                        return true;
                    }
                    if (TryFindParentParallel(n.Branches[i], target, out parentBranch, out parallelNode, out subIndex))
                        return true;
                }
            }
        }
        parentBranch = root;
        parallelNode = null!;
        subIndex = -1;
        return false;
    }

    private void SelectAndScrollNode()
    {
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
        if (_selectedNode is not null && _renderedNodeVisuals.TryGetValue(_selectedNode.Id, out var vis))
        {
            vis.BringIntoView();
        }
    }

    // =========================================================================
    // 4. INLINE EDIT (ENTER / F2 + TAB NHẢY CHÂN THAM SỐ)
    // =========================================================================

    private static List<string> GetOrderedEditableFields(LadderNode node)
    {
        var list = new List<string>();
        var meta = PlcInstructionCatalog.Get(node.Kind);
        if (meta is null) return list;

        if (meta.AddrLabel is not null && !node.IsTimer && !node.IsCounter && node.Kind is not (LadderElementKind.RTrig or LadderElementKind.FTrig))
            list.Add("Address");
        else if (meta.AddrLabel is not null && (node.IsTimer || node.IsCounter || node.Kind is LadderElementKind.RTrig or LadderElementKind.FTrig))
            list.Add("Address");

        if (meta.Param1Label is not null) list.Add("Param1");
        if (meta.Param2Label is not null) list.Add("Param2");

        if (PlcInstructionCatalog.SupportsMultiInput(node.Kind) && node.ExtraInputs is not null)
        {
            for (int i = 0; i < node.ExtraInputs.Count; i++)
                list.Add($"ExtraInput:{i}");
        }

        if (meta.ExtraLabel is not null) list.Add("ExtraParam");
        if (meta.OutLabel is not null) list.Add("OutputAddress");
        if (PlcInstructionCatalog.SupportsMultiOutput(node.Kind) && node.ExtraOutputs is not null)
        {
            for (int i = 0; i < node.ExtraOutputs.Count; i++)
                list.Add($"ExtraOutput:{i}");
        }
        return list;
    }

    private static string GetNodeFieldValue(LadderNode node, string field)
    {
        if (field.StartsWith("ExtraInput:", StringComparison.Ordinal) &&
            int.TryParse(field[11..], out int idx) &&
            node.ExtraInputs is not null && idx >= 0 && idx < node.ExtraInputs.Count)
            return node.ExtraInputs[idx];

        if (field.StartsWith("ExtraOutput:", StringComparison.Ordinal) &&
            int.TryParse(field[12..], out int outIdx) &&
            node.ExtraOutputs is not null && outIdx >= 0 && outIdx < node.ExtraOutputs.Count)
            return node.ExtraOutputs[outIdx];

        return field switch
        {
            "Address" => node.Address,
            "Param1" => node.Param1,
            "Param2" => node.Param2,
            "ExtraParam" => node.ExtraParam,
            "OutputAddress" => node.OutputAddress,
            _ => node.Address
        };
    }

    private void SetNodeFieldValue(LadderNode node, string field, string rawValue)
    {
        string trimmed = rawValue.Trim();
        if (field.StartsWith("ExtraInput:", StringComparison.Ordinal) &&
            int.TryParse(field[11..], out int idx) &&
            node.ExtraInputs is not null && idx >= 0 && idx < node.ExtraInputs.Count)
        {
            node.ExtraInputs[idx] = PlcProgramModel.NormalizeAddress(trimmed);
            return;
        }

        if (field.StartsWith("ExtraOutput:", StringComparison.Ordinal) &&
            int.TryParse(field[12..], out int outIdx) &&
            node.ExtraOutputs is not null && outIdx >= 0 && outIdx < node.ExtraOutputs.Count)
        {
            node.ExtraOutputs[outIdx] = PlcProgramModel.NormalizeAddress(trimmed);
            return;
        }

        switch (field)
        {
            case "Address":
                if (node.Kind is LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.Label or LadderElementKind.JumpList or LadderElementKind.Switch or LadderElementKind.CallBlock)
                    node.Address = trimmed;
                else
                {
                    node.Address = PlcProgramModel.NormalizeAddress(trimmed);
                    // Nếu người dùng gõ tên Tag mới chưa khai báo -> giữ SymbolName sạch
                    if (!PlcAddressHelper.IsAbsoluteAddress(trimmed))
                        node.SymbolName = PlcAddressHelper.StripQuotes(trimmed);
                    else
                        node.SymbolName = string.Empty;
                }
                break;
            case "Param1":
                node.Param1 = node.IsTimer || node.IsTimerCoil || node.Kind == LadderElementKind.CoilPT
                    ? trimmed
                    : PlcProgramModel.NormalizeAddress(trimmed);
                break;
            case "Param2":
                node.Param2 = PlcProgramModel.NormalizeAddress(trimmed);
                break;
            case "ExtraParam":
                node.ExtraParam = node.Kind == LadderElementKind.Calculate ? trimmed : PlcProgramModel.NormalizeAddress(trimmed);
                if (node.Kind == LadderElementKind.Calculate)
                    PlcProgramModel.SyncCalculateExtraInputs(node);
                break;
            case "OutputAddress":
                node.OutputAddress = PlcProgramModel.NormalizeAddress(trimmed);
                break;
        }
    }

    private void StartInlineEdit(LadderNode node, string? field)
    {
        if (string.IsNullOrEmpty(field)) return;
        _inlineEditNode = node;
        _inlineEditField = field;
        RenderNetworks();
    }

    private void CancelInlineEdit(bool rerender)
    {
        _inlineEditNode = null;
        _inlineEditField = null;
        if (rerender) RenderNetworks();
    }

    private TextBox CreateInlineEditTextBox(LadderNode node, string field, double minWidth = 76)
    {
        string initial = GetNodeFieldValue(node, field);
        var tb = new TextBox
        {
            Text = initial,
            MinWidth = minWidth,
            Padding = new Thickness(4, 1.5, 4, 1.5),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.6,
            FontWeight = FontWeights.Bold,
            Background = (Brush)FindResource("InputBgBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            BorderBrush = CreateBrush("#38BDF8"),
            BorderThickness = new Thickness(1.5)
        };

        bool committedOrCanceled = false;
        void Commit(bool moveNext, int step)
        {
            if (committedOrCanceled) return;
            committedOrCanceled = true;

            string newVal = tb.Text.Trim();
            if (newVal != initial)
            {
                PushUndoSnapshot();
                SetNodeFieldValue(node, field, newVal);
                NotifyProgramChanged();
            }

            if (moveNext)
            {
                var fields = GetOrderedEditableFields(node);
                int idx = fields.IndexOf(field);
                int nextIdx = idx + step;
                if (nextIdx >= 0 && nextIdx < fields.Count)
                {
                    _inlineEditNode = node;
                    _inlineEditField = fields[nextIdx];
                    RenderNetworks();
                    UpdatePropertyBarForSelectedNode();
                    return;
                }
            }

            _inlineEditNode = null;
            _inlineEditField = null;
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            Focus();
        }

        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Commit(moveNext: false, 0);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
                Commit(moveNext: true, step);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                committedOrCanceled = true;
                CancelInlineEdit(rerender: true);
                Focus();
            }
        };

        tb.LostFocus += (_, _) =>
        {
            if (!committedOrCanceled && _inlineEditNode == node && _inlineEditField == field)
                Commit(moveNext: false, 0);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            tb.Focus();
            tb.SelectAll();
        });

        return tb;
    }

    private void OnInsertEmptyBoxClick(object sender, RoutedEventArgs e) => InsertInstruction(LadderElementKind.EmptyBox);

    // =========================================================================
    // 5. EMPTY BOX [F8] SMART INSTRUCTION COMMAND POPUP
    // =========================================================================

    private void OpenEmptyBoxPopup(UIElement anchor, LadderNode node)
    {
        _emptyBoxTargetNode = node;
        EmptyBoxPopup.PlacementTarget = anchor;
        TbEmptyBoxCmd.Text = node.Address == "???" ? string.Empty : node.Address;
        RebuildEmptyBoxSuggestions();
        EmptyBoxPopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            TbEmptyBoxCmd.Focus();
            TbEmptyBoxCmd.SelectAll();
        });
    }

    private void OnEmptyBoxCmdTextChanged(object sender, TextChangedEventArgs e) => RebuildEmptyBoxSuggestions();

    private void RebuildEmptyBoxSuggestions()
    {
        EmptyBoxSuggestionsStack.Children.Clear();
        _emptyBoxMatches = PlcInstructionCatalog.SearchByPrefixOrText(TbEmptyBoxCmd.Text, 14);
        _emptyBoxSelectedIndex = 0;

        for (int i = 0; i < _emptyBoxMatches.Count; i++)
        {
            int idx = i;
            var meta = _emptyBoxMatches[i];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sym = new TextBlock
            {
                Text = meta.Symbol,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(meta.AccentHex),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sym, 0);

            var desc = new TextBlock
            {
                Text = PlcInstructionCatalog.GetDescription(meta),
                FontSize = 10.2,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(desc, 1);

            row.Children.Add(sym);
            row.Children.Add(desc);

            var btn = new Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = idx == _emptyBoxSelectedIndex ? (Brush)FindResource("MenuHoverBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand
            };
            btn.SetResourceReference(BorderBrushProperty, "BorderSubtleBrush");
            btn.Click += (_, _) => TransformEmptyBoxTo(meta.Kind);
            EmptyBoxSuggestionsStack.Children.Add(btn);
        }
    }

    private void HighlightEmptyBoxRow()
    {
        for (int i = 0; i < EmptyBoxSuggestionsStack.Children.Count; i++)
        {
            if (EmptyBoxSuggestionsStack.Children[i] is Button b)
            {
                b.Background = i == _emptyBoxSelectedIndex ? (Brush)FindResource("MenuHoverBrush") : Brushes.Transparent;
                if (i == _emptyBoxSelectedIndex) b.BringIntoView();
            }
        }
    }

    private void OnEmptyBoxCmdPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EmptyBoxPopup.IsOpen = false;
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _emptyBoxMatches.Count > 0)
        {
            _emptyBoxSelectedIndex = (_emptyBoxSelectedIndex + 1) % _emptyBoxMatches.Count;
            HighlightEmptyBoxRow();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && _emptyBoxMatches.Count > 0)
        {
            _emptyBoxSelectedIndex = (_emptyBoxSelectedIndex - 1 + _emptyBoxMatches.Count) % _emptyBoxMatches.Count;
            HighlightEmptyBoxRow();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string typed = TbEmptyBoxCmd.Text.Trim();
            if (PlcInstructionCatalog.TryResolveByName(typed, out var exactKind))
                TransformEmptyBoxTo(exactKind);
            else if (_emptyBoxMatches.Count > 0)
                TransformEmptyBoxTo(_emptyBoxMatches[Math.Clamp(_emptyBoxSelectedIndex, 0, _emptyBoxMatches.Count - 1)].Kind);
            else
                EmptyBoxPopup.IsOpen = false;
        }
    }

    private void TransformEmptyBoxTo(LadderElementKind resolvedKind)
    {
        if (_plc is null || _emptyBoxTargetNode is null) return;
        EmptyBoxPopup.IsOpen = false;

        PushUndoSnapshot();
        var replacement = PlcInstructionCatalog.CreateDefaultNode(resolvedKind, _plc.DraftProgram, allocateDbTag: false);
        _emptyBoxTargetNode.Kind = replacement.Kind;
        _emptyBoxTargetNode.Address = replacement.Address;
        _emptyBoxTargetNode.SymbolName = replacement.SymbolName;
        _emptyBoxTargetNode.DataType = replacement.DataType;
        _emptyBoxTargetNode.Param1 = replacement.Param1;
        _emptyBoxTargetNode.Param2 = replacement.Param2;
        _emptyBoxTargetNode.ExtraInputs = replacement.ExtraInputs;
        _emptyBoxTargetNode.ExtraOutputs = replacement.ExtraOutputs;
        _emptyBoxTargetNode.ExtraParam = replacement.ExtraParam;
        _emptyBoxTargetNode.OutputAddress = replacement.OutputAddress;

        if (_emptyBoxTargetNode.RequiresCallOptions)
            PromptCallOptionsForNode(_emptyBoxTargetNode, isNewInsert: true);

        _selectedNode = _emptyBoxTargetNode;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
        Focus();
    }

    // =========================================================================
    // 6. KẾT XUẤT NETWORKS + PARALLEL BRANCHES + CONTEXT MENU + DYNAMIC PINS
    // =========================================================================

    private static System.Windows.Shapes.Line CreatePowerWire(bool isOnline, bool isActive, bool isVertical = false, double fixedLength = double.NaN, double minLength = 0.0, double thickness = 2.4, bool isSelected = false)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = isVertical ? 0 : 1,
            Y2 = isVertical ? 1 : 0,
            Stretch = Stretch.Fill,
            StrokeThickness = thickness,
            HorizontalAlignment = isVertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = isVertical ? VerticalAlignment.Stretch : VerticalAlignment.Center
        };
        if (isVertical)
            line.Width = thickness;
        else
            line.Height = thickness;

        if (!double.IsNaN(fixedLength))
        {
            if (isVertical) line.Height = fixedLength;
            else line.Width = fixedLength;
        }
        if (minLength > 0.0)
        {
            if (isVertical) line.MinHeight = minLength;
            else line.MinWidth = minLength;
        }

        StylePowerWire(line, isOnline, isActive, isSelected);
        return line;
    }

    private static void StylePowerWire(System.Windows.Shapes.Line line, bool isOnline, bool isActive, bool isSelected = false)
    {
        if (isOnline)
        {
            line.Stroke = CreateBrush(isActive ? "#22C55E" : "#3B82F6");
            line.StrokeDashArray = isActive ? null : DashedFalseWire;
        }
        else
        {
            line.Stroke = CreateBrush(isSelected ? "#38BDF8" : "#64748B");
            line.StrokeDashArray = null;
        }
    }

    private void RenderNetworks()
    {
        NetworksContainer.Children.Clear();
        _renderedNodeVisuals.Clear();
        _liveOnlineUpdaters.Clear();
        if (_plc is null) return;

        bool isOnline = _isSimulating && _plc.IsPowered && _plc.CpuState == PlcCpuState.Run && !_plc.IsDownloading;
        var nets = GetActiveNetworks();

        for (int i = 0; i < nets.Count; i++)
        {
            int netIdx = i;
            var net = nets[i];
            bool isSelectedNet = netIdx == _selectedNetworkIndex;

            var card = new Border
            {
                BorderThickness = new Thickness(isSelectedNet ? 1.5 : 1.0),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 10),
                MinWidth = 540
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
            hdrBorder.ContextMenu = BuildNetworkContextMenu(netIdx, net);

            var hdrDock = new DockPanel();
            var btnDelNet = new Button { Content = "✕", Width = 22, Height = 20, Padding = new Thickness(0), FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            btnDelNet.SetResourceReference(StyleProperty, "DangerToolbarButtonStyle");
            btnDelNet.Click += (_, e) =>
            {
                e.Handled = true;
                PushUndoSnapshot();
                nets.RemoveAt(netIdx);
                _selectedNetworkIndex = Math.Max(0, Math.Min(_selectedNetworkIndex, nets.Count - 1));
                _selectedNode = null;
                _selectedBranch = nets.ElementAtOrDefault(_selectedNetworkIndex)?.MainBranch;
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
                Focus();
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
                _liveOnlineUpdaters.Add(online => powerRail.Background = CreateBrush(online ? "#22C55E" : "#3B82F6"));
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

    private ContextMenu BuildNetworkContextMenu(int netIdx, LadderNetwork net)
    {
        var cm = new ContextMenu();

        var insBelow = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxInsertNetBelow"), InputGestureText = "Shift+F2" };
        insBelow.Click += (_, _) => { _selectedNetworkIndex = netIdx; InsertNetworkBelowCurrent(); };
        cm.Items.Add(insBelow);

        var renameNet = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxRenameNet") };
        renameNet.Click += (_, _) =>
        {
            string? nextTitle = InputPromptDialog.Prompt(
                Window.GetWindow(this),
                LocalizationManager.Get("Str.Plc.CtxRenameNet"),
                LocalizationManager.Format("Str.Plc.CtxRenameNetPrompt", netIdx + 1),
                net.Title);
            if (!string.IsNullOrWhiteSpace(nextTitle))
            {
                PushUndoSnapshot();
                net.Title = nextTitle.Trim();
                NotifyProgramChanged();
                RenderNetworks();
            }
        };
        cm.Items.Add(renameNet);
        cm.Items.Add(new Separator());

        var copyNet = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxCopy"), InputGestureText = "Ctrl+C" };
        copyNet.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedNode = null; CopySelection(); };
        cm.Items.Add(copyNet);

        var pasteNet = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxPaste"), InputGestureText = "Ctrl+V", IsEnabled = _clipboardNetwork is not null || _clipboardNode is not null };
        pasteNet.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedBranch = net.MainBranch; _selectedNode = null; PasteSelection(); };
        cm.Items.Add(pasteNet);

        var delNet = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxDelete"), InputGestureText = "Delete", Foreground = CreateBrush("#EF4444") };
        delNet.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedNode = null; DeleteSelection(); };
        cm.Items.Add(delNet);

        return cm;
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
                Focus();
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

        var leftLead = CreatePowerWire(isOnline, powerIn, isVertical: false, fixedLength: 9, thickness: 2.5);
        leftLead.HorizontalAlignment = HorizontalAlignment.Left;
        var leftBar = CreatePowerWire(isOnline, powerIn, isVertical: true, thickness: 2.8, isSelected: isSelected);
        leftBar.Margin = new Thickness(8, 22, 0, 22);
        leftBar.HorizontalAlignment = HorizontalAlignment.Left;

        var leftHost = new Grid();
        leftHost.Children.Add(leftLead);
        leftHost.Children.Add(leftBar);
        Grid.SetColumn(leftHost, 0);
        outer.Children.Add(leftHost);

        var subLeads = new List<System.Windows.Shapes.Line>();
        var subTails = new List<(LadderBranch Sub, System.Windows.Shapes.Line Tail)>();

        var subStack = new StackPanel();
        foreach (var sub in blockNode.Branches!)
        {
            var subRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            bool subActive = isOnline && sub.Nodes.Count > 0 && _plc!.Runtime.LiveNodePowerOut.GetValueOrDefault(sub.Nodes[^1].Id);
            var subLead = CreatePowerWire(isOnline, powerIn, isVertical: false, fixedLength: 8, thickness: 2.2);
            subLeads.Add(subLead);
            DockPanel.SetDock(subLead, Dock.Left);
            subRow.Children.Add(subLead);

            var subContent = BuildBranchVisual(sub, netIdx, isOnline);
            DockPanel.SetDock(subContent, Dock.Left);
            subRow.Children.Add(subContent);

            var subTail = CreatePowerWire(isOnline, subActive, isVertical: false, minLength: 8, thickness: 2.2);
            subTails.Add((sub, subTail));
            subRow.Children.Add(subTail);
            subStack.Children.Add(subRow);
        }
        Grid.SetColumn(subStack, 1);
        outer.Children.Add(subStack);

        var rightBar = CreatePowerWire(isOnline, powerOut, isVertical: true, thickness: 2.8, isSelected: isSelected);
        rightBar.Margin = new Thickness(0, 22, 8, 22);
        rightBar.HorizontalAlignment = HorizontalAlignment.Right;
        var rightLead = CreatePowerWire(isOnline, powerOut, isVertical: false, fixedLength: 9, thickness: 2.5);
        rightLead.HorizontalAlignment = HorizontalAlignment.Right;

        var rightHost = new Grid();
        rightHost.Children.Add(rightBar);
        rightHost.Children.Add(rightLead);
        Grid.SetColumn(rightHost, 2);
        outer.Children.Add(rightHost);

        _liveOnlineUpdaters.Add(online =>
        {
            bool pIn = online && _plc is not null && _plc.Runtime.LiveNodePowerIn.GetValueOrDefault(blockNode.Id);
            bool pOut = online && _plc is not null && _plc.Runtime.LiveNodePowerOut.GetValueOrDefault(blockNode.Id);
            StylePowerWire(leftLead, online, pIn);
            StylePowerWire(leftBar, online, pIn, isSelected);
            StylePowerWire(rightBar, online, pOut, isSelected);
            StylePowerWire(rightLead, online, pOut);
            foreach (var sl in subLeads) StylePowerWire(sl, online, pIn);
            foreach (var (subB, tail) in subTails)
            {
                bool sAct = online && _plc is not null && subB.Nodes.Count > 0 &&
                            _plc.Runtime.LiveNodePowerOut.GetValueOrDefault(subB.Nodes[^1].Id);
                StylePowerWire(tail, online, sAct);
            }
        });

        outer.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            _selectedNetworkIndex = netIdx;
            _selectedBranch = parentBranch;
            _selectedNode = blockNode;
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            Focus();
        };

        outer.ContextMenu = BuildNodeContextMenu(blockNode, parentBranch, netIdx);
        _renderedNodeVisuals[blockNode.Id] = outer;
        return outer;
    }

    private UIElement BuildElementVisual(LadderNode node, LadderBranch ownerBranch, int netIdx, bool isOnline)
    {
        bool powerIn = isOnline && _plc!.Runtime.LiveNodePowerIn.GetValueOrDefault(node.Id);
        bool powerOut = isOnline && _plc!.Runtime.LiveNodePowerOut.GetValueOrDefault(node.Id);
        bool isSelected = node == _selectedNode;
        string liveText = isOnline ? _plc!.Runtime.LiveNodeStatusText.GetValueOrDefault(node.Id, string.Empty) : string.Empty;

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

        container.ContextMenu = BuildNodeContextMenu(node, ownerBranch, netIdx);
        container.MouseRightButtonDown += (_, _) =>
        {
            _selectedNetworkIndex = netIdx;
            _selectedBranch = ownerBranch;
            _selectedNode = node;
            UpdatePropertyBarForSelectedNode();
        };

        container.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _selectedNetworkIndex = netIdx;
            _selectedBranch = ownerBranch;
            _selectedNode = node;

            if (e.ClickCount == 2)
            {
                UpdatePropertyBarForSelectedNode();
                if (node.Kind == LadderElementKind.EmptyBox)
                {
                    OpenEmptyBoxPopup(container, node);
                    return;
                }
                if (node.Kind == LadderElementKind.Calculate)
                {
                    OpenCalculateEquationDialog(node);
                    return;
                }
                if (node.RequiresCallOptions && (node.Address == "<???>" || meta is { UsesDbBlock: true }))
                {
                    if (PromptCallOptionsForNode(node, isNewInsert: false, pushUndoOnSuccess: true))
                    {
                        NotifyProgramChanged();
                        RenderNetworks();
                        UpdatePropertyBarForSelectedNode();
                    }
                    return;
                }

                string targetField = meta?.AddrLabel is not null ? "Address" : (meta?.Param1Label is not null ? "Param1" : "Address");
                OpenTagAutocompletePopup(container, node, targetField);
                return;
            }

            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            Focus();
        };

        // --- A. Ký hiệu tiếp điểm / cuộn hút / nhãn nhảy inline ---
        if (meta is { IsInlineGlyph: true })
        {
            bool isJumpOrControl = node.Kind is LadderElementKind.Jump or LadderElementKind.JumpNot or LadderElementKind.InvertRLO or LadderElementKind.Return;
            string line1;
            string line2;
            bool isUndefinedTag = false;
            if (isJumpOrControl)
            {
                line1 = node.Address;
                line2 = string.Empty;
            }
            else
            {
                var (absA, symA) = _plc!.DraftProgram.FormatDisplayPair(node.Address, node.SymbolName);
                isUndefinedTag = absA == "<???>";
                (line1, line2) = _plc.DraftProgram.FormatForDisplayMode(node.Address, node.SymbolName, _displayMode);
            }

            var vStack = new StackPanel { MinWidth = 106, HorizontalAlignment = HorizontalAlignment.Center };

            if (_inlineEditNode == node && _inlineEditField == "Address")
            {
                vStack.Children.Add(CreateInlineEditTextBox(node, "Address", 92));
            }
            else
            {
                var headerClickable = new Border
                {
                    Padding = new Thickness(4, 1, 4, 1),
                    CornerRadius = new CornerRadius(3),
                    Background = isUndefinedTag ? CreateBrush("#22EF4444") : Brushes.Transparent,
                    BorderThickness = new Thickness(isUndefinedTag ? 1 : 0),
                    BorderBrush = isUndefinedTag ? CreateBrush("#EF4444") : Brushes.Transparent,
                    ToolTip = isUndefinedTag
                        ? LocalizationManager.Get("Str.Plc.UndefinedTagTip")
                        : LocalizationManager.Get("Str.Plc.DoubleClickHint")
                };

                var hdrStack = new StackPanel();
                hdrStack.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(line1) ? " " : line1,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.0,
                    FontWeight = FontWeights.Bold,
                    Foreground = CreateBrush(isUndefinedTag ? "#EF4444" : (_displayMode == PlcAddressDisplayMode.SymbolicOnly ? "#38BDF8" : addrTeal)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                string secondLineText = !string.IsNullOrEmpty(line2) ? line2 : (!string.IsNullOrEmpty(node.Param1) ? node.Param1 : " ");
                hdrStack.Children.Add(new TextBlock
                {
                    Text = secondLineText,
                    FontSize = 9.4,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = !string.IsNullOrEmpty(line2) ? (Brush)FindResource("TextPrimaryBrush") : CreateBrush(paramBlue),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                headerClickable.Child = hdrStack;

                if (isUndefinedTag && node.RequiresCallOptions)
                {
                    headerClickable.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        _selectedNetworkIndex = netIdx;
                        _selectedBranch = ownerBranch;
                        _selectedNode = node;
                        if (PromptCallOptionsForNode(node, isNewInsert: false, pushUndoOnSuccess: true))
                        {
                            NotifyProgramChanged();
                            RenderNetworks();
                            UpdatePropertyBarForSelectedNode();
                        }
                    };
                }
                vStack.Children.Add(headerClickable);
            }

            var glyphGrid = new Grid { Height = 26, Margin = new Thickness(0, 2, 0, 2) };
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            glyphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftLine = CreatePowerWire(isOnline, powerIn, isVertical: false, thickness: 2.4);
            Grid.SetColumn(leftLine, 0);
            glyphGrid.Children.Add(leftLine);

            bool activeGlow = isOnline && powerOut;
            var glyphText = new TextBlock
            {
                Text = meta.BoxHeader,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.6,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(activeGlow ? "#22C55E" : (ThemeManager.IsEffectiveDark ? "#F8FAFC" : "#0F172A")),
                VerticalAlignment = VerticalAlignment.Center
            };
            var glyphBorder = new Border
            {
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(3),
                Background = activeGlow ? CreateBrush("#3322C55E") : Brushes.Transparent,
                Child = glyphText
            };
            Grid.SetColumn(glyphBorder, 1);
            glyphGrid.Children.Add(glyphBorder);

            var rightLine = CreatePowerWire(isOnline, powerOut, isVertical: false, thickness: 2.4);
            Grid.SetColumn(rightLine, 2);
            glyphGrid.Children.Add(rightLine);

            _liveOnlineUpdaters.Add(online =>
            {
                bool pIn = online && _plc is not null && _plc.Runtime.LiveNodePowerIn.GetValueOrDefault(node.Id);
                bool pOut = online && _plc is not null && _plc.Runtime.LiveNodePowerOut.GetValueOrDefault(node.Id);
                bool glow = online && pOut;
                StylePowerWire(leftLine, online, pIn);
                StylePowerWire(rightLine, online, pOut);
                glyphBorder.Background = glow ? CreateBrush("#3322C55E") : Brushes.Transparent;
                glyphText.Foreground = CreateBrush(glow ? "#22C55E" : (ThemeManager.IsEffectiveDark ? "#F8FAFC" : "#0F172A"));
            });

            vStack.Children.Add(glyphGrid);
            container.Child = vStack;
            _renderedNodeVisuals[node.Id] = container;
            return container;
        }

        // --- B. Khối hàm chuẩn IEC 61131-3 ---
        var boxGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        boxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

        var inWire = CreatePowerWire(isOnline, powerIn, isVertical: false, thickness: 2.4);
        Grid.SetColumn(inWire, 0);
        boxGrid.Children.Add(inWire);

        var outWire = CreatePowerWire(isOnline, powerOut, isVertical: false, thickness: 2.4);
        Grid.SetColumn(outWire, 2);
        boxGrid.Children.Add(outWire);

        var blockOuter = new StackPanel { MinWidth = 160 };

        // Địa chỉ DB / địa chỉ chính ở đỉnh hộp
        if (meta is { UsesDbBlock: true } || node.Kind is LadderElementKind.FlipFlopSR or LadderElementKind.FlipFlopRS or LadderElementKind.SetBitField or LadderElementKind.ResetBitField or LadderElementKind.PTrig or LadderElementKind.NTrig)
        {
            if (_inlineEditNode == node && _inlineEditField == "Address")
            {
                blockOuter.Children.Add(CreateInlineEditTextBox(node, "Address", 110));
            }
            else
            {
                var (absA, _) = _plc!.DraftProgram.FormatDisplayPair(node.Address, node.SymbolName);
                var (line1, line2) = _plc.DraftProgram.FormatForDisplayMode(node.Address, node.SymbolName, _displayMode);
                bool isUnassigned = absA == "<???>";

                var dbHeaderPill = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 0, 1),
                    CornerRadius = new CornerRadius(3),
                    Background = isUnassigned ? CreateBrush("#28EF4444") : Brushes.Transparent,
                    BorderThickness = new Thickness(isUnassigned ? 1 : 0),
                    BorderBrush = isUnassigned ? CreateBrush("#EF4444") : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = isUnassigned
                        ? LocalizationManager.Get("Str.Plc.ClickAssignDbTip")
                        : LocalizationManager.Get("Str.Plc.DoubleClickHint")
                };

                var dbHdrStack = new StackPanel();
                dbHdrStack.Children.Add(new TextBlock
                {
                    Text = line1,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.0,
                    FontWeight = FontWeights.Bold,
                    Foreground = CreateBrush(isUnassigned ? "#EF4444" : addrTeal),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                if (!string.IsNullOrEmpty(line2))
                {
                    dbHdrStack.Children.Add(new TextBlock
                    {
                        Text = line2,
                        FontSize = 9.3,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                dbHeaderPill.Child = dbHdrStack;

                if (isUnassigned && node.RequiresCallOptions)
                {
                    dbHeaderPill.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        _selectedNetworkIndex = netIdx;
                        _selectedBranch = ownerBranch;
                        _selectedNode = node;
                        if (PromptCallOptionsForNode(node, isNewInsert: false, pushUndoOnSuccess: true))
                        {
                            NotifyProgramChanged();
                            RenderNetworks();
                            UpdatePropertyBarForSelectedNode();
                        }
                    };
                }
                blockOuter.Children.Add(dbHeaderPill);
            }
        }

        bool boxActive = isOnline && powerOut;
        var boxBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(boxActive ? 2.0 : 1.3),
            BorderBrush = CreateBrush(boxActive ? "#22C55E" : (node.Kind == LadderElementKind.EmptyBox ? "#F59E0B" : (node.Address == "<???>" ? "#EF4444" : (powerIn ? "#38BDF8" : "#64748B")))),
            Margin = new Thickness(0, 2, 0, 2)
        };
        boxBorder.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        var boxStack = new StackPanel();
        string headerTitle = node.Kind == LadderElementKind.EmptyBox
            ? $"?? {node.Address}"
            : (meta?.BoxHeader ?? node.Kind.ToString());
        if (!string.IsNullOrWhiteSpace(node.DataType) && PlcInstructionCatalog.SupportsDataTypeSelector(node.Kind))
            headerTitle = $"{headerTitle} ({node.DataType})";
        string headerHex = node.Kind == LadderElementKind.EmptyBox ? "#B45309" : (meta?.AccentHex ?? "#0284C7");

        // Header bar (có nút fx cho CALCULATE)
        var headerDock = new DockPanel { LastChildFill = true };
        if (node.Kind == LadderElementKind.Calculate)
        {
            var fxBtn = new Button
            {
                Content = "fx",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 0.5, 5, 0.5),
                Margin = new Thickness(4, 0, 0, 0),
                Background = CreateBrush("#9D174D"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = LocalizationManager.Get("Str.Plc.EditEquationBtn")
            };
            fxBtn.Click += (_, e) => { e.Handled = true; OpenCalculateEquationDialog(node); };
            DockPanel.SetDock(fxBtn, Dock.Right);
            headerDock.Children.Add(fxBtn);
        }

        headerDock.Children.Add(new TextBlock
        {
            Text = headerTitle,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.4,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var headerBorder = new Border
        {
            Background = CreateBrush(boxActive ? "#15803D" : headerHex),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Padding = new Thickness(7, 2.5, 7, 2.5),
            Child = headerDock
        };
        boxStack.Children.Add(headerBorder);

        TextBlock? liveQText = null;
        TextBlock? liveQdText = null;
        TextBlock? liveStatusTb = null;
        string liveFallbackText = string.Empty;

        var bodyStack = new StackPanel { Margin = new Thickness(7, 4, 7, 5) };
        if (node.Kind == LadderElementKind.EmptyBox)
        {
            AddBoxParamRow(bodyStack, "CMD", "TON, CMP, ADD, NORM_X...", "#F59E0B", "", "", "#94A3B8");
        }
        else if (node.IsTimer)
        {
            liveFallbackText = "T#0ms";
            (_, liveQText) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "PT", "Param1", node.Param1, paramBlue, "Q", null, boxActive ? "1" : "0", boxActive ? "#22C55E" : "#94A3B8");
            if (node.Kind == LadderElementKind.TimerTONR)
                (_, liveStatusTb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "R", "Param2", _plc!.DraftProgram.FormatCompactPinOperand(node.Param2, _displayMode), addrTeal, "ET", null, string.IsNullOrEmpty(liveText) ? "T#0ms" : liveText, "#22C55E");
            else
                (liveStatusTb, _) = AddBoxParamRow(bodyStack, "ET", string.IsNullOrEmpty(liveText) ? "T#0ms" : liveText, "#22C55E", "", "", "#94A3B8");
        }
        else if (node.Kind == LadderElementKind.CounterCTUD)
        {
            liveFallbackText = "0";
            string cvInit = ExtractCtudCv(liveText);
            string qdInit = ExtractCtudQd(liveText);
            (_, liveQText) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "PV", "Param1", node.Param1, paramBlue, "QU", null, boxActive ? "1" : "0", boxActive ? "#22C55E" : "#94A3B8");
            (_, liveQdText) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "CD", "OutputAddress", string.IsNullOrWhiteSpace(node.OutputAddress) ? "-" : _plc!.DraftProgram.FormatCompactPinOperand(node.OutputAddress, _displayMode), addrTeal, "QD", null, qdInit, qdInit == "1" ? "#22C55E" : "#94A3B8");
            (_, liveStatusTb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "R", "Param2", string.IsNullOrWhiteSpace(node.Param2) ? "-" : _plc!.DraftProgram.FormatCompactPinOperand(node.Param2, _displayMode), addrTeal, "CV", null, cvInit, "#22C55E");
            AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "LD", "ExtraParam", string.IsNullOrWhiteSpace(node.ExtraParam) ? "-" : _plc!.DraftProgram.FormatCompactPinOperand(node.ExtraParam, _displayMode), addrTeal, "", null, "", "#94A3B8");
        }
        else if (node.IsCounter)
        {
            liveFallbackText = "0";
            string rLabel = node.Kind == LadderElementKind.CounterCTD ? "LD" : "R";
            (_, liveQText) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "PV", "Param1", node.Param1, paramBlue, "Q", null, boxActive ? "1" : "0", boxActive ? "#22C55E" : "#94A3B8");
            (_, liveStatusTb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, rLabel, "Param2", string.IsNullOrWhiteSpace(node.Param2) ? "-" : _plc!.DraftProgram.FormatCompactPinOperand(node.Param2, _displayMode), addrTeal, "CV", null, string.IsNullOrEmpty(liveText) ? "0" : liveText.Replace("CV: ", ""), "#22C55E");
        }
        else if (node.Kind == LadderElementKind.Label)
        {
            AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "LABEL", "Address", node.Address, "#F97316", "", null, "", "#94A3B8");
        }
        else if (meta is not null)
        {
            if (meta.AddrLabel is not null && !meta.UsesDbBlock &&
                node.Kind is not (LadderElementKind.FlipFlopSR or LadderElementKind.FlipFlopRS or LadderElementKind.SetBitField or LadderElementKind.ResetBitField or LadderElementKind.PTrig or LadderElementKind.NTrig))
            {
                AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, meta.AddrLabel, "Address", _plc!.DraftProgram.FormatCompactPinOperand(node.Address, _displayMode), addrTeal, "", null, "", "#94A3B8");
            }

            if (meta.Param1Label is not null || meta.OutLabel is not null)
            {
                string p1Text = _plc!.DraftProgram.FormatCompactPinOperand(node.Param1, _displayMode);
                string outText = _plc.DraftProgram.FormatCompactPinOperand(node.OutputAddress, _displayMode);
                string outLbl = node.Kind == LadderElementKind.Move && node.ExtraOutputs is { Count: > 0 } ? "OUT1" : (meta.OutLabel ?? "");
                AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx,
                    meta.Param1Label ?? "", meta.Param1Label is not null ? "Param1" : null, p1Text, p1Text.StartsWith('%') ? addrTeal : paramBlue,
                    outLbl, meta.OutLabel is not null ? "OutputAddress" : null, outText, addrTeal);
            }

            // Các chân đầu ra mở rộng OUT2, OUT3... cho MOVE
            if (PlcInstructionCatalog.SupportsMultiOutput(node.Kind) && node.ExtraOutputs is not null)
            {
                for (int k = 0; k < node.ExtraOutputs.Count; k++)
                {
                    string outVal = _plc!.DraftProgram.FormatCompactPinOperand(node.ExtraOutputs[k], _displayMode);
                    AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, "", null, "", paramBlue,
                        $"OUT{k + 2}", $"ExtraOutput:{k}", outVal, addrTeal);
                }
            }

            if (meta.Param2Label is not null)
            {
                string p2Text = _plc!.DraftProgram.FormatCompactPinOperand(node.Param2, _displayMode);
                bool showLiveHere = string.IsNullOrEmpty(meta.ExtraLabel) && (node.ExtraInputs is null || node.ExtraInputs.Count == 0);
                (_, var tb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, meta.Param2Label, "Param2", p2Text, p2Text.StartsWith('%') ? addrTeal : paramBlue,
                    "", null, showLiveHere ? liveText : "", "#22C55E");
                if (showLiveHere) liveStatusTb = tb;
            }

            // Các chân mở rộng IN3, IN4, ... (ADD, MUL, AND, OR, XOR, MUX, MIN, MAX, CALCULATE)
            if (PlcInstructionCatalog.SupportsMultiInput(node.Kind) && node.ExtraInputs is not null)
            {
                for (int k = 0; k < node.ExtraInputs.Count; k++)
                {
                    string pinLbl = PlcInstructionCatalog.GetExtraInputLabel(node.Kind, k);
                    string pinVal = _plc!.DraftProgram.FormatCompactPinOperand(node.ExtraInputs[k], _displayMode);
                    bool showLive = k == node.ExtraInputs.Count - 1 && string.IsNullOrEmpty(meta.ExtraLabel);
                    (_, var tb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, pinLbl, $"ExtraInput:{k}", pinVal, pinVal.StartsWith('%') ? addrTeal : paramBlue,
                        "", null, showLive ? liveText : "", "#22C55E");
                    if (showLive) liveStatusTb = tb;
                }
            }

            if (meta.ExtraLabel is not null)
            {
                string exText = node.Kind == LadderElementKind.Calculate
                    ? node.ExtraParam
                    : _plc!.DraftProgram.FormatCompactPinOperand(node.ExtraParam, _displayMode);
                (_, liveStatusTb) = AddEditableOrStaticPinRow(bodyStack, node, ownerBranch, netIdx, meta.ExtraLabel, "ExtraParam", exText, paramBlue, "", null, liveText, "#22C55E");
            }
            else if (meta.Param2Label is null)
            {
                (liveStatusTb, _) = AddBoxParamRow(bodyStack, "VAL", liveText, "#22C55E", "", "", "#94A3B8");
            }

            // Nút "+" thêm chân đầu vào (Insert input) hoặc đầu ra (MOVE + OUT) ngay trên hộp hàm
            if (PlcInstructionCatalog.SupportsMultiInput(node.Kind) && node.Kind != LadderElementKind.Calculate)
            {
                var addPinBtn = new Button
                {
                    Content = "+ IN",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 8.8,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(5, 0.5, 5, 0.5),
                    Margin = new Thickness(0, 3, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = LocalizationManager.Get("Str.Plc.InsertPinBtn")
                };
                addPinBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
                addPinBtn.Click += (_, e) => { e.Handled = true; InsertExtraInputOnNode(node); };
                bodyStack.Children.Add(addPinBtn);
            }
            else if (PlcInstructionCatalog.SupportsMultiOutput(node.Kind))
            {
                var addOutBtn = new Button
                {
                    Content = "+ OUT",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 8.8,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(5, 0.5, 5, 0.5),
                    Margin = new Thickness(0, 3, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    ToolTip = LocalizationManager.Get("Str.Plc.InsertOutPinBtn")
                };
                addOutBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
                addOutBtn.Click += (_, e) => { e.Handled = true; InsertExtraOutputOnNode(node); };
                bodyStack.Children.Add(addOutBtn);
            }
        }

        _liveOnlineUpdaters.Add(online =>
        {
            bool pIn = online && _plc is not null && _plc.Runtime.LiveNodePowerIn.GetValueOrDefault(node.Id);
            bool pOut = online && _plc is not null && _plc.Runtime.LiveNodePowerOut.GetValueOrDefault(node.Id);
            bool bAct = online && pOut;
            StylePowerWire(inWire, online, pIn);
            StylePowerWire(outWire, online, pOut);
            boxBorder.BorderThickness = new Thickness(bAct ? 2.0 : 1.3);
            boxBorder.BorderBrush = CreateBrush(bAct ? "#22C55E" : (node.Kind == LadderElementKind.EmptyBox ? "#F59E0B" : (node.Address == "<???>" ? "#EF4444" : (pIn ? "#38BDF8" : "#64748B"))));
            headerBorder.Background = CreateBrush(bAct ? "#15803D" : headerHex);

            string stTxt = online && _plc is not null
                ? _plc.Runtime.LiveNodeStatusText.GetValueOrDefault(node.Id, string.Empty)
                : string.Empty;

            if (liveQText is not null)
            {
                liveQText.Text = bAct ? "1" : "0";
                liveQText.Foreground = CreateBrush(bAct ? "#22C55E" : "#94A3B8");
            }
            if (liveQdText is not null)
            {
                string qd = online ? ExtractCtudQd(stTxt) : "0";
                liveQdText.Text = qd;
                liveQdText.Foreground = CreateBrush(qd == "1" ? "#22C55E" : "#94A3B8");
            }
            if (liveStatusTb is not null)
            {
                if (node.Kind == LadderElementKind.CounterCTUD)
                    liveStatusTb.Text = online ? ExtractCtudCv(stTxt) : "0";
                else if (node.IsCounter)
                    liveStatusTb.Text = string.IsNullOrEmpty(stTxt) ? "0" : stTxt.Replace("CV: ", "");
                else
                    liveStatusTb.Text = string.IsNullOrEmpty(stTxt) ? liveFallbackText : stTxt;
            }
        });

        boxStack.Children.Add(bodyStack);
        boxBorder.Child = boxStack;
        blockOuter.Children.Add(boxBorder);

        Grid.SetColumn(blockOuter, 1);
        boxGrid.Children.Add(blockOuter);
        container.Child = boxGrid;
        _renderedNodeVisuals[node.Id] = container;
        return container;
    }

    private static string ExtractCtudCv(string liveText)
    {
        if (string.IsNullOrEmpty(liveText)) return "0";
        int pipe = liveText.IndexOf('|');
        string left = pipe >= 0 ? liveText[..pipe] : liveText;
        return left.Replace("CV: ", "", StringComparison.Ordinal).Trim();
    }

    private static string ExtractCtudQd(string liveText)
    {
        if (string.IsNullOrEmpty(liveText)) return "0";
        int idx = liveText.IndexOf("QD:", StringComparison.Ordinal);
        return idx >= 0 && idx + 3 < liveText.Length ? liveText[(idx + 3)..].Trim() : "0";
    }

    private (TextBlock? LeftValTb, TextBlock? RightValTb) AddEditableOrStaticPinRow(
        StackPanel host,
        LadderNode node,
        LadderBranch ownerBranch,
        int netIdx,
        string leftKey,
        string? leftField,
        string leftVal,
        string leftHex,
        string rightKey,
        string? rightField,
        string rightVal,
        string rightHex)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock? leftTb = null;
        TextBlock? rightTb = null;

        void AttachPinDoubleClick(UIElement target, string fieldName)
        {
            target.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                _selectedNetworkIndex = netIdx;
                _selectedBranch = ownerBranch;
                _selectedNode = node;
                UpdatePropertyBarForSelectedNode();
                if (node.Kind == LadderElementKind.Calculate && fieldName == "ExtraParam")
                    OpenCalculateEquationDialog(node);
                else
                    StartInlineEdit(node, fieldName);
            };
        }

        var lStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(leftKey))
            lStack.Children.Add(new TextBlock { Text = $"{leftKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center });

        if (leftField is not null && _inlineEditNode == node && _inlineEditField == leftField)
        {
            lStack.Children.Add(CreateInlineEditTextBox(node, leftField, 64));
        }
        else if (!string.IsNullOrEmpty(leftVal) || leftField is not null)
        {
            leftTb = new TextBlock { Text = string.IsNullOrEmpty(leftVal) ? "-" : leftVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(leftHex), VerticalAlignment = VerticalAlignment.Center };
            lStack.Children.Add(leftTb);
            if (leftField is not null) AttachPinDoubleClick(lStack, leftField);
        }
        Grid.SetColumn(lStack, 0);
        g.Children.Add(lStack);

        if (!string.IsNullOrEmpty(rightKey) || !string.IsNullOrEmpty(rightVal) || rightField is not null || (rightField is not null && _inlineEditNode == node && _inlineEditField == rightField))
        {
            var rStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(rightKey))
                rStack.Children.Add(new TextBlock { Text = $"{rightKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center });

            if (rightField is not null && _inlineEditNode == node && _inlineEditField == rightField)
                rStack.Children.Add(CreateInlineEditTextBox(node, rightField, 64));
            else
            {
                rightTb = new TextBlock { Text = string.IsNullOrEmpty(rightVal) && rightField is not null ? "-" : rightVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(rightHex), VerticalAlignment = VerticalAlignment.Center };
                rStack.Children.Add(rightTb);
                if (rightField is not null) AttachPinDoubleClick(rStack, rightField);
            }

            Grid.SetColumn(rStack, 2);
            g.Children.Add(rStack);
        }

        host.Children.Add(g);
        return (leftTb, rightTb);
    }

    private static (TextBlock? LeftValTb, TextBlock? RightValTb) AddBoxParamRow(StackPanel host, string leftKey, string leftVal, string leftHex, string rightKey, string rightVal, string rightHex)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock? leftTb = null;
        TextBlock? rightTb = null;

        var lStack = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(leftKey))
            lStack.Children.Add(new TextBlock { Text = $"{leftKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8") });
        leftTb = new TextBlock { Text = leftVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(leftHex) };
        lStack.Children.Add(leftTb);
        Grid.SetColumn(lStack, 0);
        g.Children.Add(lStack);

        if (!string.IsNullOrEmpty(rightKey) || !string.IsNullOrEmpty(rightVal))
        {
            var rStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            if (!string.IsNullOrEmpty(rightKey))
                rStack.Children.Add(new TextBlock { Text = $"{rightKey}: ", FontSize = 9.0, Foreground = CreateBrush("#94A3B8") });
            rightTb = new TextBlock { Text = rightVal, FontFamily = new FontFamily("Consolas"), FontSize = 9.6, FontWeight = FontWeights.Bold, Foreground = CreateBrush(rightHex) };
            rStack.Children.Add(rightTb);
            Grid.SetColumn(rStack, 2);
            g.Children.Add(rStack);
        }

        host.Children.Add(g);
        return (leftTb, rightTb);
    }

    // =========================================================================
    // 7. MENU CHUỘT PHẢI THÔNG MINH (DEFINE TAG / RENAME TAG / CALL TYPE / EQUATION / CUT-COPY-PASTE)
    // =========================================================================

    private ContextMenu BuildNodeContextMenu(LadderNode node, LadderBranch ownerBranch, int netIdx)
    {
        var cm = new ContextMenu();

        if (node.Kind != LadderElementKind.ParallelBlock)
        {
            var editItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxInlineEdit"), InputGestureText = "Enter / F2" };
            editItem.Click += (_, _) =>
            {
                _selectedNetworkIndex = netIdx;
                _selectedBranch = ownerBranch;
                _selectedNode = node;
                if (node.Kind == LadderElementKind.EmptyBox)
                    OpenEmptyBoxPopup(_renderedNodeVisuals.GetValueOrDefault(node.Id) ?? (UIElement)BtnEmptyBox, node);
                else
                    StartInlineEdit(node, GetOrderedEditableFields(node).FirstOrDefault());
            };
            cm.Items.Add(editItem);

            var defTagItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxDefineTag"), InputGestureText = "Alt+Shift+D" };
            defTagItem.Click += (_, _) => OpenDefineTagForNode(node);
            cm.Items.Add(defTagItem);

            var renTagItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxRenameTag"), InputGestureText = "Alt+Shift+N" };
            renTagItem.Click += (_, _) => OpenRenameTagForNode(node);
            cm.Items.Add(renTagItem);

            if (node.RequiresCallOptions)
            {
                var callTypeItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxChangeCallType") };
                callTypeItem.Click += (_, _) =>
                {
                    if (PromptCallOptionsForNode(node, isNewInsert: false, pushUndoOnSuccess: true))
                    {
                        NotifyProgramChanged();
                        RenderNetworks();
                        UpdatePropertyBarForSelectedNode();
                    }
                };
                cm.Items.Add(callTypeItem);
            }

            if (node.Kind == LadderElementKind.Calculate)
            {
                var calcItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.EditEquationBtn") };
                calcItem.Click += (_, _) => OpenCalculateEquationDialog(node);
                cm.Items.Add(calcItem);
            }

            if (PlcInstructionCatalog.SupportsMultiInput(node.Kind) && node.Kind != LadderElementKind.Calculate)
            {
                var addPinItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxInsertInputPin") };
                addPinItem.Click += (_, _) => InsertExtraInputOnNode(node);
                cm.Items.Add(addPinItem);

                if (node.ExtraInputs is { Count: > 0 })
                {
                    var remPinItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxRemoveInputPin") };
                    remPinItem.Click += (_, _) => RemoveLastExtraInputOnNode(node);
                    cm.Items.Add(remPinItem);
                }
            }

            if (PlcInstructionCatalog.SupportsMultiOutput(node.Kind))
            {
                var addOutItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxInsertOutPin") };
                addOutItem.Click += (_, _) => InsertExtraOutputOnNode(node);
                cm.Items.Add(addOutItem);

                if (node.ExtraOutputs is { Count: > 0 })
                {
                    var remOutItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxRemoveOutPin") };
                    remOutItem.Click += (_, _) => RemoveLastExtraOutputOnNode(node);
                    cm.Items.Add(remOutItem);
                }
            }

            cm.Items.Add(new Separator());
        }

        var cutItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxCut"), InputGestureText = "Ctrl+X" };
        cutItem.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedBranch = ownerBranch; _selectedNode = node; CutSelection(); };
        cm.Items.Add(cutItem);

        var copyItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxCopy"), InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedBranch = ownerBranch; _selectedNode = node; CopySelection(); };
        cm.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxPaste"), InputGestureText = "Ctrl+V", IsEnabled = _clipboardNode is not null };
        pasteItem.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedBranch = ownerBranch; _selectedNode = node; PasteSelection(); };
        cm.Items.Add(pasteItem);

        var delItem = new MenuItem { Header = LocalizationManager.Get("Str.Plc.CtxDelete"), InputGestureText = "Delete", Foreground = CreateBrush("#EF4444") };
        delItem.Click += (_, _) => { _selectedNetworkIndex = netIdx; _selectedBranch = ownerBranch; _selectedNode = node; DeleteSelection(); };
        cm.Items.Add(delItem);

        return cm;
    }

    private void OpenDefineTagForNode(LadderNode node)
    {
        if (_plc is null) return;
        var prog = _plc.DraftProgram;
        string raw = !string.IsNullOrWhiteSpace(node.Address) ? node.Address : node.OutputAddress;
        string suggestedName = !string.IsNullOrWhiteSpace(node.SymbolName)
            ? PlcAddressHelper.StripQuotes(node.SymbolName)
            : (!PlcAddressHelper.IsAbsoluteAddress(raw) ? PlcAddressHelper.StripQuotes(raw) : prog.ResolveTagName(raw));
        string suggestedAddr = PlcAddressHelper.IsAbsoluteAddress(raw) ? PlcProgramModel.NormalizeAddress(raw) : string.Empty;
        string suggestedType = node.IsContactOrCoilGlyph ? "Bool" : (string.IsNullOrWhiteSpace(node.DataType) ? "Int" : node.DataType);

        var dlg = new PlcDefineTagDialog(prog, suggestedName, suggestedAddr, suggestedType);
        var owner = Window.GetWindow(this);
        if (owner is { IsVisible: true }) dlg.Owner = owner;

        if (dlg.ShowDialog() == true)
        {
            PushUndoSnapshot();
            var existing = prog.Tags.FirstOrDefault(t =>
                t.Name.Equals(dlg.TagName, StringComparison.OrdinalIgnoreCase) ||
                PlcProgramModel.NormalizeAddress(t.Address).Equals(dlg.TagAddress, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Name = dlg.TagName;
                existing.Address = dlg.TagAddress;
                existing.DataType = dlg.TagDataType;
                if (!string.IsNullOrWhiteSpace(dlg.TagComment)) existing.Comment = dlg.TagComment;
            }
            else
            {
                prog.Tags.Add(new PlcTagEntry
                {
                    Name = dlg.TagName,
                    Address = dlg.TagAddress,
                    DataType = dlg.TagDataType,
                    Comment = dlg.TagComment
                });
            }

            if (!string.IsNullOrWhiteSpace(node.Address) || node.IsContactOrCoilGlyph)
            {
                node.Address = dlg.TagAddress;
                node.SymbolName = dlg.TagName;
            }
            else if (!string.IsNullOrWhiteSpace(node.OutputAddress))
            {
                node.OutputAddress = dlg.TagAddress;
            }

            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.DefineTagDone", dlg.TagName, dlg.TagAddress));
        }
    }

    private void OpenRenameTagForNode(LadderNode node)
    {
        if (_plc is null) return;
        var prog = _plc.DraftProgram;
        string currentName = prog.ResolveTagName(node.Address, node.SymbolName);
        if (string.IsNullOrWhiteSpace(currentName) && !PlcAddressHelper.IsAbsoluteAddress(node.Address))
            currentName = PlcAddressHelper.StripQuotes(node.Address);

        if (string.IsNullOrWhiteSpace(currentName))
        {
            OpenDefineTagForNode(node);
            return;
        }

        // Bỏ phần ".Q" hoặc ".x0" nếu đang đứng trên truy xuất thành viên / slice
        int dot = currentName.IndexOf('.');
        if (dot > 0) currentName = currentName[..dot];

        string? nextName = InputPromptDialog.Prompt(
            Window.GetWindow(this),
            LocalizationManager.Get("Str.Plc.CtxRenameTag"),
            LocalizationManager.Format("Str.Plc.RenameTagPrompt", currentName),
            currentName);

        if (!string.IsNullOrWhiteSpace(nextName))
        {
            string cleanNew = PlcAddressHelper.StripQuotes(nextName.Trim());
            if (!cleanNew.Equals(currentName, StringComparison.Ordinal))
            {
                PushUndoSnapshot();
                int count = prog.RenameTagEverywhere(currentName, cleanNew);
                NotifyProgramChanged();
                RenderNetworks();
                UpdatePropertyBarForSelectedNode();
                StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Plc.RenameTagDone", currentName, cleanNew, count));
            }
        }
    }

    private void OpenCalculateEquationDialog(LadderNode node)
    {
        if (_plc is null) return;
        var dlg = new PlcCalculateDialog(node.DataType, node.ExtraParam);
        var owner = Window.GetWindow(this);
        if (owner is { IsVisible: true }) dlg.Owner = owner;

        if (dlg.ShowDialog() == true)
        {
            PushUndoSnapshot();
            node.DataType = dlg.SelectedDataType;
            node.ExtraParam = dlg.ExpressionText;

            int extraNeeded = Math.Max(0, dlg.RequiredInputs - 2);
            if (extraNeeded == 0)
            {
                node.ExtraInputs = null;
            }
            else
            {
                node.ExtraInputs ??= new List<string>();
                while (node.ExtraInputs.Count < extraNeeded)
                    node.ExtraInputs.Add("0");
                while (node.ExtraInputs.Count > extraNeeded)
                    node.ExtraInputs.RemoveAt(node.ExtraInputs.Count - 1);
            }

            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
    }

    private void InsertExtraInputOnNode(LadderNode node)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        node.ExtraInputs ??= new List<string>();
        if (node.ExtraInputs.Count < 14)
        {
            node.ExtraInputs.Add("0");
            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
    }

    private void RemoveLastExtraInputOnNode(LadderNode node)
    {
        if (_plc is null || node.ExtraInputs is null || node.ExtraInputs.Count == 0) return;
        PushUndoSnapshot();
        node.ExtraInputs.RemoveAt(node.ExtraInputs.Count - 1);
        if (node.ExtraInputs.Count == 0) node.ExtraInputs = null;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void InsertExtraOutputOnNode(LadderNode node)
    {
        if (_plc is null) return;
        PushUndoSnapshot();
        node.ExtraOutputs ??= new List<string>();
        if (node.ExtraOutputs.Count < 14)
        {
            node.ExtraOutputs.Add("%MW6");
            NotifyProgramChanged();
            RenderNetworks();
            UpdatePropertyBarForSelectedNode();
        }
    }

    private void RemoveLastExtraOutputOnNode(LadderNode node)
    {
        if (_plc is null || node.ExtraOutputs is null || node.ExtraOutputs.Count == 0) return;
        PushUndoSnapshot();
        node.ExtraOutputs.RemoveAt(node.ExtraOutputs.Count - 1);
        if (node.ExtraOutputs.Count == 0) node.ExtraOutputs = null;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    // =========================================================================
    // 8. POPUP GỢI Ý 3 CỘT (USER TAGS + SYSTEM/CLOCK MEMORY + DB MEMBERS + SLICES)
    // =========================================================================

    private void OnOpenAutocompleteFromPropClick(object sender, RoutedEventArgs e)
    {
        if (_plc is null || _selectedNode is null || sender is not Button btn) return;
        string field = btn.Tag as string ?? "Address";
        OpenTagAutocompletePopup(btn, _selectedNode, field);
    }

    private void OpenTagAutocompletePopup(UIElement anchor, LadderNode? node, string targetField, PlcWatchEntry? watchEntry = null)
    {
        if (_plc is null) return;

        _autoTargetNode = node;
        _autoTargetWatch = watchEntry;
        _autoTargetField = targetField;
        _autoExpandedTag = null;
        _autoExpandedIsSlice = false;

        TagAutocompletePopup.PlacementTarget = anchor;
        _updatingAutoSearch = true;
        TbAutoSearch.Text = string.Empty;
        _updatingAutoSearch = false;
        TxtAutoSearchHint.Visibility = Visibility.Visible;

        RebuildAutocompleteResults();
        TagAutocompletePopup.IsOpen = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            TbAutoSearch.Focus();
            TbAutoSearch.SelectAll();
        });
    }

    private void OnAutoSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingAutoSearch || _plc is null) return;
        string raw = TbAutoSearch.Text ?? string.Empty;
        TxtAutoSearchHint.Visibility = string.IsNullOrEmpty(raw) ? Visibility.Visible : Visibility.Collapsed;

        int dot = raw.IndexOf('.');
        if (dot > 0)
        {
            string left = PlcAddressHelper.StripQuotes(raw[..dot]);
            var db = _plc.DraftProgram.GetAllDataBlocks().FirstOrDefault(d =>
                d.Name.Equals(left, StringComparison.OrdinalIgnoreCase) ||
                d.Address.TrimStart('%').Equals(left, StringComparison.OrdinalIgnoreCase));
            if (db is not null)
            {
                _autoExpandedTag = db;
                _autoExpandedIsSlice = false;
            }
            else
            {
                var sliceTag = _plc.DraftProgram.Tags.FirstOrDefault(t =>
                    t.Name.Equals(left, StringComparison.OrdinalIgnoreCase) &&
                    PlcDataTypeHelper.TryParse(t.DataType, out var dt) &&
                    PlcDataTypeHelper.CanSlice(dt));
                if (sliceTag is not null)
                {
                    _autoExpandedTag = sliceTag;
                    _autoExpandedIsSlice = true;
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(raw))
        {
            _autoExpandedTag = null;
            _autoExpandedIsSlice = false;
        }

        RebuildAutocompleteResults();
    }

    private void OnAutoSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TagAutocompletePopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_autoRowActions.Count > 0)
                _autoRowActions[0].Invoke();
            else if (!string.IsNullOrWhiteSpace(TbAutoSearch.Text))
                ApplyAutocompleteSelection(TbAutoSearch.Text.Trim(), string.Empty);
            e.Handled = true;
        }
    }

    private void OnAutoBackToAllClick(object sender, RoutedEventArgs e)
    {
        _autoExpandedTag = null;
        _autoExpandedIsSlice = false;
        _updatingAutoSearch = true;
        TbAutoSearch.Text = string.Empty;
        _updatingAutoSearch = false;
        TxtAutoSearchHint.Visibility = Visibility.Visible;
        RebuildAutocompleteResults();
        TbAutoSearch.Focus();
    }

    private void RebuildAutocompleteResults()
    {
        AutoResultsStack.Children.Clear();
        _autoRowActions.Clear();
        if (_plc is null) return;

        var prog = _plc.DraftProgram;
        string query = (TbAutoSearch.Text ?? string.Empty).Trim();

        // Chế độ 2: Đang mở rộng thuộc tính DB ("T1".Q, .ET) hoặc Slice ("Tag".x0, .b0, .w0)
        if (_autoExpandedTag is not null)
        {
            AutoDbHeaderBanner.Visibility = Visibility.Visible;
            TxtAutoActiveDb.Text = $"\"{_autoExpandedTag.Name}\" ({_autoExpandedTag.Address})";

            string memberFilter = string.Empty;
            int dot = query.IndexOf('.');
            if (dot >= 0 && dot < query.Length - 1)
                memberFilter = query[(dot + 1)..].Trim();
            else if (dot < 0 && !query.Equals(_autoExpandedTag.Name, StringComparison.OrdinalIgnoreCase))
                memberFilter = query;

            var members = (_autoExpandedIsSlice
                ? PlcAddressHelper.GetSliceMembers(_autoExpandedTag.DataType)
                : PlcAddressHelper.GetDbMembers(_autoExpandedTag.DataType))
                .Where(m => string.IsNullOrEmpty(memberFilter) ||
                            m.Name.Contains(memberFilter, StringComparison.OrdinalIgnoreCase) ||
                            m.DataType.Contains(memberFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var mem in members)
            {
                string symbolicMember = $"\"{_autoExpandedTag.Name}\".{mem.Name}";
                string absMember = $"{PlcProgramModel.NormalizeAddress(_autoExpandedTag.Address)}.{mem.Name}";
                Action selectAction = () => ApplyAutocompleteSelection(symbolicMember, symbolicMember);
                _autoRowActions.Add(selectAction);

                AutoResultsStack.Children.Add(CreateAutocompleteRow(
                    iconText: _autoExpandedIsSlice ? "S" : ".",
                    iconBgHex: _autoExpandedIsSlice ? "#7C3AED" : "#0284C7",
                    displayName: $".{mem.Name}  ({mem.Description})",
                    dataType: mem.DataType,
                    absAddr: absMember,
                    onClick: selectAction,
                    onExpandDb: null));
            }

            if (members.Count == 0) AddAutocompleteEmptyRow();
            return;
        }

        // Chế độ 1: PLC Tags + Data Blocks + System & Clock Memory
        AutoDbHeaderBanner.Visibility = Visibility.Collapsed;

        var combined = new List<(PlcTagEntry Entry, string Badge, string BgHex, bool CanExpand, bool IsSlice)>();
        var seenAddr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1A. User Tags
        foreach (var tag in prog.Tags)
        {
            string norm = PlcProgramModel.NormalizeAddress(tag.Address);
            bool isDb = norm.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && !norm.Contains('.');
            bool isSliceable = !isDb && PlcDataTypeHelper.TryParse(tag.DataType, out var dt) && PlcDataTypeHelper.CanSlice(dt);
            if (!string.IsNullOrWhiteSpace(norm)) seenAddr.Add(norm);
            combined.Add((tag, isDb ? "DB" : "T", isDb ? "#0F766E" : "#2563EB", isDb || isSliceable, isSliceable));
        }

        // 1B. Data Blocks chưa nằm trong Tags
        foreach (var db in prog.GetAllDataBlocks())
        {
            string norm = PlcProgramModel.NormalizeAddress(db.Address);
            if (seenAddr.Add(norm))
                combined.Add((db, "DB", "#0F766E", true, false));
        }

        // 1C. System & Clock Memory (luôn có mặt trong Autocomplete khi được bật!)
        foreach (var sys in prog.GetSystemAndClockTags())
        {
            bool isClock = sys.Name.StartsWith("Clock_", StringComparison.OrdinalIgnoreCase);
            combined.Add((sys, isClock ? "CLK" : "SYS", isClock ? "#D97706" : "#0891B2", false, false));
        }

        var filtered = combined.Where(item =>
            string.IsNullOrEmpty(query) ||
            item.Entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Entry.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Entry.DataType.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var (entry, badge, bgHex, canExpand, isSlice) in filtered)
        {
            string normAddr = PlcProgramModel.NormalizeAddress(entry.Address);
            var captured = entry;
            Action? expandAction = canExpand
                ? () =>
                {
                    _autoExpandedTag = captured;
                    _autoExpandedIsSlice = isSlice;
                    _updatingAutoSearch = true;
                    TbAutoSearch.Text = $"\"{captured.Name}\".";
                    TbAutoSearch.CaretIndex = TbAutoSearch.Text.Length;
                    _updatingAutoSearch = false;
                    TxtAutoSearchHint.Visibility = Visibility.Collapsed;
                    RebuildAutocompleteResults();
                    TbAutoSearch.Focus();
                }
                : null;

            Action clickAction = (badge == "DB")
                ? expandAction!
                : () =>
                {
                    string pickVal = entry.IsSystemGenerated ? $"\"{entry.Name}\"" : normAddr;
                    ApplyAutocompleteSelection(pickVal, entry.Name);
                };

            _autoRowActions.Add(clickAction);

            AutoResultsStack.Children.Add(CreateAutocompleteRow(
                iconText: badge,
                iconBgHex: bgHex,
                displayName: string.IsNullOrWhiteSpace(entry.Name) ? normAddr : $"\"{entry.Name}\"",
                dataType: entry.DataType,
                absAddr: badge == "DB" ? normAddr.TrimStart('%') : normAddr,
                onClick: clickAction,
                onExpandDb: expandAction));
        }

        if (filtered.Count == 0) AddAutocompleteEmptyRow();
    }

    private UIElement CreateAutocompleteRow(
        string iconText,
        string iconBgHex,
        string displayName,
        string dataType,
        string absAddr,
        Action onClick,
        Action? onExpandDb)
    {
        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

        var nameDock = new DockPanel { LastChildFill = true };
        var badge = new Border
        {
            Background = CreateBrush(iconBgHex),
            CornerRadius = new CornerRadius(3),
            MinWidth = 22,
            Height = 16,
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = iconText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8.4,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(badge, Dock.Left);
        nameDock.Children.Add(badge);

        if (onExpandDb is not null)
        {
            var expandBtn = new Button
            {
                Content = ">>",
                Padding = new Thickness(5, 0.5, 5, 0.5),
                Margin = new Thickness(4, 0, 4, 0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            expandBtn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
            expandBtn.Click += (_, e) => { e.Handled = true; onExpandDb(); };
            DockPanel.SetDock(expandBtn, Dock.Right);
            nameDock.Children.Add(expandBtn);
        }

        var nameTb = new TextBlock
        {
            Text = displayName,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nameTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameDock.Children.Add(nameTb);
        Grid.SetColumn(nameDock, 0);
        rowGrid.Children.Add(nameDock);

        var typeTb = new TextBlock
        {
            Text = dataType,
            FontSize = 10.4,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        typeTb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(typeTb, 1);
        rowGrid.Children.Add(typeTb);

        var addrTb = new TextBlock
        {
            Text = absAddr,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.4,
            FontWeight = FontWeights.Bold,
            Foreground = CreateBrush("#2DD4BF"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(addrTb, 2);
        rowGrid.Children.Add(addrTb);

        var btn = new Button
        {
            Content = rowGrid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 4),
            Cursor = Cursors.Hand
        };
        btn.SetResourceReference(BorderBrushProperty, "BorderSubtleBrush");
        btn.MouseEnter += (_, _) => btn.SetResourceReference(BackgroundProperty, "MenuHoverBrush");
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void AddAutocompleteEmptyRow()
    {
        var tb = new TextBlock
        {
            Text = LocalizationManager.Get("Str.Plc.AutoEmpty"),
            FontSize = 10.8,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(10, 10, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AutoResultsStack.Children.Add(tb);
    }

    private void ApplyAutocompleteSelection(string value, string symbol)
    {
        if (_plc is null) return;

        if (_autoTargetWatch is not null)
        {
            _autoTargetWatch.Expression = !string.IsNullOrWhiteSpace(symbol) && !symbol.Contains('.') ? $"\"{symbol}\"" : value;
            TagAutocompletePopup.IsOpen = false;
            NotifyProgramChanged();
            RenderWatchTable();
            return;
        }

        if (_autoTargetNode is null) return;
        PushUndoSnapshot();
        switch (_autoTargetField)
        {
            case "Param1":
                _autoTargetNode.Param1 = value;
                break;
            case "Param2":
                _autoTargetNode.Param2 = value;
                break;
            case "Out":
            case "OutputAddress":
                _autoTargetNode.OutputAddress = value;
                break;
            default:
                _autoTargetNode.Address = value;
                _autoTargetNode.SymbolName = symbol;
                break;
        }

        TagAutocompletePopup.IsOpen = false;
        _selectedNode = _autoTargetNode;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }

    private void InsertNetworkBelowCurrent()
    {
        if (_plc is null) return;
        RbTabLadder.IsChecked = true;
        PushUndoSnapshot();
        var nets = GetActiveNetworks();
        int insertIdx = nets.Count == 0 ? 0 : Math.Clamp(_selectedNetworkIndex + 1, 0, nets.Count);
        var net = new LadderNetwork { Title = $"Network {insertIdx + 1}" };
        nets.Insert(insertIdx, net);
        _selectedNetworkIndex = insertIdx;
        _selectedBranch = net.MainBranch;
        _selectedNode = null;
        NotifyProgramChanged();
        RenderNetworks();
        UpdatePropertyBarForSelectedNode();
    }
}
