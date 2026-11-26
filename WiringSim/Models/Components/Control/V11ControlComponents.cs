using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Nút nhấn có đèn báo công nghiệp (Illuminated Pushbutton — 6 chân):
/// - Cặp tiếp điểm NC (11-12) + cặp tiếp điểm NO (23-24) độc lập.
/// - 2 chân cấp nguồn đèn báo (X1, X2) với điện áp định mức 12VDC / 24VDC / 220VAC.
/// - Hỗ trợ chế độ Tự nhả (Momentary) / Tự duy trì (Latching) &amp; 5 màu (Xanh lá, Đỏ, Vàng, Xanh dương, Trắng).
/// </summary>
public sealed class IlluminatedPushButtonComponent : ElectricalComponent
{
    public const string TypeId = "PUSHBUTTON_ILLUMINATED";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public PushButtonActionMode ActionMode { get; set; } = PushButtonActionMode.Momentary;
    public CoilVoltageRating VoltageRating { get; set; } = CoilVoltageRating.AC_220V;
    public PilotLightColor ButtonColor { get; set; } = PilotLightColor.Green;

    public bool IsPressed { get; set; }
    public bool IsLit { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    public Terminal Nc11 { get; }
    public Terminal Nc12 { get; }
    public Terminal No23 { get; }
    public Terminal No24 { get; }
    public Terminal LampX1 { get; }
    public Terminal LampX2 { get; }

    public IlluminatedPushButtonComponent() : base("SB", 140, 100)
    {
        Nc11 = AddTerminal("11", "11(NC)", 20, 0, TerminalSide.Top, "Tiếp điểm thường đóng 11 (NC)");
        No23 = AddTerminal("23", "23(NO)", 70, 0, TerminalSide.Top, "Tiếp điểm thường mở 23 (NO)");
        LampX1 = AddTerminal("X1", "X1", 120, 0, TerminalSide.Top, "Chân nguồn đèn báo X1");

        Nc12 = AddTerminal("12", "12(NC)", 20, 100, TerminalSide.Bottom, "Tiếp điểm thường đóng 12 (NC)");
        No24 = AddTerminal("24", "24(NO)", 70, 100, TerminalSide.Bottom, "Tiếp điểm thường mở 24 (NO)");
        LampX2 = AddTerminal("X2", "X2", 120, 100, TerminalSide.Bottom, "Chân nguồn đèn báo X2");
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsPressed)
            yield return new InternalConductivePath(Nc11, Nc12, $"{DisplayName} (11-12 NC)");
        else
            yield return new InternalConductivePath(No23, No24, $"{DisplayName} (23-24 NO)");
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(VoltageRating);
        yield return new InternalLoadPath(LampX1, LampX2, $"{DisplayName} Lamp", IsLit && !IsBurnedOut, 0.04, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsLit = false;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(LampX1, LampX2, VoltageRating, out double v, out var kind);
        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLbl) = ElectricalAnalysisHelper.GetRatingSpec(VoltageRating);
            IsBurnedOut = true;
            IsLit = false;
            BurnoutReason = LocalizationManager.Format("Str.Fault.BurnoutLampMsg", DisplayName, v, kind, ratingLbl);
            return;
        }

