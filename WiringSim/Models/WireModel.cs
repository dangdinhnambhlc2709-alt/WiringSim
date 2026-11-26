using System.Windows;
using WiringSim.Models.Enums;

namespace WiringSim.Models;

/// <summary>
/// Mô hình dây dẫn kết nối giữa 2 chân đấu nối (TerminalA &lt;-&gt; TerminalB).
/// Hỗ trợ cả đi dây tự động (Auto-Routing) và đi dây bẻ góc thủ công (Manual Polyline Waypoints).
/// </summary>
public sealed class WireModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public Terminal TerminalA { get; }
    public Terminal TerminalB { get; }
    public WireColorPreset ColorPreset { get; set; }

    /// <summary>
    /// Danh sách các điểm uốn / bẻ góc trung gian do người dùng tự đặt hoặc chỉnh sửa (tọa độ Canvas).
    /// Khi danh sách này có phần tử (hoặc IsManualRoute = true), WireRouter sẽ giữ nguyên đường gấp khúc này.
    /// </summary>
    public List<Point> Waypoints { get; } = new();

    /// <summary>Đánh dấu dây đang sử dụng các điểm uốn thủ công của người dùng.</summary>
    public bool HasManualWaypoints => Waypoints.Count > 0;

    /// <summary>Tập điểm đầy đủ [TerminalA, ...Waypoints/AutoPoints..., TerminalB] để vẽ và bắt va chạm.</summary>
    public List<Point> RoutedPoints { get; set; } = new();

    // --- Trạng thái mô phỏng Live Wire ---
    public bool IsEnergized { get; set; }
    public bool HasCurrentFlow { get; set; }
    public bool IsShortCircuitPath { get; set; }
    public PhaseType CarriedPhase { get; set; } = PhaseType.None;
    public double CarriedVoltage { get; set; }

    public WireModel(Terminal terminalA, Terminal terminalB, WireColorPreset colorPreset, IEnumerable<Point>? waypoints = null)
    {
        TerminalA = terminalA;
        TerminalB = terminalB;
        ColorPreset = colorPreset;
        if (waypoints is not null)
        {
            Waypoints.AddRange(waypoints);
        }
    }

    /// <summary>
    /// Chuyển các điểm trung gian đang có của đường đi dây tự động sang Waypoints thủ công để người dùng kéo nắn.
    /// </summary>
    public void EnsureEditableWaypoints()
    {
        if (Waypoints.Count == 0 && RoutedPoints.Count > 2)
        {
            for (int i = 1; i < RoutedPoints.Count - 1; i++)
            {
                Waypoints.Add(RoutedPoints[i]);
            }
        }
    }

    public void ClearManualWaypoints()
    {
        Waypoints.Clear();
    }

    public void SyncRoutedPointsFromWaypoints()
    {
        RoutedPoints = new List<Point>(Waypoints.Count + 2)
        {
            new(TerminalA.CanvasX, TerminalA.CanvasY)
        };
        RoutedPoints.AddRange(Waypoints);
        RoutedPoints.Add(new Point(TerminalB.CanvasX, TerminalB.CanvasY));
    }

    public void ResetSimulationState()
    {
        IsEnergized = false;
        HasCurrentFlow = false;
        IsShortCircuitPath = false;
        CarriedPhase = PhaseType.None;
        CarriedVoltage = 0;
    }

    public bool Connects(Terminal a, Terminal b) =>
        (TerminalA == a && TerminalB == b) || (TerminalA == b && TerminalB == a);
}
