using System.Windows;

namespace WiringSim.Simulation;

/// <summary>
/// Lưu thông tin chi tiết về sự cố ngắn mạch để cảnh báo đỏ trên giao diện và chỉ rõ vị trí điểm chập.
/// </summary>
public sealed class ShortCircuitFaultInfo
{
    public required string Title { get; init; }
    public required string DetailMessage { get; init; }
    public required string LocationDescription { get; init; }
    public required Point FaultCanvasPoint { get; init; }
    public required HashSet<string> FaultedWireIds { get; init; }
    public required List<string> TrippedBreakerNames { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.Now;
}