        IsLit = SupplyStatus == RatedSupplyStatus.Normal;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsLit = false;
        SupplyStatus = RatedSupplyStatus.Unpowered;
        if (ActionMode == PushButtonActionMode.Momentary)
            IsPressed = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["ActionMode"] = ActionMode.ToString(),
        ["VoltageRating"] = VoltageRating.ToString(),
        ["ButtonColor"] = ButtonColor.ToString(),
        ["IsPressed"] = (ActionMode == PushButtonActionMode.Latching && IsPressed).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ActionMode", out var mStr) && Enum.TryParse<PushButtonActionMode>(mStr, true, out var am))
            ActionMode = am;
        if (props.TryGetValue("VoltageRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var vr))
            VoltageRating = vr;
        if (props.TryGetValue("ButtonColor", out var cStr) && Enum.TryParse<PilotLightColor>(cStr, true, out var col))
            ButtonColor = col;
        if (props.TryGetValue("IsPressed", out var pStr) && bool.TryParse(pStr, out bool p))
            IsPressed = p;
    }
}

/// <summary>
/// Nút dừng khẩn cấp đầu nấm đỏ đế vàng (Emergency Stop / E-Stop):
/// - Cơ chế Nhấn khóa – Xoay nhả (bấm lần 1 khóa chặt trạng thái tác động, bấm lần 2 xoay nhả).
/// - Cấu hình tiếp điểm: 1NC (11-12) + 1NO (23-24) [mặc định], Chỉ 1NC (11-12), hoặc 2NC (11-12 &amp; 21-22).
/// </summary>
public sealed class EmergencyStopComponent : ElectricalComponent
{
    public const string TypeId = "EMERGENCY_STOP";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public EmergencyStopContactConfig ContactConfig { get; private set; } = EmergencyStopContactConfig.Dual1NC1NO;
    public bool IsEngaged { get; set; }

    public Terminal Nc11 { get; private set; } = null!;
    public Terminal Nc12 { get; private set; } = null!;
    public Terminal? SecondTop { get; private set; }
    public Terminal? SecondBot { get; private set; }

    public EmergencyStopComponent() : base("ES1", 100, 110)
    {
        RebuildTerminals();
    }

    public void SetContactConfig(EmergencyStopContactConfig cfg)
    {
        if (ContactConfig == cfg) return;
        ContactConfig = cfg;
        Width = cfg == EmergencyStopContactConfig.Only1NC ? 80 : 100;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        SecondTop = SecondBot = null;

        switch (ContactConfig)
        {
            case EmergencyStopContactConfig.Only1NC:
                Nc11 = AddOrReuseTerminal(existing, "11", "11(NC)", 40, 0, TerminalSide.Top);
                Nc12 = AddOrReuseTerminal(existing, "12", "12(NC)", 40, 110, TerminalSide.Bottom);
                break;

            case EmergencyStopContactConfig.Dual2NC:
                Nc11 = AddOrReuseTerminal(existing, "11", "11(NC)", 20, 0, TerminalSide.Top);
                Nc12 = AddOrReuseTerminal(existing, "12", "12(NC)", 20, 110, TerminalSide.Bottom);
                SecondTop = AddOrReuseTerminal(existing, "21", "21(NC)", 80, 0, TerminalSide.Top);
                SecondBot = AddOrReuseTerminal(existing, "22", "22(NC)", 80, 110, TerminalSide.Bottom);
                break;

            default: // Dual1NC1NO
                Nc11 = AddOrReuseTerminal(existing, "11", "11(NC)", 20, 0, TerminalSide.Top);
                Nc12 = AddOrReuseTerminal(existing, "12", "12(NC)", 20, 110, TerminalSide.Bottom);
                SecondTop = AddOrReuseTerminal(existing, "23", "23(NO)", 80, 0, TerminalSide.Top);
                SecondBot = AddOrReuseTerminal(existing, "24", "24(NO)", 80, 110, TerminalSide.Bottom);
                break;
        }
    }

    public void ToggleEngage() => IsEngaged = !IsEngaged;

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsEngaged)
        {
            yield return new InternalConductivePath(Nc11, Nc12, $"{DisplayName} (11-12 NC)");
            if (ContactConfig == EmergencyStopContactConfig.Dual2NC && SecondTop is not null && SecondBot is not null)
                yield return new InternalConductivePath(SecondTop, SecondBot, $"{DisplayName} (21-22 NC)");
        }
        else if (ContactConfig == EmergencyStopContactConfig.Dual1NC1NO && SecondTop is not null && SecondBot is not null)
        {
            yield return new InternalConductivePath(SecondTop, SecondBot, $"{DisplayName} (23-24 NO)");
        }
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["ContactConfig"] = ContactConfig.ToString(),
        ["IsEngaged"] = IsEngaged.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ContactConfig", out var cStr) && Enum.TryParse<EmergencyStopContactConfig>(cStr, true, out var cfg))
            SetContactConfig(cfg);
        if (props.TryGetValue("IsEngaged", out var eStr) && bool.TryParse(eStr, out bool eng))
            IsEngaged = eng;
    }
}

