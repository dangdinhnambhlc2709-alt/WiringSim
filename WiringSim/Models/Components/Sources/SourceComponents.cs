using System.Globalization;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Components.Sources;

/// <summary>
/// Nguồn một chiều DC (5V, 12V, 24V, 48V DC) — có 2 chân + và -.
/// </summary>
public sealed class DcSourceComponent : ElectricalComponent
{
    public const string TypeId = "DC_SOURCE";
    public static readonly double[] SupportedVoltages = [5, 12, 24, 48];

    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.PowerSource;

    public double OutputVoltage { get; private set; } = 24.0;
    public Terminal PlusTerminal { get; }
    public Terminal MinusTerminal { get; }

    public DcSourceComponent() : base("Nguồn DC", 120, 100)
    {
        PlusTerminal = AddTerminal("PLUS", "+", 40, 100, TerminalSide.Bottom, "Cực dương DC (+)");
        MinusTerminal = AddTerminal("MINUS", "-", 80, 100, TerminalSide.Bottom, "Cực âm DC (0V / -)");
    }

    public void SetVoltage(double voltage)
    {
        OutputVoltage = Math.Clamp(Math.Round(voltage, 0), 5.0, 48.0);
    }

    public void StepVoltage(int deltaSteps)
    {
        int currentIdx = Array.FindIndex(SupportedVoltages, v => Math.Abs(v - OutputVoltage) < 0.5);
        if (currentIdx >= 0)
        {
            int nextIdx = Math.Clamp(currentIdx + deltaSteps, 0, SupportedVoltages.Length - 1);
            OutputVoltage = SupportedVoltages[nextIdx];
        }
        else
        {
            SetVoltage(OutputVoltage + deltaSteps * 12.0);
        }
    }

    public void CycleStandardVoltage()
    {
        int currentIdx = Array.FindIndex(SupportedVoltages, v => Math.Abs(v - OutputVoltage) < 0.5);
        int nextIdx = (currentIdx + 1) % SupportedVoltages.Length;
        OutputVoltage = SupportedVoltages[nextIdx];
    }

    public override IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs()
    {
        yield return (PlusTerminal, new PotentialToken(Id, PlusTerminal.Id, CurrentKind.DC, PhaseType.DC_Plus, OutputVoltage));
        yield return (MinusTerminal, new PotentialToken(Id, MinusTerminal.Id, CurrentKind.DC, PhaseType.DC_Minus, 0.0));
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["Voltage"] = OutputVoltage.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("Voltage", out var val) &&
            double.TryParse(val, CultureInfo.InvariantCulture, out double v))
        {
            SetVoltage(v);
        }
    }
}

/// <summary>
/// Nguồn xoay chiều 1 pha (chọn điện áp 24V / 110V / 220V / 380V AC, hỗ trợ giả lập mất pha L).
/// </summary>
public sealed class Ac1PhaseSourceComponent : ElectricalComponent
{
    public const string TypeId = "AC_1P_SOURCE";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.PowerSource;

    public double OutputVoltage { get; set; } = 220.0;
    public bool IsPhaseL1Lost { get; set; }

    public Terminal LineTerminal { get; }
    public Terminal NeutralTerminal { get; }

    public Ac1PhaseSourceComponent() : base("Nguồn AC 1P", 120, 100)
    {
        LineTerminal = AddTerminal("L", "L", 40, 100, TerminalSide.Bottom, "Dây pha nóng L");
        NeutralTerminal = AddTerminal("N", "N", 80, 100, TerminalSide.Bottom, "Dây trung tính N (0V)");
    }

    public override IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs()
    {
        if (!IsPhaseL1Lost)
        {
            yield return (LineTerminal, new PotentialToken(Id, LineTerminal.Id, CurrentKind.AC, PhaseType.L1, OutputVoltage));
        }
        yield return (NeutralTerminal, new PotentialToken(Id, NeutralTerminal.Id, CurrentKind.AC, PhaseType.N, 0.0));
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["OutputVoltage"] = OutputVoltage.ToString(CultureInfo.InvariantCulture),
        ["IsPhaseL1Lost"] = IsPhaseL1Lost.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("OutputVoltage", out var vStr) &&
            double.TryParse(vStr, CultureInfo.InvariantCulture, out double v))
            OutputVoltage = v;
        if (props.TryGetValue("IsPhaseL1Lost", out var lStr) && bool.TryParse(lStr, out bool lost))
            IsPhaseL1Lost = lost;
    }
}

