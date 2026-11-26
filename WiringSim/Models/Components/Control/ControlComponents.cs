using System.Globalization;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Components.Control;

/// <summary>
/// Giao diện chung cho Aptomat (MCB) có khả năng tự động nhảy OFF khi phát hiện ngắn mạch.
/// </summary>
public interface ICircuitBreaker
{
    bool IsOn { get; }
    bool IsTripped { get; }
    int RatedAmps { get; set; }
    McbCurveType CurveType { get; set; }
    void ToggleManual();
    void TripShortCircuit();
}

/// <summary>
/// Aptomat MCB 1 pha (chọn dòng định mức 6A..63A và đặc tuyến B, C, D).
/// </summary>
public sealed class Mcb1PhaseComponent : ElectricalComponent, ICircuitBreaker
{
    public static readonly int[] SupportedRatings = [6, 10, 16, 20, 32, 40, 63];

    public const string TypeId = "MCB_1P";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public bool IsOn { get; set; }
    public bool IsTripped { get; set; }
    public int RatedAmps { get; set; } = 16;
    public McbCurveType CurveType { get; set; } = McbCurveType.C;

    public Terminal In1 { get; }
    public Terminal Out2 { get; }
    public Terminal In3 { get; }
    public Terminal Out4 { get; }

    public Mcb1PhaseComponent() : base("MCB 1P", 80, 120)
    {
        In1 = AddTerminal("1", "1(L)", 20, 0, TerminalSide.Top, "Đầu vào cực 1 (Pha L)");
        In3 = AddTerminal("3", "3(N)", 60, 0, TerminalSide.Top, "Đầu vào cực 3 (Trung tính N)");
        Out2 = AddTerminal("2", "2(L)", 20, 120, TerminalSide.Bottom, "Đầu ra cực 2 (Pha L)");
        Out4 = AddTerminal("4", "4(N)", 60, 120, TerminalSide.Bottom, "Đầu ra cực 4 (Trung tính N)");
    }

    public void ToggleManual()
    {
        if (IsTripped)
        {
            IsTripped = false;
            IsOn = true;
        }
        else
        {
            IsOn = !IsOn;
        }
    }

    public void TripShortCircuit()
    {
        IsOn = false;
        IsTripped = true;
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsOn && !IsTripped)
        {
            yield return new InternalConductivePath(In1, Out2, $"{DisplayName} (1-2)");
            yield return new InternalConductivePath(In3, Out4, $"{DisplayName} (3-4)");
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsTripped = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["IsOn"] = IsOn.ToString(),
        ["RatedAmps"] = RatedAmps.ToString(CultureInfo.InvariantCulture),
        ["CurveType"] = CurveType.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("IsOn", out var v) && bool.TryParse(v, out bool on)) IsOn = on;
        if (props.TryGetValue("RatedAmps", out var aStr) && int.TryParse(aStr, out int amps)) RatedAmps = amps;
        if (props.TryGetValue("CurveType", out var cStr) && Enum.TryParse<McbCurveType>(cStr, true, out var curve)) CurveType = curve;
    }
}

/// <summary>
/// Aptomat MCB 3 pha (3 cực liên động: 1-2, 3-4, 5-6, chọn dòng định mức 6A..63A và đặc tuyến B, C, D).
/// </summary>
public sealed class Mcb3PhaseComponent : ElectricalComponent, ICircuitBreaker
{
    public const string TypeId = "MCB_3P";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public bool IsOn { get; set; }
    public bool IsTripped { get; set; }
    public int RatedAmps { get; set; } = 32;
    public McbCurveType CurveType { get; set; } = McbCurveType.C;

    public Terminal In1 { get; }
    public Terminal Out2 { get; }
    public Terminal In3 { get; }
    public Terminal Out4 { get; }
    public Terminal In5 { get; }
    public Terminal Out6 { get; }

