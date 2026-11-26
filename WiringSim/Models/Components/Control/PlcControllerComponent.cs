using System.Text.Encodings.Web;
using System.Text.Json;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Khối phần cứng PLC công nghiệp (14 DI / 10 DQ / 2 AI — DC/DC/DC):
/// - 3 đèn LED hệ thống: RUN/STOP (xanh/vàng cam/nhấp nháy khi nạp), ERROR (đỏ nhấp nháy khi lỗi), MAINT (vàng cam).
/// - Hàng trên (20 chân): L+, M, PE, 1M (chọn PNP/NPN), 14 ngõ vào số (I0.0–I0.7, I1.0–I1.5), 2 ngõ vào tương tự (AI0, AI1: 0–10V).
/// - Hàng dưới (12 chân): Nguồn phụ (L+, M) và 10 ngõ ra số (Q0.0–Q0.7, Q1.0, Q1.1).
/// - Tích hợp chương trình Ladder, Bảng PLC Tags, Data Blocks, Bộ biên dịch &amp; Vòng quét 50ms.
/// </summary>
public sealed class PlcControllerComponent : ElectricalComponent
{
    public const string TypeId = "PLC_CONTROLLER";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.ProgrammableControl;

    private static readonly JsonSerializerOptions ProgramJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public PlcProgramModel DraftProgram { get; set; } = CreateDefaultProgram();
    public PlcProgramModel LoadedProgram { get; private set; } = CreateDefaultProgram();
    public PlcRuntimeEngine Runtime { get; } = new();

    public PlcCpuState CpuState { get; set; } = PlcCpuState.Run;
    public bool IsPowered { get; private set; }
    public double SupplyVoltage { get; private set; } = 24.0;
    public bool HasCompileError { get; set; }
    public bool HasOutputShortFault { get; private set; }
    public string? PlcFaultMessage { get; private set; }
    public bool IsDownloading { get; set; }
    public double DownloadProgress { get; set; }
    public double LedBlinkPhase { get; private set; }

    // --- Các chân đấu nối hàng trên (20 chân) ---
    public Terminal SupplyLPlus { get; }
    public Terminal SupplyM { get; }
    public Terminal SupplyPe { get; }
    public Terminal InputCommon1M { get; }
    public Terminal[] DiTerminals { get; } = new Terminal[14];
    public Terminal Ai0 { get; }
    public Terminal Ai1 { get; }

    // --- Các chân đấu nối hàng dưới (12 chân) ---
    public Terminal AuxLPlus { get; }
    public Terminal AuxM { get; }
    public Terminal[] DqTerminals { get; } = new Terminal[10];

    public static readonly string[] DiNames =
        ["I0.0", "I0.1", "I0.2", "I0.3", "I0.4", "I0.5", "I0.6", "I0.7", "I1.0", "I1.1", "I1.2", "I1.3", "I1.4", "I1.5"];

    public static readonly string[] DqNames =
        ["Q0.0", "Q0.1", "Q0.2", "Q0.3", "Q0.4", "Q0.5", "Q0.6", "Q0.7", "Q1.0", "Q1.1"];

    public PlcControllerComponent() : base("PLC1", 420, 200)
    {
        SupplyLPlus = AddTerminal("L+", "L+", 20, 0, TerminalSide.Top, "Nguồn cấp PLC +24VDC (L+)");
        SupplyM = AddTerminal("M", "M", 40, 0, TerminalSide.Top, "Nguồn cấp PLC 0VDC (M)");
        SupplyPe = AddTerminal("PE", "PE", 60, 0, TerminalSide.Top, "Tiếp địa bảo vệ (PE)");
        InputCommon1M = AddTerminal("1M", "1M", 80, 0, TerminalSide.Top, "Chân chung ngõ vào DI (0V = PNP, +24V = NPN)");

        for (int i = 0; i < 14; i++)
        {
            string nm = DiNames[i];
            DiTerminals[i] = AddTerminal(nm, nm, 100 + i * 20, 0, TerminalSide.Top, $"Ngõ vào số {nm} (24VDC)");
        }

        Ai0 = AddTerminal("AI0", "AI0", 380, 0, TerminalSide.Top, "Ngõ vào tương tự AI0 (0–10VDC → %IW64)");
        Ai1 = AddTerminal("AI1", "AI1", 400, 0, TerminalSide.Top, "Ngõ vào tương tự AI1 (0–10VDC → %IW66)");

        AuxLPlus = AddTerminal("AUX_L+", "L+", 20, 200, TerminalSide.Bottom, "Nguồn phụ đầu ra +24VDC (L+)");
        AuxM = AddTerminal("AUX_M", "M", 40, 200, TerminalSide.Bottom, "Nguồn phụ đầu ra 0VDC (M)");

        for (int i = 0; i < 10; i++)
        {
            string nm = DqNames[i];
            DqTerminals[i] = AddTerminal(nm, nm, 100 + i * 20, 200, TerminalSide.Bottom, $"Ngõ ra số {nm} (24VDC Sourcing)");
        }
    }