/// <summary>
/// Nguồn xoay chiều 3 pha (5 chân: L1, L2, L3, N, PE).
/// Hỗ trợ chọn mức áp dây (220V / 380V) và nút gạt "Giả lập mất pha (Mất L1 / Mất L2 / Mất L3)".
/// </summary>
public sealed class Ac3PhaseSourceComponent : ElectricalComponent
{
    public const string TypeId = "AC_3P_SOURCE";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.PowerSource;

    /// <summary>Điện áp dây (380V chuẩn công nghiệp => áp pha 220V; hoặc 220V 3 pha => áp pha 127V).</summary>
    public double LineVoltage { get; set; } = 380.0;

    public bool IsL1Lost { get; set; }
    public bool IsL2Lost { get; set; }
    public bool IsL3Lost { get; set; }

    public Terminal L1Terminal { get; }
    public Terminal L2Terminal { get; }
    public Terminal L3Terminal { get; }
    public Terminal NeutralTerminal { get; }
    public Terminal PeTerminal { get; }

    public Ac3PhaseSourceComponent() : base("Nguồn 3 Pha 380V", 200, 100)
    {
        L1Terminal = AddTerminal("L1", "L1", 20, 100, TerminalSide.Bottom, "Pha L1 (góc 0°)");
        L2Terminal = AddTerminal("L2", "L2", 60, 100, TerminalSide.Bottom, "Pha L2 (góc -120°)");
        L3Terminal = AddTerminal("L3", "L3", 100, 100, TerminalSide.Bottom, "Pha L3 (góc +120°)");
        NeutralTerminal = AddTerminal("N", "N", 140, 100, TerminalSide.Bottom, "Trung tính N (0V)");
        PeTerminal = AddTerminal("PE", "PE", 180, 100, TerminalSide.Bottom, "Tiếp địa an toàn PE");
    }

    public override IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs()
    {
        // Lưu điện áp pha (LineVoltage / sqrt(3)) để phép trừ vector Phasor giữa 2 pha cho ra đúng LineVoltage (380V hoặc 220V)
        double phaseVolts = Math.Round(LineVoltage / Math.Sqrt(3.0), 1);

        if (!IsL1Lost)
            yield return (L1Terminal, new PotentialToken(Id, L1Terminal.Id, CurrentKind.AC, PhaseType.L1, phaseVolts));
        if (!IsL2Lost)
            yield return (L2Terminal, new PotentialToken(Id, L2Terminal.Id, CurrentKind.AC, PhaseType.L2, phaseVolts));
        if (!IsL3Lost)
            yield return (L3Terminal, new PotentialToken(Id, L3Terminal.Id, CurrentKind.AC, PhaseType.L3, phaseVolts));

        yield return (NeutralTerminal, new PotentialToken(Id, NeutralTerminal.Id, CurrentKind.AC, PhaseType.N, 0.0));
        yield return (PeTerminal, new PotentialToken(Id, PeTerminal.Id, CurrentKind.AC, PhaseType.PE, 0.0));
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["LineVoltage"] = LineVoltage.ToString(CultureInfo.InvariantCulture),
        ["IsL1Lost"] = IsL1Lost.ToString(),
        ["IsL2Lost"] = IsL2Lost.ToString(),
        ["IsL3Lost"] = IsL3Lost.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("LineVoltage", out var vStr) &&
            double.TryParse(vStr, CultureInfo.InvariantCulture, out double lv))
            LineVoltage = lv;
        if (props.TryGetValue("IsL1Lost", out var l1) && bool.TryParse(l1, out bool b1)) IsL1Lost = b1;
        if (props.TryGetValue("IsL2Lost", out var l2) && bool.TryParse(l2, out bool b2)) IsL2Lost = b2;
        if (props.TryGetValue("IsL3Lost", out var l3) && bool.TryParse(l3, out bool b3)) IsL3Lost = b3;
    }
}
