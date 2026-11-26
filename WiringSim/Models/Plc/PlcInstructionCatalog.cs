using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Models.Plc;

public sealed record PlcInstructionMeta(
    LadderElementKind Kind,
    int GroupIndex,
    string GroupTitleKey,
    string? SubGroupName,
    string Symbol,
    string BoxHeader,
    string DescKey,
    string AccentHex,
    bool IsInlineGlyph,
    bool IsOutputOrAction,
    bool UsesDbBlock,
    string? AddrLabel = null,
    string? Param1Label = null,
    string? Param2Label = null,
    string? ExtraLabel = null,
    string? OutLabel = null);

/// <summary>
/// Danh mục tập trung toàn bộ 10 nhóm hàm/lệnh PLC chuẩn TIA Portal (~105 lệnh):
/// - Giữ nguyên 100% ký hiệu/tên hàm chuyên ngành (TON, TONR, SET_BF, NORM_X, SCALE_X, CALCULATE, SHR, ROL...).
/// - Bản địa hóa 100% tên nhóm và mô tả chú thích qua LocalizationManager.
/// </summary>
public static class PlcInstructionCatalog
{
    public static readonly IReadOnlyList<(int GroupIndex, string TitleKey, string AccentHex)> Groups =
    [
        (1, "Str.Plc.Cat1", "#38BDF8"),
        (2, "Str.Plc.Cat2", "#F59E0B"),
        (3, "Str.Plc.Cat3", "#14B8A6"),
        (4, "Str.Plc.Cat4", "#60A5FA"),
        (5, "Str.Plc.Cat5", "#EC4899"),
        (6, "Str.Plc.Cat6", "#A855F7"),
        (7, "Str.Plc.Cat7", "#06B6D4"),
        (8, "Str.Plc.Cat8", "#F97316"),
        (9, "Str.Plc.Cat9", "#8B5CF6"),
        (10, "Str.Plc.Cat10", "#10B981"),
        (11, "Str.Plc.Cat11", "#0EA5E9"),
        (12, "Str.Plc.Cat12", "#F43F5E"),
        (13, "Str.Plc.Cat13", "#EAB308")
    ];

    private static readonly List<PlcInstructionMeta> _all = new();
    private static readonly Dictionary<LadderElementKind, PlcInstructionMeta> _byKind = new();

