using System.Text.Json.Serialization;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Plc;

/// <summary>
/// Ánh xạ ký hiệu biến PLC (PLC Tag Table chuẩn TIA Portal), ví dụ %I0.0 -> "START", %Q0.0 -> "LAMP1", %MW2 -> "SEQUENCE".
/// </summary>
public sealed class PlcTagEntry
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Bool";
    public string Comment { get; set; } = string.Empty;

    public PlcTagEntry Clone() => new()
    {
        Name = Name,
        Address = Address,
        DataType = DataType,
        Comment = Comment
    };
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

    /// <summary>Địa chỉ chính: %I0.0, %Q0.0, %M0.0, %DB1...</summary>
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
/// Một Network (Mạng lệnh) trong chương trình OB1 chuẩn Siemens TIA Portal.
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
            return explicitSymbol.Trim();
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        string norm = address.Trim().ToUpperInvariant();
        if (!norm.StartsWith('%') && norm.Length > 0)
            norm = "%" + norm;

        var tag = Tags.FirstOrDefault(t => NormalizeAddress(t.Address) == norm);
        return tag?.Name ?? string.Empty;
    }

    public string AllocateNextDbAddress()
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
                else if (n.IsTimer || n.IsCounter)
                {
                    string a = NormalizeAddress(n.Address);
                    if (a.StartsWith("%DB") && int.TryParse(a[3..], out int num))
                        used.Add(num);
                }
            }
        }

        foreach (var net in Networks) Scan(net.MainBranch);
        foreach (var tag in Tags)
        {
            string a = NormalizeAddress(tag.Address);
            if (a.StartsWith("%DB") && int.TryParse(a[3..], out int num))
                used.Add(num);
        }

        int next = 1;
        while (used.Contains(next)) next++;
        return $"%DB{next}";
    }

    public void EnsureTagExists(string address, string defaultName, string dataType)
    {
        string norm = NormalizeAddress(address);
        if (string.IsNullOrWhiteSpace(norm)) return;
        if (Tags.Any(t => NormalizeAddress(t.Address) == norm)) return;

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

    public static string NormalizeAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim().ToUpperInvariant();
        if (!s.StartsWith('%') && (s[0] is 'I' or 'Q' or 'M' or 'D'))
            s = "%" + s;
        return s;
    }
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
