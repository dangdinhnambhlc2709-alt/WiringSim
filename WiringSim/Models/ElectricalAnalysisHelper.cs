using WiringSim.Models.Enums;

namespace WiringSim.Models;

/// <summary>
/// Kết quả kiểm tra mức điện áp cấp vào cuộn hút hoặc đèn so với thông số định mức.
/// </summary>
public enum RatedSupplyStatus
{
    Unpowered = 0,
    Normal = 1,
    Undervoltage = 2,
    WrongCurrentKind = 3,
    OvervoltageBurnout = 4
}

/// <summary>
/// Tiện ích tính toán điện áp hiệu dụng, pha xoay chiều (Phasor 120 độ), cực tính một chiều,
/// và kiểm tra cháy cuộn hút / cháy bóng đèn khi cấp quá áp hoặc sai loại dòng AC/DC.
/// </summary>
public static class ElectricalAnalysisHelper
{
    private const double Sin120 = 0.8660254037844386; // sqrt(3)/2

    /// <summary>
    /// Trả về vector phức (Re, Im) của pha xoay chiều theo điện áp pha (so với N).
    /// Với hệ 380V/220V: V_phase = 220V => V_line = 380V.
    /// </summary>
    public static bool TryGetAcPhasor(PotentialToken token, out (double Re, double Im) phasor)
    {
        if (token.CurrentKind != CurrentKind.AC)
        {
            phasor = (0, 0);
            return false;
        }

        // Token.Voltage lưu điện áp pha đối với nguồn 3P (220V cho hệ 380V, 127V cho hệ 220V 3P)
        // hoặc điện áp dây-trung tính đối với nguồn 1P (24V, 110V, 220V, 380V).
        double vMag = token.Phase == PhaseType.N ? 0.0 : token.Voltage;

        switch (token.Phase)
        {
            case PhaseType.L1:
                phasor = (vMag, 0.0);
                return true;
            case PhaseType.L2:
                phasor = (-0.5 * vMag, -vMag * Sin120);
                return true;
            case PhaseType.L3:
                phasor = (-0.5 * vMag, +vMag * Sin120);
                return true;
            case PhaseType.N:
                phasor = (0.0, 0.0);
                return true;
            default:
                phasor = (0.0, 0.0);
                return false;
        }
    }

    public static bool TryGetAcPhasor(PhaseType phase, out (double Re, double Im) phasor) =>
        TryGetAcPhasor(new PotentialToken("", "", CurrentKind.AC, phase, 220.0), out phasor);

    /// <summary>
    /// Tính hiệu điện thế hiệu dụng (V) và loại dòng điện (AC/DC) giữa 2 chân đấu nối.
    /// Lưu ý: Dây PE không được tính là đường hồi dòng làm việc hợp lệ cho tải.
    /// </summary>
    public static double GetOperatingVoltageAcross(Terminal a, Terminal b, out CurrentKind kind)
    {
        kind = CurrentKind.None;

        // 1. Kiểm tra nguồn DC (cùng bộ nguồn)
        foreach (var pa in a.ActivePotentials)
        {
            if (pa.CurrentKind != CurrentKind.DC) continue;
            foreach (var pb in b.ActivePotentials)
            {
                if (pb.CurrentKind != CurrentKind.DC) continue;
                if (pa.SourceComponentId != pb.SourceComponentId) continue;

                if (pa.Phase == PhaseType.DC_Plus && pb.Phase == PhaseType.DC_Minus)
                {
                    kind = CurrentKind.DC;
                    return pa.Voltage;
                }
                if (pa.Phase == PhaseType.DC_Minus && pb.Phase == PhaseType.DC_Plus)
                {
                    kind = CurrentKind.DC;
                    return pb.Voltage;
                }
            }
        }

        // 2. Kiểm tra nguồn AC (L1, L2, L3, N)
        foreach (var pa in a.ActivePotentials)
        {
            if (pa.CurrentKind != CurrentKind.AC) continue;
            if (!TryGetAcPhasor(pa, out var va)) continue;

            foreach (var pb in b.ActivePotentials)
            {
                if (pb.CurrentKind != CurrentKind.AC) continue;
                if (!TryGetAcPhasor(pb, out var vb)) continue;

                double dRe = va.Re - vb.Re;
                double dIm = va.Im - vb.Im;
                double diff = Math.Sqrt(dRe * dRe + dIm * dIm);
                if (diff > 5.0)
                {
                    kind = CurrentKind.AC;
                    return Math.Round(diff, 0);
                }
            }
        }

        return 0.0;
    }

    public static (CurrentKind Kind, double NominalVolts, string Label) GetRatingSpec(CoilVoltageRating rating) => rating switch
    {
        CoilVoltageRating.DC_12V => (CurrentKind.DC, 12.0, "12VDC"),
        CoilVoltageRating.DC_24V => (CurrentKind.DC, 24.0, "24VDC"),
        CoilVoltageRating.AC_24V => (CurrentKind.AC, 24.0, "24VAC"),
        CoilVoltageRating.AC_110V => (CurrentKind.AC, 110.0, "110VAC"),
        CoilVoltageRating.AC_220V => (CurrentKind.AC, 220.0, "220VAC"),
        CoilVoltageRating.AC_380V => (CurrentKind.AC, 380.0, "380VAC"),
        _ => (CurrentKind.AC, 220.0, "220VAC")
    };

    /// <summary>
    /// Đánh giá điện áp thực tế đặt vào cuộn hút / bóng đèn so với định mức:
    /// - Nếu V_applied &gt; 1.25 * V_nominal => CHÁY CUỘN HÚT / CHÁY BÓNG (kể cả cấp 220VAC vào cuộn 24VDC!).
    /// - Nếu sai loại dòng (AC vs DC) => Không hút / Không sáng.
    /// - Nếu trong ngưỡng [0.78 * V_nominal .. 1.25 * V_nominal] và đúng AC/DC => Hoạt động chuẩn!
    /// </summary>
    public static RatedSupplyStatus EvaluateRatedSupply(
        Terminal termA,
        Terminal termB,
        CoilVoltageRating rating,
        out double appliedVoltage,
        out CurrentKind appliedKind)
    {
        appliedVoltage = GetOperatingVoltageAcross(termA, termB, out appliedKind);
        if (appliedVoltage < 1.0 || appliedKind == CurrentKind.None)
            return RatedSupplyStatus.Unpowered;

        var (reqKind, nominalV, _) = GetRatingSpec(rating);

        // Cấp điện áp vượt quá 125% định mức => Cháy cuộn hút / cháy đèn ngay lập tức!
        if (appliedVoltage > nominalV * 1.25)
            return RatedSupplyStatus.OvervoltageBurnout;

        // Cấp sai loại dòng điện (ví dụ cấp 24VAC vào cuộn 24VDC hoặc ngược lại)
        if (appliedKind != reqKind)
            return RatedSupplyStatus.WrongCurrentKind;

        // Điện áp quá thấp chưa đủ lực từ hút lõi thép
        if (appliedVoltage < nominalV * 0.78)
            return RatedSupplyStatus.Undervoltage;

        return RatedSupplyStatus.Normal;
    }
}