    public Mcb3PhaseComponent() : base("MCB 3P", 120, 120)
    {
        In1 = AddTerminal("1", "1", 20, 0, TerminalSide.Top, "Đầu vào pha 1 (L1)");
        In3 = AddTerminal("3", "3", 60, 0, TerminalSide.Top, "Đầu vào pha 2 (L2)");
        In5 = AddTerminal("5", "5", 100, 0, TerminalSide.Top, "Đầu vào pha 3 (L3)");

        Out2 = AddTerminal("2", "2", 20, 120, TerminalSide.Bottom, "Đầu ra pha 1 (T1)");
        Out4 = AddTerminal("4", "4", 60, 120, TerminalSide.Bottom, "Đầu ra pha 2 (T2)");
        Out6 = AddTerminal("6", "6", 100, 120, TerminalSide.Bottom, "Đầu ra pha 3 (T3)");
    }

    public void ToggleManual()
    {
        if (IsTripped)
        {
            IsTripped = false;
            IsOn = true;
        }
        else
        {
            IsOn = !IsOn;
        }
    }

    public void TripShortCircuit()
    {
        IsOn = false;
        IsTripped = true;
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsOn && !IsTripped)
        {
            yield return new InternalConductivePath(In1, Out2, $"{DisplayName} (1-2)");
            yield return new InternalConductivePath(In3, Out4, $"{DisplayName} (3-4)");
            yield return new InternalConductivePath(In5, Out6, $"{DisplayName} (5-6)");
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsTripped = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["IsOn"] = IsOn.ToString(),
        ["RatedAmps"] = RatedAmps.ToString(CultureInfo.InvariantCulture),
        ["CurveType"] = CurveType.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("IsOn", out var v) && bool.TryParse(v, out bool on)) IsOn = on;
        if (props.TryGetValue("RatedAmps", out var aStr) && int.TryParse(aStr, out int amps)) RatedAmps = amps;
        if (props.TryGetValue("CurveType", out var cStr) && Enum.TryParse<McbCurveType>(cStr, true, out var curve)) CurveType = curve;
    }
}

/// <summary>
/// Khối Công tắc đa năng (chuẩn dân dụng &amp; công tắc xoay tủ điện):
/// - Công tắc 1 chiều (1-2)
/// - Công tắc 2 chiều / cầu thang (COM - L1 - L2)
/// - Công tắc xoay 3 vị trí (1 - 0 - 2 / Man - Off - Auto)
/// Tự động cập nhật sơ đồ chân cắm trên khối khi chuyển đổi loại trong Menu chuột phải.
/// </summary>
public class SwitchComponent : ElectricalComponent
{
    public const string TypeIdOneWay = "SWITCH_2P";
    public const string TypeIdTwoWay = "SWITCH_3WAY";
    public const string TypeIdRotary = "SWITCH_ROTARY_3POS";

    public SwitchKind SwitchKind { get; private set; }
    public override string TypeKey => SwitchKind switch
    {
        SwitchKind.TwoWay_Staircase => TypeIdTwoWay,
        SwitchKind.Rotary3Pos_1_0_2 => TypeIdRotary,
        _ => TypeIdOneWay
    };
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    // Trạng thái công tắc:
    // - OneWay_1_2: Position == 1 (ON), 0 (OFF)
    // - TwoWay_Staircase: Position == 1 (COM->L1), 2 (COM->L2)
    // - Rotary3Pos_1_0_2: Position == 1 (MAN: COM->1), 0 (OFF), 2 (AUTO: COM->2)
    public int Position { get; set; }

    public Terminal? ComOrInTerminal { get; private set; }
    public Terminal? Out1Terminal { get; private set; }
    public Terminal? Out2Terminal { get; private set; }

    // Tương thích ngược cho mã nguồn gọi trực tiếp Term1/Term2 của công tắc 1 chiều
    public Terminal Term1 => ComOrInTerminal!;
    public Terminal Term2 => Out1Terminal!;
    public bool IsOn
    {
        get => Position == 1;
        set => Position = value ? 1 : 0;
    }

    public SwitchComponent(SwitchKind kind = SwitchKind.OneWay_1_2)
        : base(GetDefaultTitle(kind), GetWidthForKind(kind), 100)
    {
        SwitchKind = kind;
        Position = kind == SwitchKind.TwoWay_Staircase ? 1 : 0;
        RebuildTerminals();
    }

