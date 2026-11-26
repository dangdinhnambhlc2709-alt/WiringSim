using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models;

public sealed record PaletteItemDescriptor(
    string TypeKey,
    string TitleKey,
    string SubtitleKey,
    string BadgeText,
    string DefaultTagPrefix,
    string AccentHex,
    ComponentCategory Category,
    Func<ElectricalComponent> Factory)
{
    public string Title => LocalizationManager.Get(TitleKey);
    public string Subtitle => LocalizationManager.Get(SubtitleKey);
}

/// <summary>
/// Registry quản lý tập trung toàn bộ thiết bị điện theo 6 nhóm chuẩn công nghiệp,
/// không viết cứng chuỗi ngôn ngữ — tra cứu hoàn toàn qua LocalizationManager.
/// </summary>
public static class ComponentRegistry
{
    private static readonly List<PaletteItemDescriptor> _items = new();

    static ComponentRegistry()
    {
        // 1. NGUỒN CẤP
        Register(new(DcSourceComponent.TypeId, "Str.Comp.DcSource.Title", "Str.Comp.DcSource.Sub", "DC", "G", "#EF4444", ComponentCategory.PowerSource, () => new DcSourceComponent { DisplayName = "G1" }));
        Register(new(Ac1PhaseSourceComponent.TypeId, "Str.Comp.Ac1P.Title", "Str.Comp.Ac1P.Sub", "1P~", "AC", "#F59E0B", ComponentCategory.PowerSource, () => new Ac1PhaseSourceComponent { DisplayName = "AC1" }));
        Register(new(Ac3PhaseSourceComponent.TypeId, "Str.Comp.Ac3P.Title", "Str.Comp.Ac3P.Sub", "3P~", "3P", "#EAB308", ComponentCategory.PowerSource, () => new Ac3PhaseSourceComponent { DisplayName = "3P1" }));

        // 2. KHÍ CỤ ĐÓNG CẮT & CÔNG TẮC
        Register(new(Mcb1PhaseComponent.TypeId, "Str.Comp.Mcb1P.Title", "Str.Comp.Mcb1P.Sub", "MCB 1P", "QF", "#0EA5E9", ComponentCategory.SwitchingAndControl, () => new Mcb1PhaseComponent { DisplayName = "QF1" }));
        Register(new(Mcb3PhaseComponent.TypeId, "Str.Comp.Mcb3P.Title", "Str.Comp.Mcb3P.Sub", "MCB 3P", "QF", "#0284C7", ComponentCategory.SwitchingAndControl, () => new Mcb3PhaseComponent { DisplayName = "QF1" }));
        Register(new(FuseComponent.TypeId, "Str.Comp.Fuse.Title", "Str.Comp.Fuse.Sub", "FUSE", "FU", "#F59E0B", ComponentCategory.SwitchingAndControl, () => new FuseComponent { DisplayName = "FU1" }));
        Register(new(KnifeSwitchComponent.TypeId, "Str.Comp.KnifeSw.Title", "Str.Comp.KnifeSw.Sub", "KNIFE", "CD", "#D97706", ComponentCategory.SwitchingAndControl, () => new KnifeSwitchComponent { DisplayName = "CD1" }));
        Register(new(SwitchComponent.TypeIdOneWay, "Str.Comp.Sw1W.Title", "Str.Comp.Sw1W.Sub", "SW 1W", "S", "#14B8A6", ComponentCategory.SwitchingAndControl, () => new SwitchComponent(SwitchKind.OneWay_1_2) { DisplayName = "S1" }));
        Register(new(SwitchComponent.TypeIdTwoWay, "Str.Comp.Sw2W.Title", "Str.Comp.Sw2W.Sub", "SW 2W", "S", "#0D9488", ComponentCategory.SwitchingAndControl, () => new SwitchComponent(SwitchKind.TwoWay_Staircase) { DisplayName = "S1" }));
        Register(new(SwitchComponent.TypeIdRotary, "Str.Comp.SwRot.Title", "Str.Comp.SwRot.Sub", "1-0-2", "SA", "#0F766E", ComponentCategory.SwitchingAndControl, () => new SwitchComponent(SwitchKind.Rotary3Pos_1_0_2) { DisplayName = "SA1" }));
        Register(new(PushButtonComponent.TypeIdNO, "Str.Comp.PbNo.Title", "Str.Comp.PbNo.Sub", "LA38", "SB", "#22C55E", ComponentCategory.SwitchingAndControl, () => new PushButtonComponent(false) { DisplayName = "SB1" }));
        Register(new(PushButtonComponent.TypeIdNC, "Str.Comp.PbNc.Title", "Str.Comp.PbNc.Sub", "LA38", "SB", "#EF4444", ComponentCategory.SwitchingAndControl, () => new PushButtonComponent(true) { DisplayName = "SB1" }));
        Register(new(IlluminatedPushButtonComponent.TypeId, "Str.Comp.PbIllum.Title", "Str.Comp.PbIllum.Sub", "LED PB", "SB", "#10B981", ComponentCategory.SwitchingAndControl, () => new IlluminatedPushButtonComponent { DisplayName = "SB1" }));
        Register(new(EmergencyStopComponent.TypeId, "Str.Comp.EStop.Title", "Str.Comp.EStop.Sub", "E-STOP", "ES", "#DC2626", ComponentCategory.SwitchingAndControl, () => new EmergencyStopComponent { DisplayName = "ES1" }));
        Register(new(LimitSwitchComponent.TypeId, "Str.Comp.LimitSw.Title", "Str.Comp.LimitSw.Sub", "ME8104", "SQ", "#F97316", ComponentCategory.SwitchingAndControl, () => new LimitSwitchComponent { DisplayName = "SQ1" }));
        Register(new(ContactorComponent.TypeId, "Str.Comp.Contactor.Title", "Str.Comp.Contactor.Sub", "KM", "KM", "#8B5CF6", ComponentCategory.SwitchingAndControl, () => new ContactorComponent { DisplayName = "KM1" }));
        Register(new(VfdComponent.TypeId, "Str.Comp.Vfd.Title", "Str.Comp.Vfd.Sub", "VFD", "VFD", "#EC4899", ComponentCategory.SwitchingAndControl, () => new VfdComponent { DisplayName = "VFD1" }));

        // 3. RƠ-LE CHUYÊN DỤNG
        Register(new(IntermediateRelayComponent.TypeId, "Str.Comp.RelayMy.Title", "Str.Comp.RelayMy.Sub", "MY2N", "K", "#38BDF8", ComponentCategory.Relay, () => new IntermediateRelayComponent { DisplayName = "K1" }));
        Register(new(ThermalOverloadRelayComponent.TypeId, "Str.Comp.Thermal.Title", "Str.Comp.Thermal.Sub", "OL RN", "RN", "#FB923C", ComponentCategory.Relay, () => new ThermalOverloadRelayComponent { DisplayName = "RN1" }));
        Register(new(TimerOnDelayComponent.TypeId, "Str.Comp.Timer.Title", "Str.Comp.Timer.Sub", "H3CR", "KT", "#D946EF", ComponentCategory.Relay, () => new TimerOnDelayComponent { DisplayName = "KT1" }));
        Register(new(RotaryTimerAh3Component.TypeId, "Str.Comp.TimerAh3.Title", "Str.Comp.TimerAh3.Sub", "AH3-3", "KT", "#EAB308", ComponentCategory.Relay, () => new RotaryTimerAh3Component { DisplayName = "KT1" }));

        // 4. CẢM BIẾN & CÔNG TẮC CHUYÊN DỤNG (2 DÂY AC/DC & 3/4 DÂY PNP/NPN)
        Register(new(IndustrialSensorComponent.TypeIdInductive, "Str.Comp.SensInd.Title", "Str.Comp.SensInd.Sub", "IND", "B", "#06B6D4", ComponentCategory.Sensor, () => new IndustrialSensorComponent(SensorKind.Inductive) { DisplayName = "B1" }));
        Register(new(IndustrialSensorComponent.TypeIdCapacitive, "Str.Comp.SensCap.Title", "Str.Comp.SensCap.Sub", "CAP", "B", "#0EA5E9", ComponentCategory.Sensor, () => new IndustrialSensorComponent(SensorKind.Capacitive) { DisplayName = "B1" }));
        Register(new(IndustrialSensorComponent.TypeIdPhoto, "Str.Comp.SensPhoto.Title", "Str.Comp.SensPhoto.Sub", "PHOTO", "B", "#F59E0B", ComponentCategory.Sensor, () => new IndustrialSensorComponent(SensorKind.Photoelectric) { DisplayName = "B1" }));
        Register(new(IndustrialSensorComponent.TypeIdUltrasonic, "Str.Comp.SensUltra.Title", "Str.Comp.SensUltra.Sub", "ULTRA", "B", "#8B5CF6", ComponentCategory.Sensor, () => new IndustrialSensorComponent(SensorKind.Ultrasonic) { DisplayName = "B1" }));
        Register(new(CylinderReedSwitchComponent.TypeId, "Str.Comp.ReedCyl.Title", "Str.Comp.ReedCyl.Sub", "D-A93", "RS", "#EF4444", ComponentCategory.Sensor, () => new CylinderReedSwitchComponent { DisplayName = "RS1" }));
        Register(new(ThermalSwitchComponent.TypeId, "Str.Comp.ThermSw.Title", "Str.Comp.ThermSw.Sub", "KSD301", "TS", "#F97316", ComponentCategory.Sensor, () => new ThermalSwitchComponent { DisplayName = "TS1" }));
        Register(new(FloatSwitchComponent.TypeId, "Str.Comp.FloatSw.Title", "Str.Comp.FloatSw.Sub", "FLOAT", "FS", "#0284C7", ComponentCategory.Sensor, () => new FloatSwitchComponent { DisplayName = "FS1" }));

        // 5. THIẾT BỊ ĐO LƯỜNG GẮN TỦ (VOLT / AMPE / CT / CHUYỂN MẠCH KDE)
        Register(new(VoltmeterComponent.TypeId, "Str.Comp.Voltmeter.Title", "Str.Comp.Voltmeter.Sub", "VOLT", "PV", "#3B82F6", ComponentCategory.Meter, () => new VoltmeterComponent { DisplayName = "PV1" }));
        Register(new(AmmeterComponent.TypeId, "Str.Comp.Ammeter.Title", "Str.Comp.Ammeter.Sub", "AMPS", "PA", "#EC4899", ComponentCategory.Meter, () => new AmmeterComponent { DisplayName = "PA1" }));
        Register(new(CurrentTransformerComponent.TypeId, "Str.Comp.Ct.Title", "Str.Comp.Ct.Sub", "CT /5A", "CT", "#A855F7", ComponentCategory.Meter, () => new CurrentTransformerComponent { DisplayName = "CT1" }));
        Register(new(CamSwitchComponent.TypeId, "Str.Comp.CamSw.Title", "Str.Comp.CamSw.Sub", "KDE", "SA", "#6366F1", ComponentCategory.Meter, () => new CamSwitchComponent { DisplayName = "SA1" }));

        // 6. TẢI TIÊU THỤ
        Register(new(PilotLightComponent.TypeIdGreen, "Str.Comp.PilotG.Title", "Str.Comp.PilotG.Sub", "PILOT", "H", "#22C55E", ComponentCategory.Load, () => new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H1" }));
        Register(new(ResidentialLampComponent.TypeId, "Str.Comp.Lamp.Title", "Str.Comp.Lamp.Sub", "LAMP", "EL", "#EAB308", ComponentCategory.Load, () => new ResidentialLampComponent { DisplayName = "EL1" }));
        Register(new(Motor1PhaseComponent.TypeId, "Str.Comp.Motor1P.Title", "Str.Comp.Motor1P.Sub", "M 1~", "M", "#38BDF8", ComponentCategory.Load, () => new Motor1PhaseComponent { DisplayName = "M1" }));
        Register(new(Motor3Phase3PinComponent.TypeId, "Str.Comp.Motor3P3.Title", "Str.Comp.Motor3P3.Sub", "M 3P", "M", "#06B6D4", ComponentCategory.Load, () => new Motor3Phase3PinComponent { DisplayName = "M1" }));
        Register(new(Motor3Phase6PinComponent.TypeId, "Str.Comp.Motor3P6.Title", "Str.Comp.Motor3P6.Sub", "M 6P", "M", "#0891B2", ComponentCategory.Load, () => new Motor3Phase6PinComponent { DisplayName = "M1" }));
        Register(new(MotorLinearTrackComponent.TypeId, "Str.Comp.MotorTrack.Title", "Str.Comp.MotorTrack.Sub", "TRACK", "MT", "#14B8A6", ComponentCategory.Load, () => new MotorLinearTrackComponent { DisplayName = "MT1" }));

        // 7. ĐIỀU KHIỂN LẬP TRÌNH (PLC)
        Register(new(PlcControllerComponent.TypeId, "Str.Comp.Plc.Title", "Str.Comp.Plc.Sub", "PLC", "PLC", "#009999", ComponentCategory.ProgrammableControl, () => new PlcControllerComponent { DisplayName = "PLC1" }));
    }

    public static IReadOnlyList<PaletteItemDescriptor> Items => _items;

    public static void Register(PaletteItemDescriptor descriptor) => _items.Add(descriptor);

    public static PaletteItemDescriptor? FindDescriptor(string typeKey) =>
        _items.FirstOrDefault(x => string.Equals(x.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

    public static ElectricalComponent Create(string typeKey)
    {
        if (string.Equals(typeKey, WireJunctionComponent.TypeId, StringComparison.OrdinalIgnoreCase))
            return new WireJunctionComponent();
        if (typeKey.StartsWith("PLC_", StringComparison.OrdinalIgnoreCase))
            return new PlcControllerComponent { DisplayName = "PLC1" };
        if (string.Equals(typeKey, "MOTOR_3P", StringComparison.OrdinalIgnoreCase))
            return new Motor3Phase6PinComponent { DisplayName = "M1" };
        if (string.Equals(typeKey, PilotLightComponent.TypeIdRed, StringComparison.OrdinalIgnoreCase))
            return new PilotLightComponent(PilotLightColor.Red) { DisplayName = "H1" };
        if (string.Equals(typeKey, PilotLightComponent.TypeIdYellow, StringComparison.OrdinalIgnoreCase))
            return new PilotLightComponent(PilotLightColor.Yellow) { DisplayName = "H1" };
        if (string.Equals(typeKey, PilotLightComponent.TypeIdWhite, StringComparison.OrdinalIgnoreCase))
            return new PilotLightComponent(PilotLightColor.White) { DisplayName = "H1" };

        var found = FindDescriptor(typeKey)
                    ?? throw new InvalidOperationException($"Unknown component type '{typeKey}'.");
        return found.Factory();
    }
}
