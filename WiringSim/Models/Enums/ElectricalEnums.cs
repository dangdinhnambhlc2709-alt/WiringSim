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
    Load,
    ProgrammableControl
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

public enum PlcCpuState
{
    Stop = 0,
    Run = 1,
    Downloading = 2,
    Error = 3
}

public enum LadderElementKind
{
    // Cấu trúc nhánh song song & khối trống
    ParallelBlock = 0,
    EmptyBox = 1,

    // Nhóm 1: Bit logic operations
    ContactNO = 10,
    ContactNC = 11,
    InvertRLO = 12,
    CoilOut = 13,
    CoilNegate = 14,
    CoilReset = 15,
    CoilSet = 16,
    SetBitField = 17,
    ResetBitField = 18,
    FlipFlopSR = 19,
    FlipFlopRS = 20,
    EdgePositive = 21,
    EdgeNegative = 22,
    CoilEdgePositive = 23,
    CoilEdgeNegative = 24,
    PTrig = 25,
    NTrig = 26,
    RTrig = 27,
    FTrig = 28,

    // Nhóm 2: Timer operations
    TimerTP = 30,
    TimerTON = 31,
    TimerTOF = 32,
    TimerTONR = 33,
    CoilTP = 34,
    CoilTON = 35,
    CoilTOF = 36,
    CoilTONR = 37,
    CoilRT = 38,
    CoilPT = 39,

    // Nhóm 3: Counter operations
    CounterCTU = 40,
    CounterCTD = 41,
    CounterCTUD = 42,

    // Nhóm 4: Comparator operations
    CmpEqual = 50,
    CmpNotEqual = 51,
    CmpGreaterOrEqual = 52,
    CmpLessOrEqual = 53,
    CmpGreater = 54,
    CmpLess = 55,
    InRange = 56,
    OutRange = 57,
    CheckOk = 58,
    CheckNotOk = 59,
    VarEqType = 60,
    VarNeType = 61,
    VarEqElemType = 62,
    VarNeElemType = 63,
    VarIsNull = 64,
    VarNotNull = 65,
    VarIsArray = 66,
    VarEqTypeOfDb = 67,
    VarNeTypeOfDb = 68,

    // Nhóm 5: Math functions
    Calculate = 70,
    MathAdd = 71,
    MathSub = 72,
    MathMul = 73,
    MathDiv = 74,
    MathMod = 75,
    MathNeg = 76,
    MathInc = 77,
    MathDec = 78,
    MathAbs = 79,
    MathMin = 80,
    MathMax = 81,
    MathLimit = 82,
    MathSqr = 83,
    MathSqrt = 84,
    MathLn = 85,
    MathExp = 86,
    MathExpt = 87,
    MathFrac = 88,
    MathSin = 89,
    MathCos = 90,
    MathTan = 91,
    MathAsin = 92,
    MathAcos = 93,
    MathAtan = 94,

    // Nhóm 6: Move operations
    Move = 100,
    MoveBlk = 101,
    MoveBlkVariant = 102,
    UMoveBlk = 103,
    FillBlk = 104,
    UFillBlk = 105,
    Scatter = 106,
    ScatterBlk = 107,
    Gather = 108,
    GatherBlk = 109,
    Swap = 110,
    Serialize = 111,
    Deserialize = 112,
    VariantGet = 113,
    VariantPut = 114,
    CountOfElements = 115,
    LowerBound = 116,
    UpperBound = 117,
    FieldRead = 118,
    FieldWrite = 119,

    // Nhóm 7: Conversion operations
    Convert = 130,
    Round = 131,
    Ceil = 132,
    Floor = 133,
    Trunc = 134,
    NormX = 135,
    ScaleX = 136,

    // Nhóm 8: Program control operations
    Jump = 140,
    JumpNot = 141,
    Label = 142,
    JumpList = 143,
    Switch = 144,
    Return = 145,
    EndisPw = 146,
    ReTrigr = 147,
    StopCpu = 148,
    GetError = 149,
    GetErrId = 150,
    RuntimeMeasure = 151,

    // Nhóm 9: Word logic operations
    WordAnd = 160,
    WordOr = 161,
    WordXor = 162,
    WordInvert = 163,
    WordDeco = 164,
    WordEnco = 165,
    WordSel = 166,
    WordMux = 167,
    WordDemux = 168,

    // Nhóm 10: Shift and rotate
    ShiftRight = 170,
    ShiftLeft = 171,
    RotateRight = 172,
    RotateLeft = 173,

    // Nhóm 11: Extended instructions (Date/Time RTC, String + Char, Block Call)
    ReadSysTime = 180,
    ReadLocTime = 181,
    TimeAdd = 182,
    TimeSub = 183,
    TimeDiff = 184,
    TimeConv = 185,
    StringConv = 186,
    StringLen = 187,
    StringConcat = 188,
    StringLeft = 189,
    StringRight = 190,
    StringMid = 191,
    StringDelete = 192,
    StringInsert = 193,
    StringReplace = 194,
    StringFind = 195,
    CallBlock = 196,

    // Nhóm 12: Technology instructions (PWM, HSC, PID)
    CtrlPwm = 200,
    CtrlHsc = 201,
    PidCompact = 202,

    // Nhóm 13: Communication instructions (Modbus RTU / TCP)
    ModbusCommLoad = 210,
    ModbusMaster = 211,
    ModbusTcpClient = 212
}

public enum PlcBlockKind
{
    OB1_Main = 0,
    OB100_Startup = 1,
    FC = 2,
    FB = 3
}

public enum PlcStopOutputMode
{
    ResetToZero = 0,
    FreezeLastValue = 1
}

public enum PlcDiagSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