    public static PlcProgramModel CreateDefaultProgram()
    {
        var prog = new PlcProgramModel();
        prog.Tags.Add(new PlcTagEntry { Name = "START", Address = "%I0.0", DataType = "Bool" });
        prog.Tags.Add(new PlcTagEntry { Name = "STOP", Address = "%I0.1", DataType = "Bool" });
        prog.Tags.Add(new PlcTagEntry { Name = "LAMP1", Address = "%Q0.0", DataType = "Bool" });

        var nw1 = new LadderNetwork { Title = "Start / Stop" };
        nw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%I0.0" });
        nw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.0" });
        prog.Networks.Add(nw1);
        return prog;
    }

    public void CommitDownloadedProgram(PlcProgramModel compiledProgram)
    {
        DraftProgram = compiledProgram.Clone();
        LoadedProgram = compiledProgram.Clone();
        HasCompileError = false;
        HasOutputShortFault = false;
        PlcFaultMessage = null;
        Runtime.ResetAllMemory();
        CpuState = PlcCpuState.Run;
    }

    public void SetRunMode()
    {
        HasOutputShortFault = false;
        PlcFaultMessage = null;
        CpuState = PlcCpuState.Run;
    }

    public void SetStopMode()
    {
        CpuState = PlcCpuState.Stop;
        Runtime.ClearOutputsAndOverlay();
    }

    public void TriggerOutputShortFault(string reason)
    {
        HasOutputShortFault = true;
        PlcFaultMessage = reason;
        CpuState = PlcCpuState.Error;
        Runtime.ClearOutputsAndOverlay();
    }

    public override IEnumerable<(Terminal Terminal, PotentialToken Token)> GetSourceOutputs()
    {
        if (!IsPowered || IsBurnedOut)
            yield break;

        yield return (AuxLPlus, new PotentialToken(Id, AuxLPlus.Id, CurrentKind.DC, PhaseType.DC_Plus, SupplyVoltage));
        yield return (AuxM, new PotentialToken(Id, AuxM.Id, CurrentKind.DC, PhaseType.DC_Minus, 0.0));

        if (CpuState != PlcCpuState.Run || HasOutputShortFault || IsDownloading)
            yield break;

        for (int i = 0; i < DqTerminals.Length; i++)
        {
            if (Runtime.Dq[i])
            {
                yield return (DqTerminals[i], new PotentialToken(Id, DqTerminals[i].Id, CurrentKind.DC, PhaseType.DC_Plus, SupplyVoltage));
            }
        }
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        double amps = IsPowered ? (0.18 + Runtime.Dq.Count(q => q) * 0.05) : 0.0;
        yield return new InternalLoadPath(SupplyLPlus, SupplyM, $"{DisplayName} 24VDC Supply", IsPowered, amps, CurrentKind.DC);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsPowered = false;
            Runtime.ClearOutputsAndOverlay();
            return;
        }

