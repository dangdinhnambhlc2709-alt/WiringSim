using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.UI.Controls;

public readonly record struct ComponentHitZone(
    ElectricalComponent Component,
    Rect CanvasBounds,
    string ActionId,
    bool AllowedInDesignMode);

/// <summary>
/// Bộ dựng hình khối Vector WPF độ nét cao (v3.0):
/// - Tự động thích ứng màu sắc Sáng / Tối qua ThemeManager.CanvasColors.
/// - 100% nhãn văn bản trên mặt thiết bị tra cứu qua LocalizationManager (Tiếng Việt / English).
/// - Bao gồm đầy đủ Công tắc hành trình WLCA2, 4 Cảm biến công nghiệp PNP/NPN, Đồng hồ Volt, Ampe (Digital/Analog) &amp; Biến dòng CT.
/// </summary>
public static class ComponentRenderer
{
    private static readonly Typeface UiTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush BurnedBodyBrush = CreateVerticalGradient("#3B181E", "#1F0D12");
    private static readonly Pen SelectedBorderPen = CreatePen("#0284C7", 2.4);
    private static readonly Pen HoveredBorderPen = CreatePen("#38BDF8", 1.9);
    private static readonly Pen BurnedBorderPen = CreatePen("#EF4444", 2.5);

    private static readonly Dictionary<string, Action<DrawingContext, ElectricalComponent, double, List<ComponentHitZone>>> CustomRenderers = new();

    public static void RegisterCustomRenderer(
        string typeKey,
        Action<DrawingContext, ElectricalComponent, double, List<ComponentHitZone>> renderer)
    {
        CustomRenderers[typeKey] = renderer;
    }

