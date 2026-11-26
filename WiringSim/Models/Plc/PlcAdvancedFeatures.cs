using System.Globalization;
using System.Text;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Enums;

namespace WiringSim.Models.Plc;

/// <summary>
/// Khối chương trình PLC chuẩn IEC 61131-3:
/// - OB1_Main: Vòng quét chính tuần hoàn.
/// - OB100_Startup: Khối khởi động chạy đúng 1 lần khi STOP → RUN.
/// - FC: Hàm không có DB riêng (gọi qua lệnh CALL).
/// - FB: Khối hàm có Instance DB (gọi qua lệnh CALL kèm %DBn).
/// </summary>
public sealed class PlcCodeBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "OB1";
    public PlcBlockKind Kind { get; set; } = PlcBlockKind.OB1_Main;
    public int Number { get; set; } = 1;
    public string Comment { get; set; } = string.Empty;
    public List<LadderNetwork> Networks { get; set; } = new();

    public string DisplayLabel => Kind switch
    {
        PlcBlockKind.OB1_Main => $"OB1 ({Name})",
        PlcBlockKind.OB100_Startup => $"OB100 ({Name})",
        PlcBlockKind.FC => $"FC{Number} ({Name})",
        PlcBlockKind.FB => $"FB{Number} ({Name})",
        _ => Name
    };

    public PlcCodeBlock Clone(bool regenerateIds = false) => new()
    {
        Id = regenerateIds ? Guid.NewGuid().ToString("N")[..8] : Id,
        Name = Name,
        Kind = Kind,
        Number = Number,
        Comment = Comment,
        Networks = Networks.Select(n => n.Clone(regenerateIds)).ToList()
    };
}

/// <summary>
/// Một dòng trong bảng Force Table tách biệt chuẩn IEC (chỉ áp dụng địa chỉ ngoại vi :P cho DI/DQ/AI).
/// </summary>
public sealed class PlcForceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Address { get; set; } = "%I0.0:P";
    public string ForceValue { get; set; } = "TRUE";
    public bool IsEnabled { get; set; } = true;
    public string Comment { get; set; } = string.Empty;

    public PlcForceEntry Clone() => new()
    {
        Id = Id,
        Address = Address,
        ForceValue = ForceValue,
        IsEnabled = IsEnabled,
        Comment = Comment
    };
}

/// <summary>
/// Một bản ghi sự kiện trong Bộ đệm chẩn đoán CPU (Diagnostics Buffer).
/// </summary>
public sealed record PlcDiagnosticEvent(
    DateTime Timestamp,
    int EventId,
    PlcDiagSeverity Severity,
    string Category,
    string Message)
{
    public string EventIdHex => $"16#{EventId:X4}";
    public string TimeFormatted => Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}

/// <summary>
/// Trạng thái kênh phát xung PWM (CTRL_PWM — PWM1..PWM4 ánh xạ %QW1000, %QW1002, %QW1004, %QW1006).
/// </summary>
public sealed class PlcPwmChannelState
{
    public int Channel { get; set; } = 1; // 1..4
    public bool Enabled { get; set; }
    public int OutputDqIndex { get; set; } // 0..3 tương ứng Q0.0..Q0.3
    public int PulseWidthPermille { get; set; } // 0..1000 (0.0% .. 100.0%)
    public double FrequencyHz { get; set; } = 10.0;
    public double PhaseAccumulator { get; set; }
    public bool CurrentOutputLevel { get; set; }
    public int StatusWord { get; set; }
}

/// <summary>
/// Trạng thái bộ đếm tốc độ cao HSC (CTRL_HSC — HSC1..HSC6 ánh xạ %ID1000..%ID1020).
/// </summary>
public sealed class PlcHscChannelState
{
    public int Channel { get; set; } = 1; // 1..6
    public bool Enabled { get; set; }
    public int InputDiIndex { get; set; } // 0..5 tương ứng I0.0..I0.5
    public int CurrentValue { get; set; }
    public int ReferenceValue { get; set; } = 100;
    public int Direction { get; set; } = 1; // +1 = Up, -1 = Down
    public bool PrevInputState { get; set; }
    public bool ReferenceReached { get; set; }
    public int StatusWord { get; set; }
}

