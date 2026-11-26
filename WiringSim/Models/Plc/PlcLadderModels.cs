using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Plc;

/// <summary>
/// Ánh xạ ký hiệu biến PLC (PLC Tag Table chuẩn TIA Portal V21), ví dụ %I0.0 -> "ON/OFF", %Q0.0 -> "LAMP1", %MW2 -> "SEQUENCE".
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

    public PlcTagEntry Clone() => new()
    {
        Name = Name,
        Address = Address,
        DataType = DataType,
        Retain = Retain,
        Accessible = Accessible,
        Writable = Writable,
        Visible = Visible,
        Comment = Comment
    };
}

/// <summary>
/// Mô tả một thuộc tính nội bộ bên trong khối dữ liệu DB (Timer: IN/PT/Q/ET, Counter: CU/CD/R/LD/PV/QU/QD/CV).
/// </summary>
public sealed record PlcDbMemberDescriptor(string Name, string DataType, string Description);

/// <summary>
/// Công cụ chuẩn hóa, phân tích, chọn và tự động tăng địa chỉ PLC theo đúng cơ chế TIA Portal V21.
/// </summary>
public static partial class PlcAddressHelper
{
    public static readonly string[] SupportedDataTypes =
    [
        "Bool", "Byte", "Int", "Word", "DInt", "DWord", "Real", "Time"
    ];

    public static readonly string[] OperandIdentifiers = ["I", "Q", "M"];
    public static readonly string[] OperandTypes = ["Bit", "Byte", "Word", "DWord"];

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<byte>\d+)\.(?<bit>[0-7])$", RegexOptions.IgnoreCase)]
    private static partial Regex BitPattern();

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<size>B|W|D)(?<offset>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SizedPattern();

    [GeneratedRegex(@"^%?DB(?<num>\d+)(?:\.(?<field>[A-Z0-9_]+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DbPattern();

    public static bool IsAbsoluteAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim();
        return BitPattern().IsMatch(s) || SizedPattern().IsMatch(s) || DbPattern().IsMatch(s);
    }

    /// <summary>
    /// Chuẩn hóa địa chỉ tuyệt đối (%I0.0, %Q0.0, %MB0, %MW2, %MD4, %DB1, %DB1.Q).
    /// Giữ nguyên biểu thức ký hiệu như "TD11".Q hoặc &lt;???&gt; mà không chèn nhầm tiền tố %.
    /// </summary>
    public static string NormalizeAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (s == "<???>") return s;

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
    /// Tự động tính địa chỉ liền kề tiếp theo chuẩn TIA Portal V21 khi nhấn Enter trong bảng PLC Tags:
    /// - Bool: %I0.0 -> %I0.1 ... %I0.7 -> %I1.0
    /// - Byte: %MB0 -> %MB1 (+1)
    /// - Int / Word: %MW0 -> %MW2 (+2)
    /// - DInt / DWord / Real / Time: %MD0 -> %MD4 (+4)
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
            {
                return $"%{area}{byteOffset + 1}.0";
            }
            return "%I0.0";
        }

        if (dt.Equals("Byte", StringComparison.OrdinalIgnoreCase))
        {
            if (!parsedBit && !parsedSized) return "%MB0";
            return $"%{area}B{byteOffset + 1}";
        }

        if (dt.Equals("Int", StringComparison.OrdinalIgnoreCase) ||
            dt.Equals("Word", StringComparison.OrdinalIgnoreCase))
        {
            if (!parsedBit && !parsedSized) return "%MW0";
            return $"%{area}W{byteOffset + 2}";
        }

        // DInt, DWord, Real, Time -> bước nhảy +4 byte
        if (!parsedBit && !parsedSized) return "%MD0";
        return $"%{area}D{byteOffset + 4}";
    }

    public static string BuildAddress(string operandIdentifier, string operandType, int byteAddress, int bitNumber)
    {
        string id = string.IsNullOrWhiteSpace(operandIdentifier) ? "M" : operandIdentifier.Trim().ToUpperInvariant();
        if (id is not ("I" or "Q" or "M")) id = "M";

        int safeByte = Math.Clamp(byteAddress, 0, 198);
        int safeBit = Math.Clamp(bitNumber, 0, 7);

        return (operandType ?? "Bit").Trim() switch
        {
            "Byte" => $"%{id}B{safeByte}",
            "Word" => $"%{id}W{safeByte}",
            "DWord" => $"%{id}D{safeByte}",
            _ => $"%{id}{safeByte}.{safeBit}"
        };
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
            "Byte" => dt.Equals("Byte", StringComparison.OrdinalIgnoreCase),
            "Word" => dt.Equals("Int", StringComparison.OrdinalIgnoreCase) || dt.Equals("Word", StringComparison.OrdinalIgnoreCase),
            "DWord" => dt.Equals("DInt", StringComparison.OrdinalIgnoreCase) || dt.Equals("DWord", StringComparison.OrdinalIgnoreCase) ||
                       dt.Equals("Real", StringComparison.OrdinalIgnoreCase) || dt.Equals("Time", StringComparison.OrdinalIgnoreCase),
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

        return
        [
            new PlcDbMemberDescriptor("IN", "Bool", "Timer start input"),
            new PlcDbMemberDescriptor("PT", "Time", "Preset duration"),
            new PlcDbMemberDescriptor("Q", "Bool", "Timer output Q"),
            new PlcDbMemberDescriptor("ET", "Time", "Elapsed time")
        ];
    }
}

