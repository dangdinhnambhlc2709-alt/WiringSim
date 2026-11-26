using System.Globalization;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Khối Biến tần công nghiệp (VFD / Inverter):
/// - Động lực vào: R, S, T + PE (3 pha 380V hoặc 1 pha 220V vào R-S).
/// - Động lực ra động cơ: U, V, W (xuất điện áp 3 pha biến đổi tần số 0..MaxFreqHz &amp; tự đảo thứ tự pha khi chạy REV).
/// - Điều khiển số: FWD, REV, DI1, DI2, COM.
/// - Chiết áp ngoài: 10V, AI1, GND.
/// - Rơ-le trạng thái / báo lỗi: FA (NO), FB (NC), FC (COM).
/// - Thông số chuẩn P01–P06 lập trình trực tiếp qua hộp thoại VfdKeypadDialog.
/// </summary>
public sealed class VfdComponent : ElectricalComponent
{
    public const string TypeId = "VFD_INVERTER";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    // --- Thông số cài đặt P01 – P06 ---
    /// <summary>P01: 0 = Phím RUN/STOP trên Keypad | 1 = Chân kích ngoài (FWD/REV nối COM).</summary>
    public int P01_RunSource { get; set; } = 0;

    /// <summary>P02: 0 = Phím ▲/▼ trên Keypad | 1 = Núm chiết áp ảo trên Keypad | 2 = Chiết áp ngoài 10V-AI1-GND.</summary>
    public int P02_FreqSource { get; set; } = 0;

    /// <summary>P03: Tần số tối đa (10.0 – 400.0 Hz, mặc định 50.0 Hz).</summary>
    public double P03_MaxFreqHz { get; set; } = 50.0;

    /// <summary>P04: Thời gian tăng tốc ACC (0.5 – 60.0 s, mặc định 3.0 s).</summary>
    public double P04_AccelSeconds { get; set; } = 3.0;

    /// <summary>P05: Thời gian giảm tốc DEC (0.5 – 60.0 s, mặc định 3.0 s).</summary>
    public double P05_DecelSeconds { get; set; } = 3.0;

    /// <summary>P06: Tần số đa cấp DI1 (mặc định 25.0 Hz) và DI2 (mặc định 40.0 Hz).</summary>
    public double P06_Di1FreqHz { get; set; } = 25.0;
    public double P06_Di2FreqHz { get; set; } = 40.0;

    // --- Giá trị điều khiển thời gian thực ---
    public double KeypadTargetFreqHz { get; set; } = 50.0;
    public double DialFreqHz { get; set; } = 30.0;
    public double ExternalAnalogPercent { get; set; } = 70.0; // 0..100% khi nối chiết áp ngoài AI1

    public bool KeypadRunCommand { get; set; }
    public bool KeypadReverse { get; set; }

    // --- Trạng thái mô phỏng ---
    public bool IsPowered { get; private set; }
    public bool HasFault { get; private set; }
    public bool IsRunning => IsPowered && !HasFault && Math.Abs(CurrentSignedFreqHz) > 0.05;

    /// <summary>Tần số đầu ra có dấu (+ là FWD quay thuận, - là REV quay ngược).</summary>
    public double CurrentSignedFreqHz { get; private set; }
    public double CurrentOutputFreqHz => Math.Abs(CurrentSignedFreqHz);
    public double OutputCurrentAmps { get; set; }

    public bool IsExternalFwdActive { get; private set; }
    public bool IsExternalRevActive { get; private set; }
    public bool IsExternalDi1Active { get; private set; }
    public bool IsExternalDi2Active { get; private set; }

    // --- 17 chân đấu nối tiêu chuẩn ---
    public Terminal R_In { get; }
    public Terminal S_In { get; }
    public Terminal T_In { get; }
    public Terminal Pe_In { get; }

    public Terminal Fa_No { get; }
    public Terminal Fb_Nc { get; }
    public Terminal Fc_Com { get; }

    public Terminal U_Out { get; }
    public Terminal V_Out { get; }
    public Terminal W_Out { get; }

    public Terminal Fwd { get; }
    public Terminal Rev { get; }
    public Terminal Di1 { get; }
    public Terminal Di2 { get; }
    public Terminal Com { get; }