/// <summary>
/// Trạng thái bộ điều khiển PID vòng kín (PID_Compact — PIDT1 với Anti-Windup).
/// Mode: 0 = Inactive, 3 = Automatic, 4 = Manual.
/// </summary>
public sealed class PlcPidCompactState
{
    public int Mode { get; set; } = 3;
    public int State { get; set; } = 3;
    public double Setpoint { get; set; }
    public double Input { get; set; }
    public double Output { get; set; }        // 0.0 .. 100.0 %
    public short OutputPer { get; set; }      // 0 .. 27648
    public double Kp { get; set; } = 1.5;
    public double Ti { get; set; } = 5.0;     // Integral time (s), <=0 disables I
    public double Td { get; set; } = 0.0;     // Derivative time (s)
    public double IntegralTerm { get; set; }
    public double PrevError { get; set; }
    public double OutputMin { get; set; } = 0.0;
    public double OutputMax { get; set; } = 100.0;
    public uint ErrorBits { get; set; }
}

/// <summary>
/// Trạng thái phiên truyền thông Modbus RTU / TCP (Modbus_Comm_Load, Modbus_Master, MB_CLIENT).
/// </summary>
public sealed class PlcModbusState
{
    public bool PortConfigured { get; set; }
    public int BaudRate { get; set; } = 9600;
    public int Parity { get; set; } = 0; // 0=None, 1=Odd, 2=Even
    public bool Done { get; set; }
    public bool Busy { get; set; }
    public bool Error { get; set; }
    public ushort Status { get; set; }
    public ushort LastCrc16 { get; set; }
    public bool PrevReq { get; set; }
}

