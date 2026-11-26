using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Giao diện chung cho mọi Cảm biến / Công tắc 2 dây mắc nối tiếp qua tải:
/// - Hỗ trợ sụt áp thực tế khi dẫn điện (~2.5V DC / ~3.5V AC).
/// - Kiểm tra phân cực DC (với cảm biến 2 dây DC: dương vào BN, tải ra BU).
/// - Tự động phát hiện lỗi đấu song song không qua tải -> Cháy/Hỏng (DAMAGED!) &amp; cắt MCB/Cầu chì.
/// </summary>
public interface ITwoWireSensorDevice
{
    bool IsTwoWireActiveMode { get; }
    Terminal PinPositiveOrLine { get; }
    Terminal PinNegativeOrLoad { get; }
    bool RequiresDcPolarity { get; }
    bool AcceptsAc { get; }
    bool AcceptsDc { get; }
    double VoltageDropAc { get; }
    double VoltageDropDc { get; }
    bool WantsToConduct { get; }
    bool IsDamaged { get; }
    bool IsReversePolarity { get; set; }
    bool IsConductingNow { get; set; }
    void DamageByNoLoadShort(string reason);
    void RepairDevice();
}

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
/// Cảm biến công nghiệp đa chế độ (2 dây AC/DC, 3 dây PNP/NPN, 4 dây NO+NC):
/// 1. Cảm biến tiệm cận cảm ứng từ (Inductive) — hỗ trợ 2 dây AC (90-250VAC), 2 dây DC (10-30VDC), 3/4 dây PNP/NPN.
/// 2. Cảm biến tiệm cận điện dung (Capacitive) — hỗ trợ 2 dây AC/DC, 3/4 dây PNP/NPN.
/// 3. Cảm biến quang (Photoelectric) — hỗ trợ 2 dây AC/DC, 3/4 dây PNP/NPN.
/// 4. Cảm biến siêu âm / Báo mức (Ultrasonic) — 3/4 dây PNP/NPN 10-30VDC.
/// </summary>
public sealed class IndustrialSensorComponent : ElectricalComponent, ITwoWireSensorDevice
{
    public const string TypeIdInductive = "SENSOR_INDUCTIVE";
    public const string TypeIdCapacitive = "SENSOR_CAPACITIVE";
    public const string TypeIdPhoto = "SENSOR_PHOTO";
    public const string TypeIdUltrasonic = "SENSOR_ULTRASONIC";

    public SensorKind SensorKind { get; }
    public SensorWiringMode WiringMode { get; private set; } = SensorWiringMode.ThreeWire_PNP;
    public SensorPolarity Polarity { get; set; } = SensorPolarity.PNP;
    public SensorOutputConfig OutputConfig { get; private set; } = SensorOutputConfig.NO_3Wire;
    public bool TwoWireNormallyClosed { get; set; }
    public PushButtonActionMode TriggerMode { get; set; } = PushButtonActionMode.Latching;

    public bool SupportsTwoWire => SensorKind is SensorKind.Inductive or SensorKind.Capacitive or SensorKind.Photoelectric;
    public bool IsTwoWireMode => WiringMode is SensorWiringMode.TwoWire_AC or SensorWiringMode.TwoWire_DC;

    public bool IsTargetDetected { get; set; }
    public bool IsPowered { get; private set; }
    public bool IsOut1Closed { get; private set; }
    public bool IsOut2Closed { get; private set; }

    // --- ITwoWireSensorDevice ---
    public bool IsTwoWireActiveMode => IsTwoWireMode;
    public Terminal PinPositiveOrLine => BnPlus;
    public Terminal PinNegativeOrLoad => BuMinus;
    public bool RequiresDcPolarity => WiringMode == SensorWiringMode.TwoWire_DC;
    public bool AcceptsAc => WiringMode == SensorWiringMode.TwoWire_AC;
    public bool AcceptsDc => WiringMode == SensorWiringMode.TwoWire_DC;
    public double VoltageDropAc => 3.5;
    public double VoltageDropDc => 2.5;
    public bool WantsToConduct => TwoWireNormallyClosed ? !IsTargetDetected : IsTargetDetected;
    public bool IsDamaged => IsBurnedOut;
    public bool IsReversePolarity { get; set; }
    public bool IsConductingNow { get; set; }

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
    public Terminal? BkOut1 { get; private set; }
    public Terminal? WhOut2 { get; private set; }