    public static void DrawComponent(
        DrawingContext dc,
        ElectricalComponent comp,
        bool isSelected,
        bool isHovered,
        Terminal? hoveredTerminal,
        Terminal? activeWireStartTerminal,
        bool isSimulating,
        double dpiScale,
        List<ComponentHitZone> hitZones)
    {
        var theme = ThemeManager.CanvasColors;

        // Nút rẽ nhánh dây dẫn (Wire T-Junction): chỉ vẽ chấm tròn đặc cùng màu dây (không vẽ hộp chữ nhật)
        if (comp is WireJunctionComponent junc)
        {
            DrawWireJunctionDot(dc, junc, isSelected, isHovered, hoveredTerminal, activeWireStartTerminal, isSimulating, theme);
            return;
        }

        double padTop = comp.Terminals.Any(t => t.Side == TerminalSide.Top) ? 6 : 0;
        double padBot = comp.Terminals.Any(t => t.Side == TerminalSide.Bottom) ? 6 : 0;
        double padLeft = comp.Terminals.Any(t => t.Side == TerminalSide.Left) ? 6 : 0;
        double padRight = comp.Terminals.Any(t => t.Side == TerminalSide.Right) ? 6 : 0;
        Rect bodyRect = new(comp.X + padLeft, comp.Y + padTop, comp.Width - padLeft - padRight, comp.Height - padTop - padBot);

        bool hasTransform = comp.RotationAngle != 0 || comp.IsFlippedHorizontal || comp.IsFlippedVertical;
        List<ComponentHitZone> targetHitZones = hasTransform ? new List<ComponentHitZone>() : hitZones;

        if (hasTransform)
        {
            double cx = comp.X + comp.Width / 2.0;
            double cy = comp.Y + comp.Height / 2.0;
            var tg = new TransformGroup();
            if (comp.IsFlippedHorizontal || comp.IsFlippedVertical)
            {
                tg.Children.Add(new ScaleTransform(
                    comp.IsFlippedHorizontal ? -1.0 : 1.0,
                    comp.IsFlippedVertical ? -1.0 : 1.0,
                    cx, cy));
            }
            if (comp.RotationAngle != 0)
            {
                tg.Children.Add(new RotateTransform(comp.RotationAngle, cx, cy));
            }
            tg.Freeze();
            dc.PushTransform(tg);
        }

        Rect shadowRect = new(bodyRect.X + 2, bodyRect.Y + 3, bodyRect.Width, bodyRect.Height);
        dc.DrawRoundedRectangle(CreateSolid(theme.IsDark ? "#55000000" : "#220F172A"), null, shadowRect, 8, 8);

        Brush bodyFill = comp.IsBurnedOut
            ? BurnedBodyBrush
            : CreateVerticalGradient(theme.BodyTopHex, theme.BodyBottomHex);

        Pen borderPen = comp.IsBurnedOut
            ? BurnedBorderPen
            : (isSelected ? SelectedBorderPen : (isHovered ? HoveredBorderPen : CreatePen(theme.BorderDefaultHex, 1.5)));

        dc.DrawRoundedRectangle(bodyFill, borderPen, bodyRect, 8, 8);

        switch (comp)
        {
            case DcSourceComponent dcSrc:
                DrawDcSource(dc, dcSrc, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case Ac1PhaseSourceComponent ac1:
                DrawAc1PhaseSource(dc, ac1, bodyRect, theme, isSimulating, dpiScale);
                break;
            case Ac3PhaseSourceComponent ac3:
                DrawAc3PhaseSource(dc, ac3, bodyRect, theme, isSimulating, dpiScale);
                break;
            case Mcb1PhaseComponent mcb1:
                DrawMcb(dc, mcb1, mcb1.IsOn, mcb1.IsTripped, 2, $"MCB 1P • {mcb1.CurveType}{mcb1.RatedAmps}", bodyRect, theme, dpiScale, targetHitZones);
                break;
            case Mcb3PhaseComponent mcb3:
                DrawMcb(dc, mcb3, mcb3.IsOn, mcb3.IsTripped, 3, $"MCB 3P • {mcb3.CurveType}{mcb3.RatedAmps}", bodyRect, theme, dpiScale, targetHitZones);
                break;
            case SwitchComponent sw:
                DrawSwitch(dc, sw, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case PushButtonComponent pb:
                DrawPushButton(dc, pb, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case IlluminatedPushButtonComponent ipb:
                DrawIlluminatedPushButton(dc, ipb, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case EmergencyStopComponent estop:
                DrawEmergencyStop(dc, estop, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case FuseComponent fuse:
                DrawFuse(dc, fuse, bodyRect, theme, dpiScale);
                break;
            case KnifeSwitchComponent knife:
                DrawKnifeSwitch(dc, knife, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case CamSwitchComponent cam:
                DrawCamSwitch(dc, cam, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case LimitSwitchComponent ls:
                DrawLimitSwitch(dc, ls, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case VfdComponent vfd:
                DrawVfd(dc, vfd, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case ContactorComponent km:
                DrawContactor(dc, km, bodyRect, theme, dpiScale);
                break;
            case IntermediateRelayComponent my:
                DrawIntermediateRelay(dc, my, bodyRect, theme, dpiScale);
                break;
            case ThermalOverloadRelayComponent rn:
                DrawThermalRelay(dc, rn, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case TimerOnDelayComponent timer:
                DrawTimerH3Cr(dc, timer, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case RotaryTimerAh3Component ah3:
                DrawRotaryTimerAh3(dc, ah3, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case IndustrialSensorComponent sensor:
                DrawIndustrialSensor(dc, sensor, bodyRect, theme, dpiScale, targetHitZones);
                break;
            case VoltmeterComponent vm:
                DrawPanelMeter(dc, bodyRect, "V", vm.MeasureKind, vm.GetRangeSpec().Label, vm.DisplayStyle,
                    vm.MeasuredVoltage, vm.GetRangeSpec().MaxVolts, vm.IsOverRange, vm.NeedleRatio, "#38BDF8", theme, dpiScale);
                break;
            case AmmeterComponent am:
                DrawPanelMeter(dc, bodyRect, "A", am.MeasureKind, am.GetRangeSpec().Label, am.DisplayStyle,
                    am.DisplayedAmps, am.GetRangeSpec().MaxDisplayAmps, am.IsOverRange, am.NeedleRatio, "#F472B6", theme, dpiScale);
                break;
            case CurrentTransformerComponent ct:
                DrawCurrentTransformer(dc, ct, bodyRect, theme, dpiScale);
                break;
            case PilotLightComponent pilot:
                DrawPilotLight(dc, pilot, bodyRect, theme, dpiScale);
                break;
            case ResidentialLampComponent lamp:
                DrawResidentialLamp(dc, lamp, bodyRect, theme, dpiScale);
                break;
            case Motor1PhaseComponent m1:
                DrawMotor1Phase(dc, m1, bodyRect, theme, dpiScale);
                break;
            case MotorLinearTrackComponent track:
                DrawMotorLinearTrack(dc, track, bodyRect, theme, dpiScale);
                break;
            case Motor3PhaseBaseComponent m3:
                DrawMotor3Phase(dc, m3, bodyRect, theme, dpiScale);
                break;
            default:
                if (CustomRenderers.TryGetValue(comp.TypeKey, out var custom))
                    custom(dc, comp, dpiScale, targetHitZones);
                else
                    DrawGenericModularBlock(dc, comp, bodyRect, theme, dpiScale);
                break;
        }

        if (hasTransform)
        {
            dc.Pop();
            foreach (var z in targetHitZones)
            {
                hitZones.Add(z with { CanvasBounds = comp.TransformCanvasRectBySelf(z.CanvasBounds) });
            }
        }

        // Nhãn tên thiết bị & cảnh báo cháy luôn vẽ ngoài phép xoay/lật để chữ đứng thẳng, dễ đọc!
        Rect visualBounds = comp.GetVisualBounds();
        DrawComponentNameTag(dc, comp, visualBounds, theme, dpiScale);

        if (comp.IsBurnedOut)
        {
            string burnKey = comp switch
            {
                PilotLightComponent or ResidentialLampComponent or IlluminatedPushButtonComponent => "Str.Vis.BurnedLamp",
                IndustrialSensorComponent => "Str.Vis.BurnedSensor",
                _ => "Str.Vis.BurnedCoil"
            };
            DrawBurnoutBadge(dc, visualBounds, LocalizationManager.Get(burnKey), dpiScale);
        }

        foreach (var term in comp.Terminals)
        {
            DrawTerminal(dc, term, term == hoveredTerminal, term == activeWireStartTerminal, isSimulating, theme);
        }
    }

    private static void DrawBurnoutBadge(DrawingContext dc, Rect body, string text, double dpiScale)
    {
        var ft = FormatText(text, 9.2, BoldTypeface, Brushes.White, dpiScale);
        double w = Math.Min(body.Width - 6, ft.Width + 12);
        Rect badge = new(body.X + (body.Width - w) / 2.0, body.Y + (body.Height - 20) / 2.0, w, 20);
        dc.DrawRoundedRectangle(CreateSolid("#F0B91C1C"), CreatePen("#FEF08A", 1.5), badge, 4, 4);
        dc.DrawText(ft, new Point(body.X + (body.Width - ft.Width) / 2.0, badge.Y + 2.5));
    }

    private static void DrawSupplyMismatchPill(DrawingContext dc, Rect body, RatedSupplyStatus status, double dpiScale)
    {
        if (status is not (RatedSupplyStatus.WrongCurrentKind or RatedSupplyStatus.Undervoltage))
            return;

        string msg = status == RatedSupplyStatus.WrongCurrentKind
            ? LocalizationManager.Get("Str.Vis.WrongKind")
            : LocalizationManager.Get("Str.Vis.LowVolt");
        var ft = FormatText(msg, 8.2, BoldTypeface, CreateSolid("#FDE047"), dpiScale);
        Rect pill = new(body.Right - ft.Width - 12, body.Top + 4, ft.Width + 8, 15);
        dc.DrawRoundedRectangle(CreateSolid("#DD78350F"), CreatePen("#F59E0B", 1.0), pill, 3, 3);
        dc.DrawText(ft, new Point(pill.X + 4, pill.Y + 1));
    }

    private static void DrawComponentNameTag(DrawingContext dc, ElectricalComponent comp, Rect visualBounds, CanvasThemeColors theme, double dpiScale)
    {
        var ft = FormatText(comp.DisplayName, 10.5, BoldTypeface, CreateSolid(theme.NameTagTextHex), dpiScale);

        bool topHasTerminals = comp.Terminals.Any(t => t.EffectiveSide == TerminalSide.Top);
        bool leftHasTerminals = comp.Terminals.Any(t => t.EffectiveSide == TerminalSide.Left);

        if (!topHasTerminals)
        {
            double labelX = visualBounds.X + (visualBounds.Width - ft.Width) / 2.0;
            double labelY = visualBounds.Top - 21;
            Rect tagBg = new(labelX - 5, labelY - 1, ft.Width + 10, 18);
            dc.DrawRoundedRectangle(CreateSolid(theme.NameTagBgHex), CreatePen(theme.BorderDefaultHex, 0.9), tagBg, 4, 4);
            dc.DrawText(ft, new Point(labelX, labelY + 1));
        }
        else if (!leftHasTerminals)
        {
            Rect tagBg = new(visualBounds.X - ft.Width - 14, visualBounds.Y + (visualBounds.Height - 19) / 2.0, ft.Width + 10, 19);
            dc.DrawRoundedRectangle(CreateSolid(theme.NameTagBgHex), CreatePen(theme.BorderDefaultHex, 1.0), tagBg, 4, 4);
            dc.DrawText(ft, new Point(tagBg.X + 5, tagBg.Y + 2));
        }
        else
        {
            Rect tagBg = new(visualBounds.X + (visualBounds.Width - ft.Width - 10) / 2.0, visualBounds.Bottom + 5, ft.Width + 10, 19);
            dc.DrawRoundedRectangle(CreateSolid(theme.NameTagBgHex), CreatePen(theme.BorderDefaultHex, 1.0), tagBg, 4, 4);
            dc.DrawText(ft, new Point(tagBg.X + 5, tagBg.Y + 2));
        }
    }

    // =========================================================================
    // 1. NGUỒN CẤP & KHÍ CỤ ĐÓNG CẮT
    // =========================================================================

    private static void DrawDcSource(DrawingContext dc, DcSourceComponent src, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        dc.DrawRoundedRectangle(CreateSolid("#EF4444"), null, new Rect(body.X + 8, body.Y + 7, body.Width - 16, 4), 2, 2);
        dc.DrawEllipse(CreateSolid("#22C55E"), CreatePen("#86EFAC", 1), new Point(body.Right - 14, body.Y + 20), 3.5, 3.5);
        dc.DrawText(FormatText("DC PSU", 9.5, BoldTypeface, CreateSolid(theme.SubTextHex), dpiScale), new Point(body.X + 10, body.Y + 13));

        Rect screen = new(body.X + 30, body.Y + 31, body.Width - 60, 28);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#334155", 1.2), screen, 5, 5);

        var voltText = FormatText($"{src.OutputVoltage:0}VDC", 13.0, MonoTypeface, CreateSolid("#38BDF8"), dpiScale);
        dc.DrawText(voltText, new Point(screen.X + (screen.Width - voltText.Width) / 2, screen.Y + 6));

        Rect btnMinus = new(body.X + 7, body.Y + 33, 20, 24);
        Rect btnPlus = new(body.Right - 27, body.Y + 33, 20, 24);
        DrawMiniStepButton(dc, btnMinus, "-", dpiScale);
        DrawMiniStepButton(dc, btnPlus, "+", dpiScale);

        hitZones.Add(new ComponentHitZone(src, btnMinus, "DC_MINUS", AllowedInDesignMode: true));
        hitZones.Add(new ComponentHitZone(src, btnPlus, "DC_PLUS", AllowedInDesignMode: true));
        hitZones.Add(new ComponentHitZone(src, screen, "DC_CYCLE", AllowedInDesignMode: true));

        dc.DrawText(FormatText("+", 12, BoldTypeface, CreateSolid("#EF4444"), dpiScale), new Point(src.X + src.PlusTerminal.RelX - 4, body.Bottom - 20));
        dc.DrawText(FormatText("−", 12, BoldTypeface, CreateSolid(theme.SubTextHex), dpiScale), new Point(src.X + src.MinusTerminal.RelX - 4, body.Bottom - 20));
    }

    private static void DrawAc1PhaseSource(DrawingContext dc, Ac1PhaseSourceComponent src, Rect body, CanvasThemeColors theme, bool isSimulating, double dpiScale)
    {
        dc.DrawRoundedRectangle(CreateSolid("#F59E0B"), null, new Rect(body.X + 8, body.Y + 7, body.Width - 16, 4), 2, 2);
        Brush led = src.IsPhaseL1Lost ? CreateSolid("#EF4444") : (isSimulating ? CreateSolid("#22C55E") : CreateSolid("#64748B"));
        dc.DrawEllipse(led, null, new Point(body.Right - 14, body.Y + 20), 4, 4);

        dc.DrawText(FormatText("AC 1~", 9.5, BoldTypeface, CreateSolid("#D97706"), dpiScale), new Point(body.X + 10, body.Y + 13));

        Rect screen = new(body.X + 10, body.Y + 31, body.Width - 20, 28);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(src.IsPhaseL1Lost ? "#EF4444" : "#334155", 1.0), screen, 5, 5);
        string label = src.IsPhaseL1Lost ? LocalizationManager.Get("Str.Vis.LostLine") : $"{src.OutputVoltage:0}V ~ 50Hz";
        var txt = FormatText(label, 11.0, MonoTypeface, CreateSolid(src.IsPhaseL1Lost ? "#FCA5A5" : "#FDE68A"), dpiScale);
        dc.DrawText(txt, new Point(screen.X + (screen.Width - txt.Width) / 2, screen.Y + 6));

        dc.DrawText(FormatText("L", 11, BoldTypeface, CreateSolid("#EF4444"), dpiScale), new Point(src.X + src.LineTerminal.RelX - 3.5, body.Bottom - 19));
        dc.DrawText(FormatText("N", 11, BoldTypeface, CreateSolid("#2563EB"), dpiScale), new Point(src.X + src.NeutralTerminal.RelX - 4.5, body.Bottom - 19));
    }

    private static void DrawAc3PhaseSource(DrawingContext dc, Ac3PhaseSourceComponent src, Rect body, CanvasThemeColors theme, bool isSimulating, double dpiScale)
    {
        dc.DrawRoundedRectangle(CreateSolid("#F59E0B"), null, new Rect(body.X + 10, body.Y + 7, body.Width - 20, 4), 2, 2);
        bool anyLost = src.IsL1Lost || src.IsL2Lost || src.IsL3Lost;
        Brush led = anyLost ? CreateSolid("#EF4444") : (isSimulating ? CreateSolid("#22C55E") : CreateSolid("#64748B"));
        dc.DrawEllipse(led, null, new Point(body.Right - 16, body.Y + 20), 4.5, 4.5);

        double phaseV = Math.Round(src.LineVoltage / Math.Sqrt(3.0), 0);
        dc.DrawText(FormatText($"3~ {src.LineVoltage:0}V/{phaseV:0}V • 50Hz", 10.5, BoldTypeface, CreateSolid(theme.IsDark ? "#FDE68A" : "#B45309"), dpiScale), new Point(body.X + 12, body.Y + 13));

        (Terminal Term, string ColorHex, bool IsLost)[] bars =
        [
            (src.L1Terminal, "#EF4444", src.IsL1Lost),
            (src.L2Terminal, "#CA8A04", src.IsL2Lost),
            (src.L3Terminal, "#2563EB", src.IsL3Lost),
            (src.NeutralTerminal, "#64748B", false),
            (src.PeTerminal, "#16A34A", false)
        ];

        foreach (var (term, hex, isLost) in bars)
        {
            double cx = src.X + term.RelX;
            Pen barPen = isLost ? new Pen(CreateSolid("#EF4444"), 2.0) { DashStyle = DashStyles.Dot } : CreatePen(hex, 3.0);
            if (isLost) barPen.Freeze();

            dc.DrawLine(barPen, new Point(cx, body.Y + 38), new Point(cx, body.Bottom - 4));
            Rect box = new(cx - 13, body.Y + 36, 26, 18);
            dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(isLost ? "#EF4444" : hex, 1.1), box, 3, 3);

            string tag = isLost ? "✕" : term.Name;
            var lbl = FormatText(tag, 9.5, BoldTypeface, CreateSolid(isLost ? "#EF4444" : (hex == "#CA8A04" ? "#FACC15" : hex)), dpiScale);
            dc.DrawText(lbl, new Point(cx - lbl.Width / 2, body.Y + 38));
        }
    }

    private static void DrawMcb(DrawingContext dc, ElectricalComponent comp, bool isOn, bool isTripped, int poles, string specTitle, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        for (int p = 1; p < poles; p++)
        {
            double xDiv = body.X + (body.Width / poles) * p;
            dc.DrawLine(CreatePen(theme.BorderDefaultHex, 1.0), new Point(xDiv, body.Top + 4), new Point(xDiv, body.Bottom - 4));
        }

        var specTxt = FormatText(specTitle, 9.0, BoldTypeface, CreateSolid(theme.IsDark ? "#93C5FD" : "#0369A1"), dpiScale);
        dc.DrawText(specTxt, new Point(body.X + (body.Width - specTxt.Width) / 2, body.Y + 8));

        Rect statusWin = new(body.X + 8, body.Y + 26, body.Width - 16, 18);
        string statusHex = isTripped ? "#EA580C" : (isOn ? "#DC2626" : "#16A34A");
        string statusStr = isTripped ? "⚡ TRIP" : (isOn ? "I  •  ON" : "O • OFF");
        dc.DrawRoundedRectangle(CreateSolid(statusHex), CreatePen("#E2E8F0", 0.8), statusWin, 3, 3);

        var stTxt = FormatText(statusStr, 9.2, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(stTxt, new Point(statusWin.X + (statusWin.Width - stTxt.Width) / 2, statusWin.Y + 2));

        Rect leverSlot = new(body.X + 8, body.Y + 49, body.Width - 16, 38);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#334155", 1.2), leverSlot, 4, 4);

        double handleY = isOn ? leverSlot.Y + 4 : (isTripped ? leverSlot.Y + 13 : leverSlot.Bottom - 16);
        Brush handleBrush = isTripped ? CreateSolid("#F97316") : (isOn ? CreateSolid("#38BDF8") : CreateSolid("#64748B"));
        Rect handleBar = new(leverSlot.X + 4, handleY, leverSlot.Width - 8, 12);
        dc.DrawRoundedRectangle(handleBrush, CreatePen("#F8FAFC", 1.0), handleBar, 3, 3);

        hitZones.Add(new ComponentHitZone(comp, leverSlot, "MCB_TOGGLE", AllowedInDesignMode: false));
    }

    private static void DrawSwitch(DrawingContext dc, SwitchComponent sw, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        if (sw.SwitchKind == SwitchKind.OneWay_1_2)
        {
            bool on = sw.Position == 1;
            Rect plate = new(body.X + 10, body.Y + 10, body.Width - 20, 56);
            dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(on ? "#38BDF8" : "#475569", 1.4), plate, 6, 6);
            dc.DrawRoundedRectangle(CreateSolid(on ? "#38BDF8" : "#475569"), null, new Rect(plate.X + 14, plate.Y + 8, plate.Width - 28, 4), 2, 2);

            Point cLeft = new(plate.X + 14, plate.Y + 34);
            Point cRight = new(plate.Right - 14, plate.Y + 34);
            Point bladeTip = on ? cRight : new Point(plate.Right - 16, plate.Y + 21);
            dc.DrawLine(CreatePen(on ? "#4ADE80" : "#94A3B8", 2.2), cLeft, bladeTip);
            dc.DrawEllipse(Brushes.White, null, cLeft, 2.5, 2.5);
            dc.DrawEllipse(Brushes.White, null, cRight, 2.5, 2.5);

            string stText = $"1-2: {(on ? LocalizationManager.Get("Str.Vis.Closed") : LocalizationManager.Get("Str.Vis.Open"))}";
            var st = FormatText(stText, 8.8, BoldTypeface, CreateSolid(on ? "#0284C7" : theme.SubTextHex), dpiScale);
            dc.DrawText(st, new Point(body.X + (body.Width - st.Width) / 2, body.Bottom - 22));

            hitZones.Add(new ComponentHitZone(sw, plate, "SW_STEP", AllowedInDesignMode: false));
        }
        else if (sw.SwitchKind == SwitchKind.TwoWay_Staircase)
        {
            Rect plate = new(body.X + 10, body.Y + 10, body.Width - 20, body.Height - 20);
            dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#475569", 1.3), plate, 6, 6);

            bool to2 = sw.Position == 2;
            Point pCom = new(sw.X + sw.ComOrInTerminal!.RelX, plate.Y + 14);
            Point p1 = new(sw.X + sw.Out1Terminal!.RelX + 6, plate.Bottom - 14);
            Point p2 = new(sw.X + sw.Out2Terminal!.RelX - 6, plate.Bottom - 14);

            dc.DrawLine(CreatePen("#475569", 1.4), pCom, to2 ? p1 : p2);
            dc.DrawLine(CreatePen("#22D3EE", 2.6), pCom, to2 ? p2 : p1);

            dc.DrawEllipse(Brushes.White, null, pCom, 3, 3);
            dc.DrawEllipse(CreateSolid(to2 ? "#64748B" : "#22D3EE"), null, p1, 3, 3);
            dc.DrawEllipse(CreateSolid(to2 ? "#22D3EE" : "#64748B"), null, p2, 3, 3);

            var tag = FormatText(to2 ? "COM → L2" : "COM → L1", 8.8, BoldTypeface, CreateSolid("#67E8F9"), dpiScale);
            dc.DrawText(tag, new Point(plate.X + 6, plate.Y + 4));

            hitZones.Add(new ComponentHitZone(sw, plate, "SW_STEP", AllowedInDesignMode: false));
        }
        else
        {
            Point center = new(body.X + body.Width / 2.0, body.Y + body.Height / 2.0 + 4);

            var ft1 = FormatText("1(MAN)", 8.3, BoldTypeface, CreateSolid(sw.Position == 1 ? "#16A34A" : theme.SubTextHex), dpiScale);
            var ft0 = FormatText("0", 9.0, BoldTypeface, CreateSolid(sw.Position == 0 ? "#D97706" : theme.SubTextHex), dpiScale);
            var ft2 = FormatText("2(AUTO)", 8.3, BoldTypeface, CreateSolid(sw.Position == 2 ? "#0284C7" : theme.SubTextHex), dpiScale);

            dc.DrawText(ft1, new Point(body.X + 7, body.Y + 9));
            dc.DrawText(ft0, new Point(center.X - ft0.Width / 2, body.Y + 5));
            dc.DrawText(ft2, new Point(body.Right - ft2.Width - 7, body.Y + 9));

            dc.DrawEllipse(CreateSolid(theme.InsetPanelHex), CreatePen("#64748B", 2.0), center, 22, 22);

            double angle = sw.Position switch { 1 => -42.0, 2 => +42.0, _ => 0.0 };
            dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
            Rect selectorBar = new(center.X - 4.5, center.Y - 19, 9, 38);
            dc.DrawRoundedRectangle(CreateSolid("#334155"), CreatePen("#E2E8F0", 1.2), selectorBar, 3, 3);
            dc.DrawRoundedRectangle(CreateSolid("#38BDF8"), null, new Rect(center.X - 2, center.Y - 17, 4, 13), 1.5, 1.5);
            dc.Pop();

            hitZones.Add(new ComponentHitZone(sw, new Rect(body.X + 4, body.Y + 8, 36, body.Height - 16), "SW_ROT_1", AllowedInDesignMode: false));
            hitZones.Add(new ComponentHitZone(sw, new Rect(center.X - 16, body.Y + 8, 32, body.Height - 16), "SW_ROT_0", AllowedInDesignMode: false));
            hitZones.Add(new ComponentHitZone(sw, new Rect(body.Right - 40, body.Y + 8, 36, body.Height - 16), "SW_ROT_2", AllowedInDesignMode: false));
        }
    }

    private static void DrawPushButton(DrawingContext dc, PushButtonComponent pb, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        Point center = new(body.X + body.Width / 2.0, body.Y + body.Height / 2.0);
        dc.DrawEllipse(CreateSolid("#0F172A"), CreatePen("#64748B", 2.2), center, 23, 23);

        double pressOffset = pb.IsPressed ? 3.0 : 0.0;
        double capRadius = pb.IsPressed ? 15.5 : 17.5;

        (string normalHex, string pressedHex, string ringHex) = pb.ButtonColor switch
        {
            PilotLightColor.Red => ("#EF4444", "#B91C1C", "#FCA5A5"),
            PilotLightColor.Yellow => ("#EAB308", "#A16207", "#FEF08A"),
            PilotLightColor.Blue => ("#3B82F6", "#1D4ED8", "#93C5FD"),
            _ => ("#22C55E", "#15803D", "#86EFAC")
        };

        if (!pb.IsPressed)
            dc.DrawEllipse(CreateSolid("#55000000"), null, new Point(center.X, center.Y + 3), capRadius, capRadius);

        Point capCenter = new(center.X, center.Y + pressOffset);
        dc.DrawEllipse(CreateSolid(pb.IsPressed ? pressedHex : normalHex), CreatePen(ringHex, 1.5), capCenter, capRadius, capRadius);

        string cfgLabel = pb.ContactConfig switch
        {
            PushButtonContactConfig.Only1NC => "NC",
            PushButtonContactConfig.Dual1NO1NC => "LA38",
            _ => "NO"
        };
        var ft = FormatText(cfgLabel, 8.8, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(ft, new Point(capCenter.X - ft.Width / 2, capCenter.Y - ft.Height / 2));

        // Nhãn chân chuẩn LA38: 11-12 (NC) và 23-24 (NO)
        if (pb.NcIn1 is not null && pb.NcOut2 is not null)
        {
            var t11 = FormatText("11", 7.6, BoldTypeface, CreateSolid("#FB923C"), dpiScale);
            var t12 = FormatText("12", 7.6, BoldTypeface, CreateSolid("#FB923C"), dpiScale);
            dc.DrawText(t11, new Point(pb.X + pb.NcIn1.RelX - t11.Width / 2, body.Top + 2));
            dc.DrawText(t12, new Point(pb.X + pb.NcOut2.RelX - t12.Width / 2, body.Bottom - 12));
        }
        if (pb.NoIn3 is not null && pb.NoOut4 is not null)
        {
            var t23 = FormatText("23", 7.6, BoldTypeface, CreateSolid("#4ADE80"), dpiScale);
            var t24 = FormatText("24", 7.6, BoldTypeface, CreateSolid("#4ADE80"), dpiScale);
            dc.DrawText(t23, new Point(pb.X + pb.NoIn3.RelX - t23.Width / 2, body.Top + 2));
            dc.DrawText(t24, new Point(pb.X + pb.NoOut4.RelX - t24.Width / 2, body.Bottom - 12));
        }

        if (pb.ActionMode == PushButtonActionMode.Latching)
        {
            var latchFt = FormatText(LocalizationManager.Get("Str.Vis.Latch"), 7.2, BoldTypeface, CreateSolid("#F59E0B"), dpiScale);
            dc.DrawText(latchFt, new Point(center.X - latchFt.Width / 2, body.Top + 2));
        }

        Rect hitRect = new(center.X - 24, center.Y - 24, 48, 48);
        string action = pb.ActionMode == PushButtonActionMode.Latching ? "PUSHBUTTON_LATCH" : "PUSHBUTTON_HOLD";
        hitZones.Add(new ComponentHitZone(pb, hitRect, action, AllowedInDesignMode: false));
    }

    // =========================================================================
    // 2. CÔNG TẮC HÀNH TRÌNH (ME-8104) & CÁC THIẾT BỊ v1.1
    // =========================================================================

    private static void DrawLimitSwitch(DrawingContext dc, LimitSwitchComponent ls, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        // Vỏ kim loại đúc xanh-đen đặc trưng kiểu ME-8104
        Rect shell = new(body.X + 4, body.Y + 20, body.Width - 8, body.Height - 24);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#1E3A5F", "#0F172A"), CreatePen("#38BDF8", 1.1), shell, 5, 5);

        Rect inner = new(body.X + 8, body.Y + 34, body.Width - 16, body.Height - 44);
        dc.DrawRoundedRectangle(CreateSolid("#0B1120"), CreatePen("#475569", 1.1), inner, 4, 4);

        // Tiếp điểm NC (1-2) trái và NO (3-4) phải
        DrawAuxPairColumn(dc, ls.X + ls.Nc1.RelX, inner, !ls.IsActuated, "1-2 NC", "#FB923C", dpiScale);
        DrawAuxPairColumn(dc, ls.X + ls.No3.RelX, inner, ls.IsActuated, "3-4 NO", "#34D399", dpiScale);

        // Đầu trục xoay kim loại ME-8104 & cần gạt bánh xe con lăn phía trên
        Rect turret = new(body.X + body.Width / 2.0 - 14, body.Y + 8, 28, 16);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#475569", "#1E293B"), CreatePen("#94A3B8", 1.1), turret, 3, 3);

        Point pivot = new(body.X + body.Width / 2.0, body.Y + 16);
        double armAngle = ls.IsActuated ? 30.0 : -26.0;

        dc.PushTransform(new RotateTransform(armAngle, pivot.X, pivot.Y));
        dc.DrawRoundedRectangle(CreateVerticalGradient("#E2E8F0", "#94A3B8"), CreatePen("#334155", 1.0), new Rect(pivot.X - 3, pivot.Y - 3, 36, 6), 2, 2);
        // Bánh xe con lăn cao su đen viền cam/xanh
        dc.DrawEllipse(CreateSolid(ls.IsActuated ? "#22C55E" : "#1E293B"), CreatePen(ls.IsActuated ? "#86EFAC" : "#F97316", 2.4), new Point(pivot.X + 33, pivot.Y), 7.5, 7.5);
        dc.DrawEllipse(CreateSolid("#CBD5E1"), null, new Point(pivot.X + 33, pivot.Y), 2.5, 2.5);
        dc.Pop();

        dc.DrawEllipse(CreateSolid("#64748B"), CreatePen("#E2E8F0", 1.2), pivot, 5.0, 5.0);

        // Đèn LED báo tác động
        Point ledPt = new(body.X + body.Width / 2.0, inner.Bottom - 9);
        if (ls.IsActuated)
        {
            dc.DrawEllipse(CreateRadialGlow("#AA22C55E", "#0022C55E"), null, ledPt, 9, 9);
            dc.DrawEllipse(CreateSolid("#4ADE80"), null, ledPt, 3.8, 3.8);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#334155"), null, ledPt, 3.2, 3.2);
        }

        string action = ls.ActionMode == PushButtonActionMode.Latching ? "LIMIT_TOGGLE" : "LIMIT_HOLD";
        hitZones.Add(new ComponentHitZone(ls, new Rect(body.X, body.Y, body.Width, body.Height), action, AllowedInDesignMode: false));
    }

    private static void DrawWireJunctionDot(
        DrawingContext dc,
        WireJunctionComponent junc,
        bool isSelected,
        bool isHovered,
        Terminal? hoveredTerminal,
        Terminal? activeWireStartTerminal,
        bool isSimulating,
        CanvasThemeColors theme)
    {
        Point p = new(junc.NodeTerminal.CanvasX, junc.NodeTerminal.CanvasY);
        var (baseHex, brightHex, glowHex) = WiringCanvasControl.GetWirePaletteColors(junc.DotColorPreset, theme.IsDark);

        bool isHot = isSimulating && junc.NodeTerminal.IsEnergized;
        bool isActive = isHovered || hoveredTerminal == junc.NodeTerminal || activeWireStartTerminal == junc.NodeTerminal;

        if (isHot)
            dc.DrawEllipse(CreateRadialGlow(glowHex, "#00000000"), null, p, 12, 12);
        else if (isSelected || isActive)
            dc.DrawEllipse(CreateRadialGlow("#8838BDF8", "#0038BDF8"), null, p, 12, 12);

        double radius = isActive || isSelected ? 6.4 : 5.2;
        string fillHex = isHot ? brightHex : baseHex;
        Pen? ringPen = isSelected || isActive
            ? CreatePen("#38BDF8", 1.8)
            : CreatePen(theme.IsDark ? "#0B0F17" : "#FFFFFF", 1.2);

        dc.DrawEllipse(CreateSolid(fillHex), ringPen, p, radius, radius);
    }

    private static void DrawIlluminatedPushButton(DrawingContext dc, IlluminatedPushButtonComponent ipb, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        Point center = new(body.X + body.Width / 2.0, body.Y + body.Height / 2.0);
        bool lit = ipb.IsLit && !ipb.IsBurnedOut;

        (string dimHex, string litHex, string glowHex, string coreHex) = ipb.ButtonColor switch
        {
            PilotLightColor.Red => ("#7F1D1D", "#EF4444", "#CCEF4444", "#FEE2E2"),
            PilotLightColor.Yellow => ("#713F12", "#FACC15", "#CCFACC15", "#FEF9C3"),
            PilotLightColor.Blue => ("#1E3A8A", "#3B82F6", "#CC3B82F6", "#DBEAFE"),
            PilotLightColor.White => ("#475569", "#F8FAFC", "#CCF8FAFC", "#FFFFFF"),
            _ => ("#14532D", "#22C55E", "#CC22C55E", "#DCFCE7")
        };

        if (lit)
            dc.DrawEllipse(CreateRadialGlow(glowHex, "#00000000"), null, center, 42, 42);

        dc.DrawEllipse(CreateSolid("#0F172A"), CreatePen("#94A3B8", 2.2), center, 24, 24);
        double offset = ipb.IsPressed ? 2.5 : 0.0;
        double capR = ipb.IsPressed ? 17.0 : 19.0;
        Point capCenter = new(center.X, center.Y + offset);

        Brush capFill = lit ? CreateRadialGlow(coreHex, litHex) : CreateSolid(dimHex);
        dc.DrawEllipse(capFill, CreatePen(lit ? "#FFFFFF" : "#64748B", lit ? 1.8 : 1.2), capCenter, capR, capR);

        if (lit)
            dc.DrawEllipse(CreateSolid("#99FFFFFF"), null, new Point(capCenter.X - 5, capCenter.Y - 5), 6, 6);

        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(ipb.VoltageRating);
        var vFt = FormatText(vSpec, 7.6, BoldTypeface, CreateSolid(lit ? "#0F172A" : "#E2E8F0"), dpiScale);
        dc.DrawText(vFt, new Point(capCenter.X - vFt.Width / 2, capCenter.Y - vFt.Height / 2));

        // Nhãn 6 chân: 11-12 (NC), 23-24 (NO), X1-X2 (Đèn)
        DrawPinLabel(dc, ipb.X + ipb.Nc11.RelX, body.Top + 2, "11", "#FB923C", dpiScale);
        DrawPinLabel(dc, ipb.X + ipb.No23.RelX, body.Top + 2, "23", "#4ADE80", dpiScale);
        DrawPinLabel(dc, ipb.X + ipb.LampX1.RelX, body.Top + 2, "X1", "#38BDF8", dpiScale);

        DrawPinLabel(dc, ipb.X + ipb.Nc12.RelX, body.Bottom - 12, "12", "#FB923C", dpiScale);
        DrawPinLabel(dc, ipb.X + ipb.No24.RelX, body.Bottom - 12, "24", "#4ADE80", dpiScale);
        DrawPinLabel(dc, ipb.X + ipb.LampX2.RelX, body.Bottom - 12, "X2", "#38BDF8", dpiScale);

        if (ipb.ActionMode == PushButtonActionMode.Latching)
        {
            var lFt = FormatText(LocalizationManager.Get("Str.Vis.Latch"), 7.2, BoldTypeface, CreateSolid("#F59E0B"), dpiScale);
            dc.DrawText(lFt, new Point(body.X + 5, body.Y + 16));
        }

        DrawSupplyMismatchPill(dc, body, ipb.SupplyStatus, dpiScale);

        Rect hitRect = new(center.X - 25, center.Y - 25, 50, 50);
        string act = ipb.ActionMode == PushButtonActionMode.Latching ? "ILLUM_PB_LATCH" : "ILLUM_PB_HOLD";
        hitZones.Add(new ComponentHitZone(ipb, hitRect, act, AllowedInDesignMode: false));
    }

    private static void DrawEmergencyStop(DrawingContext dc, EmergencyStopComponent es, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        // Đế an toàn màu vàng công nghiệp nổi bật
        Rect yellowBase = new(body.X + 4, body.Y + 12, body.Width - 8, body.Height - 24);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#FACC15", "#CA8A04"), CreatePen("#854D0E", 1.8), yellowBase, 7, 7);

        Point center = new(body.X + body.Width / 2.0, body.Y + body.Height / 2.0);
        double capR = es.IsEngaged ? 22.0 : 26.0;

        if (!es.IsEngaged)
            dc.DrawEllipse(CreateSolid("#66000000"), null, new Point(center.X, center.Y + 3.5), capR, capR);

        // Đầu nấm màu đỏ lớn + vòng cảnh báo khi đang khóa
        dc.DrawEllipse(CreateSolid("#7F1D1D"), CreatePen("#FDE047", 1.8), center, capR + 3, capR + 3);
        dc.DrawEllipse(CreateVerticalGradient(es.IsEngaged ? "#B91C1C" : "#EF4444", "#991B1B"), CreatePen("#FCA5A5", 1.6), center, capR, capR);

        string capTxt = es.IsEngaged ? "↻ LOCK" : "E-STOP";
        var ft = FormatText(capTxt, 8.6, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));

        // Nhãn chân
        DrawPinLabel(dc, es.X + es.Nc11.RelX, body.Top + 1, "11", "#FB923C", dpiScale);
        DrawPinLabel(dc, es.X + es.Nc12.RelX, body.Bottom - 12, "12", "#FB923C", dpiScale);
        if (es.SecondTop is not null && es.SecondBot is not null)
        {
            string col = es.ContactConfig == EmergencyStopContactConfig.Dual2NC ? "#FB923C" : "#4ADE80";
            DrawPinLabel(dc, es.X + es.SecondTop.RelX, body.Top + 1, es.SecondTop.Id, col, dpiScale);
            DrawPinLabel(dc, es.X + es.SecondBot.RelX, body.Bottom - 12, es.SecondBot.Id, col, dpiScale);
        }

        hitZones.Add(new ComponentHitZone(es, yellowBase, "ESTOP_TOGGLE", AllowedInDesignMode: false));
    }

    private static void DrawFuse(DrawingContext dc, FuseComponent fuse, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        double cx = body.X + body.Width / 2.0;

        // Đầu chụp đồng trên & dưới
        Rect capTop = new(cx - 11, body.Top + 10, 22, 14);
        Rect capBot = new(cx - 11, body.Bottom - 24, 22, 14);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#CBD5E1", "#64748B"), CreatePen("#334155", 1.0), capTop, 3, 3);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#CBD5E1", "#64748B"), CreatePen("#334155", 1.0), capBot, 3, 3);

        // Ống thủy tinh trong suốt ở giữa
        Rect glass = new(cx - 9, capTop.Bottom, 18, capBot.Top - capTop.Bottom);
        dc.DrawRectangle(CreateSolid(fuse.IsBlown ? "#44EF4444" : "#33E2E8F0"), CreatePen("#94A3B8", 1.2), glass);

        if (!fuse.IsBlown)
        {
            // Sợi dây chì mảnh ánh vàng/bạc nguyên vẹn
            dc.DrawLine(CreatePen("#FACC15", 2.0), new Point(cx, glass.Top), new Point(cx, glass.Bottom));
        }
        else
        {
            // Sợi chì bị đứt cháy ở giữa kèm vệt khói đen/đỏ
            double midY = glass.Y + glass.Height / 2.0;
            dc.DrawEllipse(CreateRadialGlow("#AAEF4444", "#00000000"), null, new Point(cx, midY), 10, 10);
            dc.DrawLine(CreatePen("#94A3B8", 1.8), new Point(cx, glass.Top), new Point(cx - 3, midY - 7));
            dc.DrawLine(CreatePen("#94A3B8", 1.8), new Point(cx + 3, midY + 7), new Point(cx, glass.Bottom));
        }

        // Nhãn dòng định mức / Cảnh báo đứt chì
        string label = fuse.IsBlown ? LocalizationManager.Get("Str.Vis.FuseBlown") : $"{fuse.RatedAmps}A";
        var ft = FormatText(label, fuse.IsBlown ? 7.6 : 9.0, BoldTypeface, CreateSolid(fuse.IsBlown ? "#FCA5A5" : "#FDE047"), dpiScale);
        Rect pill = new(cx - ft.Width / 2 - 4, body.Y + body.Height / 2.0 - 8, ft.Width + 8, 16);
        dc.DrawRoundedRectangle(CreateSolid(fuse.IsBlown ? "#DD991B1B" : "#CC0F172A"), CreatePen(fuse.IsBlown ? "#FCA5A5" : "#475569", 0.9), pill, 3, 3);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, pill.Y + 2));
    }

    private static void DrawKnifeSwitch(DrawingContext dc, KnifeSwitchComponent knife, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        if (knife.SwitchKind == KnifeSwitchKind.Changeover3P_1_0_2)
        {
            // Cầu dao đảo chiều 2 ngả 1 - 0 - 2
            var title = FormatText($"1-0-2 • POS {knife.Position}", 8.4, BoldTypeface, CreateSolid("#F59E0B"), dpiScale);
            dc.DrawText(title, new Point(body.X + 8, body.Y + 6));

            for (int i = 0; i < 3; i++)
            {
                double x = knife.X + knife.TopInputs[i].RelX;
                Point pTop = new(x, body.Top + 26);
                Point pMid = new(x, body.Y + body.Height / 2.0);
                Point pBot = new(x, body.Bottom - 26);

                dc.DrawEllipse(CreateSolid("#F59E0B"), null, pTop, 3.2, 3.2);
                dc.DrawEllipse(CreateSolid("#F59E0B"), null, pBot, 3.2, 3.2);
                dc.DrawEllipse(Brushes.White, CreatePen("#D97706", 1.2), pMid, 3.6, 3.6);

                // Đường dẫn nối từ tâm ra chân chung A/B/C bên phải
                double ry = knife.Y + knife.CommonOutputs[i].RelY;
                dc.DrawLine(CreatePen("#64748B", 1.1), pMid, new Point(body.Right - 4, ry));

                // Lưỡi dao đồng đảo chiều lên (1) / mở ngang (0) / xuống (2)
                Point tip = knife.Position switch
                {
                    1 => pTop,
                    2 => pBot,
                    _ => new Point(x + 14, pMid.Y - 4)
                };
                dc.DrawLine(CreatePen(knife.Position == 0 ? "#94A3B8" : "#F59E0B", 3.4), pMid, tip);
            }

            // Tay nắm cách điện đỏ
            double handleY = knife.Position switch { 1 => body.Top + 22, 2 => body.Bottom - 34, _ => body.Y + body.Height / 2.0 - 6 };
            dc.DrawRoundedRectangle(CreateSolid("#DC2626"), CreatePen("#FCA5A5", 1.0), new Rect(body.X + 12, handleY, 96, 10), 4, 4);

            // 3 vùng bấm nhanh: Ngả 1 (trên), OFF 0 (giữa), Ngả 2 (dưới)
            hitZones.Add(new ComponentHitZone(knife, new Rect(body.X + 6, body.Top + 14, body.Width - 26, 36), "KNIFE_POS_1", AllowedInDesignMode: false));
            hitZones.Add(new ComponentHitZone(knife, new Rect(body.X + 6, body.Y + body.Height / 2.0 - 16, body.Width - 26, 32), "KNIFE_POS_0", AllowedInDesignMode: false));
            hitZones.Add(new ComponentHitZone(knife, new Rect(body.X + 6, body.Bottom - 50, body.Width - 26, 36), "KNIFE_POS_2", AllowedInDesignMode: false));
            return;
        }

        // Cầu dao 1P (2 cực) hoặc 3P (3 cực)
        bool closed = knife.Position == 1;
        string stLbl = closed ? $"ON • {LocalizationManager.Get("Str.Vis.Closed")}" : $"OFF • {LocalizationManager.Get("Str.Vis.Open")}";
        var hdr = FormatText(stLbl, 8.6, BoldTypeface, CreateSolid(closed ? "#4ADE80" : "#94A3B8"), dpiScale);
        dc.DrawText(hdr, new Point(body.X + (body.Width - hdr.Width) / 2.0, body.Y + 6));

        for (int i = 0; i < knife.TopInputs.Count; i++)
        {
            double x = knife.X + knife.TopInputs[i].RelX;
            Point pTop = new(x, body.Top + 28);
            Point pBot = new(x, body.Bottom - 26);

            dc.DrawLine(CreatePen("#94A3B8", 1.8), new Point(x, body.Top + 4), pTop);
            dc.DrawLine(CreatePen("#94A3B8", 1.8), pBot, new Point(x, body.Bottom - 4));

            // Ngàm kẹp đồng trên & bản lề dưới
            dc.DrawRoundedRectangle(CreateSolid("#D97706"), null, new Rect(x - 4, pTop.Y - 4, 8, 8), 2, 2);
            dc.DrawEllipse(CreateSolid("#F59E0B"), CreatePen("#FEF08A", 0.9), pBot, 4.0, 4.0);

            // Lưỡi dao đồng
            Point tip = closed ? pTop : new Point(x + 16, pTop.Y + 14);
            dc.DrawLine(CreatePen(closed ? "#F59E0B" : "#94A3B8", 3.6), pBot, tip);
        }

        // Thanh tay cầm cách điện màu đỏ nối các lưỡi dao
        double barY = closed ? body.Top + 34 : body.Top + 50;
        dc.DrawRoundedRectangle(CreateSolid("#DC2626"), CreatePen("#FCA5A5", 1.1), new Rect(body.X + 10, barY, body.Width - 20, 12), 5, 5);
        hitZones.Add(new ComponentHitZone(knife, new Rect(body.X + 6, body.Y + 20, body.Width - 12, body.Height - 36), "KNIFE_TOGGLE", AllowedInDesignMode: false));
    }

    private static void DrawCamSwitch(DrawingContext dc, CamSwitchComponent cam, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        // Mặt vuông nhôm bạc đặc trưng của chuyển mạch tủ điện KDE
        Rect plate = new(body.X + 12, body.Y + 18, body.Width - 24, body.Height - 36);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#E2E8F0", "#CBD5E1"), CreatePen("#475569", 1.6), plate, 6, 6);

        // 4 vít bắt tủ 4 góc
        foreach (var pt in new[] { new Point(plate.Left + 6, plate.Top + 6), new Point(plate.Right - 6, plate.Top + 6), new Point(plate.Left + 6, plate.Bottom - 6), new Point(plate.Right - 6, plate.Bottom - 6) })
            dc.DrawEllipse(CreateSolid("#64748B"), null, pt, 2.2, 2.2);

        Point center = new(plate.X + plate.Width / 2.0, plate.Y + plate.Height / 2.0 + 4);
        string[] labels = cam.GetPositionLabels();
        int n = labels.Length;
        double startDeg = n == 7 ? -120.0 : -75.0;
        double endDeg = n == 7 ? 120.0 : 75.0;
        double stepDeg = n > 1 ? (endDeg - startDeg) / (n - 1) : 0;

        for (int i = 0; i < n; i++)
        {
            double deg = startDeg + i * stepDeg;
            double rad = (deg - 90.0) * Math.PI / 180.0;
            bool isActive = i == cam.PositionIndex;

            Point tIn = new(center.X + Math.Cos(rad) * 26, center.Y + Math.Sin(rad) * 26);
            Point tOut = new(center.X + Math.Cos(rad) * 32, center.Y + Math.Sin(rad) * 32);
            dc.DrawLine(CreatePen(isActive ? "#0284C7" : "#334155", isActive ? 2.4 : 1.3), tIn, tOut);

            Point lblPt = new(center.X + Math.Cos(rad) * 44, center.Y + Math.Sin(rad) * 44);
            var ft = FormatText(labels[i], isActive ? 8.8 : 7.8, BoldTypeface, CreateSolid(isActive ? "#0369A1" : "#1E293B"), dpiScale);
            dc.DrawText(ft, new Point(lblPt.X - ft.Width / 2.0, lblPt.Y - ft.Height / 2.0));
        }

        // Núm xoay đen kiểu KDE quay theo vị trí đang chọn
        double activeDeg = startDeg + Math.Clamp(cam.PositionIndex, 0, n - 1) * stepDeg;
        dc.DrawEllipse(CreateSolid("#0F172A"), CreatePen("#475569", 1.8), center, 21, 21);

        dc.PushTransform(new RotateTransform(activeDeg, center.X, center.Y));
        dc.DrawRoundedRectangle(CreateVerticalGradient("#334155", "#0F172A"), CreatePen("#94A3B8", 1.1), new Rect(center.X - 5.5, center.Y - 21, 11, 38), 4, 4);
        dc.DrawRoundedRectangle(CreateSolid("#38BDF8"), null, new Rect(center.X - 2.2, center.Y - 19, 4.4, 14), 2, 2);
        dc.Pop();

        // Nhãn chân vào/ra
        foreach (var t in cam.Terminals)
        {
            double ty = t.Side == TerminalSide.Top ? body.Top + 2 : body.Bottom - 12;
            DrawPinLabel(dc, cam.X + t.RelX, ty, t.Name, t.Side == TerminalSide.Top ? "#F59E0B" : "#38BDF8", dpiScale);
        }

        // Nửa trái giảm nấc, nửa phải tăng nấc (cho phép xoay trực tiếp cả ở Design và Simulation + lăn chuột)
        hitZones.Add(new ComponentHitZone(cam, new Rect(plate.X, plate.Y, plate.Width / 2.0, plate.Height), "CAM_PREV", AllowedInDesignMode: true));
        hitZones.Add(new ComponentHitZone(cam, new Rect(plate.X + plate.Width / 2.0, plate.Y, plate.Width / 2.0, plate.Height), "CAM_NEXT", AllowedInDesignMode: true));
    }

    private static void DrawRotaryTimerAh3(DrawingContext dc, RotaryTimerAh3Component ah3, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        // Vỏ vuông màu kem-xám công nghiệp đặc trưng của CKC AH3-3
        Rect shell = new(body.X + 8, body.Y + 14, body.Width - 16, body.Height - 28);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#DDD6C1", "#C2B9A1"), CreatePen("#78716C", 1.5), shell, 6, 6);

        // 2 đèn LED trạng thái: ON (trái) và UP (phải)
        Point onLed = new(shell.Left + 14, shell.Top + 12);
        Point upLed = new(shell.Right - 14, shell.Top + 12);

        if (ah3.IsCoilEnergized && !ah3.IsBurnedOut)
        {
            dc.DrawEllipse(CreateRadialGlow("#AA22C55E", "#0022C55E"), null, onLed, 9, 9);
            dc.DrawEllipse(CreateSolid("#22C55E"), CreatePen("#DCFCE7", 0.9), onLed, 4.2, 4.2);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#334155"), CreatePen("#64748B", 0.8), onLed, 3.8, 3.8);
        }

        if (ah3.IsTimeUp && !ah3.IsBurnedOut)
        {
            dc.DrawEllipse(CreateRadialGlow("#AAEF4444", "#00EF4444"), null, upLed, 9, 9);
            dc.DrawEllipse(CreateSolid("#EF4444"), CreatePen("#FEE2E2", 0.9), upLed, 4.2, 4.2);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#334155"), CreatePen("#64748B", 0.8), upLed, 3.8, 3.8);
        }

        dc.DrawText(FormatText("ON", 7.2, BoldTypeface, CreateSolid("#1E293B"), dpiScale), new Point(onLed.X + 6, onLed.Y - 4.5));
        var upFt = FormatText("UP", 7.2, BoldTypeface, CreateSolid("#991B1B"), dpiScale);
        dc.DrawText(upFt, new Point(upLed.X - upFt.Width - 6, upLed.Y - 4.5));

        var hdrFt = FormatText($"AH3-3 • {ah3.RangeShortLabel}", 7.8, BoldTypeface, CreateSolid("#1E293B"), dpiScale);
        dc.DrawText(hdrFt, new Point(shell.X + (shell.Width - hdrFt.Width) / 2.0, shell.Top + 5));

        // Mặt đồng hồ tròn trong suốt ở giữa có vạch chia độ & kim đỏ xoay mượt
        Point dialCenter = new(shell.X + shell.Width / 2.0, shell.Y + shell.Height / 2.0 + 4);
        double dialR = 40.0;
        dc.DrawEllipse(CreateVerticalGradient("#F8FAFC", "#E2E8F0"), CreatePen("#475569", 1.8), dialCenter, dialR, dialR);

        // Vẽ cung tiến trình đếm thời gian thực khi cuộn 2-7 đang có điện
        if (ah3.IsCoilEnergized && ah3.DelaySeconds > 0.05)
        {
            double progressRatio = Math.Clamp(ah3.ElapsedSeconds / ah3.DelaySeconds, 0.0, 1.0);
            double maxSweep = ah3.GetDialAngleDegrees() - (-135.0);
            int segs = Math.Max(2, (int)(progressRatio * 24));
            if (progressRatio > 0.01 && maxSweep > 1.0)
            {
                var arcGeom = new StreamGeometry();
                using (var ctx = arcGeom.Open())
                {
                    double a0 = (-135.0 - 90.0) * Math.PI / 180.0;
                    ctx.BeginFigure(new Point(dialCenter.X + Math.Cos(a0) * 33, dialCenter.Y + Math.Sin(a0) * 33), false, false);
                    for (int k = 1; k <= segs; k++)
                    {
                        double deg = -135.0 + maxSweep * progressRatio * (k / (double)segs);
                        double rad = (deg - 90.0) * Math.PI / 180.0;
                        ctx.LineTo(new Point(dialCenter.X + Math.Cos(rad) * 33, dialCenter.Y + Math.Sin(rad) * 33), true, true);
                    }
                }
                arcGeom.Freeze();
                dc.DrawGeometry(null, CreatePen(ah3.IsTimeUp ? "#EF4444" : "#0284C7", 4.2), arcGeom);
            }
        }

        // 11 vạch chia độ từ -135° đến +135° kèm chữ số mốc 0 / giữa / max
        double maxScale = ah3.TimeRange == Ah3TimeRange.Range_10m ? 10.0 : ah3.MaxRangeSeconds;
        for (int t = 0; t <= 10; t++)
        {
            double deg = -135.0 + t * 27.0;
            double rad = (deg - 90.0) * Math.PI / 180.0;
            bool major = t % 2 == 0;
            Point pIn = new(dialCenter.X + Math.Cos(rad) * (major ? 29 : 32), dialCenter.Y + Math.Sin(rad) * (major ? 29 : 32));
            Point pOut = new(dialCenter.X + Math.Cos(rad) * 37, dialCenter.Y + Math.Sin(rad) * 37);
            dc.DrawLine(CreatePen("#1E293B", major ? 1.5 : 0.9), pIn, pOut);

            if (t is 0 or 5 or 10)
            {
                string num = $"{(maxScale * t / 10.0):0}";
                var nFt = FormatText(num, 7.2, BoldTypeface, CreateSolid("#0F172A"), dpiScale);
                Point np = new(dialCenter.X + Math.Cos(rad) * 22, dialCenter.Y + Math.Sin(rad) * 22);
                dc.DrawText(nFt, new Point(np.X - nFt.Width / 2, np.Y - nFt.Height / 2));
            }
        }

        // Núm xoay trong suốt & kim chỉ thị màu đỏ
        dc.DrawEllipse(CreateVerticalGradient("#FFFFFF", "#CBD5E1"), CreatePen("#64748B", 1.3), dialCenter, 16, 16);
        dc.PushTransform(new RotateTransform(ah3.GetDialAngleDegrees(), dialCenter.X, dialCenter.Y));
        dc.DrawLine(CreatePen("#DC2626", 2.8), new Point(dialCenter.X, dialCenter.Y + 4), new Point(dialCenter.X, dialCenter.Y - 34));
        dc.DrawEllipse(CreateSolid("#DC2626"), null, new Point(dialCenter.X, dialCenter.Y - 12), 2.5, 2.5);
        dc.Pop();
        dc.DrawEllipse(CreateSolid("#334155"), null, dialCenter, 3.8, 3.8);

        // Hiển thị số giây đặt (hoặc thời gian đang đếm ngược)
        string timeText = ah3.IsCoilEnergized
            ? $"{ah3.FormatDuration(ah3.ElapsedSeconds)} / {ah3.FormatDuration(ah3.DelaySeconds)}"
            : ah3.FormatDuration(ah3.DelaySeconds);
        var valFt = FormatText(timeText, 8.0, MonoTypeface, Brushes.White, dpiScale);
        Rect valPill = new(dialCenter.X - valFt.Width / 2 - 5, shell.Bottom - 16, valFt.Width + 10, 14);
        dc.DrawRoundedRectangle(CreateSolid("#0F172A"), CreatePen("#475569", 0.8), valPill, 3, 3);
        dc.DrawText(valFt, new Point(valPill.X + 5, valPill.Y + 1));

        // Nhãn 8 chân đế tròn: Hàng trên 1,3,4,2 | Hàng dưới 8,6,5,7
        foreach (var t in ah3.Terminals)
        {
            double ty = t.Side == TerminalSide.Top ? body.Top + 1 : body.Bottom - 12;
            string hex = t.Id is "2" or "7" ? "#EF4444" : (t.Id is "1" or "8" ? "#38BDF8" : "#F59E0B");
            DrawPinLabel(dc, ah3.X + t.RelX, ty, t.Id, hex, dpiScale);
        }

        DrawSupplyMismatchPill(dc, body, ah3.SupplyStatus, dpiScale);

        Rect dialHit = new(dialCenter.X - dialR, dialCenter.Y - dialR, dialR * 2, dialR * 2);
        hitZones.Add(new ComponentHitZone(ah3, dialHit, "AH3_DIAL", AllowedInDesignMode: true));
    }

    private static void DrawMotorLinearTrack(DrawingContext dc, MotorLinearTrackComponent track, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        // 1. Cụm Động cơ 3 pha bên trái kéo trục vít me
        Point motorCenter = new(body.X + 60, body.Y + body.Height / 2.0 - 2);
        bool spinning = Math.Abs(track.CurrentAngularVelocity) > 2.0 && !track.IsPhaseLost;
        string mBadge = spinning
            ? $"{track.ConnectionModeLabel} • {track.OperatingCurrentAmps:0.#}A"
            : $"{track.RatedPowerKw:0.#}kW • {track.CarriagePosition * 100.0:0}%";
        DrawMotorStatorAndRotor(dc, motorCenter, 34, track.RotorAngle, spinning, track.RotationDirection, track.IsPhaseLost, mBadge, theme, dpiScale);

        // 2. Khung bàn trượt & trục vít me (x: 106..348)
        Rect trackBox = new(body.X + 108, body.Y + 26, body.Width - 118, body.Height - 54);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#475569", 1.3), trackBox, 6, 6);

        double railY = trackBox.Y + trackBox.Height / 2.0 + 4;
        double railLeft = trackBox.Left + 18;
        double railRight = trackBox.Right - 18;

        // Khớp nối trục từ động cơ vào trục vít me
        dc.DrawRectangle(CreateSolid("#94A3B8"), CreatePen("#334155", 1.0), new Rect(body.X + 94, railY - 5, 16, 10));

        // Thanh dẫn hướng trên/dưới + trục vít me có ren xoắn động
        dc.DrawLine(CreatePen("#475569", 2.0), new Point(railLeft, railY - 18), new Point(railRight, railY - 18));
        dc.DrawLine(CreatePen("#475569", 2.0), new Point(railLeft, railY + 18), new Point(railRight, railY + 18));
        dc.DrawRoundedRectangle(CreateVerticalGradient("#CBD5E1", "#64748B"), CreatePen("#334155", 0.9),
            new Rect(railLeft, railY - 4.5, railRight - railLeft, 9), 2, 2);

        Pen threadPen = CreatePen("#334155", 1.1);
        for (double tx = railLeft + (track.LeadscrewPhase % 10.0); tx < railRight - 2; tx += 10.0)
        {
            dc.DrawLine(threadPen, new Point(tx, railY - 4), new Point(tx + 3, railY + 4));
        }

        // 3. Hai công tắc hành trình mini LS1 (đầu trái) & LS2 (đầu phải)
        DrawMiniTrackLimitSwitch(dc, new Point(railLeft + 4, trackBox.Top + 18), "LS1", track.IsLs1Actuated, isRightSide: false, dpiScale);
        DrawMiniTrackLimitSwitch(dc, new Point(railRight - 4, trackBox.Top + 18), "LS2", track.IsLs2Actuated, isRightSide: true, dpiScale);

        // 4. Con trượt (Bàn máy) di chuyển mượt theo CarriagePosition (0.0 -> 1.0)
        double travelMinX = railLeft + 26;
        double travelMaxX = railRight - 26;
        double cx = travelMinX + Math.Clamp(track.CarriagePosition, 0.0, 1.0) * (travelMaxX - travelMinX);

        Rect carriage = new(cx - 20, railY - 22, 40, 44);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#0EA5E9", "#0369A1"), CreatePen("#E0F2FE", 1.5), carriage, 5, 5);
        // Vấu cam gạt hành trình phía trên con trượt
        dc.DrawRoundedRectangle(CreateSolid("#FACC15"), CreatePen("#854D0E", 1.0), new Rect(cx - 14, carriage.Top - 6, 28, 7), 2, 2);

        var posFt = FormatText($"{track.CarriagePosition * 100.0:0}%", 8.5, MonoTypeface, Brushes.White, dpiScale);
        dc.DrawText(posFt, new Point(cx - posFt.Width / 2.0, railY - posFt.Height / 2.0));

        // 5. Nhãn 12 chân đấu nối: U1, V1, W1, PE (trên) & LS1(1-4), LS2(1-4) (dưới)
        foreach (var t in track.Terminals)
        {
            double ty = t.Side == TerminalSide.Top ? body.Top + 2 : body.Bottom - 13;
            string hex = t.Name switch
            {
                "U1" => "#EF4444",
                "V1" => "#CA8A04",
                "W1" => "#2563EB",
                "PE" => "#16A34A",
                _ when t.Id.EndsWith("_1") || t.Id.EndsWith("_2") => "#FB923C",
                _ => "#4ADE80"
            };
            DrawPinLabel(dc, track.X + t.RelX, ty, t.Name, hex, dpiScale);
        }
    }

    private static void DrawMiniTrackLimitSwitch(DrawingContext dc, Point pos, string label, bool actuated, bool isRightSide, double dpiScale)
    {
        Rect box = new(pos.X - 11, pos.Y - 10, 22, 18);
        dc.DrawRoundedRectangle(CreateSolid(actuated ? "#166534" : "#1E293B"), CreatePen(actuated ? "#4ADE80" : "#F97316", 1.3), box, 3, 3);
        var ft = FormatText(label, 7.2, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2.0, box.Y + 3));

        // Cần gạt bánh xe nhỏ nghiêng xuống khi bị con trượt ép vào
        Point pivot = new(pos.X, box.Bottom);
        double dx = isRightSide ? -10 : 10;
        double dy = actuated ? 4 : 11;
        dc.DrawLine(CreatePen("#E2E8F0", 2.0), pivot, new Point(pivot.X + dx, pivot.Y + dy));
        dc.DrawEllipse(CreateSolid(actuated ? "#4ADE80" : "#F97316"), null, new Point(pivot.X + dx, pivot.Y + dy), 3.6, 3.6);
    }

    private static void DrawPinLabel(DrawingContext dc, double cx, double y, string text, string hex, double dpiScale)
    {
        var ft = FormatText(text, 7.4, BoldTypeface, CreateSolid(hex), dpiScale);
        dc.DrawText(ft, new Point(cx - ft.Width / 2.0, y));
    }

    private static void DrawIndustrialSensor(DrawingContext dc, IndustrialSensorComponent sens, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        (string accentHex, string typeBadge, string iconChar) = sens.SensorKind switch
        {
            SensorKind.Capacitive => ("#0EA5E9", "CAP", "💧"),
            SensorKind.Photoelectric => ("#F59E0B", "PHOTO", "☀"),
            SensorKind.Ultrasonic => ("#A855F7", "ULTRA", ")))"),
            _ => ("#06B6D4", "IND", "⚙")
        };

        // Đầu vùng nhạy cảm biến (Sensing Head) phía trên
        Rect headZone = new(body.X + 10, body.Y + 6, body.Width - 20, 32);
        Brush headFill = sens.IsTargetDetected && sens.IsPowered
            ? CreateVerticalGradient(accentHex, "#0F172A")
            : CreateSolid(theme.InsetPanelHex);
        dc.DrawRoundedRectangle(headFill, CreatePen(accentHex, sens.IsTargetDetected ? 2.2 : 1.3), headZone, 6, 6);

        string targetTxt = sens.IsTargetDetected
            ? $"{iconChar} {LocalizationManager.Get("Str.Vis.ObjectDetected")}"
            : $"{iconChar} {typeBadge} • {sens.Polarity}";
        var headFt = FormatText(targetTxt, 9.2, BoldTypeface, CreateSolid(sens.IsTargetDetected ? "#FEF08A" : "#7DD3FC"), dpiScale);
        dc.DrawText(headFt, new Point(headZone.X + (headZone.Width - headFt.Width) / 2, headZone.Y + 8));

        // Đèn báo nguồn (Xanh) & Đèn báo ngõ ra OUT (Cam chuẩn cảm biến công nghiệp)
        Rect infoPanel = new(body.X + 10, body.Y + 43, body.Width - 20, 34);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#334155", 1.0), infoPanel, 4, 4);

        Point pwrLed = new(infoPanel.X + 12, infoPanel.Y + 11);
        Point outLed = new(infoPanel.Right - 12, infoPanel.Y + 11);
        dc.DrawEllipse(CreateSolid(sens.IsPowered ? "#22C55E" : "#334155"), null, pwrLed, 4.0, 4.0);
        if (sens.IsPowered && sens.IsOut1Closed)
        {
            dc.DrawEllipse(CreateRadialGlow("#AAF97316", "#00F97316"), null, outLed, 10, 10);
            dc.DrawEllipse(CreateSolid("#F97316"), CreatePen("#FEF08A", 0.8), outLed, 4.5, 4.5);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#334155"), null, outLed, 4.0, 4.0);
        }

        string cfgTag = sens.OutputConfig switch
        {
            SensorOutputConfig.NC_3Wire => $"{sens.Polarity} • NC",
            SensorOutputConfig.FourWire_NO_NC => $"{sens.Polarity} • NO+NC",
            _ => $"{sens.Polarity} • NO"
        };
        var cfgFt = FormatText(cfgTag, 8.8, BoldTypeface, CreateSolid("#E2E8F0"), dpiScale);
        dc.DrawText(cfgFt, new Point(infoPanel.X + (infoPanel.Width - cfgFt.Width) / 2, infoPanel.Y + 17));

        // Vạch màu chuẩn quốc tế cho các chân BN (Nâu), BU (Xanh dương), BK (Đen), WH (Trắng)
        DrawSensorPinTag(dc, sens.X + sens.BnPlus.RelX, body.Bottom - 17, "BN+", "#B45309", dpiScale);
        DrawSensorPinTag(dc, sens.X + sens.BuMinus.RelX, body.Bottom - 17, "BU-", "#3B82F6", dpiScale);
        DrawSensorPinTag(dc, sens.X + sens.BkOut1.RelX, body.Bottom - 17, "BK", "#CBD5E1", dpiScale);
        if (sens.WhOut2 is not null)
            DrawSensorPinTag(dc, sens.X + sens.WhOut2.RelX, body.Bottom - 17, "WH", "#F8FAFC", dpiScale);

        string act = sens.TriggerMode == PushButtonActionMode.Latching ? "SENSOR_TOGGLE" : "SENSOR_HOLD";
        hitZones.Add(new ComponentHitZone(sens, headZone, act, AllowedInDesignMode: false));
    }

    private static void DrawSensorPinTag(DrawingContext dc, double cx, double y, string label, string hex, double dpiScale)
    {
        var ft = FormatText(label, 8.2, BoldTypeface, CreateSolid(hex), dpiScale);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, y));
    }

    // =========================================================================
    // 3. ĐỒNG HỒ ĐO VOLT / AMPE (DIGITAL LED & ANALOG KIM) & BIẾN DÒNG CT
    // =========================================================================

    private static void DrawPanelMeter(
        DrawingContext dc,
        Rect body,
        string unitSymbol,
        CurrentKind measureKind,
        string rangeLabel,
        MeterDisplayStyle style,
        double value,
        double maxScaleValue,
        bool isOverload,
        double needleRatio,
        string accentHex,
        CanvasThemeColors theme,
        double dpiScale)
    {
        Rect face = new(body.X + 8, body.Y + 8, body.Width - 16, body.Height - 24);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#475569", 1.4), face, 6, 6);

        string modeBadge = $"{unitSymbol}{(measureKind == CurrentKind.AC ? "~" : "⎓")}  {rangeLabel}";
        var hdr = FormatText(modeBadge, 8.2, BoldTypeface, CreateSolid(accentHex), dpiScale);
        dc.DrawText(hdr, new Point(face.X + 6, face.Y + 4));

        if (style == MeterDisplayStyle.Digital)
        {
            Rect ledBox = new(face.X + 6, face.Y + 22, face.Width - 12, 34);
            dc.DrawRoundedRectangle(CreateSolid("#05080F"), CreatePen("#1E293B", 1.0), ledBox, 4, 4);

            string valStr = isOverload
                ? "OL"
                : (value >= 100.0 ? $"{value:0}{unitSymbol}" : $"{value:0.0}{unitSymbol}");
            string digitColor = isOverload ? "#EF4444" : accentHex;
            var valFt = FormatText(valStr, 15.5, MonoTypeface, CreateSolid(digitColor), dpiScale);
            dc.DrawText(valFt, new Point(ledBox.X + (ledBox.Width - valFt.Width) / 2, ledBox.Y + 6));
        }
        else
        {
            // Mặt đồng hồ kim (Analog Scale + Kim xoay mượt 60 FPS)
            Point pivot = new(face.X + face.Width / 2.0, face.Bottom - 12);
            double arcR = 30.0;

            // Các vạch chia cung tròn từ -55° đến +55°
            for (int tick = 0; tick <= 6; tick++)
            {
                double deg = -55.0 + (110.0 * tick / 6.0);
                double rad = (deg - 90.0) * Math.PI / 180.0;
                Point pIn = new(pivot.X + Math.Cos(rad) * (arcR - 5), pivot.Y + Math.Sin(rad) * (arcR - 5));
                Point pOut = new(pivot.X + Math.Cos(rad) * arcR, pivot.Y + Math.Sin(rad) * arcR);
                dc.DrawLine(CreatePen(tick >= 5 ? "#EF4444" : "#94A3B8", 1.3), pIn, pOut);
            }

            // Kim đồng hồ đỏ xoay mượt theo giá trị đo
            double needleDeg = -55.0 + Math.Clamp(needleRatio, 0.0, 1.06) * 110.0;
            double nRad = (needleDeg - 90.0) * Math.PI / 180.0;
            Point tip = new(pivot.X + Math.Cos(nRad) * (arcR - 2), pivot.Y + Math.Sin(nRad) * (arcR - 2));
            dc.DrawLine(CreatePen("#EF4444", 2.1), pivot, tip);
            dc.DrawEllipse(CreateSolid("#E2E8F0"), null, pivot, 3.2, 3.2);

            string subRead = isOverload ? "OL!" : $"{value:0.#}{unitSymbol}";
            var subFt = FormatText(subRead, 8.5, MonoTypeface, CreateSolid(isOverload ? "#EF4444" : "#E2E8F0"), dpiScale);
            dc.DrawText(subFt, new Point(face.Right - subFt.Width - 5, face.Bottom - 14));
        }
    }

    private static void DrawCurrentTransformer(DrawingContext dc, CurrentTransformerComponent ct, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        var (_, ratioLbl) = ct.GetRatioSpec();

        // Thanh dẫn sơ cấp P1 -> P2 xuyên qua tâm lõi xuyến
        double px = ct.X + ct.P1.RelX;
        dc.DrawLine(CreatePen("#F87171", 4.0), new Point(px, body.Top + 2), new Point(px, body.Bottom - 2));

        // Lõi từ hình xuyến bao quanh dây sơ cấp
        Point ringCenter = new(px, body.Y + body.Height / 2.0 - 4);
        dc.DrawEllipse(CreateSolid(theme.InsetPanelHex), CreatePen("#A855F7", 3.5), ringCenter, 15, 22);
        dc.DrawEllipse(CreateSolid(theme.BodyTopHex), CreatePen("#64748B", 1.2), ringCenter, 6, 11);

        // Hộp hiển thị tỷ số CT & dòng thứ cấp bên phải
        Rect box = new(body.X + 42, body.Y + 14, body.Width - 50, 62);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen("#475569", 1.2), box, 5, 5);

        dc.DrawText(FormatText($"CT {ratioLbl}", 9.2, BoldTypeface, CreateSolid("#D8B4FE"), dpiScale), new Point(box.X + 6, box.Y + 5));
        dc.DrawText(FormatText($"P: {ct.PrimaryCurrentAmps:0.0}A", 8.8, MonoTypeface, CreateSolid("#E2E8F0"), dpiScale), new Point(box.X + 6, box.Y + 23));
        dc.DrawText(FormatText($"S: {ct.SecondaryCurrentAmps:0.00}A", 8.8, MonoTypeface, CreateSolid("#38BDF8"), dpiScale), new Point(box.X + 6, box.Y + 40));

        // Dây dẫn nội bộ từ cuộn thứ cấp xuống S1, S2
        dc.DrawText(FormatText("S1", 8.2, BoldTypeface, CreateSolid("#93C5FD"), dpiScale), new Point(ct.X + ct.S1.RelX - 6, body.Bottom - 16));
        dc.DrawText(FormatText("S2", 8.2, BoldTypeface, CreateSolid("#93C5FD"), dpiScale), new Point(ct.X + ct.S2.RelX - 6, body.Bottom - 16));
    }

    // =========================================================================
    // 3B. BIẾN TẦN CÔNG NGHIỆP (VFD / INVERTER) — MÀN HÌNH LED & NÚT KEYPAD
    // =========================================================================

    private static void DrawVfd(DrawingContext dc, VfdComponent vfd, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        // Thanh màu nhận diện đầu biến tần
        dc.DrawRoundedRectangle(CreateSolid("#0284C7"), null, new Rect(body.X + 10, body.Y + 17, body.Width - 36, 3.5), 2, 2);

        // Nhãn hàng chân trên: R, S, T, PE, FA, FB, FC
        (Terminal T, string Hex)[] topPins =
        [
            (vfd.R_In, "#EF4444"), (vfd.S_In, "#CA8A04"), (vfd.T_In, "#2563EB"), (vfd.Pe_In, "#16A34A"),
            (vfd.Fa_No, "#A855F7"), (vfd.Fb_Nc, "#A855F7"), (vfd.Fc_Com, "#C084FC")
        ];
        foreach (var (t, hex) in topPins)
        {
            var ft = FormatText(t.Name, 7.6, BoldTypeface, CreateSolid(hex), dpiScale);
            dc.DrawText(ft, new Point(vfd.X + t.RelX - ft.Width / 2.0, body.Top + 3));
        }

        // Nhãn hàng chân dưới: U, V, W, FWD, REV, DI1, DI2, COM
        (Terminal T, string Hex)[] botPins =
        [
            (vfd.U_Out, "#EF4444"), (vfd.V_Out, "#CA8A04"), (vfd.W_Out, "#2563EB"),
            (vfd.Fwd, "#38BDF8"), (vfd.Rev, "#FB923C"), (vfd.Di1, "#2DD4BF"), (vfd.Di2, "#2DD4BF"), (vfd.Com, "#94A3B8")
        ];
        foreach (var (t, hex) in botPins)
        {
            var ft = FormatText(t.Name, 7.2, BoldTypeface, CreateSolid(hex), dpiScale);
            dc.DrawText(ft, new Point(vfd.X + t.RelX - ft.Width / 2.0, body.Bottom - 13));
        }

        // Nhãn cạnh phải: 10V, AI1, GND
        foreach (var t in new[] { vfd.Pot10V, vfd.PotAi1, vfd.PotGnd })
        {
            var ft = FormatText(t.Name, 7.4, BoldTypeface, CreateSolid("#F59E0B"), dpiScale);
            dc.DrawText(ft, new Point(body.Right - ft.Width - 4, vfd.Y + t.RelY - ft.Height / 2.0));
        }

        // Khối màn hình mặt trước biến tần
        Rect panel = new(body.X + 12, body.Y + 24, body.Width - 44, body.Height - 44);
        dc.DrawRoundedRectangle(CreateSolid("#0B1120"), CreatePen(vfd.IsRunning ? "#22D3EE" : "#334155", 1.5), panel, 6, 6);

        // Header nhỏ: VFD + LED RUN/STOP + FWD/REV
        var brandFt = FormatText($"VFD  P01:{vfd.P01_RunSource}  P02:{vfd.P02_FreqSource}", 7.8, BoldTypeface, CreateSolid("#94A3B8"), dpiScale);
        dc.DrawText(brandFt, new Point(panel.X + 8, panel.Y + 5));

        string stHex = !vfd.IsPowered ? "#475569" : (vfd.IsRunning ? "#16A34A" : "#B91C1C");
        string stText = !vfd.IsPowered
            ? LocalizationManager.Get("Str.Vis.VfdNoPower")
            : (vfd.IsRunning ? (vfd.CurrentSignedFreqHz < 0 ? "RUN ↺" : "RUN ↻") : "STOP");
        var stFt = FormatText(stText, 7.6, BoldTypeface, Brushes.White, dpiScale);
        Rect stPill = new(panel.Right - stFt.Width - 14, panel.Y + 4, stFt.Width + 10, 14);
        dc.DrawRoundedRectangle(CreateSolid(stHex), null, stPill, 3, 3);
        dc.DrawText(stFt, new Point(stPill.X + 5, stPill.Y + 1));

        // Màn hình số LED tần số lớn (00.0 Hz)
        Rect ledBox = new(panel.X + 8, panel.Y + 23, panel.Width - 16, 42);
        dc.DrawRoundedRectangle(CreateSolid("#04070E"), CreatePen("#1E293B", 1.2), ledBox, 4, 4);

        string freqText = vfd.IsPowered ? $"{vfd.CurrentOutputFreqHz:00.0} Hz" : "--.- Hz";
        string freqHex = !vfd.IsPowered ? "#475569" : (vfd.IsRunning ? "#22D3EE" : "#F59E0B");
        var freqFt = FormatText(freqText, 20.5, MonoTypeface, CreateSolid(freqHex), dpiScale);
        dc.DrawText(freqFt, new Point(ledBox.X + (ledBox.Width - freqFt.Width) / 2.0, ledBox.Y + 4));

        var subInfo = FormatText($"SET {vfd.ComputeTargetFrequencyHz():0.0}Hz  •  {vfd.OutputCurrentAmps:0.0}A", 7.6, MonoTypeface, CreateSolid("#94A3B8"), dpiScale);
        dc.DrawText(subInfo, new Point(ledBox.X + (ledBox.Width - subInfo.Width) / 2.0, ledBox.Bottom - 12));

        // Nút bấm "Lập trình / Keypad" ngay trên thân Biến tần
        Rect keypadBtn = new(panel.X + 8, panel.Bottom - 28, panel.Width - 16, 22);
        dc.DrawRoundedRectangle(CreateVerticalGradient("#0369A1", "#075985"), CreatePen("#38BDF8", 1.2), keypadBtn, 4, 4);
        var btnFt = FormatText($"🎛 {LocalizationManager.Get("Str.Vis.VfdKeypadBtn")}", 8.8, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(btnFt, new Point(keypadBtn.X + (keypadBtn.Width - btnFt.Width) / 2.0, keypadBtn.Y + (keypadBtn.Height - btnFt.Height) / 2.0));

        hitZones.Add(new ComponentHitZone(vfd, keypadBtn, "VFD_KEYPAD", AllowedInDesignMode: true));
    }

    // =========================================================================
    // 4. CONTACTOR, RƠ-LE TRUNG GIAN, RƠ-LE NHIỆT, TIMER H3CR, ĐÈN & ĐỘNG CƠ
    // =========================================================================

    private static void DrawContactor(DrawingContext dc, ContactorComponent km, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        bool pulledIn = km.IsCoilEnergized && !km.IsBurnedOut;

        var cols = new List<(Terminal Top, Terminal Bot, bool Closed, string Tag, string ColorHex)>
        {
            (km.L1In, km.T1Out, pulledIn, "L1", "#EF4444"),
            (km.L2In, km.T2Out, pulledIn, "L2", "#EAB308"),
            (km.L3In, km.T3Out, pulledIn, "L3", "#3B82F6")
        };
        if (km.No13 is not null && km.No14 is not null)
            cols.Add((km.No13, km.No14, pulledIn, "NO", "#10B981"));
        if (km.Nc21 is not null && km.Nc22 is not null)
            cols.Add((km.Nc21, km.Nc22, !pulledIn, "NC", "#F97316"));

        foreach (var (top, bot, closed, tag, hex) in cols)
        {
            double x = km.X + top.RelX;
            dc.DrawLine(CreatePen("#64748B", 1.5), new Point(x, body.Top + 4), new Point(x, body.Top + 34));
            dc.DrawLine(CreatePen("#64748B", 1.5), new Point(x, body.Bottom - 34), new Point(x, body.Bottom - 4));

            Pen bridgePen = closed ? CreatePen(hex, 2.6) : CreatePen("#64748B", 1.8);
            Point pUpper = new(x, body.Top + 34);
            Point pLower = new(x, body.Bottom - 34);
            Point pBlade = closed ? pLower : new Point(x + 9, body.Bottom - 38);
            dc.DrawLine(bridgePen, pUpper, pBlade);

            var tLbl = FormatText(tag, 8.5, BoldTypeface, CreateSolid(hex), dpiScale);
            dc.DrawText(tLbl, new Point(x - tLbl.Width / 2, body.Top + 8));
        }

        Pen dashPen = new(CreateSolid(pulledIn ? "#8B5CF6" : "#64748B"), 1.2) { DashStyle = DashStyles.Dash };
        dashPen.Freeze();
        dc.DrawLine(dashPen, new Point(km.X + km.L1In.RelX, body.Y + body.Height / 2), new Point(km.X + km.CoilA1.RelX - 18, body.Y + body.Height / 2));

        double cx = km.X + km.CoilA1.RelX;
        dc.DrawLine(CreatePen("#64748B", 1.6), new Point(cx, body.Top + 4), new Point(cx, body.Top + 30));
        dc.DrawLine(CreatePen("#64748B", 1.6), new Point(cx, body.Bottom - 30), new Point(cx, body.Bottom - 4));

        Rect coilBox = new(cx - 17, body.Y + 30, 34, body.Height - 60);
        Brush coilFill = pulledIn ? CreateVerticalGradient("#4C1D95", "#2E1065") : CreateSolid(theme.InsetPanelHex);
        Pen coilPen = pulledIn ? CreatePen("#C084FC", 2.0) : CreatePen("#64748B", 1.4);
        dc.DrawRoundedRectangle(coilFill, coilPen, coilBox, 4, 4);

        Point ledPos = new(cx, coilBox.Y + 13);
        if (pulledIn)
        {
            dc.DrawEllipse(CreateRadialGlow("#AA22C55E", "#0022C55E"), null, ledPos, 12, 12);
            dc.DrawEllipse(CreateSolid("#4ADE80"), CreatePen("#DCFCE7", 1.2), ledPos, 5.0, 5.0);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#1E293B"), CreatePen("#475569", 1.0), ledPos, 4.5, 4.5);
        }

        var (_, _, coilSpec) = ElectricalAnalysisHelper.GetRatingSpec(km.CoilRating);
        var specFt = FormatText(coilSpec, 7.8, BoldTypeface, CreateSolid("#C4B5FD"), dpiScale);
        dc.DrawText(specFt, new Point(cx - specFt.Width / 2, coilBox.Y + 26));

        string pullTxt = pulledIn ? LocalizationManager.Get("Str.Vis.Pulled") : LocalizationManager.Get("Str.Vis.Released");
        var aTxt = FormatText(pullTxt, 8.2, BoldTypeface, CreateSolid(pulledIn ? "#86EFAC" : "#94A3B8"), dpiScale);
        dc.DrawText(aTxt, new Point(cx - aTxt.Width / 2, coilBox.Bottom - 15));

        DrawSupplyMismatchPill(dc, body, km.SupplyStatus, dpiScale);
    }

    private static void DrawIntermediateRelay(DrawingContext dc, IntermediateRelayComponent my, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        bool active = my.IsCoilEnergized && !my.IsBurnedOut;
        var (coilKind, _, ratingLbl) = ElectricalAnalysisHelper.GetRatingSpec(my.CoilRating);
        string modelTag = my.PoleConfig == IntermediateRelayPoles.DPDT_8Pin ? "MY2N (8P)" : "MY4N (14P)";

        Rect glass = new(body.X + 8, body.Y + 8, body.Width - 16, body.Height - 16);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(active ? "#38BDF8" : "#475569", 1.3), glass, 5, 5);

        dc.DrawText(FormatText($"{modelTag} • {ratingLbl}", 8.5, BoldTypeface, CreateSolid("#7DD3FC"), dpiScale),
            new Point(glass.X + 8, glass.Y + 4));

        foreach (var pole in my.Poles)
        {
            Point pCom = new(my.X + pole.Com.RelX, glass.Bottom - 16);
            Point pNc = new(my.X + pole.Nc.RelX, glass.Top + 24);
            Point pNo = new(my.X + pole.No.RelX, glass.Top + 24);

            dc.DrawLine(CreatePen("#475569", 1.2), pCom, active ? pNc : pNo);
            dc.DrawLine(CreatePen(active ? "#4ADE80" : "#FB923C", 2.3), pCom, active ? pNo : pNc);

            dc.DrawEllipse(CreateSolid("#FB923C"), null, pNc, 2.5, 2.5);
            dc.DrawEllipse(CreateSolid("#4ADE80"), null, pNo, 2.5, 2.5);
            dc.DrawEllipse(Brushes.White, null, pCom, 2.8, 2.8);
        }

        Point ledCenter = new(glass.Right - 16, glass.Y + 14);
        string ledHex = coilKind == CurrentKind.DC ? "#22C55E" : "#F97316";
        if (active)
        {
            dc.DrawEllipse(CreateRadialGlow("#AA" + ledHex[1..], "#00" + ledHex[1..]), null, ledCenter, 11, 11);
            dc.DrawEllipse(CreateSolid(ledHex), CreatePen("#F8FAFC", 1.0), ledCenter, 4.5, 4.5);
        }
        else
        {
            dc.DrawEllipse(CreateSolid("#1E293B"), CreatePen("#475569", 1.0), ledCenter, 4.0, 4.0);
        }

        dc.DrawText(FormatText("A1", 8.0, BoldTypeface, CreateSolid("#F87171"), dpiScale), new Point(my.X + my.CoilA1.RelX - 6, glass.Bottom - 15));
        dc.DrawText(FormatText("A2", 8.0, BoldTypeface, CreateSolid("#94A3B8"), dpiScale), new Point(my.X + my.CoilA2.RelX - 6, glass.Bottom - 15));

        DrawSupplyMismatchPill(dc, body, my.SupplyStatus, dpiScale);
    }

    private static void DrawThermalRelay(DrawingContext dc, ThermalOverloadRelayComponent rn, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        var (_, _, rangeLbl) = rn.GetRangeSpec();

        (Terminal Top, Terminal Bot, string Hex)[] heaters =
        [
            (rn.L1In, rn.T1Out, "#EF4444"),
            (rn.L2In, rn.T2Out, "#EAB308"),
            (rn.L3In, rn.T3Out, "#3B82F6")
        ];

        foreach (var (top, _, hex) in heaters)
        {
            double x = rn.X + top.RelX;
            dc.DrawLine(CreatePen(hex, 2.0), new Point(x, body.Top + 4), new Point(x, body.Top + 36));
            dc.DrawLine(CreatePen(hex, 2.0), new Point(x, body.Top + 36), new Point(x - 6, body.Top + 44));
            dc.DrawLine(CreatePen(hex, 2.0), new Point(x - 6, body.Top + 44), new Point(x + 6, body.Top + 56));
            dc.DrawLine(CreatePen(hex, 2.0), new Point(x + 6, body.Top + 56), new Point(x, body.Top + 64));
            dc.DrawLine(CreatePen(hex, 2.0), new Point(x, body.Top + 64), new Point(x, body.Bottom - 4));
        }

        DrawAuxPairColumn(dc, rn.X + rn.Nc95.RelX, body, !rn.IsTripped, "95-96", "#F97316", dpiScale);
        DrawAuxPairColumn(dc, rn.X + rn.No97.RelX, body, rn.IsTripped, "97-98", "#10B981", dpiScale);

        Rect ctrlBox = new(body.X + 8, body.Bottom - 50, 108, 42);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(rn.IsTripped ? "#EF4444" : "#475569", 1.3), ctrlBox, 5, 5);

        string modeTag = rn.AutoReset ? "AUTO" : "MAN";
        string info = rn.IsTripped
            ? LocalizationManager.Get("Str.Vis.OverloadTrip")
            : $"{rangeLbl} [{modeTag}] {rn.MeasuredCurrentAmps:0.0}A";
        var infoFt = FormatText(info, 8.0, BoldTypeface, CreateSolid(rn.IsTripped ? "#FCA5A5" : "#E2E8F0"), dpiScale);
        dc.DrawText(infoFt, new Point(ctrlBox.X + 5, ctrlBox.Y + 3));

        Rect barBg = new(ctrlBox.X + 5, ctrlBox.Y + 17, ctrlBox.Width - 10, 4);
        dc.DrawRoundedRectangle(CreateSolid("#0F172A"), null, barBg, 2, 2);
        if (rn.ThermalLoadRatio > 0.01)
        {
            string barCol = rn.IsTripped ? "#EF4444" : "#F59E0B";
            dc.DrawRoundedRectangle(CreateSolid(barCol), null,
                new Rect(barBg.X, barBg.Y, barBg.Width * Math.Clamp(rn.ThermalLoadRatio, 0, 1), barBg.Height), 2, 2);
        }

        Rect btnTest = new(ctrlBox.X + 5, ctrlBox.Bottom - 17, 46, 14);
        Rect btnReset = new(ctrlBox.Right - 51, ctrlBox.Bottom - 17, 46, 14);

        dc.DrawRoundedRectangle(CreateSolid("#9A3412"), CreatePen("#FDBA74", 0.9), btnTest, 3, 3);
        var tFt = FormatText("TEST", 7.8, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(tFt, new Point(btnTest.X + (btnTest.Width - tFt.Width) / 2, btnTest.Y + 1));

        dc.DrawRoundedRectangle(CreateSolid("#1D4ED8"), CreatePen("#93C5FD", 0.9), btnReset, 3, 3);
        var rFt = FormatText("RESET", 7.8, BoldTypeface, Brushes.White, dpiScale);
        dc.DrawText(rFt, new Point(btnReset.X + (btnReset.Width - rFt.Width) / 2, btnReset.Y + 1));

        hitZones.Add(new ComponentHitZone(rn, btnTest, "THERMAL_TEST_TRIP", AllowedInDesignMode: true));
        hitZones.Add(new ComponentHitZone(rn, btnReset, "THERMAL_RESET", AllowedInDesignMode: true));
    }

    private static void DrawAuxPairColumn(DrawingContext dc, double x, Rect body, bool closed, string title, string colorHex, double dpiScale)
    {
        dc.DrawLine(CreatePen("#64748B", 1.5), new Point(x, body.Top + 4), new Point(x, body.Top + 32));
        dc.DrawLine(CreatePen("#64748B", 1.5), new Point(x, body.Bottom - 32), new Point(x, body.Bottom - 4));

        Point p1 = new(x, body.Top + 32);
        Point p2 = closed ? new Point(x, body.Bottom - 32) : new Point(x + 8, body.Bottom - 36);
        dc.DrawLine(CreatePen(closed ? colorHex : "#64748B", closed ? 2.5 : 1.7), p1, p2);

        var ft = FormatText(title, 7.8, BoldTypeface, CreateSolid(colorHex), dpiScale);
        dc.DrawText(ft, new Point(x - ft.Width / 2, body.Top + 6));
    }

    private static void DrawTimerH3Cr(DrawingContext dc, TimerOnDelayComponent timer, Rect body, CanvasThemeColors theme, double dpiScale, List<ComponentHitZone> hitZones)
    {
        string modeTitle = timer.OperatingMode switch
        {
            TimerOperatingMode.OffDelay => "OFF-DLY",
            TimerOperatingMode.StarDelta => "Y→Δ",
            _ => "ON-DLY"
        };
        var (_, _, coilLbl) = ElectricalAnalysisHelper.GetRatingSpec(timer.CoilRating);

        dc.DrawText(FormatText($"{modeTitle} • {coilLbl}", 8.5, BoldTypeface, CreateSolid(theme.IsDark ? "#E879F9" : "#9333EA"), dpiScale),
            new Point(body.X + 10, body.Y + 8));

        Brush pwrLed = timer.IsCoilEnergized ? CreateSolid("#22C55E") : CreateSolid("#64748B");
        Brush outLed = timer.IsTimedOut ? CreateSolid("#F59E0B") : CreateSolid("#64748B");
        dc.DrawEllipse(pwrLed, null, new Point(body.Right - 24, body.Y + 14), 3.8, 3.8);
        dc.DrawEllipse(outLed, null, new Point(body.Right - 12, body.Y + 14), 3.8, 3.8);

        Rect lcd = new(body.X + 10, body.Y + 25, body.Width - 20, 38);
        dc.DrawRoundedRectangle(CreateSolid(theme.InsetPanelHex), CreatePen(timer.IsCoilEnergized || timer.IsCountingActive ? "#D946EF" : "#334155", 1.4), lcd, 5, 5);

        double displaySec = (timer.IsCoilEnergized || timer.IsCountingActive) ? timer.RemainingSeconds : timer.DelaySeconds;
        string secStr = $"{displaySec:0.0} s";
        Brush digitBrush = timer.IsTimedOut
            ? CreateSolid("#4ADE80")
            : (timer.IsCountingActive ? CreateSolid("#FDE047") : CreateSolid("#E2E8F0"));
        var digitTxt = FormatText(secStr, 14.5, MonoTypeface, digitBrush, dpiScale);
        dc.DrawText(digitTxt, new Point(lcd.X + (lcd.Width - digitTxt.Width) / 2, lcd.Y + 4));

        Rect progBg = new(lcd.X + 6, lcd.Bottom - 9, lcd.Width - 12, 4.5);
        dc.DrawRoundedRectangle(CreateSolid("#0F172A"), null, progBg, 2, 2);
        if ((timer.IsCountingActive || timer.IsTimedOut) && timer.DelaySeconds > 0.01)
        {
            double ratio = Math.Clamp(1.0 - (timer.RemainingSeconds / timer.DelaySeconds), 0.0, 1.0);
            dc.DrawRoundedRectangle(CreateSolid(timer.IsTimedOut ? "#22C55E" : "#E879F9"), null,
                new Rect(progBg.X, progBg.Y, progBg.Width * ratio, progBg.Height), 2, 2);
        }

        Rect btnMinus = new(body.X + 10, body.Y + 69, 26, 22);
        Rect btnPlus = new(body.Right - 36, body.Y + 69, 26, 22);
        DrawMiniStepButton(dc, btnMinus, "-1s", dpiScale);
        DrawMiniStepButton(dc, btnPlus, "+1s", dpiScale);

        var setLbl = FormatText($"{LocalizationManager.Get("Str.Vis.SetPrefix")} {timer.DelaySeconds:0.#}s", 8.8, UiTypeface, CreateSolid(theme.SubTextHex), dpiScale);
        dc.DrawText(setLbl, new Point(body.X + (body.Width - setLbl.Width) / 2, body.Y + 73));

        hitZones.Add(new ComponentHitZone(timer, btnMinus, "TIMER_MINUS", AllowedInDesignMode: true));
        hitZones.Add(new ComponentHitZone(timer, btnPlus, "TIMER_PLUS", AllowedInDesignMode: true));

        string clsd = LocalizationManager.Get("Str.Vis.Closed");
        string opn = LocalizationManager.Get("Str.Vis.Open");
        string statusLine = timer.OperatingMode == TimerOperatingMode.StarDelta
            ? $"Y:{(timer.IsStarActive ? clsd : opn)}   Δ:{(timer.IsDeltaActive ? clsd : opn)}"
            : $"NC:{(!timer.IsTimedOutputActive ? clsd : opn)}   NO:{(timer.IsTimedOutputActive ? clsd : opn)}";

        var stFt = FormatText(statusLine, 8.0, BoldTypeface, CreateSolid(theme.NameTagTextHex), dpiScale);
        dc.DrawText(stFt, new Point(body.X + (body.Width - stFt.Width) / 2, body.Bottom - 18));

        DrawSupplyMismatchPill(dc, body, timer.SupplyStatus, dpiScale);
    }

    private static void DrawPilotLight(DrawingContext dc, PilotLightComponent pilot, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        Point center = new(body.X + body.Width / 2.0, body.Y + 40);
        bool lit = pilot.IsLit && !pilot.IsBurnedOut;

        (string offHex, string onHex, string glowHex, string coreHex) = pilot.LightColor switch
        {
            PilotLightColor.Red => ("#451A1A", "#EF4444", "#CCEF4444", "#FEE2E2"),
            PilotLightColor.Yellow => ("#423513", "#FACC15", "#CCFACC15", "#FEF9C3"),
            PilotLightColor.White => ("#334155", "#F8FAFC", "#CCE2E8F0", "#FFFFFF"),
            _ => ("#133C26", "#22C55E", "#CC22C55E", "#DCFCE7")
        };

        if (lit)
            dc.DrawEllipse(CreateRadialGlow(glowHex, "#00000000"), null, center, 38, 38);

        dc.DrawEllipse(CreateSolid("#0F172A"), CreatePen("#64748B", 2.0), center, 22, 22);
        dc.DrawEllipse(CreateSolid(lit ? onHex : offHex), CreatePen("#E2E8F0", lit ? 1.6 : 0.8), center, 17.5, 17.5);

        if (lit)
            dc.DrawEllipse(CreateSolid(coreHex), null, new Point(center.X - 4, center.Y - 4), 6.5, 6.5);
        else
            dc.DrawEllipse(CreateSolid("#33FFFFFF"), null, new Point(center.X - 5, center.Y - 5), 4.0, 4.0);

        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(pilot.VoltageRating);
        var st = FormatText(vSpec, 8.5, BoldTypeface, CreateSolid(theme.SubTextHex), dpiScale);
        dc.DrawText(st, new Point(center.X - st.Width / 2, body.Bottom - 19));

        DrawSupplyMismatchPill(dc, body, pilot.SupplyStatus, dpiScale);
    }

    private static void DrawResidentialLamp(DrawingContext dc, ResidentialLampComponent lamp, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        Point bulbCenter = new(body.X + body.Width / 2.0, body.Y + 35);
        bool lit = lamp.IsLit && !lamp.IsBurnedOut;

        (string coreHex, string edgeHex, string rayHex) = lamp.LampColor switch
        {
            PilotLightColor.Green => ("#DCFCE7", "#22C55E", "#4ADE80"),
            PilotLightColor.Red => ("#FEE2E2", "#EF4444", "#F87171"),
            PilotLightColor.White => ("#FFFFFF", "#E2E8F0", "#94A3B8"),
            _ => ("#FFFBEB", "#FACC15", "#F59E0B")
        };

        if (lit)
        {
            dc.DrawEllipse(CreateRadialGlow("#CC" + edgeHex[1..], "#00" + edgeHex[1..]), null, bulbCenter, 46, 46);
            Pen rayPen = CreatePen(rayHex, 2.0);
            for (int i = 0; i < 8; i++)
            {
                double rad = i * Math.PI / 4.0;
                Point r1 = new(bulbCenter.X + Math.Cos(rad) * 23, bulbCenter.Y + Math.Sin(rad) * 23);
                Point r2 = new(bulbCenter.X + Math.Cos(rad) * 31, bulbCenter.Y + Math.Sin(rad) * 31);
                dc.DrawLine(rayPen, r1, r2);
            }
        }

        Brush glassBrush = lit
            ? CreateRadialGlow(coreHex, edgeHex)
            : CreateVerticalGradient("#334155", "#1E293B");
        dc.DrawEllipse(glassBrush, CreatePen(lit ? rayHex : "#64748B", lit ? 2.0 : 1.5), bulbCenter, 19, 19);

        Pen filPen = CreatePen(lit ? "#EA580C" : "#64748B", 1.5);
        dc.DrawLine(filPen, new Point(bulbCenter.X - 6, bulbCenter.Y + 11), new Point(bulbCenter.X - 5, bulbCenter.Y - 3));
        dc.DrawLine(filPen, new Point(bulbCenter.X - 5, bulbCenter.Y - 3), new Point(bulbCenter.X, bulbCenter.Y + 2));
        dc.DrawLine(filPen, new Point(bulbCenter.X, bulbCenter.Y + 2), new Point(bulbCenter.X + 5, bulbCenter.Y - 3));
        dc.DrawLine(filPen, new Point(bulbCenter.X + 5, bulbCenter.Y - 3), new Point(bulbCenter.X + 6, bulbCenter.Y + 11));

        Rect baseNeck = new(bulbCenter.X - 10, bulbCenter.Y + 16, 20, 12);
        dc.DrawRoundedRectangle(CreateSolid("#94A3B8"), CreatePen("#475569", 1.1), baseNeck, 3, 3);

        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(lamp.VoltageRating);
        var vFt = FormatText(vSpec, 8.2, BoldTypeface, CreateSolid(theme.SubTextHex), dpiScale);
        dc.DrawText(vFt, new Point(bulbCenter.X - vFt.Width / 2, body.Bottom - 17));

        DrawSupplyMismatchPill(dc, body, lamp.SupplyStatus, dpiScale);
    }

    private static void DrawMotor1Phase(DrawingContext dc, Motor1PhaseComponent motor, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        Point center = new(body.X + body.Width / 2.0, body.Y + 68);
        DrawMotorStatorAndRotor(dc, center, 38, motor.RotorAngle, motor.IsRunning, 1, false, "M 1~ 220V", theme, dpiScale);
    }

    private static void DrawMotor3Phase(DrawingContext dc, Motor3PhaseBaseComponent motor, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        bool is3Pin = motor is Motor3Phase3PinComponent;
        Point center = new(body.X + body.Width / 2.0, is3Pin ? body.Y + body.Height / 2.0 + 7 : body.Y + body.Height / 2.0);
        bool spinning = Math.Abs(motor.CurrentAngularVelocity) > 2.0 && !motor.IsPhaseLost;

        string idleTag = motor switch
        {
            Motor3Phase3PinComponent m3p => m3p.InternalConnection == Motor3PinConnection.Delta_220V ? "Δ 220V" : "Y 380V",
            Motor3Phase6PinComponent m6p => m6p.WindingType == Motor6PinWindingType.Dahlander2Speed ? "2-SPD" : "Y-Δ 6P",
            _ => "3P"
        };

        string specBadge = spinning
            ? $"{motor.ConnectionModeLabel} • {motor.OperatingCurrentAmps:0.#}A"
            : $"{idleTag} • {motor.RatedPowerKw:0.#}kW";

        DrawMotorStatorAndRotor(dc, center, is3Pin ? 39 : 43, motor.RotorAngle, spinning, motor.RotationDirection, motor.IsPhaseLost, specBadge, theme, dpiScale);

        foreach (var t in motor.Terminals)
        {
            double ty = t.Side == TerminalSide.Top ? body.Top + 4 : body.Bottom - 16;
            string col = t.Name switch
            {
                "U1" or "U2" => "#EF4444",
                "V1" or "V2" => "#CA8A04",
                "W1" or "W2" => "#2563EB",
                "PE" => "#16A34A",
                _ => theme.SubTextHex
            };
            var lbl = FormatText(t.Name, 8.8, BoldTypeface, CreateSolid(col), dpiScale);
            dc.DrawText(lbl, new Point(motor.X + t.RelX - lbl.Width / 2, ty));
        }
    }

    private static void DrawMotorStatorAndRotor(DrawingContext dc, Point center, double radius, double rotorAngleDeg, bool isSpinning, int direction, bool isPhaseLost, string badgeText, CanvasThemeColors theme, double dpiScale)
    {
        Pen finPen = CreatePen(theme.BorderDefaultHex, 3.0);
        dc.DrawLine(finPen, new Point(center.X - radius - 6, center.Y - 16), new Point(center.X + radius + 6, center.Y - 16));
        dc.DrawLine(finPen, new Point(center.X - radius - 8, center.Y), new Point(center.X + radius + 8, center.Y));
        dc.DrawLine(finPen, new Point(center.X - radius - 6, center.Y + 16), new Point(center.X + radius + 6, center.Y + 16));

        Pen outerPen = isPhaseLost
            ? CreatePen("#EF4444", 2.8)
            : (isSpinning ? CreatePen("#06B6D4", 2.4) : CreatePen("#64748B", 2.0));
        dc.DrawEllipse(CreateSolid(theme.InsetPanelHex), outerPen, center, radius, radius);

        dc.PushTransform(new RotateTransform(rotorAngleDeg, center.X, center.Y));
        Brush bladeBrush = isSpinning ? CreateSolid("#38BDF8") : CreateSolid("#64748B");
        Pen bladePen = CreatePen("#E2E8F0", 0.9);

        for (int b = 0; b < 4; b++)
        {
            dc.PushTransform(new RotateTransform(b * 90.0, center.X, center.Y));
            Rect blade = new(center.X - 5, center.Y - radius + 7, 10, radius - 10);
            dc.DrawRoundedRectangle(bladeBrush, bladePen, blade, 4, 4);
            dc.Pop();
        }
        dc.DrawEllipse(CreateSolid("#FACC15"), null, new Point(center.X, center.Y - radius + 13), 3.5, 3.5);
        dc.Pop();

        dc.DrawEllipse(CreateSolid("#1E293B"), CreatePen("#E2E8F0", 1.6), center, 9, 9);
        dc.DrawEllipse(CreateSolid("#94A3B8"), null, center, 3.5, 3.5);

        if (isPhaseLost)
        {
            string plText = LocalizationManager.Get("Str.Vis.PhaseLoss");
            var errTxt = FormatText(plText, 9.5, BoldTypeface, Brushes.White, dpiScale);
            Rect errBox = new(center.X - errTxt.Width / 2 - 8, center.Y - 12, errTxt.Width + 16, 24);
            dc.DrawRoundedRectangle(CreateSolid("#EEB91C1C"), CreatePen("#FCA5A5", 1.5), errBox, 5, 5);
            dc.DrawText(errTxt, new Point(center.X - errTxt.Width / 2, errBox.Y + 4));
        }
        else
        {
            string dirLabel = isSpinning ? (direction >= 0 ? $"↻ {badgeText}" : $"↺ {badgeText}") : badgeText;
            string pillColor = isSpinning ? (direction >= 0 ? "#DD065F46" : "#DD9A3412") : "#DD1E293B";
            string textHex = isSpinning ? (direction >= 0 ? "#6EE7B7" : "#FDBA74") : "#CBD5E1";

            var dirFt = FormatText(dirLabel, 8.2, BoldTypeface, CreateSolid(textHex), dpiScale);
            Rect pill = new(center.X - dirFt.Width / 2 - 5, center.Y + radius - 14, dirFt.Width + 10, 16);
            dc.DrawRoundedRectangle(CreateSolid(pillColor), CreatePen("#475569", 0.8), pill, 4, 4);
            dc.DrawText(dirFt, new Point(pill.X + 5, pill.Y + 1.5));
        }
    }

    private static void DrawGenericModularBlock(DrawingContext dc, ElectricalComponent comp, Rect body, CanvasThemeColors theme, double dpiScale)
    {
        var title = FormatText(comp.TypeKey, 11, BoldTypeface, CreateSolid("#0284C7"), dpiScale);
        dc.DrawText(title, new Point(body.X + (body.Width - title.Width) / 2, body.Y + body.Height / 2 - 8));
        foreach (var t in comp.Terminals)
        {
            double ty = t.Side == TerminalSide.Top ? body.Top + 4 : body.Bottom - 16;
            var ft = FormatText(t.Name, 8.5, BoldTypeface, CreateSolid(theme.SubTextHex), dpiScale);
            dc.DrawText(ft, new Point(comp.X + t.RelX - ft.Width / 2, ty));
        }
    }

    private static void DrawTerminal(DrawingContext dc, Terminal term, bool isHovered, bool isWireStart, bool isSimulating, CanvasThemeColors theme)
    {
        Point pos = new(term.CanvasX, term.CanvasY);

        if (isWireStart)
            dc.DrawEllipse(CreateRadialGlow("#AA22D3EE", "#0022D3EE"), null, pos, 14, 14);
        else if (isHovered)
            dc.DrawEllipse(CreateRadialGlow("#8838BDF8", "#0038BDF8"), null, pos, 12, 12);
        else if (isSimulating && term.IsEnergized)
            dc.DrawEllipse(CreateRadialGlow("#66FACC15", "#00FACC15"), null, pos, 10, 10);

        string fillHex = isWireStart
            ? "#22D3EE"
            : (isHovered ? "#38BDF8" : (isSimulating && term.IsEnergized ? "#FBBF24" : (theme.IsDark ? "#CBD5E1" : "#94A3B8")));

        string ringHex = isWireStart || isHovered ? "#0284C7" : "#0F172A";
        double radius = isHovered || isWireStart ? 6.5 : 5.3;

        dc.DrawEllipse(CreateSolid(fillHex), CreatePen(ringHex, 1.5), pos, radius, radius);
        dc.DrawLine(CreatePen("#1E293B", 1.1), new Point(pos.X - 2.5, pos.Y - 2.5), new Point(pos.X + 2.5, pos.Y + 2.5));
    }

    private static void DrawMiniStepButton(DrawingContext dc, Rect rect, string text, double dpiScale)
    {
        dc.DrawRoundedRectangle(CreateSolid("#1E293B"), CreatePen("#475569", 1.0), rect, 4, 4);
        var ft = FormatText(text, 9.0, BoldTypeface, CreateSolid("#E2E8F0"), dpiScale);
        dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2, rect.Y + (rect.Height - ft.Height) / 2));
    }

    public static FormattedText FormatText(string text, double fontSize, Typeface typeface, Brush brush, double dpiScale) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, brush, dpiScale);

    public static SolidColorBrush CreateSolid(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public static Pen CreatePen(string hex, double thickness)
    {
        var p = new Pen(CreateSolid(hex), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    private static LinearGradientBrush CreateVerticalGradient(string topHex, string bottomHex)
    {
        var g = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(topHex)!,
            (Color)ColorConverter.ConvertFromString(bottomHex)!,
            90.0);
        g.Freeze();
        return g;
    }

    public static RadialGradientBrush CreateRadialGlow(string centerHex, string edgeHex)
    {
        var g = new RadialGradientBrush(
            (Color)ColorConverter.ConvertFromString(centerHex)!,
            (Color)ColorConverter.ConvertFromString(edgeHex)!);
        g.Freeze();
        return g;
    }
}