/// <summary>
/// Cấu trúc thời gian chuẩn IEC DTL (Date and Time Long — 12 byte: Year, Month, Day, Weekday, Hour, Minute, Second, Nanosecond).
/// </summary>
public readonly struct PlcDtlValue
{
    public ushort Year { get; init; }
    public byte Month { get; init; }
    public byte Day { get; init; }
    public byte Weekday { get; init; } // 1=Sunday .. 7=Saturday (IEC DTL)
    public byte Hour { get; init; }
    public byte Minute { get; init; }
    public byte Second { get; init; }
    public uint Nanosecond { get; init; }

    public static PlcDtlValue FromDateTime(DateTime dt) => new()
    {
        Year = (ushort)Math.Clamp(dt.Year, 1970, 2262),
        Month = (byte)dt.Month,
        Day = (byte)dt.Day,
        Weekday = (byte)((int)dt.DayOfWeek + 1),
        Hour = (byte)dt.Hour,
        Minute = (byte)dt.Minute,
        Second = (byte)dt.Second,
        Nanosecond = (uint)(dt.Millisecond * 1_000_000)
    };

    public bool TryToDateTime(out DateTime dt)
    {
        dt = default;
        if (Year < 1970 || Year > 2262 || Month < 1 || Month > 12 || Day < 1 || Day > DateTime.DaysInMonth(Year, Month))
            return false;
        if (Hour > 23 || Minute > 59 || Second > 59 || Nanosecond > 999_999_999)
            return false;
        dt = new DateTime(Year, Month, Day, Hour, Minute, Second, (int)(Nanosecond / 1_000_000));
        return true;
    }

    public string FormatIso() =>
        $"DTL#{Year:D4}-{Month:D2}-{Day:D2}-{Hour:D2}:{Minute:D2}:{Second:D2}.{Nanosecond / 1_000_000:D3}";

    public static bool TryParse(string? raw, out PlcDtlValue dtl)
    {
        dtl = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim();
        if (s.StartsWith("DTL#", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        // Hỗ trợ dạng YYYY-MM-DD-HH:mm:ss(.fff) hoặc YYYY-MM-DD HH:mm:ss
        int splitIdx = s.Length >= 11 && s[10] == '-' ? 10 : s.IndexOf(' ');
        if (splitIdx == 10)
            s = s[..10] + " " + s[11..];

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            dtl = FromDateTime(parsed);
            return true;
        }
        return false;
    }
}

/// <summary>
/// Công cụ tính CRC-16 Modbus RTU (đa thức 0xA001, khởi tạo 0xFFFF) và chuyển đổi BCD16 / BCD32 chuẩn IEC.
/// </summary>
public static class PlcProtocolAndBcdHelper
{
    /// <summary>
    /// Tính mã kiểm tra CRC-16 chuẩn Modbus RTU (Polynomial = 0xA001, Init = 0xFFFF).
    /// </summary>
    public static ushort ComputeModbusCrc16(ReadOnlySpan<byte> frame)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < frame.Length; i++)
        {
            crc ^= frame[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }

    /// <summary>
    /// Chuyển BCD16 (dạng dấu-độ lớn chuẩn PLC: bit 12..15 là dấu 0=dương / F=âm, 3 nibble thấp 0..999) sang Int.
    /// </summary>
    public static bool TryBcd16ToInt(ushort bcd, out short value)
    {
        value = 0;
        int signNibble = (bcd >> 12) & 0xF;
        if (signNibble != 0 && signNibble != 0xF)
            return false;

        int d2 = (bcd >> 8) & 0xF;
        int d1 = (bcd >> 4) & 0xF;
        int d0 = bcd & 0xF;
        if (d2 > 9 || d1 > 9 || d0 > 9)
            return false;

        int mag = d2 * 100 + d1 * 10 + d0;
        value = (short)(signNibble == 0xF ? -mag : mag);
        return true;
    }

    /// <summary>
    /// Chuyển Int (-999..+999) sang BCD16 chuẩn PLC.
    /// </summary>
    public static bool TryIntToBcd16(int value, out ushort bcd)
    {
        bcd = 0;
        if (value < -999 || value > 999) return false;
        bool neg = value < 0;
        int mag = Math.Abs(value);
        int d2 = (mag / 100) % 10;
        int d1 = (mag / 10) % 10;
        int d0 = mag % 10;
        bcd = (ushort)((neg ? 0xF000 : 0x0000) | (d2 << 8) | (d1 << 4) | d0);
        return true;
    }

    /// <summary>
    /// Chuyển BCD32 (bit 28..31 là dấu 0/F, 7 chữ số thập phân 0..9_999_999) sang DInt.
    /// </summary>
    public static bool TryBcd32ToDInt(uint bcd, out int value)
    {
        value = 0;
        uint signNibble = (bcd >> 28) & 0xF;
        if (signNibble != 0 && signNibble != 0xF)
            return false;

        int mag = 0;
        int mult = 1;
        for (int i = 0; i < 7; i++)
        {
            int digit = (int)((bcd >> (i * 4)) & 0xF);
            if (digit > 9) return false;
            mag += digit * mult;
            mult *= 10;
        }
        value = signNibble == 0xF ? -mag : mag;
        return true;
    }

    /// <summary>
    /// Chuyển DInt (-9_999_999..+9_999_999) sang BCD32 chuẩn PLC.
    /// </summary>
    public static bool TryDIntToBcd32(long value, out uint bcd)
    {
        bcd = 0;
        if (value < -9_999_999 || value > 9_999_999) return false;
        bool neg = value < 0;
        long mag = Math.Abs(value);
        uint acc = neg ? 0xF000_0000u : 0u;
        for (int i = 0; i < 7; i++)
        {
            uint digit = (uint)(mag % 10);
            mag /= 10;
            acc |= digit << (i * 4);
        }
        bcd = acc;
        return true;
    }
}

/// <summary>
/// Bus truyền thông Modbus ảo liên kết giữa PLC (Modbus_Master / MB_CLIENT) và các biến tần VFD trên sơ đồ mạch.
/// Bản đồ thanh ghi VFD chuẩn công nghiệp (Holding Registers 40001..40005):
/// - 40001 (0x2000): Control Word (1 = RUN FWD, 2 = RUN REV, 5 = STOP, 7 = FAULT RESET)
/// - 40002 (0x2001): Frequency Setpoint × 100 Hz (VD: 5000 = 50.00 Hz, 2550 = 25.50 Hz)
/// - 40003 (0x3000): Status Word (Read-only: bit0=Running, bit1=Reverse, bit2=Fault, bit3=Powered)
/// - 40004 (0x3001): Actual Output Frequency × 100 Hz (Read-only)
/// - 40005 (0x3002): Output Current × 10 A (Read-only)
/// </summary>
public sealed class PlcModbusNetworkContext
{
    public IReadOnlyList<VfdComponent> ConnectedVfds { get; set; } = [];
    public Dictionary<int, ushort> VirtualHoldingRegisters { get; } = new();

    public bool TryExecuteRequest(int slaveAddr, int mode, int dataAddr, int dataLen, ushort[] buffer, out ushort status, out ushort crc16)
    {
        status = 0;
        crc16 = 0;
        if (dataLen < 1 || dataLen > 32 || buffer.Length < dataLen)
        {
            status = 0x818B; // Invalid length / pointer
            return false;
        }

        // Xây dựng khung RTU mẫu để tính CRC-16 xác thực chuẩn Modbus RTU (0xA001)
        byte fc = mode == 0 ? (byte)0x03 : (dataLen == 1 ? (byte)0x06 : (byte)0x10);
        ushort regOffset = (ushort)(dataAddr >= 40001 ? dataAddr - 40001 : dataAddr);
        Span<byte> header = stackalloc byte[6];
        header[0] = (byte)Math.Clamp(slaveAddr, 1, 247);
        header[1] = fc;
        header[2] = (byte)(regOffset >> 8);
        header[3] = (byte)(regOffset & 0xFF);
        header[4] = (byte)(dataLen >> 8);
        header[5] = (byte)(dataLen & 0xFF);
        crc16 = PlcProtocolAndBcdHelper.ComputeModbusCrc16(header);

        // Tìm VFD có ModbusSlaveId khớp với slaveAddr
        VfdComponent? vfd = ConnectedVfds.FirstOrDefault(v => v.ModbusSlaveId == slaveAddr);
        if (vfd is not null)
        {
            for (int i = 0; i < dataLen; i++)
            {
                int addr = dataAddr + i;
                if (mode == 0) // Read Holding / Input Registers
                {
                    buffer[i] = vfd.ReadModbusRegister(addr);
                }
                else // Write Holding Registers
                {
                    if (!vfd.WriteModbusRegister(addr, buffer[i]))
                    {
                        status = 0x8383; // Illegal data address
                        return false;
                    }
                }
            }
            return true;
        }

        // Fallback: thanh ghi ảo nội bộ nếu không có VFD vật lý ứng với slaveAddr
        for (int i = 0; i < dataLen; i++)
        {
            int key = (slaveAddr << 16) | ((dataAddr + i) & 0xFFFF);
            if (mode == 0)
                buffer[i] = VirtualHoldingRegisters.GetValueOrDefault(key, (ushort)0);
            else
                VirtualHoldingRegisters[key] = buffer[i];
        }
        return true;
    }
}

/// <summary>
/// Bộ xuất / nhập chương trình PLC dạng văn bản có cấu trúc dễ đọc (Structured Document — .plctxt),
/// hỗ trợ lưu trữ Git / kiểm soát phiên bản toàn bộ Tags, Blocks (OB1, OB100, FC, FB), Networks và Force Table.
/// </summary>
public static class PlcStructuredTextSerializer
{
    public static string ExportToText(PlcProgramModel program)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PLC_PROGRAM_TEXT V1");
        sb.AppendLine($"CONFIG SystemMemory={program.EnableSystemMemory}:{program.SystemMemoryByte} ClockMemory={program.EnableClockMemory}:{program.ClockMemoryByte} WatchdogMs={program.MaxCycleTimeMs} StopOutput={program.StopOutputMode}");
        sb.AppendLine();

        sb.AppendLine("BEGIN_TAGS");
        foreach (var t in program.Tags)
        {
            sb.AppendLine($"  TAG \"{Escape(t.Name)}\" {t.DataType} {t.Address} Retain={t.Retain} Comment=\"{Escape(t.Comment)}\"");
        }
        sb.AppendLine("END_TAGS");
        sb.AppendLine();

        if (program.ForceEntries.Count > 0)
        {
            sb.AppendLine("BEGIN_FORCE_TABLE");
            foreach (var f in program.ForceEntries)
                sb.AppendLine($"  FORCE {f.Address} = {f.ForceValue} Enabled={f.IsEnabled} Comment=\"{Escape(f.Comment)}\"");
            sb.AppendLine("END_FORCE_TABLE");
            sb.AppendLine();
        }

        // Xuất OB1 chính + toàn bộ các khối bổ sung (OB100, FC, FB)
        WriteBlock(sb, new PlcCodeBlock
        {
            Name = "Main",
            Kind = PlcBlockKind.OB1_Main,
            Number = 1,
            Networks = program.Networks
        });

        foreach (var blk in program.AdditionalBlocks)
            WriteBlock(sb, blk);

        return sb.ToString();
    }

    private static void WriteBlock(StringBuilder sb, PlcCodeBlock blk)
    {
        sb.AppendLine($"BEGIN_BLOCK {blk.Kind} {blk.Number} \"{Escape(blk.Name)}\"");
        for (int i = 0; i < blk.Networks.Count; i++)
        {
            var net = blk.Networks[i];
            sb.AppendLine($"  BEGIN_NETWORK \"{Escape(net.Title)}\"");
            WriteBranch(sb, net.MainBranch, "    ");
            sb.AppendLine("  END_NETWORK");
        }
        sb.AppendLine("END_BLOCK");
        sb.AppendLine();
    }

    private static void WriteBranch(StringBuilder sb, LadderBranch branch, string indent)
    {
        foreach (var n in branch.Nodes)
        {
            if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
            {
                sb.AppendLine($"{indent}BEGIN_PARALLEL");
                foreach (var sub in n.Branches)
                {
                    sb.AppendLine($"{indent}  BEGIN_BRANCH");
                    WriteBranch(sb, sub, indent + "    ");
                    sb.AppendLine($"{indent}  END_BRANCH");
                }
                sb.AppendLine($"{indent}END_PARALLEL");
            }
            else
            {
                var fields = new List<string> { $"NODE {n.Kind}" };
                if (!string.IsNullOrEmpty(n.Address)) fields.Add($"Addr=\"{Escape(n.Address)}\"");
                if (!string.IsNullOrEmpty(n.SymbolName)) fields.Add($"Sym=\"{Escape(n.SymbolName)}\"");
                if (!string.IsNullOrEmpty(n.DataType)) fields.Add($"Type=\"{Escape(n.DataType)}\"");
                if (!string.IsNullOrEmpty(n.Param1)) fields.Add($"P1=\"{Escape(n.Param1)}\"");
                if (!string.IsNullOrEmpty(n.Param2)) fields.Add($"P2=\"{Escape(n.Param2)}\"");
                if (!string.IsNullOrEmpty(n.ExtraParam)) fields.Add($"Ex=\"{Escape(n.ExtraParam)}\"");
                if (!string.IsNullOrEmpty(n.OutputAddress)) fields.Add($"Out=\"{Escape(n.OutputAddress)}\"");
                if (n.ExtraInputs is { Count: > 0 }) fields.Add($"ExIn=\"{Escape(string.Join(";", n.ExtraInputs))}\"");
                if (n.ExtraOutputs is { Count: > 0 }) fields.Add($"ExOut=\"{Escape(string.Join(";", n.ExtraOutputs))}\"");
                sb.AppendLine(indent + string.Join(" ", fields));
            }
        }
    }

    public static bool TryImportFromText(string text, out PlcProgramModel program, out string error)
    {
        program = new PlcProgramModel();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Empty PLC text document.";
            return false;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool inTags = false;
        bool inForce = false;
        PlcCodeBlock? currentBlock = null;
        LadderNetwork? currentNet = null;
        var branchStack = new Stack<LadderBranch>();
        var parallelStack = new Stack<LadderNode>();

        for (int lineNo = 0; lineNo < lines.Length; lineNo++)
        {
            string line = lines[lineNo].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("PLC_PROGRAM_TEXT", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("CONFIG ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string tok in line[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = tok.Split('=', 2);
                    if (kv.Length != 2) continue;
                    if (kv[0].Equals("SystemMemory", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = kv[1].Split(':');
                        if (p.Length == 2 && bool.TryParse(p[0], out bool en) && int.TryParse(p[1], out int b))
                        {
                            program.EnableSystemMemory = en;
                            program.SystemMemoryByte = Math.Clamp(b, 0, OperandResolver.MaxMemoryBytes - 1);
                        }
                    }
                    else if (kv[0].Equals("ClockMemory", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = kv[1].Split(':');
                        if (p.Length == 2 && bool.TryParse(p[0], out bool en) && int.TryParse(p[1], out int b))
                        {
                            program.EnableClockMemory = en;
                            program.ClockMemoryByte = Math.Clamp(b, 0, OperandResolver.MaxMemoryBytes - 1);
                        }
                    }
                    else if (kv[0].Equals("WatchdogMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out int wd))
                    {
                        program.MaxCycleTimeMs = Math.Clamp(wd, 1, 6000);
                    }
                    else if (kv[0].Equals("StopOutput", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<PlcStopOutputMode>(kv[1], true, out var sm))
                    {
                        program.StopOutputMode = sm;
                    }
                }
                continue;
            }

            if (line.Equals("BEGIN_TAGS", StringComparison.OrdinalIgnoreCase)) { inTags = true; continue; }
            if (line.Equals("END_TAGS", StringComparison.OrdinalIgnoreCase)) { inTags = false; continue; }
            if (line.Equals("BEGIN_FORCE_TABLE", StringComparison.OrdinalIgnoreCase)) { inForce = true; continue; }
            if (line.Equals("END_FORCE_TABLE", StringComparison.OrdinalIgnoreCase)) { inForce = false; continue; }

            if (inTags && line.StartsWith("TAG ", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = Tokenize(line[4..]);
                if (tokens.Count >= 3)
                {
                    var tag = new PlcTagEntry
                    {
                        Name = Unescape(tokens[0]),
                        DataType = tokens[1],
                        Address = tokens[2]
                    };
                    foreach (string t in tokens.Skip(3))
                    {
                        if (t.StartsWith("Retain=", StringComparison.OrdinalIgnoreCase) && bool.TryParse(t[7..], out bool r))
                            tag.Retain = r;
                        else if (t.StartsWith("Comment=", StringComparison.OrdinalIgnoreCase))
                            tag.Comment = Unescape(TrimQuotes(t[8..]));
                    }
                    program.Tags.Add(tag);
                }
                continue;
            }

            if (inForce && line.StartsWith("FORCE ", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = Tokenize(line[6..]);
                if (tokens.Count >= 3 && tokens[1] == "=")
                {
                    var fe = new PlcForceEntry { Address = tokens[0], ForceValue = tokens[2] };
                    foreach (string t in tokens.Skip(3))
                    {
                        if (t.StartsWith("Enabled=", StringComparison.OrdinalIgnoreCase) && bool.TryParse(t[8..], out bool en))
                            fe.IsEnabled = en;
                        else if (t.StartsWith("Comment=", StringComparison.OrdinalIgnoreCase))
                            fe.Comment = Unescape(TrimQuotes(t[8..]));
                    }
                    program.ForceEntries.Add(fe);
                }
                continue;
            }

            if (line.StartsWith("BEGIN_BLOCK ", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = Tokenize(line[12..]);
                if (tokens.Count < 3 || !Enum.TryParse<PlcBlockKind>(tokens[0], true, out var bk) || !int.TryParse(tokens[1], out int bNum))
                {
                    error = $"Line {lineNo + 1}: Invalid BEGIN_BLOCK header.";
                    return false;
                }
                currentBlock = new PlcCodeBlock { Kind = bk, Number = bNum, Name = Unescape(tokens[2]) };
                continue;
            }

            if (line.Equals("END_BLOCK", StringComparison.OrdinalIgnoreCase))
            {
                if (currentBlock is not null)
                {
                    if (currentBlock.Kind == PlcBlockKind.OB1_Main)
                        program.Networks = currentBlock.Networks;
                    else
                        program.AdditionalBlocks.Add(currentBlock);
                    currentBlock = null;
                }
                continue;
            }

            if (line.StartsWith("BEGIN_NETWORK", StringComparison.OrdinalIgnoreCase))
            {
                string title = line.Length > 13 ? Unescape(TrimQuotes(line[13..].Trim())) : "Network";
                currentNet = new LadderNetwork { Title = title };
                branchStack.Clear();
                parallelStack.Clear();
                branchStack.Push(currentNet.MainBranch);
                continue;
            }

            if (line.Equals("END_NETWORK", StringComparison.OrdinalIgnoreCase))
            {
                if (currentNet is not null)
                {
                    if (currentBlock is not null) currentBlock.Networks.Add(currentNet);
                    else program.Networks.Add(currentNet);
                    currentNet = null;
                }
                branchStack.Clear();
                parallelStack.Clear();
                continue;
            }

            if (line.Equals("BEGIN_PARALLEL", StringComparison.OrdinalIgnoreCase))
            {
                if (branchStack.Count == 0) continue;
                var pb = new LadderNode { Kind = LadderElementKind.ParallelBlock, Branches = new List<LadderBranch>() };
                branchStack.Peek().Nodes.Add(pb);
                parallelStack.Push(pb);
                continue;
            }

            if (line.Equals("END_PARALLEL", StringComparison.OrdinalIgnoreCase))
            {
                if (parallelStack.Count > 0) parallelStack.Pop();
                continue;
            }

            if (line.Equals("BEGIN_BRANCH", StringComparison.OrdinalIgnoreCase))
            {
                if (parallelStack.Count == 0) continue;
                var sub = new LadderBranch();
                parallelStack.Peek().Branches!.Add(sub);
                branchStack.Push(sub);
                continue;
            }

            if (line.Equals("END_BRANCH", StringComparison.OrdinalIgnoreCase))
            {
                if (branchStack.Count > 1) branchStack.Pop();
                continue;
            }

            if (line.StartsWith("NODE ", StringComparison.OrdinalIgnoreCase) && branchStack.Count > 0)
            {
                var tokens = Tokenize(line[5..]);
                if (tokens.Count == 0 || !Enum.TryParse<LadderElementKind>(tokens[0], true, out var kind))
                {
                    error = $"Line {lineNo + 1}: Unknown NODE kind.";
                    return false;
                }
                var node = new LadderNode { Kind = kind };
                foreach (string tok in tokens.Skip(1))
                {
                    int eq = tok.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = tok[..eq];
                    string v = Unescape(TrimQuotes(tok[(eq + 1)..]));
                    switch (k)
                    {
                        case "Addr": node.Address = v; break;
                        case "Sym": node.SymbolName = v; break;
                        case "Type": node.DataType = v; break;
                        case "P1": node.Param1 = v; break;
                        case "P2": node.Param2 = v; break;
                        case "Ex": node.ExtraParam = v; break;
                        case "Out": node.OutputAddress = v; break;
                        case "ExIn": node.ExtraInputs = v.Split(';').ToList(); break;
                        case "ExOut": node.ExtraOutputs = v.Split(';').ToList(); break;
                    }
                }
                branchStack.Peek().Nodes.Add(node);
            }
        }

        return true;
    }

    private static string Escape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Unescape(string s) => (s ?? string.Empty).Replace("\\\"", "\"").Replace("\\\\", "\\");
    private static string TrimQuotes(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static List<string> Tokenize(string src)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '\\' && i + 1 < src.Length && inQuotes)
            {
                cur.Append(c);
                cur.Append(src[++i]);
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                cur.Append(c);
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (cur.Length > 0)
                {
                    list.Add(TrimQuotes(cur.ToString()));
                    cur.Clear();
                }
                continue;
            }
            cur.Append(c);
        }
        if (cur.Length > 0) list.Add(TrimQuotes(cur.ToString()));
        return list;
    }
}