    public IndustrialSensorComponent(SensorKind kind)
        : base("B", 120, 120)
    {
        SensorKind = kind;
        RebuildTerminals();
    }

    public void SetWiringMode(SensorWiringMode mode)
    {
        if (!SupportsTwoWire && mode is SensorWiringMode.TwoWire_AC or SensorWiringMode.TwoWire_DC)
            return;
        if (WiringMode == mode) return;

        WiringMode = mode;
        switch (mode)
        {
            case SensorWiringMode.ThreeWire_PNP:
                Polarity = SensorPolarity.PNP;
                if (OutputConfig == SensorOutputConfig.FourWire_NO_NC) OutputConfig = SensorOutputConfig.NO_3Wire;
                Width = 120;
                break;
            case SensorWiringMode.ThreeWire_NPN:
                Polarity = SensorPolarity.NPN;
                if (OutputConfig == SensorOutputConfig.FourWire_NO_NC) OutputConfig = SensorOutputConfig.NO_3Wire;
                Width = 120;
                break;
            case SensorWiringMode.FourWire_NO_NC:
                OutputConfig = SensorOutputConfig.FourWire_NO_NC;
                Width = 160;
                break;
            case SensorWiringMode.TwoWire_AC:
            case SensorWiringMode.TwoWire_DC:
                Width = 100;
                break;
        }

        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    public void SetOutputConfig(SensorOutputConfig config)
    {
        if (IsTwoWireMode)
        {
            TwoWireNormallyClosed = config == SensorOutputConfig.NC_3Wire;
            return;
        }

        if (OutputConfig == config) return;
        OutputConfig = config;
        WiringMode = config == SensorOutputConfig.FourWire_NO_NC
            ? SensorWiringMode.FourWire_NO_NC
            : (Polarity == SensorPolarity.NPN ? SensorWiringMode.ThreeWire_NPN : SensorWiringMode.ThreeWire_PNP);
        Width = config == SensorOutputConfig.FourWire_NO_NC ? 160 : 120;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        BkOut1 = null;
        WhOut2 = null;

        if (IsTwoWireMode)
        {
            string bnDesc = WiringMode == SensorWiringMode.TwoWire_DC ? "BN (+DC)" : "BN (Line)";
            string buDesc = WiringMode == SensorWiringMode.TwoWire_DC ? "BU (Load/-)" : "BU (Load)";
            BnPlus = AddOrReuseTerminal(existing, "BN", "BN", 20, 120, TerminalSide.Bottom, bnDesc);
            BuMinus = AddOrReuseTerminal(existing, "BU", "BU", 80, 120, TerminalSide.Bottom, buDesc);
            return;
        }

        BnPlus = AddOrReuseTerminal(existing, "BN", "BN(+)", 20, 120, TerminalSide.Bottom);
        BuMinus = AddOrReuseTerminal(existing, "BU", "BU(-)", 60, 120, TerminalSide.Bottom);
        BkOut1 = AddOrReuseTerminal(existing, "BK", "BK(O1)", 100, 120, TerminalSide.Bottom);

        if (OutputConfig == SensorOutputConfig.FourWire_NO_NC)
        {
            WhOut2 = AddOrReuseTerminal(existing, "WH", "WH(O2)", 140, 120, TerminalSide.Bottom);
        }
    }

    public void DamageByNoLoadShort(string reason)
    {
        IsBurnedOut = true;
        IsConductingNow = false;
        IsOut1Closed = false;
        IsOut2Closed = false;
        BurnoutReason = reason;
    }

    public void RepairDevice() => RepairBurnout();

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsBurnedOut) yield break;

        if (IsTwoWireMode)
        {
            if (IsConductingNow)
                yield return new InternalConductivePath(BnPlus, BuMinus, $"{DisplayName} (2W BN-BU)");
            yield break;
        }