    static PlcInstructionCatalog()
    {
        // =========================================================================
        // NHÓM 1: Bit logic operations
        // =========================================================================
        Add(LadderElementKind.ContactNO, 1, null, "--||--", "|   |", "Str.Plc.D.NO", "#38BDF8", true, false, false, "Addr");
        Add(LadderElementKind.ContactNC, 1, null, "--|/|--", "| / |", "Str.Plc.D.NC", "#38BDF8", true, false, false, "Addr");
        Add(LadderElementKind.InvertRLO, 1, null, "--|NOT|--", "|NOT|", "Str.Plc.D.NOT", "#38BDF8", true, false, false);
        Add(LadderElementKind.CoilOut, 1, null, "--( )--", "(   )", "Str.Plc.D.OUT", "#22C55E", true, true, false, "Addr");
        Add(LadderElementKind.CoilNegate, 1, null, "--(/)--", "( / )", "Str.Plc.D.COILNEG", "#22C55E", true, true, false, "Addr");
        Add(LadderElementKind.CoilReset, 1, null, "--(R)", "( R )", "Str.Plc.D.RESET", "#EF4444", true, true, false, "Addr");
        Add(LadderElementKind.CoilSet, 1, null, "--(S)", "( S )", "Str.Plc.D.SET", "#22C55E", true, true, false, "Addr");
        Add(LadderElementKind.SetBitField, 1, null, "SET_BF", "SET_BF", "Str.Plc.D.SET_BF", "#22C55E", false, true, false, "Start", "N");
        Add(LadderElementKind.ResetBitField, 1, null, "RESET_BF", "RESET_BF", "Str.Plc.D.RESET_BF", "#EF4444", false, true, false, "Start", "N");
        Add(LadderElementKind.FlipFlopSR, 1, null, "SR", "SR", "Str.Plc.D.SR", "#38BDF8", false, true, false, "Q", "R1");
        Add(LadderElementKind.FlipFlopRS, 1, null, "RS", "RS", "Str.Plc.D.RS", "#38BDF8", false, true, false, "Q", "S1");
        Add(LadderElementKind.EdgePositive, 1, null, "--|P|--", "| P |", "Str.Plc.D.P", "#A855F7", true, false, false, "Addr", "M-Bit");
        Add(LadderElementKind.EdgeNegative, 1, null, "--|N|--", "| N |", "Str.Plc.D.N", "#A855F7", true, false, false, "Addr", "M-Bit");
        Add(LadderElementKind.CoilEdgePositive, 1, null, "--(P)--", "( P )", "Str.Plc.D.COILP", "#A855F7", true, true, false, "Addr", "M-Bit");
        Add(LadderElementKind.CoilEdgeNegative, 1, null, "--(N)--", "( N )", "Str.Plc.D.COILN", "#A855F7", true, true, false, "Addr", "M-Bit");
        Add(LadderElementKind.PTrig, 1, null, "P_TRIG", "P_TRIG", "Str.Plc.D.P_TRIG", "#A855F7", false, false, false, "M-Bit");
        Add(LadderElementKind.NTrig, 1, null, "N_TRIG", "N_TRIG", "Str.Plc.D.N_TRIG", "#A855F7", false, false, false, "M-Bit");
        Add(LadderElementKind.RTrig, 1, null, "R_TRIG", "R_TRIG", "Str.Plc.D.R_TRIG", "#A855F7", false, false, true, "DB");
        Add(LadderElementKind.FTrig, 1, null, "F_TRIG", "F_TRIG", "Str.Plc.D.F_TRIG", "#A855F7", false, false, true, "DB");

        // =========================================================================
        // NHÓM 2: Timer operations
        // =========================================================================
        Add(LadderElementKind.TimerTP, 2, null, "TP", "TP", "Str.Plc.D.TP", "#F59E0B", false, true, true, "DB", "PT");
        Add(LadderElementKind.TimerTON, 2, null, "TON", "TON", "Str.Plc.D.TON", "#F59E0B", false, true, true, "DB", "PT");
        Add(LadderElementKind.TimerTOF, 2, null, "TOF", "TOF", "Str.Plc.D.TOF", "#F59E0B", false, true, true, "DB", "PT");
        Add(LadderElementKind.TimerTONR, 2, null, "TONR", "TONR", "Str.Plc.D.TONR", "#F59E0B", false, true, true, "DB", "PT", "R");
        Add(LadderElementKind.CoilTP, 2, null, "--(TP)--", "(TP)", "Str.Plc.D.COIL_TP", "#F59E0B", true, true, true, "DB", "PT");
        Add(LadderElementKind.CoilTON, 2, null, "--(TON)--", "(TON)", "Str.Plc.D.COIL_TON", "#F59E0B", true, true, true, "DB", "PT");
        Add(LadderElementKind.CoilTOF, 2, null, "--(TOF)--", "(TOF)", "Str.Plc.D.COIL_TOF", "#F59E0B", true, true, true, "DB", "PT");
        Add(LadderElementKind.CoilTONR, 2, null, "--(TONR)--", "(TONR)", "Str.Plc.D.COIL_TONR", "#F59E0B", true, true, true, "DB", "PT");
        Add(LadderElementKind.CoilRT, 2, null, "--(RT)--", "(RT)", "Str.Plc.D.COIL_RT", "#EF4444", true, true, false, "DB");
        Add(LadderElementKind.CoilPT, 2, null, "--(PT)--", "(PT)", "Str.Plc.D.COIL_PT", "#F59E0B", true, true, false, "DB", "PT");

        // =========================================================================
        // NHÓM 3: Counter operations
        // =========================================================================
        Add(LadderElementKind.CounterCTU, 3, null, "CTU", "CTU", "Str.Plc.D.CTU", "#14B8A6", false, true, true, "DB", "PV", "R");
        Add(LadderElementKind.CounterCTD, 3, null, "CTD", "CTD", "Str.Plc.D.CTD", "#14B8A6", false, true, true, "DB", "PV", "LD");
        Add(LadderElementKind.CounterCTUD, 3, null, "CTUD", "CTUD", "Str.Plc.D.CTUD", "#14B8A6", false, true, true, "DB", "PV", "R", "LD", "CD");

        // =========================================================================
        // NHÓM 4: Comparator operations (+ Variant)
        // =========================================================================
        Add(LadderElementKind.CmpEqual, 4, null, "CMP ==", "CMP ==", "Str.Plc.D.EQ", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.CmpNotEqual, 4, null, "CMP <>", "CMP <>", "Str.Plc.D.NE", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.CmpGreaterOrEqual, 4, null, "CMP >=", "CMP >=", "Str.Plc.D.GE", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.CmpLessOrEqual, 4, null, "CMP <=", "CMP <=", "Str.Plc.D.LE", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.CmpGreater, 4, null, "CMP >", "CMP >", "Str.Plc.D.GT", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.CmpLess, 4, null, "CMP <", "CMP <", "Str.Plc.D.LT", "#60A5FA", false, false, false, null, "IN1", "IN2");
        Add(LadderElementKind.InRange, 4, null, "IN_Range", "IN_Range", "Str.Plc.D.IN_RANGE", "#60A5FA", false, false, false, null, "VAL", "MIN", "MAX");
        Add(LadderElementKind.OutRange, 4, null, "OUT_Range", "OUT_Range", "Str.Plc.D.OUT_RANGE", "#60A5FA", false, false, false, null, "VAL", "MIN", "MAX");
        Add(LadderElementKind.CheckOk, 4, null, "--|OK|--", "| OK |", "Str.Plc.D.OK", "#22C55E", true, false, false, "Addr");
        Add(LadderElementKind.CheckNotOk, 4, null, "--|NOT_OK|--", "|!OK|", "Str.Plc.D.NOT_OK", "#EF4444", true, false, false, "Addr");
        Add(LadderElementKind.VarEqType, 4, "Variant", "EQ_Type", "EQ_Type", "Str.Plc.D.EQ_TYPE", "#38BDF8", false, false, false, null, "SRC1", "SRC2");
        Add(LadderElementKind.VarNeType, 4, "Variant", "NE_Type", "NE_Type", "Str.Plc.D.NE_TYPE", "#38BDF8", false, false, false, null, "SRC1", "SRC2");
        Add(LadderElementKind.VarEqElemType, 4, "Variant", "EQ_ElemType", "EQ_ElemType", "Str.Plc.D.EQ_ELEM", "#38BDF8", false, false, false, null, "SRC1", "SRC2");
        Add(LadderElementKind.VarNeElemType, 4, "Variant", "NE_ElemType", "NE_ElemType", "Str.Plc.D.NE_ELEM", "#38BDF8", false, false, false, null, "SRC1", "SRC2");
        Add(LadderElementKind.VarIsNull, 4, "Variant", "IS_NULL", "IS_NULL", "Str.Plc.D.IS_NULL", "#38BDF8", false, false, false, null, "IN");
        Add(LadderElementKind.VarNotNull, 4, "Variant", "NOT_NULL", "NOT_NULL", "Str.Plc.D.NOT_NULL", "#38BDF8", false, false, false, null, "IN");
        Add(LadderElementKind.VarIsArray, 4, "Variant", "IS_ARRAY", "IS_ARRAY", "Str.Plc.D.IS_ARRAY", "#38BDF8", false, false, false, null, "IN");
        Add(LadderElementKind.VarEqTypeOfDb, 4, "Variant", "EQ_TypeOfDB", "EQ_TypeOfDB", "Str.Plc.D.EQ_DB", "#38BDF8", false, false, false, null, "DB", "TYPE");
        Add(LadderElementKind.VarNeTypeOfDb, 4, "Variant", "NE_TypeOfDB", "NE_TypeOfDB", "Str.Plc.D.NE_DB", "#38BDF8", false, false, false, null, "DB", "TYPE");

        // =========================================================================
        // NHÓM 5: Math functions
        // =========================================================================
        Add(LadderElementKind.Calculate, 5, null, "CALCULATE", "CALCULATE", "Str.Plc.D.CALC", "#EC4899", false, true, false, null, "IN1", "IN2", "Expr", "OUT");
        Add(LadderElementKind.MathAdd, 5, null, "ADD", "ADD", "Str.Plc.D.ADD", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathSub, 5, null, "SUB", "SUB", "Str.Plc.D.SUB", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathMul, 5, null, "MUL", "MUL", "Str.Plc.D.MUL", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathDiv, 5, null, "DIV", "DIV", "Str.Plc.D.DIV", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathMod, 5, null, "MOD", "MOD", "Str.Plc.D.MOD", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathNeg, 5, null, "NEG", "NEG", "Str.Plc.D.NEG", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathInc, 5, null, "INC", "INC", "Str.Plc.D.INC", "#EC4899", false, true, false, null, null, null, null, "IN/OUT");
        Add(LadderElementKind.MathDec, 5, null, "DEC", "DEC", "Str.Plc.D.DEC", "#EC4899", false, true, false, null, null, null, null, "IN/OUT");
        Add(LadderElementKind.MathAbs, 5, null, "ABS", "ABS", "Str.Plc.D.ABS", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathMin, 5, null, "MIN", "MIN", "Str.Plc.D.MIN", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathMax, 5, null, "MAX", "MAX", "Str.Plc.D.MAX", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathLimit, 5, null, "LIMIT", "LIMIT", "Str.Plc.D.LIMIT", "#EC4899", false, true, false, null, "IN", "MN", "MX", "OUT");
        Add(LadderElementKind.MathSqr, 5, null, "SQR", "SQR", "Str.Plc.D.SQR", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathSqrt, 5, null, "SQRT", "SQRT", "Str.Plc.D.SQRT", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathLn, 5, null, "LN", "LN", "Str.Plc.D.LN", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathExp, 5, null, "EXP", "EXP", "Str.Plc.D.EXP", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathExpt, 5, null, "EXPT", "EXPT", "Str.Plc.D.EXPT", "#EC4899", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.MathFrac, 5, null, "FRAC", "FRAC", "Str.Plc.D.FRAC", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathSin, 5, null, "SIN", "SIN", "Str.Plc.D.SIN", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathCos, 5, null, "COS", "COS", "Str.Plc.D.COS", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathTan, 5, null, "TAN", "TAN", "Str.Plc.D.TAN", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathAsin, 5, null, "ASIN", "ASIN", "Str.Plc.D.ASIN", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathAcos, 5, null, "ACOS", "ACOS", "Str.Plc.D.ACOS", "#EC4899", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MathAtan, 5, null, "ATAN", "ATAN", "Str.Plc.D.ATAN", "#EC4899", false, true, false, null, "IN", null, null, "OUT");

        // =========================================================================
        // NHÓM 6: Move operations (+ Variant, Array[*], Legacy)
        // =========================================================================
        Add(LadderElementKind.Move, 6, null, "MOVE", "MOVE", "Str.Plc.D.MOVE", "#A855F7", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.MoveBlk, 6, null, "MOVE_BLK", "MOVE_BLK", "Str.Plc.D.MOVE_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.MoveBlkVariant, 6, null, "MOVE_BLK_VARIANT", "MOVE_BLK_VAR", "Str.Plc.D.MOVE_BLK_VAR", "#A855F7", false, true, false, null, "SRC", "COUNT", null, "DEST");
        Add(LadderElementKind.UMoveBlk, 6, null, "UMOVE_BLK", "UMOVE_BLK", "Str.Plc.D.UMOVE_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.FillBlk, 6, null, "FILL_BLK", "FILL_BLK", "Str.Plc.D.FILL_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.UFillBlk, 6, null, "UFILL_BLK", "UFILL_BLK", "Str.Plc.D.UFILL_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.Scatter, 6, null, "SCATTER", "SCATTER", "Str.Plc.D.SCATTER", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.ScatterBlk, 6, null, "SCATTER_BLK", "SCATTER_BLK", "Str.Plc.D.SCATTER_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.Gather, 6, null, "GATHER", "GATHER", "Str.Plc.D.GATHER", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.GatherBlk, 6, null, "GATHER_BLK", "GATHER_BLK", "Str.Plc.D.GATHER_BLK", "#A855F7", false, true, false, null, "IN", "COUNT", null, "OUT");
        Add(LadderElementKind.Swap, 6, null, "SWAP", "SWAP", "Str.Plc.D.SWAP", "#A855F7", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.Serialize, 6, null, "Serialize", "Serialize", "Str.Plc.D.SERIALIZE", "#A855F7", false, true, false, null, "SRC", "COUNT", null, "DEST");
        Add(LadderElementKind.Deserialize, 6, null, "Deserialize", "Deserialize", "Str.Plc.D.DESERIALIZE", "#A855F7", false, true, false, null, "SRC", "COUNT", null, "DEST");
        Add(LadderElementKind.VariantGet, 6, "Variant", "VariantGet", "VariantGet", "Str.Plc.D.VAR_GET", "#C084FC", false, true, false, null, "SRC", null, null, "DST");
        Add(LadderElementKind.VariantPut, 6, "Variant", "VariantPut", "VariantPut", "Str.Plc.D.VAR_PUT", "#C084FC", false, true, false, null, "SRC", null, null, "DST");
        Add(LadderElementKind.CountOfElements, 6, "Variant", "CountOfElements", "CountOfElem", "Str.Plc.D.COUNT_ELEM", "#C084FC", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.LowerBound, 6, "Array[*]", "LOWER_BOUND", "LOWER_BOUND", "Str.Plc.D.LOW_BND", "#C084FC", false, true, false, null, "ARR", "DIM", null, "OUT");
        Add(LadderElementKind.UpperBound, 6, "Array[*]", "UPPER_BOUND", "UPPER_BOUND", "Str.Plc.D.UP_BND", "#C084FC", false, true, false, null, "ARR", "DIM", null, "OUT");
        Add(LadderElementKind.FieldRead, 6, "Legacy", "FieldRead", "FieldRead", "Str.Plc.D.FIELD_RD", "#C084FC", false, true, false, null, "BASE", "INDEX", null, "VALUE");
        Add(LadderElementKind.FieldWrite, 6, "Legacy", "FieldWrite", "FieldWrite", "Str.Plc.D.FIELD_WR", "#C084FC", false, true, false, null, "VALUE", "INDEX", null, "BASE");

        // =========================================================================
        // NHÓM 7: Conversion operations
        // =========================================================================
        Add(LadderElementKind.Convert, 7, null, "CONVERT", "CONVERT", "Str.Plc.D.CONVERT", "#06B6D4", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.Round, 7, null, "ROUND", "ROUND", "Str.Plc.D.ROUND", "#06B6D4", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.Ceil, 7, null, "CEIL", "CEIL", "Str.Plc.D.CEIL", "#06B6D4", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.Floor, 7, null, "FLOOR", "FLOOR", "Str.Plc.D.FLOOR", "#06B6D4", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.Trunc, 7, null, "TRUNC", "TRUNC", "Str.Plc.D.TRUNC", "#06B6D4", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.NormX, 7, null, "NORM_X", "NORM_X", "Str.Plc.D.NORM_X", "#06B6D4", false, true, false, null, "VALUE", "MIN", "MAX", "OUT");
        Add(LadderElementKind.ScaleX, 7, null, "SCALE_X", "SCALE_X", "Str.Plc.D.SCALE_X", "#06B6D4", false, true, false, null, "VALUE", "MIN", "MAX", "OUT");

        // =========================================================================
        // NHÓM 8: Program control operations (+ Runtime control)
        // =========================================================================
        Add(LadderElementKind.Jump, 8, null, "-(JMP)", "(JMP)", "Str.Plc.D.JMP", "#F97316", true, true, false, "Label");
        Add(LadderElementKind.JumpNot, 8, null, "-(JMPN)", "(JMPN)", "Str.Plc.D.JMPN", "#F97316", true, true, false, "Label");
        Add(LadderElementKind.Label, 8, null, "LABEL", "LABEL", "Str.Plc.D.LABEL", "#F97316", false, false, false, "Label");
        Add(LadderElementKind.JumpList, 8, null, "JMP_LIST", "JMP_LIST", "Str.Plc.D.JMP_LIST", "#F97316", false, true, false, "Labels", "K");
        Add(LadderElementKind.Switch, 8, null, "SWITCH", "SWITCH", "Str.Plc.D.SWITCH", "#F97316", false, true, false, "Labels", "K", "Match");
        Add(LadderElementKind.Return, 8, null, "-(RET)", "(RET)", "Str.Plc.D.RET", "#EF4444", true, true, false);
        Add(LadderElementKind.EndisPw, 8, "Runtime control", "ENDIS_PW", "ENDIS_PW", "Str.Plc.D.ENDIS_PW", "#FB923C", false, true, false, null, "PW_EN", null, null, "RET");
        Add(LadderElementKind.ReTrigr, 8, "Runtime control", "RE_TRIGR", "RE_TRIGR", "Str.Plc.D.RE_TRIGR", "#FB923C", false, true, false);
        Add(LadderElementKind.StopCpu, 8, "Runtime control", "STP", "STP", "Str.Plc.D.STP", "#EF4444", false, true, false);
        Add(LadderElementKind.GetError, 8, "Runtime control", "GET_ERROR", "GET_ERROR", "Str.Plc.D.GET_ERR", "#FB923C", false, true, false, null, null, null, null, "ERROR");
        Add(LadderElementKind.GetErrId, 8, "Runtime control", "GET_ERR_ID", "GET_ERR_ID", "Str.Plc.D.GET_ERR_ID", "#FB923C", false, true, false, null, null, null, null, "ID");
        Add(LadderElementKind.RuntimeMeasure, 8, "Runtime control", "RUNTIME", "RUNTIME", "Str.Plc.D.RUNTIME", "#FB923C", false, true, false, null, "MEM", null, null, "OUT");

        // =========================================================================
        // NHÓM 9: Word logic operations
        // =========================================================================
        Add(LadderElementKind.WordAnd, 9, null, "AND", "AND", "Str.Plc.D.W_AND", "#8B5CF6", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.WordOr, 9, null, "OR", "OR", "Str.Plc.D.W_OR", "#8B5CF6", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.WordXor, 9, null, "XOR", "XOR", "Str.Plc.D.W_XOR", "#8B5CF6", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.WordInvert, 9, null, "INVERT", "INVERT", "Str.Plc.D.W_INV", "#8B5CF6", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.WordDeco, 9, null, "DECO", "DECO", "Str.Plc.D.DECO", "#8B5CF6", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.WordEnco, 9, null, "ENCO", "ENCO", "Str.Plc.D.ENCO", "#8B5CF6", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.WordSel, 9, null, "SEL", "SEL", "Str.Plc.D.SEL", "#8B5CF6", false, true, false, "G", "IN0", "IN1", null, "OUT");
        Add(LadderElementKind.WordMux, 9, null, "MUX", "MUX", "Str.Plc.D.MUX", "#8B5CF6", false, true, false, "K", "IN0", "IN1", "ELSE", "OUT");
        Add(LadderElementKind.WordDemux, 9, null, "DEMUX", "DEMUX", "Str.Plc.D.DEMUX", "#8B5CF6", false, true, false, "K", "IN", "OUT0", "OUT1", "OUT2");

        // =========================================================================
        // NHÓM 10: Shift and rotate
        // =========================================================================
        Add(LadderElementKind.ShiftRight, 10, null, "SHR", "SHR", "Str.Plc.D.SHR", "#10B981", false, true, false, null, "IN", "N", null, "OUT");
        Add(LadderElementKind.ShiftLeft, 10, null, "SHL", "SHL", "Str.Plc.D.SHL", "#10B981", false, true, false, null, "IN", "N", null, "OUT");
        Add(LadderElementKind.RotateRight, 10, null, "ROR", "ROR", "Str.Plc.D.ROR", "#10B981", false, true, false, null, "IN", "N", null, "OUT");
        Add(LadderElementKind.RotateLeft, 10, null, "ROL", "ROL", "Str.Plc.D.ROL", "#10B981", false, true, false, null, "IN", "N", null, "OUT");

        // =========================================================================
        // NHÓM 11: Extended instructions (Date/Time RTC, String + Char, Block CALL)
        // =========================================================================
        Add(LadderElementKind.ReadSysTime, 11, "Date & Time", "RD_SYS_T", "RD_SYS_T", "Str.Plc.D.RD_SYS_T", "#0EA5E9", false, true, false, null, null, null, null, "OUT");
        Add(LadderElementKind.ReadLocTime, 11, "Date & Time", "RD_LOC_T", "RD_LOC_T", "Str.Plc.D.RD_LOC_T", "#0EA5E9", false, true, false, null, null, null, null, "OUT");
        Add(LadderElementKind.TimeAdd, 11, "Date & Time", "T_ADD", "T_ADD", "Str.Plc.D.T_ADD", "#0EA5E9", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.TimeSub, 11, "Date & Time", "T_SUB", "T_SUB", "Str.Plc.D.T_SUB", "#0EA5E9", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.TimeDiff, 11, "Date & Time", "T_DIFF", "T_DIFF", "Str.Plc.D.T_DIFF", "#0EA5E9", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.TimeConv, 11, "Date & Time", "T_CONV", "T_CONV", "Str.Plc.D.T_CONV", "#0EA5E9", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.StringConv, 11, "String + Char", "S_CONV", "S_CONV", "Str.Plc.D.S_CONV", "#38BDF8", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.StringLen, 11, "String + Char", "LEN", "LEN", "Str.Plc.D.LEN", "#38BDF8", false, true, false, null, "IN", null, null, "OUT");
        Add(LadderElementKind.StringConcat, 11, "String + Char", "CONCAT", "CONCAT", "Str.Plc.D.CONCAT", "#38BDF8", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.StringLeft, 11, "String + Char", "LEFT", "LEFT", "Str.Plc.D.LEFT", "#38BDF8", false, true, false, null, "IN", "L", null, "OUT");
        Add(LadderElementKind.StringRight, 11, "String + Char", "RIGHT", "RIGHT", "Str.Plc.D.RIGHT", "#38BDF8", false, true, false, null, "IN", "L", null, "OUT");
        Add(LadderElementKind.StringMid, 11, "String + Char", "MID", "MID", "Str.Plc.D.MID", "#38BDF8", false, true, false, null, "IN", "L", "P", "OUT");
        Add(LadderElementKind.StringDelete, 11, "String + Char", "DELETE", "DELETE", "Str.Plc.D.DELETE", "#38BDF8", false, true, false, null, "IN", "L", "P", "OUT");
        Add(LadderElementKind.StringInsert, 11, "String + Char", "INSERT", "INSERT", "Str.Plc.D.INSERT", "#38BDF8", false, true, false, null, "IN1", "IN2", "P", "OUT");
        Add(LadderElementKind.StringReplace, 11, "String + Char", "REPLACE", "REPLACE", "Str.Plc.D.REPLACE", "#38BDF8", false, true, false, null, "IN1", "IN2", "L,P", "OUT");
        Add(LadderElementKind.StringFind, 11, "String + Char", "FIND", "FIND", "Str.Plc.D.FIND", "#38BDF8", false, true, false, null, "IN1", "IN2", null, "OUT");
        Add(LadderElementKind.CallBlock, 11, "Blocks", "CALL", "CALL", "Str.Plc.D.CALL", "#0284C7", false, true, false, "Block", "DB");

        // =========================================================================
        // NHÓM 12: Technology instructions (PWM, HSC, PID_Compact)
        // =========================================================================
        Add(LadderElementKind.CtrlPwm, 12, "Pulse / HSC", "CTRL_PWM", "CTRL_PWM", "Str.Plc.D.CTRL_PWM", "#F43F5E", false, true, true, "DB", "PWM", "ENABLE", "FREQ", "STATUS");
        Add(LadderElementKind.CtrlHsc, 12, "Pulse / HSC", "CTRL_HSC", "CTRL_HSC", "Str.Plc.D.CTRL_HSC", "#F43F5E", false, true, true, "DB", "HSC", "NEW_CV", "DIR,RV", "STATUS");
        Add(LadderElementKind.PidCompact, 12, "PID Control", "PID_Compact", "PID_Compact", "Str.Plc.D.PID", "#E11D48", false, true, true, "DB", "Setpoint", "Input", "Kp,Ti,Td", "Output");

        // =========================================================================
        // NHÓM 13: Communication instructions (Modbus RTU / TCP -> VFD)
        // =========================================================================
        Add(LadderElementKind.ModbusCommLoad, 13, "Modbus RTU", "Modbus_Comm_Load", "MB_COMM_LOAD", "Str.Plc.D.MB_LOAD", "#EAB308", false, true, true, "DB", "PORT", "BAUD", "PARITY", "STATUS");
        Add(LadderElementKind.ModbusMaster, 13, "Modbus RTU", "Modbus_Master", "Modbus_Master", "Str.Plc.D.MB_MASTER", "#EAB308", false, true, true, "DB", "MB_ADDR", "MODE", "ADDR,LEN", "DATA_PTR");
        Add(LadderElementKind.ModbusTcpClient, 13, "Modbus TCP", "MB_CLIENT", "MB_CLIENT", "Str.Plc.D.MB_CLIENT", "#D97706", false, true, true, "DB", "UNIT_ID", "MB_MODE", "ADDR,LEN", "DATA_PTR");
    }

    private static void Add(
        LadderElementKind kind,
        int groupIndex,
        string? subGroup,
        string symbol,
        string boxHeader,
        string descKey,
        string accentHex,
        bool isInlineGlyph,
        bool isOutputOrAction,
        bool usesDbBlock,
        string? addrLabel = null,
        string? p1Label = null,
        string? p2Label = null,
        string? extraLabel = null,
        string? outLabel = null)
    {
        var meta = new PlcInstructionMeta(
            kind, groupIndex, $"Str.Plc.Cat{groupIndex}", subGroup, symbol, boxHeader, descKey,
            accentHex, isInlineGlyph, isOutputOrAction, usesDbBlock, addrLabel, p1Label, p2Label, extraLabel, outLabel);
        _all.Add(meta);
        _byKind[kind] = meta;
    }

    public static IReadOnlyList<PlcInstructionMeta> All => _all;

    public static PlcInstructionMeta? Get(LadderElementKind kind) =>
        _byKind.TryGetValue(kind, out var m) ? m : null;

    public static string GetDescription(PlcInstructionMeta meta) => LocalizationManager.Get(meta.DescKey);

    public static bool IsOutputOrAction(LadderElementKind kind) =>
        _byKind.TryGetValue(kind, out var m) && m.IsOutputOrAction;

    public static bool IsInlineGlyph(LadderElementKind kind) =>
        _byKind.TryGetValue(kind, out var m) && m.IsInlineGlyph;

    public static bool IsComparator(LadderElementKind kind) =>
        _byKind.TryGetValue(kind, out var m) && m.GroupIndex == 4;

    public static bool SupportsMultiInput(LadderElementKind kind) => kind is
        LadderElementKind.MathAdd or
        LadderElementKind.MathMul or
        LadderElementKind.MathMin or
        LadderElementKind.MathMax or
        LadderElementKind.WordAnd or
        LadderElementKind.WordOr or
        LadderElementKind.WordXor or
        LadderElementKind.WordMux or
        LadderElementKind.Calculate;

    public static string GetExtraInputLabel(LadderElementKind kind, int extraZeroBasedIndex) =>
        kind == LadderElementKind.WordMux
            ? $"IN{extraZeroBasedIndex + 2}"
            : $"IN{extraZeroBasedIndex + 3}";

    public static bool SupportsMultiOutput(LadderElementKind kind) => kind == LadderElementKind.Move;

    public static bool SupportsDataTypeSelector(LadderElementKind kind) => kind is
        LadderElementKind.Calculate or
        LadderElementKind.CounterCTU or LadderElementKind.CounterCTD or LadderElementKind.CounterCTUD or
        LadderElementKind.MathAdd or LadderElementKind.MathSub or LadderElementKind.MathMul or LadderElementKind.MathDiv or
        LadderElementKind.MathMod or LadderElementKind.MathNeg or LadderElementKind.MathInc or LadderElementKind.MathDec or
        LadderElementKind.MathAbs or LadderElementKind.MathMin or LadderElementKind.MathMax or LadderElementKind.MathLimit or
        LadderElementKind.CmpEqual or LadderElementKind.CmpNotEqual or LadderElementKind.CmpGreaterOrEqual or
        LadderElementKind.CmpLessOrEqual or LadderElementKind.CmpGreater or LadderElementKind.CmpLess or
        LadderElementKind.InRange or LadderElementKind.OutRange or
        LadderElementKind.Move or LadderElementKind.Swap or
        LadderElementKind.NormX or LadderElementKind.ScaleX or LadderElementKind.Convert or LadderElementKind.StringConv or LadderElementKind.TimeConv or
        LadderElementKind.Round or LadderElementKind.Ceil or LadderElementKind.Floor or LadderElementKind.Trunc or
        LadderElementKind.WordAnd or LadderElementKind.WordOr or LadderElementKind.WordXor or LadderElementKind.WordInvert or
        LadderElementKind.WordDeco or LadderElementKind.WordEnco or
        LadderElementKind.ShiftRight or LadderElementKind.ShiftLeft or LadderElementKind.RotateRight or LadderElementKind.RotateLeft;

    public static string[] GetSupportedDataTypesForInstruction(LadderElementKind kind) => kind switch
    {
        LadderElementKind.Convert => ["Int", "DInt", "Real", "LReal", "Time", "Bcd16", "Bcd32", "SInt", "USInt", "UInt", "UDInt", "Byte", "Word", "DWord", "Char"],
        LadderElementKind.StringConv => ["String", "Int", "DInt", "Real", "LReal"],
        LadderElementKind.TimeConv => ["DInt", "Time", "DTL"],
        LadderElementKind.Calculate => ["Int", "DInt", "Real", "LReal", "SInt", "USInt", "UInt", "UDInt"],
        LadderElementKind.CounterCTU or LadderElementKind.CounterCTD or LadderElementKind.CounterCTUD =>
            ["Int", "DInt", "SInt", "USInt", "UInt", "UDInt"],
        LadderElementKind.MathMod or LadderElementKind.MathInc or LadderElementKind.MathDec =>
            ["Int", "DInt", "SInt", "USInt", "UInt", "UDInt"],
        LadderElementKind.Swap => ["Word", "DWord", "Int", "UInt", "DInt", "UDInt"],
        LadderElementKind.WordDeco or LadderElementKind.WordEnco => ["Word", "Byte", "DWord"],
        LadderElementKind.Round or LadderElementKind.Ceil or LadderElementKind.Floor or LadderElementKind.Trunc =>
            ["DInt", "Int", "SInt", "USInt", "UInt", "UDInt"],
        LadderElementKind.NormX => ["Int", "DInt", "UInt", "UDInt", "Real"],
        LadderElementKind.ScaleX => ["Real", "Int", "DInt", "UInt"],
        LadderElementKind.WordAnd or LadderElementKind.WordOr or LadderElementKind.WordXor or LadderElementKind.WordInvert or
        LadderElementKind.ShiftRight or LadderElementKind.ShiftLeft or LadderElementKind.RotateRight or LadderElementKind.RotateLeft =>
            ["Word", "Byte", "DWord", "Int", "DInt"],
        LadderElementKind.CmpEqual or LadderElementKind.CmpNotEqual or LadderElementKind.CmpGreaterOrEqual or
        LadderElementKind.CmpLessOrEqual or LadderElementKind.CmpGreater or LadderElementKind.CmpLess or
        LadderElementKind.InRange or LadderElementKind.OutRange =>
            ["Int", "DInt", "Real", "Time", "Word", "DWord", "Byte", "UInt", "UDInt"],
        _ => ["Int", "DInt", "Real", "Time", "SInt", "USInt", "UInt", "UDInt", "Byte", "Word", "DWord"]
    };

    public static IReadOnlyList<LadderElementKind> DefaultFavoriteInstructions { get; } =
    [
        LadderElementKind.ContactNO,
        LadderElementKind.ContactNC,
        LadderElementKind.CoilOut,
        LadderElementKind.TimerTON,
        LadderElementKind.CounterCTU,
        LadderElementKind.CmpEqual,
        LadderElementKind.Move
    ];

    public static List<PlcInstructionMeta> SearchByPrefixOrText(string? query, int maxCount = 12)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _all.Take(maxCount).ToList();

        string q = query.Trim();
        return _all
            .OrderByDescending(m => m.BoxHeader.StartsWith(q, StringComparison.OrdinalIgnoreCase) || m.Symbol.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .Where(m => m.BoxHeader.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        m.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        GetDescription(m).Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(maxCount)
            .ToList();
    }

    /// <summary>Phân giải tên lệnh gõ trực tiếp trong Empty Box [F8] (VD: "TON", "NORM_X", "SCALE_X", "ADD", "SHR", "JMP"...).</summary>
    public static bool TryResolveByName(string? typedName, out LadderElementKind kind)
    {
        kind = LadderElementKind.EmptyBox;
        if (string.IsNullOrWhiteSpace(typedName)) return false;

        string clean = typedName.Trim();
        string norm = clean.ToUpperInvariant().Replace(" ", "");

        foreach (var m in _all)
        {
            string symNorm = m.Symbol.ToUpperInvariant().Replace(" ", "");
            string hdrNorm = m.BoxHeader.ToUpperInvariant().Replace(" ", "");
            if (symNorm == norm || hdrNorm == norm || m.Kind.ToString().Equals(norm, StringComparison.OrdinalIgnoreCase))
            {
                kind = m.Kind;
                return true;
            }
        }

        // Các tên viết tắt phổ biến trong TIA Portal
        switch (norm)
        {
            case "==" or "EQ": kind = LadderElementKind.CmpEqual; return true;
            case "<>" or "!=" or "NE": kind = LadderElementKind.CmpNotEqual; return true;
            case ">=" or "GE": kind = LadderElementKind.CmpGreaterOrEqual; return true;
            case "<=" or "LE": kind = LadderElementKind.CmpLessOrEqual; return true;
            case ">" or "GT": kind = LadderElementKind.CmpGreater; return true;
            case "<" or "LT": kind = LadderElementKind.CmpLess; return true;
            case "NO": kind = LadderElementKind.ContactNO; return true;
            case "NC": kind = LadderElementKind.ContactNC; return true;
            case "NOT": kind = LadderElementKind.InvertRLO; return true;
            case "OUT" or "COIL": kind = LadderElementKind.CoilOut; return true;
            case "SET" or "S": kind = LadderElementKind.CoilSet; return true;
            case "RESET" or "R": kind = LadderElementKind.CoilReset; return true;
            case "JMP": kind = LadderElementKind.Jump; return true;
            case "JMPN": kind = LadderElementKind.JumpNot; return true;
            case "RET": kind = LadderElementKind.Return; return true;
            case "RT": kind = LadderElementKind.CoilRT; return true;
            case "PT": kind = LadderElementKind.CoilPT; return true;
            case "OK": kind = LadderElementKind.CheckOk; return true;
            case "NOT_OK": kind = LadderElementKind.CheckNotOk; return true;
            default: return false;
        }
    }

    public static LadderNode CreateDefaultNode(LadderElementKind kind, PlcProgramModel prog, bool allocateDbTag = true)
    {
        if (kind == LadderElementKind.EmptyBox)
            return new LadderNode { Kind = LadderElementKind.EmptyBox, Address = "???" };

        switch (kind)
        {
            case LadderElementKind.ContactNO:
            case LadderElementKind.ContactNC:
                return new LadderNode { Kind = kind, Address = "%I0.0" };
            case LadderElementKind.InvertRLO:
            case LadderElementKind.Return:
            case LadderElementKind.ReTrigr:
            case LadderElementKind.StopCpu:
                return new LadderNode { Kind = kind };
            case LadderElementKind.CoilOut:
            case LadderElementKind.CoilNegate:
            case LadderElementKind.CoilSet:
            case LadderElementKind.CoilReset:
                return new LadderNode { Kind = kind, Address = "%Q0.0" };
            case LadderElementKind.SetBitField:
            case LadderElementKind.ResetBitField:
                return new LadderNode { Kind = kind, Address = "%Q0.0", Param1 = "4" };
            case LadderElementKind.FlipFlopSR:
            case LadderElementKind.FlipFlopRS:
                return new LadderNode { Kind = kind, Address = "%M2.0", Param1 = "%I0.1" };
            case LadderElementKind.EdgePositive:
            case LadderElementKind.EdgeNegative:
            case LadderElementKind.CoilEdgePositive:
            case LadderElementKind.CoilEdgeNegative:
                return new LadderNode { Kind = kind, Address = "%I0.0", Param1 = "%M10.0" };
            case LadderElementKind.PTrig:
            case LadderElementKind.NTrig:
                return new LadderNode { Kind = kind, Address = "%M10.0" };
            case LadderElementKind.RTrig:
            case LadderElementKind.FTrig:
            {
                if (!allocateDbTag)
                    return new LadderNode { Kind = kind, Address = "<???>" };
                string db = prog.AllocateNextDbAddress();
                prog.EnsureTagExists(db, $"TRIG_{db[3..]}", "IEC_Trig", overwriteDataType: true);
                return new LadderNode { Kind = kind, Address = db };
            }

            // Nhóm 2: Timers
            case LadderElementKind.TimerTP:
            case LadderElementKind.TimerTON:
            case LadderElementKind.TimerTOF:
            case LadderElementKind.TimerTONR:
            case LadderElementKind.CoilTP:
            case LadderElementKind.CoilTON:
            case LadderElementKind.CoilTOF:
            case LadderElementKind.CoilTONR:
            {
                string db = "<???>";
                if (allocateDbTag)
                {
                    db = prog.AllocateNextDbAddress();
                    prog.EnsureTagExists(db, $"TIMER_{db[3..]}", "IEC_Timer", overwriteDataType: true);
                }
                return new LadderNode
                {
                    Kind = kind,
                    Address = db,
                    Param1 = "T#3s",
                    Param2 = kind == LadderElementKind.TimerTONR ? "%I0.2" : string.Empty
                };
            }
            case LadderElementKind.CoilRT:
                return new LadderNode { Kind = kind, Address = "%DB1" };
            case LadderElementKind.CoilPT:
                return new LadderNode { Kind = kind, Address = "%DB1", Param1 = "T#5s" };

            // Nhóm 3: Counters
            case LadderElementKind.CounterCTU:
            case LadderElementKind.CounterCTD:
            case LadderElementKind.CounterCTUD:
            {
                string db = "<???>";
                if (allocateDbTag)
                {
                    db = prog.AllocateNextDbAddress();
                    prog.EnsureTagExists(db, $"COUNTER_{db[3..]}", "IEC_Counter", overwriteDataType: true);
                }
                return new LadderNode
                {
                    Kind = kind,
                    Address = db,
                    DataType = "Int",
                    Param1 = "5",
                    Param2 = "%I0.2",
                    OutputAddress = kind == LadderElementKind.CounterCTUD ? "%I0.3" : string.Empty,
                    ExtraParam = kind == LadderElementKind.CounterCTUD ? "%I0.4" : string.Empty
                };
            }

            // Nhóm 4: Comparators
            case LadderElementKind.CmpEqual:
            case LadderElementKind.CmpNotEqual:
            case LadderElementKind.CmpGreaterOrEqual:
            case LadderElementKind.CmpLessOrEqual:
            case LadderElementKind.CmpGreater:
            case LadderElementKind.CmpLess:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "1" };
            case LadderElementKind.InRange:
            case LadderElementKind.OutRange:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "0", ExtraParam = "100" };
            case LadderElementKind.CheckOk:
            case LadderElementKind.CheckNotOk:
                return new LadderNode { Kind = kind, Address = "%MD10" };
            case LadderElementKind.VarEqType:
            case LadderElementKind.VarNeType:
            case LadderElementKind.VarEqElemType:
            case LadderElementKind.VarNeElemType:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "%MW4" };
            case LadderElementKind.VarIsNull:
            case LadderElementKind.VarNotNull:
            case LadderElementKind.VarIsArray:
                return new LadderNode { Kind = kind, Param1 = "%MW2" };
            case LadderElementKind.VarEqTypeOfDb:
            case LadderElementKind.VarNeTypeOfDb:
                return new LadderNode { Kind = kind, Param1 = "%DB1", Param2 = "IEC_Timer" };

            // Nhóm 5: Math
            case LadderElementKind.Calculate:
                return new LadderNode { Kind = kind, DataType = "Real", Param1 = "%MD10", Param2 = "2.0", ExtraParam = "IN1 * IN2", OutputAddress = "%MD14" };
            case LadderElementKind.MathAdd:
            case LadderElementKind.MathSub:
            case LadderElementKind.MathMul:
            case LadderElementKind.MathDiv:
            case LadderElementKind.MathMod:
            case LadderElementKind.MathMin:
            case LadderElementKind.MathMax:
            case LadderElementKind.MathExpt:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "1", OutputAddress = "%MW2" };
            case LadderElementKind.MathLimit:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "0", ExtraParam = "100", OutputAddress = "%MW4" };
            case LadderElementKind.MathInc:
            case LadderElementKind.MathDec:
                return new LadderNode { Kind = kind, OutputAddress = "%MW2" };
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
                return new LadderNode { Kind = kind, Param1 = "%MD10", OutputAddress = "%MD14" };

            // Nhóm 6: Move
            case LadderElementKind.Move:
            case LadderElementKind.Swap:
            case LadderElementKind.VariantGet:
            case LadderElementKind.VariantPut:
            case LadderElementKind.CountOfElements:
                return new LadderNode { Kind = kind, Param1 = "%MW2", OutputAddress = "%MW4" };
            case LadderElementKind.MoveBlk:
            case LadderElementKind.MoveBlkVariant:
            case LadderElementKind.UMoveBlk:
            case LadderElementKind.FillBlk:
            case LadderElementKind.UFillBlk:
            case LadderElementKind.Serialize:
            case LadderElementKind.Deserialize:
                return new LadderNode { Kind = kind, Param1 = "%MW0", Param2 = "4", OutputAddress = "%MW10" };
            case LadderElementKind.Scatter:
            case LadderElementKind.ScatterBlk:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "8", OutputAddress = "%M20.0" };
            case LadderElementKind.Gather:
            case LadderElementKind.GatherBlk:
                return new LadderNode { Kind = kind, Param1 = "%M20.0", Param2 = "8", OutputAddress = "%MW2" };
            case LadderElementKind.LowerBound:
            case LadderElementKind.UpperBound:
                return new LadderNode { Kind = kind, Param1 = "%MW0", Param2 = "1", OutputAddress = "%MW4" };
            case LadderElementKind.FieldRead:
                return new LadderNode { Kind = kind, Param1 = "%MW0", Param2 = "0", OutputAddress = "%MW20" };
            case LadderElementKind.FieldWrite:
                return new LadderNode { Kind = kind, Param1 = "10", Param2 = "0", OutputAddress = "%MW0" };

            // Nhóm 7: Conversion
            case LadderElementKind.Convert:
            case LadderElementKind.Round:
            case LadderElementKind.Ceil:
            case LadderElementKind.Floor:
            case LadderElementKind.Trunc:
                return new LadderNode { Kind = kind, Param1 = "%MD10", OutputAddress = "%MW4" };
            case LadderElementKind.NormX:
                return new LadderNode { Kind = kind, Param1 = "%IW64", Param2 = "0", ExtraParam = "27648", OutputAddress = "%MD10" };
            case LadderElementKind.ScaleX:
                return new LadderNode { Kind = kind, Param1 = "%MD10", Param2 = "0.0", ExtraParam = "100.0", OutputAddress = "%MD14" };

            // Nhóm 8: Program control
            case LadderElementKind.Jump:
            case LadderElementKind.JumpNot:
            case LadderElementKind.Label:
                return new LadderNode { Kind = kind, Address = "LBL1" };
            case LadderElementKind.JumpList:
                return new LadderNode { Kind = kind, Address = "LBL1,LBL2", Param1 = "%MW2" };
            case LadderElementKind.Switch:
                return new LadderNode { Kind = kind, Address = "LBL1,LBL2", Param1 = "%MW2", Param2 = "1,2" };
            case LadderElementKind.EndisPw:
                return new LadderNode { Kind = kind, Param1 = "1", OutputAddress = "%MW20" };
            case LadderElementKind.GetError:
            case LadderElementKind.GetErrId:
                return new LadderNode { Kind = kind, OutputAddress = "%MW20" };
            case LadderElementKind.RuntimeMeasure:
                return new LadderNode { Kind = kind, Param1 = "%MD30", OutputAddress = "%MD34" };

            // Nhóm 9: Word logic
            case LadderElementKind.WordAnd:
            case LadderElementKind.WordOr:
            case LadderElementKind.WordXor:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "15", OutputAddress = "%MW4" };
            case LadderElementKind.WordInvert:
            case LadderElementKind.WordDeco:
            case LadderElementKind.WordEnco:
                return new LadderNode { Kind = kind, Param1 = "%MW2", OutputAddress = "%MW4" };
            case LadderElementKind.WordSel:
                return new LadderNode { Kind = kind, Address = "%I0.0", Param1 = "10", Param2 = "20", OutputAddress = "%MW4" };
            case LadderElementKind.WordMux:
                return new LadderNode { Kind = kind, Address = "%MW0", Param1 = "10", Param2 = "20", ExtraParam = "30", OutputAddress = "%MW4" };
            case LadderElementKind.WordDemux:
                return new LadderNode { Kind = kind, Address = "%MW0", Param1 = "%MW2", Param2 = "%MW10", ExtraParam = "%MW12", OutputAddress = "%MW14" };

            // Nhóm 10: Shift & Rotate
            case LadderElementKind.ShiftRight:
            case LadderElementKind.ShiftLeft:
            case LadderElementKind.RotateRight:
            case LadderElementKind.RotateLeft:
                return new LadderNode { Kind = kind, Param1 = "%MW2", Param2 = "1", OutputAddress = "%MW4" };

            // Nhóm 11: Extended (RTC, String, CALL)
            case LadderElementKind.ReadSysTime:
            case LadderElementKind.ReadLocTime:
                return new LadderNode { Kind = kind, OutputAddress = "%DB10.DBD0" };
            case LadderElementKind.TimeAdd:
            case LadderElementKind.TimeSub:
                return new LadderNode { Kind = kind, Param1 = "T#1s", Param2 = "T#500ms", OutputAddress = "%MD40" };
            case LadderElementKind.TimeDiff:
                return new LadderNode { Kind = kind, Param1 = "%DB10.DBD0", Param2 = "%DB10.DBD12", OutputAddress = "%MD40" };
            case LadderElementKind.TimeConv:
                return new LadderNode { Kind = kind, DataType = "DInt", Param1 = "T#2s", OutputAddress = "%MD40" };
            case LadderElementKind.StringConv:
                return new LadderNode { Kind = kind, DataType = "String", Param1 = "%MW20", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringLen:
                return new LadderNode { Kind = kind, Param1 = "'HELLO'", OutputAddress = "%MW20" };
            case LadderElementKind.StringConcat:
                return new LadderNode { Kind = kind, Param1 = "'PLC_'", Param2 = "'RUN'", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringLeft:
            case LadderElementKind.StringRight:
                return new LadderNode { Kind = kind, Param1 = "'WIRINGSIM'", Param2 = "4", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringMid:
            case LadderElementKind.StringDelete:
                return new LadderNode { Kind = kind, Param1 = "'WIRINGSIM'", Param2 = "3", ExtraParam = "2", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringInsert:
                return new LadderNode { Kind = kind, Param1 = "'PLCSIM'", Param2 = "'_'", ExtraParam = "3", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringReplace:
                return new LadderNode { Kind = kind, Param1 = "'PLC_OLD'", Param2 = "'NEW'", ExtraParam = "3,5", OutputAddress = "%DB11.DBD0" };
            case LadderElementKind.StringFind:
                return new LadderNode { Kind = kind, Param1 = "'MOTOR_RUN'", Param2 = "'RUN'", OutputAddress = "%MW20" };
            case LadderElementKind.CallBlock:
                return new LadderNode { Kind = kind, Address = "FC1", Param1 = string.Empty };

            // Nhóm 12: Technology (PWM, HSC, PID_Compact)
            case LadderElementKind.CtrlPwm:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "1", Param2 = "TRUE", ExtraParam = "10.0", OutputAddress = "%MW60" };
            case LadderElementKind.CtrlHsc:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "1", Param2 = "0", ExtraParam = "1,100", OutputAddress = "%MW62" };
            case LadderElementKind.PidCompact:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "50.0", Param2 = "%MD28", ExtraParam = "1.5,5.0,0.0", OutputAddress = "%MD64" };

            // Nhóm 13: Communication (Modbus RTU / TCP -> VFD)
            case LadderElementKind.ModbusCommLoad:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "1", Param2 = "9600", ExtraParam = "0", OutputAddress = "%MW70" };
            case LadderElementKind.ModbusMaster:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "1", Param2 = "1", ExtraParam = "40001,1", OutputAddress = "%MW72" };
            case LadderElementKind.ModbusTcpClient:
                return new LadderNode { Kind = kind, Address = allocateDbTag ? prog.AllocateNextDbAddress() : "<???>", Param1 = "1", Param2 = "1", ExtraParam = "40001,1", OutputAddress = "%MW74" };

            default:
                return new LadderNode { Kind = kind };
        }
    }
}
