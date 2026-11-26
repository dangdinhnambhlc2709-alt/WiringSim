using System.Buffers.Binary;
using System.Globalization;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Plc;

public sealed class PlcTimerDbState
{
    public double ElapsedSeconds { get; set; }
    public double PresetSeconds { get; set; }
    public double LatchedPresetSeconds { get; set; }
    public bool In { get; set; }
    public bool Q { get; set; }
    public bool PrevIn { get; set; }
    public bool PulseActive { get; set; }
    public bool TimingActive { get; set; }
}

public sealed class PlcCounterDbState
{
    public long Cv { get; set; }
    public long Pv { get; set; }
    public PlcDataType DataType { get; set; } = PlcDataType.Int;
    public bool Cu { get; set; }
    public bool Cd { get; set; }
    public bool R { get; set; }
    public bool Ld { get; set; }
    public bool Qu { get; set; }
    public bool Qd { get; set; }
    public bool PrevCu { get; set; }
    public bool PrevCd { get; set; }
}

/// <summary>
/// Bộ biên dịch chương trình Ladder chuẩn IEC 61131-3 dùng chung OperandResolver với Runtime:
/// - Kiểm tra nhánh hở, toán hạng hợp lệ (Bool, Byte, Word, DWord, SInt/USInt, Int/UInt, DInt/UDInt, Real, Time).
/// - Hỗ trợ hằng số thời gian T#... ở Timer PT, Comparator và Math; hằng số 2#/8#/16#; hậu tố :P; thành viên DB &amp; Slice (.x/.b/.w).
/// - Kiểm tra biểu thức CALCULATE, chân mở rộng IN3..INn, Empty box chưa phân giải, trùng %DB, đích nhảy LABEL / vòng lặp vô hạn.
/// </summary>
public static class PlcProgramCompiler
{
    public static bool TryParseTimeSeconds(string? raw, out double seconds) =>
        OperandResolver.TryParseTimeSeconds(raw, out seconds);

    public static bool IsValidBitAddress(string? raw, bool allowInputsAndDb, out string errorReason, PlcProgramModel? program = null)
    {
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrEmptyAddr");
            return false;
        }

        var op = OperandResolver.Resolve(raw, program);
        if (!op.IsValid)
        {
            errorReason = op.ErrorMessage;
            return false;
        }