    private static string GetDefaultTitle(SwitchKind kind) => kind switch
    {
        SwitchKind.TwoWay_Staircase => "Công tắc 2 chiều",
        SwitchKind.Rotary3Pos_1_0_2 => "Công tắc xoay 1-0-2",
        _ => "Công tắc 1 chiều"
    };

    private static double GetWidthForKind(SwitchKind kind) => kind switch
    {
        SwitchKind.TwoWay_Staircase => 100,
        SwitchKind.Rotary3Pos_1_0_2 => 120,
        _ => 80
    };

    public void SetSwitchKind(SwitchKind newKind)
    {
        if (SwitchKind == newKind) return;
        SwitchKind = newKind;
        Width = GetWidthForKind(newKind);
        Position = newKind == SwitchKind.TwoWay_Staircase ? 1 : 0;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        ComOrInTerminal = null;
        Out1Terminal = null;
        Out2Terminal = null;

        switch (SwitchKind)
        {
            case SwitchKind.OneWay_1_2:
                ComOrInTerminal = AddOrReuseTerminal(existing, "1", "1", 20, 100, TerminalSide.Bottom, "Chân vào 1");
                Out1Terminal = AddOrReuseTerminal(existing, "2", "2", 60, 100, TerminalSide.Bottom, "Chân ra 2");
                break;

            case SwitchKind.TwoWay_Staircase:
                ComOrInTerminal = AddOrReuseTerminal(existing, "COM", "COM", 60, 0, TerminalSide.Top, "Chân chung COM");
                Out1Terminal = AddOrReuseTerminal(existing, "1", "L1", 20, 100, TerminalSide.Bottom, "Nhánh cầu thang L1");
                Out2Terminal = AddOrReuseTerminal(existing, "2", "L2", 80, 100, TerminalSide.Bottom, "Nhánh cầu thang L2");
                break;

            case SwitchKind.Rotary3Pos_1_0_2:
                ComOrInTerminal = AddOrReuseTerminal(existing, "COM", "COM", 60, 0, TerminalSide.Top, "Chân nguồn chung COM");
                Out1Terminal = AddOrReuseTerminal(existing, "1", "1(MAN)", 20, 100, TerminalSide.Bottom, "Nhánh 1 — Chế độ Tay (MAN)");
                Out2Terminal = AddOrReuseTerminal(existing, "2", "2(AUTO)", 100, 100, TerminalSide.Bottom, "Nhánh 2 — Chế độ Tự động (AUTO)");
                break;
        }
    }

    public void ToggleOrStep()
    {
        switch (SwitchKind)
        {
            case SwitchKind.OneWay_1_2:
                Position = Position == 1 ? 0 : 1;
                break;
            case SwitchKind.TwoWay_Staircase:
                Position = Position == 1 ? 2 : 1;
                break;
            case SwitchKind.Rotary3Pos_1_0_2:
                // Xoay tuần hoàn 0(OFF) -> 1(MAN) -> 0(OFF) -> 2(AUTO)... hoặc 1 -> 0 -> 2 -> 0
                Position = Position switch
                {
                    0 => 1,
                    1 => 2,
                    _ => 0
                };
                break;
        }
    }

    public void SetRotaryPosition(int pos)
    {
        if (SwitchKind == SwitchKind.Rotary3Pos_1_0_2)
            Position = Math.Clamp(pos, 0, 2);
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (ComOrInTerminal is null) yield break;

        switch (SwitchKind)
        {
            case SwitchKind.OneWay_1_2:
                if (Position == 1 && Out1Terminal is not null)
                    yield return new InternalConductivePath(ComOrInTerminal, Out1Terminal, $"{DisplayName} (1-2)");
                break;

            case SwitchKind.TwoWay_Staircase:
                if (Position != 2 && Out1Terminal is not null)
                    yield return new InternalConductivePath(ComOrInTerminal, Out1Terminal, $"{DisplayName} (COM-L1)");
                else if (Position == 2 && Out2Terminal is not null)
                    yield return new InternalConductivePath(ComOrInTerminal, Out2Terminal, $"{DisplayName} (COM-L2)");
                break;

            case SwitchKind.Rotary3Pos_1_0_2:
                if (Position == 1 && Out1Terminal is not null)
                    yield return new InternalConductivePath(ComOrInTerminal, Out1Terminal, $"{DisplayName} (COM-1 MAN)");
                else if (Position == 2 && Out2Terminal is not null)
                    yield return new InternalConductivePath(ComOrInTerminal, Out2Terminal, $"{DisplayName} (COM-2 AUTO)");
                break;
        }
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["SwitchKind"] = SwitchKind.ToString(),
        ["Position"] = Position.ToString(CultureInfo.InvariantCulture)
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("SwitchKind", out var kStr) && Enum.TryParse<SwitchKind>(kStr, true, out var k))
            SetSwitchKind(k);

