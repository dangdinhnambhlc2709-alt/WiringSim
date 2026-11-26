using WiringSim.Models.Enums;

namespace WiringSim.Models.Components.Loads;

/// <summary>
/// 1. Đồng hồ đo Volt gắn tủ (Panel Voltmeter — V1, V2):
/// - Đấu song song (nội trở rất lớn, không dẫn dòng ngắn mạch).
/// - Chọn loại dòng đo AC / DC, dải đo (0–50V, 0–300V, 0–500V), hiển thị Số (Digital LED) hoặc Kim (Analog).
/// - Báo "OL" (Overload) khi điện áp vượt quá dải đo đã chọn.
/// </summary>
public sealed class VoltmeterComponent : ElectricalComponent
{
    public const string TypeId = "VOLTMETER";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Meter;

    public CurrentKind MeasureKind { get; set; } = CurrentKind.AC;
    public VoltmeterRange Range { get; set; } = VoltmeterRange.Range_500V;
    public MeterDisplayStyle DisplayStyle { get; set; } = MeterDisplayStyle.Digital;

    public double MeasuredVoltage { get; private set; }
    public bool IsOverRange { get; private set; }
    public double NeedleRatio { get; private set; } // 0.0 -> 1.0 mượt ở 60 FPS

    public Terminal V1 { get; }
    public Terminal V2 { get; }

    public VoltmeterComponent() : base("PV", 100, 120)
    {
        V1 = AddTerminal("V1", "V1(+)", 20, 120, TerminalSide.Bottom);
        V2 = AddTerminal("V2", "V2(-)", 80, 120, TerminalSide.Bottom);
    }

    public (double MaxVolts, string Label) GetRangeSpec() => Range switch
    {
        VoltmeterRange.Range_50V => (50.0, "0–50V"),
        VoltmeterRange.Range_300V => (300.0, "0–300V"),
        _ => (500.0, "0–500V")
    };

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        double v = ElectricalAnalysisHelper.GetOperatingVoltageAcross(V1, V2, out var kind);
        var (maxV, _) = GetRangeSpec();

        if (kind == CurrentKind.None || kind != MeasureKind)
        {
            MeasuredVoltage = 0.0;
            IsOverRange = false;
            return;
        }

        MeasuredVoltage = v;
        IsOverRange = v > maxV * 1.02;
    }

    public override void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
        var (maxV, _) = GetRangeSpec();
        double target = !isSimulating
            ? 0.0
            : (IsOverRange ? 1.05 : Math.Clamp(MeasuredVoltage / maxV, 0.0, 1.0));

        NeedleRatio += (target - NeedleRatio) * Math.Min(1.0, frameDeltaSeconds * 8.0);
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        MeasuredVoltage = 0;
        IsOverRange = false;
        NeedleRatio = 0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["MeasureKind"] = MeasureKind.ToString(),
        ["Range"] = Range.ToString(),
        ["DisplayStyle"] = DisplayStyle.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("MeasureKind", out var kStr) && Enum.TryParse<CurrentKind>(kStr, true, out var k))
            MeasureKind = k;
        if (props.TryGetValue("Range", out var rStr) && Enum.TryParse<VoltmeterRange>(rStr, true, out var r))
            Range = r;
        if (props.TryGetValue("DisplayStyle", out var dStr) && Enum.TryParse<MeterDisplayStyle>(dStr, true, out var d))
            DisplayStyle = d;
    }
}

/// <summary>
/// 2. Đồng hồ đo Ampe gắn tủ (Panel Ammeter — I1, I2):
/// - Nội trở ~ 0 (phải đấu nối tiếp với tải hoặc nối vào thứ cấp S1-S2 của Biến dòng CT).
/// - Nếu đấu nhầm song song trực tiếp vào nguồn (L-N, L1-L2, +/-) sẽ gây ngắn mạch &amp; hiện cảnh báo chuyên biệt!
/// - Hỗ trợ đo trực tiếp (0–10A, 0–30A) hoặc đo qua Biến dòng CT (50/5A, 100/5A, 150/5A, 200/5A).
/// </summary>
public sealed class AmmeterComponent : ElectricalComponent
{
    public const string TypeId = "AMMETER";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Meter;

    public CurrentKind MeasureKind { get; set; } = CurrentKind.AC;
    public AmmeterRange Range { get; set; } = AmmeterRange.Direct_30A;
    public MeterDisplayStyle DisplayStyle { get; set; } = MeterDisplayStyle.Digital;

    /// <summary>Dòng điện thực tế chạy qua I1-I2 (do Simulation Engine tính từ nhánh tải hoặc thứ cấp CT).</summary>
    public double RawInputAmps { get; set; }
    public CurrentKind RawInputKind { get; set; } = CurrentKind.None;

    public double DisplayedAmps { get; private set; }
    public bool IsOverRange { get; private set; }
    public double NeedleRatio { get; private set; }

    public Terminal I1 { get; }
    public Terminal I2 { get; }

