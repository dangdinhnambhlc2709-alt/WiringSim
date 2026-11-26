using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Công tắc hành trình công nghiệp tay gạt bánh xe con lăn (kiểu ME-8104):
/// - Thân xanh/đen công nghiệp có cần gạt kim loại gắn bánh xe con lăn nhô phía trên.
/// - Tích hợp 2 cặp tiếp điểm độc lập: NC (1-2) và NO (3-4).
/// - Hỗ trợ 2 chế độ thử nghiệm: Nhấn giữ (Momentary) hoặc Nhấn khóa (Latching).
/// </summary>
public sealed class LimitSwitchComponent : ElectricalComponent
{
    public const string TypeId = "LIMIT_SWITCH";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public PushButtonActionMode ActionMode { get; set; } = PushButtonActionMode.Latching;
    public bool IsActuated { get; set; }

    public Terminal Nc1 { get; }
    public Terminal Nc2 { get; }
    public Terminal No3 { get; }
    public Terminal No4 { get; }

    // Tương thích ngược cho mã cũ
    public Terminal No13 => No3;
    public Terminal No14 => No4;
    public Terminal Nc21 => Nc1;
    public Terminal Nc22 => Nc2;

    public LimitSwitchComponent() : base("SQ", 100, 120)
    {
        Nc1 = AddTerminal("1", "1(NC)", 20, 0, TerminalSide.Top, "Tiếp điểm thường đóng NC (1)");
        No3 = AddTerminal("3", "3(NO)", 80, 0, TerminalSide.Top, "Tiếp điểm thường mở NO (3)");
        Nc2 = AddTerminal("2", "2(NC)", 20, 120, TerminalSide.Bottom, "Tiếp điểm thường đóng NC (2)");
        No4 = AddTerminal("4", "4(NO)", 80, 120, TerminalSide.Bottom, "Tiếp điểm thường mở NO (4)");
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsActuated)
            yield return new InternalConductivePath(No3, No4, $"{DisplayName} (3-4 NO)");
        else
            yield return new InternalConductivePath(Nc1, Nc2, $"{DisplayName} (1-2 NC)");
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        if (ActionMode == PushButtonActionMode.Momentary)
            IsActuated = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["ActionMode"] = ActionMode.ToString(),
        ["IsActuated"] = (ActionMode == PushButtonActionMode.Latching && IsActuated).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ActionMode", out var mStr) && Enum.TryParse<PushButtonActionMode>(mStr, true, out var m))
            ActionMode = m;
        if (props.TryGetValue("IsActuated", out var aStr) && bool.TryParse(aStr, out bool act))
            IsActuated = act;
    }
}

/// <summary>
/// Cảm biến công nghiệp 3 dây / 4 dây (10–30VDC):
/// 1. Cảm biến tiệm cận cảm ứng từ (Inductive)
/// 2. Cảm biến tiệm cận điện dung (Capacitive)
/// 3. Cảm biến quang (Photoelectric)
/// 4. Cảm biến siêu âm / Báo mức (Ultrasonic)
/// Chân chuẩn quốc tế: BN (+V Nâu), BU (0V Xanh dương), BK (OUT1 Đen), WH (OUT2 Trắng khi chọn 4 dây).
/// Hỗ trợ ngõ ra PNP (xuất +V) hoặc NPN (kéo về 0V).
/// </summary>
public sealed class IndustrialSensorComponent : ElectricalComponent
{
    public const string TypeIdInductive = "SENSOR_INDUCTIVE";
    public const string TypeIdCapacitive = "SENSOR_CAPACITIVE";
    public const string TypeIdPhoto = "SENSOR_PHOTO";
    public const string TypeIdUltrasonic = "SENSOR_ULTRASONIC";

    public SensorKind SensorKind { get; }
    public SensorPolarity Polarity { get; set; } = SensorPolarity.PNP;
    public SensorOutputConfig OutputConfig { get; private set; } = SensorOutputConfig.NO_3Wire;
    public PushButtonActionMode TriggerMode { get; set; } = PushButtonActionMode.Latching;

    public bool IsTargetDetected { get; set; }
    public bool IsPowered { get; private set; }
    public bool IsOut1Closed { get; private set; }
    public bool IsOut2Closed { get; private set; }

    public override string TypeKey => SensorKind switch
    {
        SensorKind.Capacitive => TypeIdCapacitive,
        SensorKind.Photoelectric => TypeIdPhoto,
        SensorKind.Ultrasonic => TypeIdUltrasonic,
        _ => TypeIdInductive
    };

    public override ComponentCategory Category => ComponentCategory.Sensor;

    public Terminal BnPlus { get; private set; } = null!;
    public Terminal BuMinus { get; private set; } = null!;
    public Terminal BkOut1 { get; private set; } = null!;
    public Terminal? WhOut2 { get; private set; }

