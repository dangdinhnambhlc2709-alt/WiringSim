using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Components.Loads;

/// <summary>
/// Module tích hợp: Bàn trượt Động cơ 3 pha (Trục vít me) &amp; 2 Công tắc hành trình LS1 (Trái) / LS2 (Phải):
/// - Cụm chân động cơ 3 pha: U1, V1, W1, PE (hỗ trợ đảo chiều pha &amp; điều tốc qua Biến tần VFD).
/// - Cụm chân công tắc hành trình đầu trái LS1: 1-2 (NC), 3-4 (NO).
/// - Cụm chân công tắc hành trình đầu phải LS2: 1-2 (NC), 3-4 (NO).
/// - Cơ chế tương tác cơ – điện tự động:
///   + Quay thuận: Con trượt chạy Trái → Phải, chạm LS2 thì gạt cần LS2 (mở 1-2, đóng 3-4) và dừng ở cữ chặn.
///   + Quay nghịch (đảo 2 trong 3 pha): Con trượt chạy Phải → Trái, nhả LS2, chạm LS1 thì gạt cần LS1.
/// </summary>
public sealed class MotorLinearTrackComponent : Motor3PhaseBaseComponent
{
    public const string TypeId = "MOTOR_LINEAR_TRACK";
    public override string TypeKey => TypeId;

    public static readonly double[] SupportedTravelDurations = [3.0, 5.0, 8.0, 12.0];

    public TrackInitialPosition InitialPosition { get; set; } = TrackInitialPosition.LeftAtLS1;
    public double TravelDurationSeconds { get; set; } = 5.0;

    /// <summary>Vị trí hiện tại của con trượt trên trục vít me: 0.0 (đầu trái LS1) → 1.0 (đầu phải LS2).</summary>
    public double CarriagePosition { get; set; }

    /// <summary>Góc xoay vít me phục vụ hiệu ứng ren xoắn di chuyển mượt 60 FPS.</summary>
    public double LeadscrewPhase { get; private set; }

    public bool IsLs1Actuated => CarriagePosition <= 0.03;
    public bool IsLs2Actuated => CarriagePosition >= 0.97;

    // 8 chân của 2 công tắc hành trình LS1 (trái) & LS2 (phải)
    public Terminal Ls1_Nc1 { get; }
    public Terminal Ls1_Nc2 { get; }
    public Terminal Ls1_No3 { get; }
    public Terminal Ls1_No4 { get; }

    public Terminal Ls2_Nc1 { get; }
    public Terminal Ls2_Nc2 { get; }
    public Terminal Ls2_No3 { get; }
    public Terminal Ls2_No4 { get; }

    public MotorLinearTrackComponent() : base("MT1", 360, 180)
    {
        // U1 (20,0), V1 (60,0), W1 (100,0), PE (140,0) đã được tạo sẵn từ Motor3PhaseBaseComponent!

        // Hàng dưới bên trái: Công tắc hành trình LS1 (1-2 NC, 3-4 NO)
        Ls1_Nc1 = AddTerminal("LS1_1", "LS1:1", 20, 180, TerminalSide.Bottom, "LS1 (Trái) — NC chân 1");
        Ls1_Nc2 = AddTerminal("LS1_2", "LS1:2", 60, 180, TerminalSide.Bottom, "LS1 (Trái) — NC chân 2");
        Ls1_No3 = AddTerminal("LS1_3", "LS1:3", 100, 180, TerminalSide.Bottom, "LS1 (Trái) — NO chân 3");
        Ls1_No4 = AddTerminal("LS1_4", "LS1:4", 140, 180, TerminalSide.Bottom, "LS1 (Trái) — NO chân 4");

        // Hàng dưới bên phải: Công tắc hành trình LS2 (1-2 NC, 3-4 NO)
        Ls2_Nc1 = AddTerminal("LS2_1", "LS2:1", 220, 180, TerminalSide.Bottom, "LS2 (Phải) — NC chân 1");
        Ls2_Nc2 = AddTerminal("LS2_2", "LS2:2", 260, 180, TerminalSide.Bottom, "LS2 (Phải) — NC chân 2");
        Ls2_No3 = AddTerminal("LS2_3", "LS2:3", 300, 180, TerminalSide.Bottom, "LS2 (Phải) — NO chân 3");
        Ls2_No4 = AddTerminal("LS2_4", "LS2:4", 340, 180, TerminalSide.Bottom, "LS2 (Phải) — NO chân 4");

        ApplyInitialPosition();
    }

