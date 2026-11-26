using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.Models.Presets;

public sealed record PresetCircuitDefinition(
    string Key,
    string NameKey,
    string GuideKey,
    Func<(List<ElectricalComponent> Components, List<WireModel> Wires)> Builder)
{
    public string Name => LocalizationManager.Get(NameKey);
    public string GuideSummary => LocalizationManager.Get(GuideKey);
}

/// <summary>
/// Tạo sẵn 3 mạch điện mẫu chuẩn kỹ thuật công nghiệp &amp; dân dụng (đa ngôn ngữ qua LocalizationManager).
/// </summary>
public static class PresetCircuitFactory
{
    public static IReadOnlyList<PresetCircuitDefinition> Presets { get; } =
    [
        new PresetCircuitDefinition(
            "PRESET_LAMP",
            "Str.Preset1.Name",
            "Str.Preset1.Guide",
            BuildResidentialLampCircuit),

        new PresetCircuitDefinition(
            "PRESET_DOL",
            "Str.Preset2.Name",
            "Str.Preset2.Guide",
            BuildThreePhaseSelfHoldingCircuit),

        new PresetCircuitDefinition(
            "PRESET_STAR_DELTA",
            "Str.Preset3.Name",
            "Str.Preset3.Guide",
            BuildStarDeltaMotorCircuit),

        new PresetCircuitDefinition(
            "PRESET_2WIRE_AC_RELAY",
            "Str.Preset4.Name",
            "Str.Preset4.Guide",
            BuildTwoWireAcSensorRelayCircuit),

        new PresetCircuitDefinition(
            "PRESET_PLC_SEQ",
            "Str.Preset5.Name",
            "Str.Preset5.Guide",
            BuildPlcSequentialLightsCircuit)
    ];

    private static (List<ElectricalComponent>, List<WireModel>) BuildResidentialLampCircuit()
    {
        var comps = new List<ElectricalComponent>();
        var wires = new List<WireModel>();

        var src = new Ac1PhaseSourceComponent { DisplayName = "AC 220V", X = 180, Y = 80 };
        var mcb = new Mcb1PhaseComponent { DisplayName = "QF1", X = 180, Y = 260, IsOn = true };
        var sw = new Switch2PoleComponent { DisplayName = "S1", X = 420, Y = 280, IsOn = false };
        var lamp = new ResidentialLampComponent { DisplayName = "EL1", X = 640, Y = 260 };

        comps.AddRange([src, mcb, sw, lamp]);

        Connect(wires, src.LineTerminal, mcb.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, src.NeutralTerminal, mcb.In3, WireColorPreset.Black_N_DCMinus);
        Connect(wires, mcb.Out2, sw.Term1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, sw.Term2, lamp.LineTerminal, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, lamp.NeutralTerminal, mcb.Out4, WireColorPreset.Black_N_DCMinus);

        return (comps, wires);
    }

