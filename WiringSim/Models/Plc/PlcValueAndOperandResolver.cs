using System.Globalization;
using System.Text.RegularExpressions;
using WiringSim.UI.Services;

namespace WiringSim.Models.Plc;

/// <summary>
/// Hệ thống kiểu dữ liệu chuẩn IEC 61131-3 dùng chung cho Compiler, Runtime và giao diện.
/// </summary>
public enum PlcDataType
{
    Bool = 0,
    Byte = 1,
    Word = 2,
    DWord = 3,
    SInt = 4,
    USInt = 5,
    Int = 6,
    UInt = 7,
    DInt = 8,
    UDInt = 9,
    Real = 10,
    Time = 11,
    LReal = 12,
    Char = 13,
    String = 14,
    DTL = 15,
    Bcd16 = 16,
    Bcd32 = 17
}

public enum PlcWatchFormat
{
    Auto = 0,
    Dec = 1,
    Hex = 2,
    Bin = 3,
    Float = 4,
    Time = 5,
    Bool = 6
}

public enum PlcAddressDisplayMode
{
    SymbolicAndAbsolute = 0,
    SymbolicOnly = 1,
    AbsoluteOnly = 2
}

public enum PlcMemoryArea
{
    None = 0,
    I = 1,
    Q = 2,
    M = 3,
    DB = 4,
    DbMember = 5,
    Literal = 6
}

public enum PlcSliceKind
{
    None = 0,
    Bit = 1,   // .x0 .. .x31
    Byte = 2,  // .b0 .. .b3
    Word = 3   // .w0 .. .w1
}

public sealed class PlcWatchEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Expression { get; set; } = "%M0.0";
    public PlcWatchFormat DisplayFormat { get; set; } = PlcWatchFormat.Auto;
    public string ModifyValue { get; set; } = string.Empty;
    public bool IsSelectedForModify { get; set; } = true;
    public string Comment { get; set; } = string.Empty;

    public PlcWatchEntry Clone() => new()
    {
        Id = Id,
        Expression = Expression,
        DisplayFormat = DisplayFormat,
        ModifyValue = ModifyValue,
        IsSelectedForModify = IsSelectedForModify,
        Comment = Comment
    };
}

public static class PlcDataTypeHelper
{
    public static readonly string[] AllNames =
    [
        "Bool", "Byte", "Word", "DWord", "SInt", "USInt", "Int", "UInt", "DInt", "UDInt", "Real", "LReal", "Time", "Char", "String", "DTL"
    ];

    public static bool TryParse(string? name, out PlcDataType type)
    {
        type = PlcDataType.Int;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Enum.TryParse(name.Trim(), ignoreCase: true, out type);
    }

    public static PlcDataType ParseOrDefault(string? name, PlcDataType fallback = PlcDataType.Int) =>
        TryParse(name, out var dt) ? dt : fallback;

    public static int BitSize(PlcDataType dt) => dt switch
    {
        PlcDataType.Bool => 1,
        PlcDataType.Byte or PlcDataType.SInt or PlcDataType.USInt or PlcDataType.Char => 8,
        PlcDataType.Word or PlcDataType.Int or PlcDataType.UInt or PlcDataType.Bcd16 => 16,
        PlcDataType.LReal => 64,
        _ => 32
    };

    public static int ByteSize(PlcDataType dt) => dt switch
    {
        PlcDataType.Bool => 0,
        PlcDataType.Byte or PlcDataType.SInt or PlcDataType.USInt or PlcDataType.Char => 1,
        PlcDataType.Word or PlcDataType.Int or PlcDataType.UInt or PlcDataType.Bcd16 => 2,
        PlcDataType.LReal => 8,
        PlcDataType.DTL => 12,
        _ => 4
    };

    public static bool IsSignedInteger(PlcDataType dt) =>
        dt is PlcDataType.SInt or PlcDataType.Int or PlcDataType.DInt or PlcDataType.Time;

    public static bool IsUnsignedInteger(PlcDataType dt) =>
        dt is PlcDataType.Byte or PlcDataType.USInt or PlcDataType.Char or PlcDataType.Word or PlcDataType.UInt or PlcDataType.Bcd16 or PlcDataType.DWord or PlcDataType.UDInt or PlcDataType.Bcd32;

    public static bool IsFloatingPoint(PlcDataType dt) => dt is PlcDataType.Real or PlcDataType.LReal;

    public static bool CanSlice(PlcDataType dt) => dt is not (PlcDataType.Bool or PlcDataType.String or PlcDataType.DTL or PlcDataType.LReal);

    public static (double Min, double Max) GetNumericBounds(PlcDataType dt) => dt switch
    {
        PlcDataType.Bool => (0, 1),
        PlcDataType.SInt => (sbyte.MinValue, sbyte.MaxValue),
        PlcDataType.Byte or PlcDataType.USInt or PlcDataType.Char => (byte.MinValue, byte.MaxValue),
        PlcDataType.Int => (short.MinValue, short.MaxValue),
        PlcDataType.Word or PlcDataType.UInt or PlcDataType.Bcd16 => (ushort.MinValue, ushort.MaxValue),
        PlcDataType.DInt or PlcDataType.Time => (int.MinValue, int.MaxValue),
        PlcDataType.DWord or PlcDataType.UDInt or PlcDataType.Bcd32 => (uint.MinValue, uint.MaxValue),
        PlcDataType.Real => (-3.40282347e+38, 3.40282347e+38),
        PlcDataType.LReal => (-1.7976931348623157e+308, 1.7976931348623157e+308),
        _ => (int.MinValue, int.MaxValue)
    };
}

