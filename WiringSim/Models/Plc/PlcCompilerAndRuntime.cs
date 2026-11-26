using System.Globalization;
using System.Text.RegularExpressions;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Plc;

public sealed class PlcTimerDbState
{
    public double ElapsedSeconds { get; set; }
    public double PresetOverrideSeconds { get; set; } = -1.0;
    public bool Q { get; set; }
    public bool PrevIn { get; set; }
    public bool PulseActive { get; set; }
}

public sealed class PlcCounterDbState
{
    public int Cv { get; set; }
    public bool Qu { get; set; }
    public bool Qd { get; set; }
    public bool PrevCu { get; set; }
    public bool PrevCd { get; set; }
}

/// <summary>
/// Bộ biên dịch chương trình Ladder chuẩn S7-1200 cho đầy đủ 10 nhóm lệnh TIA Portal:
/// - Kiểm tra nhánh hở, địa chỉ bit/word/real (%I, %Q, %M, %MW, %MD, %IW, %QW, %DB), PT/PV, trùng %DB.
/// - Kiểm tra Empty box chưa gán tên lệnh.
/// - Kiểm tra đích nhảy -(JMP)/-(JMPN)/JMP_LIST/SWITCH tới LABEL không tồn tại hoặc tạo vòng lặp nhảy vô hạn.
/// </summary>
public static partial class PlcProgramCompiler
{
    [GeneratedRegex(@"^%?(?<area>I|Q|M)(?<byte>\d+)\.(?<bit>[0-7])$", RegexOptions.IgnoreCase)]
    private static partial Regex BitAddrRegex();

    [GeneratedRegex(@"^%?(?<area>IW|QW|MW|MD)(?<offset>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericAddrRegex();

    [GeneratedRegex(@"^%?DB(?<num>\d+)(?:\.(?<field>Q|QU|QD|ET|CV))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DbAddrRegex();

    [GeneratedRegex(@"^T#(?:(?<h>\d+(?:\.\d+)?)H)?(?:(?<m>\d+(?:\.\d+)?)M(?!S))?(?:(?<s>\d+(?:\.\d+)?)S)?(?:(?<ms>\d+(?:\.\d+)?)MS)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeLiteralRegex();

    public static bool TryParseTimeSeconds(string? raw, out double seconds)
    {
        seconds = 0.0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var match = TimeLiteralRegex().Match(raw.Trim());
        if (!match.Success) return false;

        bool anyGroup = match.Groups["h"].Success || match.Groups["m"].Success ||
                        match.Groups["s"].Success || match.Groups["ms"].Success;
        if (!anyGroup) return false;

        double total = 0.0;
        if (match.Groups["h"].Success) total += double.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) * 3600.0;
        if (match.Groups["m"].Success) total += double.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) * 60.0;
        if (match.Groups["s"].Success) total += double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["ms"].Success) total += double.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture) / 1000.0;

        seconds = total;
        return total >= 0.0;
    }

    public static bool IsValidBitAddress(string? raw, bool allowInputsAndDb, out string errorReason)
    {
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrEmptyAddr");
            return false;
        }

        string s = raw.Trim().ToUpperInvariant();
        var dbMatch = DbAddrRegex().Match(s);
        if (dbMatch.Success)
        {
            if (!allowInputsAndDb)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrNotWritableBit", raw);
                return false;
            }
            string field = dbMatch.Groups["field"].Value.ToUpperInvariant();
            if (field is "Q" or "QU" or "QD") return true;
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw);
            return false;
        }