/// <summary>
/// Cầu chì bảo vệ (RT18 / ống thủy tinh):
/// - Dòng định mức: 1A, 2A, 4A, 6A, 10A, 16A, 25A, 32A.
/// - Tự động đứt (Blown) khi xảy ra ngắn mạch hoặc dòng tải chạy qua vượt quá định mức!
/// </summary>
public sealed class FuseComponent : ElectricalComponent
{
    public static readonly int[] SupportedRatings = [1, 2, 4, 6, 10, 16, 25, 32];

    public const string TypeId = "FUSE_RT18";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public int RatedAmps { get; set; } = 10;
    public bool IsBlown { get; private set; }
    public double MeasuredCurrentAmps { get; set; }
    public double OverloadAccumulatorSeconds { get; private set; }
    public string? BlownReason { get; private set; }

    public Terminal In1 { get; }
    public Terminal Out2 { get; }

    public FuseComponent() : base("FU1", 60, 120)
    {
        In1 = AddTerminal("1", "1", 30, 0, TerminalSide.Top, "Đầu vào cầu chì 1");
        Out2 = AddTerminal("2", "2", 30, 120, TerminalSide.Bottom, "Đầu ra cầu chì 2");
    }

    public void Blow(string reason)
    {
        IsBlown = true;
        BlownReason = reason;
        OverloadAccumulatorSeconds = 0;
    }

    public void ResetFuse()
    {
        IsBlown = false;
        BlownReason = null;
        OverloadAccumulatorSeconds = 0;
        MeasuredCurrentAmps = 0;
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (!IsBlown)
            yield return new InternalConductivePath(In1, Out2, $"{DisplayName} ({RatedAmps}A Fuse)");
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBlown) return;

        if (MeasuredCurrentAmps > RatedAmps * 1.15)
        {
            // Quá tải nặng (> 2x In) đứt gần như tức thì, quá tải vừa đứt sau ~0.35s
            double factor = MeasuredCurrentAmps >= RatedAmps * 2.0 ? 4.0 : 1.0;
            OverloadAccumulatorSeconds += Math.Max(0.05, deltaSeconds) * factor;
            if (OverloadAccumulatorSeconds >= 0.35)
            {
                Blow(LocalizationManager.Format("Str.Fault.FuseBlownOverload", DisplayName, MeasuredCurrentAmps, RatedAmps));
            }
        }
        else
        {
            OverloadAccumulatorSeconds = Math.Max(0.0, OverloadAccumulatorSeconds - deltaSeconds * 0.5);
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        MeasuredCurrentAmps = 0;
        OverloadAccumulatorSeconds = 0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["RatedAmps"] = RatedAmps.ToString(CultureInfo.InvariantCulture),
        ["IsBlown"] = IsBlown.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("RatedAmps", out var aStr) && int.TryParse(aStr, out int amps))
            RatedAmps = amps;
        if (props.TryGetValue("IsBlown", out var bStr) && bool.TryParse(bStr, out bool blown))
            IsBlown = blown;
    }
}

/// <summary>
/// Cầu dao đóng cắt / Cầu dao đảo chiều (Knife Switch / Isolator):
/// - Cầu dao 1 pha (2 cực: 1-2, 3-4)
/// - Cầu dao 3 pha (3 cực: 1-2, 3-4, 5-6)
/// - Cầu dao đảo chiều 2 ngả 1-0-2 (3 cực chung A-B-C chuyển giữa nguồn trên 1A-1B-1C và nguồn dưới 2A-2B-2C)
/// </summary>
public sealed class KnifeSwitchComponent : ElectricalComponent
{
    public const string TypeId = "KNIFE_SWITCH";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public KnifeSwitchKind SwitchKind { get; private set; } = KnifeSwitchKind.ThreePhase3Pole;

