using System.Buffers.Binary;
using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Plc;

/// <summary>
/// Phần mở rộng của PlcRuntimeEngine (Part 2 &amp; V21):
/// - Diagnostics Buffer (nhật ký sự kiện CPU có timestamp &amp; Event ID).
/// - Force Table tách biệt chuẩn :P (khóa không cho ép chân PWM / HSC).
/// - Đa khối OB100 (Startup), FC, FB (lệnh CALL đệ quy an toàn tối đa 8 cấp).
/// - Kiểu LReal (64-bit), Char, String (max 254), DTL (12-byte RTC) và BCD16/BCD32.
/// - Lệnh mở rộng Date/Time &amp; String, Technology (CTRL_PWM, CTRL_HSC, PID_Compact), Modbus RTU/TCP (kết nối VFD).
/// </summary>
public sealed partial class PlcRuntimeEngine
{
    public const int MaxDiagnosticsEntries = 100;
    public const int MaxCallDepth = 8;

    // Bộ đệm chẩn đoán CPU (Diagnostics Buffer)
    public List<PlcDiagnosticEvent> DiagnosticsBuffer { get; } = new();

    // Bộ nhớ lưu trữ String (max 254) và DTL (12-byte) theo địa chỉ chuẩn hóa
    public Dictionary<string, string> StringStore { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PlcDtlValue> DtlStore { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Trạng thái Technology Objects & Truyền thông Modbus
    public Dictionary<int, PlcPwmChannelState> PwmChannels { get; } = new();
    public Dictionary<int, PlcHscChannelState> HscChannels { get; } = new();
    public Dictionary<string, PlcPidCompactState> PidDbs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PlcModbusState> ModbusDbs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public PlcModbusNetworkContext ModbusBus { get; } = new();

    // Thanh ghi chuyên dụng PWM (%QW1000..%QW1006) & HSC (%ID1000..%ID1020)
    private readonly ushort[] _pwmWidthWords = new ushort[4]; // 0..1000 permille
    private readonly int[] _hscCurrentValues = new int[6];

    // Trạng thái Force Table tách biệt & danh sách chân phần cứng bị khóa bởi PWM/HSC
    public bool IsForceTableEnabled { get; private set; }
    private readonly HashSet<int> _protectedPwmDqIndices = new();
    private readonly HashSet<int> _protectedHscDiIndices = new();

    // Watchdog & độ sâu gọi khối CALL
    private int _callDepth;
    private int _retriggerCount;
    private int _effectiveMaxJumps = MaxJumpsPerScan;

    public bool HasActiveForces =>
        _watchInputBitOverrides.Count > 0 || _watchAiOverrides.Count > 0 || _watchOutputBitOverrides.Count > 0;

    public void LogDiagnostic(int eventId, PlcDiagSeverity severity, string category, string message)
    {
        DiagnosticsBuffer.Add(new PlcDiagnosticEvent(DateTime.Now, eventId, severity, category, message));
        if (DiagnosticsBuffer.Count > MaxDiagnosticsEntries)
            DiagnosticsBuffer.RemoveAt(0);
    }

    public void ClearDiagnosticsBuffer() => DiagnosticsBuffer.Clear();

    private void ResetExtendedState(bool clearDiagnostics = false)
    {
        StringStore.Clear();
        DtlStore.Clear();
        PwmChannels.Clear();
        HscChannels.Clear();
        PidDbs.Clear();
        ModbusDbs.Clear();
        Array.Clear(_pwmWidthWords);
        Array.Clear(_hscCurrentValues);
        _protectedPwmDqIndices.Clear();
        _protectedHscDiIndices.Clear();
        _callDepth = 0;
        _retriggerCount = 0;
        IsForceTableEnabled = false;
        if (clearDiagnostics) DiagnosticsBuffer.Clear();
    }

    /// <summary>
    /// Đồng bộ bảng Force Table (PlcProgramModel.ForceEntries) vào lớp ép phần cứng :P.
    /// Tuân thủ đặc tả Part 2: từ chối ép các ngõ vào/ra đang gán cho PWM hoặc HSC!
    /// </summary>
    public bool ApplyForceTable(PlcProgramModel program, out List<string> rejectedReasons)
    {
        rejectedReasons = new List<string>();
        _watchInputBitOverrides.Clear();
        _watchAiOverrides.Clear();
        _watchOutputBitOverrides.Clear();
        IsForceTableEnabled = false;

        int applied = 0;
        foreach (var entry in program.ForceEntries)
        {
            if (!entry.IsEnabled || string.IsNullOrWhiteSpace(entry.Address) || string.IsNullOrWhiteSpace(entry.ForceValue))
                continue;

            string expr = entry.Address.Trim();
            if (!expr.EndsWith(":P", StringComparison.OrdinalIgnoreCase))
                expr += ":P";

            var op = OperandResolver.Resolve(expr, program);
            if (!op.IsValid || op.Area is not (PlcMemoryArea.I or PlcMemoryArea.Q))
            {
                rejectedReasons.Add($"{entry.Address}: {LocalizationManager.Get("Str.Plc.ForceErrOnlyIO")}");
                continue;
            }
            if (!OperandResolver.TryParseLiteral(entry.ForceValue, out var lit))
            {
                rejectedReasons.Add($"{entry.Address}: {LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", entry.ForceValue)}");
                continue;
            }

            if (op.Area == PlcMemoryArea.I && op.AccessSizeBytes == 0)
            {
                int lin = op.ByteOffset * 8 + op.BitOffset;
                if (_protectedHscDiIndices.Contains(lin))
                {
                    string msg = LocalizationManager.Format("Str.Plc.ForceErrProtectedIo", entry.Address, "HSC");
                    rejectedReasons.Add(msg);
                    LogDiagnostic(0x80A2, PlcDiagSeverity.Warning, "Force", msg);
                    continue;
                }
                _watchInputBitOverrides[lin] = lit.AsBool;
                applied++;
            }
            else if (op.Area == PlcMemoryArea.I && op.AccessSizeBytes == 2 && op.ByteOffset is 64 or 66)
            {
                _watchAiOverrides[op.ByteOffset] = (short)Math.Clamp(lit.AsInt64, short.MinValue, short.MaxValue);
                applied++;
            }
            else if (op.Area == PlcMemoryArea.Q && op.AccessSizeBytes == 0)
            {
                int lin = op.ByteOffset * 8 + op.BitOffset;
                if (_protectedPwmDqIndices.Contains(lin))
                {
                    string msg = LocalizationManager.Format("Str.Plc.ForceErrProtectedIo", entry.Address, "PWM");
                    rejectedReasons.Add(msg);
                    LogDiagnostic(0x80A2, PlcDiagSeverity.Warning, "Force", msg);
                    continue;
                }
                _watchOutputBitOverrides[lin] = lit.AsBool;
                applied++;
            }
            else
            {
                rejectedReasons.Add($"{entry.Address}: {LocalizationManager.Get("Str.Plc.ForceErrOnlyIO")}");
            }
        }

        IsForceTableEnabled = applied > 0;
        if (applied > 0)
            LogDiagnostic(0x0201, PlcDiagSeverity.Info, "Force", $"Force Table activated ({applied} active forces).");
        return rejectedReasons.Count == 0;
    }

    public void StopAllForces()
    {
        bool had = HasActiveForces || IsForceTableEnabled;
        ClearWatchInputOverrides();
        IsForceTableEnabled = false;
        if (had)
            LogDiagnostic(0x0202, PlcDiagSeverity.Info, "Force", "All Force Table overrides stopped.");
    }

    /// <summary>
    /// Thực thi khối khởi động OB100 (Startup) đúng 1 lần trước vòng quét OB1 đầu tiên khi STOP → RUN.
    /// </summary>
    private void ExecuteStartupBlocks(PlcProgramModel program, double dt)
    {
        var startups = program.AdditionalBlocks
            .Where(b => b.Kind == PlcBlockKind.OB100_Startup)
            .OrderBy(b => b.Number)
            .ToList();

        foreach (var ob100 in startups)
        {
            if (ob100.Networks.Count == 0) continue;
            LogDiagnostic(0x0102, PlcDiagSeverity.Info, "Startup", $"Executing {ob100.DisplayLabel}.");
            ExecuteNetworkList(ob100.Networks, dt);
            if (CpuStopRequested) break;
        }
    }

    /// <summary>
    /// Thực thi một danh sách Network (dùng chung cho OB1, OB100, FC và FB với ngăn xếp cục bộ riêng cho JMP/RET).
    /// </summary>
    private void ExecuteNetworkList(List<LadderNetwork> networks, double dt)
    {
        var labelMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < networks.Count; i++)
            IndexNetworkLabels(networks[i].MainBranch, i, labelMap);

        int pc = 0;
        int jumpCount = 0;

        while (pc >= 0 && pc < networks.Count)
        {
            var net = networks[pc];
            if (net.MainBranch.Nodes.Count > 0)
            {
                EvaluateBranch(net.MainBranch, powerIn: true, dt);
            }

            if (CpuStopRequested || _returnRequested)
                break;

            if (_pendingJumpLabel is not null)
            {
                string target = _pendingJumpLabel;
                _pendingJumpLabel = null;
                if (labelMap.TryGetValue(target, out int targetIdx) && ++jumpCount <= _effectiveMaxJumps)
                {
                    pc = targetIdx;
                    continue;
                }
                SetRuntimeError(0x8090);
                LogDiagnostic(0x8090, PlcDiagSeverity.Error, "Watchdog",
                    $"Jump limit ({_effectiveMaxJumps}) exceeded or missing LABEL '{target}'.");
                if (jumpCount > _effectiveMaxJumps)
                    CpuStopRequested = true;
                break;
            }

            pc++;
        }
    }