        if (op.IsDbInstance)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrDbMemberRequired", raw);
            return false;
        }

        if (!op.IsBitAccess)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw);
            return false;
        }

        if (!allowInputsAndDb)
        {
            if (op.Area == PlcMemoryArea.I)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrCannotWriteInput", raw);
                return false;
            }
            if (op.IsSystemOrClockBit)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrWriteSysClkBit", raw);
                return false;
            }
            if (!op.IsWritable)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrNotWritableBit", raw);
                return false;
            }
        }
        else
        {
            if (!op.IsReadable)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrCannotReadImmediateOut", raw);
                return false;
            }
        }

        return true;
    }

    public static bool IsValidNumericOrAddress(string? raw, bool mustBeWritable, out string errorReason, PlcProgramModel? program = null)
    {
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrMissingOperand");
            return false;
        }

        var op = OperandResolver.Resolve(raw, program);
        if (!op.IsValid)
        {
            errorReason = op.ErrorMessage;
            return false;
        }

        if (op.IsBitAccess || op.IsDbInstance)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", raw);
            return false;
        }

        if (mustBeWritable)
        {
            if (op.IsSystemOrClockBit)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrWriteSysClkBit", raw);
                return false;
            }
            if (op.IsLiteral || !op.IsWritable)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrTargetMustBeWord", raw);
                return false;
            }
        }
        else if (!op.IsReadable)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrCannotReadImmediateOut", raw);
            return false;
        }

        return true;
    }

    public static bool IsValidTimeOperand(string? raw, out string errorReason, PlcProgramModel? program = null)
    {
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrMissingOperand");
            return false;
        }

        if (OperandResolver.TryParseTimeMs(raw, out int ms))
        {
            if (ms < 0)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidPt", 0, raw);
                return false;
            }
            return true;
        }

        // Cho phép truyền biến kiểu Time / DInt / Word vào PT (VD: %MD10, "DELAY_TIME", "T1".PT)
        var op = OperandResolver.Resolve(raw, program);
        if (op.IsValid && op.IsReadable && !op.IsBitAccess &&
            (op.InferredType is PlcDataType.Time or PlcDataType.DInt or PlcDataType.Int or PlcDataType.UInt or PlcDataType.UDInt or PlcDataType.Word or PlcDataType.DWord))
        {
            return true;
        }

        errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidPt", 0, raw);
        return false;
    }

    public static bool IsValidDbInstanceAddress(string? raw, out string normalizedDb, out string errorReason, PlcProgramModel? program = null)
    {
        normalizedDb = string.Empty;
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrMissingDb");
            return false;
        }

        var op = OperandResolver.Resolve(raw, program);
        if (!op.IsValid || op.Area != PlcMemoryArea.DB || op.DbNumber <= 0 || op.AccessSizeBytes > 0 || !string.IsNullOrEmpty(op.DbMember))
        {
            errorReason = op.IsValid ? LocalizationManager.Format("Str.Plc.ErrInvalidDb", raw) : op.ErrorMessage;
            return false;
        }

        normalizedDb = $"%DB{op.DbNumber}";
        return true;
    }

    public static PlcCompileResult Compile(PlcProgramModel program)
    {
        var result = new PlcCompileResult();

        // Kiểm tra trùng Byte giữa System Memory và Clock Memory
        if (program.EnableSystemMemory && program.EnableClockMemory && program.SystemMemoryByte == program.ClockMemoryByte)
        {
            result.Diagnostics.Add(new PlcDiagnostic(true, 0, null,
                LocalizationManager.Format("Str.Plc.ErrSysClkOverlap", program.SystemMemoryByte)));
        }

        if (program.Networks.Count == 0)
        {
            result.Diagnostics.Add(new PlcDiagnostic(false, 0, null, LocalizationManager.Get("Str.Plc.WarnNoNetworks")));
            return result;
        }

        // Lượt 1: Thu thập toàn bộ LABEL định nghĩa trong OB1, kiểm tra trùng tên và vị trí hợp lệ
        var definedLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < program.Networks.Count; i++)
            CollectLabels(program.Networks[i].MainBranch, i + 1, isMainRoot: true, definedLabels, result);

        var seenDbs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < program.Networks.Count; i++)
        {
            int netNum = i + 1;
            var net = program.Networks[i];
            if (net.MainBranch.Nodes.Count == 0)
            {
                result.Diagnostics.Add(new PlcDiagnostic(false, netNum, null,
                    LocalizationManager.Format("Str.Plc.WarnEmptyNetwork", netNum)));
                continue;
            }

            bool hasOutputOrLabel = ValidateBranch(program, net.MainBranch, netNum, hasConditionBefore: false, seenDbs, definedLabels, result);
            if (!hasOutputOrLabel)
            {
                result.Diagnostics.Add(new PlcDiagnostic(true, netNum, net.MainBranch.Nodes[^1].Id,
                    LocalizationManager.Format("Str.Plc.ErrOpenBranchNoOutput", netNum)));
            }
        }

        // Lượt 2: Kiểm tra toàn bộ các khối bổ sung (OB100 Startup, FC, FB)
        foreach (var blk in program.AdditionalBlocks)
        {
            var blkLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < blk.Networks.Count; i++)
                CollectLabels(blk.Networks[i].MainBranch, i + 1, isMainRoot: true, blkLabels, result);

            for (int i = 0; i < blk.Networks.Count; i++)
            {
                int netNum = i + 1;
                var net = blk.Networks[i];
                if (net.MainBranch.Nodes.Count == 0) continue;
                bool hasOut = ValidateBranch(program, net.MainBranch, netNum, hasConditionBefore: false, seenDbs, blkLabels, result);
                if (!hasOut)
                {
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, net.MainBranch.Nodes[^1].Id,
                        $"[{blk.DisplayLabel}] {LocalizationManager.Format("Str.Plc.ErrOpenBranchNoOutput", netNum)}"));
                }
            }
        }

        return result;
    }

    private static void CollectLabels(
        LadderBranch branch,
        int netNum,
        bool isMainRoot,
        Dictionary<string, int> labels,
        PlcCompileResult result)
    {
        for (int idx = 0; idx < branch.Nodes.Count; idx++)
        {
            var node = branch.Nodes[idx];
            if (node.Kind == LadderElementKind.ParallelBlock && node.Branches is not null)
            {
                foreach (var sub in node.Branches) CollectLabels(sub, netNum, isMainRoot: false, labels, result);
            }
            else if (node.Kind == LadderElementKind.Label && !string.IsNullOrWhiteSpace(node.Address))
            {
                string lbl = node.Address.Trim();
                if (!isMainRoot || idx != 0)
                {
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrLabelPosition", netNum, lbl)));
                }
                if (labels.TryGetValue(lbl, out int prevNet))
                {
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrDuplicateLabel", netNum, lbl, prevNet)));
                }
                else
                {
                    labels[lbl] = netNum;
                }
            }
        }
    }

    private static bool ValidateBranch(
        PlcProgramModel program,
        LadderBranch branch,
        int netNum,
        bool hasConditionBefore,
        Dictionary<string, int> seenDbs,
        Dictionary<string, int> definedLabels,
        PlcCompileResult result)
    {
        bool hasAnyOutput = false;
        bool condSeen = hasConditionBefore;

        for (int idx = 0; idx < branch.Nodes.Count; idx++)
        {
            var node = branch.Nodes[idx];
            bool isLastInBranch = idx == branch.Nodes.Count - 1;

            if (node.Kind == LadderElementKind.ParallelBlock)
            {
                if (node.Branches is null || node.Branches.Count == 0)
                {
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrEmptyParallel", netNum)));
                    continue;
                }

                bool anySubHasOutput = false;
                bool allSubsHaveOutput = true;
                foreach (var sub in node.Branches)
                {
                    if (sub.Nodes.Count == 0)
                    {
                        result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                            LocalizationManager.Format("Str.Plc.ErrEmptyParallel", netNum)));
                        allSubsHaveOutput = false;
                        continue;
                    }
                    bool subOut = ValidateBranch(program, sub, netNum, condSeen, seenDbs, definedLabels, result);
                    if (subOut) anySubHasOutput = true;
                    else allSubsHaveOutput = false;
                }

                if (anySubHasOutput && !allSubsHaveOutput)
                {
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrOpenBranchNoOutput", netNum)));
                }
                if (anySubHasOutput) hasAnyOutput = true;
                else condSeen = true;
                continue;
            }

            if (node.Kind == LadderElementKind.EmptyBox)
            {
                result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                    LocalizationManager.Format("Str.Plc.ErrEmptyBoxUnresolved", netNum, node.Address)));
                continue;
            }

            // Kiểm tra quy tắc vị trí chuẩn LAD (Part 1)
            if (node.Kind is LadderElementKind.PTrig or LadderElementKind.NTrig)
            {
                if (!condSeen || isLastInBranch)
                {
                    string sym = node.Kind == LadderElementKind.PTrig ? "P_TRIG" : "N_TRIG";
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrTrigPosition", netNum, sym)));
                }
            }
            else if (node.Kind is LadderElementKind.SetBitField or LadderElementKind.ResetBitField)
            {
                if (!isLastInBranch)
                {
                    string sym = node.Kind == LadderElementKind.SetBitField ? "SET_BF" : "RESET_BF";
                    result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id,
                        LocalizationManager.Format("Str.Plc.ErrBitFieldPosition", netNum, sym)));
                }
            }

            bool condBeforeNode = condSeen;
            if (node.IsOutputOrAction || node.Kind == LadderElementKind.Label)
                hasAnyOutput = true;
            else
                condSeen = true;

            ValidateSingleNode(program, node, netNum, condBeforeNode, seenDbs, definedLabels, result);
        }

        return hasAnyOutput;
    }

    private static void ValidateSingleNode(
        PlcProgramModel program,
        LadderNode node,
        int netNum,
        bool hasConditionBefore,
        Dictionary<string, int> seenDbs,
        Dictionary<string, int> definedLabels,
        PlcCompileResult result)
    {
        void Err(string msg) => result.Diagnostics.Add(new PlcDiagnostic(true, netNum, node.Id, msg));

        switch (node.Kind)
        {
            case LadderElementKind.ContactNO:
            case LadderElementKind.ContactNC:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: true, out string cErr, program)) Err($"NW{netNum}: {cErr}");
                break;

            case LadderElementKind.InvertRLO:
            case LadderElementKind.Return:
            case LadderElementKind.ReTrigr:
            case LadderElementKind.StopCpu:
                break;

            case LadderElementKind.CoilOut:
            case LadderElementKind.CoilNegate:
            case LadderElementKind.CoilSet:
            case LadderElementKind.CoilReset:
            case LadderElementKind.PTrig:
            case LadderElementKind.NTrig:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string qErr, program)) Err($"NW{netNum}: {qErr}");
                break;

            case LadderElementKind.EdgePositive:
            case LadderElementKind.EdgeNegative:
            case LadderElementKind.CoilEdgePositive:
            case LadderElementKind.CoilEdgeNegative:
            {
                bool allowReadOnlyAddr = node.Kind is LadderElementKind.EdgePositive or LadderElementKind.EdgeNegative && !string.IsNullOrWhiteSpace(node.Param1);
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: allowReadOnlyAddr, out string eErr, program))
                    Err($"NW{netNum}: {eErr}");
                if (!string.IsNullOrWhiteSpace(node.Param1) && !IsValidBitAddress(node.Param1, allowInputsAndDb: false, out string mErr, program))
                    Err($"NW{netNum} (M-Bit): {mErr}");
                break;
            }

            case LadderElementKind.SetBitField:
            case LadderElementKind.ResetBitField:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string bfErr, program)) Err($"NW{netNum}: {bfErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string nErr, program)) Err($"NW{netNum} (N): {nErr}");
                break;

            case LadderElementKind.FlipFlopSR:
            case LadderElementKind.FlipFlopRS:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string ffErr, program)) Err($"NW{netNum} (Q): {ffErr}");
                if (!string.IsNullOrWhiteSpace(node.Param1) && !IsValidBitAddress(node.Param1, allowInputsAndDb: true, out string ff2Err, program))
                    Err($"NW{netNum} (R1/S1): {ff2Err}");
                break;

            case LadderElementKind.RTrig:
            case LadderElementKind.FTrig:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                break;

            case LadderElementKind.TimerTP:
            case LadderElementKind.TimerTON:
            case LadderElementKind.TimerTOF:
            case LadderElementKind.TimerTONR:
            case LadderElementKind.CoilTP:
            case LadderElementKind.CoilTON:
            case LadderElementKind.CoilTOF:
            case LadderElementKind.CoilTONR:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidTimeOperand(node.Param1, out _, program))
                    Err(LocalizationManager.Format("Str.Plc.ErrInvalidPt", netNum, node.Param1));
                if (node.Kind == LadderElementKind.TimerTONR && !string.IsNullOrWhiteSpace(node.Param2) &&
                    !IsValidBitAddress(node.Param2, true, out string rErr, program))
                    Err($"NW{netNum} (R): {rErr}");
                break;

            case LadderElementKind.CoilRT:
                if (!IsValidDbInstanceAddress(node.Address, out string rtDbNorm, out string rtErr, program))
                    Err($"NW{netNum}: {rtErr}");
                else
                    CheckTimerDbType(program, rtDbNorm, "RT", netNum, Err);
                break;

            case LadderElementKind.CoilPT:
                if (!IsValidDbInstanceAddress(node.Address, out string ptDbNorm, out string ptDbErr, program))
                    Err($"NW{netNum}: {ptDbErr}");
                else
                    CheckTimerDbType(program, ptDbNorm, "PT", netNum, Err);
                if (!IsValidTimeOperand(node.Param1, out _, program)) Err(LocalizationManager.Format("Str.Plc.ErrInvalidPt", netNum, node.Param1));
                break;

            case LadderElementKind.CounterCTU:
            case LadderElementKind.CounterCTD:
            case LadderElementKind.CounterCTUD:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out _, program))
                    Err(LocalizationManager.Format("Str.Plc.ErrInvalidPv", netNum, node.Param1));
                if (!string.IsNullOrWhiteSpace(node.Param2) && !IsValidBitAddress(node.Param2, true, out string crErr, program))
                    Err($"NW{netNum} (R/LD): {crErr}");
                if (node.Kind == LadderElementKind.CounterCTUD)
                {
                    if (!string.IsNullOrWhiteSpace(node.OutputAddress) && !IsValidBitAddress(node.OutputAddress, true, out string cdErr, program))
                        Err($"NW{netNum} (CD): {cdErr}");
                    if (!string.IsNullOrWhiteSpace(node.ExtraParam) && !IsValidBitAddress(node.ExtraParam, true, out string ldErr, program))
                        Err($"NW{netNum} (LD): {ldErr}");
                }
                break;

            case LadderElementKind.CmpEqual:
            case LadderElementKind.CmpNotEqual:
            case LadderElementKind.CmpGreaterOrEqual:
            case LadderElementKind.CmpLessOrEqual:
            case LadderElementKind.CmpGreater:
            case LadderElementKind.CmpLess:
                if (!IsValidNumericOrAddress(node.Param1, false, out string c1Err, program)) Err($"NW{netNum} (IN1): {c1Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string c2Err, program)) Err($"NW{netNum} (IN2): {c2Err}");
                break;

            case LadderElementKind.InRange:
            case LadderElementKind.OutRange:
                if (!IsValidNumericOrAddress(node.Param1, false, out string vErr, program)) Err($"NW{netNum} (VAL): {vErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string mnErr, program)) Err($"NW{netNum} (MIN): {mnErr}");
                if (!IsValidNumericOrAddress(node.ExtraParam, false, out string mxErr, program)) Err($"NW{netNum} (MAX): {mxErr}");
                break;

            case LadderElementKind.CheckOk:
            case LadderElementKind.CheckNotOk:
                if (!IsValidNumericOrAddress(node.Address, false, out string okErr, program)) Err($"NW{netNum}: {okErr}");
                break;

            case LadderElementKind.VarEqType:
            case LadderElementKind.VarNeType:
            case LadderElementKind.VarEqElemType:
            case LadderElementKind.VarNeElemType:
                if (string.IsNullOrWhiteSpace(node.Param1)) Err($"NW{netNum} (SRC1): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                else if (!OperandResolver.Resolve(node.Param1, program).IsValid) Err($"NW{netNum} (SRC1): {OperandResolver.Resolve(node.Param1, program).ErrorMessage}");
                if (string.IsNullOrWhiteSpace(node.Param2)) Err($"NW{netNum} (SRC2): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                else if (!OperandResolver.Resolve(node.Param2, program).IsValid) Err($"NW{netNum} (SRC2): {OperandResolver.Resolve(node.Param2, program).ErrorMessage}");
                break;

            case LadderElementKind.VarIsNull:
            case LadderElementKind.VarNotNull:
            case LadderElementKind.VarIsArray:
                if (string.IsNullOrWhiteSpace(node.Param1)) Err($"NW{netNum} (IN): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                break;

            case LadderElementKind.VarEqTypeOfDb:
            case LadderElementKind.VarNeTypeOfDb:
                if (!IsValidDbInstanceAddress(node.Param1, out _, out string vdbErr, program)) Err($"NW{netNum} (DB): {vdbErr}");
                if (string.IsNullOrWhiteSpace(node.Param2)) Err($"NW{netNum} (TYPE): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                break;

            case LadderElementKind.Calculate:
                if (!PlcExpressionEvaluator.TryValidate(node.ExtraParam, out int reqInputs, out string calcErr))
                    Err($"NW{netNum} (CALCULATE): {calcErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string ci1Err, program)) Err($"NW{netNum} (IN1): {ci1Err}");
                if (reqInputs >= 2 && !IsValidNumericOrAddress(node.Param2, false, out string ci2Err, program)) Err($"NW{netNum} (IN2): {ci2Err}");
                for (int k = 3; k <= reqInputs; k++)
                {
                    string extraVal = node.ExtraInputs is not null && (k - 3) < node.ExtraInputs.Count ? node.ExtraInputs[k - 3] : string.Empty;
                    if (!IsValidNumericOrAddress(extraVal, false, out string exErr, program))
                        Err($"NW{netNum} (IN{k}): {exErr}");
                }
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string coErr, program)) Err($"NW{netNum} (OUT): {coErr}");
                break;

            case LadderElementKind.MathLimit:
                if (!IsValidNumericOrAddress(node.Param1, false, out string limInErr, program)) Err($"NW{netNum} (IN): {limInErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string limMnErr, program)) Err($"NW{netNum} (MN): {limMnErr}");
                if (!IsValidNumericOrAddress(node.ExtraParam, false, out string limMxErr, program)) Err($"NW{netNum} (MX): {limMxErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string limOErr, program)) Err($"NW{netNum} (OUT): {limOErr}");
                break;

            case LadderElementKind.Move:
                if (!IsValidNumericOrAddress(node.Param1, false, out string mvInErr, program)) Err($"NW{netNum} (IN): {mvInErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string mvO1Err, program)) Err($"NW{netNum} (OUT1): {mvO1Err}");
                if (node.ExtraOutputs is not null)
                {
                    for (int k = 0; k < node.ExtraOutputs.Count; k++)
                    {
                        if (!IsValidNumericOrAddress(node.ExtraOutputs[k], true, out string mvOkErr, program))
                            Err($"NW{netNum} (OUT{k + 2}): {mvOkErr}");
                    }
                }
                break;

            case LadderElementKind.RuntimeMeasure:
                if (!IsValidNumericOrAddress(node.Param1, true, out string rtMemErr, program)) Err($"NW{netNum} (MEM): {rtMemErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string rtOutErr, program)) Err($"NW{netNum} (OUT): {rtOutErr}");
                break;

            case LadderElementKind.NormX:
            case LadderElementKind.ScaleX:
                if (!IsValidNumericOrAddress(node.Param1, false, out string nxvErr, program)) Err($"NW{netNum} (VALUE): {nxvErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string nxmnErr, program)) Err($"NW{netNum} (MIN): {nxmnErr}");
                if (!IsValidNumericOrAddress(node.ExtraParam, false, out string nxmxErr, program)) Err($"NW{netNum} (MAX): {nxmxErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string nxoErr, program)) Err($"NW{netNum} (OUT): {nxoErr}");
                break;

            case LadderElementKind.Label:
                if (string.IsNullOrWhiteSpace(node.Address))
                    Err(LocalizationManager.Format("Str.Plc.ErrEmptyLabel", netNum));
                break;

            case LadderElementKind.Jump:
            case LadderElementKind.JumpNot:
                ValidateJumpTargets([node.Address], netNum, isUnconditional: node.Kind == LadderElementKind.Jump && !hasConditionBefore, definedLabels, Err);
                break;

            case LadderElementKind.JumpList:
            case LadderElementKind.Switch:
            {
                var lbls = (node.Address ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lbls.Length == 0)
                    Err(LocalizationManager.Format("Str.Plc.ErrEmptyLabel", netNum));
                else
                    ValidateJumpTargets(lbls, netNum, isUnconditional: false, definedLabels, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string kErr, program)) Err($"NW{netNum} (K): {kErr}");
                break;
            }

            case LadderElementKind.MathInc:
            case LadderElementKind.MathDec:
            case LadderElementKind.GetError:
            case LadderElementKind.GetErrId:
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string ioErr, program)) Err($"NW{netNum} (OUT): {ioErr}");
                break;

            case LadderElementKind.Scatter:
            case LadderElementKind.ScatterBlk:
                if (!IsValidNumericOrAddress(node.Param1, false, out string scInErr, program)) Err($"NW{netNum} (IN): {scInErr}");
                if (!IsValidBitAddress(node.OutputAddress, false, out string scOutErr, program)) Err($"NW{netNum} (OUT): {scOutErr}");
                break;

            case LadderElementKind.Gather:
            case LadderElementKind.GatherBlk:
                if (!IsValidBitAddress(node.Param1, true, out string gaInErr, program)) Err($"NW{netNum} (IN): {gaInErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string gaOutErr, program)) Err($"NW{netNum} (OUT): {gaOutErr}");
                break;

            case LadderElementKind.WordSel:
                if (!IsValidBitAddress(node.Address, true, out string selGErr, program)) Err($"NW{netNum} (G): {selGErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string sel0Err, program)) Err($"NW{netNum} (IN0): {sel0Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string sel1Err, program)) Err($"NW{netNum} (IN1): {sel1Err}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string selOErr, program)) Err($"NW{netNum} (OUT): {selOErr}");
                break;

            case LadderElementKind.WordMux:
                if (!IsValidNumericOrAddress(node.Address, false, out string muxKErr, program)) Err($"NW{netNum} (K): {muxKErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string mux0Err, program)) Err($"NW{netNum} (IN0): {mux0Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string mux1Err, program)) Err($"NW{netNum} (IN1): {mux1Err}");
                if (node.ExtraInputs is not null)
                {
                    for (int k = 0; k < node.ExtraInputs.Count; k++)
                    {
                        if (!IsValidNumericOrAddress(node.ExtraInputs[k], false, out string meErr, program))
                            Err($"NW{netNum} (IN{k + 2}): {meErr}");
                    }
                }
                if (!string.IsNullOrWhiteSpace(node.ExtraParam) && !IsValidNumericOrAddress(node.ExtraParam, false, out string muxElseErr, program))
                    Err($"NW{netNum} (ELSE): {muxElseErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string muxOErr, program)) Err($"NW{netNum} (OUT): {muxOErr}");
                break;

            case LadderElementKind.WordDemux:
                if (!IsValidNumericOrAddress(node.Address, false, out string dmKErr, program)) Err($"NW{netNum} (K): {dmKErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string dmInErr, program)) Err($"NW{netNum} (IN): {dmInErr}");
                if (!IsValidNumericOrAddress(node.Param2, true, out string dmO0Err, program)) Err($"NW{netNum} (OUT0): {dmO0Err}");
                if (!string.IsNullOrWhiteSpace(node.ExtraParam) && !IsValidNumericOrAddress(node.ExtraParam, true, out string dmO1Err, program))
                    Err($"NW{netNum} (OUT1): {dmO1Err}");
                if (!string.IsNullOrWhiteSpace(node.OutputAddress) && !IsValidNumericOrAddress(node.OutputAddress, true, out string dmO2Err, program))
                    Err($"NW{netNum} (OUT2): {dmO2Err}");
                break;

            // Nhóm 11: Extended (RTC, String, CALL)
            case LadderElementKind.ReadSysTime:
            case LadderElementKind.ReadLocTime:
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string rtcErr, program)) Err($"NW{netNum} (OUT): {rtcErr}");
                break;

            case LadderElementKind.TimeAdd:
            case LadderElementKind.TimeSub:
            case LadderElementKind.TimeDiff:
                if (!IsValidNumericOrAddress(node.Param1, false, out string ta1Err, program)) Err($"NW{netNum} (IN1): {ta1Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string ta2Err, program)) Err($"NW{netNum} (IN2): {ta2Err}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string taoErr, program)) Err($"NW{netNum} (OUT): {taoErr}");
                break;

            case LadderElementKind.TimeConv:
            case LadderElementKind.StringConv:
            case LadderElementKind.StringLen:
                if (!IsValidNumericOrAddress(node.Param1, false, out string sc1Err, program)) Err($"NW{netNum} (IN): {sc1Err}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string scoErr, program)) Err($"NW{netNum} (OUT): {scoErr}");
                break;

            case LadderElementKind.StringConcat:
            case LadderElementKind.StringLeft:
            case LadderElementKind.StringRight:
            case LadderElementKind.StringFind:
                if (!IsValidNumericOrAddress(node.Param1, false, out string st1Err, program)) Err($"NW{netNum} (IN1): {st1Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string st2Err, program)) Err($"NW{netNum} (IN2/L): {st2Err}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string stoErr, program)) Err($"NW{netNum} (OUT): {stoErr}");
                break;

            case LadderElementKind.StringMid:
            case LadderElementKind.StringDelete:
            case LadderElementKind.StringInsert:
            case LadderElementKind.StringReplace:
                if (!IsValidNumericOrAddress(node.Param1, false, out string sm1Err, program)) Err($"NW{netNum} (IN1): {sm1Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string sm2Err, program)) Err($"NW{netNum} (IN2/L): {sm2Err}");
                if (string.IsNullOrWhiteSpace(node.ExtraParam)) Err($"NW{netNum} (P): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string smoErr, program)) Err($"NW{netNum} (OUT): {smoErr}");
                break;

            case LadderElementKind.CallBlock:
            {
                var targetBlk = program.FindBlockByRef(node.Address);
                if (targetBlk is null || targetBlk.Kind is PlcBlockKind.OB1_Main or PlcBlockKind.OB100_Startup)
                    Err(LocalizationManager.Format("Str.Plc.ErrMissingBlock", netNum, node.Address));
                else if (targetBlk.Kind == PlcBlockKind.FB && !string.IsNullOrWhiteSpace(node.Param1))
                    CheckDbUnique(program, node.Param1, netNum, seenDbs, Err);
                break;
            }

            // Nhóm 12: Technology (CTRL_PWM, CTRL_HSC, PID_Compact)
            case LadderElementKind.CtrlPwm:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string pwmIdErr, program)) Err($"NW{netNum} (PWM): {pwmIdErr}");
                if (!string.IsNullOrWhiteSpace(node.Param2) && !IsValidBitAddress(node.Param2, true, out string pwmEnErr, program)) Err($"NW{netNum} (ENABLE): {pwmEnErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string pwmStErr, program)) Err($"NW{netNum} (STATUS): {pwmStErr}");
                break;

            case LadderElementKind.CtrlHsc:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string hscIdErr, program)) Err($"NW{netNum} (HSC): {hscIdErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string hscStErr, program)) Err($"NW{netNum} (STATUS): {hscStErr}");
                break;

            case LadderElementKind.PidCompact:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string pidSpErr, program)) Err($"NW{netNum} (Setpoint): {pidSpErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string pidInErr, program)) Err($"NW{netNum} (Input): {pidInErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string pidOutErr, program)) Err($"NW{netNum} (Output): {pidOutErr}");
                break;

            // Nhóm 13: Communication (Modbus RTU / TCP)
            case LadderElementKind.ModbusCommLoad:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string mbpErr, program)) Err($"NW{netNum} (PORT): {mbpErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string mbbErr, program)) Err($"NW{netNum} (BAUD): {mbbErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string mbsErr, program)) Err($"NW{netNum} (STATUS): {mbsErr}");
                break;

            case LadderElementKind.ModbusMaster:
            case LadderElementKind.ModbusTcpClient:
                CheckDbUnique(program, node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out string mbAddrErr, program)) Err($"NW{netNum} (MB_ADDR): {mbAddrErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string mbModeErr, program)) Err($"NW{netNum} (MODE): {mbModeErr}");
                if (string.IsNullOrWhiteSpace(node.ExtraParam)) Err($"NW{netNum} (ADDR,LEN): {LocalizationManager.Get("Str.Plc.ErrMissingOperand")}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string mbPtrErr, program)) Err($"NW{netNum} (DATA_PTR): {mbPtrErr}");
                break;

            default:
            {
                var meta = PlcInstructionCatalog.Get(node.Kind);
                if (meta is null || meta.GroupIndex == 4) break;
                if (meta.OutLabel is not null &&
                    !IsValidNumericOrAddress(node.OutputAddress, true, out string oErr, program))
                    Err($"NW{netNum} ({meta.OutLabel}): {oErr}");
                if (meta.Param1Label is not null &&
                    !IsValidNumericOrAddress(node.Param1, false, out string p1Err, program))
                    Err($"NW{netNum} ({meta.Param1Label}): {p1Err}");
                if (meta.Param2Label is not null &&
                    !IsValidNumericOrAddress(node.Param2, false, out string p2Err, program))
                    Err($"NW{netNum} ({meta.Param2Label}): {p2Err}");

                if (PlcInstructionCatalog.SupportsMultiInput(node.Kind) && node.ExtraInputs is not null)
                {
                    for (int k = 0; k < node.ExtraInputs.Count; k++)
                    {
                        string lbl = PlcInstructionCatalog.GetExtraInputLabel(node.Kind, k);
                        if (!IsValidNumericOrAddress(node.ExtraInputs[k], false, out string exErr, program))
                            Err($"NW{netNum} ({lbl}): {exErr}");
                    }
                }
                break;
            }
        }
    }

    private static void CheckTimerDbType(PlcProgramModel program, string dbNorm, string instrName, int netNum, Action<string> onError)
    {
        var db = program.GetAllDataBlocks().FirstOrDefault(d =>
            PlcProgramModel.NormalizeAddress(d.Address).Equals(dbNorm, StringComparison.OrdinalIgnoreCase));
        if (db is not null && !string.IsNullOrWhiteSpace(db.DataType) &&
            !db.DataType.Contains("Timer", StringComparison.OrdinalIgnoreCase))
        {
            onError(LocalizationManager.Format("Str.Plc.ErrTimerDbRequired", netNum, instrName, dbNorm, db.DataType));
        }
    }

    private static void CheckDbUnique(PlcProgramModel program, string? rawDb, int netNum, Dictionary<string, int> seenDbs, Action<string> onError)
    {
        if (string.Equals(rawDb?.Trim(), "<???>", StringComparison.Ordinal))
        {
            onError(LocalizationManager.Format("Str.Plc.ErrUnassignedDb", netNum));
            return;
        }

        if (!IsValidDbInstanceAddress(rawDb, out string dbNorm, out string dbErr, program))
        {
            onError($"NW{netNum}: {dbErr}");
        }
        else if (seenDbs.TryGetValue(dbNorm, out int prevNw))
        {
            onError(LocalizationManager.Format("Str.Plc.ErrDuplicateDb", netNum, dbNorm, prevNw));
        }
        else
        {
            seenDbs[dbNorm] = netNum;
        }
    }

    private static void ValidateJumpTargets(
        IEnumerable<string> targetLabels,
        int netNum,
        bool isUnconditional,
        Dictionary<string, int> definedLabels,
        Action<string> onError)
    {
        foreach (string rawLbl in targetLabels)
        {
            string lbl = rawLbl.Trim();
            if (string.IsNullOrEmpty(lbl))
            {
                onError(LocalizationManager.Format("Str.Plc.ErrEmptyLabel", netNum));
                continue;
            }

            if (!definedLabels.TryGetValue(lbl, out int targetNet))
            {
                onError(LocalizationManager.Format("Str.Plc.ErrMissingLabel", netNum, lbl));
                continue;
            }

            if (targetNet == netNum || (isUnconditional && targetNet < netNum))
            {
                onError(LocalizationManager.Format("Str.Plc.ErrInfiniteJumpLoop", netNum, lbl, targetNet));
            }
        }
    }
}

/// <summary>
/// Nhân thực thi vòng quét PLC 4 pha chuẩn IEC 61131-3 (50ms):
/// - Pha 1: Đọc ngõ vào vật lý (DI/AI) vào bảng đệm ảnh quá trình I + cập nhật System/Clock Memory.
/// - Pha 2: Thực thi tuần tự Network 1..N với EN/ENO nghiêm ngặt, JMP/JMPN/RET (tối đa 100 bước nhảy/vòng quét), truy xuất tức thời :P, Slice Access &amp; DB tuyệt đối.
/// - Pha 3: Xuất bảng đệm ảnh quá trình Q ra ngõ ra vật lý DQ.
/// - Pha 4: Cập nhật lịch sử vòng quét trước cho các lệnh bắt sườn (P, N, P_TRIG, N_TRIG, R_TRIG, F_TRIG) và xóa cờ FirstScan.
/// </summary>
public sealed partial class PlcRuntimeEngine
{
    public const int MaxJumpsPerScan = 100;

    // Ngõ vào / ra vật lý (phía phần cứng PLC)
    public bool[] PhysicalDi { get; } = new bool[14];
    public int[] PhysicalAi { get; } = new int[2];
    public bool[] PhysicalDq { get; } = new bool[10];

    // Thuộc tính tương thích trực tiếp với ComponentRenderer / PlcControllerComponent
    public bool[] Di => PhysicalDi;
    public bool[] Dq => PhysicalDq;
    public int[] Ai => PhysicalAi;

    // Bảng đệm ảnh quá trình (Process Image I & Q) và bộ nhớ Byte thống nhất (Big-Endian IEC)
    private readonly byte[] _imageI = new byte[72];  // IB0..IB1, IW64, IW66, ID0..ID68
    private readonly byte[] _imageQ = new byte[20];  // QB0..QB1, QW0..QW16, QD0..QD16
    private readonly byte[] _memoryM = new byte[OperandResolver.MaxMemoryBytes]; // MB0..MB199
    private readonly Dictionary<int, byte[]> _rawDbs = new();

    // Ép giá trị trực tiếp từ Watch Table (Force I / AI / Q)
    private readonly Dictionary<int, bool> _watchInputBitOverrides = new();
    private readonly Dictionary<int, short> _watchAiOverrides = new();
    private readonly Dictionary<int, bool> _watchOutputBitOverrides = new();

    public Dictionary<string, PlcTimerDbState> TimerDbs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PlcCounterDbState> CounterDbs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> EdgeDbPrevState { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int LastErrorCode { get; private set; }
    public bool CpuStopRequested { get; private set; }
    public double LastCycleSeconds { get; private set; } = 0.05;
    public bool PasswordProtected { get; private set; }

    public Dictionary<string, bool> LiveNodePowerIn { get; } = new();
    public Dictionary<string, bool> LiveNodePowerOut { get; } = new();
    public Dictionary<string, string> LiveNodeStatusText { get; } = new();

    private PlcProgramModel? _activeProgram;
    private string? _pendingJumpLabel;
    private bool _returnRequested;

    // Trạng thái System & Clock Memory + Startup (STOP→RUN) + DiagStatusUpdate 1-scan
    private bool _isFirstScanPending = true;
    private bool _needsStartupClear;
    private bool _diagStatusChangedThisScan;
    private double _clockElapsedSeconds;

    private void SetRuntimeError(int code)
    {
        if (LastErrorCode != code)
        {
            LastErrorCode = code;
            _diagStatusChangedThisScan = true;
        }
    }

    public void NotifyStopToRunTransition()
    {
        _isFirstScanPending = true;
        _needsStartupClear = true;
        _clockElapsedSeconds = 0.0;
    }

    public void ResetAllMemory()
    {
        Array.Clear(PhysicalDi);
        Array.Clear(PhysicalAi);
        Array.Clear(PhysicalDq);
        Array.Clear(_imageI);
        Array.Clear(_imageQ);
        Array.Clear(_memoryM);
        _rawDbs.Clear();
        _watchInputBitOverrides.Clear();
        _watchAiOverrides.Clear();
        _watchOutputBitOverrides.Clear();
        TimerDbs.Clear();
        CounterDbs.Clear();
        EdgeDbPrevState.Clear();
        ResetExtendedState(clearDiagnostics: false);
        LastErrorCode = 0;
        _diagStatusChangedThisScan = false;
        CpuStopRequested = false;
        _isFirstScanPending = true;
        _needsStartupClear = false;
        _clockElapsedSeconds = 0.0;
        ClearLiveOverlay();
    }

    /// <summary>
    /// Chuẩn Startup STOP→RUN: giữ lại vùng nhớ đánh dấu Retain=true, xóa sạch vùng nhớ không lưu giữ (non-retentive).
    /// </summary>
    private void ApplyStartupMemoryClear(PlcProgramModel program)
    {
        Array.Clear(_imageI);
        Array.Clear(_imageQ);
        Array.Clear(PhysicalDq);

        // Xác định mặt nạ các bit/byte %M được đánh dấu Retain = true
        byte[] retainMaskM = new byte[_memoryM.Length];
        var retainedDbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in program.Tags)
        {
            if (!tag.Retain || string.IsNullOrWhiteSpace(tag.Address)) continue;
            var op = OperandResolver.Resolve(tag.Address, program);
            if (!op.IsValid) continue;

            if (op.Area == PlcMemoryArea.M)
            {
                if (op.AccessSizeBytes <= 0)
                {
                    if (op.ByteOffset >= 0 && op.ByteOffset < retainMaskM.Length)
                        retainMaskM[op.ByteOffset] |= (byte)(1 << op.BitOffset);
                }
                else
                {
                    for (int b = op.ByteOffset; b < op.ByteOffset + op.AccessSizeBytes && b < retainMaskM.Length; b++)
                        retainMaskM[b] = 0xFF;
                }
            }
            else if (op.Area == PlcMemoryArea.DB && op.DbNumber > 0)
            {
                retainedDbs.Add($"%DB{op.DbNumber}");
            }
        }

        for (int i = 0; i < _memoryM.Length; i++)
            _memoryM[i] &= retainMaskM[i];

        foreach (string key in TimerDbs.Keys.Where(k => !retainedDbs.Contains(k)).ToList())
            TimerDbs.Remove(key);
        foreach (string key in CounterDbs.Keys.Where(k => !retainedDbs.Contains(k)).ToList())
            CounterDbs.Remove(key);
        foreach (string key in EdgeDbPrevState.Keys.Where(k => !retainedDbs.Contains(k)).ToList())
            EdgeDbPrevState.Remove(key);
        foreach (int dbNum in _rawDbs.Keys.Where(n => !retainedDbs.Contains($"%DB{n}")).ToList())
            _rawDbs.Remove(dbNum);
    }

    public void ClearOutputsAndOverlay()
    {
        Array.Clear(PhysicalDq);
        Array.Clear(_imageQ);
        ClearLiveOverlay();
    }

    public void ClearLiveOverlay()
    {
        LiveNodePowerIn.Clear();
        LiveNodePowerOut.Clear();
        LiveNodeStatusText.Clear();
    }

    /// <summary>
    /// Thực thi chu trình vòng quét 4 pha chuẩn IEC 61131-3.
    /// </summary>
    public void ExecuteScanCycle(PlcProgramModel program, double deltaSeconds)
    {
        _activeProgram = program;
        ClearLiveOverlay();
        double dt = Math.Clamp(deltaSeconds, 0.0, 0.25);
        LastCycleSeconds = dt > 0 ? dt : 0.05;
        CpuStopRequested = false;
        _returnRequested = false;
        _pendingJumpLabel = null;
        _retriggerCount = 0;
        _effectiveMaxJumps = MaxJumpsPerScan;

        bool runStartup = _needsStartupClear;
        if (_needsStartupClear)
        {
            _needsStartupClear = false;
            ApplyStartupMemoryClear(program);
        }

        // =========================================================================
        // PHA 1: ĐỌC NGÕ VÀO VẬT LÝ VÀO BẢNG ĐỆM I & CẬP NHẬT SYSTEM/CLOCK MEMORY
        // =========================================================================
        Phase1_UpdateProcessImageInputsAndSystemMemory(program, dt);

        // Chạy khối OB100 (Startup) đúng 1 lần khi chuyển STOP → RUN
        if (runStartup)
        {
            ExecuteStartupBlocks(program, dt);
        }

        // =========================================================================
        // PHA 2: THỰC THI TUẦN TỰ NETWORK 1 .. NETWORK N (OB1 + CALL FC/FB + WATCHDOG)
        // =========================================================================
        if (!CpuStopRequested)
        {
            ExecuteNetworkList(program.Networks, dt);
        }

        // Cập nhật phân hệ phần cứng tốc độ cao (HSC & PWM)
        UpdateHardwareTechnologySubsystems(dt);

        // =========================================================================
        // PHA 3: XUẤT BẢNG ĐỆM Q RA NGÕ RA VẬT LÝ DQ (CÓ TÔN TRỌNG FORCE Q)
        // =========================================================================
        Phase3_WriteProcessImageToPhysicalOutputs();

        // =========================================================================
        // PHA 4: HẬU XỬ LÝ VÒNG QUÉT (XÓA CỜ FIRSTSCAN & DIAGSTATUSUPDATE 1-SCAN)
        // =========================================================================
        Phase4_PostScan();
    }

    private void Phase1_UpdateProcessImageInputsAndSystemMemory(PlcProgramModel program, double dt)
    {
        // Sao chép 14 DI vật lý vào Image I (bytes 0..1)
        _imageI[0] = 0;
        _imageI[1] = 0;
        for (int i = 0; i < PhysicalDi.Length; i++)
        {
            bool bitVal = _watchInputBitOverrides.TryGetValue(i, out bool forced) ? forced : PhysicalDi[i];
            if (bitVal)
                _imageI[i / 8] |= (byte)(1 << (i % 8));
        }

        // Sao chép 2 AI vật lý vào %IW64 và %IW66
        short ai0 = _watchAiOverrides.TryGetValue(64, out short forcedAi0)
            ? forcedAi0
            : (short)Math.Clamp(PhysicalAi[0], short.MinValue, short.MaxValue);
        short ai1 = _watchAiOverrides.TryGetValue(66, out short forcedAi1)
            ? forcedAi1
            : (short)Math.Clamp(PhysicalAi[1], short.MinValue, short.MaxValue);
        BinaryPrimitives.WriteInt16BigEndian(_imageI.AsSpan(64, 2), ai0);
        BinaryPrimitives.WriteInt16BigEndian(_imageI.AsSpan(66, 2), ai1);

        // Cập nhật System Memory Byte (chỉ ghi 4 bit 0..3, bảo toàn 4 bit cao 4..7)
        if (program.EnableSystemMemory)
        {
            int sb = Math.Clamp(program.SystemMemoryByte, 0, _memoryM.Length - 1);
            byte sysLow = 0;
            if (_isFirstScanPending) sysLow |= (1 << 0);        // FirstScan (chỉ 1 scan sau STOP→RUN)
            if (_diagStatusChangedThisScan) sysLow |= (1 << 1); // DiagStatusUpdate (chỉ 1 scan khi trạng thái lỗi đổi)
            sysLow |= (1 << 2);                                 // AlwaysTRUE = 1
            // bit 3 = AlwaysFALSE = 0
            _memoryM[sb] = (byte)((_memoryM[sb] & 0xF0) | sysLow);
        }

        // Cập nhật Clock Memory Byte (mặc định MB0: 10Hz..0.5Hz xung vuông 50% duty cycle)
        if (program.EnableClockMemory)
        {
            int cb = Math.Clamp(program.ClockMemoryByte, 0, _memoryM.Length - 1);
            byte clkByte = 0;
            double t = _clockElapsedSeconds + 1e-6;
            foreach (var (bit, _, freqHz) in PlcProgramModel.ClockMemoryDefs)
            {
                long halfCycles = (long)Math.Floor(t * freqHz * 2.0);
                if ((halfCycles & 1L) == 0)
                    clkByte |= (byte)(1 << bit);
            }
            _memoryM[cb] = clkByte;
        }

        _clockElapsedSeconds += dt;
    }

    private void Phase3_WriteProcessImageToPhysicalOutputs()
    {
        for (int i = 0; i < PhysicalDq.Length; i++)
        {
            if (_watchOutputBitOverrides.TryGetValue(i, out bool forcedQ))
                PhysicalDq[i] = forcedQ;
            else
                PhysicalDq[i] = ((_imageQ[i / 8] >> (i % 8)) & 1) != 0;
        }
    }

    private void Phase4_PostScan()
    {
        _isFirstScanPending = false;
        _diagStatusChangedThisScan = false;
    }

    private static void IndexNetworkLabels(LadderBranch branch, int netIndex, Dictionary<string, int> map)
    {
        foreach (var n in branch.Nodes)
        {
            if (n.Kind == LadderElementKind.ParallelBlock && n.Branches is not null)
            {
                foreach (var sub in n.Branches) IndexNetworkLabels(sub, netIndex, map);
            }
            else if (n.Kind == LadderElementKind.Label && !string.IsNullOrWhiteSpace(n.Address))
            {
                map.TryAdd(n.Address.Trim(), netIndex);
            }
        }
    }

    private bool EvaluateBranch(LadderBranch branch, bool powerIn, double dt)
    {
        bool currentPower = powerIn;

        foreach (var node in branch.Nodes)
        {
            if (CpuStopRequested || _returnRequested || _pendingJumpLabel is not null)
                break;

            if (node.Kind == LadderElementKind.ParallelBlock)
            {
                LiveNodePowerIn[node.Id] = currentPower;
                bool anyOut = false;
                if (node.Branches is not null)
                {
                    foreach (var sub in node.Branches)
                    {
                        bool subOut = EvaluateBranch(sub, currentPower, dt);
                        if (subOut) anyOut = true;
                        if (CpuStopRequested || _returnRequested || _pendingJumpLabel is not null) break;
                    }
                }
                LiveNodePowerOut[node.Id] = anyOut;
                currentPower = anyOut;
                continue;
            }

            LiveNodePowerIn[node.Id] = currentPower;
            bool outPower = EvaluateNode(node, currentPower, dt);
            LiveNodePowerOut[node.Id] = outPower;
            currentPower = outPower;
        }

        return currentPower;
    }

    private bool EvaluateNode(LadderNode node, bool powerIn, double dt)
    {
        switch (node.Kind)
        {
            // =========================================================================
            // NHÓM 1: BIT LOGIC OPERATIONS
            // =========================================================================
            case LadderElementKind.ContactNO:
                return powerIn && ReadBit(node.Address);

            case LadderElementKind.ContactNC:
                return powerIn && !ReadBit(node.Address);

            case LadderElementKind.InvertRLO:
                return !powerIn;

            case LadderElementKind.CoilOut:
                WriteBit(node.Address, powerIn);
                return powerIn;

            case LadderElementKind.CoilNegate:
                WriteBit(node.Address, !powerIn);
                return powerIn;

            case LadderElementKind.CoilSet:
                if (powerIn) WriteBit(node.Address, true);
                return powerIn;

            case LadderElementKind.CoilReset:
                if (powerIn) WriteBit(node.Address, false);
                return powerIn;

            case LadderElementKind.SetBitField:
            case LadderElementKind.ResetBitField:
            {
                int count = Math.Clamp((int)Math.Round(ReadNumber(node.Param1)), 0, 64);
                var op = ResolveOp(node.Address);
                if (powerIn && op.IsValid && op.IsWritable && op.Area is PlcMemoryArea.Q or PlcMemoryArea.M)
                {
                    bool targetVal = node.Kind == LadderElementKind.SetBitField;
                    int startLinear = op.ByteOffset * 8 + op.BitOffset;
                    for (int k = 0; k < count; k++)
                        WriteBitLinear(op.Area, startLinear + k, targetVal, op.IsImmediate);
                }
                LiveNodeStatusText[node.Id] = $"N={count}";
                return powerIn;
            }

            case LadderElementKind.FlipFlopSR:
            {
                // Chuẩn IEC 61131-3 (Table 8-12/8-13): SR có ngõ vào S và R1 (Reset ưu tiên: R1=1 -> Q=0)
                bool s = powerIn;
                bool r1 = !string.IsNullOrWhiteSpace(node.Param1) && ReadBit(node.Param1);
                bool q = ReadBit(node.Address);
                if (s) q = true;
                if (r1) q = false;
                WriteBit(node.Address, q);
                LiveNodeStatusText[node.Id] = q ? "Q=1" : "Q=0";
                return q;
            }

            case LadderElementKind.FlipFlopRS:
            {
                // Chuẩn IEC 61131-3 (Table 8-12/8-13): RS có ngõ vào R và S1 (Set ưu tiên: S1=1 -> Q=1)
                bool r = powerIn;
                bool s1 = !string.IsNullOrWhiteSpace(node.Param1) && ReadBit(node.Param1);
                bool q = ReadBit(node.Address);
                if (r) q = false;
                if (s1) q = true;
                WriteBit(node.Address, q);
                LiveNodeStatusText[node.Id] = q ? "Q=1" : "Q=0";
                return q;
            }

            case LadderElementKind.EdgePositive:
            {
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    var memOp = ResolveOp(node.Address);
                    bool prev = ReadBitOperand(memOp);
                    if (memOp.IsValid && memOp.IsWritable) WriteBitOperand(memOp, powerIn);
                    return powerIn && !prev;
                }
                bool cur = ReadBit(node.Address);
                var mOp = ResolveOp(node.Param1);
                bool prevOp = ReadBitOperand(mOp);
                if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, cur);
                return powerIn && cur && !prevOp;
            }

            case LadderElementKind.EdgeNegative:
            {
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    var memOp = ResolveOp(node.Address);
                    bool prev = ReadBitOperand(memOp);
                    if (memOp.IsValid && memOp.IsWritable) WriteBitOperand(memOp, powerIn);
                    return !powerIn && prev;
                }
                bool cur = ReadBit(node.Address);
                var mOp = ResolveOp(node.Param1);
                bool prevOp = ReadBitOperand(mOp);
                if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, cur);
                return powerIn && !cur && prevOp;
            }

            case LadderElementKind.CoilEdgePositive:
            {
                bool prev;
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    string memKey = $"__CP_{node.Id}";
                    prev = EdgeDbPrevState.GetValueOrDefault(memKey);
                    EdgeDbPrevState[memKey] = powerIn;
                }
                else
                {
                    var mOp = ResolveOp(node.Param1);
                    prev = ReadBitOperand(mOp);
                    if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, powerIn);
                }
                bool pulse = powerIn && !prev;
                WriteBit(node.Address, pulse);
                return powerIn; // Chuẩn IEC: -(P)- truyền thẳng powerIn ra output wire
            }

            case LadderElementKind.CoilEdgeNegative:
            {
                bool prev;
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    string memKey = $"__CN_{node.Id}";
                    prev = EdgeDbPrevState.GetValueOrDefault(memKey);
                    EdgeDbPrevState[memKey] = powerIn;
                }
                else
                {
                    var mOp = ResolveOp(node.Param1);
                    prev = ReadBitOperand(mOp);
                    if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, powerIn);
                }
                bool pulse = !powerIn && prev;
                WriteBit(node.Address, pulse);
                return powerIn;
            }

            case LadderElementKind.PTrig:
            {
                var mOp = ResolveOp(node.Address);
                bool prev = ReadBitOperand(mOp);
                if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, powerIn);
                return powerIn && !prev;
            }

            case LadderElementKind.NTrig:
            {
                var mOp = ResolveOp(node.Address);
                bool prev = ReadBitOperand(mOp);
                if (mOp.IsValid && mOp.IsWritable) WriteBitOperand(mOp, powerIn);
                return !powerIn && prev;
            }

            case LadderElementKind.RTrig:
            case LadderElementKind.FTrig:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                bool prev = EdgeDbPrevState.GetValueOrDefault(dbKey);
                EdgeDbPrevState[dbKey] = powerIn;
                bool q = node.Kind == LadderElementKind.RTrig ? (powerIn && !prev) : (!powerIn && prev);
                EdgeDbPrevState[$"{dbKey}.Q"] = q;
                LiveNodeStatusText[node.Id] = q ? "Q=1" : "Q=0";
                return q;
            }

            // =========================================================================
            // NHÓM 2: TIMER OPERATIONS
            // =========================================================================
            case LadderElementKind.TimerTP:
            case LadderElementKind.TimerTON:
            case LadderElementKind.TimerTOF:
            case LadderElementKind.TimerTONR:
            case LadderElementKind.CoilTP:
            case LadderElementKind.CoilTON:
            case LadderElementKind.CoilTOF:
            case LadderElementKind.CoilTONR:
                return EvaluateTimer(node, powerIn, dt);

            case LadderElementKind.CoilRT:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                if (powerIn && TimerDbs.TryGetValue(dbKey, out var st))
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = false;
                    st.PulseActive = false;
                    st.TimingActive = false;
                }
                return powerIn;
            }

            case LadderElementKind.CoilPT:
            {
                string dbKey = ResolveRuntimeAddress(node.Address);
                if (powerIn && TryReadTimeSeconds(node.Param1, out double newPt))
                {
                    if (!TimerDbs.TryGetValue(dbKey, out var st))
                        TimerDbs[dbKey] = st = new PlcTimerDbState();
                    double clampedPt = Math.Max(0.0, newPt);
                    st.PresetSeconds = clampedPt;
                    st.LatchedPresetSeconds = clampedPt;
                }
                return powerIn;
            }

            // =========================================================================
            // NHÓM 3: COUNTER OPERATIONS
            // =========================================================================
            case LadderElementKind.CounterCTU:
            case LadderElementKind.CounterCTD:
            case LadderElementKind.CounterCTUD:
                return EvaluateCounter(node, powerIn);

            // =========================================================================
            // NHÓM 4: COMPARATOR OPERATIONS (+ Time literals + DataType + NaN-safe + Variant)
            // =========================================================================
            case LadderElementKind.CmpEqual:
            case LadderElementKind.CmpNotEqual:
            case LadderElementKind.CmpGreaterOrEqual:
            case LadderElementKind.CmpLessOrEqual:
            case LadderElementKind.CmpGreater:
            case LadderElementKind.CmpLess:
            {
                PlcDataType? cmpDt = PlcDataTypeHelper.TryParse(node.DataType, out var parsedCmpDt) ? parsedCmpDt : null;
                var v1 = ReadTypedValue(node.Param1, cmpDt);
                var v2 = ReadTypedValue(node.Param2, cmpDt);
                bool isReal = cmpDt == PlcDataType.Real || v1.DataType == PlcDataType.Real || v2.DataType == PlcDataType.Real;
                bool isTime = cmpDt == PlcDataType.Time || v1.DataType == PlcDataType.Time || v2.DataType == PlcDataType.Time;

                double a = isReal ? v1.AsReal : v1.AsDouble;
                double b = isReal ? v2.AsReal : v2.AsDouble;
                bool hasNaN = double.IsNaN(a) || double.IsNaN(b);

                bool cmp = hasNaN
                    ? node.Kind == LadderElementKind.CmpNotEqual
                    : node.Kind switch
                    {
                        LadderElementKind.CmpEqual => a == b,
                        LadderElementKind.CmpNotEqual => a != b,
                        LadderElementKind.CmpGreaterOrEqual => a >= b,
                        LadderElementKind.CmpLessOrEqual => a <= b,
                        LadderElementKind.CmpGreater => a > b,
                        LadderElementKind.CmpLess => a < b,
                        _ => false
                    };
                LiveNodeStatusText[node.Id] = isTime
                    ? $"{OperandResolver.FormatTimeMs(v1.AsTimeMs)} / {OperandResolver.FormatTimeMs(v2.AsTimeMs)}"
                    : $"{FmtNum(a)} / {FmtNum(b)}";
                return powerIn && cmp;
            }

            case LadderElementKind.InRange:
            case LadderElementKind.OutRange:
            {
                PlcDataType? rngDt = PlcDataTypeHelper.TryParse(node.DataType, out var parsedRngDt) ? parsedRngDt : null;
                var vVal = ReadTypedValue(node.Param1, rngDt);
                var vMin = ReadTypedValue(node.Param2, rngDt);
                var vMax = ReadTypedValue(node.ExtraParam, rngDt);
                bool isReal = rngDt == PlcDataType.Real || vVal.DataType == PlcDataType.Real || vMin.DataType == PlcDataType.Real || vMax.DataType == PlcDataType.Real;
                bool isTime = rngDt == PlcDataType.Time || vVal.DataType == PlcDataType.Time || vMin.DataType == PlcDataType.Time || vMax.DataType == PlcDataType.Time;

                double val = isReal ? vVal.AsReal : vVal.AsDouble;
                double min = isReal ? vMin.AsReal : vMin.AsDouble;
                double max = isReal ? vMax.AsReal : vMax.AsDouble;
                bool hasNaN = double.IsNaN(val) || double.IsNaN(min) || double.IsNaN(max);

                bool ok = !hasNaN && (node.Kind == LadderElementKind.InRange
                    ? (val >= min && val <= max)
                    : (val < min || val > max));
                LiveNodeStatusText[node.Id] = isTime
                    ? $"{OperandResolver.FormatTimeMs(vVal.AsTimeMs)} [{OperandResolver.FormatTimeMs(vMin.AsTimeMs)}..{OperandResolver.FormatTimeMs(vMax.AsTimeMs)}]"
                    : $"{FmtNum(val)} [{FmtNum(min)}..{FmtNum(max)}]";
                return powerIn && ok;
            }

            case LadderElementKind.CheckOk:
            case LadderElementKind.CheckNotOk:
            {
                var v = ReadTypedValue(node.Address, PlcDataType.Real);
                bool valid = v.IsValidReal;
                return powerIn && (node.Kind == LadderElementKind.CheckOk ? valid : !valid);
            }

            case LadderElementKind.VarEqType:
            case LadderElementKind.VarNeType:
            case LadderElementKind.VarEqElemType:
            case LadderElementKind.VarNeElemType:
            {
                string t1 = ResolveOp(node.Param1).InferredType.ToString();
                string t2 = ResolveOp(node.Param2).InferredType.ToString();
                bool same = string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase);
                bool res = node.Kind is LadderElementKind.VarEqType or LadderElementKind.VarEqElemType ? same : !same;
                LiveNodeStatusText[node.Id] = $"{t1}/{t2}";
                return powerIn && res;
            }

            case LadderElementKind.VarIsNull:
            case LadderElementKind.VarNotNull:
            {
                bool isNull = string.IsNullOrWhiteSpace(node.Param1) || node.Param1.Equals("NULL", StringComparison.OrdinalIgnoreCase);
                return powerIn && (node.Kind == LadderElementKind.VarIsNull ? isNull : !isNull);
            }

            case LadderElementKind.VarIsArray:
                return powerIn && (node.Param1?.Contains('[') == true);

            case LadderElementKind.VarEqTypeOfDb:
            case LadderElementKind.VarNeTypeOfDb:
            {
                string dbKey = ResolveRuntimeAddress(node.Param1);
                string dbType = _activeProgram?.GetAllDataBlocks()
                    .FirstOrDefault(d => PlcProgramModel.NormalizeAddress(d.Address).Equals(dbKey, StringComparison.OrdinalIgnoreCase))?.DataType
                    ?? (TimerDbs.ContainsKey(dbKey) ? "IEC_Timer" : (CounterDbs.ContainsKey(dbKey) ? "IEC_Counter" : "DB"));
                bool eq = string.Equals(dbType, node.Param2?.Trim(), StringComparison.OrdinalIgnoreCase);
                return powerIn && (node.Kind == LadderElementKind.VarEqTypeOfDb ? eq : !eq);
            }

            // =========================================================================
            // NHÓM 5: MATH FUNCTIONS (EN / ENO NGHIÊM NGẶT + MULTI-INPUT + CALCULATE)
            // =========================================================================
            case LadderElementKind.Calculate:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Real);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                var inputVals = CollectPositionalNumericInputs(node, targetDt);
                bool intDiv = !PlcDataTypeHelper.IsFloatingPoint(targetDt);
                if (!PlcExpressionEvaluator.TryEvaluate(node.ExtraParam, inputVals, out double calcRes, integerDivision: intDiv) ||
                    !PlcValue.TryFromDouble(calcRes, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8200);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }

                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.MathAdd:
            case LadderElementKind.MathMul:
            case LadderElementKind.MathMin:
            case LadderElementKind.MathMax:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                var vals = CollectAllNumericInputs(node, targetDt);
                if (vals.Count == 0) return false;

                double acc = vals[0];
                for (int i = 1; i < vals.Count; i++)
                {
                    acc = node.Kind switch
                    {
                        LadderElementKind.MathAdd => acc + vals[i],
                        LadderElementKind.MathMul => acc * vals[i],
                        LadderElementKind.MathMin => Math.Min(acc, vals[i]),
                        _ => Math.Max(acc, vals[i])
                    };
                }

                if (!PlcValue.TryFromDouble(acc, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8203); // Overflow
                    LiveNodeStatusText[node.Id] = "OVF (ENO=0)";
                    return false;
                }

                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.MathSub:
            case LadderElementKind.MathDiv:
            case LadderElementKind.MathMod:
            case LadderElementKind.MathExpt:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                double a = ReadNumber(node.Param1, targetDt);
                double b = ReadNumber(node.Param2, targetDt);
                if ((node.Kind is LadderElementKind.MathDiv or LadderElementKind.MathMod) && b == 0.0)
                {
                    // Chuẩn IEC Part 1: DIV/MOD chia cho 0 -> OUT = 0, ENO = FALSE
                    if (PlcValue.TryFromDouble(0.0, targetDt, out var zeroVal))
                        WriteTypedValue(node.OutputAddress, zeroVal);
                    SetRuntimeError(0x8201); // Division by zero
                    LiveNodeStatusText[node.Id] = "DIV0 (ENO=0)";
                    return false;
                }

                double res = node.Kind switch
                {
                    LadderElementKind.MathSub => a - b,
                    LadderElementKind.MathDiv => targetDt == PlcDataType.Real ? (a / b) : Math.Truncate(a / b),
                    LadderElementKind.MathMod => Math.Truncate(a) % Math.Truncate(b),
                    LadderElementKind.MathExpt => Math.Pow(a, b),
                    _ => 0.0
                };

                if (!PlcValue.TryFromDouble(res, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8203);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }

                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.MathLimit:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                double val = ReadNumber(node.Param1, targetDt);
                double mn = ReadNumber(node.Param2, targetDt);
                double mx = ReadNumber(node.ExtraParam, targetDt);
                if (mn > mx || double.IsNaN(val) || double.IsNaN(mn) || double.IsNaN(mx))
                {
                    // Chuẩn IEC Part 1 (LIMIT): nếu MN > MX thì OUT := IN và ENO := FALSE
                    if (mn > mx && PlcValue.TryFromDouble(val, targetDt, out var passthroughVal))
                        WriteTypedValue(node.OutputAddress, passthroughVal);
                    SetRuntimeError(0x8204);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }
                double clamped = Math.Clamp(val, mn, mx);
                if (!PlcValue.TryFromDouble(clamped, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8203);
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.MathInc:
            case LadderElementKind.MathDec:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }
                double cur = ReadNumber(node.OutputAddress, targetDt);
                double next = node.Kind == LadderElementKind.MathInc ? cur + 1.0 : cur - 1.0;
                if (!PlcValue.TryFromDouble(next, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8203);
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.MathNeg:
            case LadderElementKind.MathAbs:
            case LadderElementKind.MathSqr:
            case LadderElementKind.MathSqrt:
            case LadderElementKind.MathLn:
            case LadderElementKind.MathExp:
            case LadderElementKind.MathFrac:
            case LadderElementKind.MathSin:
            case LadderElementKind.MathCos:
            case LadderElementKind.MathTan:
            case LadderElementKind.MathAsin:
            case LadderElementKind.MathAcos:
            case LadderElementKind.MathAtan:
            {
                PlcDataType defaultDt = node.Kind is LadderElementKind.MathNeg or LadderElementKind.MathAbs ? PlcDataType.Int : PlcDataType.Real;
                PlcDataType targetDt = ResolveBoxTargetType(node, defaultDt);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                double x = ReadNumber(node.Param1, targetDt);
                double res = node.Kind switch
                {
                    LadderElementKind.MathNeg => -x,
                    LadderElementKind.MathAbs => Math.Abs(x),
                    LadderElementKind.MathSqr => x * x,
                    LadderElementKind.MathSqrt => x >= 0 ? Math.Sqrt(x) : double.NaN,
                    LadderElementKind.MathLn => x > 0 ? Math.Log(x) : double.NaN,
                    LadderElementKind.MathExp => Math.Exp(x),
                    LadderElementKind.MathFrac => x - Math.Truncate(x),
                    LadderElementKind.MathSin => Math.Sin(x),
                    LadderElementKind.MathCos => Math.Cos(x),
                    LadderElementKind.MathTan => Math.Tan(x),
                    LadderElementKind.MathAsin => x is >= -1.0 and <= 1.0 ? Math.Asin(x) : double.NaN,
                    LadderElementKind.MathAcos => x is >= -1.0 and <= 1.0 ? Math.Acos(x) : double.NaN,
                    LadderElementKind.MathAtan => Math.Atan(x),
                    _ => x
                };

                if (!PlcValue.TryFromDouble(res, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8202);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            // =========================================================================
            // NHÓM 6: MOVE OPERATIONS (+ Multi-Output MOVE, Overlap-safe MOVE_BLK)
            // =========================================================================
            case LadderElementKind.Move:
            case LadderElementKind.VariantGet:
            case LadderElementKind.VariantPut:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"→ {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }
                var v = ReadTypedValue(node.Param1, targetDt);
                PlcValue MoveConvert(PlcValue src, PlcDataType dstType)
                {
                    if (src.TryConvertTo(dstType, out var c)) return c;
                    // Nếu cùng độ rộng byte (VD: Word 16#FFFF -> Int), bảo toàn nguyên mẫu bit theo chuẩn MOVE
                    if (PlcDataTypeHelper.ByteSize(src.DataType) == PlcDataTypeHelper.ByteSize(dstType))
                        return PlcValue.FromRawBits(src.AsRawUInt32, dstType);
                    return PlcValue.FromRawBits(src.AsRawUInt32, dstType);
                }

                var outOp1 = ResolveOp(node.OutputAddress);
                PlcDataType outDt1 = outOp1.IsValid && !outOp1.IsBitAccess ? outOp1.InferredType : targetDt;
                var converted1 = MoveConvert(v, outDt1);
                WriteTypedValue(node.OutputAddress, converted1);

                if (node.Kind == LadderElementKind.Move && node.ExtraOutputs is not null)
                {
                    foreach (string extraOut in node.ExtraOutputs)
                    {
                        if (string.IsNullOrWhiteSpace(extraOut)) continue;
                        var exOp = ResolveOp(extraOut);
                        PlcDataType exDt = exOp.IsValid && !exOp.IsBitAccess ? exOp.InferredType : targetDt;
                        WriteTypedValue(extraOut, MoveConvert(v, exDt));
                    }
                }

                LiveNodeStatusText[node.Id] = $"{v.Format(PlcWatchFormat.Auto)} → {converted1.Format(PlcWatchFormat.Auto)}";
                return true; // Chuẩn IEC: MOVE luôn xuất ENO = TRUE khi EN = TRUE
            }

            case LadderElementKind.MoveBlk:
            case LadderElementKind.MoveBlkVariant:
            case LadderElementKind.UMoveBlk:
            case LadderElementKind.Serialize:
            case LadderElementKind.Deserialize:
            {
                int count = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 32);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"BLK×{count}";
                    return false;
                }
                if (count == 0)
                {
                    LiveNodeStatusText[node.Id] = "BLK×0";
                    return true;
                }
                var srcOp = ResolveOp(node.Param1);
                var dstOp = ResolveOp(node.OutputAddress);
                if (!srcOp.IsValid || !dstOp.IsValid || srcOp.AccessSizeBytes <= 0 || dstOp.AccessSizeBytes <= 0 || !dstOp.IsWritable)
                    return false;

                // Đọc vào bộ đệm tạm trước khi ghi để an toàn khi vùng nguồn & đích chồng lấn (overlapping)
                int step = srcOp.AccessSizeBytes;
                uint[] tmpBuf = new uint[count];
                for (int k = 0; k < count; k++)
                {
                    if (!TryReadRawBytes(srcOp.Area, srcOp.DbNumber, srcOp.ByteOffset + k * step, step, srcOp.IsImmediate, out tmpBuf[k]))
                    {
                        SetRuntimeError(0x8206);
                        return false;
                    }
                }
                for (int k = 0; k < count; k++)
                {
                    if (!TryWriteRawBytes(dstOp.Area, dstOp.DbNumber, dstOp.ByteOffset + k * step, step, dstOp.IsImmediate, tmpBuf[k]))
                    {
                        SetRuntimeError(0x8206);
                        return false;
                    }
                }
                LiveNodeStatusText[node.Id] = $"BLK×{count}";
                return true;
            }

            case LadderElementKind.FillBlk:
            case LadderElementKind.UFillBlk:
            {
                int count = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"×{count}";
                    return false;
                }
                var dstOp = ResolveOp(node.OutputAddress);
                if (!dstOp.IsValid || dstOp.AccessSizeBytes <= 0 || !dstOp.IsWritable) return false;
                var fillVal = ReadTypedValue(node.Param1);
                if (!fillVal.TryConvertTo(dstOp.InferredType, out var conv)) return false;

                int step = dstOp.AccessSizeBytes;
                uint raw = conv.AsRawUInt32;
                for (int k = 0; k < count; k++)
                {
                    if (!TryWriteRawBytes(dstOp.Area, dstOp.DbNumber, dstOp.ByteOffset + k * step, step, dstOp.IsImmediate, raw))
                        return false;
                }
                LiveNodeStatusText[node.Id] = $"{conv.Format(PlcWatchFormat.Auto)}×{count}";
                return true;
            }

            case LadderElementKind.Scatter:
            case LadderElementKind.ScatterBlk:
            {
                int bits = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (!powerIn) return false;
                uint wordVal = ReadTypedValue(node.Param1).AsRawUInt32;
                var dstOp = ResolveOp(node.OutputAddress);
                if (dstOp.IsValid && dstOp.IsWritable && dstOp.Area is PlcMemoryArea.Q or PlcMemoryArea.M)
                {
                    int startBit = dstOp.ByteOffset * 8 + dstOp.BitOffset;
                    for (int k = 0; k < bits; k++)
                        WriteBitLinear(dstOp.Area, startBit + k, ((wordVal >> k) & 1u) != 0, dstOp.IsImmediate);
                }
                LiveNodeStatusText[node.Id] = $"16#{wordVal:X}→{bits}b";
                return true;
            }

            case LadderElementKind.Gather:
            case LadderElementKind.GatherBlk:
            {
                int bits = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (!powerIn) return false;
                var srcOp = ResolveOp(node.Param1);
                if (srcOp.IsValid && srcOp.Area is PlcMemoryArea.I or PlcMemoryArea.Q or PlcMemoryArea.M)
                {
                    int startBit = srcOp.ByteOffset * 8 + srcOp.BitOffset;
                    uint acc = 0;
                    for (int k = 0; k < bits; k++)
                    {
                        if (ReadBitLinear(srcOp.Area, startBit + k, srcOp.IsImmediate)) acc |= (1u << k);
                    }
                    PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                    WriteTypedValue(node.OutputAddress, PlcValue.FromRawBits(acc, targetDt));
                }
                LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress)}";
                return true;
            }

            case LadderElementKind.Swap:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                if (!powerIn) return false;
                uint raw = ReadTypedValue(node.Param1, targetDt).AsRawUInt32;
                int sz = PlcDataTypeHelper.ByteSize(targetDt);
                uint swapped = sz switch
                {
                    1 => raw & 0xFFu,
                    4 => ((raw & 0xFF) << 24) | ((raw & 0xFF00) << 8) | ((raw >> 8) & 0xFF00) | ((raw >> 24) & 0xFF),
                    _ => (ushort)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF))
                };
                var res = PlcValue.FromRawBits(swapped, targetDt);
                WriteTypedValue(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = res.Format(PlcWatchFormat.Hex);
                return true;
            }

            case LadderElementKind.CountOfElements:
                if (!powerIn) return false;
                WriteNumber(node.OutputAddress, 8);
                return true;

            case LadderElementKind.LowerBound:
                if (!powerIn) return false;
                WriteNumber(node.OutputAddress, 0);
                return true;

            case LadderElementKind.UpperBound:
                if (!powerIn) return false;
                WriteNumber(node.OutputAddress, 7);
                return true;

            case LadderElementKind.FieldRead:
            {
                int idx = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 31);
                if (!powerIn) return false;
                var baseOp = ResolveOp(node.Param1);
                if (!baseOp.IsValid || baseOp.AccessSizeBytes <= 0) return false;
                if (!TryReadRawBytes(baseOp.Area, baseOp.DbNumber, baseOp.ByteOffset + idx * baseOp.AccessSizeBytes, baseOp.AccessSizeBytes, baseOp.IsImmediate, out uint raw))
                    return false;
                WriteTypedValue(node.OutputAddress, PlcValue.FromRawBits(raw, baseOp.InferredType));
                LiveNodeStatusText[node.Id] = $"[{idx}]={FormatAddressLiveShort(node.OutputAddress)}";
                return true;
            }

            case LadderElementKind.FieldWrite:
            {
                int idx = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 31);
                if (!powerIn) return false;
                var baseOp = ResolveOp(node.OutputAddress);
                if (!baseOp.IsValid || baseOp.AccessSizeBytes <= 0 || !baseOp.IsWritable) return false;
                var val = ReadTypedValue(node.Param1);
                if (!val.TryConvertTo(baseOp.InferredType, out var conv)) return false;
                if (!TryWriteRawBytes(baseOp.Area, baseOp.DbNumber, baseOp.ByteOffset + idx * baseOp.AccessSizeBytes, baseOp.AccessSizeBytes, baseOp.IsImmediate, conv.AsRawUInt32))
                    return false;
                LiveNodeStatusText[node.Id] = $"[{idx}]←{conv.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            // =========================================================================
            // NHÓM 7: CONVERSION OPERATIONS (+ Analog NORM_X / SCALE_X)
            // =========================================================================
            case LadderElementKind.Convert:
            case LadderElementKind.Round:
            case LadderElementKind.Ceil:
            case LadderElementKind.Floor:
            case LadderElementKind.Trunc:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }

                // Hỗ trợ chuyển đổi BCD16 <-> Int và BCD32 <-> DInt chuẩn IEC trong CONVERT
                if (node.Kind == LadderElementKind.Convert)
                {
                    var srcOp = ResolveOp(node.Param1);
                    if (targetDt == PlcDataType.Bcd16)
                    {
                        int srcInt = (int)Math.Round(ReadNumber(node.Param1, PlcDataType.Int));
                        if (!PlcProtocolAndBcdHelper.TryIntToBcd16(srcInt, out ushort bcd16))
                        {
                            SetRuntimeError(0x8207);
                            LiveNodeStatusText[node.Id] = "BCD ERR";
                            return false;
                        }
                        var bVal = PlcValue.FromWord(bcd16);
                        WriteTypedValue(node.OutputAddress, bVal);
                        LiveNodeStatusText[node.Id] = bVal.Format(PlcWatchFormat.Hex);
                        return true;
                    }
                    if (targetDt == PlcDataType.Bcd32)
                    {
                        long srcDInt = (long)Math.Round(ReadNumber(node.Param1, PlcDataType.DInt));
                        if (!PlcProtocolAndBcdHelper.TryDIntToBcd32(srcDInt, out uint bcd32))
                        {
                            SetRuntimeError(0x8207);
                            LiveNodeStatusText[node.Id] = "BCD ERR";
                            return false;
                        }
                        var bVal = PlcValue.FromDWord(bcd32);
                        WriteTypedValue(node.OutputAddress, bVal);
                        LiveNodeStatusText[node.Id] = bVal.Format(PlcWatchFormat.Hex);
                        return true;
                    }
                    if (srcOp.IsValid && (srcOp.InferredType == PlcDataType.Bcd16 ||
                        string.Equals(node.ExtraParam, "FROM_BCD16", StringComparison.OrdinalIgnoreCase)))
                    {
                        ushort rawBcd = (ushort)(ReadTypedValue(srcOp, PlcDataType.Word).AsRawUInt32 & 0xFFFF);
                        if (!PlcProtocolAndBcdHelper.TryBcd16ToInt(rawBcd, out short dec16) ||
                            !PlcValue.TryFromDouble(dec16, targetDt, out var outDec16))
                        {
                            SetRuntimeError(0x8207);
                            LiveNodeStatusText[node.Id] = "BCD ERR";
                            return false;
                        }
                        WriteTypedValue(node.OutputAddress, outDec16);
                        LiveNodeStatusText[node.Id] = $"= {outDec16.Format(PlcWatchFormat.Auto)}";
                        return true;
                    }
                    if (srcOp.IsValid && (srcOp.InferredType == PlcDataType.Bcd32 ||
                        string.Equals(node.ExtraParam, "FROM_BCD32", StringComparison.OrdinalIgnoreCase)))
                    {
                        uint rawBcd = ReadTypedValue(srcOp, PlcDataType.DWord).AsRawUInt32;
                        if (!PlcProtocolAndBcdHelper.TryBcd32ToDInt(rawBcd, out int dec32) ||
                            !PlcValue.TryFromDouble(dec32, targetDt, out var outDec32))
                        {
                            SetRuntimeError(0x8207);
                            LiveNodeStatusText[node.Id] = "BCD ERR";
                            return false;
                        }
                        WriteTypedValue(node.OutputAddress, outDec32);
                        LiveNodeStatusText[node.Id] = $"= {outDec32.Format(PlcWatchFormat.Auto)}";
                        return true;
                    }
                }

                double rawVal = ReadNumber(node.Param1);
                if (double.IsNaN(rawVal) || double.IsInfinity(rawVal))
                {
                    SetRuntimeError(0x8207);
                    return false;
                }
                double computed = node.Kind switch
                {
                    LadderElementKind.Round => Math.Round(rawVal, MidpointRounding.ToEven),
                    LadderElementKind.Ceil => Math.Ceiling(rawVal),
                    LadderElementKind.Floor => Math.Floor(rawVal),
                    LadderElementKind.Trunc => Math.Truncate(rawVal),
                    _ => rawVal
                };
                if (!PlcValue.TryFromDouble(computed, targetDt, out var typedRes))
                {
                    SetRuntimeError(0x8203);
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedRes);
                LiveNodeStatusText[node.Id] = $"= {typedRes.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.NormX:
            {
                PlcDataType inDt = PlcDataTypeHelper.TryParse(node.DataType, out var parsedNormDt) ? parsedNormDt : PlcDataType.Int;
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {ReadNumber(node.OutputAddress, PlcDataType.Real):0.000}";
                    return false;
                }
                double val = ReadNumber(node.Param1, inDt);
                double min = ReadNumber(node.Param2, inDt);
                double max = ReadNumber(node.ExtraParam, inDt);
                if (max <= min || double.IsNaN(val) || double.IsInfinity(val) || double.IsNaN(min) || double.IsNaN(max))
                {
                    SetRuntimeError(0x8208);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }
                double norm = (val - min) / (max - min);
                if (!PlcValue.TryFromDouble(norm, PlcDataType.Real, out var typedNorm))
                {
                    SetRuntimeError(0x8208);
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedNorm);
                LiveNodeStatusText[node.Id] = $"= {typedNorm.AsDouble:0.000}";
                return true;
            }

            case LadderElementKind.ScaleX:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Real);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = $"= {FormatAddressLiveShort(node.OutputAddress, targetDt)}";
                    return false;
                }
                double val = ReadNumber(node.Param1, PlcDataType.Real);
                double min = ReadNumber(node.Param2, targetDt);
                double max = ReadNumber(node.ExtraParam, targetDt);
                if (max <= min || double.IsNaN(val) || double.IsInfinity(val))
                {
                    SetRuntimeError(0x8208);
                    LiveNodeStatusText[node.Id] = "ERR (ENO=0)";
                    return false;
                }
                double scaled = val * (max - min) + min;
                if (!PlcValue.TryFromDouble(scaled, targetDt, out var typedScaled))
                {
                    SetRuntimeError(0x8203);
                    LiveNodeStatusText[node.Id] = "OVF (ENO=0)";
                    return false;
                }
                WriteTypedValue(node.OutputAddress, typedScaled);
                LiveNodeStatusText[node.Id] = $"= {typedScaled.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            // =========================================================================
            // NHÓM 8: PROGRAM CONTROL OPERATIONS (+ Runtime control)
            // =========================================================================
            case LadderElementKind.Label:
                return powerIn;

            case LadderElementKind.Jump:
                if (powerIn && !string.IsNullOrWhiteSpace(node.Address))
                    _pendingJumpLabel = node.Address.Trim();
                return powerIn;

            case LadderElementKind.JumpNot:
                if (!powerIn && !string.IsNullOrWhiteSpace(node.Address))
                    _pendingJumpLabel = node.Address.Trim();
                return !powerIn;

            case LadderElementKind.JumpList:
            {
                int k = (int)Math.Round(ReadNumber(node.Param1));
                var lbls = (node.Address ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (powerIn && k >= 0 && k < lbls.Length)
                    _pendingJumpLabel = lbls[k];
                LiveNodeStatusText[node.Id] = $"K={k}";
                return powerIn;
            }

            case LadderElementKind.Switch:
            {
                int k = (int)Math.Round(ReadNumber(node.Param1));
                var lbls = (node.Address ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var vals = (node.Param2 ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (powerIn)
                {
                    for (int i = 0; i < Math.Min(lbls.Length, vals.Length); i++)
                    {
                        if (int.TryParse(vals[i], out int m) && m == k)
                        {
                            _pendingJumpLabel = lbls[i];
                            break;
                        }
                    }
                }
                LiveNodeStatusText[node.Id] = $"K={k}";
                return powerIn;
            }

            case LadderElementKind.Return:
                if (powerIn) _returnRequested = true;
                return powerIn;

            case LadderElementKind.EndisPw:
                if (!powerIn) return false;
                PasswordProtected = ReadNumber(node.Param1) != 0;
                WriteNumber(node.OutputAddress, 0);
                return true;

            case LadderElementKind.ReTrigr:
                if (powerIn && _retriggerCount < 9)
                {
                    _retriggerCount++;
                    _effectiveMaxJumps = MaxJumpsPerScan * (_retriggerCount + 1);
                }
                return powerIn;

            case LadderElementKind.StopCpu:
                if (powerIn)
                {
                    CpuStopRequested = true;
                    LogDiagnostic(0x0105, PlcDiagSeverity.Warning, "CPU", "STP instruction executed — transitioning CPU to STOP.");
                }
                return powerIn;

            case LadderElementKind.GetError:
            case LadderElementKind.GetErrId:
            {
                if (!powerIn) return false;
                int errCode = LastErrorCode;
                // Chuẩn IEC: GET_ERROR / GET_ERR_ID trả về mã lỗi Word 16-bit (VD: 16#8204), bảo toàn mẫu bit khi ghi vào %MW
                WriteTypedValue(node.OutputAddress, PlcValue.FromWord(unchecked((ushort)errCode)));
                LiveNodeStatusText[node.Id] = $"ERR=16#{errCode:X4}";
                // Chuẩn IEC Part 1: GET_ERROR / GET_ERR_ID xóa thông tin lỗi sau khi đã đọc
                if (errCode != 0)
                {
                    LastErrorCode = 0;
                    _diagStatusChangedThisScan = true;
                }
                return errCode != 0;
            }

            case LadderElementKind.RuntimeMeasure:
            {
                if (!powerIn) return false;
                // Chuẩn IEC Part 1: dùng chân MEM (IN_OUT) lưu mốc thời gian trước và xuất hiệu thời gian giữa 2 lần gọi
                double prevStamp = ReadNumber(node.Param1, PlcDataType.Real);
                double nowStamp = _clockElapsedSeconds;
                double measured = prevStamp > 0.0 && nowStamp >= prevStamp ? (nowStamp - prevStamp) : LastCycleSeconds;
                WriteTypedValue(node.Param1, PlcValue.FromReal((float)nowStamp));
                WriteTypedValue(node.OutputAddress, PlcValue.FromReal((float)measured));
                LiveNodeStatusText[node.Id] = $"{measured * 1000.0:0.#}ms";
                return true;
            }

            // =========================================================================
            // NHÓM 9: WORD LOGIC OPERATIONS (MULTI-INPUT + EN/ENO)
            // =========================================================================
            case LadderElementKind.WordAnd:
            case LadderElementKind.WordOr:
            case LadderElementKind.WordXor:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                if (!powerIn)
                {
                    LiveNodeStatusText[node.Id] = FormatAddressLiveHex(node.OutputAddress, targetDt);
                    return false;
                }

                var rawInputs = CollectAllRawBitInputs(node, targetDt);
                if (rawInputs.Count == 0) return false;
                uint acc = rawInputs[0];
                for (int i = 1; i < rawInputs.Count; i++)
                {
                    acc = node.Kind switch
                    {
                        LadderElementKind.WordAnd => acc & rawInputs[i],
                        LadderElementKind.WordOr => acc | rawInputs[i],
                        _ => acc ^ rawInputs[i]
                    };
                }

                var res = PlcValue.FromRawBits(acc, targetDt);
                WriteTypedValue(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = res.Format(PlcWatchFormat.Hex);
                return true;
            }

            case LadderElementKind.WordInvert:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                if (!powerIn) return false;
                uint raw = ReadTypedValue(node.Param1, targetDt).AsRawUInt32;
                var res = PlcValue.FromRawBits(~raw, targetDt);
                WriteTypedValue(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = res.Format(PlcWatchFormat.Hex);
                return true;
            }

            case LadderElementKind.WordDeco:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                if (!powerIn) return false;
                int bitWidth = PlcDataTypeHelper.BitSize(targetDt);
                int n = ((int)Math.Round(ReadNumber(node.Param1))) & (bitWidth - 1);
                var res = PlcValue.FromRawBits(1u << n, targetDt);
                WriteTypedValue(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"Bit {n}";
                return true;
            }

            case LadderElementKind.WordEnco:
            {
                if (!powerIn) return false;
                uint v = ReadTypedValue(node.Param1).AsRawUInt32;
                if (v == 0)
                {
                    WriteNumber(node.OutputAddress, 0);
                    LiveNodeStatusText[node.Id] = "Pos=0 (ENO=0)";
                    return false; // Chuẩn IEC Table 8-171: IN == 0 -> OUT=0, ENO=FALSE
                }
                int pos = 0;
                while (((v >> pos) & 1u) == 0 && pos < 31) pos++;
                WriteNumber(node.OutputAddress, pos);
                LiveNodeStatusText[node.Id] = $"Pos={pos}";
                return true;
            }

            case LadderElementKind.WordSel:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn) return false;
                bool g = ReadBit(node.Address);
                var chosen = ReadTypedValue(g ? node.Param2 : node.Param1, targetDt);
                WriteTypedValue(node.OutputAddress, chosen);
                LiveNodeStatusText[node.Id] = $"= {chosen.Format(PlcWatchFormat.Auto)}";
                return true;
            }

            case LadderElementKind.WordMux:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node);
                if (!powerIn) return false;
                int k = (int)Math.Round(ReadNumber(node.Address));
                var muxInputs = new List<string> { node.Param1, node.Param2 };
                if (node.ExtraInputs is not null) muxInputs.AddRange(node.ExtraInputs);

                if (k >= 0 && k < muxInputs.Count)
                {
                    var chosen = ReadTypedValue(muxInputs[k], targetDt);
                    WriteTypedValue(node.OutputAddress, chosen);
                    LiveNodeStatusText[node.Id] = $"K={k}→{chosen.Format(PlcWatchFormat.Auto)}";
                    return true;
                }
                if (!string.IsNullOrWhiteSpace(node.ExtraParam))
                {
                    var fallback = ReadTypedValue(node.ExtraParam, targetDt);
                    WriteTypedValue(node.OutputAddress, fallback);
                    LiveNodeStatusText[node.Id] = $"ELSE→{fallback.Format(PlcWatchFormat.Auto)}";
                }
                return false; // Chuẩn IEC Table 8-179: K vượt giới hạn -> ENO=FALSE
            }

            case LadderElementKind.WordDemux:
            {
                if (!powerIn) return false;
                int k = (int)Math.Round(ReadNumber(node.Address));
                var v = ReadTypedValue(node.Param1);
                string? dst = k switch
                {
                    0 => node.Param2,
                    1 => node.ExtraParam,
                    2 => node.OutputAddress,
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(dst))
                {
                    WriteTypedValue(dst, v);
                    LiveNodeStatusText[node.Id] = $"K={k}→OUT{k}";
                    return true;
                }
                LiveNodeStatusText[node.Id] = $"K={k} (ENO=0)";
                return false; // Chuẩn IEC: K vượt phạm vi OUT0..OUT2 -> không ghi bừa và ENO=FALSE
            }

            // =========================================================================
            // NHÓM 10: SHIFT AND ROTATE (Byte 8-bit / Word 16-bit / DWord 32-bit)
            // =========================================================================
            case LadderElementKind.ShiftRight:
            case LadderElementKind.ShiftLeft:
            case LadderElementKind.RotateRight:
            case LadderElementKind.RotateLeft:
            {
                PlcDataType targetDt = ResolveBoxTargetType(node, PlcDataType.Word);
                if (!powerIn) return false;

                int width = PlcDataTypeHelper.BitSize(targetDt);
                uint mask = width == 32 ? 0xFFFFFFFFu : ((1u << width) - 1u);
                uint val = ReadTypedValue(node.Param1, targetDt).AsRawUInt32 & mask;
                int n = Math.Max(0, (int)Math.Round(ReadNumber(node.Param2)));

                uint resBits;
                if (node.Kind is LadderElementKind.ShiftRight or LadderElementKind.ShiftLeft)
                {
                    if (n >= width) resBits = 0u;
                    else resBits = node.Kind == LadderElementKind.ShiftRight
                        ? (val >> n)
                        : ((val << n) & mask);
                }
                else
                {
                    int rot = n % width;
                    resBits = rot == 0
                        ? val
                        : node.Kind == LadderElementKind.RotateRight
                            ? ((val >> rot) | ((val << (width - rot)) & mask))
                            : (((val << rot) & mask) | (val >> (width - rot)));
                }

                var res = PlcValue.FromRawBits(resBits, targetDt);
                WriteTypedValue(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = res.Format(PlcWatchFormat.Hex);
                return true;
            }

            default:
            {
                bool extRes = EvaluateExtendedNode(node, powerIn, dt, out bool handled);
                return handled ? extRes : powerIn;
            }
        }
    }

    private bool EvaluateTimer(LadderNode node, bool powerIn, double dt)
    {
        string dbKey = ResolveRuntimeAddress(node.Address);
        if (string.IsNullOrWhiteSpace(dbKey) || dbKey == "<???>") return false;
        if (!TimerDbs.TryGetValue(dbKey, out var st))
            TimerDbs[dbKey] = st = new PlcTimerDbState();

        TryReadTimeSeconds(node.Param1, out double ptSec);
        if (ptSec < 0) ptSec = 0.0;
        st.In = powerIn;
        st.PresetSeconds = ptSec;

        switch (node.Kind)
        {
            case LadderElementKind.TimerTON:
            case LadderElementKind.CoilTON:
                if (powerIn)
                {
                    if (!st.PrevIn)
                    {
                        // Chuẩn IEC Part 1: chốt PT tại cạnh lên khởi kích; thay đổi PT khi đang chạy không tác động chu kỳ hiện tại
                        st.LatchedPresetSeconds = ptSec;
                        st.ElapsedSeconds = 0.0;
                        st.Q = st.LatchedPresetSeconds <= 0.0;
                    }
                    else
                    {
                        double effPt = st.LatchedPresetSeconds;
                        st.ElapsedSeconds = Math.Min(effPt, st.ElapsedSeconds + dt);
                        st.Q = st.ElapsedSeconds >= effPt - 1e-9;
                    }
                }
                else
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = false;
                }
                break;

            case LadderElementKind.TimerTOF:
            case LadderElementKind.CoilTOF:
                if (powerIn)
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = true;
                    st.TimingActive = false;
                }
                else
                {
                    if (st.PrevIn)
                    {
                        // Cạnh xuống IN (1→0): chốt PT và bắt đầu trễ ngắt
                        st.LatchedPresetSeconds = ptSec;
                        st.ElapsedSeconds = 0.0;
                        if (st.LatchedPresetSeconds <= 0.0)
                        {
                            st.Q = false;
                            st.TimingActive = false;
                        }
                        else
                        {
                            st.Q = true;
                            st.TimingActive = true;
                        }
                    }
                    else if (st.TimingActive)
                    {
                        double effPt = st.LatchedPresetSeconds;
                        st.ElapsedSeconds = Math.Min(effPt, st.ElapsedSeconds + dt);
                        if (st.ElapsedSeconds >= effPt - 1e-9)
                        {
                            st.Q = false;
                            st.TimingActive = false;
                        }
                    }
                }
                break;

            case LadderElementKind.TimerTP:
            case LadderElementKind.CoilTP:
                if (powerIn && !st.PrevIn && !st.PulseActive)
                {
                    st.LatchedPresetSeconds = ptSec;
                    st.ElapsedSeconds = 0.0;
                    if (st.LatchedPresetSeconds > 0.0)
                    {
                        st.PulseActive = true;
                        st.Q = true;
                    }
                    else
                    {
                        st.PulseActive = false;
                        st.Q = false;
                    }
                }
                else if (st.PulseActive)
                {
                    double effPt = st.LatchedPresetSeconds;
                    st.ElapsedSeconds = Math.Min(effPt, st.ElapsedSeconds + dt);
                    if (st.ElapsedSeconds >= effPt - 1e-9)
                    {
                        st.Q = false;
                        st.PulseActive = false;
                    }
                }
                else if (!powerIn)
                {
                    st.ElapsedSeconds = 0.0;
                }
                break;

            case LadderElementKind.TimerTONR:
            case LadderElementKind.CoilTONR:
            {
                bool resetBit = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                st.LatchedPresetSeconds = ptSec; // Chuẩn IEC Part 1: TONR áp dụng PT mới khi chạy/tiếp tục
                if (resetBit)
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = false;
                }
                else if (powerIn)
                {
                    if (st.PrevIn || st.ElapsedSeconds > 0.0)
                        st.ElapsedSeconds = Math.Min(ptSec, st.ElapsedSeconds + dt);
                    st.Q = st.ElapsedSeconds >= ptSec - 1e-9;
                }
                else
                {
                    // Đánh giá lại Q nếu PT bị thay đổi cao hơn ET trong lúc IN=0
                    if (st.Q && st.ElapsedSeconds < ptSec - 1e-9)
                        st.Q = false;
                }
                break;
            }
        }

        st.PrevIn = powerIn;
        LiveNodeStatusText[node.Id] = FormatElapsedTime(st.ElapsedSeconds);
        return st.Q;
    }

    private bool EvaluateCounter(LadderNode node, bool powerIn)
    {
        string dbKey = ResolveRuntimeAddress(node.Address);
        if (string.IsNullOrWhiteSpace(dbKey) || dbKey == "<???>") return false;
        if (!CounterDbs.TryGetValue(dbKey, out var st))
            CounterDbs[dbKey] = st = new PlcCounterDbState();

        PlcDataType counterDt = PlcDataTypeHelper.TryParse(node.DataType, out var parsedDt) &&
                                !PlcDataTypeHelper.IsFloatingPoint(parsedDt) && parsedDt != PlcDataType.Bool
            ? parsedDt
            : PlcDataType.Int;
        st.DataType = counterDt;

        var (minD, maxD) = PlcDataTypeHelper.GetNumericBounds(counterDt);
        long minBound = (long)minD;
        long maxBound = (long)maxD;

        long pv = Math.Clamp((long)Math.Round(ReadNumber(node.Param1, counterDt)), minBound, maxBound);
        st.Pv = pv;
        st.Cv = Math.Clamp(st.Cv, minBound, maxBound);
        st.Cu = powerIn;

        switch (node.Kind)
        {
            case LadderElementKind.CounterCTU:
            {
                bool r = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                st.R = r;
                if (r) st.Cv = 0;
                else if (powerIn && !st.PrevCu && st.Cv < maxBound) st.Cv++;
                st.PrevCu = powerIn;
                st.Qu = st.Cv >= pv;
                st.Qd = st.Cv <= 0;
                LiveNodeStatusText[node.Id] = $"CV: {st.Cv}";
                return st.Qu;
            }
            case LadderElementKind.CounterCTD:
            {
                bool ld = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                st.Ld = ld;
                st.Cd = powerIn;
                if (ld) st.Cv = pv;
                else if (powerIn && !st.PrevCd && st.Cv > minBound) st.Cv--;
                st.PrevCd = powerIn;
                st.Qd = st.Cv <= 0;
                st.Qu = st.Cv >= pv;
                LiveNodeStatusText[node.Id] = $"CV: {st.Cv}";
                return st.Qd;
            }
            case LadderElementKind.CounterCTUD:
            {
                bool r = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                bool cd = !string.IsNullOrWhiteSpace(node.OutputAddress) && ReadBit(node.OutputAddress);
                bool ld = !string.IsNullOrWhiteSpace(node.ExtraParam) && ReadBit(node.ExtraParam);
                st.R = r;
                st.Cd = cd;
                st.Ld = ld;
                if (r) st.Cv = 0;
                else if (ld) st.Cv = pv;
                else
                {
                    if (powerIn && !st.PrevCu && st.Cv < maxBound) st.Cv++;
                    if (cd && !st.PrevCd && st.Cv > minBound) st.Cv--;
                }
                st.PrevCu = powerIn;
                st.PrevCd = cd;
                st.Qu = st.Cv >= pv;
                st.Qd = st.Cv <= 0;
                LiveNodeStatusText[node.Id] = $"CV: {st.Cv}|QD:{(st.Qd ? "1" : "0")}";
                return st.Qu;
            }
        }
        return false;
    }

    // =========================================================================
    // TRỢ GIÚP PHÂN GIẢI, ĐỌC / GHI CÓ KIỂU (PlcValue + Slice + :P + DB)
    // =========================================================================

    private ResolvedOperand ResolveOp(string? raw) => OperandResolver.Resolve(raw, _activeProgram);

    private string ResolveRuntimeAddress(string? raw)
    {
        var op = ResolveOp(raw);
        return op.IsValid ? op.NormalizedText : PlcProgramModel.NormalizeAddress(raw);
    }

    private PlcDataType ResolveBoxTargetType(LadderNode node, PlcDataType fallback = PlcDataType.Int)
    {
        if (!string.IsNullOrWhiteSpace(node.DataType) && PlcDataTypeHelper.TryParse(node.DataType, out var explicitDt))
            return explicitDt;

        var outOp = ResolveOp(node.OutputAddress);
        if (outOp.IsValid && !outOp.IsBitAccess)
            return outOp.InferredType;

        var in1Op = ResolveOp(node.Param1);
        if (in1Op.IsValid && !in1Op.IsBitAccess)
            return in1Op.InferredType;

        return fallback;
    }

    private List<double> CollectAllNumericInputs(LadderNode node, PlcDataType dt)
    {
        var list = new List<double>();
        if (!string.IsNullOrWhiteSpace(node.Param1)) list.Add(ReadNumber(node.Param1, dt));
        if (!string.IsNullOrWhiteSpace(node.Param2)) list.Add(ReadNumber(node.Param2, dt));
        if (node.ExtraInputs is not null)
        {
            foreach (string s in node.ExtraInputs)
                list.Add(ReadNumber(s, dt));
        }
        return list;
    }

    /// <summary>
    /// Thu thập mảng tham số giữ nguyên vị trí chỉ số IN1, IN2, IN3... (dùng cho CALCULATE để không bao giờ bị lệch chân).
    /// </summary>
    private List<double> CollectPositionalNumericInputs(LadderNode node, PlcDataType dt)
    {
        var list = new List<double>
        {
            string.IsNullOrWhiteSpace(node.Param1) ? 0.0 : ReadNumber(node.Param1, dt),
            string.IsNullOrWhiteSpace(node.Param2) ? 0.0 : ReadNumber(node.Param2, dt)
        };
        if (node.ExtraInputs is not null)
        {
            foreach (string s in node.ExtraInputs)
                list.Add(string.IsNullOrWhiteSpace(s) ? 0.0 : ReadNumber(s, dt));
        }
        return list;
    }

    private List<uint> CollectAllRawBitInputs(LadderNode node, PlcDataType dt)
    {
        var list = new List<uint>();
        if (!string.IsNullOrWhiteSpace(node.Param1)) list.Add(ReadTypedValue(node.Param1, dt).AsRawUInt32);
        if (!string.IsNullOrWhiteSpace(node.Param2)) list.Add(ReadTypedValue(node.Param2, dt).AsRawUInt32);
        if (node.ExtraInputs is not null)
        {
            foreach (string s in node.ExtraInputs)
                list.Add(ReadTypedValue(s, dt).AsRawUInt32);
        }
        return list;
    }

    private bool TryReadTimeSeconds(string? raw, out double seconds)
    {
        if (OperandResolver.TryParseTimeSeconds(raw, out seconds))
            return true;

        var op = ResolveOp(raw);
        if (op.IsValid && op.IsReadable)
        {
            seconds = ReadTypedValue(op, PlcDataType.Time).AsTimeMs / 1000.0;
            return true;
        }
        seconds = 0.0;
        return false;
    }

    public bool ReadBit(string? address) => ReadBitOperand(ResolveOp(address));

    private bool ReadBitOperand(ResolvedOperand op)
    {
        if (!op.IsValid || !op.IsReadable) return false;

        if (op.IsLiteral)
            return op.LiteralValue?.AsBool ?? false;

        if (op.SliceKind == PlcSliceKind.Bit)
        {
            if (!TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, out uint raw))
                return false;
            return ((raw >> op.SliceIndex) & 1u) != 0;
        }

        if (op.Area == PlcMemoryArea.DbMember)
        {
            string dbKey = $"%DB{op.DbNumber}";
            if (TimerDbs.TryGetValue(dbKey, out var t))
            {
                if (op.DbMember == "Q") return t.Q;
                if (op.DbMember == "IN") return t.In;
            }
            if (CounterDbs.TryGetValue(dbKey, out var c))
            {
                if (op.DbMember is "Q" or "QU") return c.Qu;
                if (op.DbMember == "QD") return c.Qd;
                if (op.DbMember == "CU") return c.Cu;
                if (op.DbMember == "CD") return c.Cd;
                if (op.DbMember == "R") return c.R;
                if (op.DbMember == "LD") return c.Ld;
            }
            if (op.DbMember == "Q" && EdgeDbPrevState.TryGetValue($"{dbKey}.Q", out bool trigQ))
                return trigQ;
            if (op.DbMember == "CLK" && EdgeDbPrevState.TryGetValue(dbKey, out bool trigClk))
                return trigClk;
            return false;
        }

        if (op.Area == PlcMemoryArea.DB && op.AccessSizeBytes == 0 && !op.IsDbInstance)
        {
            byte[] buf = GetOrCreateDbBuffer(op.DbNumber);
            if (op.ByteOffset < 0 || op.ByteOffset >= buf.Length) return false;
            return ((buf[op.ByteOffset] >> op.BitOffset) & 1) != 0;
        }

        return ReadBitLinear(op.Area, op.ByteOffset * 8 + op.BitOffset, op.IsImmediate);
    }

    public void WriteBit(string? address, bool value) => WriteBitOperand(ResolveOp(address), value);

    private void WriteBitOperand(ResolvedOperand op, bool value)
    {
        if (!op.IsValid || !op.IsWritable) return;

        if (op.SliceKind == PlcSliceKind.Bit)
        {
            if (!TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, isImmediate: false, out uint raw))
                return;
            if (value) raw |= (1u << op.SliceIndex);
            else raw &= ~(1u << op.SliceIndex);
            TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, raw);
            return;
        }

        if (op.Area == PlcMemoryArea.DB && op.AccessSizeBytes == 0)
        {
            byte[] buf = GetOrCreateDbBuffer(op.DbNumber);
            if (op.ByteOffset < 0 || op.ByteOffset >= buf.Length) return;
            if (value) buf[op.ByteOffset] |= (byte)(1 << op.BitOffset);
            else buf[op.ByteOffset] &= (byte)~(1 << op.BitOffset);
            return;
        }

        WriteBitLinear(op.Area, op.ByteOffset * 8 + op.BitOffset, value, op.IsImmediate);
    }

    private bool ReadBitLinear(PlcMemoryArea area, int linearBit, bool isImmediate)
    {
        if (linearBit < 0) return false;
        switch (area)
        {
            case PlcMemoryArea.I:
                if (isImmediate)
                {
                    if (_watchInputBitOverrides.TryGetValue(linearBit, out bool forcedDi))
                        return forcedDi;
                    return linearBit < PhysicalDi.Length && PhysicalDi[linearBit];
                }
                int ib = linearBit / 8;
                return ib < _imageI.Length && ((_imageI[ib] >> (linearBit % 8)) & 1) != 0;

            case PlcMemoryArea.Q:
                int qb = linearBit / 8;
                return qb < _imageQ.Length && ((_imageQ[qb] >> (linearBit % 8)) & 1) != 0;

            case PlcMemoryArea.M:
                int mb = linearBit / 8;
                return mb < _memoryM.Length && ((_memoryM[mb] >> (linearBit % 8)) & 1) != 0;

            default:
                return false;
        }
    }

    private void WriteBitLinear(PlcMemoryArea area, int linearBit, bool value, bool isImmediate)
    {
        if (linearBit < 0) return;
        int byteIdx = linearBit / 8;
        int bitIdx = linearBit % 8;

        if (area == PlcMemoryArea.Q && byteIdx < _imageQ.Length)
        {
            if (value) _imageQ[byteIdx] |= (byte)(1 << bitIdx);
            else _imageQ[byteIdx] &= (byte)~(1 << bitIdx);

            // Ghi tức thời (:P): cập nhật ngay cả chân vật lý DQ trong cùng vòng quét (vẫn tôn trọng Force Q nếu có)!
            if (isImmediate && linearBit < PhysicalDq.Length)
                PhysicalDq[linearBit] = _watchOutputBitOverrides.TryGetValue(linearBit, out bool forcedQ) ? forcedQ : value;
        }
        else if (area == PlcMemoryArea.M && byteIdx < _memoryM.Length)
        {
            if (value) _memoryM[byteIdx] |= (byte)(1 << bitIdx);
            else _memoryM[byteIdx] &= (byte)~(1 << bitIdx);
        }
    }

    public PlcValue ReadTypedValue(string? raw, PlcDataType? overrideType = null) =>
        ReadTypedValue(ResolveOp(raw), overrideType);

    private PlcValue ReadTypedValue(ResolvedOperand op, PlcDataType? overrideType = null)
    {
        if (!op.IsValid || !op.IsReadable) return PlcValue.FromInt(0);

        if (op.IsLiteral && op.LiteralValue.HasValue)
            return op.LiteralValue.Value;

        if (op.IsBitAccess)
            return PlcValue.FromBool(ReadBitOperand(op));

        if (op.Area == PlcMemoryArea.DbMember)
        {
            string dbKey = $"%DB{op.DbNumber}";
            if (op.DbMember == "CV" && CounterDbs.TryGetValue(dbKey, out var c))
                return PlcValue.TryFromDouble(c.Cv, c.DataType, out var cvVal) ? cvVal : PlcValue.FromDInt((int)Math.Clamp(c.Cv, int.MinValue, int.MaxValue));
            if (op.DbMember == "PV" && CounterDbs.TryGetValue(dbKey, out var cp))
                return PlcValue.TryFromDouble(cp.Pv, cp.DataType, out var pvVal) ? pvVal : PlcValue.FromDInt((int)Math.Clamp(cp.Pv, int.MinValue, int.MaxValue));
            if (op.DbMember == "ET" && TimerDbs.TryGetValue(dbKey, out var t)) return PlcValue.FromTimeMs((int)Math.Round(t.ElapsedSeconds * 1000.0));
            if (op.DbMember == "PT" && TimerDbs.TryGetValue(dbKey, out var tp)) return PlcValue.FromTimeMs((int)Math.Round(tp.PresetSeconds * 1000.0));
            return PlcValue.FromInt(0);
        }

        if (op.InferredType == PlcDataType.String || (overrideType == PlcDataType.String))
            return PlcValue.FromString(StringStore.GetValueOrDefault(op.NormalizedText, string.Empty));
        if (op.InferredType == PlcDataType.DTL || (overrideType == PlcDataType.DTL))
            return PlcValue.FromDtl(DtlStore.TryGetValue(op.NormalizedText, out var dtl) ? dtl : PlcDtlValue.FromDateTime(DateTime.Now));

        // Đọc LReal 64-bit (8 byte Big-Endian)
        if ((op.InferredType == PlcDataType.LReal || overrideType == PlcDataType.LReal) && op.SliceKind == PlcSliceKind.None)
        {
            if (TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, 4, op.IsImmediate, out uint hi) &&
                TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset + 4, 4, op.IsImmediate, out uint lo))
            {
                ulong bits64 = ((ulong)hi << 32) | lo;
                return PlcValue.FromRawBits64(bits64, PlcDataType.LReal);
            }
            return PlcValue.FromLReal(0.0);
        }

        if (!TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, out uint raw))
            return PlcValue.FromInt(0);

        if (op.SliceKind == PlcSliceKind.Byte)
            return PlcValue.FromByte((byte)((raw >> (op.SliceIndex * 8)) & 0xFF));
        if (op.SliceKind == PlcSliceKind.Word)
            return PlcValue.FromWord((ushort)((raw >> (op.SliceIndex * 16)) & 0xFFFF));

        // Ưu tiên kiểu khai báo trên Tag nếu có, nếu không dùng kiểu yêu cầu của hộp lệnh hoặc kiểu mặc định theo kích thước
        PlcDataType dt = op.InferredType;
        if (string.IsNullOrEmpty(op.ResolvedTagName) && overrideType.HasValue &&
            PlcDataTypeHelper.ByteSize(overrideType.Value) == op.AccessSizeBytes)
        {
            dt = overrideType.Value;
        }
        return PlcValue.FromRawBits(raw, dt);
    }

    public void WriteTypedValue(string? raw, PlcValue value) => WriteTypedValue(ResolveOp(raw), value);

    private void WriteTypedValue(ResolvedOperand op, PlcValue value)
    {
        if (!op.IsValid || !op.IsWritable) return;

        if (op.IsBitAccess)
        {
            WriteBitOperand(op, value.AsBool);
            return;
        }

        if (op.SliceKind == PlcSliceKind.Byte)
        {
            if (!TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, false, out uint cur)) return;
            uint bVal = value.AsRawUInt32 & 0xFFu;
            int shift = op.SliceIndex * 8;
            cur = (cur & ~(0xFFu << shift)) | (bVal << shift);
            TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, cur);
            return;
        }

        if (op.SliceKind == PlcSliceKind.Word)
        {
            if (!TryReadRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, false, out uint cur)) return;
            uint wVal = value.AsRawUInt32 & 0xFFFFu;
            int shift = op.SliceIndex * 16;
            cur = (cur & ~(0xFFFFu << shift)) | (wVal << shift);
            TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, cur);
            return;
        }

        if (value.DataType == PlcDataType.String || op.InferredType == PlcDataType.String)
        {
            WriteStringOperand(op.NormalizedText, value.AsString);
            return;
        }
        if (value.DataType == PlcDataType.DTL || op.InferredType == PlcDataType.DTL)
        {
            WriteDtlOperand(op.NormalizedText, value.AsDtl);
            return;
        }
        if ((value.DataType == PlcDataType.LReal || op.InferredType == PlcDataType.LReal) && op.SliceKind == PlcSliceKind.None)
        {
            ulong bits64 = value.DataType == PlcDataType.LReal
                ? value.AsRawUInt64
                : unchecked((ulong)BitConverter.DoubleToInt64Bits(value.AsDouble));
            TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset, 4, op.IsImmediate, (uint)(bits64 >> 32));
            TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset + 4, 4, op.IsImmediate, (uint)(bits64 & 0xFFFFFFFFu));
            return;
        }

        // Bảo toàn bit chính xác khi ghi đúng độ rộng (VD: ghi Real vào %MD, ghi Word vào %MW)
        PlcValue toStore = value;
        if (PlcDataTypeHelper.ByteSize(value.DataType) != op.AccessSizeBytes)
        {
            PlcDataType fitDt = op.InferredType;
            if (!value.TryConvertTo(fitDt, out toStore))
                toStore = PlcValue.FromRawBits(value.AsRawUInt32, fitDt);
        }

        TryWriteRawBytes(op.Area, op.DbNumber, op.ByteOffset, op.AccessSizeBytes, op.IsImmediate, toStore.AsRawUInt32);
    }

    public double ReadNumber(string? raw, PlcDataType? hintType = null) =>
        ReadTypedValue(raw, hintType).AsDouble;

    public int ReadWordOrConstant(string? raw) => (int)Math.Round(ReadNumber(raw));

    public void WriteNumber(string? raw, double value)
    {
        var op = ResolveOp(raw);
        if (!op.IsValid || !op.IsWritable) return;
        if (PlcValue.TryFromDouble(value, op.InferredType, out var v))
            WriteTypedValue(op, v);
    }

    private bool TryReadRawBytes(PlcMemoryArea area, int dbNum, int offset, int sizeBytes, bool isImmediate, out uint raw)
    {
        raw = 0;
        if (offset < 0 || sizeBytes <= 0) return false;

        // Thanh ghi chuyên dụng HSC (%ID1000..%ID1020) và PWM (%QW1000..%QW1006) chuẩn Part 2
        if (area == PlcMemoryArea.I && sizeBytes == 4 && offset is 1000 or 1004 or 1008 or 1012 or 1016 or 1020)
        {
            int hscIdx = (offset - 1000) / 4;
            raw = unchecked((uint)_hscCurrentValues[hscIdx]);
            return true;
        }
        if (area == PlcMemoryArea.Q && sizeBytes == 2 && offset is 1000 or 1002 or 1004 or 1006)
        {
            int pwmIdx = (offset - 1000) / 2;
            raw = _pwmWidthWords[pwmIdx];
            return true;
        }

        if (area == PlcMemoryArea.I && isImmediate)
        {
            byte ReadPhysicalDiByte(int byteOff)
            {
                byte b = 0;
                for (int i = 0; i < 8; i++)
                {
                    int idx = byteOff * 8 + i;
                    bool val = _watchInputBitOverrides.TryGetValue(idx, out bool forced)
                        ? forced
                        : (idx < PhysicalDi.Length && PhysicalDi[idx]);
                    if (val) b |= (byte)(1 << i);
                }
                return b;
            }

            if (sizeBytes == 2 && offset == 64)
            {
                short v = _watchAiOverrides.TryGetValue(64, out short f0) ? f0 : (short)Math.Clamp(PhysicalAi[0], short.MinValue, short.MaxValue);
                raw = unchecked((ushort)v);
                return true;
            }
            if (sizeBytes == 2 && offset == 66)
            {
                short v = _watchAiOverrides.TryGetValue(66, out short f1) ? f1 : (short)Math.Clamp(PhysicalAi[1], short.MinValue, short.MaxValue);
                raw = unchecked((ushort)v);
                return true;
            }
            if (sizeBytes == 1 && offset <= 1)
            {
                raw = ReadPhysicalDiByte(offset);
                return true;
            }
            if (sizeBytes == 2 && offset == 0)
            {
                raw = (uint)((ReadPhysicalDiByte(0) << 8) | ReadPhysicalDiByte(1));
                return true;
            }
            if (sizeBytes == 4 && offset == 0)
            {
                raw = (uint)((ReadPhysicalDiByte(0) << 24) | (ReadPhysicalDiByte(1) << 16));
                return true;
            }
        }

        // Ghi thanh ghi độ rộng xung PWM1..PWM4 đã xử lý ở TryWriteRawBytes; ở đây chỉ đọc _imageI/_imageQ/_memoryM/_rawDbs
        byte[]? buf = area switch
        {
            PlcMemoryArea.I => _imageI,
            PlcMemoryArea.Q => _imageQ,
            PlcMemoryArea.M => _memoryM,
            PlcMemoryArea.DB => GetOrCreateDbBuffer(dbNum),
            _ => null
        };
        if (buf is null || offset + sizeBytes > buf.Length) return false;

        raw = sizeBytes switch
        {
            1 => buf[offset],
            2 => BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset, 2)),
            4 => BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(offset, 4)),
            _ => 0u
        };
        return true;
    }

    private bool TryWriteRawBytes(PlcMemoryArea area, int dbNum, int offset, int sizeBytes, bool isImmediate, uint raw)
    {
        if (offset < 0 || sizeBytes <= 0) return false;

        // Ghi thanh ghi độ rộng xung PWM1..PWM4 (%QW1000, %QW1002, %QW1004, %QW1006)
        if (area == PlcMemoryArea.Q && sizeBytes == 2 && offset is 1000 or 1002 or 1004 or 1006)
        {
            int pwmIdx = (offset - 1000) / 2;
            _pwmWidthWords[pwmIdx] = (ushort)Math.Clamp((int)(raw & 0xFFFF), 0, 1000);
            return true;
        }

        byte[]? buf = area switch
        {
            PlcMemoryArea.Q => _imageQ,
            PlcMemoryArea.M => _memoryM,
            PlcMemoryArea.DB => GetOrCreateDbBuffer(dbNum),
            _ => null
        };
        if (buf is null || offset + sizeBytes > buf.Length) return false;

        switch (sizeBytes)
        {
            case 1:
                buf[offset] = (byte)(raw & 0xFF);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(offset, 2), (ushort)(raw & 0xFFFF));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(offset, 4), raw);
                break;
            default:
                return false;
        }

        if (area == PlcMemoryArea.Q && isImmediate && offset <= 1)
        {
            Phase3_WriteProcessImageToPhysicalOutputs();
        }
        return true;
    }

    private byte[] GetOrCreateDbBuffer(int dbNum)
    {
        if (!_rawDbs.TryGetValue(dbNum, out var buf))
            _rawDbs[dbNum] = buf = new byte[OperandResolver.MaxDbBytes];
        return buf;
    }

    // =========================================================================
    // WATCH TABLE & LIVE MONITORING FORMATTING
    // =========================================================================

    public (string ValueText, bool IsValid, string DataTypeName) ReadWatchFormatted(
        PlcProgramModel program,
        string? expression,
        PlcWatchFormat format)
    {
        if (string.IsNullOrWhiteSpace(expression)) return ("—", false, "—");

        var op = OperandResolver.Resolve(expression, program);
        if (!op.IsValid) return ("ERR", false, "—");

        if (op.IsDbInstance)
        {
            string dbKey = $"%DB{op.DbNumber}";
            if (TimerDbs.TryGetValue(dbKey, out var t))
                return ($"Q={(t.Q ? "1" : "0")}, ET={FormatElapsedTime(t.ElapsedSeconds)}", true, "IEC_Timer");
            if (CounterDbs.TryGetValue(dbKey, out var c))
                return ($"CV={c.Cv}, Q={(c.Qu ? "1" : "0")}", true, "IEC_Counter");
            return ("Idle", true, "DB");
        }

        var val = ReadTypedValue(op);
        return (val.Format(format), true, val.DataType.ToString());
    }

    /// <summary>
    /// Ghi / ép giá trị trực tiếp từ Watch Table (Modify Now / Force :P) khi đang mô phỏng.
    /// </summary>
    public bool TryModifyFromWatch(PlcProgramModel program, string? expression, string? newValueRaw, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(newValueRaw))
        {
            error = LocalizationManager.Get("Str.Plc.ErrMissingOperand");
            return false;
        }

        var op = OperandResolver.Resolve(expression, program);
        if (!op.IsValid)
        {
            error = op.ErrorMessage;
            return false;
        }

        if (!OperandResolver.TryParseLiteral(newValueRaw, out var parsedVal))
        {
            error = LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", newValueRaw);
            return false;
        }

        // Hỗ trợ ép ngõ vào I / AI từ Watch Table
        if (op.Area == PlcMemoryArea.I)
        {
            if (op.AccessSizeBytes == 0)
            {
                int lin = op.ByteOffset * 8 + op.BitOffset;
                bool b = parsedVal.AsBool;
                _watchInputBitOverrides[lin] = b;
                if (lin < PhysicalDi.Length) PhysicalDi[lin] = b;
                if (b) _imageI[op.ByteOffset] |= (byte)(1 << op.BitOffset);
                else _imageI[op.ByteOffset] &= (byte)~(1 << op.BitOffset);
                return true;
            }
            if (op.AccessSizeBytes == 2 && op.ByteOffset is 64 or 66)
            {
                short v = (short)Math.Clamp(parsedVal.AsInt64, short.MinValue, short.MaxValue);
                _watchAiOverrides[op.ByteOffset] = v;
                PhysicalAi[op.ByteOffset == 64 ? 0 : 1] = v;
                BinaryPrimitives.WriteInt16BigEndian(_imageI.AsSpan(op.ByteOffset, 2), v);
                return true;
            }
        }

        // Hỗ trợ ép ngõ ra vật lý Q (:P) từ Watch Table
        if (op.Area == PlcMemoryArea.Q && op.AccessSizeBytes == 0 && op.IsImmediate)
        {
            int lin = op.ByteOffset * 8 + op.BitOffset;
            bool b = parsedVal.AsBool;
            _watchOutputBitOverrides[lin] = b;
            WriteBitLinear(PlcMemoryArea.Q, lin, b, isImmediate: true);
            return true;
        }

        // Hỗ trợ ép thành viên Timer/Counter DB (PT/PV/CV)
        if (op.Area == PlcMemoryArea.DbMember)
        {
            string dbKey = $"%DB{op.DbNumber}";
            if (op.DbMember == "PT" && TimerDbs.TryGetValue(dbKey, out var t))
            {
                double pt = Math.Max(0.0, parsedVal.AsTimeMs / 1000.0);
                t.PresetSeconds = pt;
                t.LatchedPresetSeconds = pt;
                return true;
            }
            if (op.DbMember == "CV" && CounterDbs.TryGetValue(dbKey, out var c))
            {
                var (minD, maxD) = PlcDataTypeHelper.GetNumericBounds(c.DataType);
                c.Cv = Math.Clamp(parsedVal.AsInt64, (long)minD, (long)maxD);
                c.Qu = c.Cv >= c.Pv;
                c.Qd = c.Cv <= 0;
                return true;
            }
            if (op.DbMember == "PV" && CounterDbs.TryGetValue(dbKey, out var cp))
            {
                var (minD, maxD) = PlcDataTypeHelper.GetNumericBounds(cp.DataType);
                cp.Pv = Math.Clamp(parsedVal.AsInt64, (long)minD, (long)maxD);
                return true;
            }
        }

        if (!op.IsWritable)
        {
            error = LocalizationManager.Format("Str.Plc.ErrNotWritableBit", expression);
            return false;
        }

        if (op.IsBitAccess)
        {
            WriteBitOperand(op, parsedVal.AsBool);
            if (op.Area == PlcMemoryArea.Q)
                Phase3_WriteProcessImageToPhysicalOutputs();
            return true;
        }

        if (!parsedVal.TryConvertTo(op.InferredType, out var conv))
            conv = PlcValue.FromRawBits(parsedVal.AsRawUInt32, op.InferredType);

        WriteTypedValue(op, conv);
        if (op.Area == PlcMemoryArea.Q)
            Phase3_WriteProcessImageToPhysicalOutputs();
        return true;
    }

    public void ClearWatchInputOverrides()
    {
        _watchInputBitOverrides.Clear();
        _watchAiOverrides.Clear();
        _watchOutputBitOverrides.Clear();
    }

    public string FormatLiveValueForAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || _activeProgram is null) return "—";
        var (text, ok, _) = ReadWatchFormatted(_activeProgram, address, PlcWatchFormat.Auto);
        return ok ? text : "—";
    }

    public string FormatLiveValueForAddress(PlcProgramModel program, string? address)
    {
        var (text, ok, _) = ReadWatchFormatted(program, address, PlcWatchFormat.Auto);
        return ok ? text : "—";
    }

    private string FormatAddressLiveShort(string? addr, PlcDataType? hint = null)
    {
        var v = ReadTypedValue(addr, hint);
        return v.Format(PlcWatchFormat.Auto);
    }

    private string FormatAddressLiveHex(string? addr, PlcDataType dt)
    {
        var v = ReadTypedValue(addr, dt);
        return v.Format(PlcWatchFormat.Hex);
    }

    public static string FormatElapsedTime(double sec) =>
        OperandResolver.FormatTimeMs((int)Math.Round(sec * 1000.0));

    private static string FmtNum(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-5
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);
}
