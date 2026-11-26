using WiringSim.Models.Enums;

namespace WiringSim.Models;

/// <summary>
/// Đường dẫn điện nội bộ có điện trở ~ 0 (tiếp điểm đóng, thanh cái Ampe kế, sơ cấp biến dòng CT...).
/// </summary>
public readonly record struct InternalConductivePath(
    Terminal TerminalA,
    Terminal TerminalB,
    string PathLabel);

/// <summary>
/// Phần tử tải tiêu thụ điện nội bộ kèm cường độ dòng điện định mức (A) phục vụ Ampe kế &amp; Biến dòng CT.
/// </summary>
public readonly record struct InternalLoadPath(
    Terminal TerminalA,
    Terminal TerminalB,
    string LoadName,
    bool IsActive,
    double CurrentAmps = 0.05,
    CurrentKind CurrentKind = CurrentKind.AC);