    /// <summary>
    /// Cập nhật các kênh phần cứng tốc độ cao HSC (đếm cạnh lên DI) và PWM (xuất xung ra DQ) trong vòng quét.
    /// </summary>
    private void UpdateHardwareTechnologySubsystems(double dt)
    {
        // 1. Cập nhật HSC1..HSC6 từ DI0.0..DI0.5
        foreach (var kv in HscChannels)
        {
            var hsc = kv.Value;
            if (!hsc.Enabled || hsc.InputDiIndex < 0 || hsc.InputDiIndex >= PhysicalDi.Length) continue;
            bool curIn = PhysicalDi[hsc.InputDiIndex];
            if (curIn && !hsc.PrevInputState)
            {
                hsc.CurrentValue = unchecked(hsc.CurrentValue + (hsc.Direction >= 0 ? 1 : -1));
                _hscCurrentValues[hsc.Channel - 1] = hsc.CurrentValue;
                hsc.ReferenceReached = hsc.Direction >= 0
                    ? hsc.CurrentValue >= hsc.ReferenceValue
                    : hsc.CurrentValue <= hsc.ReferenceValue;
            }
            hsc.PrevInputState = curIn;
        }

        // 2. Cập nhật PWM1..PWM4 ra DQ0.0..DQ0.3 (ghi đè lên _imageQ trước Pha 3)
        foreach (var kv in PwmChannels)
        {
            var pwm = kv.Value;
            if (!pwm.Enabled || pwm.OutputDqIndex < 0 || pwm.OutputDqIndex >= PhysicalDq.Length) continue;

            pwm.PulseWidthPermille = Math.Clamp((int)_pwmWidthWords[pwm.Channel - 1], 0, 1000);
            double duty = pwm.PulseWidthPermille / 1000.0;
            double freq = Math.Clamp(pwm.FrequencyHz, 0.5, 50.0);
            pwm.PhaseAccumulator = (pwm.PhaseAccumulator + dt * freq) % 1.0;

            bool level = duty <= 0.0 ? false : (duty >= 1.0 ? true : pwm.PhaseAccumulator < duty);
            pwm.CurrentOutputLevel = level;

            int bIdx = pwm.OutputDqIndex / 8;
            int bitIdx = pwm.OutputDqIndex % 8;
            if (level) _imageQ[bIdx] |= (byte)(1 << bitIdx);
            else _imageQ[bIdx] &= (byte)~(1 << bitIdx);
        }
    }