        if (props.TryGetValue("Position", out var pStr) && int.TryParse(pStr, out int p))
            Position = p;
        else if (props.TryGetValue("IsOn", out var onStr) && bool.TryParse(onStr, out bool on))
            Position = on ? 1 : 0;
        else if (props.TryGetValue("IsSwitchedTo2", out var s2Str) && bool.TryParse(s2Str, out bool s2))
            Position = s2 ? 2 : 1;
    }
}

/// <summary>Lớp kế thừa giữ tương thích ngược hoàn toàn cho mã Preset / JSON v1.0.</summary>
public sealed class Switch2PoleComponent : SwitchComponent
{
    public Switch2PoleComponent() : base(SwitchKind.OneWay_1_2) { }
}

/// <summary>Lớp kế thừa giữ tương thích ngược hoàn toàn cho mã JSON v1.0.</summary>
public sealed class Switch3WayComponent : SwitchComponent
{
    public Switch3WayComponent() : base(SwitchKind.TwoWay_Staircase) { }
}

/// <summary>
/// Nút nhấn công nghiệp chuẩn phi 22 (kiểu LA38 / XB2):
/// - Mặc định tích hợp sẵn 2 cặp tiếp điểm độc lập: NC (11 - 12) và NO (23 - 24) (tổng 4 chân).
/// - Kiểu tác động: Momentary (Không tự duy trì / Tự nhả) hoặc Latching (Tự duy trì / Có nhớ).
/// - Hỗ trợ đổi màu (Xanh lá, Đỏ, Vàng, Xanh dương) hoặc thu gọn về 1NO / 1NC.
/// </summary>
public sealed class PushButtonComponent : ElectricalComponent
{
    public const string TypeIdNO = "PUSHBUTTON_NO";
    public const string TypeIdNC = "PUSHBUTTON_NC";

    private readonly bool _preferNcPrimary;

    public PushButtonActionMode ActionMode { get; set; } = PushButtonActionMode.Momentary;
    public PushButtonContactConfig ContactConfig { get; private set; }
    public PilotLightColor ButtonColor { get; set; }

    public override string TypeKey => _preferNcPrimary ? TypeIdNC : TypeIdNO;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    /// <summary>Trạng thái nút đang lún xuống (do giữ chuột ở Momentary hoặc đã chốt ở Latching).</summary>
    public bool IsPressed { get; set; }

    public Terminal? NcIn1 { get; private set; }
    public Terminal? NcOut2 { get; private set; }
    public Terminal? NoIn3 { get; private set; }
    public Terminal? NoOut4 { get; private set; }

    // Thuộc tính tiện ích tương thích 100% với Preset (nút Đỏ ưu tiên cặp NC 11-12, nút Xanh ưu tiên cặp NO 23-24)
    public Terminal TopTerminal =>
        (_preferNcPrimary || ContactConfig == PushButtonContactConfig.Only1NC ? NcIn1 : NoIn3)
        ?? NcIn1 ?? NoIn3 ?? _terminals[0];
    public Terminal BottomTerminal =>
        (_preferNcPrimary || ContactConfig == PushButtonContactConfig.Only1NC ? NcOut2 : NoOut4)
        ?? NcOut2 ?? NoOut4 ?? _terminals[1];
    public bool IsNormallyClosed => _preferNcPrimary || ContactConfig == PushButtonContactConfig.Only1NC;

    public PushButtonComponent(bool isNormallyClosed = false)
        : base(isNormallyClosed ? "Nút nhấn Đỏ" : "Nút nhấn Xanh", 100, 100)
    {
        _preferNcPrimary = isNormallyClosed;
        ContactConfig = PushButtonContactConfig.Dual1NO1NC;
        ButtonColor = isNormallyClosed ? PilotLightColor.Red : PilotLightColor.Green;
        RebuildTerminals();
    }