        double vSup = ElectricalAnalysisHelper.GetOperatingVoltageAcross(SupplyLPlus, SupplyM, out var kind);
        bool correctPol = kind == CurrentKind.DC &&
                          vSup >= 18.0 && vSup <= 32.0 &&
                          SupplyLPlus.ActivePotentials.Any(p => p.CurrentKind == CurrentKind.DC && p.Phase == PhaseType.DC_Plus) &&
                          SupplyM.ActivePotentials.Any(p => p.CurrentKind == CurrentKind.DC && p.Phase == PhaseType.DC_Minus);

        IsPowered = correctPol;
        if (!IsPowered)
        {
            Array.Clear(Runtime.Di);
            Array.Clear(Runtime.Ai);
            Runtime.ClearOutputsAndOverlay();
            return;
        }

        SupplyVoltage = Math.Round(vSup, 1);

        // 1. Đọc 14 ngõ vào số DI (hỗ trợ cả Sink/PNP khi 1M=0V và Source/NPN khi 1M=+24V)
        for (int i = 0; i < DiTerminals.Length; i++)
        {
            double vIn = ElectricalAnalysisHelper.GetOperatingVoltageAcross(DiTerminals[i], InputCommon1M, out var inKind);
            Runtime.Di[i] = inKind == CurrentKind.DC && vIn >= 12.0;
        }

        // 2. Đọc 2 ngõ vào tương tự AI0, AI1 (0–10VDC -> 0–27648)
        double vAi0 = ElectricalAnalysisHelper.GetOperatingVoltageAcross(Ai0, SupplyM, out var k0);
        double vAi1 = ElectricalAnalysisHelper.GetOperatingVoltageAcross(Ai1, SupplyM, out var k1);
        Runtime.Ai[0] = k0 == CurrentKind.DC ? (int)Math.Round(Math.Clamp(vAi0, 0.0, 10.0) * 2764.8) : 0;
        Runtime.Ai[1] = k1 == CurrentKind.DC ? (int)Math.Round(Math.Clamp(vAi1, 0.0, 10.0) * 2764.8) : 0;

        // 3. Nếu CPU đang ở chế độ RUN (và không đang nạp/lỗi), thực thi vòng quét Ladder OB1!
        if (CpuState == PlcCpuState.Run && !IsDownloading && !HasOutputShortFault)
        {
            Runtime.ExecuteScanCycle(LoadedProgram, deltaSeconds);
            if (Runtime.CpuStopRequested)
            {
                SetStopMode();
            }
        }
        else
        {
            Runtime.ClearOutputsAndOverlay();
        }
    }

    public override void UpdateVisualAnimation(double deltaSeconds, bool isSimulating)
    {
        LedBlinkPhase = (LedBlinkPhase + deltaSeconds * 3.0) % 100.0;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsPowered = false;
        HasOutputShortFault = false;
        PlcFaultMessage = null;
        if (CpuState == PlcCpuState.Error)
            CpuState = PlcCpuState.Stop;
        Runtime.ResetAllMemory();
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["CpuState"] = (CpuState == PlcCpuState.Downloading ? PlcCpuState.Run : CpuState).ToString(),
        ["DraftProgramJson"] = JsonSerializer.Serialize(DraftProgram, ProgramJsonOptions),
        ["LoadedProgramJson"] = JsonSerializer.Serialize(LoadedProgram, ProgramJsonOptions)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("CpuState", out var stStr) && Enum.TryParse<PlcCpuState>(stStr, true, out var st))
            CpuState = st == PlcCpuState.Downloading ? PlcCpuState.Run : st;

        if (props.TryGetValue("DraftProgramJson", out var dJson) && !string.IsNullOrWhiteSpace(dJson))
        {
            var parsed = JsonSerializer.Deserialize<PlcProgramModel>(dJson, ProgramJsonOptions);
            if (parsed is not null) DraftProgram = parsed;
        }
        if (props.TryGetValue("LoadedProgramJson", out var lJson) && !string.IsNullOrWhiteSpace(lJson))
        {
            var parsed = JsonSerializer.Deserialize<PlcProgramModel>(lJson, ProgramJsonOptions);
            if (parsed is not null) LoadedProgram = parsed;
        }
        else
        {
            LoadedProgram = DraftProgram.Clone();
        }
    }
}
