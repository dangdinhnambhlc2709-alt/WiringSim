using System.Windows;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models;

/// <summary>
/// Đại diện một thế điện áp truyền từ một nguồn cụ thể qua mạng dây dẫn (hỗ trợ cả tần số biến thiên từ Biến tần VFD).
/// </summary>
public readonly record struct PotentialToken(
    string SourceComponentId,
    string SourceTerminalId,
    CurrentKind CurrentKind,
    PhaseType Phase,
    double Voltage,
    double FrequencyHz = 50.0);

/// <summary>
/// Chân đấu nối (Terminal) gắn trên thiết bị điện — tự động đồng bộ tọa độ &amp; hướng thoát dây khi thiết bị Xoay / Lật.
/// </summary>
public sealed class Terminal
{
    public string Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public ElectricalComponent Owner { get; }

    public double RelX { get; private set; }
    public double RelY { get; private set; }
    public TerminalSide Side { get; private set; }

    public double Voltage { get; set; }
    public CurrentKind CurrentKind { get; set; } = CurrentKind.None;
    public PhaseType Phase { get; set; } = PhaseType.None;
    public bool IsEnergized { get; set; }
    public bool HasCurrentFlow { get; set; }
    public int NetId { get; set; } = -1;

    public List<PotentialToken> ActivePotentials { get; } = new();

    public Terminal(
        ElectricalComponent owner,
        string id,
        string name,
        double relX,
        double relY,
        TerminalSide side,
        string description = "")
    {
        Owner = owner;
        Id = id;
        Name = name;
        RelX = relX;
        RelY = relY;
        Side = side;
        Description = description;
    }

    public void UpdateLayout(string name, double relX, double relY, TerminalSide side, string description = "")
    {
        Name = name;
        RelX = relX;
        RelY = relY;
        Side = side;
        Description = description;
    }

    /// <summary>Tọa độ tuyệt đối X trên Canvas sau khi đã áp dụng Xoay (Rotate) &amp; Lật (Flip) của thiết bị.</summary>
    public double CanvasX => Owner.TransformLocalPointToCanvas(RelX, RelY).X;

    /// <summary>Tọa độ tuyệt đối Y trên Canvas sau khi đã áp dụng Xoay (Rotate) &amp; Lật (Flip) của thiết bị.</summary>
    public double CanvasY => Owner.TransformLocalPointToCanvas(RelX, RelY).Y;

    /// <summary>Hướng thoát dây thực tế sau khi đã Xoay &amp; Lật thiết bị.</summary>
    public TerminalSide EffectiveSide => Owner.TransformSide(Side);

    public void ResetElectricalState()
    {
        Voltage = 0;
        CurrentKind = CurrentKind.None;
        Phase = PhaseType.None;
        IsEnergized = false;
        HasCurrentFlow = false;
        NetId = -1;
        ActivePotentials.Clear();
    }

    public string GetTooltipText()
    {
        string stateInfo = CurrentKind == CurrentKind.None && Phase == PhaseType.None
            ? LocalizationManager.Get("Str.Term.Unpowered")
            : LocalizationManager.Format("Str.Term.StateFormat", CurrentKind, FormatPhase(Phase), Voltage);
        return LocalizationManager.Format("Str.Term.TooltipFormat", Owner.DisplayName, Name, stateInfo);
    }

    public static string FormatPhase(PhaseType phase) => phase switch
    {
        PhaseType.L1 => LocalizationManager.Get("Str.Phase.L1"),
        PhaseType.L2 => LocalizationManager.Get("Str.Phase.L2"),
        PhaseType.L3 => LocalizationManager.Get("Str.Phase.L3"),
        PhaseType.N => LocalizationManager.Get("Str.Phase.N"),
        PhaseType.PE => LocalizationManager.Get("Str.Phase.PE"),
        PhaseType.DC_Plus => LocalizationManager.Get("Str.Phase.DCPlus"),
        PhaseType.DC_Minus => LocalizationManager.Get("Str.Phase.DCMinus"),
        _ => LocalizationManager.Get("Str.Phase.Open")
    };
}