/// <summary>
/// Một nhánh lệnh nối tiếp trong mạng Ladder (có thể nằm trong Network chính hoặc trong một ParallelBlock).
/// </summary>
public sealed class LadderBranch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public List<LadderNode> Nodes { get; set; } = new();

    public LadderBranch Clone() => new()
    {
        Id = Id,
        Nodes = Nodes.Select(n => n.Clone()).ToList()
    };
}

/// <summary>
/// Một phần tử trong ngôn ngữ lập trình Ladder (Tiếp điểm, Cuộn hút, Khối Timer/Counter DB, So sánh, Toán học/Move, hoặc Khối rẽ nhánh song song).
/// </summary>
public sealed class LadderNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public LadderElementKind Kind { get; set; } = LadderElementKind.ContactNO;

    /// <summary>Địa chỉ chính: %I0.0, %Q0.0, %M0.0, %DB1, "TD11".Q, hoặc &lt;???&gt;...</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Tên ký hiệu biến (nếu để trống sẽ tự tra cứu trong PLC Tag Table theo Address).</summary>
    public string SymbolName { get; set; } = string.Empty;

    /// <summary>Tham số 1: PT (T#3s) cho Timer | PV (5) cho Counter | IN1 cho So sánh / Toán học / MOVE | Bit nhớ sườn cho P/N.</summary>
    public string Param1 { get; set; } = string.Empty;

    /// <summary>Tham số 2: IN2 cho So sánh / Toán học | Địa chỉ Reset R cho TONR / CTU / CTUD | Địa chỉ Load LD cho CTD.</summary>
    public string Param2 { get; set; } = string.Empty;

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

    [JsonIgnore]
    public bool IsCounter => Kind is
        LadderElementKind.CounterCTU or LadderElementKind.CounterCTD or LadderElementKind.CounterCTUD;

    [JsonIgnore]
    public bool RequiresCallOptions => IsTimer || IsCounter || Kind is
        LadderElementKind.CoilTP or LadderElementKind.CoilTON or LadderElementKind.CoilTOF or LadderElementKind.CoilTONR;

    [JsonIgnore]
    public bool IsComparator => PlcInstructionCatalog.IsComparator(Kind);

    [JsonIgnore]
    public bool IsContactOrCoilGlyph => PlcInstructionCatalog.IsInlineGlyph(Kind);

    public LadderNode Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Address = Address,
        SymbolName = SymbolName,
        Param1 = Param1,
        Param2 = Param2,
        OutputAddress = OutputAddress,
        ExtraParam = ExtraParam,
        Branches = Branches?.Select(b => b.Clone()).ToList()
    };
}