        if (!IsPowered || BkOut1 is null) yield break;

        Terminal commonRail = Polarity == SensorPolarity.PNP ? BnPlus : BuMinus;

        if (IsOut1Closed)
            yield return new InternalConductivePath(commonRail, BkOut1, $"{DisplayName} ({Polarity} BK)");

        if (IsOut2Closed && WhOut2 is not null)
            yield return new InternalConductivePath(commonRail, WhOut2, $"{DisplayName} ({Polarity} WH)");
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        if (IsTwoWireMode) yield break;
        yield return new InternalLoadPath(BnPlus, BuMinus, $"{DisplayName} Supply", IsPowered && !IsBurnedOut, 0.02, CurrentKind.DC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsPowered = false;
            IsOut1Closed = IsOut2Closed = false;
            IsConductingNow = false;
            return;
        }

        if (IsTwoWireMode)
        {
            // Ở chế độ 2 dây, trạng thái dẫn điện & cực tính đã được SimulationEngine kiểm định trước khi truyền thế
            IsPowered = BnPlus.ActivePotentials.Count > 0 || BuMinus.ActivePotentials.Count > 0;
            IsOut1Closed = IsConductingNow && !IsReversePolarity;
            IsOut2Closed = false;
            return;
        }

        double v = ElectricalAnalysisHelper.GetOperatingVoltageAcross(BnPlus, BuMinus, out var kind);

        if (v > 36.0)
        {
            IsBurnedOut = true;
            IsPowered = false;
            IsOut1Closed = IsOut2Closed = false;
            BurnoutReason = LocalizationManager.Format("Str.Fault.BurnoutSensorMsg", DisplayName, v, kind);
            return;
        }

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
                IsOut1Closed = IsTargetDetected;
                IsOut2Closed = !IsTargetDetected;
                break;
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsPowered = false;
        IsOut1Closed = false;
        IsOut2Closed = false;
        IsReversePolarity = false;
        IsConductingNow = false;
        if (TriggerMode == PushButtonActionMode.Momentary)
            IsTargetDetected = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["WiringMode"] = WiringMode.ToString(),
        ["Polarity"] = Polarity.ToString(),
        ["OutputConfig"] = OutputConfig.ToString(),
        ["TwoWireNC"] = TwoWireNormallyClosed.ToString(),
        ["TriggerMode"] = TriggerMode.ToString(),
        ["IsTargetDetected"] = (TriggerMode == PushButtonActionMode.Latching && IsTargetDetected).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("Polarity", out var pStr) && Enum.TryParse<SensorPolarity>(pStr, true, out var pol))
            Polarity = pol;
        if (props.TryGetValue("OutputConfig", out var oStr) && Enum.TryParse<SensorOutputConfig>(oStr, true, out var oc))
            SetOutputConfig(oc);
        if (props.TryGetValue("WiringMode", out var wStr) && Enum.TryParse<SensorWiringMode>(wStr, true, out var wm))
            SetWiringMode(wm);
        if (props.TryGetValue("TwoWireNC", out var ncStr) && bool.TryParse(ncStr, out bool nc))
            TwoWireNormallyClosed = nc;
        if (props.TryGetValue("TriggerMode", out var tStr) && Enum.TryParse<PushButtonActionMode>(tStr, true, out var tm))
            TriggerMode = tm;
        if (props.TryGetValue("IsTargetDetected", out var dStr) && bool.TryParse(dStr, out bool det))
            IsTargetDetected = det;
    }
}

/// <summary>
/// 1. Cảm biến từ xi-lanh khí nén 2 dây (Cylinder Reed Switch — kiểu SMC D-A93):
/// - Thân dẹt gắn rãnh xi-lanh, 2 chân BN - BU không phân cực AC/DC 5–240V, đèn LED đỏ sáng khi tác động.
/// </summary>
public sealed class CylinderReedSwitchComponent : ElectricalComponent, ITwoWireSensorDevice
{
    public const string TypeId = "SENSOR_REED_CYL";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Sensor;