    private static (List<ElectricalComponent>, List<WireModel>) BuildThreePhaseSelfHoldingCircuit()
    {
        var comps = new List<ElectricalComponent>();
        var wires = new List<WireModel>();

        var src = new Ac3PhaseSourceComponent { DisplayName = "3P 380V", X = 140, Y = 40 };
        var qf1 = new Mcb3PhaseComponent { DisplayName = "QF1", X = 140, Y = 180, IsOn = true, RatedAmps = 20 };
        var km1 = new ContactorComponent { DisplayName = "KM1", X = 140, Y = 340 };
        var rn1 = new ThermalOverloadRelayComponent { DisplayName = "RN1", X = 140, Y = 520, CurrentRange = ThermalCurrentRange.Range_9_13A };
        var motor = new Motor3Phase3PinComponent { DisplayName = "M1", X = 140, Y = 700, RatedPowerKw = 3.0 };

        var qf2 = new Mcb1PhaseComponent { DisplayName = "QF2", X = 540, Y = 180, IsOn = true, RatedAmps = 6 };
        var btnStop = new PushButtonComponent(isNormallyClosed: true) { DisplayName = "STOP", X = 540, Y = 360 };
        var btnStart = new PushButtonComponent(isNormallyClosed: false) { DisplayName = "START", X = 540, Y = 520 };
        var lampRun = new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H1 (RUN)", X = 740, Y = 520 };
        var lampTrip = new PilotLightComponent(PilotLightColor.Yellow) { DisplayName = "H2 (TRIP)", X = 880, Y = 520 };

        comps.AddRange([src, qf1, km1, rn1, motor, qf2, btnStop, btnStart, lampRun, lampTrip]);

        Connect(wires, src.L1Terminal, qf1.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, src.L2Terminal, qf1.In3, WireColorPreset.Yellow_L2);
        Connect(wires, src.L3Terminal, qf1.In5, WireColorPreset.Blue_L3);

        Connect(wires, qf1.Out2, km1.L1In, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, qf1.Out4, km1.L2In, WireColorPreset.Yellow_L2);
        Connect(wires, qf1.Out6, km1.L3In, WireColorPreset.Blue_L3);

        Connect(wires, km1.T1Out, rn1.L1In, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, km1.T2Out, rn1.L2In, WireColorPreset.Yellow_L2);
        Connect(wires, km1.T3Out, rn1.L3In, WireColorPreset.Blue_L3);

        Connect(wires, rn1.T1Out, motor.U1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, rn1.T2Out, motor.V1, WireColorPreset.Yellow_L2);
        Connect(wires, rn1.T3Out, motor.W1, WireColorPreset.Blue_L3);
        Connect(wires, src.PeTerminal, motor.Pe, WireColorPreset.Green_PE);

        Connect(wires, src.L1Terminal, qf2.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, qf2.Out2, rn1.Nc95, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, rn1.Nc96, btnStop.TopTerminal, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStop.BottomTerminal, btnStart.TopTerminal, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, btnStop.BottomTerminal, km1.No13!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStart.BottomTerminal, km1.No14!, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, km1.No14!, km1.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, km1.CoilA1, lampRun.X1, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, qf2.Out2, rn1.No97, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, rn1.No98, lampTrip.X1, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, src.NeutralTerminal, km1.CoilA2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, km1.CoilA2, lampRun.X2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, lampRun.X2, lampTrip.X2, WireColorPreset.Black_N_DCMinus);

        return (comps, wires);
    }

