using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.Simulation;
using WiringSim.UI.Helpers;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

/// <summary>
/// LỚP 1: UI / CANVAS LAYER (v3.0)
/// - Bóng mờ xem trước bán trong suốt (Visual Drag Ghost) bắt dính theo lưới khi kéo thiết bị từ Thư viện.
/// - Chế độ Dây: Đường cong (Catmull-Rom Spline) giữ nguyên hình dạng cong mượt cho MỌI dây (kể cả khi bấm chọn chỉnh sửa!),
///   hiển thị chấm tròn tại Waypoints và chấm [+] tại đúng trung điểm t=0.5 trên từng nhịp cong.
/// - Đồng bộ 100% Theme Sáng/Tối (ThemeManager) &amp; Đa ngôn ngữ (LocalizationManager).
/// - Hỗ trợ tương tác Công tắc hành trình, 4 Cảm biến công nghiệp, kim đồng hồ Analog 60 FPS và Undo/Redo snapshot.
/// </summary>
public sealed class WiringCanvasControl : FrameworkElement
{
    public const double GridStep = 20.0;
    private const double MinZoom = 0.35;
    private const double MaxZoom = 2.80;
    private const double TerminalHitRadius = 11.5;
    private const double HandleHitRadius = 9.5;

    private readonly List<ElectricalComponent> _components = new();
    private readonly List<WireModel> _wires = new();
    private readonly List<ComponentHitZone> _hitZones = new();

    private readonly Stopwatch _animWatch = Stopwatch.StartNew();
    private double _lastAnimSeconds;
    private double _dashPhase;
    private double _faultPulsePhase;

    public double ZoomScale { get; private set; } = 1.0;
    public Vector PanOffset { get; private set; } = new(40, 20);

    public AppMode CurrentMode { get; private set; } = AppMode.Design;
    public WireColorPreset ActiveWireColor { get; set; } = WireColorPreset.Red_L1_DCPlus;
    public WireRoutingStyle RoutingStyle { get; private set; } = WireRoutingStyle.Orthogonal;
    public bool SnapToGridEnabled { get; set; } = true;

    private ElectricalComponent? _selectedComponent;
    private ElectricalComponent? _hoveredComponent;
    private ElectricalComponent? _draggingComponent;
    private Vector _dragComponentOffset;

    // Bóng mờ xem trước bán trong suốt (Drag Ghost Preview) khi kéo từ Palette
    private ElectricalComponent? _dragGhostComponent;
    private string? _dragGhostTypeKey;

    private Terminal? _hoveredTerminal;
    private Terminal? _wireStartTerminal;
    private readonly List<Point> _draftWaypoints = new();

    private WireModel? _selectedWire;
    private WireModel? _hoveredWire;
    private int _draggingWaypointIndex = -1;
    private int _draggingParallelSegmentIndex = -1;
    private bool _draggingSegmentHorizontal;

    private Point _currentCanvasMousePos;
    private bool _isPanning;
    private Point _panStartScreenPoint;
    private Vector _panStartOffset;

    private PushButtonComponent? _heldPushButton;
    private IlluminatedPushButtonComponent? _heldIllumPushButton;
    private LimitSwitchComponent? _heldLimitSwitch;
    private IndustrialSensorComponent? _heldSensor;
    private CylinderReedSwitchComponent? _heldReedSwitch;
    private RotaryTimerAh3Component? _draggingAh3Dial;
    private ShortCircuitFaultInfo? _activeFault;

    // Trạng thái bắt dính & tách dây tạo điểm rẽ nhánh T-Junction
    private WireModel? _hoveredBranchWire;
    private Point? _hoveredBranchSnapPoint;
    private int _hoveredBranchSplitIndex;
    private WireJunctionComponent? _pendingCreatedJunction;
    private WireModel? _pendingWireDragBranchSource;
    private Point _pendingWireDragStartCanvasPt;

    public event Action<string>? StatusMessageChanged;
    public event Action<double>? ZoomChanged;
    public event Action? UserInteractedInSimulation;
    public event Action? CircuitModified;
    public event Action? SelectionChanged;
    public event Action<VfdComponent>? OpenVfdKeypadRequested;
    public event Action<PlcS71200Component, bool>? OpenPlcLadderRequested;

    public IReadOnlyList<ElectricalComponent> Components => _components;
    public IReadOnlyList<WireModel> Wires => _wires;
    public bool HasSelection => _selectedComponent is not null || _selectedWire is not null;

    private static readonly Typeface TooltipTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface PlusTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Pen WireHitTestPen = CreateFrozenHitPen();

    private static Pen CreateFrozenHitPen()
    {
        var p = new Pen(Brushes.Black, 13.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    public WiringCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
        AllowDrop = true;

        Loaded += (_, _) =>
        {
            _lastAnimSeconds = _animWatch.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnCompositionRendering;
        };
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
        };
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        double now = _animWatch.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastAnimSeconds, 0.0, 0.08);
        _lastAnimSeconds = now;