    public void ApplyInitialPosition()
    {
        CarriagePosition = InitialPosition switch
        {
            TrackInitialPosition.Center => 0.5,
            TrackInitialPosition.RightAtLS2 => 1.0,
            _ => 0.0
        };
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // LS1 đầu trái: chạm LS1 -> mở 1-2, đóng 3-4
        if (IsLs1Actuated)
            yield return new InternalConductivePath(Ls1_No3, Ls1_No4, $"{DisplayName} LS1 (3-4 NO)");
        else
            yield return new InternalConductivePath(Ls1_Nc1, Ls1_Nc2, $"{DisplayName} LS1 (1-2 NC)");

        // LS2 đầu phải: chạm LS2 -> mở 1-2, đóng 3-4
        if (IsLs2Actuated)
            yield return new InternalConductivePath(Ls2_No3, Ls2_No4, $"{DisplayName} LS2 (3-4 NO)");
        else
            yield return new InternalConductivePath(Ls2_Nc1, Ls2_Nc2, $"{DisplayName} LS2 (1-2 NC)");
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

        int count = CountLinePhases(pU, pV, pW);
        if (count == 0)
        {
            RotationDirection = 0;
            IsPhaseLost = false;
            TargetSpeedFactor = 0.0;
            OperatingCurrentAmps = 0.0;
            ConnectionModeLabel = $"{CarriagePosition * 100.0:0}%";
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
        int cmdDir = DeterminePhaseSequenceDirection(pU, pV, pW);
        double freqScale = Math.Clamp(AppliedFrequencyHz / 50.0, 0.08, 4.0);
        double speedPerSec = freqScale / Math.Clamp(TravelDurationSeconds, 1.0, 30.0);

        // Chặn cữ cơ học 2 đầu hành trình
        bool blockedRight = cmdDir > 0 && CarriagePosition >= 1.0;
        bool blockedLeft = cmdDir < 0 && CarriagePosition <= 0.0;

        if (!blockedRight && !blockedLeft)
        {
            RotationDirection = cmdDir;
            TargetSpeedFactor = 1.0;
            CarriagePosition = Math.Clamp(CarriagePosition + cmdDir * speedPerSec * Math.Max(0.0, deltaSeconds), 0.0, 1.0);
            OperatingCurrentAmps = NominalCurrentAmps;
        }
        else
        {
            // Chạm cữ chặn cuối hành trình -> dừng cơ học (nếu mạch ngoài chưa ngắt vẫn có dòng giữ nhẹ)
            RotationDirection = 0;
            TargetSpeedFactor = 0.0;
            OperatingCurrentAmps = Math.Round(NominalCurrentAmps * 1.1, 1);
        }

        ConnectionModeLabel = cmdDir > 0 ? $"→ {CarriagePosition * 100.0:0}%" : $"← {CarriagePosition * 100.0:0}%";
    }

    public override void UpdateVisualAnimation(double frameDeltaSeconds, bool isSimulating)
    {
        base.UpdateVisualAnimation(frameDeltaSeconds, isSimulating);
        if (Math.Abs(CurrentAngularVelocity) > 1.0)
        {
            LeadscrewPhase = (LeadscrewPhase + CurrentAngularVelocity * frameDeltaSeconds * 0.12) % 10.0;
            if (LeadscrewPhase < 0) LeadscrewPhase += 10.0;
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        ApplyInitialPosition();
        LeadscrewPhase = 0;
    }

    public override Dictionary<string, string> ExportProperties()
    {
        var d = base.ExportProperties();
        d["InitialPosition"] = InitialPosition.ToString();
        d["TravelDuration"] = TravelDurationSeconds.ToString(CultureInfo.InvariantCulture);
        return d;
    }

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        base.ImportProperties(props);
        if (props.TryGetValue("InitialPosition", out var posStr) && Enum.TryParse<TrackInitialPosition>(posStr, true, out var pos))
        {
            InitialPosition = pos;
            ApplyInitialPosition();
        }
        if (props.TryGetValue("TravelDuration", out var durStr) && double.TryParse(durStr, CultureInfo.InvariantCulture, out double dur))
            TravelDurationSeconds = Math.Clamp(dur, 1.0, 30.0);
    }
}