    /// <summary>
    /// Đọc chuỗi String chuẩn IEC (từ hằng số 'ABC' hoặc từ StringStore theo địa chỉ chuẩn hóa).
    /// </summary>
    private string ReadStringOperand(string? raw)
    {
        var op = ResolveOp(raw);
        if (!op.IsValid) return string.Empty;
        if (op.IsLiteral && op.LiteralValue.HasValue)
            return op.LiteralValue.Value.AsString;
        if (StringStore.TryGetValue(op.NormalizedText, out var s))
            return s;
        // Fallback: chuyển số/char thành chuỗi nếu chưa khởi tạo trong StringStore
        if (op.InferredType == PlcDataType.Char)
            return ReadTypedValue(op, PlcDataType.Char).AsString;
        return string.Empty;
    }

    private void WriteStringOperand(string? raw, string value)
    {
        var op = ResolveOp(raw);
        if (!op.IsValid || !op.IsWritable) return;
        string safe = value.Length > 254 ? value[..254] : value;
        StringStore[op.NormalizedText] = safe;
    }

    private bool TryReadDtlOperand(string? raw, out PlcDtlValue dtl)
    {
        dtl = default;
        var op = ResolveOp(raw);
        if (!op.IsValid) return false;
        if (op.IsLiteral && op.LiteralValue is { DataType: PlcDataType.DTL } lit)
        {
            dtl = lit.AsDtl;
            return true;
        }
        if (DtlStore.TryGetValue(op.NormalizedText, out dtl))
            return true;
        dtl = PlcDtlValue.FromDateTime(DateTime.Now);
        return true;
    }

