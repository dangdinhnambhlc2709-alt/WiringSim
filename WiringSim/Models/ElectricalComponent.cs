using System.Windows;
using WiringSim.Models.Enums;

namespace WiringSim.Models;

/// <summary>
/// Lớp cơ sở trừu tượng cho mọi thiết bị điện trong WiringSim.
/// Hỗ trợ Xoay (0°/90°/180°/270°), Lật ngang/dọc với tính toán lại chính xác tọa độ chân đấu nối và vùng bao Canvas.
/// </summary>
public abstract class ElectricalComponent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public abstract string TypeKey { get; }
    public string DisplayName { get; set; }
    public abstract ComponentCategory Category { get; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; protected set; }
    public double Height { get; protected set; }

    // --- Phép biến hình Xoay / Lật (Lưu trong JSON) ---
    /// <summary>Góc xoay theo chiều kim đồng hồ: 0, 90, 180, 270 độ.</summary>
    public int RotationAngle { get; set; }
    public bool IsFlippedHorizontal { get; set; }
    public bool IsFlippedVertical { get; set; }

    public bool IsBurnedOut { get; set; }
    public string? BurnoutReason { get; set; }

    protected readonly List<Terminal> _terminals = new();
    public IReadOnlyList<Terminal> Terminals => _terminals;

    public event Action<ElectricalComponent>? TerminalsChanged;

    protected ElectricalComponent(string displayName, double width, double height)
    {
        DisplayName = displayName;
        Width = width;
        Height = height;
    }

    public void RotateCW() => RotationAngle = (RotationAngle + 90) % 360;
    public void RotateCCW() => RotationAngle = (RotationAngle + 270) % 360;
    public void FlipHorizontal() => IsFlippedHorizontal = !IsFlippedHorizontal;
    public void FlipVertical() => IsFlippedVertical = !IsFlippedVertical;

    /// <summary>
    /// Biến đổi một tọa độ cục bộ (relX, relY) của thiết bị sang tọa độ tuyệt đối trên Canvas
    /// sau khi áp dụng phép Lật (Flip) và Xoay (Rotate) quanh tâm thiết bị.
    /// </summary>
    public Point TransformLocalPointToCanvas(double relX, double relY)
    {
        double cx = X + Width / 2.0;
        double cy = Y + Height / 2.0;
        double dx = relX - Width / 2.0;
        double dy = relY - Height / 2.0;

        if (IsFlippedHorizontal) dx = -dx;
        if (IsFlippedVertical) dy = -dy;

        (double rx, double ry) = ((RotationAngle % 360 + 360) % 360) switch
        {
            90 => (-dy, dx),
            180 => (-dx, -dy),
            270 => (dy, -dx),
            _ => (dx, dy)
        };

        return new Point(cx + rx, cy + ry);
    }

    /// <summary>Biến đổi một hình chữ nhật tương tác cục bộ (tọa độ Canvas trước khi xoay) sang khung bao Canvas sau Xoay/Lật.</summary>
    public Rect TransformCanvasRectBySelf(Rect unrotatedCanvasRect)
    {
        Point p1 = TransformLocalPointToCanvas(unrotatedCanvasRect.Left - X, unrotatedCanvasRect.Top - Y);
        Point p2 = TransformLocalPointToCanvas(unrotatedCanvasRect.Right - X, unrotatedCanvasRect.Top - Y);
        Point p3 = TransformLocalPointToCanvas(unrotatedCanvasRect.Right - X, unrotatedCanvasRect.Bottom - Y);
        Point p4 = TransformLocalPointToCanvas(unrotatedCanvasRect.Left - X, unrotatedCanvasRect.Bottom - Y);

        double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    /// <summary>Khung bao hiển thị thực tế trên Canvas sau khi đã xoay 90°/270°.</summary>
    public Rect GetVisualBounds()
    {
        int norm = (RotationAngle % 360 + 360) % 360;
        if (norm is 90 or 270)
        {
            double cx = X + Width / 2.0;
            double cy = Y + Height / 2.0;
            return new Rect(cx - Height / 2.0, cy - Width / 2.0, Height, Width);
        }
        return new Rect(X, Y, Width, Height);
    }

    public TerminalSide TransformSide(TerminalSide side)
    {
        (int vx, int vy) = side switch
        {
            TerminalSide.Top => (0, -1),
            TerminalSide.Bottom => (0, 1),
            TerminalSide.Left => (-1, 0),
            TerminalSide.Right => (1, 0),
            _ => (0, 1)
        };

        if (IsFlippedHorizontal) vx = -vx;
        if (IsFlippedVertical) vy = -vy;

        (int rx, int ry) = ((RotationAngle % 360 + 360) % 360) switch
        {
            90 => (-vy, vx),
            180 => (-vx, -vy),
            270 => (vy, -vx),
            _ => (vx, vy)
        };

        if (rx > 0) return TerminalSide.Right;
        if (rx < 0) return TerminalSide.Left;
        return ry < 0 ? TerminalSide.Top : TerminalSide.Bottom;
    }

    protected Terminal AddTerminal(string id, string name, double relX, double relY, TerminalSide side, string description = "")
    {
        var terminal = new Terminal(this, id, name, relX, relY, side, description);
        _terminals.Add(terminal);
        return terminal;
    }

    protected Dictionary<string, Terminal> SnapshotTerminalsForRebuild()
    {
        var map = _terminals.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        _terminals.Clear();
        return map;
    }

    protected Terminal AddOrReuseTerminal(
        Dictionary<string, Terminal> existing,
        string id,
        string name,
        double relX,
        double relY,
        TerminalSide side,
        string description = "")
    {
        if (existing.TryGetValue(id, out var term))
        {
            term.UpdateLayout(name, relX, relY, side, description);
            _terminals.Add(term);
            return term;
        }
        return AddTerminal(id, name, relX, relY, side, description);
    }

    protected void NotifyTerminalsChanged() => TerminalsChanged?.Invoke(this);

    public Terminal? GetTerminal(string idOrName) =>
        _terminals.FirstOrDefault(t =>
            string.Equals(t.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    public void RepairBurnout()
    {
        IsBurnedOut = false;
        BurnoutReason = null;
    }

    public virtual IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs() =>
        Enumerable.Empty<(Terminal, PotentialToken)>();

    public virtual IEnumerable<InternalConductivePath> GetInternalConductivePaths() =>
        Enumerable.Empty<InternalConductivePath>();

    public virtual IEnumerable<InternalLoadPath> GetActiveLoadPaths() =>
        Enumerable.Empty<InternalLoadPath>();

    public virtual void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
    }

    public virtual void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
    }

    public virtual void ResetSimulationState()
    {
        IsBurnedOut = false;
        BurnoutReason = null;
        foreach (var t in _terminals)
            t.ResetElectricalState();
    }

    public virtual Dictionary<string, string> ExportProperties() => new();

    public virtual void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
    }
}
