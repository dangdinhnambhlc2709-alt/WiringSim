using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Plc;

/// <summary>
/// Ánh xạ ký hiệu biến PLC (PLC Tag Table chuẩn IEC 61131-3), ví dụ %I0.0 -> "START", %Q0.0 -> "LAMP1", %MW2 -> "SEQUENCE".
/// </summary>
public sealed class PlcTagEntry
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Bool";
    public bool Retain { get; set; }
    public bool Accessible { get; set; } = true;
    public bool Writable { get; set; } = true;
    public bool Visible { get; set; } = true;
    public string Comment { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsSystemGenerated { get; set; }

    public PlcTagEntry Clone() => new()
    {
        Name = Name,
        Address = Address,
        DataType = DataType,
        Retain = Retain,
        Accessible = Accessible,
        Writable = Writable,
        Visible = Visible,
        Comment = Comment,
        IsSystemGenerated = IsSystemGenerated
    };
}

/// <summary>
/// Mô tả một thuộc tính nội bộ bên trong khối dữ liệu DB (Timer: IN/PT/Q/ET, Counter: CU/CD/R/LD/PV/QU/QD/CV).
/// </summary>
public sealed record PlcDbMemberDescriptor(string Name, string DataType, string Description);

/// <summary>
/// Công cụ chuẩn hóa, phân tích, chọn và tự động tăng địa chỉ PLC chuẩn IEC 61131-3.
/// </summary>
public static partial class PlcAddressHelper
{
    public static readonly string[] SupportedDataTypes =
    [
        "Bool", "Byte", "Word", "DWord", "SInt", "USInt", "Int", "UInt", "DInt", "UDInt", "Real", "LReal", "Time", "Char", "String", "DTL"
    ];