    public bool IsNormallyClosed { get; set; }
    public PushButtonActionMode TriggerMode { get; set; } = PushButtonActionMode.Latching;
    public bool IsMagnetDetected { get; set; }

    public Terminal PinBn { get; }
    public Terminal PinBu { get; }

    public bool IsTwoWireActiveMode => true;
    public Terminal PinPositiveOrLine => PinBn;
    public Terminal PinNegativeOrLoad => PinBu;
    public bool RequiresDcPolarity => false; // AC/DC 5-240V không phân cực
    public bool AcceptsAc => true;
    public bool AcceptsDc => true;
    public double VoltageDropAc => 3.5;
    public double VoltageDropDc => 2.5;
    public bool WantsToConduct => IsNormallyClosed ? !IsMagnetDetected : IsMagnetDetected;
    public bool IsDamaged => IsBurnedOut;
    public bool IsReversePolarity { get; set; }
    public bool IsConductingNow { get; set; }

    public CylinderReedSwitchComponent() : base("RS1", 120, 84)
    {
        PinBn = AddTerminal("BN", "BN", 30, 84, TerminalSide.Bottom, "Chân BN (Nâu — AC/DC 5-240V)");
        PinBu = AddTerminal("BU", "BU", 90, 84, TerminalSide.Bottom, "Chân BU (Xanh dương — AC/DC 5-240V)");
    }

    public void DamageByNoLoadShort(string reason)
    {
        IsBurnedOut = true;
        IsConductingNow = false;
        BurnoutReason = reason;
    }

    public void RepairDevice() => RepairBurnout();

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsBurnedOut && IsConductingNow)
            yield return new InternalConductivePath(PinBn, PinBu, $"{DisplayName} (SMC D-A93 BN-BU)");
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsConductingNow = false;
        IsReversePolarity = false;
        if (TriggerMode == PushButtonActionMode.Momentary)
            IsMagnetDetected = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["IsNormallyClosed"] = IsNormallyClosed.ToString(),
        ["TriggerMode"] = TriggerMode.ToString(),
        ["IsMagnetDetected"] = (TriggerMode == PushButtonActionMode.Latching && IsMagnetDetected).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("IsNormallyClosed", out var ncStr) && bool.TryParse(ncStr, out bool nc))
            IsNormallyClosed = nc;
        if (props.TryGetValue("TriggerMode", out var tStr) && Enum.TryParse<PushButtonActionMode>(tStr, true, out var tm))
            TriggerMode = tm;
        if (props.TryGetValue("IsMagnetDetected", out var dStr) && bool.TryParse(dStr, out bool det))
            IsMagnetDetected = det;
    }
}

/// <summary>
/// 2. Công tắc nhiệt độ lưỡng kim 2 dây (Thermal Switch — kiểu KSD301):
/// - Hình khối đĩa nhiệt có tai bắt vít, 2 chân 1 - 2.
/// - Bấm chuột trong mô phỏng để giả lập Quá nhiệt (Overheat): NC ngắt mạch bảo vệ, NO đóng bật quạt/còi.
/// </summary>
public sealed class ThermalSwitchComponent : ElectricalComponent, ITwoWireSensorDevice
{
    public const string TypeId = "SWITCH_THERMAL_KSD301";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Sensor;

    /// <summary>Mặc định NC (Thường đóng: quá nhiệt thì mở ngắt bảo vệ).</summary>
    public bool IsNormallyClosed { get; set; } = true;
    public bool IsOverheated { get; set; }

    public Terminal Pin1 { get; }
    public Terminal Pin2 { get; }

    public bool IsTwoWireActiveMode => true;
    public Terminal PinPositiveOrLine => Pin1;
    public Terminal PinNegativeOrLoad => Pin2;
    public bool RequiresDcPolarity => false;
    public bool AcceptsAc => true;
    public bool AcceptsDc => true;
    public double VoltageDropAc => 3.5;
    public double VoltageDropDc => 2.5;
    public bool WantsToConduct => IsNormallyClosed ? !IsOverheated : IsOverheated;
    public bool IsDamaged => IsBurnedOut;
    public bool IsReversePolarity { get; set; }
    public bool IsConductingNow { get; set; }