        var m = BitAddrRegex().Match(s);
        if (!m.Success)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw);
            return false;
        }

        string area = m.Groups["area"].Value.ToUpperInvariant();
        int byteIdx = int.Parse(m.Groups["byte"].Value, CultureInfo.InvariantCulture);
        int bitIdx = int.Parse(m.Groups["bit"].Value, CultureInfo.InvariantCulture);

        switch (area)
        {
            case "I":
                if (!allowInputsAndDb)
                {
                    errorReason = LocalizationManager.Format("Str.Plc.ErrCannotWriteInput", raw);
                    return false;
                }
                if ((byteIdx == 0 && bitIdx <= 7) || (byteIdx == 1 && bitIdx <= 5)) return true;
                errorReason = LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%I0.0–%I1.5");
                return false;

            case "Q":
                if ((byteIdx == 0 && bitIdx <= 7) || (byteIdx == 1 && bitIdx <= 1)) return true;
                errorReason = LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%Q0.0–%Q1.1");
                return false;

            case "M":
                if (byteIdx <= 99) return true;
                errorReason = LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%M0.0–%M99.7");
                return false;

            default:
                errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidAddr", raw);
                return false;
        }
    }

    public static bool IsValidNumericOrAddress(string? raw, bool mustBeWritable, out string errorReason)
    {
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrMissingOperand");
            return false;
        }

        string s = raw.Trim().ToUpperInvariant();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            if (mustBeWritable)
            {
                errorReason = LocalizationManager.Format("Str.Plc.ErrTargetMustBeWord", raw);
                return false;
            }
            return true;
        }

        var dbMatch = DbAddrRegex().Match(s);
        if (dbMatch.Success)
        {
            string field = dbMatch.Groups["field"].Value.ToUpperInvariant();
            if (!mustBeWritable && field is "CV" or "ET") return true;
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", raw);
            return false;
        }

        var m = NumericAddrRegex().Match(s);
        if (!m.Success)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", raw);
            return false;
        }

        string area = m.Groups["area"].Value.ToUpperInvariant();
        int offset = int.Parse(m.Groups["offset"].Value, CultureInfo.InvariantCulture);

        return area switch
        {
            "IW" when mustBeWritable => Fail(LocalizationManager.Format("Str.Plc.ErrTargetMustBeWord", raw), out errorReason),
            "IW" when offset is 64 or 66 => true,
            "IW" => Fail(LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%IW64, %IW66"), out errorReason),
            "MW" when offset <= 198 => true,
            "MW" => Fail(LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%MW0–%MW198"), out errorReason),
            "MD" when offset <= 196 => true,
            "MD" => Fail(LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%MD0–%MD196"), out errorReason),
            "QW" when offset <= 16 => true,
            "QW" => Fail(LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%QW0–%QW16"), out errorReason),
            _ => Fail(LocalizationManager.Format("Str.Plc.ErrInvalidWordAddr", raw), out errorReason)
        };
    }

    private static bool Fail(string msg, out string err)
    {
        err = msg;
        return false;
    }

    public static bool IsValidDbInstanceAddress(string? raw, out string normalizedDb, out string errorReason)
    {
        normalizedDb = string.Empty;
        errorReason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            errorReason = LocalizationManager.Get("Str.Plc.ErrMissingDb");
            return false;
        }

        var m = DbAddrRegex().Match(raw.Trim());
        if (!m.Success || m.Groups["field"].Success)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrInvalidDb", raw);
            return false;
        }

        int num = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
        if (num < 1 || num > 99)
        {
            errorReason = LocalizationManager.Format("Str.Plc.ErrAddrOutOfRange", raw, "%DB1–%DB99");
            return false;
        }

        normalizedDb = $"%DB{num}";
        return true;
    }

    public static PlcCompileResult Compile(PlcProgramModel program)
    {
        var result = new PlcCompileResult();
        if (program.Networks.Count == 0)
        {
            result.Diagnostics.Add(new PlcDiagnostic(false, 0, null, LocalizationManager.Get("Str.Plc.WarnNoNetworks")));
            return result;
        }

        // Lượt 1: Thu thập toàn bộ LABEL định nghĩa trong chương trình để kiểm tra đích nhảy JMP/JMPN/JMP_LIST/SWITCH
        var definedLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < program.Networks.Count; i++)
        {
            CollectLabels(program.Networks[i].MainBranch, i + 1, definedLabels);
        }

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

            bool hasOutputOrLabel = ValidateBranch(net.MainBranch, netNum, hasConditionBefore: false, seenDbs, definedLabels, result);
            if (!hasOutputOrLabel)
            {
                result.Diagnostics.Add(new PlcDiagnostic(true, netNum, net.MainBranch.Nodes[^1].Id,
                    LocalizationManager.Format("Str.Plc.ErrOpenBranchNoOutput", netNum)));
            }
        }

        return result;
    }

    private static void CollectLabels(LadderBranch branch, int netNum, Dictionary<string, int> labels)
    {
        foreach (var node in branch.Nodes)
        {
            if (node.Kind == LadderElementKind.ParallelBlock && node.Branches is not null)
            {
                foreach (var sub in node.Branches) CollectLabels(sub, netNum, labels);
            }
            else if (node.Kind == LadderElementKind.Label && !string.IsNullOrWhiteSpace(node.Address))
            {
                labels.TryAdd(node.Address.Trim(), netNum);
            }
        }
    }

    private static bool ValidateBranch(
        LadderBranch branch,
        int netNum,
        bool hasConditionBefore,
        Dictionary<string, int> seenDbs,
        Dictionary<string, int> definedLabels,
        PlcCompileResult result)
    {
        bool hasAnyOutput = false;
        bool condSeen = hasConditionBefore;

        foreach (var node in branch.Nodes)
        {
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
                    bool subOut = ValidateBranch(sub, netNum, condSeen, seenDbs, definedLabels, result);
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

            if (node.IsOutputOrAction || node.Kind == LadderElementKind.Label)
                hasAnyOutput = true;
            else
                condSeen = true;

            ValidateSingleNode(node, netNum, condSeen, seenDbs, definedLabels, result);
        }

        return hasAnyOutput;
    }

    private static void ValidateSingleNode(
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
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: true, out string cErr)) Err($"NW{netNum}: {cErr}");
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
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string qErr)) Err($"NW{netNum}: {qErr}");
                break;

            case LadderElementKind.EdgePositive:
            case LadderElementKind.EdgeNegative:
            case LadderElementKind.CoilEdgePositive:
            case LadderElementKind.CoilEdgeNegative:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: node.Kind is LadderElementKind.EdgePositive or LadderElementKind.EdgeNegative, out string eErr))
                    Err($"NW{netNum}: {eErr}");
                if (!string.IsNullOrWhiteSpace(node.Param1) && !IsValidBitAddress(node.Param1, allowInputsAndDb: false, out string mErr))
                    Err($"NW{netNum} (M-Bit): {mErr}");
                break;

            case LadderElementKind.SetBitField:
            case LadderElementKind.ResetBitField:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string bfErr)) Err($"NW{netNum}: {bfErr}");
                if (!IsValidNumericOrAddress(node.Param1, false, out string nErr)) Err($"NW{netNum} (N): {nErr}");
                break;

            case LadderElementKind.FlipFlopSR:
            case LadderElementKind.FlipFlopRS:
                if (!IsValidBitAddress(node.Address, allowInputsAndDb: false, out string ffErr)) Err($"NW{netNum} (Q): {ffErr}");
                if (!string.IsNullOrWhiteSpace(node.Param1) && !IsValidBitAddress(node.Param1, allowInputsAndDb: true, out string ff2Err))
                    Err($"NW{netNum} (R1/S1): {ff2Err}");
                break;

            case LadderElementKind.RTrig:
            case LadderElementKind.FTrig:
                CheckDbUnique(node.Address, netNum, seenDbs, Err);
                break;

            case LadderElementKind.TimerTP:
            case LadderElementKind.TimerTON:
            case LadderElementKind.TimerTOF:
            case LadderElementKind.TimerTONR:
            case LadderElementKind.CoilTP:
            case LadderElementKind.CoilTON:
            case LadderElementKind.CoilTOF:
            case LadderElementKind.CoilTONR:
                CheckDbUnique(node.Address, netNum, seenDbs, Err);
                if (!TryParseTimeSeconds(node.Param1, out _))
                    Err(LocalizationManager.Format("Str.Plc.ErrInvalidPt", netNum, node.Param1));
                if (node.Kind == LadderElementKind.TimerTONR && !string.IsNullOrWhiteSpace(node.Param2) &&
                    !IsValidBitAddress(node.Param2, true, out string rErr))
                    Err($"NW{netNum} (R): {rErr}");
                break;

            case LadderElementKind.CoilRT:
                if (!IsValidDbInstanceAddress(node.Address, out _, out string rtErr)) Err($"NW{netNum}: {rtErr}");
                break;

            case LadderElementKind.CoilPT:
                if (!IsValidDbInstanceAddress(node.Address, out _, out string ptDbErr)) Err($"NW{netNum}: {ptDbErr}");
                if (!TryParseTimeSeconds(node.Param1, out _)) Err(LocalizationManager.Format("Str.Plc.ErrInvalidPt", netNum, node.Param1));
                break;

            case LadderElementKind.CounterCTU:
            case LadderElementKind.CounterCTD:
            case LadderElementKind.CounterCTUD:
                CheckDbUnique(node.Address, netNum, seenDbs, Err);
                if (!IsValidNumericOrAddress(node.Param1, false, out _))
                    Err(LocalizationManager.Format("Str.Plc.ErrInvalidPv", netNum, node.Param1));
                if (!string.IsNullOrWhiteSpace(node.Param2) && !IsValidBitAddress(node.Param2, true, out string crErr))
                    Err($"NW{netNum} (R/LD): {crErr}");
                if (node.Kind == LadderElementKind.CounterCTUD && !string.IsNullOrWhiteSpace(node.OutputAddress) &&
                    !IsValidBitAddress(node.OutputAddress, true, out string cdErr))
                    Err($"NW{netNum} (CD): {cdErr}");
                break;

            case LadderElementKind.CmpEqual:
            case LadderElementKind.CmpNotEqual:
            case LadderElementKind.CmpGreaterOrEqual:
            case LadderElementKind.CmpLessOrEqual:
            case LadderElementKind.CmpGreater:
            case LadderElementKind.CmpLess:
                if (!IsValidNumericOrAddress(node.Param1, false, out string c1Err)) Err($"NW{netNum} (IN1): {c1Err}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string c2Err)) Err($"NW{netNum} (IN2): {c2Err}");
                break;

            case LadderElementKind.InRange:
            case LadderElementKind.OutRange:
                if (!IsValidNumericOrAddress(node.Param1, false, out string vErr)) Err($"NW{netNum} (VAL): {vErr}");
                if (!IsValidNumericOrAddress(node.Param2, false, out string mnErr)) Err($"NW{netNum} (MIN): {mnErr}");
                if (!IsValidNumericOrAddress(node.ExtraParam, false, out string mxErr)) Err($"NW{netNum} (MAX): {mxErr}");
                break;

            case LadderElementKind.CheckOk:
            case LadderElementKind.CheckNotOk:
                if (!IsValidNumericOrAddress(node.Address, false, out string okErr)) Err($"NW{netNum}: {okErr}");
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
                if (!IsValidNumericOrAddress(node.Param1, false, out string kErr)) Err($"NW{netNum} (K): {kErr}");
                break;
            }

            case LadderElementKind.MathInc:
            case LadderElementKind.MathDec:
            case LadderElementKind.GetError:
            case LadderElementKind.GetErrId:
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string ioErr)) Err($"NW{netNum} (OUT): {ioErr}");
                break;

            case LadderElementKind.Scatter:
            case LadderElementKind.ScatterBlk:
                if (!IsValidNumericOrAddress(node.Param1, false, out string scInErr)) Err($"NW{netNum} (IN): {scInErr}");
                if (!IsValidBitAddress(node.OutputAddress, false, out string scOutErr)) Err($"NW{netNum} (OUT): {scOutErr}");
                break;

            case LadderElementKind.Gather:
            case LadderElementKind.GatherBlk:
                if (!IsValidBitAddress(node.Param1, true, out string gaInErr)) Err($"NW{netNum} (IN): {gaInErr}");
                if (!IsValidNumericOrAddress(node.OutputAddress, true, out string gaOutErr)) Err($"NW{netNum} (OUT): {gaOutErr}");
                break;

            default:
            {
                var meta = PlcInstructionCatalog.Get(node.Kind);
                if (meta is null || meta.GroupIndex == 4) break; // Variant comparators accept flexible names
                if (meta.OutLabel is not null && node.Kind != LadderElementKind.WordDemux &&
                    !IsValidNumericOrAddress(node.OutputAddress, true, out string oErr))
                    Err($"NW{netNum} ({meta.OutLabel}): {oErr}");
                if (meta.Param1Label is not null && node.Kind is not (LadderElementKind.VarEqTypeOfDb or LadderElementKind.VarNeTypeOfDb) &&
                    !IsValidNumericOrAddress(node.Param1, false, out string p1Err))
                    Err($"NW{netNum} ({meta.Param1Label}): {p1Err}");
                if (meta.Param2Label is not null && node.Kind is not (LadderElementKind.Calculate or LadderElementKind.WordDemux) &&
                    !IsValidNumericOrAddress(node.Param2, false, out string p2Err))
                    Err($"NW{netNum} ({meta.Param2Label}): {p2Err}");
                break;
            }
        }
    }

    private static void CheckDbUnique(string? rawDb, int netNum, Dictionary<string, int> seenDbs, Action<string> onError)
    {
        if (!IsValidDbInstanceAddress(rawDb, out string dbNorm, out string dbErr))
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

            // Bắt lỗi vòng lặp vô hạn: nhảy về đúng Network hiện tại (self-loop) hoặc nhảy ngược không điều kiện về Network trước đó!
            if (targetNet == netNum || (isUnconditional && targetNet < netNum))
            {
                onError(LocalizationManager.Format("Str.Plc.ErrInfiniteJumpLoop", netNum, lbl, targetNet));
            }
        }
    }
}