    public static readonly string[] OperandIdentifiers = ["I", "Q", "M"];
    public static readonly string[] OperandTypes = ["Bit", "Byte", "Word", "DWord"];

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<byte>\d+)\.(?<bit>[0-7])(?<imm>:P)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BitPattern();

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<size>B|W|D)(?<offset>\d+)(?<imm>:P)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SizedPattern();

    [GeneratedRegex(@"^%?DB(?<num>\d+)(?:\.(?<field>[A-Z0-9_.]+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DbPattern();

    public static bool IsAbsoluteAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim();
        return BitPattern().IsMatch(s) || SizedPattern().IsMatch(s) || DbPattern().IsMatch(s);
    }

    /// <summary>
    /// Chuẩn hóa địa chỉ tuyệt đối (%I0.0, %Q0.0:P, %MB0, %MW2, %MD4, %DB1, %DB1.Q, %DB1.DBW0).
    /// Giữ nguyên biểu thức ký hiệu như "T1".Q, "Tag".x0 hoặc &lt;???&gt; mà không chèn nhầm tiền tố %.
    /// </summary>
    public static string NormalizeAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (s == "<???>") return s;

        if (OperandResolver.TryResolveAbsolute(s, out var abs) && abs.IsValid)
            return abs.NormalizedText;

        if (BitPattern().IsMatch(s) || SizedPattern().IsMatch(s) || DbPattern().IsMatch(s))
        {
            string upper = s.ToUpperInvariant();
            return upper.StartsWith('%') ? upper : "%" + upper;
        }

        return s;
    }

    public static string StripQuotes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1].Trim();
        return s;
    }

    /// <summary>
    /// Tự động tính địa chỉ liền kề tiếp theo khi nhấn Enter trong bảng PLC Tags:
    /// - Bool: %I0.0 -> %I0.1 ... %I0.7 -> %I1.0
    /// - Byte / SInt / USInt: %MB0 -> %MB1 (+1)
    /// - Int / UInt / Word: %MW0 -> %MW2 (+2)
    /// - DInt / UDInt / DWord / Real / Time: %MD0 -> %MD4 (+4)
    /// </summary>
    public static string ComputeNextAddress(string? currentAddress, string? dataType)
    {
        string dt = string.IsNullOrWhiteSpace(dataType) ? "Bool" : dataType.Trim();
        string norm = NormalizeAddress(currentAddress);

        string area = "I";
        int byteOffset = 0;
        int bitIdx = 0;
        bool parsedBit = false;
        bool parsedSized = false;

        var bm = BitPattern().Match(norm);
        if (bm.Success)
        {
            area = bm.Groups["area"].Value.ToUpperInvariant();
            byteOffset = int.Parse(bm.Groups["byte"].Value, CultureInfo.InvariantCulture);
            bitIdx = int.Parse(bm.Groups["bit"].Value, CultureInfo.InvariantCulture);
            parsedBit = true;
        }
        else
        {
            var sm = SizedPattern().Match(norm);
            if (sm.Success)
            {
                area = sm.Groups["area"].Value.ToUpperInvariant();
                byteOffset = int.Parse(sm.Groups["offset"].Value, CultureInfo.InvariantCulture);
                parsedSized = true;
            }
        }

        if (dt.Equals("Bool", StringComparison.OrdinalIgnoreCase))
        {
            if (parsedBit)
            {
                bitIdx++;
                if (bitIdx > 7)
                {
                    bitIdx = 0;
                    byteOffset++;
                }
                return $"%{area}{byteOffset}.{bitIdx}";
            }
            if (parsedSized)
                return $"%{area}{byteOffset + 1}.0";
            return "%I0.0";
        }

        if (dt.Equals("Byte", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("SInt", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("USInt", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("Char", StringComparison.OrdinalIgnoreCase))
        {
            if (!parsedBit && !parsedSized) return "%MB2";
            return $"%{area}B{byteOffset + 1}";
        }

        if (dt.Equals("Int", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("UInt", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("Word", StringComparison.OrdinalIgnoreCase))
        {
            if (!parsedBit && !parsedSized) return "%MW2";
            return $"%{area}W{byteOffset + 2}";
        }

        if (dt.Equals("LReal", StringComparison.OrdinalIgnoreCase))
        {
            if (!parsedBit && !parsedSized) return "%MD8";
            return $"%{area}D{byteOffset + 8}";
        }

        // DInt, UDInt, DWord, Real, Time, String, DTL -> bước nhảy +4 byte
        if (!parsedBit && !parsedSized) return "%MD4";
        return $"%{area}D{byteOffset + 4}";
    }

    public static string BuildAddress(string operandIdentifier, string operandType, int byteAddress, int bitNumber)
    {
        string id = string.IsNullOrWhiteSpace(operandIdentifier) ? "M" : operandIdentifier.Trim().ToUpperInvariant();
        if (id is not ("I" or "Q" or "M")) id = "M";

        string type = (operandType ?? "Bit").Trim();
        int safeBit = Math.Clamp(bitNumber, 0, 7);

        if (id == "I")
        {
            return type switch
            {
                "Byte" => $"%IB{Math.Clamp(byteAddress, 0, 1)}",
                "Word" => byteAddress switch
                {
                    >= 66 => "%IW66",
                    >= 32 => "%IW64",
                    _ => "%IW0"
                },
                "DWord" => $"%ID{Math.Clamp(byteAddress, 0, 68)}",
                _ => BuildBoundedBit("I", Math.Clamp(byteAddress, 0, 1), safeBit, maxByte1Bit: 5)
            };
        }

        if (id == "Q")
        {
            return type switch
            {
                "Byte" => $"%QB{Math.Clamp(byteAddress, 0, 1)}",
                "Word" => $"%QW{Math.Clamp(byteAddress, 0, 18)}",
                "DWord" => $"%QD{Math.Clamp(byteAddress, 0, 16)}",
                _ => BuildBoundedBit("Q", Math.Clamp(byteAddress, 0, 1), safeBit, maxByte1Bit: 1)
            };
        }

        return type switch
        {
            "Byte" => $"%MB{Math.Clamp(byteAddress, 0, OperandResolver.MaxMemoryBytes - 1)}",
            "Word" => $"%MW{Math.Clamp(byteAddress, 0, OperandResolver.MaxMemoryBytes - 2)}",
            "DWord" => $"%MD{Math.Clamp(byteAddress, 0, OperandResolver.MaxMemoryBytes - 4)}",
            _ => $"%M{Math.Clamp(byteAddress, 0, OperandResolver.MaxMemoryBytes - 1)}.{safeBit}"
        };
    }

    private static string BuildBoundedBit(string area, int byteAddr, int bitNum, int maxByte1Bit)
    {
        if (byteAddr == 1) bitNum = Math.Clamp(bitNum, 0, maxByte1Bit);
        return $"%{area}{byteAddr}.{bitNum}";
    }

    public static bool TryParseAddressParts(
        string? raw,
        out string operandIdentifier,
        out string operandType,
        out int byteAddress,
        out int bitNumber)
    {
        operandIdentifier = "I";
        operandType = "Bit";
        byteAddress = 0;
        bitNumber = 0;

        string norm = NormalizeAddress(raw);
        var bm = BitPattern().Match(norm);
        if (bm.Success)
        {
            operandIdentifier = bm.Groups["area"].Value.ToUpperInvariant();
            operandType = "Bit";
            byteAddress = int.Parse(bm.Groups["byte"].Value, CultureInfo.InvariantCulture);
            bitNumber = int.Parse(bm.Groups["bit"].Value, CultureInfo.InvariantCulture);
            return true;
        }

        var sm = SizedPattern().Match(norm);
        if (sm.Success)
        {
            operandIdentifier = sm.Groups["area"].Value.ToUpperInvariant();
            string size = sm.Groups["size"].Value.ToUpperInvariant();
            operandType = size switch
            {
                "B" => "Byte",
                "W" => "Word",
                "D" => "DWord",
                _ => "Bit"
            };
            byteAddress = int.Parse(sm.Groups["offset"].Value, CultureInfo.InvariantCulture);
            bitNumber = 0;
            return true;
        }

        return false;
    }

    public static string DefaultDataTypeForOperandType(string operandType) => (operandType ?? "Bit") switch
    {
        "Byte" => "Byte",
        "Word" => "Int",
        "DWord" => "DInt",
        _ => "Bool"
    };

    public static bool IsDataTypeCompatibleWithOperandType(string? dataType, string operandType)
    {
        string dt = (dataType ?? string.Empty).Trim();
        return operandType switch
        {
            "Bit" => dt.Equals("Bool", StringComparison.OrdinalIgnoreCase),
            "Byte" => dt.Equals("Byte", StringComparison.OrdinalIgnoreCase) ||
                      dt.Equals("SInt", StringComparison.OrdinalIgnoreCase) ||
                      dt.Equals("USInt", StringComparison.OrdinalIgnoreCase) ||
                      dt.Equals("Char", StringComparison.OrdinalIgnoreCase),
            "Word" => dt.Equals("Int", StringComparison.OrdinalIgnoreCase) ||
                      dt.Equals("UInt", StringComparison.OrdinalIgnoreCase) ||
                      dt.Equals("Word", StringComparison.OrdinalIgnoreCase),
            "DWord" => dt.Equals("DInt", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("UDInt", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("DWord", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("Real", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("LReal", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("Time", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("String", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("DTL", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    public static IReadOnlyList<PlcDbMemberDescriptor> GetDbMembers(string? dbDataType)
    {
        string dt = dbDataType ?? string.Empty;
        if (dt.Contains("Counter", StringComparison.OrdinalIgnoreCase) ||
            dt.Contains("CTU", StringComparison.OrdinalIgnoreCase) ||
            dt.Contains("CTD", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("CU", "Bool", "Count up input"),
                new PlcDbMemberDescriptor("CD", "Bool", "Count down input"),
                new PlcDbMemberDescriptor("R", "Bool", "Reset counter"),
                new PlcDbMemberDescriptor("LD", "Bool", "Load preset value"),
                new PlcDbMemberDescriptor("PV", "Int", "Preset value"),
                new PlcDbMemberDescriptor("QU", "Bool", "Count up output Q"),
                new PlcDbMemberDescriptor("QD", "Bool", "Count down output Q"),
                new PlcDbMemberDescriptor("CV", "Int", "Current counter value")
            ];
        }

        if (dt.Contains("Trig", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("CLK", "Bool", "Clock trigger input"),
                new PlcDbMemberDescriptor("Q", "Bool", "Edge detection output pulse")
            ];
        }

        if (dt.Contains("PID", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("Setpoint", "Real", "PID target setpoint"),
                new PlcDbMemberDescriptor("Input", "Real", "PID process feedback"),
                new PlcDbMemberDescriptor("Output", "Real", "PID output (0..100%)"),
                new PlcDbMemberDescriptor("OutputPer", "Int", "PID analog output (0..27648)"),
                new PlcDbMemberDescriptor("State", "Int", "Operating mode (0=Off, 3=Auto)"),
                new PlcDbMemberDescriptor("ErrorBits", "DWord", "PID diagnostic flags")
            ];
        }

        if (dt.Contains("Modbus", StringComparison.OrdinalIgnoreCase) ||
            dt.Contains("MB_CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("Done", "Bool", "Transaction completed without error"),
                new PlcDbMemberDescriptor("Busy", "Bool", "Transaction in progress"),
                new PlcDbMemberDescriptor("Error", "Bool", "Transaction error flag"),
                new PlcDbMemberDescriptor("Status", "Word", "Status / Error code (16#xxxx)")
            ];
        }

        if (dt.Contains("PWM", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("Enable", "Bool", "PWM pulse generation enabled"),
                new PlcDbMemberDescriptor("PulseWidth", "Word", "Pulse width (0..1000 permille)"),
                new PlcDbMemberDescriptor("Status", "Word", "PWM status code")
            ];
        }

        if (dt.Contains("HSC", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new PlcDbMemberDescriptor("CV", "DInt", "High-speed counter current value"),
                new PlcDbMemberDescriptor("RV", "DInt", "Reference comparison value"),
                new PlcDbMemberDescriptor("Status", "Word", "HSC status code")
            ];
        }

        return
        [
            new PlcDbMemberDescriptor("IN", "Bool", "Timer start input"),
            new PlcDbMemberDescriptor("PT", "Time", "Preset duration"),
            new PlcDbMemberDescriptor("Q", "Bool", "Timer output Q"),
            new PlcDbMemberDescriptor("ET", "Time", "Elapsed time")
        ];
    }

    public static IReadOnlyList<PlcDbMemberDescriptor> GetSliceMembers(string? dataType)
    {
        if (!PlcDataTypeHelper.TryParse(dataType, out var dt) || !PlcDataTypeHelper.CanSlice(dt))
            return [];

        int bytes = PlcDataTypeHelper.ByteSize(dt);
        var list = new List<PlcDbMemberDescriptor>();
        if (bytes >= 4)
        {
            list.Add(new PlcDbMemberDescriptor("w0", "Word", "Low word (bits 0..15)"));
            list.Add(new PlcDbMemberDescriptor("w1", "Word", "High word (bits 16..31)"));
        }
        else if (bytes == 2)
        {
            list.Add(new PlcDbMemberDescriptor("w0", "Word", "Word (bits 0..15)"));
        }

        for (int b = 0; b < bytes; b++)
            list.Add(new PlcDbMemberDescriptor($"b{b}", "Byte", $"Byte {b}"));

        int bits = bytes * 8;
        for (int x = 0; x < bits; x++)
            list.Add(new PlcDbMemberDescriptor($"x{x}", "Bool", $"Bit {x}"));

        return list;
    }
}

/// <summary>
/// Một nhánh lệnh nối tiếp trong mạng Ladder (có thể nằm trong Network chính hoặc trong một ParallelBlock).
/// </summary>
public sealed class LadderBranch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public List<LadderNode> Nodes { get; set; } = new();

    public LadderBranch Clone(bool regenerateIds = false) => new()
    {
        Id = regenerateIds ? Guid.NewGuid().ToString("N")[..8] : Id,
        Nodes = Nodes.Select(n => n.Clone(regenerateIds)).ToList()
    };
}

/// <summary>
/// Một phần tử trong ngôn ngữ lập trình Ladder (Tiếp điểm, Cuộn hút, Khối Timer/Counter DB, So sánh, Toán học/Move, hoặc Khối rẽ nhánh song song).
/// </summary>
public sealed class LadderNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public LadderElementKind Kind { get; set; } = LadderElementKind.ContactNO;

    /// <summary>Địa chỉ chính: %I0.0, %Q0.0, %M0.0, %DB1, "T1".Q, "Tag".x0, hoặc &lt;???&gt;...</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Tên ký hiệu biến (nếu để trống sẽ tự tra cứu trong PLC Tag Table theo Address).</summary>
    public string SymbolName { get; set; } = string.Empty;

    /// <summary>Kiểu dữ liệu chọn cho khối hàm (Int, DInt, Real, Time, Word, DWord...).</summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>Tham số 1: PT (T#3s) cho Timer | PV (5) cho Counter | IN1 cho So sánh / Toán học / MOVE | Bit nhớ sườn cho P/N.</summary>
    public string Param1 { get; set; } = string.Empty;

    /// <summary>Tham số 2: IN2 cho So sánh / Toán học | Địa chỉ Reset R cho TONR / CTU / CTUD | Địa chỉ Load LD cho CTD.</summary>
    public string Param2 { get; set; } = string.Empty;

    /// <summary>Các chân đầu vào mở rộng (IN3, IN4, ... cho ADD, MUL, AND, OR, XOR, MUX, MIN, MAX, CALCULATE).</summary>
    public List<string>? ExtraInputs { get; set; }

    /// <summary>Các chân đầu ra mở rộng (OUT2, OUT3, ... cho MOVE).</summary>
    public List<string>? ExtraOutputs { get; set; }

    /// <summary>Địa chỉ đầu ra: OUT (%MW2, %QW0) cho MOVE / ADD / SUB / MUL / DIV | Địa chỉ đếm xuống CD cho CTUD.</summary>
    public string OutputAddress { get; set; } = string.Empty;

    /// <summary>Tham số mở rộng (VD: MAX cho IN_Range/OUT_Range/LIMIT/NORM_X/SCALE_X, biểu thức CALCULATE, LD cho CTUD...).</summary>
    public string ExtraParam { get; set; } = string.Empty;

    /// <summary>Danh sách nhánh song song (chỉ dùng khi Kind == LadderElementKind.ParallelBlock).</summary>
    public List<LadderBranch>? Branches { get; set; }

    [JsonIgnore]
    public bool IsOutputOrAction => PlcInstructionCatalog.IsOutputOrAction(Kind);

    [JsonIgnore]
    public bool IsTimer => Kind is
        LadderElementKind.TimerTP or LadderElementKind.TimerTON or LadderElementKind.TimerTOF or LadderElementKind.TimerTONR;

    [JsonIgnore]
    public bool IsTimerCoil => Kind is
        LadderElementKind.CoilTP or LadderElementKind.CoilTON or LadderElementKind.CoilTOF or LadderElementKind.CoilTONR or LadderElementKind.CoilRT or LadderElementKind.CoilPT;

    /// <summary>True chỉ cho các lệnh tạo bản thể Timer DB riêng (loại trừ CoilRT/CoilPT chỉ tham chiếu DB có sẵn).</summary>
    [JsonIgnore]
    public bool IsTimerInstance => IsTimer || Kind is
        LadderElementKind.CoilTP or LadderElementKind.CoilTON or LadderElementKind.CoilTOF or LadderElementKind.CoilTONR;

    [JsonIgnore]
    public bool IsCounter => Kind is
        LadderElementKind.CounterCTU or LadderElementKind.CounterCTD or LadderElementKind.CounterCTUD;

    [JsonIgnore]
    public bool RequiresCallOptions => IsTimerInstance || IsCounter || Kind is
        LadderElementKind.RTrig or LadderElementKind.FTrig or
        LadderElementKind.CtrlPwm or LadderElementKind.CtrlHsc or LadderElementKind.PidCompact or
        LadderElementKind.ModbusCommLoad or LadderElementKind.ModbusMaster or LadderElementKind.ModbusTcpClient;

    [JsonIgnore]
    public bool IsComparator => PlcInstructionCatalog.IsComparator(Kind);

    [JsonIgnore]
    public bool IsContactOrCoilGlyph => PlcInstructionCatalog.IsInlineGlyph(Kind);

    public LadderNode Clone(bool regenerateIds = false) => new()
    {
        Id = regenerateIds ? Guid.NewGuid().ToString("N")[..8] : Id,
        Kind = Kind,
        Address = Address,
        SymbolName = SymbolName,
        DataType = DataType,
        Param1 = Param1,
        Param2 = Param2,
        ExtraInputs = ExtraInputs is not null ? new List<string>(ExtraInputs) : null,
        ExtraOutputs = ExtraOutputs is not null ? new List<string>(ExtraOutputs) : null,
        OutputAddress = OutputAddress,
        ExtraParam = ExtraParam,
        Branches = Branches?.Select(b => b.Clone(regenerateIds)).ToList()
    };
}

/// <summary>
/// Một Network (Mạng lệnh) trong chương trình OB1 chuẩn IEC 61131-3.
/// </summary>
public sealed class LadderNetwork
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Network";
    public string Comment { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public LadderBranch MainBranch { get; set; } = new();

    public LadderNetwork Clone(bool regenerateIds = false) => new()
    {
        Id = regenerateIds ? Guid.NewGuid().ToString("N")[..8] : Id,
        Title = Title,
        Comment = Comment,
        IsCollapsed = IsCollapsed,
        MainBranch = MainBranch.Clone(regenerateIds)
    };
}

/// <summary>
/// Toàn bộ chương trình PLC (Danh sách Networks, Bảng Tag ký hiệu biến, Cấu hình System/Clock Memory, Watch Table, Favorites).
/// </summary>
public sealed class PlcProgramModel
{
    public List<PlcTagEntry> Tags { get; set; } = new();
    public List<LadderNetwork> Networks { get; set; } = new();

    /// <summary>Các khối chương trình bổ sung (OB100 Startup, FC1..FCn, FB1..FBn); OB1 chính nằm trong Networks.</summary>
    public List<PlcCodeBlock> AdditionalBlocks { get; set; } = new();

    // Cấu hình Vùng nhớ hệ thống, Xung nhịp vuông, Watchdog & Hành vi ngõ ra khi STOP
    public bool EnableSystemMemory { get; set; } = true;
    public int SystemMemoryByte { get; set; } = 1; // Mặc định MB1 (%M1.0..%M1.3)
    public bool EnableClockMemory { get; set; } = true;
    public int ClockMemoryByte { get; set; } = 0;  // Mặc định MB0 (%M0.0..%M0.7)
    public int MaxCycleTimeMs { get; set; } = 150; // Watchdog mặc định 150ms (1..6000ms)
    public PlcStopOutputMode StopOutputMode { get; set; } = PlcStopOutputMode.ResetToZero;

    // Watch Table & Force Table tách biệt
    public List<PlcWatchEntry> WatchEntries { get; set; } = new();
    public List<PlcForceEntry> ForceEntries { get; set; } = new();

    // Danh sách lệnh ghim trên thanh Favorites (nếu null dùng bộ mặc định)
    public List<LadderElementKind>? FavoriteInstructions { get; set; }

    public IEnumerable<LadderNetwork> EnumerateAllNetworks()
    {
        foreach (var net in Networks) yield return net;
        foreach (var blk in AdditionalBlocks)
            foreach (var net in blk.Networks) yield return net;
    }

    public PlcCodeBlock? FindBlockByRef(string? blockRef)
    {
        if (string.IsNullOrWhiteSpace(blockRef)) return null;
        string clean = PlcAddressHelper.StripQuotes(blockRef).Trim();
        return AdditionalBlocks.FirstOrDefault(b =>
            b.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
            (b.Kind == PlcBlockKind.FC && $"FC{b.Number}".Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (b.Kind == PlcBlockKind.FB && $"FB{b.Number}".Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (b.Kind == PlcBlockKind.OB100_Startup && "OB100".Equals(clean, StringComparison.OrdinalIgnoreCase)));
    }

    public static readonly (int Bit, string Name, string Desc)[] SystemMemoryDefs =
    [
        (0, "FirstScan", "First scan cycle bit (1 on 1st scan after STOP→RUN)"),
        (1, "DiagStatusUpdate", "Diagnostics status changed indicator"),
        (2, "AlwaysTRUE", "Always 1 (HIGH)"),
        (3, "AlwaysFALSE", "Always 0 (LOW)")
    ];

    public static readonly (int Bit, string Name, double FrequencyHz)[] ClockMemoryDefs =
    [
        (0, "Clock_10Hz", 10.0),
        (1, "Clock_5Hz", 5.0),
        (2, "Clock_2.5Hz", 2.5),
        (3, "Clock_2Hz", 2.0),
        (4, "Clock_1.25Hz", 1.25),
        (5, "Clock_1Hz", 1.0),
        (6, "Clock_0.625Hz", 0.625),
        (7, "Clock_0.5Hz", 0.5)
    ];

    public List<PlcTagEntry> GetSystemAndClockTags()
    {
        var list = new List<PlcTagEntry>();
        if (EnableSystemMemory)
        {
            int sb = Math.Clamp(SystemMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
            foreach (var (bit, name, desc) in SystemMemoryDefs)
            {
                list.Add(new PlcTagEntry
                {
                    Name = name,
                    Address = $"%M{sb}.{bit}",
                    DataType = "Bool",
                    Writable = false,
                    Comment = desc,
                    IsSystemGenerated = true
                });
            }
        }

        if (EnableClockMemory)
        {
            int cb = Math.Clamp(ClockMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
            foreach (var (bit, name, freq) in ClockMemoryDefs)
            {
                list.Add(new PlcTagEntry
                {
                    Name = name,
                    Address = $"%M{cb}.{bit}",
                    DataType = "Bool",
                    Writable = false,
                    Comment = $"Square wave {freq.ToString(CultureInfo.InvariantCulture)} Hz",
                    IsSystemGenerated = true
                });
            }
        }
        return list;
    }

    public string ResolveTagName(string address, string explicitSymbol = "")
    {
        if (!string.IsNullOrWhiteSpace(explicitSymbol))
            return PlcAddressHelper.StripQuotes(explicitSymbol);
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        string norm = NormalizeAddress(address);
        // Bỏ hậu tố :P khi tra tên tag
        string lookupAddr = norm.EndsWith(":P", StringComparison.OrdinalIgnoreCase) ? norm[..^2] : norm;

        var tag = Tags.FirstOrDefault(t => NormalizeAddress(t.Address).Equals(lookupAddr, StringComparison.OrdinalIgnoreCase));
        if (tag is not null && !string.IsNullOrWhiteSpace(tag.Name))
            return tag.Name;

        var sysTag = GetSystemAndClockTags().FirstOrDefault(t => NormalizeAddress(t.Address).Equals(lookupAddr, StringComparison.OrdinalIgnoreCase));
        if (sysTag is not null)
            return sysTag.Name;

        // Hỗ trợ ngược %DB2.Q -> tra tên DB2 ("T1")
        if (lookupAddr.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && lookupAddr.Contains('.'))
        {
            int dot = lookupAddr.IndexOf('.');
            string dbAddr = lookupAddr[..dot];
            string field = lookupAddr[(dot + 1)..];
            var dbEntry = GetAllDataBlocks().FirstOrDefault(d => NormalizeAddress(d.Address).Equals(dbAddr, StringComparison.OrdinalIgnoreCase));
            if (dbEntry is not null && !string.IsNullOrWhiteSpace(dbEntry.Name))
                return $"{dbEntry.Name}.{field}";
        }

        return string.Empty;
    }

    /// <summary>
    /// Quy đổi địa chỉ ký hiệu hoặc truy xuất thành phần DB / Slice (VD: "T1".Q, "FirstScan", "STATUS".x0)
    /// thành địa chỉ tuyệt đối chuẩn hóa cho Compiler và Runtime Engine.
    /// </summary>
    public string ResolveAddressForRuntime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (s == "<???>") return s;

        var resolved = OperandResolver.Resolve(s, this);
        if (resolved.IsValid)
            return resolved.NormalizedText;

        return NormalizeAddress(s);
    }

    /// <summary>
    /// Trả về cặp (Địa chỉ tuyệt đối, Ký hiệu có ngoặc kép).
    /// VD: "T1".Q -> ("%DB1.Q", "\"T1\".Q"); %I0.0 -> ("%I0.0", "\"START\""); "Clock_1Hz" -> ("%M0.5", "\"Clock_1Hz\"").
    /// </summary>
    public (string AbsoluteAddr, string QuotedSymbol) FormatDisplayPair(string? rawAddress, string? explicitSymbol)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return (string.Empty, string.IsNullOrWhiteSpace(explicitSymbol) ? string.Empty : $"\"{PlcAddressHelper.StripQuotes(explicitSymbol)}\"");

        string trimmed = rawAddress.Trim();
        if (trimmed == "<???>")
            return ("<???>", string.Empty);

        var op = OperandResolver.Resolve(trimmed, this);
        if (op.IsValid)
        {
            if (op.Area == PlcMemoryArea.Literal)
                return (op.NormalizedText, string.Empty);

            if (op.SliceKind != PlcSliceKind.None)
            {
                string symSlice = !string.IsNullOrEmpty(op.ResolvedTagName) ? op.ResolvedTagName : string.Empty;
                return (op.NormalizedText, symSlice);
            }

            if (op.Area == PlcMemoryArea.DbMember)
            {
                string symDb = !string.IsNullOrEmpty(op.ResolvedTagName)
                    ? op.ResolvedTagName
                    : FormatDbMemberSymbol(op.NormalizedText);
                return (op.NormalizedText, symDb);
            }

            string sym = !string.IsNullOrWhiteSpace(explicitSymbol)
                ? PlcAddressHelper.StripQuotes(explicitSymbol)
                : (!string.IsNullOrWhiteSpace(op.ResolvedTagName) ? op.ResolvedTagName : ResolveTagName(op.NormalizedText));
            if (!string.IsNullOrEmpty(sym))
            {
                string immSuffix = op.IsImmediate ? ":P" : "";
                return (op.NormalizedText, $"\"{sym}\"{immSuffix}");
            }
            return (op.NormalizedText, string.Empty);
        }

        // Chưa khai báo trong PLC Tags (VD: người dùng gõ trực tiếp "MOTOR_RUN")
        string cleaned = PlcAddressHelper.StripQuotes(trimmed);
        if (!PlcAddressHelper.IsAbsoluteAddress(trimmed) && !OperandResolver.TryParseLiteral(trimmed, out _))
            return ("<???>", $"\"{cleaned}\"");

        return (NormalizeAddress(trimmed), string.IsNullOrWhiteSpace(explicitSymbol) ? string.Empty : $"\"{PlcAddressHelper.StripQuotes(explicitSymbol)}\"");
    }

    private string FormatDbMemberSymbol(string absMember)
    {
        int dot = absMember.IndexOf('.');
        if (dot <= 0) return string.Empty;
        string dbAddr = absMember[..dot];
        string member = absMember[(dot + 1)..];
        var db = GetAllDataBlocks().FirstOrDefault(d => NormalizeAddress(d.Address).Equals(dbAddr, StringComparison.OrdinalIgnoreCase));
        return db is not null && !string.IsNullOrWhiteSpace(db.Name) ? $"\"{db.Name}\".{member}" : string.Empty;
    }

    /// <summary>
    /// Định dạng hiển thị địa chỉ theo 3 chế độ (Ký hiệu &amp; Tuyệt đối | Chỉ ký hiệu | Chỉ tuyệt đối).
    /// </summary>
    public (string PrimaryLine, string SecondaryLine) FormatForDisplayMode(
        string? rawAddress,
        string? explicitSymbol,
        PlcAddressDisplayMode mode)
    {
        var (abs, sym) = FormatDisplayPair(rawAddress, explicitSymbol);
        return mode switch
        {
            PlcAddressDisplayMode.SymbolicOnly =>
                !string.IsNullOrEmpty(sym) ? (sym, string.Empty) : (abs, string.Empty),
            PlcAddressDisplayMode.AbsoluteOnly =>
                (abs, string.Empty),
            _ => (abs, sym)
        };
    }

    /// <summary>
    /// Định dạng ngắn gọn cho chân tham số trên hộp hàm (IN1, IN2, OUT...) theo chế độ hiển thị.
    /// </summary>
    public string FormatCompactPinOperand(string? rawOperand, PlcAddressDisplayMode mode)
    {
        if (string.IsNullOrWhiteSpace(rawOperand)) return string.Empty;
        var (abs, sym) = FormatDisplayPair(rawOperand, null);
        if (string.IsNullOrEmpty(sym)) return abs;
        return mode switch
        {
            PlcAddressDisplayMode.SymbolicOnly => sym,
            PlcAddressDisplayMode.AbsoluteOnly => abs,
            _ => $"{sym} ({abs})"
        };
    }

    /// <summary>
    /// Đổi tên biến đồng bộ trên toàn bộ bảng PLC Tags và mọi Network trong chương trình.
    /// </summary>
    public int RenameTagEverywhere(string oldName, string newName)
    {
        string oldClean = PlcAddressHelper.StripQuotes(oldName);
        string newClean = PlcAddressHelper.StripQuotes(newName);
        if (string.IsNullOrWhiteSpace(oldClean) || string.IsNullOrWhiteSpace(newClean) ||
            oldClean.Equals(newClean, StringComparison.Ordinal))
            return 0;

        int updated = 0;
        foreach (var tag in Tags)
        {
            if (tag.Name.Equals(oldClean, StringComparison.OrdinalIgnoreCase))
            {
                tag.Name = newClean;
                updated++;
            }
        }

        string RewriteOperand(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            string t = s.Trim();
            string noQuote = PlcAddressHelper.StripQuotes(t);

            // Trường hợp khớp chính xác tên tag ("OLD" hoặc OLD hoặc "OLD:P")
            bool imm = noQuote.EndsWith(":P", StringComparison.OrdinalIgnoreCase);
            string baseOnly = imm ? noQuote[..^2] : noQuote;
            if (baseOnly.Equals(oldClean, StringComparison.OrdinalIgnoreCase))
            {
                updated++;
                return t.StartsWith('"') ? $"\"{newClean}\"{(imm ? ":P" : "")}" : $"{newClean}{(imm ? ":P" : "")}";
            }

            // Trường hợp thành viên DB hoặc Slice: "OLD".Q, "OLD".x0, OLD.ET
            int dot = t.LastIndexOf('.');
            if (dot > 0 && dot < t.Length - 1)
            {
                string left = PlcAddressHelper.StripQuotes(t[..dot]);
                string right = t[(dot + 1)..];
                if (left.Equals(oldClean, StringComparison.OrdinalIgnoreCase))
                {
                    updated++;
                    return $"\"{newClean}\".{right}";
                }
            }
            return s;
        }

        void WalkBranch(LadderBranch branch)
        {
            foreach (var n in branch.Nodes)
            {
                if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
                {
                    foreach (var sub in n.Branches) WalkBranch(sub);
                    continue;
                }

                if (PlcAddressHelper.StripQuotes(n.SymbolName).Equals(oldClean, StringComparison.OrdinalIgnoreCase))
                {
                    n.SymbolName = newClean;
                    updated++;
                }
                n.Address = RewriteOperand(n.Address);
                n.Param1 = RewriteOperand(n.Param1);
                n.Param2 = RewriteOperand(n.Param2);
                n.ExtraParam = RewriteOperand(n.ExtraParam);
                n.OutputAddress = RewriteOperand(n.OutputAddress);
                if (n.ExtraInputs is not null)
                {
                    for (int i = 0; i < n.ExtraInputs.Count; i++)
                        n.ExtraInputs[i] = RewriteOperand(n.ExtraInputs[i]);
                }
                if (n.ExtraOutputs is not null)
                {
                    for (int i = 0; i < n.ExtraOutputs.Count; i++)
                        n.ExtraOutputs[i] = RewriteOperand(n.ExtraOutputs[i]);
                }
            }
        }

        foreach (var net in EnumerateAllNetworks()) WalkBranch(net.MainBranch);
        foreach (var w in WatchEntries) w.Expression = RewriteOperand(w.Expression);
        foreach (var f in ForceEntries) f.Address = RewriteOperand(f.Address);
        return updated;
    }

    /// <summary>
    /// Kiểm tra xem toán hạng vùng nhớ %M có giao với System Memory (MB_sys bit 0..3) hoặc Clock Memory (MB_clk bit 0..7) hay không.
    /// </summary>
    public bool IsAddressInSystemOrClockMemory(PlcMemoryArea area, int byteOffset, int bitOffset, int accessSizeBytes)
    {
        if (area != PlcMemoryArea.M) return false;
        int startByte = byteOffset;
        int endByte = accessSizeBytes <= 0 ? byteOffset : byteOffset + accessSizeBytes - 1;

        if (EnableClockMemory)
        {
            int cb = Math.Clamp(ClockMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
            if (cb >= startByte && cb <= endByte)
                return true;
        }

        if (EnableSystemMemory)
        {
            int sb = Math.Clamp(SystemMemoryByte, 0, OperandResolver.MaxMemoryBytes - 1);
            if (sb >= startByte && sb <= endByte)
            {
                // Nếu truy cập từng bit trên byte hệ thống, chỉ khóa 4 bit 0..3 (FirstScan, DiagStatusUpdate, AlwaysTRUE, AlwaysFALSE)
                if (accessSizeBytes <= 0)
                    return bitOffset >= 0 && bitOffset <= 3;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tự động đồng bộ danh sách chân ExtraInputs (IN3..INn) theo biểu thức hiện tại của khối CALCULATE.
    /// </summary>
    public static void SyncCalculateExtraInputs(LadderNode node)
    {
        if (node.Kind != LadderElementKind.Calculate) return;
        int reqInputs = PlcExpressionEvaluator.DetectMaxInputIndex(node.ExtraParam);
        int extraNeeded = Math.Max(0, reqInputs - 2);
        if (extraNeeded == 0)
        {
            node.ExtraInputs = null;
            return;
        }

        node.ExtraInputs ??= new List<string>();
        while (node.ExtraInputs.Count < extraNeeded)
            node.ExtraInputs.Add("0");
        while (node.ExtraInputs.Count > extraNeeded)
            node.ExtraInputs.RemoveAt(node.ExtraInputs.Count - 1);
    }

    /// <summary>
    /// Lấy danh sách tất cả Data Block đã tạo (từ bảng Tags và từ các khối Timer/Counter/R_TRIG/F_TRIG trên Networks).
    /// </summary>
    public List<PlcTagEntry> GetAllDataBlocks()
    {
        var result = new List<PlcTagEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in Tags)
        {
            string norm = NormalizeAddress(tag.Address);
            if (norm.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && !norm.Contains('.'))
            {
                if (seen.Add(norm))
                    result.Add(tag);
            }
        }

        void ScanBranch(LadderBranch branch)
        {
            foreach (var n in branch.Nodes)
            {
                if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
                {
                    foreach (var sub in n.Branches) ScanBranch(sub);
                }
                else if (n.RequiresCallOptions)
                {
                    string norm = NormalizeAddress(n.Address);
                    if (norm.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && !norm.Contains('.') && seen.Add(norm))
                    {
                        string fallbackName = !string.IsNullOrWhiteSpace(n.SymbolName)
                            ? PlcAddressHelper.StripQuotes(n.SymbolName)
                            : norm.TrimStart('%');
                        string dbType = n.Kind switch
                        {
                            LadderElementKind.CounterCTU or LadderElementKind.CounterCTD or LadderElementKind.CounterCTUD => "IEC_Counter",
                            LadderElementKind.RTrig or LadderElementKind.FTrig => "IEC_Trig",
                            LadderElementKind.CtrlPwm => "IEC_PWM",
                            LadderElementKind.CtrlHsc => "IEC_HSC",
                            LadderElementKind.PidCompact => "PID_Compact",
                            LadderElementKind.ModbusCommLoad => "Modbus_Comm_Load",
                            LadderElementKind.ModbusMaster => "Modbus_Master",
                            LadderElementKind.ModbusTcpClient => "MB_CLIENT",
                            _ => "IEC_Timer"
                        };
                        result.Add(new PlcTagEntry
                        {
                            Name = fallbackName,
                            Address = norm,
                            DataType = dbType
                        });
                    }
                }
            }
        }

        foreach (var net in EnumerateAllNetworks()) ScanBranch(net.MainBranch);
        return result;
    }

    public int AllocateNextDbNumber()
    {
        var used = new HashSet<int>();
        void Scan(LadderBranch branch)
        {
            foreach (var n in branch.Nodes)
            {
                if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
                {
                    foreach (var sub in n.Branches) Scan(sub);
                }
                else if (n.RequiresCallOptions)
                {
                    string a = NormalizeAddress(n.Address);
                    if (a.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && int.TryParse(a[3..], out int num))
                        used.Add(num);
                }
            }
        }

        foreach (var net in EnumerateAllNetworks()) Scan(net.MainBranch);
        foreach (var tag in Tags)
        {
            string a = NormalizeAddress(tag.Address);
            if (a.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && int.TryParse(a[3..], out int num))
                used.Add(num);
        }

        int next = 1;
        while (used.Contains(next)) next++;
        return next;
    }

    public string AllocateNextDbAddress() => $"%DB{AllocateNextDbNumber()}";

    public string SuggestNextDbName(bool isTimer, bool isTrig = false)
    {
        string prefix = isTrig ? "IEC_Trig_" : (isTimer ? "IEC_Timer_" : "IEC_Counter_");
        var existingNames = new HashSet<string>(
            Tags.Select(t => t.Name).Concat(GetAllDataBlocks().Select(d => d.Name)),
            StringComparer.OrdinalIgnoreCase);

        int idx = 0;
        while (existingNames.Contains($"{prefix}{idx}_DB")) idx++;
        return $"{prefix}{idx}_DB";
    }

    public void EnsureTagExists(string address, string defaultName, string dataType, bool overwriteDataType = false)
    {
        string norm = NormalizeAddress(address);
        if (string.IsNullOrWhiteSpace(norm) || norm == "<???>") return;
        var existing = Tags.FirstOrDefault(t => NormalizeAddress(t.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(defaultName)) existing.Name = defaultName;
            if ((overwriteDataType || string.IsNullOrWhiteSpace(existing.DataType)) && !string.IsNullOrWhiteSpace(dataType))
                existing.DataType = dataType;
            return;
        }

        Tags.Add(new PlcTagEntry
        {
            Name = defaultName,
            Address = norm,
            DataType = dataType
        });
    }

    public PlcProgramModel Clone() => new()
    {
        Tags = Tags.Select(t => t.Clone()).ToList(),
        Networks = Networks.Select(n => n.Clone()).ToList(),
        AdditionalBlocks = AdditionalBlocks.Select(b => b.Clone()).ToList(),
        EnableSystemMemory = EnableSystemMemory,
        SystemMemoryByte = SystemMemoryByte,
        EnableClockMemory = EnableClockMemory,
        ClockMemoryByte = ClockMemoryByte,
        MaxCycleTimeMs = MaxCycleTimeMs,
        StopOutputMode = StopOutputMode,
        WatchEntries = WatchEntries.Select(w => w.Clone()).ToList(),
        ForceEntries = ForceEntries.Select(f => f.Clone()).ToList(),
        FavoriteInstructions = FavoriteInstructions is not null ? new List<LadderElementKind>(FavoriteInstructions) : null
    };

    public static string NormalizeAddress(string? raw) => PlcAddressHelper.NormalizeAddress(raw);
}

public sealed record PlcDiagnostic(
    bool IsError,
    int NetworkNumber,
    string? NodeId,
    string Message);

public sealed class PlcCompileResult
{
    public List<PlcDiagnostic> Diagnostics { get; } = new();
    public int ErrorCount => Diagnostics.Count(d => d.IsError);
    public int WarningCount => Diagnostics.Count(d => !d.IsError);
    public bool Success => ErrorCount == 0;
}
