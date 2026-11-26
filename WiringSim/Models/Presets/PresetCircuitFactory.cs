using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
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
            BuildStarDeltaMotorCircuit)
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

    private static void Connect(List<WireModel> wires, Terminal a, Terminal b, WireColorPreset color)
    {
        wires.Add(new WireModel(a, b, color));
    }
}