/// <summary>
/// Giá trị có kiểu chuẩn IEC 61131-3: bảo toàn chính xác độ rộng bit (1/8/16/32-bit),
/// dấu, IEEE 754 Real và Time (32-bit có dấu tính theo mili-giây ms).
/// </summary>
public readonly struct PlcValue
{
    public PlcDataType DataType { get; }
    private readonly long _intBits;
    private readonly float _realVal;
    private readonly double _lrealVal;
    private readonly string? _strVal;
    private readonly PlcDtlValue _dtlVal;

    private PlcValue(PlcDataType dataType, long intBits, float realVal = 0f, double lrealVal = 0.0, string? strVal = null, PlcDtlValue dtlVal = default)
    {
        DataType = dataType;
        _intBits = intBits;
        _realVal = realVal;
        _lrealVal = lrealVal;
        _strVal = strVal;
        _dtlVal = dtlVal;
    }

    public static PlcValue FromBool(bool v) => new(PlcDataType.Bool, v ? 1L : 0L);
    public static PlcValue FromByte(byte v) => new(PlcDataType.Byte, v);
    public static PlcValue FromChar(char v) => new(PlcDataType.Char, (byte)v);
    public static PlcValue FromWord(ushort v) => new(PlcDataType.Word, v);
    public static PlcValue FromDWord(uint v) => new(PlcDataType.DWord, v);
    public static PlcValue FromSInt(sbyte v) => new(PlcDataType.SInt, v);
    public static PlcValue FromUSInt(byte v) => new(PlcDataType.USInt, v);
    public static PlcValue FromInt(short v) => new(PlcDataType.Int, v);
    public static PlcValue FromUInt(ushort v) => new(PlcDataType.UInt, v);
    public static PlcValue FromDInt(int v) => new(PlcDataType.DInt, v);
    public static PlcValue FromUDInt(uint v) => new(PlcDataType.UDInt, v);
    public static PlcValue FromReal(float v) => new(PlcDataType.Real, BitConverter.SingleToInt32Bits(v), v, v);
    public static PlcValue FromLReal(double v) => new(PlcDataType.LReal, BitConverter.DoubleToInt64Bits(v), (float)v, v);
    public static PlcValue FromTimeMs(int ms) => new(PlcDataType.Time, ms);
    public static PlcValue FromString(string? s)
    {
        string safe = s ?? string.Empty;
        if (safe.Length > 254) safe = safe[..254];
        return new(PlcDataType.String, safe.Length, strVal: safe);
    }
    public static PlcValue FromDtl(PlcDtlValue dtl) => new(PlcDataType.DTL, 0, dtlVal: dtl);

    public bool AsBool => DataType switch
    {
        PlcDataType.Real => Math.Abs(_realVal) > float.Epsilon,
        PlcDataType.LReal => Math.Abs(_lrealVal) > double.Epsilon,
        PlcDataType.String => !string.IsNullOrEmpty(_strVal),
        _ => _intBits != 0
    };

    public double AsDouble => DataType switch
    {
        PlcDataType.Real => _realVal,
        PlcDataType.LReal => _lrealVal,
        _ => _intBits
    };

    public float AsReal => DataType switch
    {
        PlcDataType.Real => _realVal,
        PlcDataType.LReal => (float)_lrealVal,
        _ => (float)_intBits
    };

    public string AsString => DataType switch
    {
        PlcDataType.String => _strVal ?? string.Empty,
        PlcDataType.Char => ((char)(_intBits & 0xFF)).ToString(),
        PlcDataType.DTL => _dtlVal.FormatIso(),
        PlcDataType.Time => OperandResolver.FormatTimeMs(AsTimeMs),
        PlcDataType.Real or PlcDataType.LReal => AsDouble.ToString("G", CultureInfo.InvariantCulture),
        PlcDataType.Bool => AsBool ? "TRUE" : "FALSE",
        _ => _intBits.ToString(CultureInfo.InvariantCulture)
    };

    public PlcDtlValue AsDtl => _dtlVal;

    public long AsInt64 => DataType switch
    {
        PlcDataType.Real => double.IsNaN(_realVal) || double.IsInfinity(_realVal) ? 0L : (long)Math.Round(_realVal, MidpointRounding.ToEven),
        PlcDataType.LReal => double.IsNaN(_lrealVal) || double.IsInfinity(_lrealVal) ? 0L : (long)Math.Round(_lrealVal, MidpointRounding.ToEven),
        _ => _intBits
    };

    public int AsTimeMs => DataType switch
    {
        PlcDataType.Real => (int)Math.Clamp(Math.Round(_realVal), int.MinValue, int.MaxValue),
        PlcDataType.LReal => (int)Math.Clamp(Math.Round(_lrealVal), int.MinValue, int.MaxValue),
        _ => (int)Math.Clamp(_intBits, int.MinValue, int.MaxValue)
    };

    public uint AsRawUInt32 => DataType switch
    {
        PlcDataType.Real => unchecked((uint)BitConverter.SingleToInt32Bits(_realVal)),
        PlcDataType.LReal => unchecked((uint)(BitConverter.DoubleToInt64Bits(_lrealVal) & 0xFFFFFFFFL)),
        PlcDataType.Bool => _intBits != 0 ? 1u : 0u,
        PlcDataType.Byte or PlcDataType.USInt or PlcDataType.SInt or PlcDataType.Char => unchecked((byte)_intBits),
        PlcDataType.Word or PlcDataType.UInt or PlcDataType.Int or PlcDataType.Bcd16 => unchecked((ushort)_intBits),
        _ => unchecked((uint)_intBits)
    };

    public ulong AsRawUInt64 => DataType == PlcDataType.LReal
        ? unchecked((ulong)BitConverter.DoubleToInt64Bits(_lrealVal))
        : AsRawUInt32;

    public bool IsValidReal => DataType switch
    {
        PlcDataType.Real => !float.IsNaN(_realVal) && !float.IsInfinity(_realVal) && !float.IsSubnormal(_realVal),
        PlcDataType.LReal => !double.IsNaN(_lrealVal) && !double.IsInfinity(_lrealVal) && !double.IsSubnormal(_lrealVal),
        _ => true
    };

    /// <summary>
    /// Tạo PlcValue từ số double với kiểm tra tràn dải chuẩn IEC 61131-3.
    /// </summary>
    public static bool TryFromDouble(double value, PlcDataType target, out PlcValue result)
    {
        result = default;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;

        if (target == PlcDataType.LReal)
        {
            result = FromLReal(value);
            return true;
        }

        if (target == PlcDataType.Real)
        {
            if (Math.Abs(value) > float.MaxValue)
                return false;
            float f = (float)value;
            if (float.IsNaN(f) || float.IsInfinity(f))
                return false;
            result = FromReal(f);
            return true;
        }

        if (target == PlcDataType.Bool)
        {
            result = FromBool(Math.Abs(value) > 1e-12);
            return true;
        }

        if (target == PlcDataType.String)
        {
            result = FromString(value.ToString("G", CultureInfo.InvariantCulture));
            return true;
        }

        double rounded = Math.Round(value, MidpointRounding.ToEven);
        var (min, max) = PlcDataTypeHelper.GetNumericBounds(target);
        if (rounded < min || rounded > max)
            return false;

        result = target switch
        {
            PlcDataType.SInt => FromSInt((sbyte)rounded),
            PlcDataType.Byte => FromByte((byte)rounded),
            PlcDataType.Char => FromChar((char)(byte)rounded),
            PlcDataType.USInt => FromUSInt((byte)rounded),
            PlcDataType.Int => FromInt((short)rounded),
            PlcDataType.Word => FromWord((ushort)rounded),
            PlcDataType.UInt => FromUInt((ushort)rounded),
            PlcDataType.Bcd16 => FromWord((ushort)rounded),
            PlcDataType.DInt => FromDInt((int)rounded),
            PlcDataType.Time => FromTimeMs((int)rounded),
            PlcDataType.DWord => FromDWord((uint)rounded),
            PlcDataType.UDInt => FromUDInt((uint)rounded),
            PlcDataType.Bcd32 => FromDWord((uint)rounded),
            _ => FromInt((short)rounded)
        };
        return true;
    }

    public static PlcValue FromRawBits(uint rawBits, PlcDataType target) => target switch
    {
        PlcDataType.Bool => FromBool((rawBits & 1u) != 0),
        PlcDataType.Byte => FromByte((byte)(rawBits & 0xFF)),
        PlcDataType.Char => FromChar((char)(rawBits & 0xFF)),
        PlcDataType.USInt => FromUSInt((byte)(rawBits & 0xFF)),
        PlcDataType.SInt => FromSInt(unchecked((sbyte)(rawBits & 0xFF))),
        PlcDataType.Word => FromWord((ushort)(rawBits & 0xFFFF)),
        PlcDataType.UInt => FromUInt((ushort)(rawBits & 0xFFFF)),
        PlcDataType.Bcd16 => FromWord((ushort)(rawBits & 0xFFFF)),
        PlcDataType.Int => FromInt(unchecked((short)(rawBits & 0xFFFF))),
        PlcDataType.DWord => FromDWord(rawBits),
        PlcDataType.UDInt => FromUDInt(rawBits),
        PlcDataType.Bcd32 => FromDWord(rawBits),
        PlcDataType.DInt => FromDInt(unchecked((int)rawBits)),
        PlcDataType.Time => FromTimeMs(unchecked((int)rawBits)),
        PlcDataType.Real => FromReal(BitConverter.Int32BitsToSingle(unchecked((int)rawBits))),
        PlcDataType.LReal => FromLReal(BitConverter.Int32BitsToSingle(unchecked((int)rawBits))),
        _ => FromInt(unchecked((short)(rawBits & 0xFFFF)))
    };

    public static PlcValue FromRawBits64(ulong rawBits, PlcDataType target) =>
        target == PlcDataType.LReal
            ? FromLReal(BitConverter.Int64BitsToDouble(unchecked((long)rawBits)))
            : FromRawBits((uint)rawBits, target);

    public bool TryConvertTo(PlcDataType target, out PlcValue converted)
    {
        if (DataType == target)
        {
            converted = this;
            return IsValidReal;
        }
        return TryFromDouble(AsDouble, target, out converted);
    }

    public string Format(PlcWatchFormat fmt = PlcWatchFormat.Auto)
    {
        if (DataType == PlcDataType.String) return $"'{_strVal ?? string.Empty}'";
        if (DataType == PlcDataType.DTL) return _dtlVal.FormatIso();
        if (DataType == PlcDataType.Char && fmt == PlcWatchFormat.Auto)
        {
            char c = (char)(_intBits & 0xFF);
            return char.IsControl(c) ? $"16#{(byte)c:X2}" : $"'{c}'";
        }

        PlcWatchFormat eff = fmt == PlcWatchFormat.Auto
            ? DataType switch
            {
                PlcDataType.Bool => PlcWatchFormat.Bool,
                PlcDataType.Real or PlcDataType.LReal => PlcWatchFormat.Float,
                PlcDataType.Time => PlcWatchFormat.Time,
                PlcDataType.Byte or PlcDataType.Word or PlcDataType.DWord or PlcDataType.Bcd16 or PlcDataType.Bcd32 => PlcWatchFormat.Hex,
                _ => PlcWatchFormat.Dec
            }
            : fmt;

        int bitWidth = PlcDataTypeHelper.BitSize(DataType);
        uint raw = AsRawUInt32;

        return eff switch
        {
            PlcWatchFormat.Bool => AsBool ? "TRUE (1)" : "FALSE (0)",
            PlcWatchFormat.Float => AsDouble.ToString("0.####", CultureInfo.InvariantCulture),
            PlcWatchFormat.Time => OperandResolver.FormatTimeMs(AsTimeMs),
            PlcWatchFormat.Hex => bitWidth switch
            {
                1 => AsBool ? "16#1" : "16#0",
                8 => $"16#{raw & 0xFF:X2}",
                16 => $"16#{raw & 0xFFFF:X4}",
                _ => $"16#{raw:X8}"
            },
            PlcWatchFormat.Bin => bitWidth switch
            {
                1 => AsBool ? "2#1" : "2#0",
                8 => $"2#{Convert.ToString(raw & 0xFF, 2).PadLeft(8, '0')}",
                16 => $"2#{FormatGroupedBin(raw & 0xFFFF, 16)}",
                _ => $"2#{FormatGroupedBin(raw, 32)}"
            },
            _ => DataType == PlcDataType.Real
                ? AsDouble.ToString("0.###", CultureInfo.InvariantCulture)
                : AsInt64.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatGroupedBin(uint val, int width)
    {
        string raw = Convert.ToString(val, 2).PadLeft(width, '0');
        var parts = new List<string>(width / 4);
        for (int i = 0; i < raw.Length; i += 4)
            parts.Add(raw.Substring(i, Math.Min(4, raw.Length - i)));
        return string.Join("_", parts);
    }
}

/// <summary>
/// Kết quả phân giải toán hạng dùng chung duy nhất cho cả Compiler (Compile) và RuntimeEngine (Scan Cycle).
/// </summary>
public sealed class ResolvedOperand
{
    public bool IsValid { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public string NormalizedText { get; init; } = string.Empty;

    public PlcMemoryArea Area { get; init; } = PlcMemoryArea.None;
    public bool IsImmediate { get; init; }
    public int ByteOffset { get; init; }
    public int BitOffset { get; init; }
    public int AccessSizeBytes { get; init; } // 0 = Bit, 1 = Byte, 2 = Word, 4 = DWord

    public int DbNumber { get; init; }
    public string DbMember { get; init; } = string.Empty;
    public bool IsDbInstance { get; init; }

    public PlcSliceKind SliceKind { get; init; } = PlcSliceKind.None;
    public int SliceIndex { get; init; }

    public PlcDataType InferredType { get; init; } = PlcDataType.Int;
    public PlcValue? LiteralValue { get; init; }

    public bool IsTimeLiteral => Area == PlcMemoryArea.Literal && InferredType == PlcDataType.Time;
    public bool IsLiteral => Area == PlcMemoryArea.Literal;
    public bool IsBitAccess => SliceKind == PlcSliceKind.Bit
        || (!IsDbInstance && Area is PlcMemoryArea.I or PlcMemoryArea.Q or PlcMemoryArea.M or PlcMemoryArea.DB && AccessSizeBytes == 0)
        || (Area == PlcMemoryArea.DbMember && DbMember is "IN" or "Q" or "CLK" or "CU" or "CD" or "R" or "LD" or "QU" or "QD")
        || (Area == PlcMemoryArea.Literal && InferredType == PlcDataType.Bool);

    public bool IsNumericAccess => !IsBitAccess && !IsDbInstance && Area != PlcMemoryArea.None;

    public bool IsReadable { get; init; }
    public bool IsWritable { get; init; }

    public string ResolvedTagName { get; init; } = string.Empty;
    public bool IsSystemOrClockBit { get; init; }
    public string SystemClockKey { get; init; } = string.Empty;

    public static ResolvedOperand Invalid(string raw, string error) => new()
    {
        IsValid = false,
        RawText = raw ?? string.Empty,
        ErrorMessage = error
    };
}

/// <summary>
/// Bộ phân giải toán hạng dùng chung duy nhất (Compile + Runtime) theo chuẩn IEC 61131-3:
/// - Hằng số thời gian: T#500ms, T#3s, T#10S, T#1m30s, T#2h, TIME#... (lưu ms 32-bit có dấu).
/// - Hằng số hệ cơ số: thập phân, 2#..., 8#..., 16#..., B#16#..., W#16#..., DW#16#..., TRUE/FALSE.
/// - Địa chỉ tuyệt đối: %I, %Q, %M, %IB/%QB/%MB, %IW/%QW/%MW, %ID/%QD/%MD, %DBn.DBX/DBB/DBW/DBD, hậu tố :P.
/// - Truy xuất thành viên DB ("T1".Q, "T1".ET, "C1".CV...) và Slice Access ("Tag".x0..x31, .b0..b3, .w0..w1).
/// - Biến hệ thống &amp; xung nhịp vuông toàn cục ("FirstScan", "AlwaysTRUE", "AlwaysFALSE", "Clock_10Hz".."Clock_0.5Hz").
/// </summary>
public static partial class OperandResolver
{
    public const int MaxMemoryBytes = 200; // %MB0..%MB199 (%M0.0..%M199.7, %MW0..%MW198, %MD0..%MD196)
    public const int MaxDbCount = 99;      // %DB1..%DB99
    public const int MaxDbBytes = 256;     // %DBn.DBB0..255

    [GeneratedRegex(@"^(?:TIME|T)#(?<neg>-)?(?<body>[0-9A-Za-z_.]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimePrefixRegex();

    [GeneratedRegex(@"(?<val>\d+(?:\.\d+)?)(?<unit>MS|D|H|M|S)", RegexOptions.IgnoreCase)]
    private static partial Regex TimeSegmentRegex();

    [GeneratedRegex(@"^(?:(?<prefix>B|W|DW)#)?(?<base>2|8|16)#(?<digits>[0-9A-Fa-f_]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RadixLiteralRegex();

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<byte>\d+)\.(?<bit>[0-7])(?<imm>:P)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BitAddrRegex();

    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<size>B|W|D)(?<offset>\d+)(?<imm>:P)?(?:\.(?<slice>[XBW])(?<sidx>\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SizedAddrRegex();

    [GeneratedRegex(@"^%?DB(?<num>\d+)\.DB(?<size>X|B|W|D)(?<offset>\d+)(?:\.(?<bit>[0-7]))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DbAbsoluteRegex();

    [GeneratedRegex(@"^%?DB(?<num>\d+)(?:\.(?<field>IN|Q|CLK|PT|ET|CU|CD|R|LD|QU|QD|PV|CV))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DbMemberRegex();

    [GeneratedRegex(@"^(?<slice>[XBW])(?<idx>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SliceSuffixRegex();

    /// <summary>
    /// Phân tích hằng số thời gian chuẩn IEC (T#500ms, T#3s, T#10S, T#1m30s, T#2h, TIME#1d_2h_30s...) ra tổng mili-giây có dấu (32-bit).
    /// </summary>
    public static bool TryParseTimeMs(string? raw, out int totalMs)
    {
        totalMs = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var pm = TimePrefixRegex().Match(raw.Trim());
        if (!pm.Success) return false;

        bool isNeg = pm.Groups["neg"].Success;
        string body = pm.Groups["body"].Value.Replace("_", "", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(body)) return false;

        var matches = TimeSegmentRegex().Matches(body);
        if (matches.Count == 0) return false;

        // Xác nhận toàn bộ chuỗi body khớp kín các đoạn số+đơn vị (không chứa ký tự rác)
        int consumed = 0;
        double sumMs = 0.0;
        foreach (Match m in matches)
        {
            if (m.Index != consumed) return false;
            consumed = m.Index + m.Length;
            if (!double.TryParse(m.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return false;

            string unit = m.Groups["unit"].Value.ToUpperInvariant();
            sumMs += unit switch
            {
                "D" => v * 86_400_000.0,
                "H" => v * 3_600_000.0,
                "M" => v * 60_000.0,
                "S" => v * 1_000.0,
                "MS" => v,
                _ => 0.0
            };
        }

        if (consumed != body.Length) return false;
        if (isNeg) sumMs = -sumMs;
        if (sumMs < int.MinValue || sumMs > int.MaxValue) return false;

        totalMs = (int)Math.Round(sumMs);
        return true;
    }

    public static bool TryParseTimeSeconds(string? raw, out double seconds)
    {
        if (TryParseTimeMs(raw, out int ms))
        {
            seconds = ms / 1000.0;
            return true;
        }
        seconds = 0.0;
        return false;
    }

    public static string FormatTimeMs(int ms)
    {
        if (ms == 0) return "T#0ms";
        bool neg = ms < 0;
        long abs = Math.Abs((long)ms);
        string sign = neg ? "-" : "";

        if (abs < 1000) return $"T#{sign}{abs}ms";
        if (abs % 60_000 == 0 && abs >= 60_000)
        {
            long mins = abs / 60_000;
            if (mins % 60 == 0 && mins >= 60)
                return $"T#{sign}{mins / 60}h";
            return $"T#{sign}{mins}m";
        }
        if (abs >= 60_000)
        {
            long mins = abs / 60_000;
            long remMs = abs % 60_000;
            long secs = remMs / 1000;
            long msPart = remMs % 1000;
            if (msPart == 0) return $"T#{sign}{mins}m{secs}s";
            return $"T#{sign}{mins}m{secs}s{msPart}ms";
        }
        if (abs % 1000 == 0) return $"T#{sign}{abs / 1000}s";
        return $"T#{sign}{abs / 1000.0:0.###}s";
    }

    /// <summary>
    /// Phân tích hằng số (Time, 2#, 8#, 16#, W#16#, DW#16#, số thập phân, TRUE/FALSE).
    /// </summary>
    public static bool TryParseLiteral(string? raw, out PlcValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim();

        // 0. String / Char literal ('ABC', 'A') hoặc DTL literal (DTL#2026-11-26-12:00:00)
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
        {
            string inner = s[1..^1];
            value = inner.Length == 1 ? PlcValue.FromChar(inner[0]) : PlcValue.FromString(inner);
            return true;
        }
        if (s.StartsWith("DTL#", StringComparison.OrdinalIgnoreCase) && PlcDtlValue.TryParse(s, out var dtlVal))
        {
            value = PlcValue.FromDtl(dtlVal);
            return true;
        }

        // 1. Boolean literal
        if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = PlcValue.FromBool(true);
            return true;
        }
        if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            value = PlcValue.FromBool(false);
            return true;
        }

        // 2. Time literal (T#500ms, T#3s, TIME#1m30s...)
        if (TryParseTimeMs(s, out int timeMs))
        {
            value = PlcValue.FromTimeMs(timeMs);
            return true;
        }

        // 3. Radix literal (2#..., 8#..., 16#..., B#16#..., W#16#..., DW#16#...)
        var rm = RadixLiteralRegex().Match(s);
        if (rm.Success)
        {
            string prefix = rm.Groups["prefix"].Value.ToUpperInvariant();
            int radix = int.Parse(rm.Groups["base"].Value, CultureInfo.InvariantCulture);
            string digits = rm.Groups["digits"].Value.Replace("_", "", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(digits)) return false;

            try
            {
                ulong parsed = Convert.ToUInt64(digits, radix);
                if (parsed > uint.MaxValue) return false;

                if (prefix == "B")
                {
                    if (parsed > byte.MaxValue) return false;
                    value = PlcValue.FromByte((byte)parsed);
                    return true;
                }
                if (prefix == "W")
                {
                    if (parsed > ushort.MaxValue) return false;
                    value = PlcValue.FromWord((ushort)parsed);
                    return true;
                }
                if (prefix == "DW" || parsed > ushort.MaxValue)
                {
                    value = PlcValue.FromDWord((uint)parsed);
                    return true;
                }
                if (parsed <= byte.MaxValue)
                    value = PlcValue.FromByte((byte)parsed);
                else
                    value = PlcValue.FromWord((ushort)parsed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 4. Decimal integer or floating-point literal (optional underscores)
        string decClean = s.Replace("_", "", StringComparison.Ordinal);
        if (long.TryParse(decClean, NumberStyles.Integer, CultureInfo.InvariantCulture, out long intLit))
        {
            if (intLit >= short.MinValue && intLit <= short.MaxValue)
                value = PlcValue.FromInt((short)intLit);
            else if (intLit >= int.MinValue && intLit <= int.MaxValue)
                value = PlcValue.FromDInt((int)intLit);
            else if (intLit >= 0 && intLit <= uint.MaxValue)
                value = PlcValue.FromUDInt((uint)intLit);
            else
                return false;
            return true;
        }

        if (double.TryParse(decClean, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblLit) &&
            !double.IsNaN(dblLit) && !double.IsInfinity(dblLit) &&
            Math.Abs(dblLit) <= float.MaxValue)
        {
            value = PlcValue.FromReal((float)dblLit);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Bộ phân giải toán hạng duy nhất cho cả Compiler và RuntimeEngine.
    /// </summary>
    public static ResolvedOperand Resolve(string? raw, PlcProgramModel? program)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ResolvedOperand.Invalid(raw ?? string.Empty, LocalizationManager.Get("Str.Plc.ErrEmptyAddr"));

        string trimmed = raw.Trim();
        if (trimmed == "<???>")
            return ResolvedOperand.Invalid(trimmed, LocalizationManager.Get("Str.Plc.ErrEmptyAddr"));

        // 1. Thử phân giải hằng số (Time, 2#/8#/16#, Decimal, TRUE/FALSE)
        if (TryParseLiteral(trimmed, out var litVal))
        {
            return new ResolvedOperand
            {
                IsValid = true,
                RawText = trimmed,
                NormalizedText = litVal.DataType == PlcDataType.Time ? FormatTimeMs(litVal.AsTimeMs) : trimmed,
                Area = PlcMemoryArea.Literal,
                InferredType = litVal.DataType,
                LiteralValue = litVal,
                IsReadable = true,
                IsWritable = false
            };
        }

        // 2. Thử phân giải địa chỉ tuyệt đối trực tiếp (%I0.0, %Q0.0:P, %MW2, %MW2.x3, %DB1.DBW0, %DB1.Q)
        if (TryResolveAbsolute(trimmed, out var absResult))
        {
            if (absResult.IsValid && program is not null &&
                program.IsAddressInSystemOrClockMemory(absResult.Area, absResult.ByteOffset, absResult.BitOffset, absResult.AccessSizeBytes))
            {
                string sysName = program.ResolveTagName(absResult.NormalizedText);
                return new ResolvedOperand
                {
                    IsValid = true,
                    RawText = absResult.RawText,
                    NormalizedText = absResult.NormalizedText,
                    Area = absResult.Area,
                    IsImmediate = absResult.IsImmediate,
                    ByteOffset = absResult.ByteOffset,
                    BitOffset = absResult.BitOffset,
                    AccessSizeBytes = absResult.AccessSizeBytes,
                    SliceKind = absResult.SliceKind,
                    SliceIndex = absResult.SliceIndex,
                    InferredType = absResult.InferredType,
                    IsReadable = absResult.IsReadable,
                    IsWritable = false,
                    ResolvedTagName = sysName,
                    IsSystemOrClockBit = true,
                    SystemClockKey = sysName
                };
            }
            return absResult;
        }

        // 3. Xử lý hậu tố :P trên ký hiệu (VD: "START:P" hoặc "START":P hoặc START:P)
        bool symbolicImm = false;
        string symWork = trimmed;
        if (symWork.EndsWith(":P", StringComparison.OrdinalIgnoreCase))
        {
            symbolicImm = true;
            symWork = symWork[..^2].Trim();
        }
        else if (symWork.Length >= 4 && symWork[0] == '"' && symWork[^1] == '"' &&
                 symWork[1..^1].EndsWith(":P", StringComparison.OrdinalIgnoreCase))
        {
            symbolicImm = true;
            symWork = symWork[1..^3].Trim();
        }

        // 4. Phân tách dấu chấm: Truy xuất thành viên DB ("T1".Q, "T1".ET, "C1".CV) HOẶC Slice Access ("TagName".x0/.b0/.w0)
        int lastDot = symWork.LastIndexOf('.');
        if (lastDot > 0 && lastDot < symWork.Length - 1)
        {
            string leftRaw = PlcAddressHelper.StripQuotes(symWork[..lastDot]);
            string rightRaw = symWork[(lastDot + 1)..].Trim();

            // 4A. Kiểm tra Slice Access (.x0..x31, .b0..b3, .w0..w1)
            var sliceMatch = SliceSuffixRegex().Match(rightRaw);
            if (sliceMatch.Success && program is not null)
            {
                char sliceChar = char.ToUpperInvariant(sliceMatch.Groups["slice"].Value[0]);
                int sliceIdx = int.Parse(sliceMatch.Groups["idx"].Value, CultureInfo.InvariantCulture);
                var baseTag = FindTagByName(program, leftRaw);
                if (baseTag is not null)
                {
                    return BuildSlicedOperand(trimmed, baseTag, sliceChar, sliceIdx, symbolicImm);
                }
            }

            // 4B. Kiểm tra truy xuất thành viên Data Block ("T1".Q, "T1".ET, "C1".CV...)
            if (program is not null)
            {
                var db = program.GetAllDataBlocks().FirstOrDefault(d =>
                    d.Name.Equals(leftRaw, StringComparison.OrdinalIgnoreCase));
                if (db is not null)
                {
                    string member = rightRaw.ToUpperInvariant();
                    string dbAbs = $"{PlcProgramModel.NormalizeAddress(db.Address)}.{member}";
                    if (TryResolveAbsolute(dbAbs, out var dbMemberRes))
                    {
                        return new ResolvedOperand
                        {
                            IsValid = dbMemberRes.IsValid,
                            ErrorMessage = dbMemberRes.ErrorMessage,
                            RawText = trimmed,
                            NormalizedText = dbMemberRes.NormalizedText,
                            Area = dbMemberRes.Area,
                            DbNumber = dbMemberRes.DbNumber,
                            DbMember = dbMemberRes.DbMember,
                            InferredType = dbMemberRes.InferredType,
                            IsReadable = dbMemberRes.IsReadable,
                            IsWritable = dbMemberRes.IsWritable,
                            ResolvedTagName = $"\"{db.Name}\".{member}"
                        };
                    }
                }
            }
        }

        // 5. Tra cứu System & Clock Memory + Bảng PLC Tags theo tên ký hiệu ("FirstScan", "Clock_1Hz", "START"...)
        if (program is not null)
        {
            string cleanName = PlcAddressHelper.StripQuotes(symWork);

            // 5A. System & Clock Memory (nếu đang bật)
            var sysTag = program.GetSystemAndClockTags().FirstOrDefault(t =>
                t.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (sysTag is not null)
            {
                string addrToResolve = symbolicImm ? sysTag.Address + ":P" : sysTag.Address;
                if (TryResolveAbsolute(addrToResolve, out var sysAbs))
                {
                    return new ResolvedOperand
                    {
                        IsValid = sysAbs.IsValid,
                        ErrorMessage = sysAbs.ErrorMessage,
                        RawText = trimmed,
                        NormalizedText = sysAbs.NormalizedText,
                        Area = sysAbs.Area,
                        IsImmediate = sysAbs.IsImmediate,
                        ByteOffset = sysAbs.ByteOffset,
                        BitOffset = sysAbs.BitOffset,
                        AccessSizeBytes = sysAbs.AccessSizeBytes,
                        InferredType = PlcDataType.Bool,
                        IsReadable = true,
                        IsWritable = false, // Tránh ghi đè nhầm bit hệ thống / xung nhịp
                        ResolvedTagName = sysTag.Name,
                        IsSystemOrClockBit = true,
                        SystemClockKey = sysTag.Name
                    };
                }
            }

            // 5B. PLC Tag do người dùng định nghĩa
            var tag = FindTagByName(program, cleanName);
            if (tag is not null && !string.IsNullOrWhiteSpace(tag.Address))
            {
                string targetAddr = symbolicImm ? tag.Address.Trim() + ":P" : tag.Address.Trim();
                if (TryResolveAbsolute(targetAddr, out var tagAbs))
                {
                    PlcDataType declaredDt = PlcDataTypeHelper.TryParse(tag.DataType, out var parsedDt)
                        ? parsedDt
                        : tagAbs.InferredType;
                    bool inSysClk = tagAbs.IsValid &&
                        program.IsAddressInSystemOrClockMemory(tagAbs.Area, tagAbs.ByteOffset, tagAbs.BitOffset, tagAbs.AccessSizeBytes);

                    return new ResolvedOperand
                    {
                        IsValid = tagAbs.IsValid,
                        ErrorMessage = tagAbs.ErrorMessage,
                        RawText = trimmed,
                        NormalizedText = tagAbs.NormalizedText,
                        Area = tagAbs.Area,
                        IsImmediate = tagAbs.IsImmediate,
                        ByteOffset = tagAbs.ByteOffset,
                        BitOffset = tagAbs.BitOffset,
                        AccessSizeBytes = tagAbs.AccessSizeBytes,
                        DbNumber = tagAbs.DbNumber,
                        DbMember = tagAbs.DbMember,
                        IsDbInstance = tagAbs.IsDbInstance,
                        InferredType = declaredDt,
                        IsReadable = tagAbs.IsReadable,
                        IsWritable = tagAbs.IsWritable && !inSysClk,
                        ResolvedTagName = tag.Name,
                        IsSystemOrClockBit = inSysClk
                    };
                }
            }

            // 5C. Tên Data Block không có dấu chấm ("TIMER1")
            var dbOnly = program.GetAllDataBlocks().FirstOrDefault(d =>
                d.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (dbOnly is not null && TryResolveAbsolute(dbOnly.Address, out var dbOnlyRes))
            {
                return new ResolvedOperand
                {
                    IsValid = dbOnlyRes.IsValid,
                    ErrorMessage = dbOnlyRes.ErrorMessage,
                    RawText = trimmed,
                    NormalizedText = dbOnlyRes.NormalizedText,
                    Area = dbOnlyRes.Area,
                    DbNumber = dbOnlyRes.DbNumber,
                    IsDbInstance = dbOnlyRes.IsDbInstance,
                    InferredType = dbOnlyRes.InferredType,
                    IsReadable = dbOnlyRes.IsReadable,
                    IsWritable = dbOnlyRes.IsWritable,
                    ResolvedTagName = dbOnly.Name
                };
            }
        }

        return ResolvedOperand.Invalid(trimmed, LocalizationManager.Format("Str.Plc.ErrInvalidAddr", trimmed));
    }

    private static PlcTagEntry? FindTagByName(PlcProgramModel program, string name) =>
        program.Tags.FirstOrDefault(t =>
            !string.IsNullOrWhiteSpace(t.Name) &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(t.Address));

    private static ResolvedOperand BuildSlicedOperand(
        string rawText,
        PlcTagEntry baseTag,
        char sliceChar,
        int sliceIdx,
        bool isImmediate)
    {
        if (!TryResolveAbsolute(baseTag.Address, out var baseAbs) || !baseAbs.IsValid)
            return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrInvalidAddr", rawText));

        PlcDataType baseDt = PlcDataTypeHelper.TryParse(baseTag.DataType, out var dt) ? dt : baseAbs.InferredType;
        if (!PlcDataTypeHelper.CanSlice(baseDt) || baseAbs.AccessSizeBytes <= 0)
            return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrSliceNotSupported", rawText, baseTag.DataType));

        int totalBits = baseAbs.AccessSizeBytes * 8;
        int totalBytes = baseAbs.AccessSizeBytes;
        int totalWords = baseAbs.AccessSizeBytes / 2;

        PlcSliceKind sk;
        PlcDataType sliceDt;
        switch (sliceChar)
        {
            case 'X':
                if (sliceIdx < 0 || sliceIdx >= totalBits)
                    return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", rawText, $"x0..x{totalBits - 1}"));
                sk = PlcSliceKind.Bit;
                sliceDt = PlcDataType.Bool;
                break;
            case 'B':
                if (sliceIdx < 0 || sliceIdx >= totalBytes)
                    return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", rawText, $"b0..b{totalBytes - 1}"));
                sk = PlcSliceKind.Byte;
                sliceDt = PlcDataType.Byte;
                break;
            case 'W':
                if (totalWords == 0 || sliceIdx < 0 || sliceIdx >= totalWords)
                    return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", rawText, totalWords > 0 ? $"w0..w{totalWords - 1}" : "N/A"));
                sk = PlcSliceKind.Word;
                sliceDt = PlcDataType.Word;
                break;
            default:
                return ResolvedOperand.Invalid(rawText, LocalizationManager.Format("Str.Plc.ErrInvalidAddr", rawText));
        }

        string normSlice = $"{baseAbs.NormalizedText}.{char.ToLowerInvariant(sliceChar)}{sliceIdx}";
        return new ResolvedOperand
        {
            IsValid = true,
            RawText = rawText,
            NormalizedText = normSlice,
            Area = baseAbs.Area,
            IsImmediate = isImmediate || baseAbs.IsImmediate,
            ByteOffset = baseAbs.ByteOffset,
            BitOffset = baseAbs.BitOffset,
            AccessSizeBytes = baseAbs.AccessSizeBytes,
            DbNumber = baseAbs.DbNumber,
            SliceKind = sk,
            SliceIndex = sliceIdx,
            InferredType = sliceDt,
            IsReadable = baseAbs.IsReadable,
            IsWritable = baseAbs.IsWritable,
            ResolvedTagName = $"\"{baseTag.Name}\".{char.ToLowerInvariant(sliceChar)}{sliceIdx}"
        };
    }

    public static bool TryResolveAbsolute(string raw, out ResolvedOperand result)
    {
        result = null!;
        string s = raw.Trim();

        // A. Địa chỉ DB tuyệt đối: %DB1.DBX0.0, %DB1.DBB0, %DB1.DBW0, %DB1.DBD0
        var dbAbs = DbAbsoluteRegex().Match(s);
        if (dbAbs.Success)
        {
            int dbNum = int.Parse(dbAbs.Groups["num"].Value, CultureInfo.InvariantCulture);
            string sz = dbAbs.Groups["size"].Value.ToUpperInvariant();
            int off = int.Parse(dbAbs.Groups["offset"].Value, CultureInfo.InvariantCulture);
            if (dbNum < 1 || dbNum > MaxDbCount)
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%DB1–%DB99"));
                return true;
            }

            if (sz == "X")
            {
                if (!dbAbs.Groups["bit"].Success)
                {
                    result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw));
                    return true;
                }
                int bit = int.Parse(dbAbs.Groups["bit"].Value, CultureInfo.InvariantCulture);
                if (off < 0 || off >= MaxDbBytes)
                {
                    result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, $"%DB{dbNum}.DBX0.0–{MaxDbBytes - 1}.7"));
                    return true;
                }
                result = new ResolvedOperand
                {
                    IsValid = true,
                    RawText = raw,
                    NormalizedText = $"%DB{dbNum}.DBX{off}.{bit}",
                    Area = PlcMemoryArea.DB,
                    DbNumber = dbNum,
                    ByteOffset = off,
                    BitOffset = bit,
                    AccessSizeBytes = 0,
                    InferredType = PlcDataType.Bool,
                    IsReadable = true,
                    IsWritable = true
                };
                return true;
            }

            if (dbAbs.Groups["bit"].Success)
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw));
                return true;
            }

            int byteSize = sz switch { "B" => 1, "W" => 2, _ => 4 };
            if (off < 0 || off + byteSize > MaxDbBytes)
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, $"%DB{dbNum}.DB{sz}0–{MaxDbBytes - byteSize}"));
                return true;
            }
            PlcDataType dt = sz switch { "B" => PlcDataType.Byte, "W" => PlcDataType.Int, _ => PlcDataType.DInt };
            result = new ResolvedOperand
            {
                IsValid = true,
                RawText = raw,
                NormalizedText = $"%DB{dbNum}.DB{sz}{off}",
                Area = PlcMemoryArea.DB,
                DbNumber = dbNum,
                ByteOffset = off,
                AccessSizeBytes = byteSize,
                InferredType = dt,
                IsReadable = true,
                IsWritable = true
            };
            return true;
        }

        // B. Địa chỉ DB instance hoặc DB Member: %DB1, %DB1.Q, %DB1.ET, %DB1.CV...
        var dbMem = DbMemberRegex().Match(s);
        if (dbMem.Success)
        {
            int dbNum = int.Parse(dbMem.Groups["num"].Value, CultureInfo.InvariantCulture);
            if (dbNum < 1 || dbNum > MaxDbCount)
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%DB1–%DB99"));
                return true;
            }
            string field = dbMem.Groups["field"].Success ? dbMem.Groups["field"].Value.ToUpperInvariant() : string.Empty;
            if (string.IsNullOrEmpty(field))
            {
                result = new ResolvedOperand
                {
                    IsValid = true,
                    RawText = raw,
                    NormalizedText = $"%DB{dbNum}",
                    Area = PlcMemoryArea.DB,
                    DbNumber = dbNum,
                    IsDbInstance = true,
                    AccessSizeBytes = 0,
                    InferredType = PlcDataType.DInt,
                    IsReadable = true,
                    IsWritable = false
                };
                return true;
            }

            PlcDataType mDt = field switch
            {
                "IN" or "Q" or "CLK" or "CU" or "CD" or "R" or "LD" or "QU" or "QD" => PlcDataType.Bool,
                "PT" or "ET" => PlcDataType.Time,
                _ => PlcDataType.Int // CV, PV
            };

            result = new ResolvedOperand
            {
                IsValid = true,
                RawText = raw,
                NormalizedText = $"%DB{dbNum}.{field}",
                Area = PlcMemoryArea.DbMember,
                DbNumber = dbNum,
                DbMember = field,
                InferredType = mDt,
                IsReadable = true,
                IsWritable = field is "PT" or "PV" // Chỉ cho ghi override PT/PV qua Watch Table nếu cần; đầu ra Q/ET/CV là read-only trong logic
            };
            return true;
        }

        // C. Địa chỉ Bit tuyệt đối: %I0.0, %Q0.0, %M0.0 (+ tùy chọn :P)
        var bm = BitAddrRegex().Match(s);
        if (bm.Success)
        {
            string areaStr = bm.Groups["area"].Value.ToUpperInvariant();
            int byteIdx = int.Parse(bm.Groups["byte"].Value, CultureInfo.InvariantCulture);
            int bitIdx = int.Parse(bm.Groups["bit"].Value, CultureInfo.InvariantCulture);
            bool imm = bm.Groups["imm"].Success;

            if (imm && areaStr == "M")
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrImmediateOnlyIO", raw));
                return true;
            }

            switch (areaStr)
            {
                case "I":
                    if (!((byteIdx == 0 && bitIdx <= 7) || (byteIdx == 1 && bitIdx <= 5)))
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%I0.0–%I1.5"));
                        return true;
                    }
                    result = new ResolvedOperand
                    {
                        IsValid = true,
                        RawText = raw,
                        NormalizedText = $"%I{byteIdx}.{bitIdx}{(imm ? ":P" : "")}",
                        Area = PlcMemoryArea.I,
                        IsImmediate = imm,
                        ByteOffset = byteIdx,
                        BitOffset = bitIdx,
                        AccessSizeBytes = 0,
                        InferredType = PlcDataType.Bool,
                        IsReadable = true,
                        IsWritable = false
                    };
                    return true;

                case "Q":
                    if (!((byteIdx == 0 && bitIdx <= 7) || (byteIdx == 1 && bitIdx <= 1)))
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%Q0.0–%Q1.1"));
                        return true;
                    }
                    result = new ResolvedOperand
                    {
                        IsValid = true,
                        RawText = raw,
                        NormalizedText = $"%Q{byteIdx}.{bitIdx}{(imm ? ":P" : "")}",
                        Area = PlcMemoryArea.Q,
                        IsImmediate = imm,
                        ByteOffset = byteIdx,
                        BitOffset = bitIdx,
                        AccessSizeBytes = 0,
                        InferredType = PlcDataType.Bool,
                        IsReadable = !imm, // Chuẩn IEC: %Q...:P là write-only
                        IsWritable = true
                    };
                    return true;

                case "M":
                    if (byteIdx < 0 || byteIdx >= MaxMemoryBytes)
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, $"%M0.0–%M{MaxMemoryBytes - 1}.7"));
                        return true;
                    }
                    result = new ResolvedOperand
                    {
                        IsValid = true,
                        RawText = raw,
                        NormalizedText = $"%M{byteIdx}.{bitIdx}",
                        Area = PlcMemoryArea.M,
                        ByteOffset = byteIdx,
                        BitOffset = bitIdx,
                        AccessSizeBytes = 0,
                        InferredType = PlcDataType.Bool,
                        IsReadable = true,
                        IsWritable = true
                    };
                    return true;
            }
        }

        // D. Địa chỉ Byte/Word/DWord tuyệt đối: %IB0, %QB0, %MB0, %IW64, %QW0, %MW0, %ID0, %QD0, %MD0 (+ tùy chọn :P và Slice .x/.b/.w)
        var sm = SizedAddrRegex().Match(s);
        if (sm.Success)
        {
            string areaStr = sm.Groups["area"].Value.ToUpperInvariant();
            string sizeStr = sm.Groups["size"].Value.ToUpperInvariant();
            int offset = int.Parse(sm.Groups["offset"].Value, CultureInfo.InvariantCulture);
            bool imm = sm.Groups["imm"].Success;

            if (imm && areaStr == "M")
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrImmediateOnlyIO", raw));
                return true;
            }

            int byteSize = sizeStr switch { "B" => 1, "W" => 2, _ => 4 };
            PlcMemoryArea area = areaStr switch { "I" => PlcMemoryArea.I, "Q" => PlcMemoryArea.Q, _ => PlcMemoryArea.M };
            PlcDataType dt = sizeStr switch { "B" => PlcDataType.Byte, "W" => PlcDataType.Int, _ => areaStr == "M" ? PlcDataType.DInt : PlcDataType.DWord };

            // Kiểm tra giới hạn phần cứng theo từng vùng nhớ (+ %QW1000..%QW1006 PWM và %ID1000..%ID1020 HSC chuẩn Part 2)
            bool isPwmAddr = areaStr == "Q" && sizeStr == "W" && offset is 1000 or 1002 or 1004 or 1006;
            bool isHscAddr = areaStr == "I" && sizeStr == "D" && offset is 1000 or 1004 or 1008 or 1012 or 1016 or 1020;
            string? rangeErr = (areaStr, sizeStr) switch
            {
                ("I", "B") when offset > 1 => "%IB0–%IB1",
                ("Q", "B") when offset > 1 => "%QB0–%QB1",
                ("M", "B") when offset >= MaxMemoryBytes => $"%MB0–%MB{MaxMemoryBytes - 1}",
                ("I", "W") when offset is not (0 or 64 or 66) => "%IW0, %IW64, %IW66",
                ("Q", "W") when !isPwmAddr && offset > 18 => "%QW0–%QW18, %QW1000–%QW1006",
                ("M", "W") when offset > MaxMemoryBytes - 2 => $"%MW0–%MW{MaxMemoryBytes - 2}",
                ("I", "D") when !isHscAddr && offset > 68 => "%ID0–%ID68, %ID1000–%ID1020",
                ("Q", "D") when offset > 16 => "%QD0–%QD16",
                ("M", "D") when offset > MaxMemoryBytes - 4 => $"%MD0–%MD{MaxMemoryBytes - 4}",
                _ => null
            };

            if (rangeErr is not null)
            {
                result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, rangeErr));
                return true;
            }

            PlcSliceKind sliceKind = PlcSliceKind.None;
            int sliceIdx = 0;
            string sliceNorm = string.Empty;
            if (sm.Groups["slice"].Success)
            {
                char sc = char.ToUpperInvariant(sm.Groups["slice"].Value[0]);
                sliceIdx = int.Parse(sm.Groups["sidx"].Value, CultureInfo.InvariantCulture);
                int maxBits = byteSize * 8;
                int maxWords = byteSize / 2;
                if (sc == 'X')
                {
                    if (sliceIdx < 0 || sliceIdx >= maxBits)
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", raw, $"x0..x{maxBits - 1}"));
                        return true;
                    }
                    sliceKind = PlcSliceKind.Bit;
                    dt = PlcDataType.Bool;
                }
                else if (sc == 'B')
                {
                    if (sliceIdx < 0 || sliceIdx >= byteSize)
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", raw, $"b0..b{byteSize - 1}"));
                        return true;
                    }
                    sliceKind = PlcSliceKind.Byte;
                    dt = PlcDataType.Byte;
                }
                else if (sc == 'W')
                {
                    if (maxWords == 0 || sliceIdx < 0 || sliceIdx >= maxWords)
                    {
                        result = ResolvedOperand.Invalid(raw, LocalizationManager.Format("Str.Plc.ErrSliceOutOfRange", raw, maxWords > 0 ? $"w0..w{maxWords - 1}" : "N/A"));
                        return true;
                    }
                    sliceKind = PlcSliceKind.Word;
                    dt = PlcDataType.Word;
                }
                sliceNorm = $".{char.ToLowerInvariant(sc)}{sliceIdx}";
            }

            result = new ResolvedOperand
            {
                IsValid = true,
                RawText = raw,
                NormalizedText = $"%{areaStr}{sizeStr}{offset}{(imm ? ":P" : "")}{sliceNorm}",
                Area = area,
                IsImmediate = imm,
                ByteOffset = offset,
                AccessSizeBytes = byteSize,
                SliceKind = sliceKind,
                SliceIndex = sliceIdx,
                InferredType = dt,
                IsReadable = !(area == PlcMemoryArea.Q && imm),
                IsWritable = area != PlcMemoryArea.I
            };
            return true;
        }

        return false;
    }
}

