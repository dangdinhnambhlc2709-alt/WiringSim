using System.Globalization;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// 1. Rơ-le trung gian (Intermediate Relay - chuẩn Omron MY2N / MY4N):
/// - Cuộn hút: 13(A1) - 14(A2), chọn điện áp 12VDC, 24VDC, 220VAC (kiểm tra quá áp cháy cuộn hút).
/// - Cấu hình chân tiếp điểm:
///   + DPDT 8 chân (MY2N): 2 cặp chuyển mạch độc lập (COM1-NO1-NC1, COM2-NO2-NC2) + A1-A2.
///   + 4PDT 14 chân (MY4N): 4 cặp chuyển mạch độc lập (COM1..4, NO1..4, NC1..4) + A1-A2.
/// </summary>
public sealed class IntermediateRelayComponent : ElectricalComponent
{
    public const string TypeId = "INTERMEDIATE_RELAY";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Relay;

    public CoilVoltageRating CoilRating { get; set; } = CoilVoltageRating.DC_24V;
    public IntermediateRelayPoles PoleConfig { get; private set; } = IntermediateRelayPoles.DPDT_8Pin;

    public bool IsCoilEnergized { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    public sealed record ChangeoverPole(int Index, Terminal Com, Terminal No, Terminal Nc);

    private readonly List<ChangeoverPole> _poles = new();
    public IReadOnlyList<ChangeoverPole> Poles => _poles;

    public Terminal CoilA1 { get; private set; } = null!;
    public Terminal CoilA2 { get; private set; } = null!;

    public IntermediateRelayComponent() : base("Rơ-le MY2N", 160, 140)
    {
        RebuildTerminals();
    }

    public void SetPoleConfig(IntermediateRelayPoles config)
    {
        if (PoleConfig == config) return;
        PoleConfig = config;
        Width = config == IntermediateRelayPoles.DPDT_8Pin ? 160 : 280;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        _poles.Clear();

        if (PoleConfig == IntermediateRelayPoles.DPDT_8Pin)
        {
            // Chuẩn đế 8 chân MY2N: 2 bộ COM-NO-NC + cuộn hút 13(A1)-14(A2)
            var nc1 = AddOrReuseTerminal(existing, "1", "1(NC1)", 20, 0, TerminalSide.Top, "Thường đóng cặp 1 (Chân 1)");
            var no1 = AddOrReuseTerminal(existing, "5", "5(NO1)", 60, 0, TerminalSide.Top, "Thường mở cặp 1 (Chân 5)");
            var nc2 = AddOrReuseTerminal(existing, "4", "4(NC2)", 100, 0, TerminalSide.Top, "Thường đóng cặp 2 (Chân 4)");
            var no2 = AddOrReuseTerminal(existing, "8", "8(NO2)", 140, 0, TerminalSide.Top, "Thường mở cặp 2 (Chân 8)");

            var com1 = AddOrReuseTerminal(existing, "9", "9(COM1)", 20, 140, TerminalSide.Bottom, "Chân chung cặp 1 (Chân 9)");
            var com2 = AddOrReuseTerminal(existing, "12", "12(COM2)", 60, 140, TerminalSide.Bottom, "Chân chung cặp 2 (Chân 12)");
            CoilA1 = AddOrReuseTerminal(existing, "13", "13(A1)", 100, 140, TerminalSide.Bottom, "Cuộn hút Rơ-le 13 (A1 / +)");
            CoilA2 = AddOrReuseTerminal(existing, "14", "14(A2)", 140, 140, TerminalSide.Bottom, "Cuộn hút Rơ-le 14 (A2 / -)");

            _poles.Add(new ChangeoverPole(1, com1, no1, nc1));
            _poles.Add(new ChangeoverPole(2, com2, no2, nc2));
        }
        else
        {
            // Chuẩn đế 14 chân MY4N: 4 bộ COM-NO-NC + cuộn hút 13(A1)-14(A2)
            int[] ncPins = [1, 2, 3, 4];
            int[] noPins = [5, 6, 7, 8];
            int[] comPins = [9, 10, 11, 12];

            var ncs = new Terminal[4];
            var nos = new Terminal[4];
            for (int i = 0; i < 4; i++)
            {
                double xNc = 20 + i * 60;
                double xNo = 40 + i * 60;
                ncs[i] = AddOrReuseTerminal(existing, ncPins[i].ToString(), $"{ncPins[i]}(NC{i + 1})", xNc, 0, TerminalSide.Top, $"Thường đóng cặp {i + 1} (Chân {ncPins[i]})");
                nos[i] = AddOrReuseTerminal(existing, noPins[i].ToString(), $"{noPins[i]}(NO{i + 1})", xNo, 0, TerminalSide.Top, $"Thường mở cặp {i + 1} (Chân {noPins[i]})");
            }

            for (int i = 0; i < 4; i++)
            {
                double xCom = 20 + i * 60;
                var com = AddOrReuseTerminal(existing, comPins[i].ToString(), $"{comPins[i]}(C{i + 1})", xCom, 140, TerminalSide.Bottom, $"Chân chung cặp {i + 1} (Chân {comPins[i]})");
                _poles.Add(new ChangeoverPole(i + 1, com, nos[i], ncs[i]));
            }

            CoilA1 = AddOrReuseTerminal(existing, "13", "13(A1)", 240, 140, TerminalSide.Bottom, "Cuộn hút Rơ-le 13 (A1 / +)");
            CoilA2 = AddOrReuseTerminal(existing, "14", "14(A2)", 260, 140, TerminalSide.Bottom, "Cuộn hút Rơ-le 14 (A2 / -)");
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        bool active = IsCoilEnergized && !IsBurnedOut;
        foreach (var pole in _poles)
        {
            if (active)
                yield return new InternalConductivePath(pole.Com, pole.No, $"{DisplayName} (COM{pole.Index}-NO{pole.Index})");
            else
                yield return new InternalConductivePath(pole.Com, pole.Nc, $"{DisplayName} (COM{pole.Index}-NC{pole.Index})");
        }
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
        yield return new InternalLoadPath(CoilA1, CoilA2, $"{DisplayName} Coil", IsCoilEnergized && !IsBurnedOut, 0.04, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsCoilEnergized = false;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(CoilA1, CoilA2, CoilRating, out double v, out var kind);
        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLabel) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
            IsBurnedOut = true;
            IsCoilEnergized = false;
            BurnoutReason = WiringSim.UI.Services.LocalizationManager.Format("Str.Fault.BurnoutCoilMsg", DisplayName, v, kind, ratingLabel);
            return;
        }

        IsCoilEnergized = SupplyStatus == RatedSupplyStatus.Normal;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsCoilEnergized = false;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["CoilRating"] = CoilRating.ToString(),
        ["PoleConfig"] = PoleConfig.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("CoilRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            CoilRating = r;
        if (props.TryGetValue("PoleConfig", out var pStr) && Enum.TryParse<IntermediateRelayPoles>(pStr, true, out var p))
            SetPoleConfig(p);
    }
}

/// <summary>
/// 2. Rơ-le nhiệt (Thermal Overload Relay - chuẩn LS MT / Schneider LRD):
/// - 3 cực động lực nối tiếp: L1-T1, L2-T2, L3-T3 (thẳng hàng dọc với Contactor &amp; Động cơ 3 pha).
/// - 2 cặp tiếp điểm phụ bảo vệ độc lập: 95-96 (NC) và 97-98 (NO).
/// - Chọn dải dòng (4-6A, 9-13A, 12-18A), chế độ Reset tay / tự động, nút Test Trip &amp; Reset trực tiếp,
///   và tự động tích lũy nhiệt nhảy Trip khi dòng tải Động cơ 3 pha vượt quá ngưỡng chỉnh định!
/// </summary>
public sealed class ThermalOverloadRelayComponent : ElectricalComponent
{
    public const string TypeId = "THERMAL_RELAY";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Relay;

    public ThermalCurrentRange CurrentRange { get; set; } = ThermalCurrentRange.Range_9_13A;
    public bool AutoReset { get; set; }
    public bool IsTripped { get; private set; }

    /// <summary>Dòng điện thực tế (A) đi qua thanh lưỡng kim (do Simulation Engine cập nhật từ tải động cơ).</summary>
    public double MeasuredCurrentAmps { get; set; }

    /// <summary>Mức tích lũy nhiệt (0.0 -> 1.0 = 100% Trip).</summary>
    public double ThermalLoadRatio { get; private set; }
    private double _autoResetCooldown;

    public Terminal L1In { get; }
    public Terminal T1Out { get; }
    public Terminal L2In { get; }
    public Terminal T2Out { get; }
    public Terminal L3In { get; }
    public Terminal T3Out { get; }

    public Terminal Nc95 { get; }
    public Terminal Nc96 { get; }
    public Terminal No97 { get; }
    public Terminal No98 { get; }

    public ThermalOverloadRelayComponent() : base("RN (Rơ-le nhiệt)", 200, 140)
    {
        L1In = AddTerminal("L1", "L1(1)", 20, 0, TerminalSide.Top, "Động lực vào L1");
        L2In = AddTerminal("L2", "L2(3)", 60, 0, TerminalSide.Top, "Động lực vào L2");
        L3In = AddTerminal("L3", "L3(5)", 100, 0, TerminalSide.Top, "Động lực vào L3");
        Nc95 = AddTerminal("95", "95(NC)", 140, 0, TerminalSide.Top, "Tiếp điểm thường đóng bảo vệ 95 (NC)");
        No97 = AddTerminal("97", "97(NO)", 180, 0, TerminalSide.Top, "Tiếp điểm thường mở báo lỗi 97 (NO)");

        T1Out = AddTerminal("T1", "T1(2)", 20, 140, TerminalSide.Bottom, "Động lực ra T1");
        T2Out = AddTerminal("T2", "T2(4)", 60, 140, TerminalSide.Bottom, "Động lực ra T2");
        T3Out = AddTerminal("T3", "T3(6)", 100, 140, TerminalSide.Bottom, "Động lực ra T3");
        Nc96 = AddTerminal("96", "96(NC)", 140, 140, TerminalSide.Bottom, "Tiếp điểm thường đóng bảo vệ 96 (NC)");
        No98 = AddTerminal("98", "98(NO)", 180, 140, TerminalSide.Bottom, "Tiếp điểm thường mở báo lỗi 98 (NO)");
    }

    public (double MinA, double MaxA, string Label) GetRangeSpec() => CurrentRange switch
    {
        ThermalCurrentRange.Range_4_6A => (4.0, 6.0, "4–6A"),
        ThermalCurrentRange.Range_9_13A => (9.0, 13.0, "9–13A"),
        ThermalCurrentRange.Range_12_18A => (12.0, 18.0, "12–18A"),
        _ => (9.0, 13.0, "9–13A")
    };

    public void TestTrip()
    {
        IsTripped = true;
        ThermalLoadRatio = 1.0;
        _autoResetCooldown = 3.0;
    }

    public void ManualResetAction()
    {
        IsTripped = false;
        ThermalLoadRatio = 0.0;
        _autoResetCooldown = 0.0;
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Thanh lưỡng kim động lực luôn dẫn dòng chính tới động cơ
        yield return new InternalConductivePath(L1In, T1Out, $"{DisplayName} (L1-T1)");
        yield return new InternalConductivePath(L2In, T2Out, $"{DisplayName} (L2-T2)");
        yield return new InternalConductivePath(L3In, T3Out, $"{DisplayName} (L3-T3)");

        // Tiếp điểm phụ điều khiển: Bình thường 95-96 đóng; Khi quá tải 95-96 mở và 97-98 đóng!
        if (!IsTripped)
        {
            yield return new InternalConductivePath(Nc95, Nc96, $"{DisplayName} (95-96 NC)");
        }
        else
        {
            yield return new InternalConductivePath(No97, No98, $"{DisplayName} (97-98 NO)");
        }
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        var (_, maxAmps, _) = GetRangeSpec();

        if (!IsTripped)
        {
            if (MeasuredCurrentAmps > maxAmps)
            {
                // Quá tải càng lớn nhảy càng nhanh (tỷ lệ (I / I_max)^2)
                double overloadFactor = Math.Pow(MeasuredCurrentAmps / maxAmps, 2.0);
                ThermalLoadRatio = Math.Min(1.0, ThermalLoadRatio + deltaSeconds * 0.35 * overloadFactor);
                if (ThermalLoadRatio >= 1.0)
                {
                    IsTripped = true;
                    _autoResetCooldown = 3.0;
                }
            }
            else
            {
                ThermalLoadRatio = Math.Max(0.0, ThermalLoadRatio - deltaSeconds * 0.25);
            }
        }
        else if (AutoReset && MeasuredCurrentAmps < 0.1)
        {
            _autoResetCooldown -= deltaSeconds;
            ThermalLoadRatio = Math.Max(0.0, _autoResetCooldown / 3.0);
            if (_autoResetCooldown <= 0.0)
            {
                IsTripped = false;
                ThermalLoadRatio = 0.0;
            }
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsTripped = false;
        ThermalLoadRatio = 0;
        MeasuredCurrentAmps = 0;
        _autoResetCooldown = 0;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["CurrentRange"] = CurrentRange.ToString(),
        ["AutoReset"] = AutoReset.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("CurrentRange", out var rStr) && Enum.TryParse<ThermalCurrentRange>(rStr, true, out var r))
            CurrentRange = r;
        if (props.TryGetValue("AutoReset", out var aStr) && bool.TryParse(aStr, out bool ar))
            AutoReset = ar;
    }
}

/// <summary>
/// 3. Rơ-le thời gian đa năng (Timer chuẩn Omron H3CR):
/// - Chế độ hoạt động: ON-Delay, OFF-Delay, hoặc Timer Sao - Tam giác (Star-Delta).
/// - Điện áp cuộn hút: 24VDC, 220VAC (có bảo vệ báo cháy cuộn hút khi cấp quá áp).
/// - Cấu hình tiếp điểm: 1 cặp định thời (COM - NO - NC) hoặc 1 cặp tức thời + 1 cặp định thời (đế 8 chân).
/// - Thời gian đặt: 0.5s đến 60.0s.
/// </summary>
public sealed class TimerOnDelayComponent : ElectricalComponent
{
    public const string TypeId = "TIMER_ONDELAY";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Relay;

    public TimerOperatingMode OperatingMode { get; private set; } = TimerOperatingMode.OnDelay;
    public CoilVoltageRating CoilRating { get; set; } = CoilVoltageRating.AC_220V;
    public TimerContactConfig ContactConfig { get; private set; } = TimerContactConfig.Timed1C;

    public double DelaySeconds { get; private set; } = 5.0;
    public double RemainingSeconds { get; private set; } = 5.0;
    public bool IsCoilEnergized { get; private set; }
    public bool IsTimedOutputActive { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    // Phục vụ OFF-Delay & Star-Delta
    private bool _offDelayArmed;
    private bool _starActive;
    private bool _deltaActive;
    private double _starDeltaDeadTimeRemaining;

    // Cờ giữ tương thích với tên thuộc tính cũ
    public bool IsTimedOut => OperatingMode == TimerOperatingMode.StarDelta ? _deltaActive : IsTimedOutputActive;

    // Chân đấu nối chính (luôn có mặt)
    public Terminal CoilA1 { get; private set; } = null!;
    public Terminal CoilA2 { get; private set; } = null!;
    public Terminal TimedCom { get; private set; } = null!;
    public Terminal TimedNcOrStar { get; private set; } = null!;
    public Terminal TimedNoOrDelta { get; private set; } = null!;

    // Chân cặp tiếp điểm tức thời (khi chọn cấu hình đế 8 chân: 1 cặp tức thời + 1 cặp định thời)
    public Terminal? InstCom { get; private set; }
    public Terminal? InstNc { get; private set; }
    public Terminal? InstNo { get; private set; }

    public TimerOnDelayComponent() : base("Timer H3CR", 160, 140)
    {
        RebuildTerminals();
    }

    public void SetOperatingMode(TimerOperatingMode mode)
    {
        if (OperatingMode == mode) return;
        OperatingMode = mode;
        RebuildTerminals();
        ResetSimulationState();
        NotifyTerminalsChanged();
    }

    public void SetContactConfig(TimerContactConfig config)
    {
        if (ContactConfig == config) return;
        ContactConfig = config;
        Width = config == TimerContactConfig.InstPlusTimed8Pin ? 220 : 160;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        InstCom = InstNc = InstNo = null;

        bool isStarDelta = OperatingMode == TimerOperatingMode.StarDelta;
        string ncName = isStarDelta ? "Y(Sao)" : "5(NC)";
        string noName = isStarDelta ? "Δ(TG)" : "6(NO)";
        string ncDesc = isStarDelta ? "Tiếp điểm khởi động SAO (Y)" : "Tiếp điểm định thời thường đóng 5 (NC)";
        string noDesc = isStarDelta ? "Tiếp điểm chạy TAM GIÁC (Δ)" : "Tiếp điểm định thời thường mở 6 (NO)";

        if (ContactConfig == TimerContactConfig.Timed1C)
        {
            Width = 160;
            CoilA1 = AddOrReuseTerminal(existing, "A1", "2(A1)", 20, 0, TerminalSide.Top, "Cuộn nguồn Timer 2 (A1 / +)");
            TimedCom = AddOrReuseTerminal(existing, "COM", "8(COM)", 80, 0, TerminalSide.Top, "Chân chung tiếp điểm định thời 8 (COM)");

            CoilA2 = AddOrReuseTerminal(existing, "A2", "7(A2)", 20, 140, TerminalSide.Bottom, "Cuộn nguồn Timer 7 (A2 / -)");
            TimedNcOrStar = AddOrReuseTerminal(existing, "NC", ncName, 80, 140, TerminalSide.Bottom, ncDesc);
            TimedNoOrDelta = AddOrReuseTerminal(existing, "NO", noName, 140, 140, TerminalSide.Bottom, noDesc);
        }
        else
        {
            // Đế 8 chân H3CR-A8: Cuộn 2-7, Cặp tức thời (1-4-3) + Cặp định thời (8-5-6)
            Width = 220;
            CoilA1 = AddOrReuseTerminal(existing, "A1", "2(A1)", 20, 0, TerminalSide.Top, "Cuộn nguồn Timer 2 (A1 / +)");
            InstCom = AddOrReuseTerminal(existing, "ICOM", "1(C.Tức)", 80, 0, TerminalSide.Top, "Chân chung tiếp điểm tức thời 1");
            TimedCom = AddOrReuseTerminal(existing, "COM", "8(C.Trễ)", 160, 0, TerminalSide.Top, "Chân chung tiếp điểm định thời 8");

            CoilA2 = AddOrReuseTerminal(existing, "A2", "7(A2)", 20, 140, TerminalSide.Bottom, "Cuộn nguồn Timer 7 (A2 / -)");
            InstNc = AddOrReuseTerminal(existing, "INC", "4(NC.T)", 60, 140, TerminalSide.Bottom, "Thường đóng tức thời 4");
            InstNo = AddOrReuseTerminal(existing, "INO", "3(NO.T)", 100, 140, TerminalSide.Bottom, "Thường mở tức thời 3");
            TimedNcOrStar = AddOrReuseTerminal(existing, "NC", ncName, 160, 140, TerminalSide.Bottom, ncDesc);
            TimedNoOrDelta = AddOrReuseTerminal(existing, "NO", noName, 200, 140, TerminalSide.Bottom, noDesc);
        }
    }

    public void SetDelaySeconds(double seconds)
    {
        DelaySeconds = Math.Clamp(Math.Round(seconds, 1), 0.5, 60.0);
        if (!IsCoilEnergized && !_offDelayArmed)
        {
            RemainingSeconds = DelaySeconds;
        }
    }

    public void AdjustDelay(double delta)
    {
        SetDelaySeconds(DelaySeconds + delta);
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsBurnedOut)
        {
            // Khi cháy cuộn hút, tiếp điểm rơi về trạng thái nghỉ
            if (OperatingMode != TimerOperatingMode.StarDelta)
                yield return new InternalConductivePath(TimedCom, TimedNcOrStar, $"{DisplayName} (COM-NC)");
            if (InstCom is not null && InstNc is not null)
                yield return new InternalConductivePath(InstCom, InstNc, $"{DisplayName} (Inst NC)");
            yield break;
        }

        // 1. Cặp tiếp điểm tức thời (nếu dùng đế 8 chân)
        if (InstCom is not null && InstNc is not null && InstNo is not null)
        {
            if (IsCoilEnergized)
                yield return new InternalConductivePath(InstCom, InstNo, $"{DisplayName} (1-3 Inst NO)");
            else
                yield return new InternalConductivePath(InstCom, InstNc, $"{DisplayName} (1-4 Inst NC)");
        }

        // 2. Cặp tiếp điểm định thời theo chế độ
        if (OperatingMode == TimerOperatingMode.StarDelta)
        {
            if (_starActive)
                yield return new InternalConductivePath(TimedCom, TimedNcOrStar, $"{DisplayName} (COM-Y Sao)");
            else if (_deltaActive)
                yield return new InternalConductivePath(TimedCom, TimedNoOrDelta, $"{DisplayName} (COM-Δ Tam giác)");
        }
        else
        {
            if (IsTimedOutputActive)
                yield return new InternalConductivePath(TimedCom, TimedNoOrDelta, $"{DisplayName} (COM-NO)");
            else
                yield return new InternalConductivePath(TimedCom, TimedNcOrStar, $"{DisplayName} (COM-NC)");
        }
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
        yield return new InternalLoadPath(CoilA1, CoilA2, $"{DisplayName} Coil", IsCoilEnergized && !IsBurnedOut, 0.04, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsCoilEnergized = false;
            IsTimedOutputActive = false;
            _starActive = _deltaActive = false;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(CoilA1, CoilA2, CoilRating, out double v, out var kind);
        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLabel) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
            IsBurnedOut = true;
            IsCoilEnergized = false;
            IsTimedOutputActive = false;
            _starActive = _deltaActive = false;
            BurnoutReason = WiringSim.UI.Services.LocalizationManager.Format("Str.Fault.BurnoutCoilMsg", DisplayName, v, kind, ratingLabel);
            return;
        }

        bool powered = SupplyStatus == RatedSupplyStatus.Normal;
        IsCoilEnergized = powered;

        switch (OperatingMode)
        {
            case TimerOperatingMode.OnDelay:
                if (powered)
                {
                    if (!IsTimedOutputActive)
                    {
                        RemainingSeconds = Math.Max(0.0, RemainingSeconds - deltaSeconds);
                        if (RemainingSeconds <= 0.0001)
                        {
                            RemainingSeconds = 0.0;
                            IsTimedOutputActive = true;
                        }
                    }
                }
                else
                {
                    IsTimedOutputActive = false;
                    RemainingSeconds = DelaySeconds;
                }
                break;

            case TimerOperatingMode.OffDelay:
                if (powered)
                {
                    // Khi có điện: Đóng tiếp điểm ngay lập tức và nạp sẵn thời gian trễ ngắt
                    IsTimedOutputActive = true;
                    _offDelayArmed = true;
                    RemainingSeconds = DelaySeconds;
                }
                else if (_offDelayArmed)
                {
                    // Khi mất điện: Giữ tiếp điểm đóng và đếm ngược về 0 rồi mới ngắt!
                    RemainingSeconds = Math.Max(0.0, RemainingSeconds - deltaSeconds);
                    if (RemainingSeconds <= 0.0001)
                    {
                        RemainingSeconds = 0.0;
                        IsTimedOutputActive = false;
                        _offDelayArmed = false;
                    }
                }
                else
                {
                    IsTimedOutputActive = false;
                    RemainingSeconds = DelaySeconds;
                }
                break;

            case TimerOperatingMode.StarDelta:
                if (powered)
                {
                    if (!_deltaActive && _starDeltaDeadTimeRemaining <= 0.0 && RemainingSeconds > 0.0)
                    {
                        _starActive = true;
                        RemainingSeconds = Math.Max(0.0, RemainingSeconds - deltaSeconds);
                        if (RemainingSeconds <= 0.0001)
                        {
                            RemainingSeconds = 0.0;
                            _starActive = false;
                            _starDeltaDeadTimeRemaining = 0.10; // Thời gian trễ chuyển mạch an toàn 100ms
                        }
                    }
                    else if (!_deltaActive && _starDeltaDeadTimeRemaining > 0.0)
                    {
                        _starActive = false;
                        _starDeltaDeadTimeRemaining = Math.Max(0.0, _starDeltaDeadTimeRemaining - deltaSeconds);
                        if (_starDeltaDeadTimeRemaining <= 0.0001)
                        {
                            _deltaActive = true;
                        }
                    }
                }
                else
                {
                    _starActive = false;
                    _deltaActive = false;
                    _starDeltaDeadTimeRemaining = 0.0;
                    RemainingSeconds = DelaySeconds;
                }
                break;
        }
    }

    public bool IsCountingActive => OperatingMode switch
    {
        TimerOperatingMode.OnDelay => IsCoilEnergized && !IsTimedOutputActive,
        TimerOperatingMode.OffDelay => !IsCoilEnergized && _offDelayArmed,
        TimerOperatingMode.StarDelta => IsCoilEnergized && !_deltaActive,
        _ => false
    };

    public bool IsStarActive => _starActive;
    public bool IsDeltaActive => _deltaActive;

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsCoilEnergized = false;
        IsTimedOutputActive = false;
        _offDelayArmed = false;
        _starActive = false;
        _deltaActive = false;
        _starDeltaDeadTimeRemaining = 0.0;
        RemainingSeconds = DelaySeconds;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["OperatingMode"] = OperatingMode.ToString(),
        ["CoilRating"] = CoilRating.ToString(),
        ["ContactConfig"] = ContactConfig.ToString(),
        ["DelaySeconds"] = DelaySeconds.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("OperatingMode", out var mStr) && Enum.TryParse<TimerOperatingMode>(mStr, true, out var m))
            SetOperatingMode(m);
        if (props.TryGetValue("CoilRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            CoilRating = r;
        if (props.TryGetValue("ContactConfig", out var cStr) && Enum.TryParse<TimerContactConfig>(cStr, true, out var c))
            SetContactConfig(c);
        if (props.TryGetValue("DelaySeconds", out var val) &&
            double.TryParse(val, CultureInfo.InvariantCulture, out double sec))
            SetDelaySeconds(sec);
    }
}