    /// <summary>
    /// 1P / 3P: 0 = OFF (Mở), 1 = ON (Đóng).
    /// Changeover 1-0-2: 1 = Ngả 1 (trên), 0 = OFF (giữa), 2 = Ngả 2 (dưới).
    /// </summary>
    public int Position { get; set; }

    public List<Terminal> TopInputs { get; } = new();
    public List<Terminal> BottomTerminals { get; } = new();
    public List<Terminal> CommonOutputs { get; } = new();

    public KnifeSwitchComponent(KnifeSwitchKind kind = KnifeSwitchKind.ThreePhase3Pole)
        : base("CD1", GetWidth(kind), 140)
    {
        SwitchKind = kind;
        RebuildTerminals();
    }

    private static double GetWidth(KnifeSwitchKind kind) => kind switch
    {
        KnifeSwitchKind.SinglePhase2Pole => 100,
        KnifeSwitchKind.Changeover3P_1_0_2 => 160,
        _ => 140
    };

    public void SetSwitchKind(KnifeSwitchKind kind)
    {
        if (SwitchKind == kind) return;
        SwitchKind = kind;
        Width = GetWidth(kind);
        Position = 0;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        TopInputs.Clear();
        BottomTerminals.Clear();
        CommonOutputs.Clear();

        switch (SwitchKind)
        {
            case KnifeSwitchKind.SinglePhase2Pole:
                TopInputs.Add(AddOrReuseTerminal(existing, "1", "1", 20, 0, TerminalSide.Top));
                TopInputs.Add(AddOrReuseTerminal(existing, "3", "3", 80, 0, TerminalSide.Top));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "2", "2", 20, 140, TerminalSide.Bottom));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "4", "4", 80, 140, TerminalSide.Bottom));
                break;

            case KnifeSwitchKind.ThreePhase3Pole:
                TopInputs.Add(AddOrReuseTerminal(existing, "1", "1", 20, 0, TerminalSide.Top));
                TopInputs.Add(AddOrReuseTerminal(existing, "3", "3", 70, 0, TerminalSide.Top));
                TopInputs.Add(AddOrReuseTerminal(existing, "5", "5", 120, 0, TerminalSide.Top));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "2", "2", 20, 140, TerminalSide.Bottom));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "4", "4", 70, 140, TerminalSide.Bottom));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "6", "6", 120, 140, TerminalSide.Bottom));
                break;

            case KnifeSwitchKind.Changeover3P_1_0_2:
                TopInputs.Add(AddOrReuseTerminal(existing, "1A", "1A", 20, 0, TerminalSide.Top));
                TopInputs.Add(AddOrReuseTerminal(existing, "1B", "1B", 60, 0, TerminalSide.Top));
                TopInputs.Add(AddOrReuseTerminal(existing, "1C", "1C", 100, 0, TerminalSide.Top));

                BottomTerminals.Add(AddOrReuseTerminal(existing, "2A", "2A", 20, 140, TerminalSide.Bottom));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "2B", "2B", 60, 140, TerminalSide.Bottom));
                BottomTerminals.Add(AddOrReuseTerminal(existing, "2C", "2C", 100, 140, TerminalSide.Bottom));

                CommonOutputs.Add(AddOrReuseTerminal(existing, "A", "A", 160, 30, TerminalSide.Right));
                CommonOutputs.Add(AddOrReuseTerminal(existing, "B", "B", 160, 70, TerminalSide.Right));
                CommonOutputs.Add(AddOrReuseTerminal(existing, "C", "C", 160, 110, TerminalSide.Right));
                break;
        }
    }

    public void ToggleOrStep()
    {
        if (SwitchKind == KnifeSwitchKind.Changeover3P_1_0_2)
        {
            Position = Position switch { 0 => 1, 1 => 2, _ => 0 };
        }
        else
        {
            Position = Position == 1 ? 0 : 1;
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (SwitchKind == KnifeSwitchKind.Changeover3P_1_0_2)
        {
            if (Position == 1)
            {
                for (int i = 0; i < Math.Min(TopInputs.Count, CommonOutputs.Count); i++)
                    yield return new InternalConductivePath(TopInputs[i], CommonOutputs[i], $"{DisplayName} ({TopInputs[i].Name}-{CommonOutputs[i].Name})");
            }
            else if (Position == 2)
            {
                for (int i = 0; i < Math.Min(BottomTerminals.Count, CommonOutputs.Count); i++)
                    yield return new InternalConductivePath(BottomTerminals[i], CommonOutputs[i], $"{DisplayName} ({BottomTerminals[i].Name}-{CommonOutputs[i].Name})");
            }
        }
        else if (Position == 1)
        {
            for (int i = 0; i < Math.Min(TopInputs.Count, BottomTerminals.Count); i++)
                yield return new InternalConductivePath(TopInputs[i], BottomTerminals[i], $"{DisplayName} ({TopInputs[i].Name}-{BottomTerminals[i].Name})");
        }
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["SwitchKind"] = SwitchKind.ToString(),
        ["Position"] = Position.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("SwitchKind", out var kStr) && Enum.TryParse<KnifeSwitchKind>(kStr, true, out var k))
            SetSwitchKind(k);
        if (props.TryGetValue("Position", out var pStr) && int.TryParse(pStr, out int pos))
            Position = pos;
    }
}