/// <summary>
/// Bộ phân tích và tính toán biểu thức toán học chuẩn IEC 61131-3 cho khối CALCULATE:
/// - Hỗ trợ toán tử: +, -, *, /, ** (lũy thừa), ngoặc đơn (...), dấu âm/dương một ngôi.
/// - Hỗ trợ đầy đủ hàm toán học: ABS, NEG, SQR, SQRT, SIN, COS, TAN, ASIN, ACOS, ATAN, LN, EXP, ROUND, CEIL, FLOOR, TRUNC, FRAC.
/// - Tự động nhận diện số lượng chân đầu vào IN1..INn (tối đa IN32).
/// </summary>
public static partial class PlcExpressionEvaluator
{
    public static readonly string[] SupportedFunctions =
    [
        "ABS", "NEG", "SQR", "SQRT", "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN",
        "LN", "EXP", "ROUND", "CEIL", "FLOOR", "TRUNC", "FRAC"
    ];

    [GeneratedRegex(@"\bIN(?<idx>[1-9]\d?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InputRefRegex();

    public static int DetectMaxInputIndex(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 2;
        int maxIdx = 2;
        foreach (Match m in InputRefRegex().Matches(expression))
        {
            if (int.TryParse(m.Groups["idx"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
            {
                if (idx >= 1 && idx <= 32 && idx > maxIdx)
                    maxIdx = idx;
            }
        }
        return maxIdx;
    }

    public static bool TryValidate(string? expression, out int requiredInputs, out string error)
    {
        requiredInputs = 2;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = LocalizationManager.Get("Str.Plc.CalcErrEmpty");
            return false;
        }

        requiredInputs = DetectMaxInputIndex(expression);
        double[] dummy = new double[requiredInputs];
        for (int i = 0; i < dummy.Length; i++) dummy[i] = 1.0 + i * 0.25;

        if (!TryEvaluateSyntaxCheck(expression, dummy, out string syntaxErr))
        {
            error = syntaxErr;
            return false;
        }
        return true;
    }

    public static bool TryEvaluate(string? expression, IReadOnlyList<double> inputs, out double result, bool integerDivision = false)
    {
        result = 0.0;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        string clean = StripLeadingOutAssignment(expression);
        var parser = new Parser(clean, inputs, ignoreDomainErrors: false, integerDivision);
        if (!parser.ParseRoot(out result))
            return false;

        return !double.IsNaN(result) && !double.IsInfinity(result);
    }

    private static bool TryEvaluateSyntaxCheck(string expression, IReadOnlyList<double> inputs, out string error)
    {
        error = string.Empty;
        string clean = StripLeadingOutAssignment(expression);
        var parser = new Parser(clean, inputs, ignoreDomainErrors: true, integerDivision: false);
        if (!parser.ParseRoot(out _))
        {
            error = string.IsNullOrEmpty(parser.Error)
                ? LocalizationManager.Get("Str.Plc.CalcErrSyntax")
                : parser.Error;
            return false;
        }
        return true;
    }

    private static string StripLeadingOutAssignment(string expr)
    {
        string s = expr.Trim();
        int eqIdx = s.IndexOf(":=", StringComparison.Ordinal);
        if (eqIdx > 0 && s[..eqIdx].Trim().Equals("OUT", StringComparison.OrdinalIgnoreCase))
            return s[(eqIdx + 2)..].Trim();

        int singleEq = s.IndexOf('=');
        if (singleEq > 0 && s[..singleEq].Trim().Equals("OUT", StringComparison.OrdinalIgnoreCase))
            return s[(singleEq + 1)..].Trim();

        return s;
    }

    private sealed class Parser
    {
        private readonly string _src;
        private readonly IReadOnlyList<double> _inputs;
        private readonly bool _ignoreDomainErrors;
        private readonly bool _integerDivision;
        private int _pos;

        public string Error { get; private set; } = string.Empty;

        public Parser(string src, IReadOnlyList<double> inputs, bool ignoreDomainErrors, bool integerDivision)
        {
            _src = src.Replace(" ", "", StringComparison.Ordinal).Replace("\t", "", StringComparison.Ordinal).ToUpperInvariant();
            _inputs = inputs;
            _ignoreDomainErrors = ignoreDomainErrors;
            _integerDivision = integerDivision;
        }

        public bool ParseRoot(out double val)
        {
            val = 0.0;
            if (string.IsNullOrEmpty(_src))
            {
                Error = LocalizationManager.Get("Str.Plc.CalcErrEmpty");
                return false;
            }
            if (!ParseAddSub(out val)) return false;
            if (_pos != _src.Length)
            {
                Error = LocalizationManager.Format("Str.Plc.CalcErrUnexpected", _src[_pos..]);
                return false;
            }
            return true;
        }

        private bool ParseAddSub(out double val)
        {
            if (!ParseMulDiv(out val)) return false;
            while (_pos < _src.Length && (_src[_pos] is '+' or '-'))
            {
                char op = _src[_pos++];
                if (!ParseMulDiv(out double rhs)) return false;
                val = op == '+' ? val + rhs : val - rhs;
            }
            return true;
        }

        private bool MatchKeywordOperator(string kw)
        {
            if (_pos + kw.Length > _src.Length) return false;
            if (!MemoryExtensions.Equals(_src.AsSpan(_pos, kw.Length), kw.AsSpan(), StringComparison.Ordinal))
                return false;
            // Đảm bảo ký tự ngay sau từ khóa không phải chữ cái thuộc định danh khác (trừ IN/số/ngoặc)
            if (_pos + kw.Length < _src.Length && _src[_pos + kw.Length] == '_') return false;
            return true;
        }

        private bool ParseMulDiv(out double val)
        {
            if (!ParseUnary(out val)) return false;
            while (_pos < _src.Length)
            {
                char op;
                if (_src[_pos] is '*' or '/')
                {
                    if (_src[_pos] == '*' && _pos + 1 < _src.Length && _src[_pos + 1] == '*')
                        break; // ** belongs to power
                    op = _src[_pos++];
                }
                else if (MatchKeywordOperator("MOD"))
                {
                    _pos += 3;
                    op = '%';
                }
                else
                {
                    break;
                }

                if (!ParseUnary(out double rhs)) return false;
                if (op == '/')
                {
                    if (!_ignoreDomainErrors && rhs == 0.0) return false;
                    val = rhs == 0.0 ? 0.0 : (_integerDivision ? Math.Truncate(val / rhs) : val / rhs);
                }
                else if (op == '%')
                {
                    if (!_ignoreDomainErrors && rhs == 0.0) return false;
                    val = rhs == 0.0 ? 0.0 : Math.Truncate(val) % Math.Truncate(rhs);
                }
                else
                {
                    val *= rhs;
                }
            }
            return true;
        }

        private bool ParseUnary(out double val)
        {
            if (_pos < _src.Length && _src[_pos] == '+')
            {
                _pos++;
                return ParseUnary(out val);
            }
            if (_pos < _src.Length && _src[_pos] == '-')
            {
                _pos++;
                if (!ParseUnary(out val)) return false;
                val = -val;
                return true;
            }
            return ParsePower(out val);
        }

        private bool ParsePower(out double val)
        {
            if (!ParsePrimary(out val)) return false;
            if (_pos + 1 < _src.Length && _src[_pos] == '*' && _src[_pos + 1] == '*')
            {
                _pos += 2;
                if (!ParseUnary(out double exp)) return false;
                val = Math.Pow(val, exp);
                if (!_ignoreDomainErrors && (double.IsNaN(val) || double.IsInfinity(val)))
                    return false;
            }
            return true;
        }

        private bool ParsePrimary(out double val)
        {
            val = 0.0;
            if (_pos >= _src.Length)
            {
                Error = LocalizationManager.Get("Str.Plc.CalcErrSyntax");
                return false;
            }

            if (_src[_pos] == '(')
            {
                _pos++;
                if (!ParseAddSub(out val)) return false;
                if (_pos >= _src.Length || _src[_pos] != ')')
                {
                    Error = LocalizationManager.Get("Str.Plc.CalcErrMissingParen");
                    return false;
                }
                _pos++;
                return true;
            }

            // Identifier: IN1..IN32 hoặc tên hàm toán học
            if (char.IsLetter(_src[_pos]))
            {
                int start = _pos;
                while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                    _pos++;
                string ident = _src[start.._pos];

                if (ident.StartsWith("IN", StringComparison.Ordinal) &&
                    int.TryParse(ident[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inIdx) &&
                    inIdx >= 1 && inIdx <= 32)
                {
                    val = inIdx - 1 < _inputs.Count ? _inputs[inIdx - 1] : 0.0;
                    return true;
                }

                // Hàm 1 tham số: FUNC(expr)
                if (_pos < _src.Length && _src[_pos] == '(')
                {
                    _pos++;
                    if (!ParseAddSub(out double arg)) return false;
                    if (_pos >= _src.Length || _src[_pos] != ')')
                    {
                        Error = LocalizationManager.Get("Str.Plc.CalcErrMissingParen");
                        return false;
                    }
                    _pos++;
                    return EvalFunction(ident, arg, out val);
                }

                Error = LocalizationManager.Format("Str.Plc.CalcErrUnknownIdent", ident);
                return false;
            }

            // Hằng số số học (hỗ trợ cả ký pháp khoa học VD: 1e-3, 2.5E+4)
            int numStart = _pos;
            bool hasDot = false;
            while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || (_src[_pos] == '.' && !hasDot)))
            {
                if (_src[_pos] == '.') hasDot = true;
                _pos++;
            }
            if (_pos > numStart && _pos < _src.Length && _src[_pos] == 'E')
            {
                int expMark = _pos;
                _pos++;
                if (_pos < _src.Length && (_src[_pos] is '+' or '-')) _pos++;
                int expDigitStart = _pos;
                while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
                if (_pos == expDigitStart) _pos = expMark; // Không có chữ số mũ -> lùi lại
            }
            if (_pos > numStart && double.TryParse(_src[numStart.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                return true;

            Error = LocalizationManager.Format("Str.Plc.CalcErrUnexpected", _src[_pos..]);
            return false;
        }

        private bool EvalFunction(string fn, double x, out double res)
        {
            res = 0.0;
            switch (fn)
            {
                case "ABS": res = Math.Abs(x); return true;
                case "NEG": res = -x; return true;
                case "SQR": res = x * x; return true;
                case "SQRT":
                    if (!_ignoreDomainErrors && x < 0.0) return false;
                    res = x >= 0.0 ? Math.Sqrt(x) : 0.0;
                    return true;
                case "SIN": res = Math.Sin(x); return true;
                case "COS": res = Math.Cos(x); return true;
                case "TAN": res = Math.Tan(x); return true;
                case "ASIN":
                    if (!_ignoreDomainErrors && (x < -1.0 || x > 1.0)) return false;
                    res = Math.Asin(Math.Clamp(x, -1.0, 1.0));
                    return true;
                case "ACOS":
                    if (!_ignoreDomainErrors && (x < -1.0 || x > 1.0)) return false;
                    res = Math.Acos(Math.Clamp(x, -1.0, 1.0));
                    return true;
                case "ATAN": res = Math.Atan(x); return true;
                case "LN":
                    if (!_ignoreDomainErrors && x <= 0.0) return false;
                    res = x > 0.0 ? Math.Log(x) : 0.0;
                    return true;
                case "EXP":
                    res = Math.Exp(x);
                    return _ignoreDomainErrors || (!double.IsInfinity(res) && !double.IsNaN(res));
                case "ROUND": res = Math.Round(x, MidpointRounding.ToEven); return true;
                case "CEIL": res = Math.Ceiling(x); return true;
                case "FLOOR": res = Math.Floor(x); return true;
                case "TRUNC": res = Math.Truncate(x); return true;
                case "FRAC": res = x - Math.Truncate(x); return true;
                default:
                    Error = LocalizationManager.Format("Str.Plc.CalcErrUnknownIdent", fn);
                    return false;
            }
        }
    }
}