    public IndustrialSensorComponent(SensorKind kind)
        : base("B", 120, 120)
    {
        SensorKind = kind;
        RebuildTerminals();
    }

    public void SetOutputConfig(SensorOutputConfig config)
    {
        if (OutputConfig == config) return;
        OutputConfig = config;
        Width = config == SensorOutputConfig.FourWire_NO_NC ? 160 : 120;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        WhOut2 = null;

        BnPlus = AddOrReuseTerminal(existing, "BN", "BN(+)", 20, 120, TerminalSide.Bottom);
        BuMinus = AddOrReuseTerminal(existing, "BU", "BU(-)", 60, 120, TerminalSide.Bottom);
        BkOut1 = AddOrReuseTerminal(existing, "BK", "BK(O1)", 100, 120, TerminalSide.Bottom);

        if (OutputConfig == SensorOutputConfig.FourWire_NO_NC)
        {
            WhOut2 = AddOrReuseTerminal(existing, "WH", "WH(O2)", 140, 120, TerminalSide.Bottom);
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsPowered || IsBurnedOut) yield break;

        // PNP: transistor xuất nguồn dương BN(+) ra chân BK / WH
        // NPN: transistor kéo chân BK / WH xuống âm nguồn BU(-)
        Terminal commonRail = Polarity == SensorPolarity.PNP ? BnPlus : BuMinus;

        if (IsOut1Closed)
            yield return new InternalConductivePath(commonRail, BkOut1, $"{DisplayName} ({Polarity} BK)");

        if (IsOut2Closed && WhOut2 is not null)
            yield return new InternalConductivePath(commonRail, WhOut2, $"{DisplayName} ({Polarity} WH)");
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        yield return new InternalLoadPath(BnPlus, BuMinus, $"{DisplayName} Supply", IsPowered && !IsBurnedOut, 0.02, CurrentKind.DC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsPowered = false;
            IsOut1Closed = IsOut2Closed = false;
            return;
        }

        double v = ElectricalAnalysisHelper.GetOperatingVoltageAcross(BnPlus, BuMinus, out var kind);

        // Nếu cấp quá áp > 36V (ví dụ đấu nhầm 48VDC hoặc 220VAC vào cảm biến 10-30VDC) -> Cháy cảm biến!
        if (v > 36.0)
        {
            IsBurnedOut = true;
            IsPowered = false;
            IsOut1Closed = IsOut2Closed = false;
            BurnoutReason = LocalizationManager.Format("Str.Fault.BurnoutSensorMsg", DisplayName, v, kind);
            return;
        }

        // Kiểm tra đúng cực tính DC: BN phải là DC_Plus (10..36V) và BU phải là DC_Minus!
        bool correctPolarity = kind == CurrentKind.DC &&
                               v >= 9.5 &&
                               BnPlus.ActivePotentials.Any(p => p.CurrentKind == CurrentKind.DC && p.Phase == PhaseType.DC_Plus) &&
                               BuMinus.ActivePotentials.Any(p => p.CurrentKind == CurrentKind.DC && p.Phase == PhaseType.DC_Minus);

        IsPowered = correctPolarity;

        if (!IsPowered)
        {
            IsOut1Closed = false;
            IsOut2Closed = false;
            return;
        }

        switch (OutputConfig)
        {
            case SensorOutputConfig.NO_3Wire:
                IsOut1Closed = IsTargetDetected;
                IsOut2Closed = false;
                break;

            case SensorOutputConfig.NC_3Wire:
                IsOut1Closed = !IsTargetDetected;
                IsOut2Closed = false;
                break;

            case SensorOutputConfig.FourWire_NO_NC:
                IsOut1Closed = IsTargetDetected;  // BK = NO
                IsOut2Closed = !IsTargetDetected; // WH = NC
                break;
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsPowered = false;
        IsOut1Closed = false;
        IsOut2Closed = false;
        if (TriggerMode == PushButtonActionMode.Momentary)
            IsTargetDetected = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["Polarity"] = Polarity.ToString(),
        ["OutputConfig"] = OutputConfig.ToString(),
        ["TriggerMode"] = TriggerMode.ToString(),
        ["IsTargetDetected"] = (TriggerMode == PushButtonActionMode.Latching && IsTargetDetected).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("Polarity", out var pStr) && Enum.TryParse<SensorPolarity>(pStr, true, out var pol))
            Polarity = pol;
        if (props.TryGetValue("OutputConfig", out var oStr) && Enum.TryParse<SensorOutputConfig>(oStr, true, out var oc))
            SetOutputConfig(oc);
        if (props.TryGetValue("TriggerMode", out var tStr) && Enum.TryParse<PushButtonActionMode>(tStr, true, out var tm))
            TriggerMode = tm;
        if (props.TryGetValue("IsTargetDetected", out var dStr) && bool.TryParse(dStr, out bool det))
            IsTargetDetected = det;
    }
}