/// <summary>
/// Một Network (Mạng lệnh) trong chương trình OB1 chuẩn TIA Portal.
/// </summary>
public sealed class LadderNetwork
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Network";
    public string Comment { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public LadderBranch MainBranch { get; set; } = new();

    public LadderNetwork Clone() => new()
    {
        Id = Id,
        Title = Title,
        Comment = Comment,
        IsCollapsed = IsCollapsed,
        MainBranch = MainBranch.Clone()
    };
}

/// <summary>
/// Toàn bộ chương trình PLC (Danh sách Networks + Bảng Tag ký hiệu biến).
/// </summary>
public sealed class PlcProgramModel
{
    public List<PlcTagEntry> Tags { get; set; } = new();
    public List<LadderNetwork> Networks { get; set; } = new();

    public string ResolveTagName(string address, string explicitSymbol = "")
    {
        if (!string.IsNullOrWhiteSpace(explicitSymbol))
            return PlcAddressHelper.StripQuotes(explicitSymbol);
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        string norm = NormalizeAddress(address);
        var tag = Tags.FirstOrDefault(t => NormalizeAddress(t.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (tag is not null && !string.IsNullOrWhiteSpace(tag.Name))
            return tag.Name;

        // Hỗ trợ ngược %DB2.Q -> tra tên DB2 ("TD11")
        if (norm.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && norm.Contains('.'))
        {
            int dot = norm.IndexOf('.');
            string dbAddr = norm[..dot];
            string field = norm[(dot + 1)..];
            var dbEntry = GetAllDataBlocks().FirstOrDefault(d => NormalizeAddress(d.Address).Equals(dbAddr, StringComparison.OrdinalIgnoreCase));
            if (dbEntry is not null && !string.IsNullOrWhiteSpace(dbEntry.Name))
                return $"{dbEntry.Name}.{field}";
        }

        return string.Empty;
    }

    /// <summary>
    /// Quy đổi địa chỉ ký hiệu hoặc truy xuất thành phần DB (VD: "TD11".Q, TD11.Q, "ON/OFF", ON/OFF)
    /// thành địa chỉ tuyệt đối (%DB2.Q, %I0.0) cho Compiler và Runtime Engine.
    /// </summary>
    public string ResolveAddressForRuntime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (s == "<???>") return s;

        if (PlcAddressHelper.IsAbsoluteAddress(s))
            return NormalizeAddress(s);

        // Trường hợp truy xuất thuộc tính DB theo tên: "TD11".Q hoặc TD11.Q
        int dotIdx = s.LastIndexOf('.');
        if (dotIdx > 0 && dotIdx < s.Length - 1)
        {
            string leftPart = PlcAddressHelper.StripQuotes(s[..dotIdx]);
            string memberPart = s[(dotIdx + 1)..].Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(leftPart) && !string.IsNullOrEmpty(memberPart))
            {
                var db = GetAllDataBlocks().FirstOrDefault(d =>
                    d.Name.Equals(leftPart, StringComparison.OrdinalIgnoreCase));
                if (db is not null)
                    return $"{NormalizeAddress(db.Address)}.{memberPart}";
            }
        }

        // Trường hợp gõ tên Tag trực tiếp: "ON/OFF" hoặc ON/OFF hoặc "TD11"
        string cleanName = PlcAddressHelper.StripQuotes(s);
        var matchedTag = Tags.FirstOrDefault(t =>
            t.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(t.Address));
        if (matchedTag is not null)
            return NormalizeAddress(matchedTag.Address);

        var matchedDb = GetAllDataBlocks().FirstOrDefault(d =>
            d.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        if (matchedDb is not null)
            return NormalizeAddress(matchedDb.Address);

        return NormalizeAddress(s);
    }

    /// <summary>
    /// Trả về cặp (Địa chỉ tuyệt đối hiển thị màu xanh ngọc, Ký hiệu hiển thị có ngoặc kép chuẩn TIA Portal).
    /// VD: "TD11".Q -> ("%DB2.Q", "\"TD11\".Q"); %I0.0 -> ("%I0.0", "\"ON/OFF\"").
    /// </summary>
    public (string AbsoluteAddr, string QuotedSymbol) FormatDisplayPair(string? rawAddress, string? explicitSymbol)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return (string.Empty, string.IsNullOrWhiteSpace(explicitSymbol) ? string.Empty : $"\"{PlcAddressHelper.StripQuotes(explicitSymbol)}\"");

        string trimmed = rawAddress.Trim();
        if (trimmed == "<???>")
            return ("<???>", string.Empty);

        string resolved = ResolveAddressForRuntime(trimmed);

        // Truy xuất thành phần DB (%DB2.Q hoặc "TD11".Q)
        if (resolved.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && resolved.Contains('.'))
        {
            int dot = resolved.IndexOf('.');
            string dbAddr = resolved[..dot];
            string member = resolved[(dot + 1)..];
            var db = GetAllDataBlocks().FirstOrDefault(d => NormalizeAddress(d.Address).Equals(dbAddr, StringComparison.OrdinalIgnoreCase));
            if (db is not null && !string.IsNullOrWhiteSpace(db.Name))
                return (resolved, $"\"{db.Name}\".{member}");

            // Nếu người dùng nhập sẵn "TD11".Q nhưng DB chưa tồn tại
            if (!PlcAddressHelper.IsAbsoluteAddress(trimmed))
                return (resolved, trimmed);

            return (resolved, string.Empty);
        }

        string sym = ResolveTagName(resolved, explicitSymbol ?? string.Empty);
        if (!string.IsNullOrEmpty(sym))
            return (resolved, $"\"{sym}\"");

        return (resolved, string.Empty);
    }