/// <summary>
/// Động cơ thực thi vòng quét PLC S7-1200 (50ms) hỗ trợ trọn vẹn 10 nhóm hàm/lệnh TIA Portal:
/// - Vùng nhớ Bit (%I, %Q, %M), Word (%MW, %IW, %QW), Real/DWord (%MD), DataBlock (%DB).
/// - Online Monitoring thời gian thực + bảo vệ chống treo vòng lặp nhảy tối đa 64 bước/vòng quét.
/// </summary>
public sealed class PlcRuntimeEngine
{
    public bool[] Di { get; } = new bool[14];
    public bool[] Dq { get; } = new bool[10];
    public int[] Ai { get; } = new int[2];
    public bool[] MemoryBits { get; } = new bool[800]; // %M0.0 .. %M99.7
    public Dictionary<int, int> MemoryWords { get; } = new();
    public Dictionary<int, double> MemoryReals { get; } = new(); // %MD0 .. %MD196
    public Dictionary<int, int> OutputWords { get; } = new();

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

    private string? _pendingJumpLabel;
    private bool _returnRequested;

    public void ResetAllMemory()
    {
        Array.Clear(Di);
        Array.Clear(Dq);
        Array.Clear(Ai);
        Array.Clear(MemoryBits);
        MemoryWords.Clear();
        MemoryReals.Clear();
        OutputWords.Clear();
        TimerDbs.Clear();
        CounterDbs.Clear();
        EdgeDbPrevState.Clear();
        LastErrorCode = 0;
        CpuStopRequested = false;
        ClearLiveOverlay();
    }

    public void ClearOutputsAndOverlay()
    {
        Array.Clear(Dq);
        ClearLiveOverlay();
    }

    public void ClearLiveOverlay()
    {
        LiveNodePowerIn.Clear();
        LiveNodePowerOut.Clear();
        LiveNodeStatusText.Clear();
    }

    public void ExecuteScanCycle(PlcProgramModel program, double deltaSeconds)
    {
        ClearLiveOverlay();
        double dt = Math.Clamp(deltaSeconds, 0.0, 0.25);
        LastCycleSeconds = dt > 0 ? dt : 0.05;
        CpuStopRequested = false;
        _returnRequested = false;
        _pendingJumpLabel = null;

        // Lập bảng ánh xạ LABEL -> chỉ số Network (0-based)
        var labelMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < program.Networks.Count; i++)
            IndexNetworkLabels(program.Networks[i].MainBranch, i, labelMap);

        int pc = 0;
        int jumpCount = 0;
        const int maxJumpsPerScan = 64;

        while (pc >= 0 && pc < program.Networks.Count)
        {
            var net = program.Networks[pc];
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
                if (labelMap.TryGetValue(target, out int targetIdx) && ++jumpCount <= maxJumpsPerScan)
                {
                    pc = targetIdx;
                    continue;
                }
                LastErrorCode = 0x8090; // Jump loop or missing label protection
                break;
            }

