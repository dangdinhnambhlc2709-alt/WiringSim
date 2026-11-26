using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Loads;

/// <summary>
/// Đèn báo pha công nghiệp (Xanh lá, Đỏ, Vàng, Trắng).
/// </summary>
public sealed class PilotLightComponent : ElectricalComponent
{
    public const string TypeIdGreen = "PILOT_GREEN";
    public const string TypeIdRed = "PILOT_RED";
    public const string TypeIdYellow = "PILOT_YELLOW";
    public const string TypeIdWhite = "PILOT_WHITE";

    public PilotLightColor LightColor { get; set; }
    public CoilVoltageRating VoltageRating { get; set; } = CoilVoltageRating.AC_220V;

    public bool IsLit { get; private set; }
    public double AppliedVoltage { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    public override string TypeKey => LightColor switch
    {
        PilotLightColor.Red => TypeIdRed,
        PilotLightColor.Yellow => TypeIdYellow,
        PilotLightColor.White => TypeIdWhite,
        _ => TypeIdGreen
    };

    public override ComponentCategory Category => ComponentCategory.Load;

    public Terminal X1 { get; }
    public Terminal X2 { get; }

    public PilotLightComponent(PilotLightColor color = PilotLightColor.Green)
        : base("H1", 80, 100)
    {
        LightColor = color;
        X1 = AddTerminal("X1", "X1", 20, 100, TerminalSide.Bottom);
        X2 = AddTerminal("X2", "X2", 60, 100, TerminalSide.Bottom);
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(VoltageRating);
        yield return new InternalLoadPath(X1, X2, DisplayName, IsLit && !IsBurnedOut, 0.02, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsLit = false;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(X1, X2, VoltageRating, out double v, out var kind);
        AppliedVoltage = v;

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
        AppliedVoltage = 0;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["LightColor"] = LightColor.ToString(),
        ["VoltageRating"] = VoltageRating.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("LightColor", out var cStr) && Enum.TryParse<PilotLightColor>(cStr, true, out var c))
            LightColor = c;
        if (props.TryGetValue("VoltageRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            VoltageRating = r;
    }
}

/// <summary>
/// Bóng đèn chiếu sáng dân dụng (12VDC / 24VDC / 220VAC).
/// </summary>
public sealed class ResidentialLampComponent : ElectricalComponent
{
    public const string TypeId = "RESIDENTIAL_LAMP";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Load;

    public CoilVoltageRating VoltageRating { get; set; } = CoilVoltageRating.AC_220V;
    public PilotLightColor LampColor { get; set; } = PilotLightColor.Yellow;

    public bool IsLit { get; private set; }
    public double Brightness { get; private set; }
    public double AppliedVoltage { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    public Terminal LineTerminal { get; }
    public Terminal NeutralTerminal { get; }

    public ResidentialLampComponent() : base("EL1", 80, 100)
    {
        LineTerminal = AddTerminal("L", "L(+)", 20, 100, TerminalSide.Bottom);
        NeutralTerminal = AddTerminal("N", "N(-)", 60, 100, TerminalSide.Bottom);
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, nomV, _) = ElectricalAnalysisHelper.GetRatingSpec(VoltageRating);
        double amps = nomV >= 100 ? 0.45 : 1.0;
        yield return new InternalLoadPath(LineTerminal, NeutralTerminal, DisplayName, IsLit && !IsBurnedOut, amps, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsLit = false;
            Brightness = 0;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(LineTerminal, NeutralTerminal, VoltageRating, out double v, out var kind);
        AppliedVoltage = v;

        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLbl) = ElectricalAnalysisHelper.GetRatingSpec(VoltageRating);
            IsBurnedOut = true;
            IsLit = false;
            Brightness = 0;
            BurnoutReason = LocalizationManager.Format("Str.Fault.BurnoutLampMsg", DisplayName, v, kind, ratingLbl);
            return;
        }

        IsLit = SupplyStatus == RatedSupplyStatus.Normal;
        Brightness = IsLit ? 1.0 : 0.0;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsLit = false;
        Brightness = 0;
        AppliedVoltage = 0;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["VoltageRating"] = VoltageRating.ToString(),
        ["LampColor"] = LampColor.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("VoltageRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            VoltageRating = r;
        if (props.TryGetValue("LampColor", out var cStr) && Enum.TryParse<PilotLightColor>(cStr, true, out var c))
            LampColor = c;
    }
}

/// <summary>
/// Động cơ xoay chiều 1 pha 220V (quay mượt 60 FPS).
/// </summary>
public sealed class Motor1PhaseComponent : ElectricalComponent
{
    public const string TypeId = "MOTOR_1P";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Load;

    public bool IsRunning { get; private set; }
    public double RotorAngle { get; private set; }
    public double CurrentRpmRatio { get; private set; }

    public Terminal LineTerminal { get; }
    public Terminal NeutralTerminal { get; }
    public Terminal PeTerminal { get; }

    public Motor1PhaseComponent() : base("M1", 120, 140)
    {
        LineTerminal = AddTerminal("L", "L", 20, 0, TerminalSide.Top);
        NeutralTerminal = AddTerminal("N", "N", 60, 0, TerminalSide.Top);
        PeTerminal = AddTerminal("PE", "PE", 100, 0, TerminalSide.Top);
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        yield return new InternalLoadPath(LineTerminal, NeutralTerminal, DisplayName, IsRunning, 4.8, CurrentKind.AC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        double v = ElectricalAnalysisHelper.GetOperatingVoltageAcross(LineTerminal, NeutralTerminal, out var kind);
        IsRunning = kind == CurrentKind.AC && v >= 175.0;
    }

    public override void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
        double target = isSimulating && IsRunning ? 1.0 : 0.0;
        CurrentRpmRatio += (target - CurrentRpmRatio) * Math.Min(1.0, frameDeltaSeconds * 5.0);
        if (CurrentRpmRatio < 0.005) CurrentRpmRatio = 0;
        if (CurrentRpmRatio > 0)
            RotorAngle = (RotorAngle + CurrentRpmRatio * 540.0 * frameDeltaSeconds) % 360.0;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsRunning = false;
        CurrentRpmRatio = 0;
    }
}

/// <summary>
/// Lớp cơ sở dùng chung cho 2 loại Động cơ 3 pha (3 đầu dây &amp; 6 đầu dây).
/// Hỗ trợ tần số biến thiên từ Biến tần (VFD), đảo chiều pha, báo mất pha và tính dòng định mức phối hợp Rơ-le nhiệt / Ampe kế.
/// </summary>
public abstract class Motor3PhaseBaseComponent : ElectricalComponent
{
    public static readonly double[] SupportedPowersKw = [1.5, 3.0, 5.5, 7.5];

    public override ComponentCategory Category => ComponentCategory.Load;

    public double RatedPowerKw { get; set; } = 3.0;
    public double NominalCurrentAmps => RatedPowerKw switch
    {
        <= 1.6 => 3.5,
        <= 3.2 => 6.8,
        <= 5.8 => 11.6,
        _ => 15.5
    };

    public double OperatingCurrentAmps { get; protected set; }
    public double AppliedFrequencyHz { get; protected set; } = 50.0;
    public int RotationDirection { get; protected set; }
    public bool IsPhaseLost { get; protected set; }
    public string ConnectionModeLabel { get; protected set; } = "3P";
    public double RotorAngle { get; protected set; }
    public double CurrentAngularVelocity { get; protected set; }
    protected double TargetSpeedFactor;

    public Terminal U1 { get; }
    public Terminal V1 { get; }
    public Terminal W1 { get; }
    public Terminal Pe { get; }

    protected Motor3PhaseBaseComponent(string displayName, double width, double height)
        : base(displayName, width, height)
    {
        U1 = AddTerminal("U1", "U1", 20, 0, TerminalSide.Top);
        V1 = AddTerminal("V1", "V1", 60, 0, TerminalSide.Top);
        W1 = AddTerminal("W1", "W1", 100, 0, TerminalSide.Top);
        Pe = AddTerminal("PE", "PE", 140, 0, TerminalSide.Top);
    }

    protected static PhaseType GetLinePhaseOnly(Terminal t)
    {
        foreach (var p in t.ActivePotentials)
        {
            if (p.CurrentKind == CurrentKind.AC && p.Phase is PhaseType.L1 or PhaseType.L2 or PhaseType.L3)
                return p.Phase;
        }
        return PhaseType.None;
    }

    protected static double GetSupplyFrequencyHz(params Terminal[] terms)
    {
        foreach (var t in terms)
        {
            foreach (var p in t.ActivePotentials)
            {
                if (p.CurrentKind == CurrentKind.AC && p.Phase is PhaseType.L1 or PhaseType.L2 or PhaseType.L3)
                    return p.FrequencyHz > 0.05 ? p.FrequencyHz : 50.0;
            }
        }
        return 50.0;
    }

    protected static int CountLinePhases(PhaseType a, PhaseType b, PhaseType c) =>
        (a != PhaseType.None ? 1 : 0) + (b != PhaseType.None ? 1 : 0) + (c != PhaseType.None ? 1 : 0);

    protected static bool HasDistinctThreePhases(PhaseType a, PhaseType b, PhaseType c) =>
        a != PhaseType.None && b != PhaseType.None && c != PhaseType.None && a != b && b != c && a != c;

    protected static int DeterminePhaseSequenceDirection(PhaseType pU, PhaseType pV, PhaseType pW)
    {
        bool cw = (pU == PhaseType.L1 && pV == PhaseType.L2 && pW == PhaseType.L3) ||
                  (pU == PhaseType.L2 && pV == PhaseType.L3 && pW == PhaseType.L1) ||
                  (pU == PhaseType.L3 && pV == PhaseType.L1 && pW == PhaseType.L2);
        return cw ? 1 : -1;
    }

    public override void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
        double freqScale = Math.Clamp(AppliedFrequencyHz / 50.0, 0.05, 4.0);
        double targetVel = isSimulating && RotationDirection != 0 && !IsPhaseLost
            ? RotationDirection * 580.0 * TargetSpeedFactor * freqScale
            : 0.0;

        CurrentAngularVelocity += (targetVel - CurrentAngularVelocity) * Math.Min(1.0, frameDeltaSeconds * 5.0);
        if (Math.Abs(CurrentAngularVelocity) < 1.5) CurrentAngularVelocity = 0;

        if (Math.Abs(CurrentAngularVelocity) > 0)
        {
            RotorAngle = (RotorAngle + CurrentAngularVelocity * frameDeltaSeconds) % 360.0;
            if (RotorAngle < 0) RotorAngle += 360.0;
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        RotationDirection = 0;
        IsPhaseLost = false;
        CurrentAngularVelocity = 0;
        TargetSpeedFactor = 0;
        OperatingCurrentAmps = 0;
        AppliedFrequencyHz = 50.0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["RatedPowerKw"] = RatedPowerKw.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("RatedPowerKw", out var pStr) &&
            double.TryParse(pStr, CultureInfo.InvariantCulture, out double kw))
            RatedPowerKw = kw;
    }
}

/// <summary>
/// 1. Động cơ 3 pha — 3 chân đấu nối (U1, V1, W1 + PE):
/// Cho phép chọn kiểu đấu nối hộp cực nội bộ: Đấu Sao (Y — 380V) hoặc Đấu Tam giác (Δ — 220V).
/// </summary>
public sealed class Motor3Phase3PinComponent : Motor3PhaseBaseComponent
{
    public const string TypeId = "MOTOR_3P_3PIN";
    public override string TypeKey => TypeId;

    public Motor3PinConnection InternalConnection { get; set; } = Motor3PinConnection.Star_380V;

    public Motor3Phase3PinComponent() : base("M1", 160, 140)
    {
        ConnectionModeLabel = "Y 380V";
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        bool running = RotationDirection != 0 && !IsPhaseLost;
        yield return new InternalLoadPath(U1, V1, $"{DisplayName} U-V", running, OperatingCurrentAmps, CurrentKind.AC);
        yield return new InternalLoadPath(V1, W1, $"{DisplayName} V-W", running, OperatingCurrentAmps, CurrentKind.AC);
        yield return new InternalLoadPath(W1, U1, $"{DisplayName} W-U", running, OperatingCurrentAmps, CurrentKind.AC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        PhaseType pU = GetLinePhaseOnly(U1);
        PhaseType pV = GetLinePhaseOnly(V1);
        PhaseType pW = GetLinePhaseOnly(W1);
        AppliedFrequencyHz = GetSupplyFrequencyHz(U1, V1, W1);

        string connTag = InternalConnection == Motor3PinConnection.Delta_220V ? "Δ 220V" : "Y 380V";
        int count = CountLinePhases(pU, pV, pW);

        if (count == 0)
        {
            RotationDirection = 0;
            IsPhaseLost = false;
            TargetSpeedFactor = 0.0;
            OperatingCurrentAmps = 0.0;
            ConnectionModeLabel = connTag;
            return;
        }

        if (count < 3 || !HasDistinctThreePhases(pU, pV, pW))
        {
            RotationDirection = 0;
            IsPhaseLost = true;
            TargetSpeedFactor = 0.0;
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.PhaseLoss");
            OperatingCurrentAmps = count >= 2 ? Math.Round(NominalCurrentAmps * 2.2, 1) : 0.0;
            return;
        }

        IsPhaseLost = false;
        RotationDirection = DeterminePhaseSequenceDirection(pU, pV, pW);
        TargetSpeedFactor = 1.0;
        OperatingCurrentAmps = InternalConnection == Motor3PinConnection.Delta_220V
            ? Math.Round(NominalCurrentAmps * 1.73, 1)
            : NominalCurrentAmps;

        ConnectionModeLabel = Math.Abs(AppliedFrequencyHz - 50.0) > 0.5
            ? $"{connTag} • {AppliedFrequencyHz:0.0}Hz"
            : connTag;
    }

    public override Dictionary<string, string> ExportProperties()
    {
        var d = base.ExportProperties();
        d["InternalConnection"] = InternalConnection.ToString();
        return d;
    }

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        base.ImportProperties(props);
        if (props.TryGetValue("InternalConnection", out var cStr) && Enum.TryParse<Motor3PinConnection>(cStr, true, out var conn))
            InternalConnection = conn;
    }
}

/// <summary>
/// 2. Động cơ 3 pha — 6 chân đấu nối (U1, V1, W1 &amp; U2, V2, W2 + PE):
/// Hỗ trợ chọn cấu hình cuộn dây bên trong:
/// - StarDelta: Động cơ Sao – Tam giác 6 đầu dây.
/// - Dahlander2Speed: Động cơ 2 cấp tốc độ (Thấp Δ khi cấp U1,V1,W1 &amp; hở U2,V2,W2; Cao YY khi cấp U2,V2,W2 &amp; chụm U1-V1-W1).
/// </summary>
public sealed class Motor3Phase6PinComponent : Motor3PhaseBaseComponent
{
    public const string TypeId = "MOTOR_3P_6PIN";
    public override string TypeKey => TypeId;

    public Motor6PinWindingType WindingType { get; set; } = Motor6PinWindingType.StarDelta;

    public Terminal U2 { get; }
    public Terminal V2 { get; }
    public Terminal W2 { get; }

    private bool _runningTopBottom;

    public Motor3Phase6PinComponent() : base("M1", 160, 160)
    {
        U2 = AddTerminal("U2", "U2", 20, 160, TerminalSide.Bottom);
        V2 = AddTerminal("V2", "V2", 60, 160, TerminalSide.Bottom);
        W2 = AddTerminal("W2", "W2", 100, 160, TerminalSide.Bottom);
        ConnectionModeLabel = "Y-Δ";
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        bool running = RotationDirection != 0 && !IsPhaseLost;
        if (_runningTopBottom)
        {
            yield return new InternalLoadPath(U1, U2, $"{DisplayName} U1-U2", running, OperatingCurrentAmps, CurrentKind.AC);
            yield return new InternalLoadPath(V1, V2, $"{DisplayName} V1-V2", running, OperatingCurrentAmps, CurrentKind.AC);
            yield return new InternalLoadPath(W1, W2, $"{DisplayName} W1-W2", running, OperatingCurrentAmps, CurrentKind.AC);
        }
        else
        {
            // Ở chế độ Dahlander tốc độ thấp (cấp U1,V1,W1 và hở U2,V2,W2), dòng chạy trong vòng tam giác U1-V1-W1
            yield return new InternalLoadPath(U1, V1, $"{DisplayName} U1-V1", running, OperatingCurrentAmps, CurrentKind.AC);
            yield return new InternalLoadPath(V1, W1, $"{DisplayName} V1-W1", running, OperatingCurrentAmps, CurrentKind.AC);
            yield return new InternalLoadPath(W1, U1, $"{DisplayName} W1-U1", running, OperatingCurrentAmps, CurrentKind.AC);
        }
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        OperatingCurrentAmps = 0.0;
        _runningTopBottom = true;

        PhaseType pU1 = GetLinePhaseOnly(U1);
        PhaseType pV1 = GetLinePhaseOnly(V1);
        PhaseType pW1 = GetLinePhaseOnly(W1);
        PhaseType pU2 = GetLinePhaseOnly(U2);
        PhaseType pV2 = GetLinePhaseOnly(V2);
        PhaseType pW2 = GetLinePhaseOnly(W2);

        AppliedFrequencyHz = GetSupplyFrequencyHz(U1, V1, W1, U2, V2, W2);

        if (WindingType == Motor6PinWindingType.Dahlander2Speed)
        {
            EvaluateDahlander2Speed(pU1, pV1, pW1, pU2, pV2, pW2);
            return;
        }

        EvaluateStarDelta6Lead(pU1, pV1, pW1, pU2, pV2, pW2);
    }

    private void EvaluateStarDelta6Lead(
        PhaseType pU1, PhaseType pV1, PhaseType pW1,
        PhaseType pU2, PhaseType pV2, PhaseType pW2)
    {
        int topCount = CountLinePhases(pU1, pV1, pW1);
        int botCount = CountLinePhases(pU2, pV2, pW2);

        // 1. Đấu SAO (U2, V2, W2 chụm chung 1 Net trung tính nổi)
        bool starBridged = U2.NetId >= 0 && U2.NetId == V2.NetId && V2.NetId == W2.NetId && botCount == 0;
        if (starBridged)
        {
            if (topCount == 3 && HasDistinctThreePhases(pU1, pV1, pW1))
            {
                IsPhaseLost = false;
                RotationDirection = DeterminePhaseSequenceDirection(pU1, pV1, pW1);
                TargetSpeedFactor = 0.58;
                OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 0.58, 1);
                ConnectionModeLabel = LocalizationManager.Get("Str.Vis.Star");
            }
            else if (topCount > 0)
            {
                RotationDirection = 0;
                IsPhaseLost = true;
                TargetSpeedFactor = 0;
                ConnectionModeLabel = LocalizationManager.Get("Str.Vis.PhaseLoss");
                OperatingCurrentAmps = topCount >= 2 ? Math.Round(NominalCurrentAmps * 2.2, 1) : 0;
            }
            else
            {
                RotationDirection = 0;
                IsPhaseLost = false;
                TargetSpeedFactor = 0;
                ConnectionModeLabel = "Y-Δ";
            }
            return;
        }

        // 2. Đấu TAM GIÁC (3 cặp cuộn chịu áp dây 380V lệch 120°: U1-W2, V1-U2, W1-V2)
        if (topCount == 3 && botCount == 3 &&
            HasDistinctThreePhases(pU1, pV1, pW1) &&
            HasDistinctThreePhases(pU2, pV2, pW2) &&
            pU1 != pU2 && pV1 != pV2 && pW1 != pW2)
        {
            ElectricalAnalysisHelper.TryGetAcPhasor(pU1, out var vU1);
            ElectricalAnalysisHelper.TryGetAcPhasor(pU2, out var vU2);
            ElectricalAnalysisHelper.TryGetAcPhasor(pV1, out var vV1);
            ElectricalAnalysisHelper.TryGetAcPhasor(pV2, out var vV2);

            double cross = (vU1.Re - vU2.Re) * (vV1.Im - vV2.Im) - (vU1.Im - vU2.Im) * (vV1.Re - vV2.Re);
            IsPhaseLost = false;
            RotationDirection = cross < 0 ? 1 : -1;
            TargetSpeedFactor = 1.0;
            OperatingCurrentAmps = NominalCurrentAmps;
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.Delta");
            return;
        }

        // 3. Hở cầu Y/Δ (chưa đóng KY hay KD) hoặc Mất pha
        bool botOpen = botCount == 0 && !(U2.NetId >= 0 && (U2.NetId == V2.NetId || V2.NetId == W2.NetId || U2.NetId == W2.NetId));
        if (botOpen)
        {
            RotationDirection = 0;
            TargetSpeedFactor = 0;
            IsPhaseLost = topCount is 1 or 2 || (topCount == 3 && !HasDistinctThreePhases(pU1, pV1, pW1));
            ConnectionModeLabel = IsPhaseLost ? LocalizationManager.Get("Str.Vis.PhaseLoss") : LocalizationManager.Get("Str.Vis.OpenYD");
            return;
        }

        if (topCount > 0 || botCount > 0)
        {
            RotationDirection = 0;
            TargetSpeedFactor = 0;
            IsPhaseLost = true;
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.PhaseLoss");
            if (topCount >= 2 || botCount >= 2)
                OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 2.2, 1);
        }
        else
        {
            RotationDirection = 0;
            TargetSpeedFactor = 0;
            IsPhaseLost = false;
            ConnectionModeLabel = "Y-Δ";
        }
    }

