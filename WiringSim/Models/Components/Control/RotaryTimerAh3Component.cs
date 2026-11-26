using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Rơ-le thời gian xoay cơ học kiểu CKC AH3-3 (Interactive Rotary Timer — Đế tròn 8 chân):
/// - Nguồn cuộn dây (POWER): Chân 2 và 7 (12VDC, 24VDC, 110VAC, 220VAC).
/// - Cụm tiếp điểm KHÔNG định thời (Instantaneous — tác động ngay khi cấp nguồn 2-7): 1(COM), 3(NO), 4(NC).
/// - Cụm tiếp điểm CÓ định thời (Timed — tác động sau khi hết thời gian đặt): 8(COM), 6(NO), 5(NC).
/// - Tương tác trực tiếp: Bấm giữ chuột kéo xoay núm vặn tròn hoặc lăn con lăn chuột ngay trên thân Timer.
/// </summary>
public sealed class RotaryTimerAh3Component : ElectricalComponent
{
    public const string TypeId = "TIMER_AH3_3";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.Relay;

    public CoilVoltageRating CoilRating { get; set; } = CoilVoltageRating.AC_220V;
    public Ah3TimeRange TimeRange { get; private set; } = Ah3TimeRange.Range_10s;
    public double DelaySeconds { get; private set; } = 5.0;