    public Terminal Pot10V { get; }
    public Terminal PotAi1 { get; }
    public Terminal PotGnd { get; }

    public VfdComponent() : base("VFD1", 260, 180)
    {
        // Hàng trên: Nguồn vào động lực (R,S,T,PE) + Rơ-le trạng thái/lỗi (FA,FB,FC)
        R_In = AddTerminal("R", "R", 20, 0, TerminalSide.Top);
        S_In = AddTerminal("S", "S", 60, 0, TerminalSide.Top);
        T_In = AddTerminal("T", "T", 100, 0, TerminalSide.Top);
        Pe_In = AddTerminal("PE", "PE", 140, 0, TerminalSide.Top);

        Fa_No = AddTerminal("FA", "FA", 180, 0, TerminalSide.Top);
        Fb_Nc = AddTerminal("FB", "FB", 200, 0, TerminalSide.Top);
        Fc_Com = AddTerminal("FC", "FC", 220, 0, TerminalSide.Top);

        // Hàng dưới: Ngõ ra động cơ (U,V,W) thẳng hàng động cơ 3 pha + Đầu vào số (FWD,REV,DI1,DI2,COM)
        U_Out = AddTerminal("U", "U", 20, 180, TerminalSide.Bottom);
        V_Out = AddTerminal("V", "V", 60, 180, TerminalSide.Bottom);
        W_Out = AddTerminal("W", "W", 100, 180, TerminalSide.Bottom);

        Fwd = AddTerminal("FWD", "FWD", 140, 180, TerminalSide.Bottom);
        Rev = AddTerminal("REV", "REV", 160, 180, TerminalSide.Bottom);
        Di1 = AddTerminal("DI1", "DI1", 180, 180, TerminalSide.Bottom);
        Di2 = AddTerminal("DI2", "DI2", 200, 180, TerminalSide.Bottom);
        Com = AddTerminal("COM", "COM", 220, 180, TerminalSide.Bottom);

        // Cạnh phải: Cụm chiết áp ngoài (10V, AI1, GND)
        Pot10V = AddTerminal("10V", "10V", 260, 60, TerminalSide.Right);
        PotAi1 = AddTerminal("AI1", "AI1", 260, 100, TerminalSide.Right);
        PotGnd = AddTerminal("GND", "GND", 260, 140, TerminalSide.Right);
    }

    public double ComputeTargetFrequencyHz()
    {
        double raw;
        if (IsExternalDi1Active)
        {
            raw = P06_Di1FreqHz;
        }
        else if (IsExternalDi2Active)
        {
            raw = P06_Di2FreqHz;
        }
        else
        {
            raw = P02_FreqSource switch
            {
                1 => DialFreqHz,
                2 => EvaluateExternalAnalogHz(),
                _ => KeypadTargetFreqHz
            };
        }

        return Math.Clamp(raw, 0.0, P03_MaxFreqHz);
    }

    private double EvaluateExternalAnalogHz()
    {
        if (PotAi1.NetId >= 0 && PotAi1.NetId == Pot10V.NetId)
            return P03_MaxFreqHz;
        if (PotAi1.NetId >= 0 && PotAi1.NetId == PotGnd.NetId)
            return 0.0;
        return P03_MaxFreqHz * Math.Clamp(ExternalAnalogPercent / 100.0, 0.0, 1.0);
    }

    public void StopAndResetFault()
    {
        KeypadRunCommand = false;
        HasFault = false;
    }