    public void SetContactConfig(PushButtonContactConfig config)
    {
        if (ContactConfig == config) return;
        ContactConfig = config;
        Width = config == PushButtonContactConfig.Dual1NO1NC ? 100 : 80;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        NcIn1 = NcOut2 = NoIn3 = NoOut4 = null;

        switch (ContactConfig)
        {
            case PushButtonContactConfig.Only1NO:
                NoIn3 = AddOrReuseTerminal(existing, "23", "23(NO)", 40, 0, TerminalSide.Top, "Tiếp điểm thường mở 23 (NO)");
                NoOut4 = AddOrReuseTerminal(existing, "24", "24(NO)", 40, 100, TerminalSide.Bottom, "Tiếp điểm thường mở 24 (NO)");
                break;

            case PushButtonContactConfig.Only1NC:
                NcIn1 = AddOrReuseTerminal(existing, "11", "11(NC)", 40, 0, TerminalSide.Top, "Tiếp điểm thường đóng 11 (NC)");
                NcOut2 = AddOrReuseTerminal(existing, "12", "12(NC)", 40, 100, TerminalSide.Bottom, "Tiếp điểm thường đóng 12 (NC)");
                break;

            case PushButtonContactConfig.Dual1NO1NC:
                NcIn1 = AddOrReuseTerminal(existing, "11", "11(NC)", 20, 0, TerminalSide.Top, "Khối NC chân 11 (Thường đóng)");
                NoIn3 = AddOrReuseTerminal(existing, "23", "23(NO)", 80, 0, TerminalSide.Top, "Khối NO chân 23 (Thường mở)");
                NcOut2 = AddOrReuseTerminal(existing, "12", "12(NC)", 20, 100, TerminalSide.Bottom, "Khối NC chân 12 (Thường đóng)");
                NoOut4 = AddOrReuseTerminal(existing, "24", "24(NO)", 80, 100, TerminalSide.Bottom, "Khối NO chân 24 (Thường mở)");
                break;
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        // Tiếp điểm NC (11-12) đóng khi KHÔNG nhấn, mở khi nhấn
        if (!IsPressed && NcIn1 is not null && NcOut2 is not null)
        {
            yield return new InternalConductivePath(NcIn1, NcOut2, $"{DisplayName} (11-12 NC)");
        }

        // Tiếp điểm NO (23-24) đóng khi ĐANG nhấn
        if (IsPressed && NoIn3 is not null && NoOut4 is not null)
        {
            yield return new InternalConductivePath(NoIn3, NoOut4, $"{DisplayName} (23-24 NO)");
        }
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsPressed = false;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["ActionMode"] = ActionMode.ToString(),
        ["ContactConfig"] = ContactConfig.ToString(),
        ["ButtonColor"] = ButtonColor.ToString(),
        ["IsPressed"] = (ActionMode == PushButtonActionMode.Latching && IsPressed).ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ActionMode", out var mStr) && Enum.TryParse<PushButtonActionMode>(mStr, true, out var am))
            ActionMode = am;
        if (props.TryGetValue("ContactConfig", out var cStr) && Enum.TryParse<PushButtonContactConfig>(cStr, true, out var cc))
            SetContactConfig(cc);
        if (props.TryGetValue("ButtonColor", out var colStr) && Enum.TryParse<PilotLightColor>(colStr, true, out var col))
            ButtonColor = col;
        if (props.TryGetValue("IsPressed", out var pStr) && bool.TryParse(pStr, out bool p))
            IsPressed = p;
    }
}

/// <summary>
/// Contactor công nghiệp (Khởi động từ kiểu LS MC / Schneider LC1D):
/// - Điện áp cuộn hút: 24VDC, 24VAC, 110VAC, 220VAC, 380VAC (Kiểm tra đúng loại dòng AC/DC &amp; báo CHÁY CUỘN HÚT nếu quá áp!).
/// - Tiếp điểm phụ tích hợp: Chỉ 1NO (13-14), Chỉ 1NC (21-22), hoặc 1NO + 1NC (13-14 &amp; 21-22).
/// </summary>
public sealed class ContactorComponent : ElectricalComponent
{
    public const string TypeId = "CONTACTOR";
    public override string TypeKey => TypeId;
    public override ComponentCategory Category => ComponentCategory.SwitchingAndControl;