    public bool IsCoilEnergized { get; private set; }
    public bool IsTimeUp { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double RemainingSeconds => Math.Max(0.0, DelaySeconds - ElapsedSeconds);
    public RatedSupplyStatus SupplyStatus { get; private set; }

    // 8 chân đấu nối chuẩn đế 8 chân CKC AH3-3
    public Terminal InstCom1 { get; }
    public Terminal InstNo3 { get; }
    public Terminal InstNc4 { get; }
    public Terminal Power2 { get; }

    public Terminal TimedCom8 { get; }
    public Terminal TimedNo6 { get; }
    public Terminal TimedNc5 { get; }
    public Terminal Power7 { get; }

    public RotaryTimerAh3Component() : base("KT", 160, 160)
    {
        // Hàng trên: Cụm tiếp điểm tức thời (1-COM, 3-NO, 4-NC) + Chân nguồn 2
        InstCom1 = AddTerminal("1", "1(COM)", 20, 0, TerminalSide.Top, "Chân chung tức thời 1 (Inst COM)");
        InstNo3 = AddTerminal("3", "3(NO)", 60, 0, TerminalSide.Top, "Thường mở tức thời 3 (Inst NO)");
        InstNc4 = AddTerminal("4", "4(NC)", 100, 0, TerminalSide.Top, "Thường đóng tức thời 4 (Inst NC)");
        Power2 = AddTerminal("2", "2(PWR)", 140, 0, TerminalSide.Top, "Chân nguồn cuộn dây 2");

        // Hàng dưới: Cụm tiếp điểm định thời (8-COM, 6-NO, 5-NC) + Chân nguồn 7
        TimedCom8 = AddTerminal("8", "8(COM)", 20, 160, TerminalSide.Bottom, "Chân chung định thời 8 (Timed COM)");
        TimedNo6 = AddTerminal("6", "6(NO)", 60, 160, TerminalSide.Bottom, "Thường mở định thời 6 (Timed NO)");
        TimedNc5 = AddTerminal("5", "5(NC)", 100, 160, TerminalSide.Bottom, "Thường đóng định thời 5 (Timed NC)");
        Power7 = AddTerminal("7", "7(PWR)", 140, 160, TerminalSide.Bottom, "Chân nguồn cuộn dây 7");
    }

    public double MaxRangeSeconds => TimeRange switch
    {
        Ah3TimeRange.Range_30s => 30.0,
        Ah3TimeRange.Range_60s => 60.0,
        Ah3TimeRange.Range_10m => 600.0,
        _ => 10.0
    };

    public string RangeShortLabel => TimeRange switch
    {
        Ah3TimeRange.Range_30s => "30S",
        Ah3TimeRange.Range_60s => "60S",
        Ah3TimeRange.Range_10m => "10M",
        _ => "10S"
    };

    public void SetTimeRange(Ah3TimeRange range)
    {
        double ratio = Math.Clamp(DelaySeconds / Math.Max(0.1, MaxRangeSeconds), 0.05, 1.0);
        TimeRange = range;
        DelaySeconds = Math.Round(Math.Clamp(ratio * MaxRangeSeconds, 0.1, MaxRangeSeconds), 1);
    }

    public void SetDelaySeconds(double seconds)
    {
        DelaySeconds = Math.Round(Math.Clamp(seconds, 0.1, MaxRangeSeconds), 1);
    }

    public void StepDial(int deltaSteps)
    {
        double step = TimeRange switch
        {
            Ah3TimeRange.Range_10s => 0.5,
            Ah3TimeRange.Range_30s => 1.0,
            Ah3TimeRange.Range_60s => 2.0,
            Ah3TimeRange.Range_10m => 10.0,
            _ => 0.5
        };
        SetDelaySeconds(DelaySeconds + deltaSteps * step);
    }

    /// <summary>
    /// Quy đổi thời gian đặt sang góc xoay kim đỏ trên mặt đồng hồ: -135° (0) → +135° (Max).
    /// Góc 0° hướng thẳng lên trên (12 giờ).
    /// </summary>
    public double GetDialAngleDegrees()
    {
        double ratio = Math.Clamp(DelaySeconds / Math.Max(0.1, MaxRangeSeconds), 0.0, 1.0);
        return -135.0 + ratio * 270.0;
    }

    /// <summary>
    /// Cập nhật thời gian đặt từ góc kéo chuột (độ, với 0° là 12 giờ, chiều dương theo kim đồng hồ).
    /// </summary>
    public void SetDelayFromDialAngle(double angleFromTopClockwiseDeg)
    {
        // Chuẩn hóa góc vào [-180, 180] rồi kẹp vào dải mặt số [-135, +135]
        double a = ((angleFromTopClockwiseDeg + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        double clamped = Math.Clamp(a, -135.0, 135.0);
        double ratio = (clamped + 135.0) / 270.0;
        double rawSec = ratio * MaxRangeSeconds;

        double snapStep = TimeRange switch
        {
            Ah3TimeRange.Range_10s => 0.2,
            Ah3TimeRange.Range_30s => 0.5,
            Ah3TimeRange.Range_60s => 1.0,
            Ah3TimeRange.Range_10m => 5.0,
            _ => 0.5
        };
        double snapped = Math.Round(rawSec / snapStep) * snapStep;
        SetDelaySeconds(Math.Max(0.1, snapped));
    }

    public string FormatDuration(double sec)
    {
        if (TimeRange == Ah3TimeRange.Range_10m && sec >= 60.0)
        {
            int m = (int)(sec / 60.0);
            double s = sec - m * 60.0;
            return $"{m}m{s:00}s";
        }
        return $"{sec:0.#}s";
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Cụm tiếp điểm tức thời (1-3 NO, 1-4 NC): tác động ngay khi cuộn 2-7 có điện
        if (IsCoilEnergized && !IsBurnedOut)
            yield return new InternalConductivePath(InstCom1, InstNo3, $"{DisplayName} (1-3 Inst NO)");
        else
            yield return new InternalConductivePath(InstCom1, InstNc4, $"{DisplayName} (1-4 Inst NC)");

        // Cụm tiếp điểm định thời (8-6 NO, 8-5 NC): tác động khi hết thời gian trễ (UP sáng)
        if (IsTimeUp && !IsBurnedOut)
            yield return new InternalConductivePath(TimedCom8, TimedNo6, $"{DisplayName} (8-6 Timed NO)");
        else
            yield return new InternalConductivePath(TimedCom8, TimedNc5, $"{DisplayName} (8-5 Timed NC)");
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
        yield return new InternalLoadPath(Power2, Power7, $"{DisplayName} Coil (2-7)", IsCoilEnergized && !IsBurnedOut, 0.04, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsCoilEnergized = false;
            IsTimeUp = false;
            ElapsedSeconds = 0;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(Power2, Power7, CoilRating, out double v, out var appliedKind);
        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLabel) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
            IsBurnedOut = true;
            IsCoilEnergized = false;
            IsTimeUp = false;
            ElapsedSeconds = 0;
            BurnoutReason = LocalizationManager.Format("Str.Fault.BurnoutCoilMsg", DisplayName, v, appliedKind, ratingLabel);
            return;
        }

        IsCoilEnergized = SupplyStatus == RatedSupplyStatus.Normal;
        if (IsCoilEnergized)
        {
            if (ElapsedSeconds < DelaySeconds)
            {
                ElapsedSeconds = Math.Min(DelaySeconds, ElapsedSeconds + Math.Max(0.0, deltaSeconds));
            }
            IsTimeUp = ElapsedSeconds >= DelaySeconds - 0.001;
        }
        else
        {
            ElapsedSeconds = 0;
            IsTimeUp = false;
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsCoilEnergized = false;
        IsTimeUp = false;
        ElapsedSeconds = 0;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["CoilRating"] = CoilRating.ToString(),
        ["TimeRange"] = TimeRange.ToString(),
        ["DelaySeconds"] = DelaySeconds.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("CoilRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            CoilRating = r;
        if (props.TryGetValue("TimeRange", out var trStr) && Enum.TryParse<Ah3TimeRange>(trStr, true, out var tr))
            TimeRange = tr;
        if (props.TryGetValue("DelaySeconds", out var dStr) && double.TryParse(dStr, CultureInfo.InvariantCulture, out double sec))
            SetDelaySeconds(sec);
    }
}
