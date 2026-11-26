namespace WiringSim.Models.Enums;

public enum CurrentKind
{
    None = 0,
    AC = 1,
    DC = 2
}

public enum PhaseType
{
    None = 0,
    L1 = 1,
    L2 = 2,
    L3 = 3,
    N = 4,
    PE = 5,
    DC_Plus = 6,
    DC_Minus = 7
}

public enum TerminalSide
{
    Top,
    Bottom,
    Left,
    Right
}

public enum WireColorPreset
{
    Red_L1_DCPlus = 0,
    Yellow_L2 = 1,
    Blue_L3 = 2,
    Black_N_DCMinus = 3,
    Green_PE = 4
}

public enum ComponentCategory
{
    PowerSource,
    SwitchingAndControl,
    Relay,
    Sensor,
    Meter,
    Load
}

public enum AppMode
{
    Design,
    Simulation
}

public enum WireRoutingStyle
{
    Orthogonal,
    Curved
}

public enum PilotLightColor
{
    Green,
    Red,
    Yellow,
    White,
    Blue
}

public enum McbCurveType
{
    B,
    C,
    D
}

public enum PushButtonActionMode
{
    Momentary,
    Latching
}

public enum PushButtonContactConfig
{
    Only1NO,
    Only1NC,
    Dual1NO1NC
}

public enum SwitchKind
{
    OneWay_1_2,
    TwoWay_Staircase,
    Rotary3Pos_1_0_2
}

public enum ContactorAuxConfig
{
    Only1NO,
    Only1NC,
    Both1NO1NC
}

public enum CoilVoltageRating
{
    DC_12V,
    DC_24V,
    AC_24V,
    AC_110V,
    AC_220V,
    AC_380V
}

public enum IntermediateRelayPoles
{
    DPDT_8Pin,
    FourPDT_14Pin
}

public enum ThermalCurrentRange
{
    Range_4_6A,
    Range_9_13A,
    Range_12_18A
}

public enum TimerOperatingMode
{
    OnDelay,
    OffDelay,
    StarDelta
}

public enum TimerContactConfig
{
    Timed1C,
    InstPlusTimed8Pin
}

public enum SensorKind
{
    Inductive,
    Capacitive,
    Photoelectric,
    Ultrasonic
}

public enum SensorPolarity
{
    PNP,
    NPN
}

public enum SensorOutputConfig
{
    NO_3Wire,
    NC_3Wire,
    FourWire_NO_NC
}

public enum MeterDisplayStyle
{
    Digital,
    Analog
}

public enum VoltmeterRange
{
    Range_50V,
    Range_300V,
    Range_500V
}

public enum AmmeterRange
{
    Direct_10A,
    Direct_30A,
    ViaCT_50_5,
    ViaCT_100_5,
    ViaCT_150_5,
    ViaCT_200_5
}

public enum CtRatio
{
    Ratio_50_5,
    Ratio_100_5,
    Ratio_150_5,
    Ratio_200_5
}

public enum Motor3PinConnection
{
    Star_380V,
    Delta_220V
}

public enum Motor6PinWindingType
{
    StarDelta,
    Dahlander2Speed
}

public enum Ah3TimeRange
{
    Range_10s,
    Range_30s,
    Range_60s,
    Range_10m
}

public enum EmergencyStopContactConfig
{
    Dual1NC1NO,
    Only1NC,
    Dual2NC
}

public enum KnifeSwitchKind
{
    SinglePhase2Pole,
    ThreePhase3Pole,
    Changeover3P_1_0_2
}

public enum CamSwitchMode
{
    Volt7Pos,
    Volt4Pos,
    Amp4Pos
}

public enum TrackInitialPosition
{
    LeftAtLS1,
    Center,
    RightAtLS2
}

public enum SensorWiringMode
{
    ThreeWire_PNP,
    ThreeWire_NPN,
    FourWire_NO_NC,
    TwoWire_AC,
    TwoWire_DC
}

public enum FloatSwitchMode
{
    WaterSupply,
    Drainage
}