    public override IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs()
    {
        if (!IsPowered || HasFault || IsBurnedOut)
            yield break;

        double freq = CurrentOutputFreqHz;
        if (freq > 0.05)
        {
            // Điều chế V/f tuyến tính mượt tới 380V dây (220V pha)
            double phaseVolts = Math.Clamp(220.0 * Math.Min(1.0, freq / 50.0), 40.0, 220.0);
            bool reverse = CurrentSignedFreqHz < 0;

            yield return (U_Out, new PotentialToken(Id, U_Out.Id, CurrentKind.AC, PhaseType.L1, phaseVolts, freq));
            yield return (V_Out, new PotentialToken(Id, V_Out.Id, CurrentKind.AC, reverse ? PhaseType.L3 : PhaseType.L2, phaseVolts, freq));
            yield return (W_Out, new PotentialToken(Id, W_Out.Id, CurrentKind.AC, reverse ? PhaseType.L2 : PhaseType.L3, phaseVolts, freq));
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Rơ-le FA-FB-FC: Báo trạng thái hoạt động/lỗi (FC-FA đóng khi RUN hoặc Fault; bình thường FC-FB đóng)
        bool relayActive = IsRunning || HasFault;
        if (relayActive)
            yield return new InternalConductivePath(Fc_Com, Fa_No, $"{DisplayName} (FC-FA)");
        else
            yield return new InternalConductivePath(Fc_Com, Fb_Nc, $"{DisplayName} (FC-FB)");
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        double inputAmps = IsRunning ? Math.Max(0.3, OutputCurrentAmps) : (IsPowered ? 0.12 : 0.0);
        yield return new InternalLoadPath(R_In, S_In, $"{DisplayName} Input R-S", IsPowered, inputAmps, CurrentKind.AC);
        if (T_In.ActivePotentials.Any(p => p.CurrentKind == CurrentKind.AC))
            yield return new InternalLoadPath(S_In, T_In, $"{DisplayName} Input S-T", IsPowered, inputAmps, CurrentKind.AC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        // 1. Kiểm tra nguồn cấp đầu vào R, S, T (3 pha 380V hoặc 1 pha 220V vào R-S)
        double vRS = ElectricalAnalysisHelper.GetOperatingVoltageAcross(R_In, S_In, out var kRS);
        double vST = ElectricalAnalysisHelper.GetOperatingVoltageAcross(S_In, T_In, out var kST);
        double vTR = ElectricalAnalysisHelper.GetOperatingVoltageAcross(T_In, R_In, out var kTR);

        bool has3Phase = kRS == CurrentKind.AC && kST == CurrentKind.AC && kTR == CurrentKind.AC &&
                         vRS >= 180 && vST >= 180 && vTR >= 180;
        bool tWired = allWires.Any(w => w.TerminalA == T_In || w.TerminalB == T_In);
        bool has1Phase = !tWired && kRS == CurrentKind.AC && vRS >= 180;

        IsPowered = has3Phase || has1Phase;

        if (!IsPowered)
        {
            IsExternalFwdActive = IsExternalRevActive = IsExternalDi1Active = IsExternalDi2Active = false;
            // Khi mất nguồn, tần số đầu ra suy giảm nhanh về 0
            CurrentSignedFreqHz = MoveToward(CurrentSignedFreqHz, 0.0, Math.Max(40.0, P03_MaxFreqHz) * deltaSeconds);
            OutputCurrentAmps = 0.0;
            return;
        }

        // 2. Đọc trạng thái các chân lệnh số ngoài (FWD, REV, DI1, DI2 nối với COM)
        int comNet = Com.NetId;
        IsExternalFwdActive = comNet >= 0 && Fwd.NetId == comNet;
        IsExternalRevActive = comNet >= 0 && Rev.NetId == comNet;
        IsExternalDi1Active = comNet >= 0 && Di1.NetId == comNet;
        IsExternalDi2Active = comNet >= 0 && Di2.NetId == comNet;

        // 3. Xác định lệnh chạy và chiều quay theo thông số P01
        bool runCmd;
        int dirSign = 1;
        if (P01_RunSource == 1)
        {
            if (IsExternalFwdActive && !IsExternalRevActive) { runCmd = true; dirSign = 1; }
            else if (IsExternalRevActive && !IsExternalFwdActive) { runCmd = true; dirSign = -1; }
            else { runCmd = false; }
        }
        else
        {
            runCmd = KeypadRunCommand;
            dirSign = KeypadReverse ? -1 : 1;
        }

        double targetSigned = (runCmd && !HasFault) ? dirSign * ComputeTargetFrequencyHz() : 0.0;

        // 4. Tính dốc tăng tốc (P04 ACC) và giảm tốc (P05 DEC) mượt mà
        double maxF = Math.Clamp(P03_MaxFreqHz, 10.0, 400.0);
        double accRate = maxF / Math.Clamp(P04_AccelSeconds, 0.5, 60.0);
        double decRate = maxF / Math.Clamp(P05_DecelSeconds, 0.5, 60.0);

        bool accelerating = Math.Abs(targetSigned) > Math.Abs(CurrentSignedFreqHz) &&
                            (Math.Sign(targetSigned) == Math.Sign(CurrentSignedFreqHz) || Math.Abs(CurrentSignedFreqHz) < 0.05);

        double stepRate = (accelerating ? accRate : decRate) * Math.Max(0.0, deltaSeconds);
        CurrentSignedFreqHz = MoveToward(CurrentSignedFreqHz, targetSigned, stepRate);
        if (Math.Abs(CurrentSignedFreqHz) < 0.02 && Math.Abs(targetSigned) < 0.02)
            CurrentSignedFreqHz = 0.0;
    }

    private static double MoveToward(double current, double target, double maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta) return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsPowered = false;
        HasFault = false;
        KeypadRunCommand = false;
        CurrentSignedFreqHz = 0.0;
        OutputCurrentAmps = 0.0;
        IsExternalFwdActive = IsExternalRevActive = IsExternalDi1Active = IsExternalDi2Active = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["P01"] = P01_RunSource.ToString(CultureInfo.InvariantCulture),
        ["P02"] = P02_FreqSource.ToString(CultureInfo.InvariantCulture),
        ["P03"] = P03_MaxFreqHz.ToString(CultureInfo.InvariantCulture),
        ["P04"] = P04_AccelSeconds.ToString(CultureInfo.InvariantCulture),
        ["P05"] = P05_DecelSeconds.ToString(CultureInfo.InvariantCulture),
        ["P06_DI1"] = P06_Di1FreqHz.ToString(CultureInfo.InvariantCulture),
        ["P06_DI2"] = P06_Di2FreqHz.ToString(CultureInfo.InvariantCulture),
        ["KeypadFreq"] = KeypadTargetFreqHz.ToString(CultureInfo.InvariantCulture),
        ["DialFreq"] = DialFreqHz.ToString(CultureInfo.InvariantCulture),
        ["ExtAnalogPct"] = ExternalAnalogPercent.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("P01", out var s1) && int.TryParse(s1, out int p1)) P01_RunSource = Math.Clamp(p1, 0, 1);
        if (props.TryGetValue("P02", out var s2) && int.TryParse(s2, out int p2)) P02_FreqSource = Math.Clamp(p2, 0, 2);
        if (props.TryGetValue("P03", out var s3) && double.TryParse(s3, CultureInfo.InvariantCulture, out double p3)) P03_MaxFreqHz = Math.Clamp(p3, 10.0, 400.0);
        if (props.TryGetValue("P04", out var s4) && double.TryParse(s4, CultureInfo.InvariantCulture, out double p4)) P04_AccelSeconds = Math.Clamp(p4, 0.5, 60.0);
        if (props.TryGetValue("P05", out var s5) && double.TryParse(s5, CultureInfo.InvariantCulture, out double p5)) P05_DecelSeconds = Math.Clamp(p5, 0.5, 60.0);
        if (props.TryGetValue("P06_DI1", out var d1) && double.TryParse(d1, CultureInfo.InvariantCulture, out double f1)) P06_Di1FreqHz = Math.Clamp(f1, 0.0, 400.0);
        if (props.TryGetValue("P06_DI2", out var d2) && double.TryParse(d2, CultureInfo.InvariantCulture, out double f2)) P06_Di2FreqHz = Math.Clamp(f2, 0.0, 400.0);
        if (props.TryGetValue("KeypadFreq", out var kf) && double.TryParse(kf, CultureInfo.InvariantCulture, out double kv)) KeypadTargetFreqHz = Math.Clamp(kv, 0.0, P03_MaxFreqHz);
        if (props.TryGetValue("DialFreq", out var df) && double.TryParse(df, CultureInfo.InvariantCulture, out double dv)) DialFreqHz = Math.Clamp(dv, 0.0, P03_MaxFreqHz);
        if (props.TryGetValue("ExtAnalogPct", out var ep) && double.TryParse(ep, CultureInfo.InvariantCulture, out double ev)) ExternalAnalogPercent = Math.Clamp(ev, 0.0, 100.0);
    }
}