        if (CurrentMode == AppMode.Simulation)
        {
            _dashPhase = (_dashPhase - dt * 28.0) % 1000.0;
            _faultPulsePhase = (_faultPulsePhase + dt * 5.5) % (Math.PI * 2.0);

            foreach (var comp in _components)
            {
                comp.UpdateVisualAnimation(dt, isSimulating: true);
            }

            if (_wires.Any(w => w.HasCurrentFlow || w.IsShortCircuitPath) ||
                _components.Any(c => c is Motor1PhaseComponent or Motor3PhaseBaseComponent or VfdComponent or PlcS71200Component or VoltmeterComponent or AmmeterComponent or RotaryTimerAh3Component) ||
                _activeFault is not null)
            {
                InvalidateVisual();
            }
        }
    }

    // =========================================================================
    // QUẢN LÝ MẠCH, CHỌN / XÓA & ZOOM/PAN
    // =========================================================================

    private void AttachComponentHooks(ElectricalComponent comp)
    {
        comp.TerminalsChanged -= OnComponentTerminalsChanged;
        comp.TerminalsChanged += OnComponentTerminalsChanged;
    }

    private void DetachComponentHooks(ElectricalComponent comp)
    {
        comp.TerminalsChanged -= OnComponentTerminalsChanged;
    }

    private void OnComponentTerminalsChanged(ElectricalComponent comp)
    {
        _wires.RemoveAll(w =>
            (w.TerminalA.Owner == comp && !comp.Terminals.Contains(w.TerminalA)) ||
            (w.TerminalB.Owner == comp && !comp.Terminals.Contains(w.TerminalB)));

        if (_selectedWire is not null && !_wires.Contains(_selectedWire))
            SetSelection(null, null);

        RebuildWireRoutes();
        CircuitModified?.Invoke();
        InvalidateVisual();
    }

    private void SetSelection(ElectricalComponent? comp, WireModel? wire)
    {
        bool changed = _selectedComponent != comp || _selectedWire != wire;
        _selectedComponent = comp;
        _selectedWire = wire;
        if (changed) SelectionChanged?.Invoke();
    }

    public void SetMode(AppMode mode)
    {
        CurrentMode = mode;
        CancelDraftWire();
        _dragGhostComponent = null;
        _dragGhostTypeKey = null;
        _draggingComponent = null;
        _draggingWaypointIndex = -1;
        _draggingParallelSegmentIndex = -1;
        ReleaseHeldControls();

        if (mode == AppMode.Design)
        {
            _activeFault = null;
        }
        else
        {
            SetSelection(null, null);
        }
        InvalidateVisual();
    }

    public void SetRoutingStyle(WireRoutingStyle style)
    {
        RoutingStyle = style;
        InvalidateVisual();
    }

    public void SetFaultOverlay(ShortCircuitFaultInfo? fault)
    {
        _activeFault = fault;
        InvalidateVisual();
    }

    public void LoadCircuit(IEnumerable<ElectricalComponent> components, IEnumerable<WireModel> wires, bool autoFit = false, bool raiseModified = true)
    {
        foreach (var old in _components) DetachComponentHooks(old);

        _components.Clear();
        _wires.Clear();
        CancelDraftWire();
        SetSelection(null, null);
        _hoveredComponent = null;
        _hoveredTerminal = null;
        _hoveredWire = null;
        _activeFault = null;

        foreach (var comp in components)
        {
            AttachComponentHooks(comp);
            _components.Add(comp);
        }
        _wires.AddRange(wires);

        RebuildWireRoutes();
        if (autoFit) ResetOrFitView();
        if (raiseModified) CircuitModified?.Invoke();
        InvalidateVisual();
    }

    public void ClearAll(bool raiseModified = true)
    {
        foreach (var old in _components) DetachComponentHooks(old);
        _components.Clear();
        _wires.Clear();
        CancelDraftWire();
        SetSelection(null, null);
        _hoveredTerminal = null;
        _hoveredWire = null;
        _activeFault = null;
        if (raiseModified) CircuitModified?.Invoke();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (CurrentMode != AppMode.Design) return;
        if (_selectedWire is not null) DeleteWire(_selectedWire);
        else if (_selectedComponent is not null) DeleteComponent(_selectedComponent);
    }

    public void AddComponentAtScreenPoint(string typeKey, Point screenPoint)
    {
        if (CurrentMode != AppMode.Design)
        {
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.LockedInSim"));
            return;
        }

        var comp = ComponentRegistry.Create(typeKey);
        Point canvasPt = ScreenToCanvas(screenPoint);
        double x = canvasPt.X - comp.Width / 2.0;
        double y = canvasPt.Y - comp.Height / 2.0;
        if (SnapToGridEnabled)
        {
            x = Snap(x);
            y = Snap(y);
        }

        comp.X = x;
        comp.Y = y;

        var desc = ComponentRegistry.FindDescriptor(typeKey);
        string prefix = desc?.DefaultTagPrefix ?? "D";
        int idx = 1;
        while (_components.Any(c => string.Equals(c.DisplayName, $"{prefix}{idx}", StringComparison.OrdinalIgnoreCase)))
            idx++;
        comp.DisplayName = $"{prefix}{idx}";

        AttachComponentHooks(comp);
        _components.Add(comp);
        SetSelection(comp, null);

        RebuildWireRoutes();
        CircuitModified?.Invoke();
        StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.AddedComp", comp.DisplayName, comp.X, comp.Y));
        InvalidateVisual();

        // Tự động mở trình lập trình Ladder ngay khi người dùng đặt khối PLC S7-1200 vào Canvas!
        if (comp is PlcS71200Component plc)
        {
            OpenPlcLadderRequested?.Invoke(plc, false);
        }
    }

    public void DeleteComponent(ElectricalComponent comp)
    {
        if (CurrentMode != AppMode.Design) return;
        DetachComponentHooks(comp);
        _wires.RemoveAll(w => w.TerminalA.Owner == comp || w.TerminalB.Owner == comp);
        _components.Remove(comp);
        if (_selectedComponent == comp) SetSelection(null, null);
        if (_hoveredComponent == comp) _hoveredComponent = null;
        if (_selectedWire is not null && !_wires.Contains(_selectedWire)) SetSelection(null, null);

        RebuildWireRoutes();
        CleanupRedundantJunctions();
        CircuitModified?.Invoke();
        StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.DeletedComp", comp.DisplayName));
        InvalidateVisual();
    }

    public void DeleteWire(WireModel wire)
    {
        if (CurrentMode != AppMode.Design) return;
        _wires.Remove(wire);
        if (_selectedWire == wire) SetSelection(null, null);
        if (_hoveredWire == wire) _hoveredWire = null;

        CleanupRedundantJunctions();
        RebuildWireRoutes();
        CircuitModified?.Invoke();
        StatusMessageChanged?.Invoke(LocalizationManager.Format(
            "Str.Status.DeletedWire",
            $"{wire.TerminalA.Owner.DisplayName}:{wire.TerminalA.Name}",
            $"{wire.TerminalB.Owner.DisplayName}:{wire.TerminalB.Name}"));
        InvalidateVisual();
    }

    public void ZoomByStep(double factor)
    {
        Point center = new(ActualWidth > 0 ? ActualWidth / 2.0 : 500, ActualHeight > 0 ? ActualHeight / 2.0 : 350);
        ApplyZoomAtScreenPoint(ZoomScale * factor, center);
    }

    public void ResetOrFitView()
    {
        if (_components.Count == 0 || ActualWidth < 50 || ActualHeight < 50)
        {
            ZoomScale = 1.0;
            PanOffset = new Vector(40, 20);
            ZoomChanged?.Invoke(ZoomScale);
            InvalidateVisual();
            return;
        }

        double minX = _components.Min(c => c.GetVisualBounds().Left) - 135;
        double minY = _components.Min(c => c.GetVisualBounds().Top) - 85;
        double maxX = _components.Max(c => c.GetVisualBounds().Right) + 70;
        double maxY = _components.Max(c => c.GetVisualBounds().Bottom) + 75;

        double contentW = Math.Max(200, maxX - minX);
        double contentH = Math.Max(200, maxY - minY);

        double scaleX = ActualWidth / contentW;
        double scaleY = ActualHeight / contentH;
        ZoomScale = Math.Clamp(Math.Min(scaleX, scaleY), 0.45, 1.15);

        PanOffset = new Vector(
            (ActualWidth - contentW * ZoomScale) / 2.0 - minX * ZoomScale,
            (ActualHeight - contentH * ZoomScale) / 2.0 - minY * ZoomScale + 14);

        ZoomChanged?.Invoke(ZoomScale);
        InvalidateVisual();
    }

    public void RebuildWireRoutes()
    {
        WireRouter.RouteAllWires(_wires, _components);
    }

    // =========================================================================
    // KÉO THẢ CÓ BÓNG MỜ (VISUAL DRAG GHOST PREVIEW)
    // =========================================================================

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (CurrentMode == AppMode.Design &&
            e.Data.GetData("WiringSim.ComponentTypeKey") is string typeKey)
        {
            e.Effects = DragDropEffects.Copy;

            if (_dragGhostComponent is null || !string.Equals(_dragGhostTypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
            {
                _dragGhostTypeKey = typeKey;
                _dragGhostComponent = ComponentRegistry.Create(typeKey);
            }

            Point canvasPt = ScreenToCanvas(e.GetPosition(this));
            double gx = canvasPt.X - _dragGhostComponent.Width / 2.0;
            double gy = canvasPt.Y - _dragGhostComponent.Height / 2.0;
            if (SnapToGridEnabled)
            {
                gx = Snap(gx);
                gy = Snap(gy);
            }

            if (Math.Abs(gx - _dragGhostComponent.X) > 0.1 || Math.Abs(gy - _dragGhostComponent.Y) > 0.1)
            {
                _dragGhostComponent.X = gx;
                _dragGhostComponent.Y = gy;
            }
            InvalidateVisual();
        }
        else
        {
            e.Effects = DragDropEffects.None;
            _dragGhostComponent = null;
            _dragGhostTypeKey = null;
        }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dragGhostComponent = null;
        _dragGhostTypeKey = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        _dragGhostComponent = null;
        _dragGhostTypeKey = null;

        if (CurrentMode == AppMode.Design &&
            e.Data.GetData("WiringSim.ComponentTypeKey") is string typeKey)
        {
            AddComponentAtScreenPoint(typeKey, e.GetPosition(this));
        }
        else
        {
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // =========================================================================
    // SỰ KIỆN CHUỘT, WAYPOINTS, CONTROL HANDLES & MENU CHUỘT PHẢI
    // =========================================================================

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        Point canvasPt = ScreenToCanvas(e.GetPosition(this));
        var hz = FindHitZone(canvasPt);
        if (hz is { ActionId: "AH3_DIAL", Component: RotaryTimerAh3Component ah3 })
        {
            ah3.StepDial(e.Delta > 0 ? +1 : -1);
            if (CurrentMode == AppMode.Simulation)
                UserInteractedInSimulation?.Invoke();
            else
                CircuitModified?.Invoke();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.UpdatedProp", ah3.DisplayName, ah3.FormatDuration(ah3.DelaySeconds)));
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (hz is { ActionId: "CAM_PREV" or "CAM_NEXT", Component: CamSwitchComponent cam })
        {
            cam.StepPosition(e.Delta > 0 ? +1 : -1);
            if (CurrentMode == AppMode.Simulation)
                UserInteractedInSimulation?.Invoke();
            else
                CircuitModified?.Invoke();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.UpdatedProp", cam.DisplayName, cam.CurrentPositionLabel));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        double factor = e.Delta > 0 ? 1.12 : (1.0 / 1.12);
        ApplyZoomAtScreenPoint(ZoomScale * factor, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        Point screenPt = e.GetPosition(this);
        Point canvasPt = ScreenToCanvas(screenPt);
        _currentCanvasMousePos = canvasPt;

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            StartPanning(screenPt);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            HandleLeftMouseDown(screenPt, canvasPt, e.ClickCount);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            HandleRightMouseDown(canvasPt);
            e.Handled = true;
        }
    }

    private void HandleLeftMouseDown(Point screenPt, Point canvasPt, int clickCount)
    {
        if (CurrentMode == AppMode.Design && _wireStartTerminal is not null)
        {
            var destTerm = FindTerminalAt(canvasPt);
            if (destTerm is not null)
            {
                CompleteOrCancelWireOnTerminal(destTerm);
                return;
            }

            // Nếu người dùng bấm vào một điểm nằm trên thân một sợi dây đã có -> tạo T-Junction và nối nhánh ngay!
            if (TryFindWireBranchSnap(canvasPt, out var targetWire, out Point snapPt, out int splitIdx))
            {
                var junc = SplitWireAtPoint(targetWire, snapPt, splitIdx);
                CompleteOrCancelWireOnTerminal(junc.NodeTerminal);
                StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.BranchCreated"));
                return;
            }

            bool shift = IsShiftDown();
            Point wp = RoutingStyle == WireRoutingStyle.Curved
                ? SnapIfEnabled(canvasPt, shift)
                : ComputeConstrainedDraftPoint(canvasPt, shift);

            if (_draftWaypoints.Count == 0 || (wp - _draftWaypoints[^1]).Length > 2.0)
            {
                _draftWaypoints.Add(wp);
                StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.WaypointAdded", _draftWaypoints.Count, wp.X, wp.Y));
                InvalidateVisual();
            }
            return;
        }

        var hitZone = FindHitZone(canvasPt);
        if (hitZone.HasValue && (CurrentMode == AppMode.Simulation || hitZone.Value.AllowedInDesignMode))
        {
            ExecuteHitZoneAction(hitZone.Value);
            return;
        }

        if (CurrentMode == AppMode.Design)
        {
            if (_selectedWire is not null && TryBeginDragWireControlHandle(_selectedWire, canvasPt))
            {
                CaptureMouse();
                InvalidateVisual();
                return;
            }

            var term = FindTerminalAt(canvasPt);
            if (term is not null)
            {
                SetSelection(null, null);
                _wireStartTerminal = term;
                _draftWaypoints.Clear();
                StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.WireStarted", term.Owner.DisplayName, term.Name));
                InvalidateVisual();
                return;
            }

            var clickedWire = FindWireAt(canvasPt);
            if (clickedWire is not null)
            {
                // Ctrl + Click trên thân dây: tạo ngay nút rẽ nhánh T-Junction và bắt đầu kéo nhánh mới!
                if (Keyboard.Modifiers == ModifierKeys.Control && clickCount == 1)
                {
                    StartBranchFromWire(clickedWire, canvasPt);
                    return;
                }

                SetSelection(null, clickedWire);

                if (clickCount >= 2)
                {
                    InsertWaypointOnWire(clickedWire, canvasPt);
                }
                else
                {
                    // Cho phép kéo chuột trực tiếp từ thân dây (> 8px) để tự động rẽ nhánh dây mới!
                    _pendingWireDragBranchSource = clickedWire;
                    _pendingWireDragStartCanvasPt = canvasPt;
                    StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.WireSelected"));
                }
                InvalidateVisual();
                return;
            }

            var comp = FindComponentAt(canvasPt);
            if (comp is not null)
            {
                SetSelection(comp, null);
                _draggingComponent = comp;
                _dragComponentOffset = new Vector(canvasPt.X - comp.X, canvasPt.Y - comp.Y);
                CaptureMouse();
                StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.CompSelected", comp.DisplayName));
                InvalidateVisual();
                return;
            }

            SetSelection(null, null);
            StartPanning(screenPt);
            InvalidateVisual();
        }
        else
        {
            var comp = FindComponentAt(canvasPt);
            if (comp is ICircuitBreaker mcb)
            {
                mcb.ToggleManual();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is SwitchComponent sw)
            {
                sw.ToggleOrStep();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is PushButtonComponent pb)
            {
                if (pb.ActionMode == PushButtonActionMode.Latching)
                    pb.IsPressed = !pb.IsPressed;
                else
                {
                    _heldPushButton = pb;
                    pb.IsPressed = true;
                    CaptureMouse();
                }
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is LimitSwitchComponent ls)
            {
                if (ls.ActionMode == PushButtonActionMode.Latching)
                    ls.IsActuated = !ls.IsActuated;
                else
                {
                    _heldLimitSwitch = ls;
                    ls.IsActuated = true;
                    CaptureMouse();
                }
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is IndustrialSensorComponent sens)
            {
                if (sens.TriggerMode == PushButtonActionMode.Latching)
                    sens.IsTargetDetected = !sens.IsTargetDetected;
                else
                {
                    _heldSensor = sens;
                    sens.IsTargetDetected = true;
                    CaptureMouse();
                }
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is CylinderReedSwitchComponent reed)
            {
                if (reed.TriggerMode == PushButtonActionMode.Latching)
                    reed.IsMagnetDetected = !reed.IsMagnetDetected;
                else
                {
                    _heldReedSwitch = reed;
                    reed.IsMagnetDetected = true;
                    CaptureMouse();
                }
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is ThermalSwitchComponent thermSw)
            {
                thermSw.ToggleOverheat();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is FloatSwitchComponent floatSw)
            {
                floatSw.ToggleWaterLevel();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is IlluminatedPushButtonComponent ipb)
            {
                if (ipb.ActionMode == PushButtonActionMode.Latching)
                    ipb.IsPressed = !ipb.IsPressed;
                else
                {
                    _heldIllumPushButton = ipb;
                    ipb.IsPressed = true;
                    CaptureMouse();
                }
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is EmergencyStopComponent es)
            {
                es.ToggleEngage();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is KnifeSwitchComponent knife)
            {
                knife.ToggleOrStep();
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }
            if (comp is CamSwitchComponent cam)
            {
                cam.StepPosition(+1);
                UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
                return;
            }

            StartPanning(screenPt);
        }
    }

    private void CompleteOrCancelWireOnTerminal(Terminal clickedTerminal)
    {
        if (_wireStartTerminal is null) return;

        if (_wireStartTerminal == clickedTerminal)
        {
            CancelDraftWire();
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.WireCanceled"));
            InvalidateVisual();
            return;
        }

        var existing = _wires.FirstOrDefault(w => w.Connects(_wireStartTerminal, clickedTerminal));
        if (existing is not null)
        {
            existing.ColorPreset = ActiveWireColor;
            CancelDraftWire();
            CircuitModified?.Invoke();
            InvalidateVisual();
            return;
        }

        List<Point>? customWaypoints = null;
        if (_draftWaypoints.Count > 0)
        {
            customWaypoints = new List<Point>(_draftWaypoints);
            Point pEnd = new(clickedTerminal.CanvasX, clickedTerminal.CanvasY);
            Point lastWp = customWaypoints[^1];

            if (RoutingStyle == WireRoutingStyle.Orthogonal &&
                !IsShiftDown() &&
                Math.Abs(lastWp.X - pEnd.X) > 1.0 && Math.Abs(lastWp.Y - pEnd.Y) > 1.0)
            {
                customWaypoints.Add(clickedTerminal.EffectiveSide is TerminalSide.Top or TerminalSide.Bottom
                    ? new Point(pEnd.X, lastWp.Y)
                    : new Point(lastWp.X, pEnd.Y));
            }
        }

        var newWire = new WireModel(_wireStartTerminal, clickedTerminal, ActiveWireColor, customWaypoints);
        _wires.Add(newWire);
        SetSelection(null, newWire);

        string msg = LocalizationManager.Format(
            "Str.Status.WireCompleted",
            _wireStartTerminal.Owner.DisplayName,
            _wireStartTerminal.Name,
            clickedTerminal.Owner.DisplayName,
            clickedTerminal.Name);

        _pendingCreatedJunction = null;
        CancelDraftWire();
        RebuildWireRoutes();
        CircuitModified?.Invoke();
        StatusMessageChanged?.Invoke(msg);
        InvalidateVisual();
    }

    private void HandleRightMouseDown(Point canvasPt)
    {
        if (_wireStartTerminal is not null)
        {
            CancelDraftWire();
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.WireCanceled"));
            InvalidateVisual();
            return;
        }

        Window? ownerWin = Window.GetWindow(this);

        var wire = FindWireAt(canvasPt);
        if (wire is not null)
        {
            SetSelection(null, wire);
            InvalidateVisual();

            Point rightClickPt = canvasPt;
            var wireMenu = ContextMenuBuilder.BuildWireMenu(
                wire,
                CurrentMode,
                onChangeColor: newCol =>
                {
                    wire.ColorPreset = newCol;
                    if (wire.TerminalA.Owner is WireJunctionComponent jA) jA.DotColorPreset = newCol;
                    if (wire.TerminalB.Owner is WireJunctionComponent jB) jB.DotColorPreset = newCol;
                    CircuitModified?.Invoke();
                    InvalidateVisual();
                },
                onAddWaypointHere: () => InsertWaypointOnWire(wire, rightClickPt),
                onRemoveNearestWaypoint: () => RemoveNearestWaypointOnWire(wire, rightClickPt),
                onResetAutoRoute: () =>
                {
                    wire.ClearManualWaypoints();
                    RebuildWireRoutes();
                    CircuitModified?.Invoke();
                    InvalidateVisual();
                },
                onDeleteWire: () => DeleteWire(wire),
                onStartBranchHere: () => StartBranchFromWire(wire, rightClickPt));

            wireMenu.PlacementTarget = this;
            wireMenu.IsOpen = true;
            return;
        }

        var comp = FindComponentAt(canvasPt);
        if (comp is not null)
        {
            if (CurrentMode == AppMode.Design)
            {
                SetSelection(comp, null);
                InvalidateVisual();
            }

            var compMenu = ContextMenuBuilder.BuildComponentMenu(
                comp,
                CurrentMode,
                ownerWin,
                onChangedMessage: msg =>
                {
                    RebuildWireRoutes();
                    if (CurrentMode == AppMode.Simulation)
                        UserInteractedInSimulation?.Invoke();
                    else
                        CircuitModified?.Invoke();

                    StatusMessageChanged?.Invoke(msg);
                    InvalidateVisual();
                },
                onDeleteRequested: () => DeleteComponent(comp),
                onOpenVfdKeypad: vfd => OpenVfdKeypadRequested?.Invoke(vfd),
                onOpenPlcLadder: (plc, openTags) => OpenPlcLadderRequested?.Invoke(plc, openTags));

            compMenu.PlacementTarget = this;
            compMenu.IsOpen = true;
        }
    }

    // =========================================================================
    // KÉO NẮN CONTROL HANDLES (GIỮ NGUYÊN ĐƯỜNG CONG MƯỢT TRONG CHẾ ĐỘ CURVED!)
    // =========================================================================

    private bool TryBeginDragWireControlHandle(WireModel wire, Point canvasPt)
    {
        double hitRadius = HandleHitRadius / Math.Max(0.6, ZoomScale);

        if (RoutingStyle == WireRoutingStyle.Curved)
        {
            // 1. Kiểm tra chấm tròn tại các Waypoints hiện có của dây cong
            for (int i = 0; i < wire.Waypoints.Count; i++)
            {
                if ((wire.Waypoints[i] - canvasPt).Length <= hitRadius)
                {
                    _draggingWaypointIndex = i;
                    _draggingParallelSegmentIndex = -1;
                    return true;
                }
            }

            // 2. Kiểm tra chấm [+] tại trung điểm t=0.5 trên từng nhịp cong Catmull-Rom
            var anchors = WireRouter.GetEffectiveAnchorPoints(wire, WireRoutingStyle.Curved);
            var spans = WireRouter.ComputeSmoothCurveSpans(anchors, wire.TerminalA.EffectiveSide, wire.TerminalB.EffectiveSide, _wires.IndexOf(wire));
            for (int s = 0; s < spans.Count; s++)
            {
                if ((spans[s].MidPoint - canvasPt).Length <= hitRadius)
                {
                    Point insertPt = SnapIfEnabled(spans[s].MidPoint, IsShiftDown());
                    wire.Waypoints.Insert(s, insertPt);
                    wire.SyncRoutedPointsFromWaypoints();
                    _draggingWaypointIndex = s;
                    _draggingParallelSegmentIndex = -1;
                    return true;
                }
            }

            return false;
        }

        // Chế độ Vuông góc (Orthogonal)
        var pts = wire.RoutedPoints;
        if (pts.Count < 2) return false;

        for (int i = 1; i < pts.Count - 1; i++)
        {
            if ((pts[i] - canvasPt).Length <= hitRadius)
            {
                wire.EnsureEditableWaypoints();
                _draggingWaypointIndex = i - 1;
                _draggingParallelSegmentIndex = -1;
                return true;
            }
        }

        for (int s = 0; s < pts.Count - 1; s++)
        {
            Point mid = new((pts[s].X + pts[s + 1].X) / 2.0, (pts[s].Y + pts[s + 1].Y) / 2.0);
            if ((mid - canvasPt).Length <= hitRadius)
            {
                wire.EnsureEditableWaypoints();

                bool isInteriorSegment = s >= 1 && s <= wire.Waypoints.Count - 1;
                bool isHoriz = Math.Abs(pts[s].Y - pts[s + 1].Y) < 1.0;
                bool isVert = Math.Abs(pts[s].X - pts[s + 1].X) < 1.0;

                if (isInteriorSegment && (isHoriz || isVert) && !IsShiftDown())
                {
                    _draggingParallelSegmentIndex = s - 1;
                    _draggingSegmentHorizontal = isHoriz;
                    _draggingWaypointIndex = -1;
                }
                else
                {
                    Point insertPt = SnapIfEnabled(mid, IsShiftDown());
                    wire.Waypoints.Insert(s, insertPt);
                    wire.SyncRoutedPointsFromWaypoints();
                    _draggingWaypointIndex = s;
                    _draggingParallelSegmentIndex = -1;
                }
                return true;
            }
        }

        return false;
    }

    private void InsertWaypointOnWire(WireModel wire, Point clickCanvasPt)
    {
        if (RoutingStyle == WireRoutingStyle.Curved)
        {
            int spanIdx = WireRouter.FindNearestSpanIndex(clickCanvasPt, wire, WireRoutingStyle.Curved, out Point onCurve);
            wire.Waypoints.Insert(Math.Clamp(spanIdx, 0, wire.Waypoints.Count), SnapIfEnabled(onCurve, IsShiftDown()));
            wire.SyncRoutedPointsFromWaypoints();
        }
        else
        {
            wire.EnsureEditableWaypoints();
            wire.SyncRoutedPointsFromWaypoints();
            int segIdx = WireRouter.FindNearestSegmentIndex(clickCanvasPt, wire.RoutedPoints, out Point proj);
            wire.Waypoints.Insert(Math.Clamp(segIdx, 0, wire.Waypoints.Count), SnapIfEnabled(proj, IsShiftDown()));
            wire.SyncRoutedPointsFromWaypoints();
        }

        CircuitModified?.Invoke();
        InvalidateVisual();
    }

    private void RemoveNearestWaypointOnWire(WireModel wire, Point refPt)
    {
        if (wire.Waypoints.Count == 0) return;

        int bestIdx = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < wire.Waypoints.Count; i++)
        {
            double d = (wire.Waypoints[i] - refPt).Length;
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }

        wire.Waypoints.RemoveAt(bestIdx);
        RebuildWireRoutes();
        CircuitModified?.Invoke();
        InvalidateVisual();
    }

    private void ExecuteHitZoneAction(ComponentHitZone zone)
    {
        switch (zone.ActionId)
        {
            case "DC_MINUS" when zone.Component is DcSourceComponent dc:
                dc.StepVoltage(-1);
                break;
            case "DC_PLUS" when zone.Component is DcSourceComponent dc:
                dc.StepVoltage(+1);
                break;
            case "DC_CYCLE" when zone.Component is DcSourceComponent dc:
                dc.CycleStandardVoltage();
                break;
            case "TIMER_MINUS" when zone.Component is TimerOnDelayComponent tm:
                tm.AdjustDelay(-1.0);
                break;
            case "TIMER_PLUS" when zone.Component is TimerOnDelayComponent tm:
                tm.AdjustDelay(+1.0);
                break;
            case "THERMAL_TEST_TRIP" when zone.Component is ThermalOverloadRelayComponent rn:
                rn.TestTrip();
                break;
            case "THERMAL_RESET" when zone.Component is ThermalOverloadRelayComponent rn:
                rn.ManualResetAction();
                break;
            case "MCB_TOGGLE" when zone.Component is ICircuitBreaker cb:
                cb.ToggleManual();
                break;
            case "SW_STEP" when zone.Component is SwitchComponent sw:
                sw.ToggleOrStep();
                break;
            case "SW_ROT_1" when zone.Component is SwitchComponent sw:
                sw.SetRotaryPosition(1);
                break;
            case "SW_ROT_0" when zone.Component is SwitchComponent sw:
                sw.SetRotaryPosition(0);
                break;
            case "SW_ROT_2" when zone.Component is SwitchComponent sw:
                sw.SetRotaryPosition(2);
                break;
            case "PUSHBUTTON_LATCH" when zone.Component is PushButtonComponent pb:
                pb.IsPressed = !pb.IsPressed;
                break;
            case "PUSHBUTTON_HOLD" when zone.Component is PushButtonComponent pb:
                _heldPushButton = pb;
                pb.IsPressed = true;
                CaptureMouse();
                break;
            case "LIMIT_TOGGLE" when zone.Component is LimitSwitchComponent ls:
                ls.IsActuated = !ls.IsActuated;
                break;
            case "LIMIT_HOLD" when zone.Component is LimitSwitchComponent ls:
                _heldLimitSwitch = ls;
                ls.IsActuated = true;
                CaptureMouse();
                break;
            case "SENSOR_TOGGLE" when zone.Component is IndustrialSensorComponent sens:
                sens.IsTargetDetected = !sens.IsTargetDetected;
                break;
            case "SENSOR_HOLD" when zone.Component is IndustrialSensorComponent sens:
                _heldSensor = sens;
                sens.IsTargetDetected = true;
                CaptureMouse();
                break;
            case "REED_TOGGLE" when zone.Component is CylinderReedSwitchComponent reed:
                reed.IsMagnetDetected = !reed.IsMagnetDetected;
                break;
            case "REED_HOLD" when zone.Component is CylinderReedSwitchComponent reed:
                _heldReedSwitch = reed;
                reed.IsMagnetDetected = true;
                CaptureMouse();
                break;
            case "THERMAL_SW_TOGGLE" when zone.Component is ThermalSwitchComponent thermSw:
                thermSw.ToggleOverheat();
                break;
            case "FLOAT_SW_TOGGLE" when zone.Component is FloatSwitchComponent floatSw:
                floatSw.ToggleWaterLevel();
                break;
            case "VFD_KEYPAD" when zone.Component is VfdComponent vfd:
                OpenVfdKeypadRequested?.Invoke(vfd);
                return;
            case "PLC_OPEN_LADDER" when zone.Component is PlcS71200Component plc:
                OpenPlcLadderRequested?.Invoke(plc, false);
                return;
            case "ILLUM_PB_LATCH" when zone.Component is IlluminatedPushButtonComponent ipb:
                ipb.IsPressed = !ipb.IsPressed;
                break;
            case "ILLUM_PB_HOLD" when zone.Component is IlluminatedPushButtonComponent ipb:
                _heldIllumPushButton = ipb;
                ipb.IsPressed = true;
                CaptureMouse();
                break;
            case "ESTOP_TOGGLE" when zone.Component is EmergencyStopComponent es:
                es.ToggleEngage();
                break;
            case "KNIFE_TOGGLE" when zone.Component is KnifeSwitchComponent knife:
                knife.ToggleOrStep();
                break;
            case "KNIFE_POS_1" when zone.Component is KnifeSwitchComponent knife:
                knife.Position = 1;
                break;
            case "KNIFE_POS_0" when zone.Component is KnifeSwitchComponent knife:
                knife.Position = 0;
                break;
            case "KNIFE_POS_2" when zone.Component is KnifeSwitchComponent knife:
                knife.Position = 2;
                break;
            case "CAM_PREV" when zone.Component is CamSwitchComponent cam:
                cam.StepPosition(-1);
                break;
            case "CAM_NEXT" when zone.Component is CamSwitchComponent cam:
                cam.StepPosition(+1);
                break;
            case "AH3_DIAL" when zone.Component is RotaryTimerAh3Component ah3:
                _draggingAh3Dial = ah3;
                CaptureMouse();
                UpdateAh3DialFromCanvasPoint(ah3, _currentCanvasMousePos);
                break;
        }

        if (CurrentMode == AppMode.Simulation)
            UserInteractedInSimulation?.Invoke();
        else
            CircuitModified?.Invoke();

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point screenPt = e.GetPosition(this);
        Point canvasPt = ScreenToCanvas(screenPt);
        _currentCanvasMousePos = canvasPt;

        if (_isPanning)
        {
            PanOffset = _panStartOffset + (screenPt - _panStartScreenPoint);
            InvalidateVisual();
            return;
        }

        if (_draggingAh3Dial is not null)
        {
            UpdateAh3DialFromCanvasPoint(_draggingAh3Dial, canvasPt);
            if (CurrentMode == AppMode.Simulation)
                UserInteractedInSimulation?.Invoke();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.UpdatedProp", _draggingAh3Dial.DisplayName, _draggingAh3Dial.FormatDuration(_draggingAh3Dial.DelaySeconds)));
            InvalidateVisual();
            return;
        }

        if (CurrentMode == AppMode.Design && _pendingWireDragBranchSource is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((canvasPt - _pendingWireDragStartCanvasPt).Length >= 8.0)
            {
                var srcWire = _pendingWireDragBranchSource;
                Point startPt = _pendingWireDragStartCanvasPt;
                _pendingWireDragBranchSource = null;
                StartBranchFromWire(srcWire, startPt);
                return;
            }
        }

        if (CurrentMode == AppMode.Design && _selectedWire is not null)
        {
            if (_draggingWaypointIndex >= 0 && _draggingWaypointIndex < _selectedWire.Waypoints.Count)
            {
                _selectedWire.Waypoints[_draggingWaypointIndex] = SnapIfEnabled(canvasPt, IsShiftDown());
                _selectedWire.SyncRoutedPointsFromWaypoints();
                InvalidateVisual();
                return;
            }

            if (_draggingParallelSegmentIndex >= 0 && _draggingParallelSegmentIndex + 1 < _selectedWire.Waypoints.Count)
            {
                Point snapped = SnapIfEnabled(canvasPt, IsShiftDown());
                int idxA = _draggingParallelSegmentIndex;
                int idxB = _draggingParallelSegmentIndex + 1;
                if (_draggingSegmentHorizontal)
                {
                    _selectedWire.Waypoints[idxA] = new Point(_selectedWire.Waypoints[idxA].X, snapped.Y);
                    _selectedWire.Waypoints[idxB] = new Point(_selectedWire.Waypoints[idxB].X, snapped.Y);
                }
                else
                {
                    _selectedWire.Waypoints[idxA] = new Point(snapped.X, _selectedWire.Waypoints[idxA].Y);
                    _selectedWire.Waypoints[idxB] = new Point(snapped.X, _selectedWire.Waypoints[idxB].Y);
                }
                _selectedWire.SyncRoutedPointsFromWaypoints();
                InvalidateVisual();
                return;
            }
        }

        if (_draggingComponent is not null && CurrentMode == AppMode.Design)
        {
            double nx = canvasPt.X - _dragComponentOffset.X;
            double ny = canvasPt.Y - _dragComponentOffset.Y;
            if (SnapToGridEnabled) { nx = Snap(nx); ny = Snap(ny); }

            if (Math.Abs(nx - _draggingComponent.X) > 0.1 || Math.Abs(ny - _draggingComponent.Y) > 0.1)
            {
                _draggingComponent.X = nx;
                _draggingComponent.Y = ny;
                RebuildWireRoutes();
                InvalidateVisual();
            }
            return;
        }

        var prevTerm = _hoveredTerminal;
        var prevWire = _hoveredWire;
        var prevComp = _hoveredComponent;
        var prevSnap = _hoveredBranchSnapPoint;

        _hoveredTerminal = FindTerminalAt(canvasPt);
        _hoveredWire = _hoveredTerminal is null ? FindWireAt(canvasPt) : null;
        _hoveredComponent = FindComponentAt(canvasPt);

        _hoveredBranchWire = null;
        _hoveredBranchSnapPoint = null;
        if (CurrentMode == AppMode.Design && _hoveredTerminal is null &&
            (_wireStartTerminal is not null || Keyboard.Modifiers == ModifierKeys.Control))
        {
            if (TryFindWireBranchSnap(canvasPt, out var bw, out Point sp, out int sIdx))
            {
                _hoveredBranchWire = bw;
                _hoveredBranchSnapPoint = sp;
                _hoveredBranchSplitIndex = sIdx;
            }
        }

        UpdateCursorShape(canvasPt);

        if (_wireStartTerminal is not null || prevTerm != _hoveredTerminal || prevWire != _hoveredWire || prevComp != _hoveredComponent || prevSnap != _hoveredBranchSnapPoint)
            InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _isPanning = false;
            ReleaseMouseCapture();
            UpdateCursorShape(_currentCanvasMousePos);
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _pendingWireDragBranchSource = null;

            if (_draggingAh3Dial is not null)
            {
                _draggingAh3Dial = null;
                ReleaseMouseCapture();
                if (CurrentMode == AppMode.Design)
                    CircuitModified?.Invoke();
                else
                    UserInteractedInSimulation?.Invoke();
                InvalidateVisual();
            }

            if (_draggingWaypointIndex >= 0 || _draggingParallelSegmentIndex >= 0)
            {
                _draggingWaypointIndex = -1;
                _draggingParallelSegmentIndex = -1;
                ReleaseMouseCapture();
                CircuitModified?.Invoke();
                InvalidateVisual();
            }

            if (_draggingComponent is not null)
            {
                _draggingComponent = null;
                ReleaseMouseCapture();
                CircuitModified?.Invoke();
                InvalidateVisual();
            }

            if (_heldPushButton is not null || _heldIllumPushButton is not null || _heldLimitSwitch is not null || _heldSensor is not null || _heldReedSwitch is not null)
            {
                ReleaseHeldControls();
                ReleaseMouseCapture();
            }
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isPanning = false;
        _draggingComponent = null;
        _draggingAh3Dial = null;
        _pendingWireDragBranchSource = null;
        _draggingWaypointIndex = -1;
        _draggingParallelSegmentIndex = -1;
        ReleaseHeldControls();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && _wireStartTerminal is not null)
        {
            CancelDraftWire();
            StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.WireCanceled"));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && _wireStartTerminal is not null && _draftWaypoints.Count > 0)
        {
            _draftWaypoints.RemoveAt(_draftWaypoints.Count - 1);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.R && CurrentMode == AppMode.Design &&
            Keyboard.Modifiers == ModifierKeys.None && _selectedComponent is not null)
        {
            _selectedComponent.RotateCW();
            RebuildWireRoutes();
            CircuitModified?.Invoke();
            StatusMessageChanged?.Invoke(LocalizationManager.Format("Str.Status.UpdatedProp", _selectedComponent.DisplayName, $"{_selectedComponent.RotationAngle}°"));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && CurrentMode == AppMode.Design)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void CancelDraftWire()
    {
        _wireStartTerminal = null;
        _draftWaypoints.Clear();
        _hoveredBranchWire = null;
        _hoveredBranchSnapPoint = null;
        if (_pendingCreatedJunction is not null)
        {
            _pendingCreatedJunction = null;
            CleanupRedundantJunctions();
            RebuildWireRoutes();
        }
    }

    private void ReleaseHeldControls()
    {
        bool changed = false;
        if (_heldPushButton is not null)
        {
            if (_heldPushButton.ActionMode == PushButtonActionMode.Momentary)
            {
                _heldPushButton.IsPressed = false;
                changed = true;
            }
            _heldPushButton = null;
        }
        if (_heldIllumPushButton is not null)
        {
            if (_heldIllumPushButton.ActionMode == PushButtonActionMode.Momentary)
            {
                _heldIllumPushButton.IsPressed = false;
                changed = true;
            }
            _heldIllumPushButton = null;
        }
        if (_heldLimitSwitch is not null)
        {
            if (_heldLimitSwitch.ActionMode == PushButtonActionMode.Momentary)
            {
                _heldLimitSwitch.IsActuated = false;
                changed = true;
            }
            _heldLimitSwitch = null;
        }
        if (_heldSensor is not null)
        {
            if (_heldSensor.TriggerMode == PushButtonActionMode.Momentary)
            {
                _heldSensor.IsTargetDetected = false;
                changed = true;
            }
            _heldSensor = null;
        }
        if (_heldReedSwitch is not null)
        {
            if (_heldReedSwitch.TriggerMode == PushButtonActionMode.Momentary)
            {
                _heldReedSwitch.IsMagnetDetected = false;
                changed = true;
            }
            _heldReedSwitch = null;
        }

        if (changed)
        {
            UserInteractedInSimulation?.Invoke();
            InvalidateVisual();
        }
    }

    private void StartPanning(Point screenPt)
    {
        _isPanning = true;
        _panStartScreenPoint = screenPt;
        _panStartOffset = PanOffset;
        Cursor = Cursors.ScrollAll;
        CaptureMouse();
    }

    private void ApplyZoomAtScreenPoint(double newScale, Point screenPivot)
    {
        double clamped = Math.Clamp(newScale, MinZoom, MaxZoom);
        Point beforeCanvas = ScreenToCanvas(screenPivot);
        ZoomScale = clamped;
        PanOffset = new Vector(
            screenPivot.X - beforeCanvas.X * ZoomScale,
            screenPivot.Y - beforeCanvas.Y * ZoomScale);

        ZoomChanged?.Invoke(ZoomScale);
        InvalidateVisual();
    }

    private void UpdateCursorShape(Point canvasPt)
    {
        if (_isPanning) { Cursor = Cursors.ScrollAll; return; }
        if (CurrentMode == AppMode.Design && _selectedWire is not null && IsPointNearAnyWireHandle(_selectedWire, canvasPt))
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        if (_hoveredTerminal is not null || _wireStartTerminal is not null) { Cursor = Cursors.Cross; return; }
        if (FindHitZone(canvasPt) is { } hz && (CurrentMode == AppMode.Simulation || hz.AllowedInDesignMode))
        {
            Cursor = Cursors.Hand;
            return;
        }
        if (CurrentMode == AppMode.Design && (_hoveredComponent is not null || _hoveredWire is not null))
        {
            Cursor = Cursors.Hand;
            return;
        }
        if (CurrentMode == AppMode.Simulation && _hoveredComponent is ICircuitBreaker or SwitchComponent or PushButtonComponent or IlluminatedPushButtonComponent or EmergencyStopComponent or KnifeSwitchComponent or CamSwitchComponent or RotaryTimerAh3Component or LimitSwitchComponent or IndustrialSensorComponent or CylinderReedSwitchComponent or ThermalSwitchComponent or FloatSwitchComponent)
        {
            Cursor = Cursors.Hand;
            return;
        }
        Cursor = Cursors.Arrow;
    }

    // =========================================================================
    // KẾT XUẤT ĐỒ HỌA VECTOR (THEME SÁNG/TỐI, CURVED HANDLES, DRAG GHOST)
    // =========================================================================

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        var theme = ThemeManager.CanvasColors;
        double dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRectangle(ComponentRenderer.CreateSolid(theme.CanvasBgHex), null, new Rect(0, 0, w, h));

        var matrix = new Matrix();
        matrix.Scale(ZoomScale, ZoomScale);
        matrix.Translate(PanOffset.X, PanOffset.Y);
        var matrixTransform = new MatrixTransform(matrix);
        matrixTransform.Freeze();

        dc.PushTransform(matrixTransform);

        DrawDotGrid(dc, w, h, theme);

        for (int i = 0; i < _wires.Count; i++)
        {
            var wire = _wires[i];
            DrawWire(dc, wire, i, isHovered: wire == _hoveredWire, isSelected: wire == _selectedWire, theme);
        }

        if (CurrentMode == AppMode.Design && _selectedWire is not null)
        {
            DrawWireControlHandles(dc, _selectedWire, dpiScale);
        }

        if (CurrentMode == AppMode.Design && _wireStartTerminal is not null)
        {
            DrawDraftPolylinePreview(dc);
        }

        if (CurrentMode == AppMode.Design && _hoveredBranchSnapPoint.HasValue)
        {
            Point sp = _hoveredBranchSnapPoint.Value;
            dc.DrawEllipse(ComponentRenderer.CreateRadialGlow("#AA22D3EE", "#0022D3EE"), null, sp, 14, 14);
            dc.DrawEllipse(ComponentRenderer.CreateSolid("#22D3EE"), ComponentRenderer.CreatePen("#FFFFFF", 1.8), sp, 5.5, 5.5);
        }

        _hitZones.Clear();
        foreach (var comp in _components)
        {
            ComponentRenderer.DrawComponent(
                dc,
                comp,
                isSelected: comp == _selectedComponent,
                isHovered: comp == _hoveredComponent,
                hoveredTerminal: _hoveredTerminal,
                activeWireStartTerminal: _wireStartTerminal,
                isSimulating: CurrentMode == AppMode.Simulation,
                dpiScale: dpiScale,
                hitZones: _hitZones);
        }

        // Bóng mờ bán trong suốt (Visual Drag Ghost) bắt dính lưới khi kéo thiết bị từ Thư viện!
        if (_dragGhostComponent is not null && CurrentMode == AppMode.Design)
        {
            dc.PushOpacity(0.62);
            var dummyZones = new List<ComponentHitZone>();
            ComponentRenderer.DrawComponent(
                dc, _dragGhostComponent, isSelected: true, isHovered: false,
                null, null, isSimulating: false, dpiScale: dpiScale, hitZones: dummyZones);

            var ghostOutline = new Pen(ComponentRenderer.CreateSolid("#38BDF8"), 1.8)
            {
                DashStyle = DashStyles.Dash
            };
            ghostOutline.Freeze();
            dc.DrawRoundedRectangle(null, ghostOutline,
                new Rect(_dragGhostComponent.X - 3, _dragGhostComponent.Y - 3, _dragGhostComponent.Width + 6, _dragGhostComponent.Height + 6), 9, 9);
            dc.Pop();
        }

        if (_activeFault is not null)
        {
            DrawShortCircuitMarker(dc, _activeFault, dpiScale);
        }

        dc.Pop();

        if (_hoveredTerminal is not null)
        {
            DrawTerminalTooltipOverlay(dc, _hoveredTerminal, theme, dpiScale);
        }
    }

    private void DrawDotGrid(DrawingContext dc, double screenW, double screenH, CanvasThemeColors theme)
    {
        if (ZoomScale < 0.42) return;

        Brush minorBrush = ComponentRenderer.CreateSolid(theme.GridDotHex);
        Brush majorBrush = ComponentRenderer.CreateSolid(theme.MajorGridDotHex);

        Point topLeft = ScreenToCanvas(new Point(0, 0));
        Point bottomRight = ScreenToCanvas(new Point(screenW, screenH));

        double startX = Math.Floor(topLeft.X / GridStep) * GridStep;
        double startY = Math.Floor(topLeft.Y / GridStep) * GridStep;
        double endX = Math.Ceiling(bottomRight.X / GridStep) * GridStep;
        double endY = Math.Ceiling(bottomRight.Y / GridStep) * GridStep;

        for (double x = startX; x <= endX; x += GridStep)
        {
            int ix = (int)Math.Round(x / GridStep);
            for (double y = startY; y <= endY; y += GridStep)
            {
                int iy = (int)Math.Round(y / GridStep);
                bool major = ix % 5 == 0 && iy % 5 == 0;
                dc.DrawEllipse(major ? majorBrush : minorBrush, null, new Point(x, y), major ? 1.7 : 1.05, major ? 1.7 : 1.05);
            }
        }
    }

    private void DrawWire(DrawingContext dc, WireModel wire, int index, bool isHovered, bool isSelected, CanvasThemeColors theme)
    {
        var geom = WireRouter.BuildWireGeometry(wire, RoutingStyle, index);
        if (geom.IsEmpty()) return;

        (string baseHex, string brightHex, string glowHex) = GetWirePaletteColors(wire.ColorPreset, theme.IsDark);

        if (CurrentMode == AppMode.Simulation && wire.IsShortCircuitPath)
        {
            double pulse = 0.55 + 0.45 * Math.Sin(_faultPulsePhase * 2.0);
            var faultGlowPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(160 * pulse + 60), 239, 68, 68)), 10.0)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            faultGlowPen.Freeze();
            dc.DrawGeometry(null, faultGlowPen, geom);
            dc.DrawGeometry(null, ComponentRenderer.CreatePen("#EF4444", 4.2), geom);

            var faultDashPen = new Pen(ComponentRenderer.CreateSolid("#FEF2F2"), 2.0)
            {
                DashStyle = new DashStyle([3.0, 3.0], _dashPhase)
            };
            faultDashPen.Freeze();
            dc.DrawGeometry(null, faultDashPen, geom);
            return;
        }

        if (CurrentMode == AppMode.Simulation && wire.HasCurrentFlow)
        {
            dc.DrawGeometry(null, ComponentRenderer.CreatePen(glowHex, 9.5), geom);
            dc.DrawGeometry(null, ComponentRenderer.CreatePen(brightHex, 3.8), geom);

            var flowPen = new Pen(ComponentRenderer.CreateSolid("#FFFFFF"), 1.7)
            {
                DashStyle = new DashStyle([4.0, 5.0], _dashPhase),
                DashCap = PenLineCap.Round
            };
            flowPen.Freeze();
            dc.DrawGeometry(null, flowPen, geom);
            return;
        }

        if (CurrentMode == AppMode.Simulation && wire.IsEnergized)
        {
            dc.DrawGeometry(null, ComponentRenderer.CreatePen(glowHex, 5.5), geom);
            dc.DrawGeometry(null, ComponentRenderer.CreatePen(brightHex, 3.1), geom);
            return;
        }

        if (CurrentMode == AppMode.Design && isSelected)
        {
            dc.DrawGeometry(null, ComponentRenderer.CreatePen("#7738BDF8", 9.0), geom);
            dc.DrawGeometry(null, ComponentRenderer.CreatePen(brightHex, 3.6), geom);
            return;
        }

        if (CurrentMode == AppMode.Design && isHovered)
        {
            dc.DrawGeometry(null, ComponentRenderer.CreatePen("#447DD3FC", 7.5), geom);
        }
        dc.DrawGeometry(null, ComponentRenderer.CreatePen(baseHex, isHovered ? 3.4 : 2.8), geom);
    }

    private void DrawWireControlHandles(DrawingContext dc, WireModel wire, double dpiScale)
    {
        Brush plusBg = ComponentRenderer.CreateSolid("#0284C7");
        Pen plusRing = ComponentRenderer.CreatePen("#FFFFFF", 1.3);
        Brush vertexBg = ComponentRenderer.CreateSolid("#FACC15");
        Pen vertexRing = ComponentRenderer.CreatePen("#0F172A", 1.6);

        if (RoutingStyle == WireRoutingStyle.Curved)
        {
            // Chấm [+] nằm chính xác tại trung điểm t=0.5 trên mỗi nhịp đường cong mượt!
            var anchors = WireRouter.GetEffectiveAnchorPoints(wire, WireRoutingStyle.Curved);
            var spans = WireRouter.ComputeSmoothCurveSpans(anchors, wire.TerminalA.EffectiveSide, wire.TerminalB.EffectiveSide, _wires.IndexOf(wire));

            foreach (var sp in spans)
            {
                DrawPlusHandle(dc, sp.MidPoint, plusBg, plusRing, dpiScale);
            }

            foreach (var wp in wire.Waypoints)
            {
                dc.DrawEllipse(ComponentRenderer.CreateSolid("#F8FAFC"), null, wp, 6.8, 6.8);
                dc.DrawEllipse(vertexBg, vertexRing, wp, 5.2, 5.2);
            }
            return;
        }

        // Chế độ Vuông góc (Orthogonal)
        var pts = wire.RoutedPoints;
        if (pts.Count < 2) return;

        for (int s = 0; s < pts.Count - 1; s++)
        {
            if ((pts[s + 1] - pts[s]).Length < 18.0) continue;
            Point mid = new((pts[s].X + pts[s + 1].X) / 2.0, (pts[s].Y + pts[s + 1].Y) / 2.0);
            DrawPlusHandle(dc, mid, plusBg, plusRing, dpiScale);
        }

        for (int i = 1; i < pts.Count - 1; i++)
        {
            dc.DrawEllipse(ComponentRenderer.CreateSolid("#F8FAFC"), null, pts[i], 6.8, 6.8);
            dc.DrawEllipse(vertexBg, vertexRing, pts[i], 5.2, 5.2);
        }
    }

    private static void DrawPlusHandle(DrawingContext dc, Point center, Brush bg, Pen ring, double dpiScale)
    {
        dc.DrawEllipse(bg, ring, center, 6.2, 6.2);
        var plusFt = ComponentRenderer.FormatText("+", 9.2, PlusTypeface, Brushes.White, dpiScale);
        dc.DrawText(plusFt, new Point(center.X - plusFt.Width / 2.0, center.Y - plusFt.Height / 2.0 - 0.5));
    }

    private void DrawDraftPolylinePreview(DrawingContext dc)
    {
        if (_wireStartTerminal is null) return;

        var previewPts = new List<Point> { new(_wireStartTerminal.CanvasX, _wireStartTerminal.CanvasY) };
        previewPts.AddRange(_draftWaypoints);

        TerminalSide endSide = _hoveredTerminal?.EffectiveSide ?? TerminalSide.Top;

        if (_hoveredTerminal is not null && _hoveredTerminal != _wireStartTerminal)
        {
            Point pEnd = new(_hoveredTerminal.CanvasX, _hoveredTerminal.CanvasY);
            Point last = previewPts[^1];
            if (RoutingStyle == WireRoutingStyle.Orthogonal)
            {
                if (!IsShiftDown() && _draftWaypoints.Count > 0 && Math.Abs(last.X - pEnd.X) > 1 && Math.Abs(last.Y - pEnd.Y) > 1)
                {
                    previewPts.Add(_hoveredTerminal.EffectiveSide is TerminalSide.Top or TerminalSide.Bottom
                        ? new Point(pEnd.X, last.Y)
                        : new Point(last.X, pEnd.Y));
                }
                else if (_draftWaypoints.Count == 0 && !IsShiftDown())
                {
                    double midY = (last.Y + pEnd.Y) / 2.0;
                    previewPts.Add(new Point(last.X, midY));
                    previewPts.Add(new Point(pEnd.X, midY));
                }
            }
            previewPts.Add(pEnd);
        }
        else
        {
            Point next = RoutingStyle == WireRoutingStyle.Curved
                ? SnapIfEnabled(_currentCanvasMousePos, IsShiftDown())
                : ComputeConstrainedDraftPoint(_currentCanvasMousePos, IsShiftDown());
            previewPts.Add(next);
        }

        var geom = WireRouter.BuildPreviewGeometry(previewPts, _wireStartTerminal.EffectiveSide, endSide, RoutingStyle);
        var (_, brightHex, _) = GetWirePaletteColors(ActiveWireColor, ThemeManager.IsEffectiveDark);

        var dashPen = new Pen(ComponentRenderer.CreateSolid(brightHex), 2.6)
        {
            DashStyle = new DashStyle([4, 3], 0),
            DashCap = PenLineCap.Round
        };
        dashPen.Freeze();
        dc.DrawGeometry(null, dashPen, geom);

        foreach (var wp in _draftWaypoints)
        {
            dc.DrawEllipse(ComponentRenderer.CreateSolid("#22D3EE"), ComponentRenderer.CreatePen("#FFFFFF", 1.3), wp, 4.5, 4.5);
        }
    }

    private void DrawShortCircuitMarker(DrawingContext dc, ShortCircuitFaultInfo fault, double dpiScale)
    {
        Point p = fault.FaultCanvasPoint;
        double pulse = (Math.Sin(_faultPulsePhase * 1.8) + 1.0) * 0.5;
        double outerRadius = 18.0 + pulse * 16.0;

        dc.DrawEllipse(ComponentRenderer.CreateRadialGlow("#AAEF4444", "#00EF4444"), null, p, outerRadius + 10, outerRadius + 10);
        dc.DrawEllipse(null, ComponentRenderer.CreatePen("#F87171", 2.4), p, outerRadius, outerRadius);
        dc.DrawEllipse(ComponentRenderer.CreateSolid("#DC2626"), ComponentRenderer.CreatePen("#FEF2F2", 2.0), p, 12, 12);

        var bolt = ComponentRenderer.FormatText("⚡", 12, new Typeface("Segoe UI Symbol"), Brushes.White, dpiScale);
        dc.DrawText(bolt, new Point(p.X - bolt.Width / 2, p.Y - bolt.Height / 2));

        var tagFt = ComponentRenderer.FormatText(LocalizationManager.Get("Str.Vis.ShortPoint"), 10.5,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), Brushes.White, dpiScale);
        Rect callout = new(p.X - tagFt.Width / 2 - 8, p.Y - 42, tagFt.Width + 16, 22);
        dc.DrawRoundedRectangle(ComponentRenderer.CreateSolid("#EE991B1B"), ComponentRenderer.CreatePen("#FCA5A5", 1.5), callout, 5, 5);
        dc.DrawText(tagFt, new Point(callout.X + 8, callout.Y + 3));
    }

    private void DrawTerminalTooltipOverlay(DrawingContext dc, Terminal term, CanvasThemeColors theme, double dpiScale)
    {
        Point screenPos = CanvasToScreen(new Point(term.CanvasX, term.CanvasY));
        string text = term.GetTooltipText();
        var ft = ComponentRenderer.FormatText(text, 11.5, TooltipTypeface, ComponentRenderer.CreateSolid(theme.TooltipTextHex), dpiScale);

        double padX = 10;
        double padY = 7;
        double boxW = ft.Width + padX * 2;
        double boxH = ft.Height + padY * 2;

        double x = screenPos.X + 16;
        double y = screenPos.Y - boxH - 10;
        if (x + boxW > ActualWidth - 8) x = screenPos.X - boxW - 14;
        if (y < 8) y = screenPos.Y + 16;

        Rect rect = new(x, y, boxW, boxH);
        dc.DrawRoundedRectangle(ComponentRenderer.CreateSolid(theme.TooltipBgHex), ComponentRenderer.CreatePen("#38BDF8", 1.3), rect, 6, 6);
        dc.DrawText(ft, new Point(rect.X + padX, rect.Y + padY));
    }

    // =========================================================================
    // TƯƠNG TÁC NÚM XOAY AH3-3 & TÁCH DÂY TẠO NÚT RẼ NHÁNH (WIRE T-JUNCTION)
    // =========================================================================

    private static void UpdateAh3DialFromCanvasPoint(RotaryTimerAh3Component ah3, Point canvasPt)
    {
        Point center = ah3.TransformLocalPointToCanvas(ah3.Width / 2.0, ah3.Height / 2.0 + 4.0);
        double dx = canvasPt.X - center.X;
        double dy = canvasPt.Y - center.Y;
        if (dx * dx + dy * dy < 9.0) return;

        // Góc 0° ở 12 giờ, tăng dương theo chiều kim đồng hồ (bù góc xoay của thiết bị nếu đang xoay!)
        double rad = Math.Atan2(dx, -dy);
        double deg = rad * 180.0 / Math.PI - ah3.RotationAngle;
        ah3.SetDelayFromDialAngle(deg);
    }

    private bool TryFindWireBranchSnap(Point canvasPt, out WireModel bestWire, out Point bestPoint, out int bestSplitIdx)
    {
        bestWire = null!;
        bestPoint = canvasPt;
        bestSplitIdx = 0;

        double maxDist = 11.5 / Math.Max(0.6, ZoomScale);
        double minFound = maxDist;

        foreach (var wire in _wires)
        {
            double d = WireRouter.FindClosestPointOnWire(canvasPt, wire, RoutingStyle, out Point proj, out int idx);
            if (d < minFound)
            {
                // Bỏ qua nếu điểm bấm quá sát hai đầu mút (chân thiết bị sẽ xử lý trực tiếp)
                double dA = (proj - new Point(wire.TerminalA.CanvasX, wire.TerminalA.CanvasY)).Length;
                double dB = (proj - new Point(wire.TerminalB.CanvasX, wire.TerminalB.CanvasY)).Length;
                if (dA < 10.0 || dB < 10.0) continue;

                minFound = d;
                bestWire = wire;
                bestPoint = proj;
                bestSplitIdx = idx;
            }
        }

        return bestWire is not null;
    }

    public WireJunctionComponent SplitWireAtPoint(WireModel wire, Point splitPt, int splitIdx)
    {
        if (RoutingStyle == WireRoutingStyle.Orthogonal)
        {
            wire.EnsureEditableWaypoints();
            wire.SyncRoutedPointsFromWaypoints();
        }

        var junc = new WireJunctionComponent
        {
            CenterPoint = splitPt,
            DotColorPreset = wire.ColorPreset
        };
        AttachComponentHooks(junc);
        _components.Add(junc);

        int clampedSplit = Math.Clamp(splitIdx, 0, wire.Waypoints.Count);
        var wpBefore = wire.Waypoints.Take(clampedSplit).ToList();
        var wpAfter = wire.Waypoints.Skip(clampedSplit).ToList();

        var w1 = new WireModel(wire.TerminalA, junc.NodeTerminal, wire.ColorPreset, wpBefore.Count > 0 ? wpBefore : null);
        var w2 = new WireModel(junc.NodeTerminal, wire.TerminalB, wire.ColorPreset, wpAfter.Count > 0 ? wpAfter : null);

        int origIndex = _wires.IndexOf(wire);
        if (origIndex >= 0)
        {
            _wires.RemoveAt(origIndex);
            _wires.Insert(origIndex, w2);
            _wires.Insert(origIndex, w1);
        }
        else
        {
            _wires.Add(w1);
            _wires.Add(w2);
        }

        if (_selectedWire == wire) SetSelection(null, null);
        if (_hoveredWire == wire) _hoveredWire = null;
        RebuildWireRoutes();
        return junc;
    }

    private void StartBranchFromWire(WireModel wire, Point clickCanvasPt)
    {
        if (CurrentMode != AppMode.Design) return;
        WireRouter.FindClosestPointOnWire(clickCanvasPt, wire, RoutingStyle, out Point proj, out int splitIdx);
        var junc = SplitWireAtPoint(wire, proj, splitIdx);
        _pendingCreatedJunction = junc;
        ActiveWireColor = wire.ColorPreset;
        SetSelection(null, null);
        _wireStartTerminal = junc.NodeTerminal;
        _draftWaypoints.Clear();
        StatusMessageChanged?.Invoke(LocalizationManager.Get("Str.Status.BranchStarted"));
        InvalidateVisual();
    }

    /// <summary>
    /// Tự động dọn dẹp các nút rẽ nhánh (T-Junction) dư thừa:
    /// - Nếu chỉ còn 2 đoạn dây nối tiếp qua nút -> gộp liền lại thành 1 sợi dây duy nhất và xóa chấm Junction!
    /// - Nếu còn 0 hoặc 1 đoạn cụt -> xóa bỏ gọn gàng.
    /// </summary>
    public void CleanupRedundantJunctions()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            var junctions = _components.OfType<WireJunctionComponent>().ToList();
            foreach (var junc in junctions)
            {
                if (junc == _pendingCreatedJunction) continue;

                var attached = _wires
                    .Where(w => w.TerminalA == junc.NodeTerminal || w.TerminalB == junc.NodeTerminal)
                    .ToList();

                if (attached.Count == 0)
                {
                    DetachComponentHooks(junc);
                    _components.Remove(junc);
                    changed = true;
                }
                else if (attached.Count == 1)
                {
                    _wires.Remove(attached[0]);
                    DetachComponentHooks(junc);
                    _components.Remove(junc);
                    changed = true;
                }
                else if (attached.Count == 2)
                {
                    var w1 = attached[0];
                    var w2 = attached[1];
                    Terminal endA = w1.TerminalA == junc.NodeTerminal ? w1.TerminalB : w1.TerminalA;
                    Terminal endB = w2.TerminalA == junc.NodeTerminal ? w2.TerminalB : w2.TerminalA;

                    var combinedWp = new List<Point>();
                    if (w1.Waypoints.Count > 0)
                    {
                        var list1 = w1.TerminalB == junc.NodeTerminal ? w1.Waypoints : w1.Waypoints.AsEnumerable().Reverse().ToList();
                        combinedWp.AddRange(list1);
                    }
                    if (w2.Waypoints.Count > 0)
                    {
                        var list2 = w2.TerminalA == junc.NodeTerminal ? w2.Waypoints : w2.Waypoints.AsEnumerable().Reverse().ToList();
                        combinedWp.AddRange(list2);
                    }

                    _wires.Remove(w1);
                    _wires.Remove(w2);
                    if (endA != endB && !_wires.Any(w => w.Connects(endA, endB)))
                    {
                        _wires.Add(new WireModel(endA, endB, w1.ColorPreset, combinedWp.Count > 0 ? combinedWp : null));
                    }

                    DetachComponentHooks(junc);
                    _components.Remove(junc);
                    changed = true;
                }
            }
        }
    }

    // =========================================================================
    // TOÁN HỌC BẮT DÍNH & VA CHẠM
    // =========================================================================

    private Point ComputeConstrainedDraftPoint(Point rawCanvasPt, bool isShiftDown)
    {
        Point anchor = _draftWaypoints.Count > 0
            ? _draftWaypoints[^1]
            : new Point(_wireStartTerminal!.CanvasX, _wireStartTerminal.CanvasY);

        if (isShiftDown) return rawCanvasPt;

        Point pt = SnapToGridEnabled ? new Point(Snap(rawCanvasPt.X), Snap(rawCanvasPt.Y)) : rawCanvasPt;
        return Math.Abs(pt.X - anchor.X) >= Math.Abs(pt.Y - anchor.Y)
            ? new Point(pt.X, anchor.Y)
            : new Point(anchor.X, pt.Y);
    }

    private Point SnapIfEnabled(Point pt, bool isShiftDown) =>
        SnapToGridEnabled && !isShiftDown ? new Point(Snap(pt.X), Snap(pt.Y)) : pt;

    private static bool IsShiftDown() =>
        Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

    private bool IsPointNearAnyWireHandle(WireModel wire, Point canvasPt)
    {
        double hitRadius = HandleHitRadius / Math.Max(0.6, ZoomScale);

        if (RoutingStyle == WireRoutingStyle.Curved)
        {
            if (wire.Waypoints.Any(wp => (wp - canvasPt).Length <= hitRadius)) return true;
            var anchors = WireRouter.GetEffectiveAnchorPoints(wire, WireRoutingStyle.Curved);
            var spans = WireRouter.ComputeSmoothCurveSpans(anchors, wire.TerminalA.EffectiveSide, wire.TerminalB.EffectiveSide, _wires.IndexOf(wire));
            return spans.Any(sp => (sp.MidPoint - canvasPt).Length <= hitRadius);
        }

        var pts = wire.RoutedPoints;
        for (int i = 1; i < pts.Count - 1; i++)
            if ((pts[i] - canvasPt).Length <= hitRadius) return true;
        for (int s = 0; s < pts.Count - 1; s++)
        {
            Point mid = new((pts[s].X + pts[s + 1].X) / 2.0, (pts[s].Y + pts[s + 1].Y) / 2.0);
            if ((mid - canvasPt).Length <= hitRadius) return true;
        }
        return false;
    }

    public Point ScreenToCanvas(Point screenPt) =>
        new((screenPt.X - PanOffset.X) / ZoomScale, (screenPt.Y - PanOffset.Y) / ZoomScale);

    public Point CanvasToScreen(Point canvasPt) =>
        new(canvasPt.X * ZoomScale + PanOffset.X, canvasPt.Y * ZoomScale + PanOffset.Y);

    private static double Snap(double value) => Math.Round(value / GridStep) * GridStep;

    private Terminal? FindTerminalAt(Point canvasPt)
    {
        Terminal? best = null;
        double bestDist = TerminalHitRadius;

        foreach (var comp in _components)
        {
            foreach (var t in comp.Terminals)
            {
                double d = (new Point(t.CanvasX, t.CanvasY) - canvasPt).Length;
                if (d <= bestDist) { bestDist = d; best = t; }
            }
        }
        return best;
    }

    private ElectricalComponent? FindComponentAt(Point canvasPt)
    {
        for (int i = _components.Count - 1; i >= 0; i--)
        {
            var c = _components[i];
            if (c.GetVisualBounds().Contains(canvasPt))
                return c;
        }
        return null;
    }

    private WireModel? FindWireAt(Point canvasPt)
    {
        for (int i = _wires.Count - 1; i >= 0; i--)
        {
            var geom = WireRouter.BuildWireGeometry(_wires[i], RoutingStyle, i);
            if (geom.StrokeContains(WireHitTestPen, canvasPt))
                return _wires[i];
        }
        return null;
    }

    private ComponentHitZone? FindHitZone(Point canvasPt)
    {
        for (int i = _hitZones.Count - 1; i >= 0; i--)
        {
            if (_hitZones[i].CanvasBounds.Contains(canvasPt))
                return _hitZones[i];
        }
        return null;
    }

    public static (string BaseHex, string BrightHex, string GlowHex) GetWirePaletteColors(WireColorPreset preset, bool isDark) => preset switch
    {
        WireColorPreset.Red_L1_DCPlus => ("#DC2626", "#FF4D4D", "#66EF4444"),
        WireColorPreset.Yellow_L2 => (isDark ? "#CA8A04" : "#B45309", "#FACC15", "#66FACC15"),
        WireColorPreset.Blue_L3 => ("#2563EB", "#60A5FA", "#663B82F6"),
        WireColorPreset.Black_N_DCMinus => (isDark ? "#525B6C" : "#1E293B", isDark ? "#94A3B8" : "#334155", "#5564748B"),
        WireColorPreset.Green_PE => ("#16A34A", "#4ADE80", "#6622C55E"),
        _ => ("#DC2626", "#FF4D4D", "#66EF4444")
    };
}