    private static (List<ElectricalComponent>, List<WireModel>) BuildStarDeltaMotorCircuit()
    {
        var comps = new List<ElectricalComponent>();
        var wires = new List<WireModel>();

        var src = new Ac3PhaseSourceComponent { DisplayName = "3P 380V", X = 120, Y = 40 };
        var qf1 = new Mcb3PhaseComponent { DisplayName = "QF1", X = 120, Y = 180, IsOn = true };
        var km = new ContactorComponent { DisplayName = "KM", X = 120, Y = 360 };
        var kd = new ContactorComponent { DisplayName = "KD (Δ)", X = 420, Y = 360 };
        var ky = new ContactorComponent { DisplayName = "KY (Y)", X = 720, Y = 360 };
        var motor = new Motor3Phase6PinComponent { DisplayName = "M1", X = 120, Y = 580, RatedPowerKw = 5.5 };

        var qf2 = new Mcb1PhaseComponent { DisplayName = "QF2", X = 1040, Y = 160, IsOn = true };
        var btnStop = new PushButtonComponent(isNormallyClosed: true) { DisplayName = "STOP", X = 1040, Y = 320 };
        var btnStart = new PushButtonComponent(isNormallyClosed: false) { DisplayName = "START", X = 1040, Y = 480 };
        var timer = new TimerOnDelayComponent { DisplayName = "KT", X = 1220, Y = 360 };
        timer.SetOperatingMode(TimerOperatingMode.StarDelta);
        timer.SetDelaySeconds(5.0);

        var lampStar = new PilotLightComponent(PilotLightColor.Yellow) { DisplayName = "H_Y", X = 1040, Y = 640 };
        var lampDelta = new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H_Δ", X = 1180, Y = 640 };

        comps.AddRange([src, qf1, km, kd, ky, motor, qf2, btnStop, btnStart, timer, lampStar, lampDelta]);

        Connect(wires, src.L1Terminal, qf1.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, src.L2Terminal, qf1.In3, WireColorPreset.Yellow_L2);
        Connect(wires, src.L3Terminal, qf1.In5, WireColorPreset.Blue_L3);

        Connect(wires, qf1.Out2, km.L1In, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, qf1.Out4, km.L2In, WireColorPreset.Yellow_L2);
        Connect(wires, qf1.Out6, km.L3In, WireColorPreset.Blue_L3);

        Connect(wires, km.L1In, kd.L1In, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, km.L2In, kd.L2In, WireColorPreset.Yellow_L2);
        Connect(wires, km.L3In, kd.L3In, WireColorPreset.Blue_L3);

        Connect(wires, km.T1Out, motor.U1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, km.T2Out, motor.V1, WireColorPreset.Yellow_L2);
        Connect(wires, km.T3Out, motor.W1, WireColorPreset.Blue_L3);
        Connect(wires, src.PeTerminal, motor.Pe, WireColorPreset.Green_PE);

        Connect(wires, kd.T1Out, motor.W2, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, kd.T2Out, motor.U2, WireColorPreset.Yellow_L2);
        Connect(wires, kd.T3Out, motor.V2, WireColorPreset.Blue_L3);

        Connect(wires, ky.L1In, ky.L2In, WireColorPreset.Black_N_DCMinus);
        Connect(wires, ky.L2In, ky.L3In, WireColorPreset.Black_N_DCMinus);
        Connect(wires, ky.T1Out, motor.U2, WireColorPreset.Yellow_L2);
        Connect(wires, ky.T2Out, motor.V2, WireColorPreset.Blue_L3);
        Connect(wires, ky.T3Out, motor.W2, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, src.L1Terminal, qf2.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, qf2.Out2, btnStop.TopTerminal, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStop.BottomTerminal, btnStart.TopTerminal, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, btnStop.BottomTerminal, km.No13!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStart.BottomTerminal, km.No14!, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, km.No14!, km.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, km.CoilA1, timer.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, timer.CoilA1, timer.TimedCom, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, timer.TimedNcOrStar, kd.Nc21!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, kd.Nc22!, ky.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, ky.CoilA1, lampStar.X1, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, timer.TimedNoOrDelta, ky.Nc21!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, ky.Nc22!, kd.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, kd.CoilA1, lampDelta.X1, WireColorPreset.Red_L1_DCPlus);

        Connect(wires, src.NeutralTerminal, km.CoilA2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, km.CoilA2, kd.CoilA2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, kd.CoilA2, ky.CoilA2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, ky.CoilA2, timer.CoilA2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, timer.CoilA2, lampStar.X2, WireColorPreset.Black_N_DCMinus);
        Connect(wires, lampStar.X2, lampDelta.X2, WireColorPreset.Black_N_DCMinus);

        return (comps, wires);
    }