    public CoilVoltageRating CoilRating { get; set; } = CoilVoltageRating.AC_220V;
    public ContactorAuxConfig AuxConfig { get; private set; } = ContactorAuxConfig.Both1NO1NC;

    public bool IsCoilEnergized { get; private set; }
    public double AppliedCoilVoltage { get; private set; }
    public RatedSupplyStatus SupplyStatus { get; private set; }

    public Terminal L1In { get; private set; } = null!;
    public Terminal T1Out { get; private set; } = null!;
    public Terminal L2In { get; private set; } = null!;
    public Terminal T2Out { get; private set; } = null!;
    public Terminal L3In { get; private set; } = null!;
    public Terminal T3Out { get; private set; } = null!;

    public Terminal? No13 { get; private set; }
    public Terminal? No14 { get; private set; }
    public Terminal? Nc21 { get; private set; }
    public Terminal? Nc22 { get; private set; }

    public Terminal CoilA1 { get; private set; } = null!;
    public Terminal CoilA2 { get; private set; } = null!;

    public ContactorComponent() : base("KM", 240, 140)
    {
        RebuildTerminals();
    }

    public void SetAuxConfig(ContactorAuxConfig config)
    {
        if (AuxConfig == config) return;
        AuxConfig = config;
        Width = config == ContactorAuxConfig.Both1NO1NC ? 240 : 200;
        RebuildTerminals();
        NotifyTerminalsChanged();
    }

    private void RebuildTerminals()
    {
        var existing = SnapshotTerminalsForRebuild();
        No13 = No14 = Nc21 = Nc22 = null;

        L1In = AddOrReuseTerminal(existing, "1", "1", 20, 0, TerminalSide.Top, "Động lực vào 1 (L1)");
        L2In = AddOrReuseTerminal(existing, "3", "3", 60, 0, TerminalSide.Top, "Động lực vào 3 (L2)");
        L3In = AddOrReuseTerminal(existing, "5", "5", 100, 0, TerminalSide.Top, "Động lực vào 5 (L3)");

        T1Out = AddOrReuseTerminal(existing, "2", "2", 20, 140, TerminalSide.Bottom, "Động lực ra 2 (T1)");
        T2Out = AddOrReuseTerminal(existing, "4", "4", 60, 140, TerminalSide.Bottom, "Động lực ra 4 (T2)");
        T3Out = AddOrReuseTerminal(existing, "6", "6", 100, 140, TerminalSide.Bottom, "Động lực ra 6 (T3)");

        switch (AuxConfig)
        {
            case ContactorAuxConfig.Only1NO:
                No13 = AddOrReuseTerminal(existing, "13", "13(NO)", 140, 0, TerminalSide.Top, "Tiếp điểm phụ thường mở 13 (NO)");
                No14 = AddOrReuseTerminal(existing, "14", "14(NO)", 140, 140, TerminalSide.Bottom, "Tiếp điểm phụ thường mở 14 (NO)");
                CoilA1 = AddOrReuseTerminal(existing, "A1", "A1", 180, 0, TerminalSide.Top, "Cuộn hút A1");
                CoilA2 = AddOrReuseTerminal(existing, "A2", "A2", 180, 140, TerminalSide.Bottom, "Cuộn hút A2");
                break;

            case ContactorAuxConfig.Only1NC:
                Nc21 = AddOrReuseTerminal(existing, "21", "21(NC)", 140, 0, TerminalSide.Top, "Tiếp điểm phụ thường đóng 21 (NC)");
                Nc22 = AddOrReuseTerminal(existing, "22", "22(NC)", 140, 140, TerminalSide.Bottom, "Tiếp điểm phụ thường đóng 22 (NC)");
                CoilA1 = AddOrReuseTerminal(existing, "A1", "A1", 180, 0, TerminalSide.Top, "Cuộn hút A1");
                CoilA2 = AddOrReuseTerminal(existing, "A2", "A2", 180, 140, TerminalSide.Bottom, "Cuộn hút A2");
                break;

            default: // Both1NO1NC
                No13 = AddOrReuseTerminal(existing, "13", "13(NO)", 140, 0, TerminalSide.Top, "Tiếp điểm phụ thường mở 13 (NO)");
                No14 = AddOrReuseTerminal(existing, "14", "14(NO)", 140, 140, TerminalSide.Bottom, "Tiếp điểm phụ thường mở 14 (NO)");
                Nc21 = AddOrReuseTerminal(existing, "21", "21(NC)", 180, 0, TerminalSide.Top, "Tiếp điểm phụ thường đóng 21 (NC)");
                Nc22 = AddOrReuseTerminal(existing, "22", "22(NC)", 180, 140, TerminalSide.Bottom, "Tiếp điểm phụ thường đóng 22 (NC)");
                CoilA1 = AddOrReuseTerminal(existing, "A1", "A1", 220, 0, TerminalSide.Top, "Cuộn hút A1");
                CoilA2 = AddOrReuseTerminal(existing, "A2", "A2", 220, 140, TerminalSide.Bottom, "Cuộn hút A2");
                break;
        }
    }