    public ThermalSwitchComponent() : base("TS1", 100, 100)
    {
        Pin1 = AddTerminal("1", "1", 20, 100, TerminalSide.Bottom, "Chân 1 (KSD301)");
        Pin2 = AddTerminal("2", "2", 80, 100, TerminalSide.Bottom, "Chân 2 (KSD301)");
    }

    public void ToggleOverheat() => IsOverheated = !IsOverheated;

    public void DamageByNoLoadShort(string reason)
    {
        IsBurnedOut = true;
        IsConductingNow = false;
        BurnoutReason = reason;
    }

    public void RepairDevice() => RepairBurnout();

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsBurnedOut && IsConductingNow)
            yield return new InternalConductivePath(Pin1, Pin2, $"{DisplayName} (KSD301 1-2)");
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsConductingNow = false;
        IsReversePolarity = false;
        IsOverheated = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["IsNormallyClosed"] = IsNormallyClosed.ToString(),
        ["IsOverheated"] = IsOverheated.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("IsNormallyClosed", out var ncStr) && bool.TryParse(ncStr, out bool nc))
            IsNormallyClosed = nc;
        if (props.TryGetValue("IsOverheated", out var ohStr) && bool.TryParse(ohStr, out bool oh))
            IsOverheated = oh;
    }
}

/// <summary>
/// 3. Phao báo mức nước 2 dây (Float Switch):
/// - 2 chân COM - OUT; chuyển đổi trực quan giữa Cạn nước (Low) và Đầy nước (Full).
/// - WaterSupply (Cấp nước): đóng khi Cạn, ngắt khi Đầy.
/// - Drainage (Thoát nước): đóng khi Đầy, ngắt khi Cạn.
/// </summary>
public sealed class FloatSwitchComponent : ElectricalComponent, ITwoWireSensorDevice
{
    public const string TypeId = "SWITCH_FLOAT_2W";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Sensor;

    public FloatSwitchMode OperatingMode { get; set; } = FloatSwitchMode.WaterSupply;
    public bool IsWaterFull { get; set; }

    public Terminal PinCom { get; }
    public Terminal PinOut { get; }

    public bool IsTwoWireActiveMode => true;
    public Terminal PinPositiveOrLine => PinCom;
    public Terminal PinNegativeOrLoad => PinOut;
    public bool RequiresDcPolarity => false;
    public bool AcceptsAc => true;
    public bool AcceptsDc => true;
    public double VoltageDropAc => 3.5;
    public double VoltageDropDc => 2.5;
    public bool WantsToConduct => OperatingMode == FloatSwitchMode.WaterSupply ? !IsWaterFull : IsWaterFull;
    public bool IsDamaged => IsBurnedOut;
    public bool IsReversePolarity { get; set; }
    public bool IsConductingNow { get; set; }

    public FloatSwitchComponent() : base("FS1", 110, 120)
    {
        PinCom = AddTerminal("COM", "COM", 25, 120, TerminalSide.Bottom, "Chân chung COM (Phao mức nước)");
        PinOut = AddTerminal("OUT", "OUT", 85, 120, TerminalSide.Bottom, "Chân ra OUT (Phao mức nước)");
    }

    public void ToggleWaterLevel() => IsWaterFull = !IsWaterFull;

    public void DamageByNoLoadShort(string reason)
    {
        IsBurnedOut = true;
        IsConductingNow = false;
        BurnoutReason = reason;
    }

    public void RepairDevice() => RepairBurnout();

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsBurnedOut && IsConductingNow)
            yield return new InternalConductivePath(PinCom, PinOut, $"{DisplayName} (Float COM-OUT)");
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsConductingNow = false;
        IsReversePolarity = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["OperatingMode"] = OperatingMode.ToString(),
        ["IsWaterFull"] = IsWaterFull.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("OperatingMode", out var mStr) && Enum.TryParse<FloatSwitchMode>(mStr, true, out var mode))
            OperatingMode = mode;
        if (props.TryGetValue("IsWaterFull", out var fStr) && bool.TryParse(fStr, out bool full))
            IsWaterFull = full;
    }
}