    private static (List<ElectricalComponent>, List<WireModel>) BuildTwoWireAcSensorRelayCircuit()
    {
        var comps = new List<ElectricalComponent>();
        var wires = new List<WireModel>();

        var src = new Ac1PhaseSourceComponent { DisplayName = "AC 220V", X = 120, Y = 60 };
        var qf1 = new Mcb1PhaseComponent { DisplayName = "QF1", X = 120, Y = 220, IsOn = true, RatedAmps = 6 };
        var sensor = new IndustrialSensorComponent(SensorKind.Inductive) { DisplayName = "B1", X = 320, Y = 180 };
        sensor.SetWiringMode(SensorWiringMode.TwoWire_AC);

        var relay = new IntermediateRelayComponent { DisplayName = "K1", X = 520, Y = 220, CoilRating = CoilVoltageRating.AC_220V };
        var vm = new VoltmeterComponent { DisplayName = "PV1", X = 520, Y = 40, MeasureKind = CurrentKind.AC, Range = VoltmeterRange.Range_300V };
        var lamp = new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H1", X = 760, Y = 220, VoltageRating = CoilVoltageRating.AC_220V };

        comps.AddRange([src, qf1, sensor, relay, vm, lamp]);

        // Nguồn -> MCB 1P
        Connect(wires, src.LineTerminal, qf1.In1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, src.NeutralTerminal, qf1.In3, WireColorPreset.Black_N_DCMinus);

        // Đấu nối tiếp CHUẨN cho cảm biến 2 dây AC 220V: L -> BN -> BU -> Cuộn hút K1 (A1-A2) -> N
        Connect(wires, qf1.Out2, sensor.BnPlus, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, sensor.BuMinus, relay.CoilA1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, relay.CoilA2, qf1.Out4, WireColorPreset.Black_N_DCMinus);

        // Đồng hồ Volt đo song song 2 đầu cuộn hút K1 để thấy rõ sụt áp ~3.5V (216.5V)!
        Connect(wires, relay.CoilA1, vm.V1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, relay.CoilA2, vm.V2, WireColorPreset.Black_N_DCMinus);

        // Tiếp điểm thường mở (NO) của K1 bật đèn báo H1
        var pole1 = relay.Poles[0];
        Connect(wires, qf1.Out2, pole1.Com, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, pole1.No, lamp.X1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, lamp.X2, qf1.Out4, WireColorPreset.Black_N_DCMinus);

        return (comps, wires);
    }

    private static (List<ElectricalComponent>, List<WireModel>) BuildPlcSequentialLightsCircuit()
    {
        var comps = new List<ElectricalComponent>();
        var wires = new List<WireModel>();

        var dc24 = new DcSourceComponent { DisplayName = "DC 24V", X = 60, Y = 40 };
        dc24.SetVoltage(24.0);

        var btnStart = new PushButtonComponent(isNormallyClosed: false) { DisplayName = "START", X = 280, Y = 40, ButtonColor = PilotLightColor.Green };
        var btnStop = new PushButtonComponent(isNormallyClosed: true) { DisplayName = "STOP", X = 420, Y = 40, ButtonColor = PilotLightColor.Red };

        var plc = new PlcControllerComponent { DisplayName = "PLC1", X = 180, Y = 220 };
        var prog = BuildSequentialLightsProgram();
        plc.CommitDownloadedProgram(prog);

        var h1 = new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H1 (Q0.0)", X = 180, Y = 500, VoltageRating = CoilVoltageRating.DC_24V };
        var h2 = new PilotLightComponent(PilotLightColor.Yellow) { DisplayName = "H2 (Q0.1)", X = 310, Y = 500, VoltageRating = CoilVoltageRating.DC_24V };
        var h3 = new PilotLightComponent(PilotLightColor.Red) { DisplayName = "H3 (Q0.2)", X = 440, Y = 500, VoltageRating = CoilVoltageRating.DC_24V };
        var h4 = new PilotLightComponent(PilotLightColor.Blue) { DisplayName = "H4 1Hz (Q0.3)", X = 570, Y = 500, VoltageRating = CoilVoltageRating.DC_24V };

        comps.AddRange([dc24, btnStart, btnStop, plc, h1, h2, h3, h4]);

        // Cấp nguồn 24VDC cho PLC (L+, M) & nối chân chung 1M về 0V (chế độ nhận tín hiệu PNP)
        Connect(wires, dc24.PlusTerminal, plc.SupplyLPlus, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, dc24.MinusTerminal, plc.SupplyM, WireColorPreset.Black_N_DCMinus);
        Connect(wires, dc24.MinusTerminal, plc.InputCommon1M, WireColorPreset.Black_N_DCMinus);

        // Nút START (NO 23-24 -> I0.0) & Nút STOP (NC 11-12 -> I0.1)
        Connect(wires, dc24.PlusTerminal, btnStart.NoIn3!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStart.NoOut4!, plc.DiTerminals[0], WireColorPreset.Yellow_L2);

        Connect(wires, dc24.PlusTerminal, btnStop.NcIn1!, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, btnStop.NcOut2!, plc.DiTerminals[1], WireColorPreset.Blue_L3);

        // Ngõ ra Q0.0, Q0.1, Q0.2 cấp nguồn 24VDC cho 3 đèn tuần tự H1..H3 và Q0.3 cho đèn nhịp 1Hz H4
        Connect(wires, plc.DqTerminals[0], h1.X1, WireColorPreset.Green_PE);
        Connect(wires, plc.DqTerminals[1], h2.X1, WireColorPreset.Yellow_L2);
        Connect(wires, plc.DqTerminals[2], h3.X1, WireColorPreset.Red_L1_DCPlus);
        Connect(wires, plc.DqTerminals[3], h4.X1, WireColorPreset.Blue_L3);

        Connect(wires, h1.X2, plc.AuxM, WireColorPreset.Black_N_DCMinus);
        Connect(wires, h2.X2, plc.AuxM, WireColorPreset.Black_N_DCMinus);
        Connect(wires, h3.X2, plc.AuxM, WireColorPreset.Black_N_DCMinus);
        Connect(wires, h4.X2, plc.AuxM, WireColorPreset.Black_N_DCMinus);

        return (comps, wires);
    }