    public override IEnumerable<InternalConductivePath> GetInternalConductivePaths()
    {
        if (IsCoilEnergized && !IsBurnedOut)
        {
            yield return new InternalConductivePath(L1In, T1Out, $"{DisplayName} (1-2)");
            yield return new InternalConductivePath(L2In, T2Out, $"{DisplayName} (3-4)");
            yield return new InternalConductivePath(L3In, T3Out, $"{DisplayName} (5-6)");
            if (No13 is not null && No14 is not null)
                yield return new InternalConductivePath(No13, No14, $"{DisplayName} (13-14 NO)");
        }
        else
        {
            if (Nc21 is not null && Nc22 is not null)
                yield return new InternalConductivePath(Nc21, Nc22, $"{DisplayName} (21-22 NC)");
        }
    }

    public override IEnumerable<InternalLoadPath> GetActiveLoadPaths()
    {
        var (kind, _, _) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
        yield return new InternalLoadPath(CoilA1, CoilA2, $"{DisplayName} Coil", IsCoilEnergized && !IsBurnedOut, 0.08, kind);
    }

    public override void EvaluateSimulationStep(double deltaSeconds, IReadOnlyList<WireModel> allWires)
    {
        if (IsBurnedOut)
        {
            IsCoilEnergized = false;
            SupplyStatus = RatedSupplyStatus.OvervoltageBurnout;
            return;
        }

        SupplyStatus = ElectricalAnalysisHelper.EvaluateRatedSupply(CoilA1, CoilA2, CoilRating, out double v, out var appliedKind);
        AppliedCoilVoltage = v;

        if (SupplyStatus == RatedSupplyStatus.OvervoltageBurnout)
        {
            var (_, _, ratingLabel) = ElectricalAnalysisHelper.GetRatingSpec(CoilRating);
            IsBurnedOut = true;
            IsCoilEnergized = false;
            BurnoutReason = WiringSim.UI.Services.LocalizationManager.Format("Str.Fault.BurnoutCoilMsg", DisplayName, v, appliedKind, ratingLabel);
            return;
        }

        IsCoilEnergized = SupplyStatus == RatedSupplyStatus.Normal;
    }

    public override void ResetSimulationState()
    {
        base.ResetSimulationState();
        IsCoilEnergized = false;
        AppliedCoilVoltage = 0;
        SupplyStatus = RatedSupplyStatus.Unpowered;
    }

    public override Dictionary<string, string> ExportProperties() => new()
    {
        ["CoilRating"] = CoilRating.ToString(),
        ["AuxConfig"] = AuxConfig.ToString()
    };

    public override void ImportProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("CoilRating", out var rStr) && Enum.TryParse<CoilVoltageRating>(rStr, true, out var r))
            CoilRating = r;
        if (props.TryGetValue("AuxConfig", out var aStr) && Enum.TryParse<ContactorAuxConfig>(aStr, true, out var a))
            SetAuxConfig(a);
    }
}