    private void WriteDtlOperand(string? raw, PlcDtlValue dtl)
    {
        var op = ResolveOp(raw);
        if (!op.IsValid || !op.IsWritable) return;
        DtlStore[op.NormalizedText] = dtl;
    }

    /// <summary>
    /// Thực thi toàn bộ các lệnh thuộc Nhóm 11 (Extended), Nhóm 12 (Technology) và Nhóm 13 (Communication).
    /// </summary>
    private bool EvaluateExtendedNode(LadderNode node, bool powerIn, double dt, out bool handled)
    {
        handled = true;
        switch (node.Kind)
        {
            // =========================================================================
            // NHÓM 11A: DATE & TIME / RTC (RD_SYS_T, RD_LOC_T, T_ADD, T_SUB, T_DIFF, T_CONV)
            // =========================================================================
            case LadderElementKind.ReadSysTime:
            case LadderElementKind.ReadLocTime:
            {
                if (!powerIn) return false;
                var dtl = PlcDtlValue.FromDateTime(node.Kind == LadderElementKind.ReadSysTime ? DateTime.UtcNow : DateTime.Now);
                WriteDtlOperand(node.OutputAddress, dtl);
                LiveNodeStatusText[node.Id] = $"{dtl.Hour:D2}:{dtl.Minute:D2}:{dtl.Second:D2}";
                return true;
            }

            case LadderElementKind.TimeAdd:
            case LadderElementKind.TimeSub:
            {
                if (!powerIn) return false;
                var op1 = ResolveOp(node.Param1);
                bool isDtl1 = op1.IsValid && (op1.InferredType == PlcDataType.DTL || DtlStore.ContainsKey(op1.NormalizedText));
                var t2 = ReadTypedValue(node.Param2, PlcDataType.Time);
                int deltaMs = t2.AsTimeMs;

                if (isDtl1 && TryReadDtlOperand(node.Param1, out var dtl1) && dtl1.TryToDateTime(out var dt1))
                {
                    DateTime resDt = node.Kind == LadderElementKind.TimeAdd
                        ? dt1.AddMilliseconds(deltaMs)
                        : dt1.AddMilliseconds(-deltaMs);
                    var outDtl = PlcDtlValue.FromDateTime(resDt);
                    WriteDtlOperand(node.OutputAddress, outDtl);
                    LiveNodeStatusText[node.Id] = $"{outDtl.Hour:D2}:{outDtl.Minute:D2}:{outDtl.Second:D2}";
                    return true;
                }

                long ms1 = ReadTypedValue(node.Param1, PlcDataType.Time).AsTimeMs;
                long resMs = node.Kind == LadderElementKind.TimeAdd ? ms1 + deltaMs : ms1 - deltaMs;
                if (resMs < int.MinValue || resMs > int.MaxValue)
                {
                    SetRuntimeError(0x8203);
                    LiveNodeStatusText[node.Id] = "OVF (ENO=0)";
                    return false;
                }
                var tv = PlcValue.FromTimeMs((int)resMs);
                WriteTypedValue(node.OutputAddress, tv);
                LiveNodeStatusText[node.Id] = tv.Format(PlcWatchFormat.Time);
                return true;
            }

            case LadderElementKind.TimeDiff:
            {
                if (!powerIn) return false;
                if (!TryReadDtlOperand(node.Param1, out var d1) || !d1.TryToDateTime(out var dt1) ||
                    !TryReadDtlOperand(node.Param2, out var d2) || !d2.TryToDateTime(out var dt2))
                {
                    SetRuntimeError(0x8081);
                    return false;
                }
                double diffMs = (dt1 - dt2).TotalMilliseconds;
                if (diffMs < int.MinValue || diffMs > int.MaxValue)
                {
                    SetRuntimeError(0x8203);
                    return false;
                }
                var tv = PlcValue.FromTimeMs((int)Math.Round(diffMs));
                WriteTypedValue(node.OutputAddress, tv);
                LiveNodeStatusText[node.Id] = tv.Format(PlcWatchFormat.Time);
                return true;
            }

            case LadderElementKind.TimeConv:
            {
                if (!powerIn) return false;
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.DInt);
                if (targetDt == PlcDataType.DTL)
                {
                    int ms = ReadTypedValue(node.Param1, PlcDataType.Time).AsTimeMs;
                    var dtl = PlcDtlValue.FromDateTime(DateTime.Today.AddMilliseconds(Math.Max(0, ms)));
                    WriteDtlOperand(node.OutputAddress, dtl);
                    LiveNodeStatusText[node.Id] = dtl.FormatIso();
                    return true;
                }
                var v = ReadTypedValue(node.Param1, PlcDataType.Time);
                if (!PlcValue.TryFromDouble(v.AsDouble, targetDt, out var conv))
                    return false;
                WriteTypedValue(node.OutputAddress, conv);
                LiveNodeStatusText[node.Id] = conv.Format(PlcWatchFormat.Auto);
                return true;
            }

            // =========================================================================
            // NHÓM 11B: STRING + CHAR OPERATIONS (IEC 1-based indexing, max 254 chars)
            // =========================================================================
            case LadderElementKind.StringConv:
            {
                if (!powerIn) return false;
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.String);
                if (targetDt == PlcDataType.String)
                {
                    var v = ReadTypedValue(node.Param1);
                    string s = v.AsString;
                    WriteStringOperand(node.OutputAddress, s);
                    LiveNodeStatusText[node.Id] = $"'{s}'";
                    return true;
                }
                string srcStr = ReadStringOperand(node.Param1).Trim().Replace(",", "", StringComparison.Ordinal);
                if (!double.TryParse(srcStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) ||
                    !PlcValue.TryFromDouble(num, targetDt, out var parsed))
                {
                    SetRuntimeError(0x8120);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }
                WriteTypedValue(node.OutputAddress, parsed);
                LiveNodeStatusText[node.Id] = parsed.Format(PlcWatchFormat.Auto);
                return true;
            }