    public static PlcProgramModel BuildSequentialLightsProgram()
    {
        var prog = new PlcProgramModel
        {
            EnableSystemMemory = true,
            SystemMemoryByte = 1, // MB1: FirstScan (%M1.0), AlwaysTRUE (%M1.2), AlwaysFALSE (%M1.3)
            EnableClockMemory = true,
            ClockMemoryByte = 0   // MB0: Clock_10Hz (%M0.0) .. Clock_1Hz (%M0.5) .. Clock_0.5Hz (%M0.7)
        };

        // Tránh MB0 (Clock) & MB1 (System): đặt toàn bộ biến người dùng từ %M10.0 và %MW20 trở đi
        prog.Tags.AddRange([
            new PlcTagEntry { Name = "START", Address = "%I0.0", DataType = "Bool", Comment = "Start pushbutton (NO)" },
            new PlcTagEntry { Name = "STOP", Address = "%I0.1", DataType = "Bool", Comment = "Stop pushbutton (NC)" },
            new PlcTagEntry { Name = "RUN_SYS", Address = "%M10.0", DataType = "Bool", Comment = "System running latch" },
            new PlcTagEntry { Name = "START_EDGE", Address = "%M10.1", DataType = "Bool", Comment = "Start rising edge memory" },
            new PlcTagEntry { Name = "STEP_PULSE", Address = "%M10.2", DataType = "Bool", Comment = "3s timer completion pulse" },
            new PlcTagEntry { Name = "HALF_TIME", Address = "%M10.3", DataType = "Bool", Comment = "Elapsed time >= T#1500ms" },
            new PlcTagEntry { Name = "SEQUENCE", Address = "%MW20", DataType = "Int", Comment = "Active lamp step (1..3)" },
            new PlcTagEntry { Name = "AI0_RAW", Address = "%IW64", DataType = "Int", Comment = "Analog input AI0 (0..27648)" },
            new PlcTagEntry { Name = "AI0_NORM", Address = "%MD24", DataType = "Real", Comment = "Normalized AI0 (0.0..1.0)" },
            new PlcTagEntry { Name = "AI0_PERCENT", Address = "%MD28", DataType = "Real", Comment = "Scaled AI0 (0.0..100.0 %)" },
            new PlcTagEntry { Name = "TIMER1", Address = "%DB1", DataType = "IEC_Timer", Comment = "3s step delay timer" },
            new PlcTagEntry { Name = "LAMP1", Address = "%Q0.0", DataType = "Bool", Comment = "Green lamp H1" },
            new PlcTagEntry { Name = "LAMP2", Address = "%Q0.1", DataType = "Bool", Comment = "Yellow lamp H2" },
            new PlcTagEntry { Name = "LAMP3", Address = "%Q0.2", DataType = "Bool", Comment = "Red lamp H3" },
            new PlcTagEntry { Name = "LAMP_1HZ", Address = "%Q0.3", DataType = "Bool", Comment = "Blue 1Hz heartbeat lamp H4" }
        ]);

        // Network 1: Khởi tạo an toàn ở vòng quét đầu tiên bằng biến hệ thống "FirstScan"
        var nw1 = new LadderNetwork { Title = "FirstScan Initialization (System Memory)" };
        nw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "\"FirstScan\"" });
        nw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Move, DataType = "Int", Param1 = "0", OutputAddress = "%MW20" });
        prog.Networks.Add(nw1);

        // Network 2: Khởi động & Dừng hệ thống (Tự giữ %I0.0 // %M10.0)
        var nw2 = new LadderNetwork { Title = "Start / Stop System (Self-Holding)" };
        nw2.MainBranch.Nodes.Add(new LadderNode
        {
            Kind = LadderElementKind.ParallelBlock,
            Branches =
            [
                new LadderBranch { Nodes = [new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%I0.0" }] },
                new LadderBranch { Nodes = [new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%M10.0" }] }
            ]
        });
        nw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%I0.1" });
        nw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%M10.0" });
        prog.Networks.Add(nw2);

        // Network 3: Khởi tạo bước 1 khi bật (P-Trig) & Xóa bước về 0 khi dừng
        var nw3 = new LadderNetwork { Title = "Init Step = 1 on Start / Reset = 0 on Stop" };
        nw3.MainBranch.Nodes.Add(new LadderNode
        {
            Kind = LadderElementKind.ParallelBlock,
            Branches =
            [
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%M10.0" },
                        new LadderNode { Kind = LadderElementKind.PTrig, Address = "%M10.1" },
                        new LadderNode { Kind = LadderElementKind.Move, DataType = "Int", Param1 = "1", OutputAddress = "%MW20" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.ContactNC, Address = "%M10.0" },
                        new LadderNode { Kind = LadderElementKind.Move, DataType = "Int", Param1 = "0", OutputAddress = "%MW20" }
                    ]
                }
            ]
        });
        prog.Networks.Add(nw3);

        // Network 4: Bộ định thời TON 3 giây tạo xung chuyển bước & so sánh trực tiếp hằng số thời gian T#1500ms
        var nw4 = new LadderNetwork { Title = "3s Step Timer (TON %DB1) & Direct Time Compare (T#1500ms)" };
        nw4.MainBranch.Nodes.Add(new LadderNode
        {
            Kind = LadderElementKind.ParallelBlock,
            Branches =
            [
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%M10.0" },
                        new LadderNode { Kind = LadderElementKind.ContactNC, Address = "%M10.2" },
                        new LadderNode { Kind = LadderElementKind.TimerTON, Address = "%DB1", SymbolName = "TIMER1", Param1 = "T#3s" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%M10.2" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.CmpGreaterOrEqual, DataType = "Time", Param1 = "\"TIMER1\".ET", Param2 = "T#1500ms" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%M10.3" }
                    ]
                }
            ]
        });
        prog.Networks.Add(nw4);

        // Network 5: Tăng biến bước %MW20 (ADD +1) và quay vòng về 1 khi > 3
        var nw5 = new LadderNetwork { Title = "Increment %MW20 (1 → 2 → 3 → 1)" };
        nw5.MainBranch.Nodes.Add(new LadderNode
        {
            Kind = LadderElementKind.ParallelBlock,
            Branches =
            [
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%M10.2" },
                        new LadderNode { Kind = LadderElementKind.MathAdd, DataType = "Int", Param1 = "%MW20", Param2 = "1", OutputAddress = "%MW20" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.CmpGreater, DataType = "Int", Param1 = "%MW20", Param2 = "3" },
                        new LadderNode { Kind = LadderElementKind.Move, DataType = "Int", Param1 = "1", OutputAddress = "%MW20" }
                    ]
                }
            ]
        });
        prog.Networks.Add(nw5);

        // Network 6: So sánh %MW20 xuất 3 đèn tuần tự Q0.0..Q0.2 & đèn nhịp Clock_1Hz ra Q0.3
        var nw6 = new LadderNetwork { Title = "Sequential Outputs & Clock_1Hz Heartbeat" };
        nw6.MainBranch.Nodes.Add(new LadderNode
        {
            Kind = LadderElementKind.ParallelBlock,
            Branches =
            [
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.CmpEqual, DataType = "Int", Param1 = "%MW20", Param2 = "1" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.0" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.CmpEqual, DataType = "Int", Param1 = "%MW20", Param2 = "2" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.1" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.CmpEqual, DataType = "Int", Param1 = "%MW20", Param2 = "3" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.2" }
                    ]
                },
                new LadderBranch
                {
                    Nodes =
                    [
                        new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%M10.0" },
                        new LadderNode { Kind = LadderElementKind.ContactNO, Address = "\"Clock_1Hz\"" },
                        new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.3" }
                    ]
                }
            ]
        });
        prog.Networks.Add(nw6);

        // Network 7: Chuẩn hóa & quy đổi tuyến tính ngõ vào Analog AI0 (%IW64: 0..27648 -> 0.0..100.0 %)
        var nw7 = new LadderNetwork { Title = "Analog Input Scaling (NORM_X & SCALE_X: 0..27648 → 0..100%)" };
        nw7.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "\"AlwaysTRUE\"" });
        nw7.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.NormX, DataType = "Int", Param1 = "%IW64", Param2 = "0", ExtraParam = "27648", OutputAddress = "%MD24" });
        nw7.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ScaleX, DataType = "Real", Param1 = "%MD24", Param2 = "0.0", ExtraParam = "100.0", OutputAddress = "%MD28" });
        prog.Networks.Add(nw7);

        // Cấu hình sẵn Watch Table mẫu để người dùng theo dõi & ép giá trị ngay lập tức
        prog.WatchEntries.AddRange([
            new PlcWatchEntry { Expression = "\"RUN_SYS\"", DisplayFormat = PlcWatchFormat.Bool },
            new PlcWatchEntry { Expression = "\"SEQUENCE\"", DisplayFormat = PlcWatchFormat.Dec },
            new PlcWatchEntry { Expression = "\"TIMER1\".ET", DisplayFormat = PlcWatchFormat.Time },
            new PlcWatchEntry { Expression = "\"Clock_1Hz\"", DisplayFormat = PlcWatchFormat.Bool },
            new PlcWatchEntry { Expression = "\"AI0_RAW\"", DisplayFormat = PlcWatchFormat.Dec, ModifyValue = "13824" },
            new PlcWatchEntry { Expression = "\"AI0_PERCENT\"", DisplayFormat = PlcWatchFormat.Float }
        ]);

        return prog;
    }

    private static void Connect(List<WireModel> wires, Terminal a, Terminal b, WireColorPreset color)
    {
        wires.Add(new WireModel(a, b, color));
    }
}