/// <summary>
/// Công tắc chuyển mạch đo lường 4–7 vị trí (Kiểu KDE Cam Switch):
/// 1. Chuyển mạch Volt 7 vị trí: 0 - RS - ST - TR - RN - SN - TN (Chân vào R,S,T,N -> V1,V2).
/// 2. Chuyển mạch Volt 4 vị trí: 0 - RS - ST - TR (Chân vào R,S,T -> V1,V2).
/// 3. Chuyển mạch Ampe 4 vị trí: 0 - L1 - L2 - L3 (Chân vào CT L1,L2,L3,COM -> A1,A2; tự khép kín CT không đo).
/// </summary>
public sealed class CamSwitchComponent : ElectricalComponent
{
    public const string TypeId = "CAM_SWITCH_KDE";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Meter;

    public CamSwitchMode Mode { get; private set; } = CamSwitchMode.Volt7Pos;
    public int PositionIndex { get; set; }

    public Terminal InR { get; private set; } = null!;
    public Terminal InS { get; private set; } = null!;
    public Terminal InT { get; private set; } = null!;
    public Terminal? InNOrCom { get; private set; }
    public Terminal Out1 { get; private set; } = null!;
    public Terminal Out2 { get; private set; } = null!;

    public CamSwitchComponent() : base("SA1", 160, 160)
    {
        RebuildTerminals();
    }

    public string[] GetPositionLabels() => Mode switch
    {
        CamSwitchMode.Volt4Pos => ["0", "RS", "ST", "TR"],
        CamSwitchMode.Amp4Pos => ["0", "L1", "L2", "L3"],
        _ => ["0", "RS", "ST", "TR", "RN", "SN", "TN"]
    };

    public string CurrentPositionLabel
    {
        get
        {
            var labels = GetPositionLabels();
            return labels[Math.Clamp(PositionIndex, 0, labels.Length - 1)];
        }
    }