            case LadderElementKind.StringLen:
            {
                if (!powerIn) return false;
                string s = ReadStringOperand(node.Param1);
                WriteNumber(node.OutputAddress, s.Length);
                LiveNodeStatusText[node.Id] = $"LEN={s.Length}";
                return true;
            }

            case LadderElementKind.StringConcat:
            {
                if (!powerIn) return false;
                string combined = ReadStringOperand(node.Param1) + ReadStringOperand(node.Param2);
                bool overflow = combined.Length > 254;
                string safe = overflow ? combined[..254] : combined;
                WriteStringOperand(node.OutputAddress, safe);
                LiveNodeStatusText[node.Id] = $"'{safe}'";
                return !overflow;
            }

            case LadderElementKind.StringLeft:
            case LadderElementKind.StringRight:
            {
                if (!powerIn) return false;
                string s = ReadStringOperand(node.Param1);
                int len = (int)Math.Round(ReadNumber(node.Param2));
                if (len < 0)
                {
                    WriteStringOperand(node.OutputAddress, string.Empty);
                    return false;
                }
                int take = Math.Min(len, s.Length);
                string res = node.Kind == LadderElementKind.StringLeft ? s[..take] : s[^take..];
                WriteStringOperand(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"'{res}'";
                return true;
            }

            case LadderElementKind.StringMid:
            {
                if (!powerIn) return false;
                string s = ReadStringOperand(node.Param1);
                int len = (int)Math.Round(ReadNumber(node.Param2));
                int pos1 = (int)Math.Round(ReadNumber(node.ExtraParam)); // 1-based IEC index
                if (pos1 < 1 || len < 0)
                {
                    WriteStringOperand(node.OutputAddress, string.Empty);
                    return false;
                }
                if (pos1 > s.Length || len == 0)
                {
                    WriteStringOperand(node.OutputAddress, string.Empty);
                    LiveNodeStatusText[node.Id] = "''";
                    return true;
                }
                int start = pos1 - 1;
                int take = Math.Min(len, s.Length - start);
                string res = s.Substring(start, take);
                WriteStringOperand(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"'{res}'";
                return true;
            }

            case LadderElementKind.StringDelete:
            {
                if (!powerIn) return false;
                string s = ReadStringOperand(node.Param1);
                int len = (int)Math.Round(ReadNumber(node.Param2));
                int pos1 = (int)Math.Round(ReadNumber(node.ExtraParam));
                if (pos1 < 1 || len < 0)
                {
                    WriteStringOperand(node.OutputAddress, s);
                    return false;
                }
                string res = pos1 > s.Length || len == 0
                    ? s
                    : s.Remove(pos1 - 1, Math.Min(len, s.Length - (pos1 - 1)));
                WriteStringOperand(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"'{res}'";
                return true;
            }

            case LadderElementKind.StringInsert:
            {
                if (!powerIn) return false;
                string s1 = ReadStringOperand(node.Param1);
                string s2 = ReadStringOperand(node.Param2);
                int pos = (int)Math.Round(ReadNumber(node.ExtraParam)); // IEC INSERT: sau vị trí P (P=0 -> chèn đầu chuỗi)
                if (pos < 0)
                {
                    WriteStringOperand(node.OutputAddress, s1);
                    return false;
                }
                int idx = Math.Min(pos, s1.Length);
                string combined = s1.Insert(idx, s2);
                bool overflow = combined.Length > 254;
                string safe = overflow ? combined[..254] : combined;
                WriteStringOperand(node.OutputAddress, safe);
                LiveNodeStatusText[node.Id] = $"'{safe}'";
                return !overflow;
            }

            case LadderElementKind.StringReplace:
            {
                if (!powerIn) return false;
                string s1 = ReadStringOperand(node.Param1);
                string s2 = ReadStringOperand(node.Param2);
                var parts = (node.ExtraParam ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int len = parts.Length >= 1 ? (int)Math.Round(ReadNumber(parts[0])) : 0;
                int pos1 = parts.Length >= 2 ? (int)Math.Round(ReadNumber(parts[1])) : 1;
                if (pos1 < 1 || len < 0)
                {
                    WriteStringOperand(node.OutputAddress, s1);
                    return false;
                }
                string res;
                if (pos1 > s1.Length)
                    res = s1 + s2;
                else
                    res = s1.Remove(pos1 - 1, Math.Min(len, s1.Length - (pos1 - 1))).Insert(pos1 - 1, s2);
                bool overflow = res.Length > 254;
                string safe = overflow ? res[..254] : res;
                WriteStringOperand(node.OutputAddress, safe);
                LiveNodeStatusText[node.Id] = $"'{safe}'";
                return !overflow;
            }

            case LadderElementKind.StringFind:
            {
                if (!powerIn) return false;
                string s1 = ReadStringOperand(node.Param1);
                string s2 = ReadStringOperand(node.Param2);
                int idx = string.IsNullOrEmpty(s2) ? 0 : s1.IndexOf(s2, StringComparison.Ordinal) + 1; // 1-based, 0 nếu không tìm thấy
                WriteNumber(node.OutputAddress, idx);
                LiveNodeStatusText[node.Id] = $"POS={idx}";
                return true;
            }

            // =========================================================================
            // NHÓM 11C: MULTI-BLOCK CALL (FC / FB)
            // =========================================================================
            case LadderElementKind.CallBlock:
            {
                if (!powerIn || _activeProgram is null) return false;
                var targetBlk = _activeProgram.FindBlockByRef(node.Address);
                if (targetBlk is null || targetBlk.Kind is PlcBlockKind.OB1_Main or PlcBlockKind.OB100_Startup)
                {
                    SetRuntimeError(0x80B0);
                    LiveNodeStatusText[node.Id] = "NO BLK";
                    return false;
                }
                if (_callDepth >= MaxCallDepth)
                {
                    SetRuntimeError(0x80B1);
                    LogDiagnostic(0x80B1, PlcDiagSeverity.Error, "BlockCall", $"Max CALL recursion depth ({MaxCallDepth}) exceeded at {targetBlk.DisplayLabel}.");
                    LiveNodeStatusText[node.Id] = "STACK OVF";
                    return false;
                }

                // Lưu ngữ cảnh nhảy cục bộ của khối cha
                string? savedJump = _pendingJumpLabel;
                bool savedRet = _returnRequested;
                _pendingJumpLabel = null;
                _returnRequested = false;
                _callDepth++;
                try
                {
                    ExecuteNetworkList(targetBlk.Networks, dt);
                }
                finally
                {
                    _callDepth--;
                    _pendingJumpLabel = savedJump;
                    _returnRequested = savedRet;
                }

                LiveNodeStatusText[node.Id] = $"CALL {targetBlk.Name}";
                return !CpuStopRequested;
            }

            // =========================================================================
            // NHÓM 12: TECHNOLOGY INSTRUCTIONS (CTRL_PWM, CTRL_HSC, PID_Compact)
            // =========================================================================
            case LadderElementKind.CtrlPwm:
            {
                if (!powerIn) return false;
                int ch = (int)Math.Round(ReadNumber(node.Param1));
                if (ch < 1 || ch > 4)
                {
                    WriteTypedValue(node.OutputAddress, PlcValue.FromWord(0x80A1)); // Invalid PWM ID (Part 2)
                    SetRuntimeError(0x80A1);
                    LiveNodeStatusText[node.Id] = "ERR 80A1";
                    return false;
                }
                bool enable = string.IsNullOrWhiteSpace(node.Param2) || ReadBit(node.Param2);
                double freqHz = string.IsNullOrWhiteSpace(node.ExtraParam) ? 10.0 : ReadNumber(node.ExtraParam, PlcDataType.Real);

                if (!PwmChannels.TryGetValue(ch, out var pwm))
                    PwmChannels[ch] = pwm = new PlcPwmChannelState { Channel = ch, OutputDqIndex = ch - 1 };

                pwm.Enabled = enable;
                pwm.FrequencyHz = Math.Clamp(freqHz, 0.5, 50.0);
                pwm.StatusWord = 0;

                if (enable) _protectedPwmDqIndices.Add(pwm.OutputDqIndex);
                else _protectedPwmDqIndices.Remove(pwm.OutputDqIndex);

                WriteTypedValue(node.OutputAddress, PlcValue.FromWord(0));
                LiveNodeStatusText[node.Id] = enable ? $"PWM{ch}:{_pwmWidthWords[ch - 1] / 10.0:0.0}%" : $"PWM{ch}:OFF";
                return true;
            }

            case LadderElementKind.CtrlHsc:
            {
                if (!powerIn) return false;
                int ch = (int)Math.Round(ReadNumber(node.Param1));
                if (ch < 1 || ch > 6)
                {
                    WriteTypedValue(node.OutputAddress, PlcValue.FromWord(0x80A1)); // Invalid HSC ID (Part 2)
                    SetRuntimeError(0x80A1);
                    LiveNodeStatusText[node.Id] = "ERR 80A1";
                    return false;
                }

                if (!HscChannels.TryGetValue(ch, out var hsc))
                    HscChannels[ch] = hsc = new PlcHscChannelState { Channel = ch, InputDiIndex = ch - 1 };

                bool wasDisabled = !hsc.Enabled;
                hsc.Enabled = true;
                _protectedHscDiIndices.Add(hsc.InputDiIndex);

                // Tùy chọn nạp NEW_CV và DIR,RV
                if (!string.IsNullOrWhiteSpace(node.Param2) && wasDisabled)
                {
                    hsc.CurrentValue = (int)Math.Round(ReadNumber(node.Param2));
                    _hscCurrentValues[ch - 1] = hsc.CurrentValue;
                }
                var cfg = (node.ExtraParam ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (cfg.Length >= 1) hsc.Direction = (int)Math.Round(ReadNumber(cfg[0])) >= 0 ? 1 : -1;
                if (cfg.Length >= 2) hsc.ReferenceValue = (int)Math.Round(ReadNumber(cfg[1]));

                hsc.StatusWord = 0;
                WriteTypedValue(node.OutputAddress, PlcValue.FromWord(0));
                LiveNodeStatusText[node.Id] = $"HSC{ch}={hsc.CurrentValue}";
                return true;
            }

            case LadderElementKind.PidCompact:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                if (string.IsNullOrWhiteSpace(dbKey) || dbKey == "<???>") return false;
                if (!PidDbs.TryGetValue(dbKey, out var pid))
                    PidDbs[dbKey] = pid = new PlcPidCompactState();

                if (!powerIn)
                {
                    pid.Mode = 0;
                    pid.State = 0;
                    pid.IntegralTerm = 0.0;
                    LiveNodeStatusText[node.Id] = "PID:OFF";
                    return false;
                }

                pid.Mode = 3;
                pid.State = 3;
                pid.Setpoint = ReadNumber(node.Param1, PlcDataType.Real);
                pid.Input = ReadNumber(node.Param2, PlcDataType.Real);

                var gains = (node.ExtraParam ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (gains.Length >= 1 && double.TryParse(gains[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double kp)) pid.Kp = kp;
                if (gains.Length >= 2 && double.TryParse(gains[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ti)) pid.Ti = ti;
                if (gains.Length >= 3 && double.TryParse(gains[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double td)) pid.Td = td;

                if (double.IsNaN(pid.Setpoint) || double.IsNaN(pid.Input) || double.IsInfinity(pid.Setpoint) || double.IsInfinity(pid.Input))
                {
                    pid.ErrorBits = 0x0001;
                    SetRuntimeError(0x8301);
                    LiveNodeStatusText[node.Id] = "PID:ERR";
                    return false;
                }

                pid.ErrorBits = 0;
                double err = pid.Setpoint - pid.Input;
                double pTerm = pid.Kp * err;
                double dTerm = dt > 1e-6 && pid.Td > 0.0 ? pid.Kp * pid.Td * (err - pid.PrevError) / dt : 0.0;
                double candidateI = pid.Ti > 1e-6 ? pid.IntegralTerm + (pid.Kp / pid.Ti) * err * dt : 0.0;
                double rawOut = pTerm + candidateI + dTerm;

                // Anti-windup: chỉ tích phân khi chưa bão hòa hoặc sai số kéo đầu ra về trong dải
                if ((rawOut >= pid.OutputMin && rawOut <= pid.OutputMax) ||
                    (rawOut > pid.OutputMax && err < 0) ||
                    (rawOut < pid.OutputMin && err > 0))
                {
                    pid.IntegralTerm = Math.Clamp(candidateI, pid.OutputMin - pTerm, pid.OutputMax - pTerm);
                }

                pid.Output = Math.Clamp(pTerm + pid.IntegralTerm + dTerm, pid.OutputMin, pid.OutputMax);
                pid.OutputPer = (short)Math.Clamp(Math.Round((pid.Output / 100.0) * 27648.0), 0, 27648);
                pid.PrevError = err;

                WriteTypedValue(node.OutputAddress, PlcValue.FromReal((float)pid.Output));
                LiveNodeStatusText[node.Id] = $"OUT={pid.Output:0.0}%";
                return true;
            }

            // =========================================================================
            // NHÓM 13: COMMUNICATION (Modbus_Comm_Load, Modbus_Master, MB_CLIENT -> VFD)
            // =========================================================================
            case LadderElementKind.ModbusCommLoad:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                if (string.IsNullOrWhiteSpace(dbKey) || dbKey == "<???>") return false;
                if (!ModbusDbs.TryGetValue(dbKey, out var mb))
                    ModbusDbs[dbKey] = mb = new PlcModbusState();

                if (!powerIn) return false;
                mb.PortConfigured = true;
                mb.BaudRate = (int)Math.Round(ReadNumber(node.Param2));
                mb.Parity = (int)Math.Round(ReadNumber(node.ExtraParam));
                mb.Done = true;
                mb.Error = false;
                mb.Status = 0;
                WriteTypedValue(node.OutputAddress, PlcValue.FromWord(0));
                LiveNodeStatusText[node.Id] = $"OK {mb.BaudRate}";
                return true;
            }

            case LadderElementKind.ModbusMaster:
            case LadderElementKind.ModbusTcpClient:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                if (string.IsNullOrWhiteSpace(dbKey) || dbKey == "<???>") return false;
                if (!ModbusDbs.TryGetValue(dbKey, out var mb))
                    ModbusDbs[dbKey] = mb = new PlcModbusState();

                if (!powerIn)
                {
                    mb.Done = false;
                    mb.Busy = false;
                    mb.Error = false;
                    mb.PrevReq = false;
                    LiveNodeStatusText[node.Id] = "IDLE";
                    return false;
                }

                int slaveId = Math.Clamp((int)Math.Round(ReadNumber(node.Param1)), 1, 247);
                int mode = (int)Math.Round(ReadNumber(node.Param2)); // 0=Read, 1=Write
                var addrLen = (node.ExtraParam ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int dataAddr = addrLen.Length >= 1 ? (int)Math.Round(ReadNumber(addrLen[0])) : 40001;
                int dataLen = addrLen.Length >= 2 ? Math.Clamp((int)Math.Round(ReadNumber(addrLen[1])), 1, 16) : 1;

                var ptrOp = ResolveOp(node.OutputAddress);
                if (!ptrOp.IsValid || ptrOp.AccessSizeBytes <= 0)
                {
                    mb.Error = true;
                    mb.Status = 0x818B;
                    SetRuntimeError(0x818B);
                    LiveNodeStatusText[node.Id] = "ERR 818B";
                    return false;
                }

                ushort[] buf = new ushort[dataLen];
                if (mode != 0)
                {
                    for (int i = 0; i < dataLen; i++)
                    {
                        if (TryReadRawBytes(ptrOp.Area, ptrOp.DbNumber, ptrOp.ByteOffset + i * 2, 2, false, out uint raw))
                            buf[i] = (ushort)(raw & 0xFFFF);
                    }
                }

                bool ok = ModbusBus.TryExecuteRequest(slaveId, mode, dataAddr, dataLen, buf, out ushort status, out ushort crc);
                mb.LastCrc16 = crc;
                mb.Status = status;
                mb.Done = ok;
                mb.Error = !ok;
                mb.PrevReq = true;

                if (ok && mode == 0)
                {
                    for (int i = 0; i < dataLen; i++)
                        TryWriteRawBytes(ptrOp.Area, ptrOp.DbNumber, ptrOp.ByteOffset + i * 2, 2, false, buf[i]);
                }
                else if (!ok)
                {
                    SetRuntimeError(status);
                    LogDiagnostic(status, PlcDiagSeverity.Error, "Modbus",
                        $"Modbus {(mode == 0 ? "READ" : "WRITE")} Slave={slaveId} Addr={dataAddr} failed (16#{status:X4}).");
                }

                LiveNodeStatusText[node.Id] = ok
                    ? $"S{slaveId}:{(mode == 0 ? "R" : "W")}{dataAddr} CRC={crc:X4}"
                    : $"ERR {status:X4}";
                return ok;
            }

            default:
                handled = false;
                return false;
        }
    }
}