    private void EvaluateDahlander2Speed(
        PhaseType pU1, PhaseType pV1, PhaseType pW1,
        PhaseType pU2, PhaseType pV2, PhaseType pW2)
    {
        int topCount = CountLinePhases(pU1, pV1, pW1);
        int botCount = CountLinePhases(pU2, pV2, pW2);

        // CẤP TỐC ĐỘ CAO (YY): Chụm sao U1-V1-W1 (không mang pha) và cấp 3 pha vào U2, V2, W2!
        bool topStarBridged = U1.NetId >= 0 && U1.NetId == V1.NetId && V1.NetId == W1.NetId && topCount == 0;
        if (topStarBridged && botCount == 3 && HasDistinctThreePhases(pU2, pV2, pW2))
        {
            _runningTopBottom = true;
            IsPhaseLost = false;
            RotationDirection = DeterminePhaseSequenceDirection(pU2, pV2, pW2);
            TargetSpeedFactor = 1.45; // Quay nhanh rõ rệt (tốc độ cao 2 cực)
            OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 1.15, 1);
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.HighSpeed");
            return;
        }

        // CẤP TỐC ĐỘ THẤP (Δ): Cấp 3 pha vào U1, V1, W1 và để hở U2, V2, W2!
        bool botUnpowered = botCount == 0;
        if (topCount == 3 && HasDistinctThreePhases(pU1, pV1, pW1) && botUnpowered)
        {
            _runningTopBottom = false;
            IsPhaseLost = false;
            RotationDirection = DeterminePhaseSequenceDirection(pU1, pV1, pW1);
            TargetSpeedFactor = 0.52; // Quay chậm rõ rệt (tốc độ thấp 4 cực)
            OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 0.72, 1);
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.LowSpeed");
            return;
        }

        if (topCount > 0 || botCount > 0)
        {
            RotationDirection = 0;
            TargetSpeedFactor = 0;
            IsPhaseLost = true;
            ConnectionModeLabel = LocalizationManager.Get("Str.Vis.PhaseLoss");
            OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 2.0, 1);
        }
        else
        {
            RotationDirection = 0;
            TargetSpeedFactor = 0;
            IsPhaseLost = false;
            ConnectionModeLabel = "Δ / YY";
        }
    }

    public override Dictionary<string, string> ExportProperties()
    {
        var d = base.ExportProperties();
        d["WindingType"] = WindingType.ToString();
        return d;
    }

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        base.ImportProperties(props);
        if (props.TryGetValue("WindingType", out var wStr) && Enum.TryParse<Motor6PinWindingType>(wStr, true, out var wt))
            WindingType = wt;
    }
}