    public void SetMode(CamSwitchMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        PositionIndex = 0;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    public void StepPosition(int delta)
    {
        int count = GetPositionLabels().Length;
        PositionIndex = ((PositionIndex + delta) % count + count) % count;
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        InNOrCom = null;

        switch (Mode)
        {
            case CamSwitchMode.Volt4Pos:
                InR = AddOrReuseTerminal(existing, "R", "R(L1)", 20, 0, TerminalSide.Top);
                InS = AddOrReuseTerminal(existing, "S", "S(L2)", 80, 0, TerminalSide.Top);
                InT = AddOrReuseTerminal(existing, "T", "T(L3)", 140, 0, TerminalSide.Top);
                Out1 = AddOrReuseTerminal(existing, "V1", "V1", 40, 160, TerminalSide.Bottom);
                Out2 = AddOrReuseTerminal(existing, "V2", "V2", 120, 160, TerminalSide.Bottom);
                break;

            case CamSwitchMode.Amp4Pos:
                InR = AddOrReuseTerminal(existing, "R", "L1", 20, 0, TerminalSide.Top);
                InS = AddOrReuseTerminal(existing, "S", "L2", 60, 0, TerminalSide.Top);
                InT = AddOrReuseTerminal(existing, "T", "L3", 100, 0, TerminalSide.Top);
                InNOrCom = AddOrReuseTerminal(existing, "N", "COM", 140, 0, TerminalSide.Top);
                Out1 = AddOrReuseTerminal(existing, "V1", "A1", 40, 160, TerminalSide.Bottom);
                Out2 = AddOrReuseTerminal(existing, "V2", "A2", 120, 160, TerminalSide.Bottom);
                break;

            default: // Volt7Pos
                InR = AddOrReuseTerminal(existing, "R", "R(L1)", 20, 0, TerminalSide.Top);
                InS = AddOrReuseTerminal(existing, "S", "S(L2)", 60, 0, TerminalSide.Top);
                InT = AddOrReuseTerminal(existing, "T", "T(L3)", 100, 0, TerminalSide.Top);
                InNOrCom = AddOrReuseTerminal(existing, "N", "N", 140, 0, TerminalSide.Top);
                Out1 = AddOrReuseTerminal(existing, "V1", "V1", 40, 160, TerminalSide.Bottom);
                Out2 = AddOrReuseTerminal(existing, "V2", "V2", 120, 160, TerminalSide.Bottom);
                break;
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (Mode == CamSwitchMode.Amp4Pos)
        {
            if (InNOrCom is null) yield break;
            switch (PositionIndex)
            {
                case 1: // Đo L1 -> A1, A2 -> COM; ngắn mạch bảo vệ CT L2, L3 về COM
                    yield return new InternalConductivePath(InR, Out1, $"{DisplayName} (L1-A1)");
                    yield return new InternalConductivePath(Out2, InNOrCom, $"{DisplayName} (A2-COM)");
                    yield return new InternalConductivePath(InS, InNOrCom, $"{DisplayName} (CT-S Short)");
                    yield return new InternalConductivePath(InT, InNOrCom, $"{DisplayName} (CT-T Short)");
                    break;
                case 2:
                    yield return new InternalConductivePath(InS, Out1, $"{DisplayName} (L2-A1)");
                    yield return new InternalConductivePath(Out2, InNOrCom, $"{DisplayName} (A2-COM)");
                    yield return new InternalConductivePath(InR, InNOrCom, $"{DisplayName} (CT-R Short)");
                    yield return new InternalConductivePath(InT, InNOrCom, $"{DisplayName} (CT-T Short)");
                    break;
                case 3:
                    yield return new InternalConductivePath(InT, Out1, $"{DisplayName} (L3-A1)");
                    yield return new InternalConductivePath(Out2, InNOrCom, $"{DisplayName} (A2-COM)");
                    yield return new InternalConductivePath(InR, InNOrCom, $"{DisplayName} (CT-R Short)");
                    yield return new InternalConductivePath(InS, InNOrCom, $"{DisplayName} (CT-S Short)");
                    break;
                default: // 0 = OFF: ngắn mạch an toàn cả 3 thứ cấp CT về COM, Ampe kế hở (0A)
                    yield return new InternalConductivePath(InR, InNOrCom, $"{DisplayName} (OFF CT-R)");
                    yield return new InternalConductivePath(InS, InNOrCom, $"{DisplayName} (OFF CT-S)");
                    yield return new InternalConductivePath(InT, InNOrCom, $"{DisplayName} (OFF CT-T)");
                    break;
            }
            yield break;
        }

        // Volt 4 vị trí hoặc Volt 7 vị trí
        switch (PositionIndex)
        {
            case 1: // RS
                yield return new InternalConductivePath(InR, Out1, $"{DisplayName} (R-V1)");
                yield return new InternalConductivePath(InS, Out2, $"{DisplayName} (S-V2)");
                break;
            case 2: // ST
                yield return new InternalConductivePath(InS, Out1, $"{DisplayName} (S-V1)");
                yield return new InternalConductivePath(InT, Out2, $"{DisplayName} (T-V2)");
                break;
            case 3: // TR
                yield return new InternalConductivePath(InT, Out1, $"{DisplayName} (T-V1)");
                yield return new InternalConductivePath(InR, Out2, $"{DisplayName} (R-V2)");
                break;
            case 4 when Mode == CamSwitchMode.Volt7Pos && InNOrCom is not null: // RN
                yield return new InternalConductivePath(InR, Out1, $"{DisplayName} (R-V1)");
                yield return new InternalConductivePath(InNOrCom, Out2, $"{DisplayName} (N-V2)");
                break;
            case 5 when Mode == CamSwitchMode.Volt7Pos && InNOrCom is not null: // SN
                yield return new InternalConductivePath(InS, Out1, $"{DisplayName} (S-V1)");
                yield return new InternalConductivePath(InNOrCom, Out2, $"{DisplayName} (N-V2)");
                break;
            case 6 when Mode == CamSwitchMode.Volt7Pos && InNOrCom is not null: // TN
                yield return new InternalConductivePath(InT, Out1, $"{DisplayName} (T-V1)");
                yield return new InternalConductivePath(InNOrCom, Out2, $"{DisplayName} (N-V2)");
                break;
        }
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["Mode"] = Mode.ToString(),
        ["PositionIndex"] = PositionIndex.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("Mode", out var mStr) && Enum.TryParse<CamSwitchMode>(mStr, true, out var mode))
            SetMode(mode);
        if (props.TryGetValue("PositionIndex", out var pStr) && int.TryParse(pStr, out int idx))
            PositionIndex = Math.Clamp(idx, 0, GetPositionLabels().Length - 1);
    }
}

/// <summary>
/// Nút rẽ nhánh dây điện (Wire T-Junction / Splice Node):
/// - Một điểm nối điện gọn nhẹ tạo tự động khi nối nhánh từ thân một sợi dây đã có.
/// - Hiển thị bằng chấm tròn đặc cùng màu dây để phân biệt rõ ràng với giao cắt không nối điện.
/// - Tự đồng bộ 100% với Simulation Engine (BFS), Undo/Redo và lưu/mở JSON.
/// </summary>
public sealed class WireJunctionComponent : ElectricalComponent
{
    public const string TypeId = "WIRE_JUNCTION";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public WireColorPreset DotColorPreset { get; set; } = WireColorPreset.Red_L1_DCPlus;
    public Terminal NodeTerminal { get; }

    public WireJunctionComponent() : base("J", 16, 16)
    {
        NodeTerminal = AddTerminal("J", "T-Node", 8, 8, TerminalSide.Top, "Nút rẽ nhánh dây dẫn (T-Junction)");
    }

    public System.Windows.Point CenterPoint
    {
        get => new(X + 8.0, Y + 8.0);
        set { X = value.X - 8.0; Y = value.Y - 8.0; }
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["DotColor"] = DotColorPreset.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("DotColor", out var cStr) && Enum.TryParse<WireColorPreset>(cStr, true, out var col))
            DotColorPreset = col;
    }
}