            pc++;
        }
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
                return !powerIn;

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
                if (powerIn && TryParseBitOffset(PlcProgramModel.NormalizeAddress(node.Address), out char area, out int startBit))
                {
                    bool targetVal = node.Kind == LadderElementKind.SetBitField;
                    for (int k = 0; k < count; k++) WriteBitLinear(area, startBit + k, targetVal);
                }
                LiveNodeStatusText[node.Id] = $"N={count}";
                return powerIn;
            }

            case LadderElementKind.FlipFlopSR:
            {
                bool s = powerIn;
                bool r1 = !string.IsNullOrWhiteSpace(node.Param1) && ReadBit(node.Param1);
                bool q = ReadBit(node.Address);
                if (r1) q = false;
                if (s) q = true; // SR: Set priority (or Reset if R1 is dominant; S overrides)
                WriteBit(node.Address, q);
                LiveNodeStatusText[node.Id] = q ? "Q=1" : "Q=0";
                return q;
            }

            case LadderElementKind.FlipFlopRS:
            {
                bool r = powerIn;
                bool s1 = !string.IsNullOrWhiteSpace(node.Param1) && ReadBit(node.Param1);
                bool q = ReadBit(node.Address);
                if (s1) q = true;
                if (r) q = false; // RS: Reset priority
                WriteBit(node.Address, q);
                LiveNodeStatusText[node.Id] = q ? "Q=1" : "Q=0";
                return q;
            }

            case LadderElementKind.EdgePositive:
            {
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    // Tương thích kiểu RLO edge (như Preset 5: Address là %M0.1)
                    bool prev = ReadBit(node.Address);
                    WriteBit(node.Address, powerIn);
                    return powerIn && !prev;
                }
                bool cur = ReadBit(node.Address);
                bool prevOp = ReadBit(node.Param1);
                WriteBit(node.Param1, cur);
                return powerIn && cur && !prevOp;
            }

            case LadderElementKind.EdgeNegative:
            {
                if (string.IsNullOrWhiteSpace(node.Param1))
                {
                    bool prev = ReadBit(node.Address);
                    WriteBit(node.Address, powerIn);
                    return !powerIn && prev;
                }
                bool cur = ReadBit(node.Address);
                bool prevOp = ReadBit(node.Param1);
                WriteBit(node.Param1, cur);
                return powerIn && !cur && prevOp;
            }

            case LadderElementKind.CoilEdgePositive:
            {
                string memKey = string.IsNullOrWhiteSpace(node.Param1) ? $"__CP_{node.Id}" : node.Param1;
                bool prev = string.IsNullOrWhiteSpace(node.Param1) ? EdgeDbPrevState.GetValueOrDefault(memKey) : ReadBit(memKey);
                bool pulse = powerIn && !prev;
                if (string.IsNullOrWhiteSpace(node.Param1)) EdgeDbPrevState[memKey] = powerIn; else WriteBit(memKey, powerIn);
                WriteBit(node.Address, pulse);
                return pulse;
            }

            case LadderElementKind.CoilEdgeNegative:
            {
                string memKey = string.IsNullOrWhiteSpace(node.Param1) ? $"__CN_{node.Id}" : node.Param1;
                bool prev = string.IsNullOrWhiteSpace(node.Param1) ? EdgeDbPrevState.GetValueOrDefault(memKey) : ReadBit(memKey);
                bool pulse = !powerIn && prev;
                if (string.IsNullOrWhiteSpace(node.Param1)) EdgeDbPrevState[memKey] = powerIn; else WriteBit(memKey, powerIn);
                WriteBit(node.Address, pulse);
                return pulse;
            }

            case LadderElementKind.PTrig:
            {
                bool prev = ReadBit(node.Address);
                WriteBit(node.Address, powerIn);
                return powerIn && !prev;
            }

            case LadderElementKind.NTrig:
            {
                bool prev = ReadBit(node.Address);
                WriteBit(node.Address, powerIn);
                return !powerIn && prev;
            }

            case LadderElementKind.RTrig:
            case LadderElementKind.FTrig:
            {
                string dbKey = PlcProgramModel.NormalizeAddress(node.Address);
                bool prev = EdgeDbPrevState.GetValueOrDefault(dbKey);
                EdgeDbPrevState[dbKey] = powerIn;
                bool q = node.Kind == LadderElementKind.RTrig ? (powerIn && !prev) : (!powerIn && prev);
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
                string dbKey = PlcProgramModel.NormalizeAddress(node.Address);
                if (powerIn && TimerDbs.TryGetValue(dbKey, out var st))
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = false;
                    st.PulseActive = false;
                }
                return powerIn;
            }

            case LadderElementKind.CoilPT:
            {
                string dbKey = PlcProgramModel.NormalizeAddress(node.Address);
                if (powerIn && PlcProgramCompiler.TryParseTimeSeconds(node.Param1, out double newPt))
                {
                    if (!TimerDbs.TryGetValue(dbKey, out var st))
                        TimerDbs[dbKey] = st = new PlcTimerDbState();
                    st.PresetOverrideSeconds = newPt;
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
            // NHÓM 4: COMPARATOR OPERATIONS (+ Variant)
            // =========================================================================
            case LadderElementKind.CmpEqual:
            case LadderElementKind.CmpNotEqual:
            case LadderElementKind.CmpGreaterOrEqual:
            case LadderElementKind.CmpLessOrEqual:
            case LadderElementKind.CmpGreater:
            case LadderElementKind.CmpLess:
            {
                double a = ReadNumber(node.Param1);
                double b = ReadNumber(node.Param2);
                bool cmp = node.Kind switch
                {
                    LadderElementKind.CmpEqual => Math.Abs(a - b) < 1e-6,
                    LadderElementKind.CmpNotEqual => Math.Abs(a - b) >= 1e-6,
                    LadderElementKind.CmpGreaterOrEqual => a >= b - 1e-6,
                    LadderElementKind.CmpLessOrEqual => a <= b + 1e-6,
                    LadderElementKind.CmpGreater => a > b + 1e-6,
                    LadderElementKind.CmpLess => a < b - 1e-6,
                    _ => false
                };
                LiveNodeStatusText[node.Id] = $"{FmtNum(a)} / {FmtNum(b)}";
                return powerIn && cmp;
            }

            case LadderElementKind.InRange:
            case LadderElementKind.OutRange:
            {
                double val = ReadNumber(node.Param1);
                double min = ReadNumber(node.Param2);
                double max = ReadNumber(node.ExtraParam);
                bool inside = val >= min - 1e-6 && val <= max + 1e-6;
                bool ok = node.Kind == LadderElementKind.InRange ? inside : !inside;
                LiveNodeStatusText[node.Id] = $"{FmtNum(val)} [{FmtNum(min)}..{FmtNum(max)}]";
                return powerIn && ok;
            }

            case LadderElementKind.CheckOk:
            case LadderElementKind.CheckNotOk:
            {
                double v = ReadNumber(node.Address);
                bool valid = !double.IsNaN(v) && !double.IsInfinity(v);
                return powerIn && (node.Kind == LadderElementKind.CheckOk ? valid : !valid);
            }

            case LadderElementKind.VarEqType:
            case LadderElementKind.VarNeType:
            case LadderElementKind.VarEqElemType:
            case LadderElementKind.VarNeElemType:
            {
                string t1 = InferAddressDataType(node.Param1);
                string t2 = InferAddressDataType(node.Param2);
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
                return powerIn && (node.Param1?.Contains('[') == true || InferAddressDataType(node.Param1) == "Word");

            case LadderElementKind.VarEqTypeOfDb:
            case LadderElementKind.VarNeTypeOfDb:
            {
                string dbKey = PlcProgramModel.NormalizeAddress(node.Param1);
                string dbType = TimerDbs.ContainsKey(dbKey) ? "IEC_Timer" : (CounterDbs.ContainsKey(dbKey) ? "IEC_Counter" : "DB");
                bool eq = string.Equals(dbType, node.Param2?.Trim(), StringComparison.OrdinalIgnoreCase);
                return powerIn && (node.Kind == LadderElementKind.VarEqTypeOfDb ? eq : !eq);
            }

            // =========================================================================
            // NHÓM 5: MATH FUNCTIONS
            // =========================================================================
            case LadderElementKind.Calculate:
            {
                double in1 = ReadNumber(node.Param1);
                double in2 = ReadNumber(node.Param2);
                bool eno = powerIn;
                if (powerIn)
                {
                    if (TryEvaluateExpression(node.ExtraParam, in1, in2, out double calcRes) && !double.IsNaN(calcRes) && !double.IsInfinity(calcRes))
                        WriteNumber(node.OutputAddress, calcRes);
                    else
                    {
                        LastErrorCode = 0x8200;
                        eno = false;
                    }
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return eno;
            }

            case LadderElementKind.MathAdd:
            case LadderElementKind.MathSub:
            case LadderElementKind.MathMul:
            case LadderElementKind.MathDiv:
            case LadderElementKind.MathMod:
            case LadderElementKind.MathMin:
            case LadderElementKind.MathMax:
            case LadderElementKind.MathExpt:
            {
                double a = ReadNumber(node.Param1);
                double b = ReadNumber(node.Param2);
                bool eno = powerIn;
                if (powerIn)
                {
                    if ((node.Kind is LadderElementKind.MathDiv or LadderElementKind.MathMod) && Math.Abs(b) < 1e-12)
                    {
                        LastErrorCode = 0x8201;
                        eno = false;
                    }
                    else
                    {
                        double res = node.Kind switch
                        {
                            LadderElementKind.MathAdd => a + b,
                            LadderElementKind.MathSub => a - b,
                            LadderElementKind.MathMul => a * b,
                            LadderElementKind.MathDiv => IsRealAddress(node.OutputAddress) ? (a / b) : Math.Truncate(a / b),
                            LadderElementKind.MathMod => (int)Math.Round(a) % (int)Math.Round(b),
                            LadderElementKind.MathMin => Math.Min(a, b),
                            LadderElementKind.MathMax => Math.Max(a, b),
                            LadderElementKind.MathExpt => Math.Pow(a, b),
                            _ => 0.0
                        };
                        WriteNumber(node.OutputAddress, res);
                    }
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return eno;
            }

            case LadderElementKind.MathLimit:
            {
                double val = ReadNumber(node.Param1);
                double mn = ReadNumber(node.Param2);
                double mx = ReadNumber(node.ExtraParam);
                if (powerIn) WriteNumber(node.OutputAddress, Math.Clamp(val, Math.Min(mn, mx), Math.Max(mn, mx)));
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
            }

            case LadderElementKind.MathInc:
            case LadderElementKind.MathDec:
            {
                if (powerIn)
                {
                    double cur = ReadNumber(node.OutputAddress);
                    WriteNumber(node.OutputAddress, node.Kind == LadderElementKind.MathInc ? cur + 1 : cur - 1);
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
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
                double x = ReadNumber(node.Param1);
                bool eno = powerIn;
                if (powerIn)
                {
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
                    if (double.IsNaN(res) || double.IsInfinity(res)) { LastErrorCode = 0x8202; eno = false; }
                    else WriteNumber(node.OutputAddress, res);
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return eno;
            }

            // =========================================================================
            // NHÓM 6: MOVE OPERATIONS (+ Variant, Array[*], Legacy)
            // =========================================================================
            case LadderElementKind.Move:
            case LadderElementKind.VariantGet:
            case LadderElementKind.VariantPut:
            {
                double v = ReadNumber(node.Param1);
                if (powerIn) WriteNumber(node.OutputAddress, v);
                LiveNodeStatusText[node.Id] = $"{FmtNum(v)} → {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
            }

            case LadderElementKind.MoveBlk:
            case LadderElementKind.MoveBlkVariant:
            case LadderElementKind.UMoveBlk:
            case LadderElementKind.Serialize:
            case LadderElementKind.Deserialize:
            {
                int count = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (powerIn && TryParseWordOffset(node.Param1, out string srcArea, out int srcOff) &&
                    TryParseWordOffset(node.OutputAddress, out string dstArea, out int dstOff))
                {
                    int step = srcArea == "MD" ? 4 : 2;
                    for (int k = 0; k < count; k++)
                    {
                        double v = ReadNumber($"%{srcArea}{srcOff + k * step}");
                        WriteNumber($"%{dstArea}{dstOff + k * step}", v);
                    }
                }
                LiveNodeStatusText[node.Id] = $"BLK×{count}";
                return powerIn;
            }

            case LadderElementKind.FillBlk:
            case LadderElementKind.UFillBlk:
            {
                double fillVal = ReadNumber(node.Param1);
                int count = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (powerIn && TryParseWordOffset(node.OutputAddress, out string dstArea, out int dstOff))
                {
                    int step = dstArea == "MD" ? 4 : 2;
                    for (int k = 0; k < count; k++)
                        WriteNumber($"%{dstArea}{dstOff + k * step}", fillVal);
                }
                LiveNodeStatusText[node.Id] = $"{FmtNum(fillVal)}×{count}";
                return powerIn;
            }

            case LadderElementKind.Scatter:
            case LadderElementKind.ScatterBlk:
            {
                uint wordVal = (uint)(int)Math.Round(ReadNumber(node.Param1));
                int bits = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (powerIn && TryParseBitOffset(PlcProgramModel.NormalizeAddress(node.OutputAddress), out char bArea, out int bStart))
                {
                    for (int k = 0; k < bits; k++)
                        WriteBitLinear(bArea, bStart + k, ((wordVal >> k) & 1u) != 0);
                }
                LiveNodeStatusText[node.Id] = $"0x{wordVal:X}→{bits}b";
                return powerIn;
            }

            case LadderElementKind.Gather:
            case LadderElementKind.GatherBlk:
            {
                int bits = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 1, 32);
                if (powerIn && TryParseBitOffset(PlcProgramModel.NormalizeAddress(node.Param1), out char bArea, out int bStart))
                {
                    uint acc = 0;
                    for (int k = 0; k < bits; k++)
                    {
                        if (ReadBitLinear(bArea, bStart + k)) acc |= (1u << k);
                    }
                    WriteNumber(node.OutputAddress, (int)acc);
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
            }

            case LadderElementKind.Swap:
            {
                ushort v = (ushort)(int)Math.Round(ReadNumber(node.Param1));
                ushort swapped = (ushort)((v >> 8) | (v << 8));
                if (powerIn) WriteNumber(node.OutputAddress, swapped);
                LiveNodeStatusText[node.Id] = $"0x{swapped:X4}";
                return powerIn;
            }

            case LadderElementKind.CountOfElements:
                if (powerIn) WriteNumber(node.OutputAddress, 8);
                return powerIn;

            case LadderElementKind.LowerBound:
                if (powerIn) WriteNumber(node.OutputAddress, 0);
                return powerIn;

            case LadderElementKind.UpperBound:
                if (powerIn) WriteNumber(node.OutputAddress, 7);
                return powerIn;

            case LadderElementKind.FieldRead:
            {
                int idx = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 31);
                if (powerIn && TryParseWordOffset(node.Param1, out string bArea, out int bOff))
                {
                    int step = bArea == "MD" ? 4 : 2;
                    WriteNumber(node.OutputAddress, ReadNumber($"%{bArea}{bOff + idx * step}"));
                }
                LiveNodeStatusText[node.Id] = $"[{idx}]={FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
            }

            case LadderElementKind.FieldWrite:
            {
                double val = ReadNumber(node.Param1);
                int idx = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 31);
                if (powerIn && TryParseWordOffset(node.OutputAddress, out string bArea, out int bOff))
                {
                    int step = bArea == "MD" ? 4 : 2;
                    WriteNumber($"%{bArea}{bOff + idx * step}", val);
                }
                LiveNodeStatusText[node.Id] = $"[{idx}]←{FmtNum(val)}";
                return powerIn;
            }

            // =========================================================================
            // NHÓM 7: CONVERSION OPERATIONS (+ Analog NORM_X / SCALE_X)
            // =========================================================================
            case LadderElementKind.Convert:
            {
                double v = ReadNumber(node.Param1);
                if (powerIn) WriteNumber(node.OutputAddress, v);
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
            }

            case LadderElementKind.Round:
                if (powerIn) WriteNumber(node.OutputAddress, Math.Round(ReadNumber(node.Param1), MidpointRounding.ToEven));
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;

            case LadderElementKind.Ceil:
                if (powerIn) WriteNumber(node.OutputAddress, Math.Ceiling(ReadNumber(node.Param1)));
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;

            case LadderElementKind.Floor:
                if (powerIn) WriteNumber(node.OutputAddress, Math.Floor(ReadNumber(node.Param1)));
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;

            case LadderElementKind.Trunc:
                if (powerIn) WriteNumber(node.OutputAddress, Math.Truncate(ReadNumber(node.Param1)));
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;

            case LadderElementKind.NormX:
            {
                double val = ReadNumber(node.Param1);
                double min = ReadNumber(node.Param2);
                double max = ReadNumber(node.ExtraParam);
                bool eno = powerIn && Math.Abs(max - min) > 1e-9;
                if (eno)
                {
                    double norm = (val - min) / (max - min);
                    WriteNumber(node.OutputAddress, norm);
                }
                LiveNodeStatusText[node.Id] = $"= {ReadNumber(node.OutputAddress):0.000}";
                return eno;
            }

            case LadderElementKind.ScaleX:
            {
                double val = ReadNumber(node.Param1);
                double min = ReadNumber(node.Param2);
                double max = ReadNumber(node.ExtraParam);
                if (powerIn)
                {
                    double scaled = min + val * (max - min);
                    WriteNumber(node.OutputAddress, scaled);
                }
                LiveNodeStatusText[node.Id] = $"= {FmtNum(ReadNumber(node.OutputAddress))}";
                return powerIn;
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
                if (powerIn)
                {
                    PasswordProtected = ReadNumber(node.Param1) != 0;
                    WriteNumber(node.OutputAddress, 0);
                }
                return powerIn;

            case LadderElementKind.ReTrigr:
                return powerIn;

            case LadderElementKind.StopCpu:
                if (powerIn) CpuStopRequested = true;
                return powerIn;

            case LadderElementKind.GetError:
            case LadderElementKind.GetErrId:
                if (powerIn) WriteNumber(node.OutputAddress, LastErrorCode);
                LiveNodeStatusText[node.Id] = $"ERR={LastErrorCode}";
                return powerIn;

            case LadderElementKind.RuntimeMeasure:
                if (powerIn) WriteNumber(node.OutputAddress, LastCycleSeconds);
                LiveNodeStatusText[node.Id] = $"{LastCycleSeconds * 1000.0:0}ms";
                return powerIn;

            // =========================================================================
            // NHÓM 9: WORD LOGIC OPERATIONS
            // =========================================================================
            case LadderElementKind.WordAnd:
            case LadderElementKind.WordOr:
            case LadderElementKind.WordXor:
            {
                uint a = (uint)(int)Math.Round(ReadNumber(node.Param1));
                uint b = (uint)(int)Math.Round(ReadNumber(node.Param2));
                if (powerIn)
                {
                    uint r = node.Kind switch
                    {
                        LadderElementKind.WordAnd => a & b,
                        LadderElementKind.WordOr => a | b,
                        _ => a ^ b
                    };
                    WriteNumber(node.OutputAddress, (int)(r & 0xFFFF));
                }
                LiveNodeStatusText[node.Id] = $"0x{(int)Math.Round(ReadNumber(node.OutputAddress)) & 0xFFFF:X4}";
                return powerIn;
            }

            case LadderElementKind.WordInvert:
            {
                ushort a = (ushort)(int)Math.Round(ReadNumber(node.Param1));
                if (powerIn) WriteNumber(node.OutputAddress, (ushort)~a);
                LiveNodeStatusText[node.Id] = $"0x{(int)Math.Round(ReadNumber(node.OutputAddress)) & 0xFFFF:X4}";
                return powerIn;
            }

            case LadderElementKind.WordDeco:
            {
                int n = Math.Clamp((int)Math.Round(ReadNumber(node.Param1)), 0, 15);
                if (powerIn) WriteNumber(node.OutputAddress, 1 << n);
                LiveNodeStatusText[node.Id] = $"Bit {n}";
                return powerIn;
            }

            case LadderElementKind.WordEnco:
            {
                ushort v = (ushort)(int)Math.Round(ReadNumber(node.Param1));
                int pos = 0;
                if (v != 0)
                {
                    while (((v >> pos) & 1) == 0 && pos < 15) pos++;
                }
                if (powerIn) WriteNumber(node.OutputAddress, pos);
                LiveNodeStatusText[node.Id] = $"Pos={pos}";
                return powerIn;
            }

            case LadderElementKind.WordSel:
            {
                bool g = ReadBit(node.Address);
                double res = g ? ReadNumber(node.Param2) : ReadNumber(node.Param1);
                if (powerIn) WriteNumber(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"= {FmtNum(res)}";
                return powerIn;
            }

            case LadderElementKind.WordMux:
            {
                int k = (int)Math.Round(ReadNumber(node.Address));
                double res = k switch { 0 => ReadNumber(node.Param1), 1 => ReadNumber(node.Param2), _ => ReadNumber(node.ExtraParam) };
                if (powerIn) WriteNumber(node.OutputAddress, res);
                LiveNodeStatusText[node.Id] = $"K={k}→{FmtNum(res)}";
                return powerIn;
            }

            case LadderElementKind.WordDemux:
            {
                int k = (int)Math.Round(ReadNumber(node.Address));
                double v = ReadNumber(node.Param1);
                if (powerIn)
                {
                    string dst = k switch { 0 => node.Param2, 1 => node.ExtraParam, _ => node.OutputAddress };
                    if (!string.IsNullOrWhiteSpace(dst)) WriteNumber(dst, v);
                }
                LiveNodeStatusText[node.Id] = $"K={k}";
                return powerIn;
            }

            // =========================================================================
            // NHÓM 10: SHIFT AND ROTATE (16-bit Word)
            // =========================================================================
            case LadderElementKind.ShiftRight:
            case LadderElementKind.ShiftLeft:
            case LadderElementKind.RotateRight:
            case LadderElementKind.RotateLeft:
            {
                ushort val = (ushort)(int)Math.Round(ReadNumber(node.Param1));
                int n = Math.Clamp((int)Math.Round(ReadNumber(node.Param2)), 0, 16);
                if (powerIn)
                {
                    int rotN = n % 16;
                    ushort res = node.Kind switch
                    {
                        LadderElementKind.ShiftRight => (ushort)(val >> n),
                        LadderElementKind.ShiftLeft => (ushort)((val << n) & 0xFFFF),
                        LadderElementKind.RotateRight => (ushort)((val >> rotN) | ((val << (16 - rotN)) & 0xFFFF)),
                        LadderElementKind.RotateLeft => (ushort)(((val << rotN) & 0xFFFF) | (val >> (16 - rotN))),
                        _ => val
                    };
                    WriteNumber(node.OutputAddress, res);
                }
                LiveNodeStatusText[node.Id] = $"0x{(int)Math.Round(ReadNumber(node.OutputAddress)) & 0xFFFF:X4}";
                return powerIn;
            }

            default:
                return powerIn;
        }
    }

    private bool EvaluateTimer(LadderNode node, bool powerIn, double dt)
    {
        string dbKey = PlcProgramModel.NormalizeAddress(node.Address);
        if (!TimerDbs.TryGetValue(dbKey, out var st))
            TimerDbs[dbKey] = st = new PlcTimerDbState();

        PlcProgramCompiler.TryParseTimeSeconds(node.Param1, out double ptSec);
        if (st.PresetOverrideSeconds >= 0) ptSec = st.PresetOverrideSeconds;

        switch (node.Kind)
        {
            case LadderElementKind.TimerTON:
            case LadderElementKind.CoilTON:
                if (powerIn)
                {
                    st.ElapsedSeconds = Math.Min(ptSec, st.ElapsedSeconds + dt);
                    st.Q = st.ElapsedSeconds >= ptSec - 1e-6;
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
                }
                else if (st.Q)
                {
                    st.ElapsedSeconds = Math.Min(ptSec, st.ElapsedSeconds + dt);
                    if (st.ElapsedSeconds >= ptSec - 1e-6) st.Q = false;
                }
                break;

            case LadderElementKind.TimerTP:
            case LadderElementKind.CoilTP:
                if (powerIn && !st.PrevIn && !st.PulseActive)
                {
                    st.PulseActive = true;
                    st.ElapsedSeconds = 0.0;
                    st.Q = true;
                }
                if (st.PulseActive)
                {
                    st.ElapsedSeconds = Math.Min(ptSec, st.ElapsedSeconds + dt);
                    if (st.ElapsedSeconds >= ptSec - 1e-6)
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
                bool resetBit = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                if (resetBit)
                {
                    st.ElapsedSeconds = 0.0;
                    st.Q = false;
                }
                else if (powerIn)
                {
                    st.ElapsedSeconds = Math.Min(ptSec, st.ElapsedSeconds + dt);
                    st.Q = st.ElapsedSeconds >= ptSec - 1e-6;
                }
                break;
        }

        st.PrevIn = powerIn;
        LiveNodeStatusText[node.Id] = FormatElapsedTime(st.ElapsedSeconds);
        return st.Q;
    }

    private bool EvaluateCounter(LadderNode node, bool powerIn)
    {
        string dbKey = PlcProgramModel.NormalizeAddress(node.Address);
        if (!CounterDbs.TryGetValue(dbKey, out var st))
            CounterDbs[dbKey] = st = new PlcCounterDbState();

        int pv = (int)Math.Round(ReadNumber(node.Param1));

        switch (node.Kind)
        {
            case LadderElementKind.CounterCTU:
            {
                bool r = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                if (r) st.Cv = 0;
                else if (powerIn && !st.PrevCu) st.Cv++;
                st.PrevCu = powerIn;
                st.Qu = st.Cv >= pv;
                st.Qd = st.Cv <= 0;
                LiveNodeStatusText[node.Id] = $"CV: {st.Cv}";
                return st.Qu;
            }
            case LadderElementKind.CounterCTD:
            {
                bool ld = !string.IsNullOrWhiteSpace(node.Param2) && ReadBit(node.Param2);
                if (ld) st.Cv = pv;
                else if (powerIn && !st.PrevCd) st.Cv--;
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
                if (r) st.Cv = 0;
                else if (ld) st.Cv = pv;
                else
                {
                    if (powerIn && !st.PrevCu) st.Cv++;
                    if (cd && !st.PrevCd) st.Cv--;
                }
                st.PrevCu = powerIn;
                st.PrevCd = cd;
                st.Qu = st.Cv >= pv;
                st.Qd = st.Cv <= 0;
                LiveNodeStatusText[node.Id] = $"CV: {st.Cv}";
                return st.Qu;
            }
        }
        return false;
    }

    // --- Bộ phân tích biểu thức số học đệ quy cho lệnh CALCULATE ---
    private static bool TryEvaluateExpression(string? expr, double in1, double in2, out double result)
    {
        result = 0.0;
        if (string.IsNullOrWhiteSpace(expr)) return false;
        string s = expr.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        int pos = 0;
        if (!ParseAddSub(s, ref pos, in1, in2, out result) || pos != s.Length)
            return false;
        return true;
    }

    private static bool ParseAddSub(string s, ref int pos, double in1, double in2, out double val)
    {
        if (!ParseMulDiv(s, ref pos, in1, in2, out val)) return false;
        while (pos < s.Length && (s[pos] is '+' or '-'))
        {
            char op = s[pos++];
            if (!ParseMulDiv(s, ref pos, in1, in2, out double rhs)) return false;
            val = op == '+' ? val + rhs : val - rhs;
        }
        return true;
    }

    private static bool ParseMulDiv(string s, ref int pos, double in1, double in2, out double val)
    {
        if (!ParseFactor(s, ref pos, in1, in2, out val)) return false;
        while (pos < s.Length && (s[pos] is '*' or '/'))
        {
            char op = s[pos++];
            if (!ParseFactor(s, ref pos, in1, in2, out double rhs)) return false;
            if (op == '/' && Math.Abs(rhs) < 1e-12) return false;
            val = op == '*' ? val * rhs : val / rhs;
        }
        return true;
    }

    private static bool ParseFactor(string s, ref int pos, double in1, double in2, out double val)
    {
        val = 0.0;
        if (pos >= s.Length) return false;
        if (s[pos] == '-')
        {
            pos++;
            if (!ParseFactor(s, ref pos, in1, in2, out val)) return false;
            val = -val;
            return true;
        }
        if (s[pos] == '(')
        {
            pos++;
            if (!ParseAddSub(s, ref pos, in1, in2, out val)) return false;
            if (pos >= s.Length || s[pos] != ')') return false;
            pos++;
            return true;
        }
        if (s[pos..].StartsWith("IN1", StringComparison.Ordinal)) { pos += 3; val = in1; return true; }
        if (s[pos..].StartsWith("IN2", StringComparison.Ordinal)) { pos += 3; val = in2; return true; }

        int start = pos;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
        return pos > start && double.TryParse(s[start..pos], NumberStyles.Float, CultureInfo.InvariantCulture, out val);
    }

    // --- Đọc / Ghi Bộ nhớ Bit, Word (%MW/%IW/%QW) & Real (%MD) ---
    public bool ReadBit(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        string s = PlcProgramModel.NormalizeAddress(address);

        if (s.StartsWith("%DB"))
        {
            int dot = s.IndexOf('.');
            if (dot > 3)
            {
                string dbKey = s[..dot];
                string field = s[(dot + 1)..];
                if (field == "Q" && TimerDbs.TryGetValue(dbKey, out var t)) return t.Q;
                if (field is "Q" or "QU" && CounterDbs.TryGetValue(dbKey, out var cu)) return cu.Qu;
                if (field == "QD" && CounterDbs.TryGetValue(dbKey, out var cd)) return cd.Qd;
            }
            return false;
        }

        if (!TryParseBitOffset(s, out char area, out int linearBit)) return false;
        return ReadBitLinear(area, linearBit);
    }

    public void WriteBit(string? address, bool value)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        string s = PlcProgramModel.NormalizeAddress(address);
        if (!TryParseBitOffset(s, out char area, out int linearBit)) return;
        WriteBitLinear(area, linearBit, value);
    }

    private bool ReadBitLinear(char area, int linearBit) => area switch
    {
        'I' => linearBit >= 0 && linearBit < Di.Length && Di[linearBit],
        'Q' => linearBit >= 0 && linearBit < Dq.Length && Dq[linearBit],
        'M' => linearBit >= 0 && linearBit < MemoryBits.Length && MemoryBits[linearBit],
        _ => false
    };

    private void WriteBitLinear(char area, int linearBit, bool value)
    {
        if (area == 'Q' && linearBit >= 0 && linearBit < Dq.Length) Dq[linearBit] = value;
        else if (area == 'M' && linearBit >= 0 && linearBit < MemoryBits.Length) MemoryBits[linearBit] = value;
    }

    public int ReadWordOrConstant(string? raw) => (int)Math.Round(ReadNumber(raw));

    public double ReadNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0.0;
        string s = raw.Trim().ToUpperInvariant();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double lit))
            return lit;

        s = PlcProgramModel.NormalizeAddress(s);
        if (s.StartsWith("%DB"))
        {
            int dot = s.IndexOf('.');
            if (dot > 3)
            {
                string dbKey = s[..dot];
                string field = s[(dot + 1)..];
                if (field == "CV" && CounterDbs.TryGetValue(dbKey, out var c)) return c.Cv;
                if (field == "ET" && TimerDbs.TryGetValue(dbKey, out var t)) return Math.Round(t.ElapsedSeconds * 1000.0);
            }
            return 0.0;
        }

        if (s.StartsWith("%IW") && int.TryParse(s[3..], out int iwOff))
            return iwOff == 64 ? Ai[0] : (iwOff == 66 ? Ai[1] : 0);
        if (s.StartsWith("%MW") && int.TryParse(s[3..], out int mwOff))
            return MemoryWords.GetValueOrDefault(mwOff, 0);
        if (s.StartsWith("%MD") && int.TryParse(s[3..], out int mdOff))
            return MemoryReals.GetValueOrDefault(mdOff, 0.0);
        if (s.StartsWith("%QW") && int.TryParse(s[3..], out int qwOff))
            return OutputWords.GetValueOrDefault(qwOff, 0);

        return 0.0;
    }

    public void WriteWord(string? raw, int value) => WriteNumber(raw, value);

    public void WriteNumber(string? raw, double value)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string s = PlcProgramModel.NormalizeAddress(raw);
        if (s.StartsWith("%MW") && int.TryParse(s[3..], out int mwOff))
            MemoryWords[mwOff] = (int)Math.Round(value);
        else if (s.StartsWith("%MD") && int.TryParse(s[3..], out int mdOff))
            MemoryReals[mdOff] = value;
        else if (s.StartsWith("%QW") && int.TryParse(s[3..], out int qwOff))
            OutputWords[qwOff] = (int)Math.Round(value);
    }

    public string FormatLiveValueForAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "-";
        string s = PlcProgramModel.NormalizeAddress(address);
        if (s.StartsWith("%DB") && !s.Contains('.'))
        {
            if (TimerDbs.TryGetValue(s, out var t)) return $"Q={(t.Q ? "1" : "0")}, ET={FormatElapsedTime(t.ElapsedSeconds)}";
            if (CounterDbs.TryGetValue(s, out var c)) return $"CV={c.Cv}, Q={(c.Qu ? "1" : "0")}";
            return "Idle";
        }
        if (s.StartsWith("%MD")) return FmtNum(ReadNumber(s));
        if (s.StartsWith("%MW") || s.StartsWith("%IW") || s.StartsWith("%QW"))
            return ReadWordOrConstant(s).ToString(CultureInfo.InvariantCulture);

        return ReadBit(s) ? "TRUE (1)" : "FALSE (0)";
    }

    private static bool IsRealAddress(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && PlcProgramModel.NormalizeAddress(raw).StartsWith("%MD");

    private static string InferAddressDataType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Null";
        string s = PlcProgramModel.NormalizeAddress(raw);
        if (s.StartsWith("%MD")) return "Real";
        if (s.StartsWith("%MW") || s.StartsWith("%IW") || s.StartsWith("%QW")) return "Word";
        if (s.StartsWith("%DB")) return "DB";
        if (s.StartsWith("%I") || s.StartsWith("%Q") || s.StartsWith("%M")) return "Bool";
        return raw.Contains('.') ? "Real" : "Int";
    }

    private static bool TryParseWordOffset(string? raw, out string area, out int offset)
    {
        area = string.Empty;
        offset = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = PlcProgramModel.NormalizeAddress(raw);
        if (s.Length < 4 || s[0] != '%') return false;
        area = s[1..3];
        return (area is "MW" or "MD" or "IW" or "QW") && int.TryParse(s[3..], out offset);
    }

    private static bool TryParseBitOffset(string norm, out char area, out int linearBit)
    {
        area = '\0';
        linearBit = -1;
        if (norm.Length < 4 || norm[0] != '%') return false;
        area = norm[1];
        int dot = norm.IndexOf('.');
        if (dot <= 2 || dot >= norm.Length - 1) return false;
        if (!int.TryParse(norm[2..dot], out int byteIdx)) return false;
        if (!int.TryParse(norm[(dot + 1)..], out int bitIdx) || bitIdx < 0 || bitIdx > 7) return false;
        linearBit = byteIdx * 8 + bitIdx;
        return true;
    }

    public static string FormatElapsedTime(double sec) =>
        sec >= 1.0 ? $"T#{sec:0.0}s" : $"T#{Math.Round(sec * 1000.0):0}ms";

    private static string FmtNum(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-5
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);
}