    /// <summary>
    /// Lấy danh sách tất cả Data Block đã tạo (từ bảng Tags và từ các khối Timer/Counter trên Networks).
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
                else if (n.IsTimer || n.IsCounter || n.IsTimerCoil)
                {
                    string norm = NormalizeAddress(n.Address);
                    if (norm.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && !norm.Contains('.') && seen.Add(norm))
                    {
                        string fallbackName = !string.IsNullOrWhiteSpace(n.SymbolName)
                            ? PlcAddressHelper.StripQuotes(n.SymbolName)
                            : norm.TrimStart('%');
                        result.Add(new PlcTagEntry
                        {
                            Name = fallbackName,
                            Address = norm,
                            DataType = n.IsCounter ? "IEC_Counter" : "IEC_Timer"
                        });
                    }
                }
            }
        }

        foreach (var net in Networks) ScanBranch(net.MainBranch);
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
                else if (n.IsTimer || n.IsCounter || n.IsTimerCoil)
                {
                    string a = NormalizeAddress(n.Address);
                    if (a.StartsWith("%DB", StringComparison.OrdinalIgnoreCase) && int.TryParse(a[3..], out int num))
                        used.Add(num);
                }
            }
        }

        foreach (var net in Networks) Scan(net.MainBranch);
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

    public string SuggestNextDbName(bool isTimer)
    {
        string prefix = isTimer ? "IEC_Timer_" : "IEC_Counter_";
        var existingNames = new HashSet<string>(
            Tags.Select(t => t.Name).Concat(GetAllDataBlocks().Select(d => d.Name)),
            StringComparer.OrdinalIgnoreCase);

        int idx = 0;
        while (existingNames.Contains($"{prefix}{idx}_DB")) idx++;
        return $"{prefix}{idx}_DB";
    }

    public void EnsureTagExists(string address, string defaultName, string dataType)
    {
        string norm = NormalizeAddress(address);
        if (string.IsNullOrWhiteSpace(norm) || norm == "<???>") return;
        var existing = Tags.FirstOrDefault(t => NormalizeAddress(t.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(defaultName)) existing.Name = defaultName;
            if (!string.IsNullOrWhiteSpace(dataType)) existing.DataType = dataType;
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
        Networks = Networks.Select(n => n.Clone()).ToList()
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