    public AmmeterComponent() : base("PA", 100, 120)
    {
        I1 = AddTerminal("I1", "I1", 20, 0, TerminalSide.Top);
        I2 = AddTerminal("I2", "I2", 20, 120, TerminalSide.Bottom);
    }

    public (double MaxDisplayAmps, double CtMultiplier, string Label) GetRangeSpec() => Range switch
    {
        AmmeterRange.Direct_10A => (10.0, 1.0, "0–10A"),
        AmmeterRange.Direct_30A => (30.0, 1.0, "0–30A"),
        AmmeterRange.ViaCT_50_5 => (50.0, 50.0 / 5.0, "CT 50/5A"),
        AmmeterRange.ViaCT_100_5 => (100.0, 100.0 / 5.0, "CT 100/5A"),
        AmmeterRange.ViaCT_150_5 => (150.0, 150.0 / 5.0, "CT 150/5A"),
        AmmeterRange.ViaCT_200_5 => (200.0, 200.0 / 5.0, "CT 200/5A"),
        _ => (30.0, 1.0, "0–30A")
    };

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Nội trở Shunt của Ampe kế gần bằng 0!
        yield return new InternalConductivePath(I1, I2, $"{DisplayName} (I1-I2 Shunt)");
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        var (maxA, mult, _) = GetRangeSpec();

        if (RawInputAmps <= 0.001 || RawInputKind == CurrentKind.None || RawInputKind != MeasureKind)
        {
            DisplayedAmps = 0.0;
            IsOverRange = false;
            return;
        }

        double scaled = RawInputAmps * mult;
        DisplayedAmps = Math.Round(scaled, 2);
        IsOverRange = scaled > maxA * 1.05;
    }

    public override void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
        var (maxA, _, _) = GetRangeSpec();
        double target = !isSimulating
            ? 0.0
            : (IsOverRange ? 1.05 : Math.Clamp(DisplayedAmps / maxA, 0.0, 1.0));

        NeedleRatio += (target - NeedleRatio) * Math.Min(1.0, frameDeltaSeconds * 8.0);
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        RawInputAmps = 0;
        RawInputKind = CurrentKind.None;
        DisplayedAmps = 0;
        IsOverRange = false;
        NeedleRatio = 0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["MeasureKind"] = MeasureKind.ToString(),
        ["Range"] = Range.ToString(),
        ["DisplayStyle"] = DisplayStyle.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("MeasureKind", out var kStr) && Enum.TryParse<CurrentKind>(kStr, true, out var k))
            MeasureKind = k;
        if (props.TryGetValue("Range", out var rStr) && Enum.TryParse<AmmeterRange>(rStr, true, out var r))
            Range = r;
        if (props.TryGetValue("DisplayStyle", out var dStr) && Enum.TryParse<MeterDisplayStyle>(dStr, true, out var d))
            DisplayStyle = d;
    }
}

/// <summary>
/// 3. Biến dòng đo lường (Current Transformer — CT):
/// - Sơ cấp luồn dây động lực: P1 - P2.
/// - Thứ cấp nối về Ampe kế: S1 - S2 (tối đa 5A quy đổi theo tỷ số 50/5A, 100/5A, 150/5A, 200/5A).
/// </summary>
public sealed class CurrentTransformerComponent : ElectricalComponent
{
    public const string TypeId = "CURRENT_TRANSFORMER";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Meter;

    public CtRatio Ratio { get; set; } = CtRatio.Ratio_50_5;

    public double PrimaryCurrentAmps { get; set; }
    public double SecondaryCurrentAmps { get; set; }

    public Terminal P1 { get; }
    public Terminal P2 { get; }
    public Terminal S1 { get; }
    public Terminal S2 { get; }

    public CurrentTransformerComponent() : base("CT", 120, 120)
    {
        P1 = AddTerminal("P1", "P1", 20, 0, TerminalSide.Top);
        P2 = AddTerminal("P2", "P2", 20, 120, TerminalSide.Bottom);
        S1 = AddTerminal("S1", "S1", 60, 120, TerminalSide.Bottom);
        S2 = AddTerminal("S2", "S2", 100, 120, TerminalSide.Bottom);
    }

    public (double PrimaryRatedAmps, string Label) GetRatioSpec() => Ratio switch
    {
        CtRatio.Ratio_50_5 => (50.0, "50/5A"),
        CtRatio.Ratio_100_5 => (100.0, "100/5A"),
        CtRatio.Ratio_150_5 => (150.0, "150/5A"),
        CtRatio.Ratio_200_5 => (200.0, "200/5A"),
        _ => (50.0, "50/5A")
    };

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Dây cái sơ cấp xuyên thẳng qua lõi xuyến P1 -> P2
        yield return new InternalConductivePath(P1, P2, $"{DisplayName} (P1-P2 Primary)");
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        PrimaryCurrentAmps = 0;
        SecondaryCurrentAmps = 0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["Ratio"] = Ratio.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("Ratio", out var rStr) && Enum.TryParse<CtRatio>(rStr, true, out var r))
            Ratio = r;
    }
}
