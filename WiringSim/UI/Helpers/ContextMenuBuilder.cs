using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.UI.Dialogs;
using WiringSim.UI.Services;

namespace WiringSim.UI.Helpers;

/// <summary>
/// Xây dựng Menu Chuột Phải Động (Context-Sensitive Properties) 100% đồng bộ Theme Sáng/Tối
/// (không có dải trắng icon mặc định) và 100% Đa ngôn ngữ (LocalizationManager).
/// </summary>
public static class ContextMenuBuilder
{
    public static ContextMenu BuildComponentMenu(
        ElectricalComponent comp,
        AppMode currentMode,
        Window? ownerWindow,
        Action<string> onChangedMessage,
        Action onDeleteRequested,
        Action<VfdComponent>? onOpenVfdKeypad = null,
        Action<PlcControllerComponent, bool>? onOpenPlcLadder = null)
    {
        bool isDesign = currentMode == AppMode.Design;
        var menu = new ContextMenu();

        menu.Items.Add(CreateSectionHeader($"⚙ {comp.DisplayName}  ({comp.TypeKey})"));
        menu.Items.Add(new Separator());

        if (comp.IsBurnedOut)
        {
            string repairKey = comp is ITwoWireSensorDevice { IsTwoWireActiveMode: true }
                ? "Str.Menu.RepairSensor"
                : "Str.Menu.RepairBurnout";
            menu.Items.Add(CreateActionItem(LocalizationManager.Get(repairKey), () =>
            {
                if (comp is ITwoWireSensorDevice tw)
                    tw.RepairDevice();
                else
                    comp.RepairBurnout();
                onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, "OK"));
            }, "#22C55E"));
            menu.Items.Add(new Separator());
        }

        switch (comp)
        {
            case DcSourceComponent dcSrc:
                BuildDcSourceItems(menu, dcSrc, onChangedMessage);
                break;
            case Ac1PhaseSourceComponent ac1:
                BuildAc1PhaseItems(menu, ac1, onChangedMessage);
                break;
            case Ac3PhaseSourceComponent ac3:
                BuildAc3PhaseItems(menu, ac3, onChangedMessage);
                break;
            case ICircuitBreaker mcb:
                BuildMcbItems(menu, comp, mcb, onChangedMessage);
                break;
            case PushButtonComponent pb:
                BuildPushButtonItems(menu, pb, isDesign, onChangedMessage);
                break;
            case IlluminatedPushButtonComponent ipb:
                BuildIlluminatedPushButtonItems(menu, ipb, onChangedMessage);
                break;
            case EmergencyStopComponent estop:
                BuildEmergencyStopItems(menu, estop, isDesign, onChangedMessage);
                break;
            case FuseComponent fuse:
                BuildFuseItems(menu, fuse, onChangedMessage);
                break;
            case KnifeSwitchComponent knife:
                BuildKnifeSwitchItems(menu, knife, isDesign, onChangedMessage);
                break;
            case CamSwitchComponent cam:
                BuildCamSwitchItems(menu, cam, isDesign, onChangedMessage);
                break;
            case SwitchComponent sw:
                BuildSwitchItems(menu, sw, isDesign, onChangedMessage);
                break;
            case LimitSwitchComponent ls:
                BuildLimitSwitchItems(menu, ls, onChangedMessage);
                break;
            case VfdComponent vfd:
                menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.VfdOpenKeypad"), () =>
                {
                    onOpenVfdKeypad?.Invoke(vfd);
                }, "#38BDF8"));
                break;
            case PlcControllerComponent plc:
                BuildPlcItems(menu, plc, onChangedMessage, onOpenPlcLadder);
                break;
            case ContactorComponent km:
                BuildContactorItems(menu, km, isDesign, onChangedMessage);
                break;
            case IntermediateRelayComponent my:
                BuildIntermediateRelayItems(menu, my, isDesign, onChangedMessage);
                break;
            case ThermalOverloadRelayComponent rn:
                BuildThermalRelayItems(menu, rn, onChangedMessage);
                break;
            case TimerOnDelayComponent timer:
                BuildTimerItems(menu, timer, isDesign, ownerWindow, onChangedMessage);
                break;
            case RotaryTimerAh3Component ah3:
                BuildRotaryTimerAh3Items(menu, ah3, ownerWindow, onChangedMessage);
                break;
            case IndustrialSensorComponent sensor:
                BuildSensorItems(menu, sensor, isDesign, onChangedMessage);
                break;
            case CylinderReedSwitchComponent reed:
                BuildReedSwitchItems(menu, reed, onChangedMessage);
                break;
            case ThermalSwitchComponent thermSw:
                BuildThermalSwitchItems(menu, thermSw, onChangedMessage);
                break;
            case FloatSwitchComponent floatSw:
                BuildFloatSwitchItems(menu, floatSw, onChangedMessage);
                break;
            case VoltmeterComponent vm:
                BuildVoltmeterItems(menu, vm, onChangedMessage);
                break;
            case AmmeterComponent am:
                BuildAmmeterItems(menu, am, onChangedMessage);
                break;
            case CurrentTransformerComponent ct:
                BuildCtItems(menu, ct, onChangedMessage);
                break;
            case PilotLightComponent pilot:
                BuildPilotLightItems(menu, pilot, onChangedMessage);
                break;
            case ResidentialLampComponent lamp:
                BuildResidentialLampItems(menu, lamp, onChangedMessage);
                break;
            case MotorLinearTrackComponent track:
                BuildMotorLinearTrackItems(menu, track, onChangedMessage);
                break;
            case Motor3Phase3PinComponent m3p:
                BuildMotor3PinItems(menu, m3p, onChangedMessage);
                break;
            case Motor3Phase6PinComponent m6p:
                BuildMotor6PinItems(menu, m6p, onChangedMessage);
                break;
        }

        menu.Items.Add(new Separator());

        // Menu Xoay & Lật thiết bị (hỗ trợ mọi thiết bị trên vùng vẽ)
        var transformSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.Transform"));
        transformSub.IsEnabled = isDesign;
        transformSub.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.RotateCW"), () =>
        {
            comp.RotateCW();
            onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, $"{comp.RotationAngle}°"));
        }));
        transformSub.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.RotateCCW"), () =>
        {
            comp.RotateCCW();
            onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, $"{comp.RotationAngle}°"));
        }));
        transformSub.Items.Add(new Separator());
        transformSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.FlipH"), comp.IsFlippedHorizontal, () =>
        {
            comp.FlipHorizontal();
            onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, "FlipH"));
        }));
        transformSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.FlipV"), comp.IsFlippedVertical, () =>
        {
            comp.FlipVertical();
            onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, "FlipV"));
        }));
        menu.Items.Add(transformSub);

        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.Rename"), () =>
        {
            string? nextName = InputPromptDialog.Prompt(
                ownerWindow,
                LocalizationManager.Get("Str.Menu.RenameTitle"),
                LocalizationManager.Format("Str.Menu.RenamePrompt", comp.DisplayName),
                comp.DisplayName);

            if (!string.IsNullOrWhiteSpace(nextName))
            {
                comp.DisplayName = nextName;
                onChangedMessage(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, nextName));
            }
        }));

        var deleteItem = CreateActionItem(LocalizationManager.Get("Str.Menu.DeleteComp"), onDeleteRequested, "#EF4444");
        deleteItem.IsEnabled = isDesign;
        menu.Items.Add(deleteItem);

        return menu;
    }

    public static ContextMenu BuildWireMenu(
        WireModel wire,
        AppMode currentMode,
        Action<WireColorPreset> onChangeColor,
        Action onAddWaypointHere,
        Action onRemoveNearestWaypoint,
        Action onResetAutoRoute,
        Action onDeleteWire,
        Action? onStartBranchHere = null)
    {
        bool isDesign = currentMode == AppMode.Design;
        var menu = new ContextMenu();

        string wireTitle = LocalizationManager.Format(
            "Str.Menu.WireHeader",
            $"{wire.TerminalA.Owner.DisplayName}:{wire.TerminalA.Name}",
            $"{wire.TerminalB.Owner.DisplayName}:{wire.TerminalB.Name}");
        menu.Items.Add(CreateSectionHeader(wireTitle));
        menu.Items.Add(new Separator());

        if (onStartBranchHere is not null)
        {
            var branchItem = CreateActionItem(LocalizationManager.Get("Str.Menu.WireBranchHere"), onStartBranchHere, "#38BDF8");
            branchItem.IsEnabled = isDesign;
            menu.Items.Add(branchItem);
        }

        var colorSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.WireChangeColor"));
        (WireColorPreset Preset, string Key)[] colors =
        [
            (WireColorPreset.Red_L1_DCPlus, "Str.Wire.FullRed"),
            (WireColorPreset.Yellow_L2, "Str.Wire.FullYellow"),
            (WireColorPreset.Blue_L3, "Str.Wire.FullBlue"),
            (WireColorPreset.Black_N_DCMinus, "Str.Wire.FullBlack"),
            (WireColorPreset.Green_PE, "Str.Wire.FullGreen")
        ];
        foreach (var (preset, key) in colors)
        {
            colorSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), wire.ColorPreset == preset, () => onChangeColor(preset)));
        }
        menu.Items.Add(colorSub);

        var addWp = CreateActionItem(LocalizationManager.Get("Str.Menu.WireAddWp"), onAddWaypointHere);
        addWp.IsEnabled = isDesign;
        menu.Items.Add(addWp);

        var removeWp = CreateActionItem(LocalizationManager.Get("Str.Menu.WireRemoveWp"), onRemoveNearestWaypoint);
        removeWp.IsEnabled = isDesign && wire.Waypoints.Count > 0;
        menu.Items.Add(removeWp);

        var resetAuto = CreateActionItem(LocalizationManager.Get("Str.Menu.WireResetAuto"), onResetAutoRoute);
        resetAuto.IsEnabled = isDesign && wire.Waypoints.Count > 0;
        menu.Items.Add(resetAuto);

        menu.Items.Add(new Separator());

        var delWire = CreateActionItem(LocalizationManager.Get("Str.Menu.WireDelete"), onDeleteWire, "#EF4444");
        delWire.IsEnabled = isDesign;
        menu.Items.Add(delWire);

        return menu;
    }

    // =========================================================================
    // MENU CẤU HÌNH CHUYÊN BIỆT CHO TỪNG THIẾT BỊ
    // =========================================================================

    private static void BuildDcSourceItems(ContextMenu menu, DcSourceComponent dc, Action<string> notify)
    {
        var sub = CreateSubMenu(LocalizationManager.Format("Str.Menu.OutputVoltage", $"{dc.OutputVoltage:0}VDC"));
        foreach (double v in DcSourceComponent.SupportedVoltages)
        {
            double target = v;
            sub.Items.Add(CreateCheckOption($"{target:0}V DC", Math.Abs(dc.OutputVoltage - target) < 0.5, () =>
            {
                dc.SetVoltage(target);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", dc.DisplayName, $"{target:0}VDC"));
            }));
        }
        menu.Items.Add(sub);
    }

    private static void BuildAc1PhaseItems(ContextMenu menu, Ac1PhaseSourceComponent ac1, Action<string> notify)
    {
        var vSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.OutputVoltage", $"{ac1.OutputVoltage:0}VAC"));
        foreach (double v in new[] { 24.0, 110.0, 220.0, 380.0 })
        {
            double target = v;
            vSub.Items.Add(CreateCheckOption($"{target:0}V AC", Math.Abs(ac1.OutputVoltage - target) < 0.5, () =>
            {
                ac1.OutputVoltage = target;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac1.DisplayName, $"{target:0}VAC"));
            }));
        }
        menu.Items.Add(vSub);

        menu.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LostL"), ac1.IsPhaseL1Lost, () =>
        {
            ac1.IsPhaseL1Lost = !ac1.IsPhaseL1Lost;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac1.DisplayName, ac1.IsPhaseL1Lost ? "L OFF" : "L ON"));
        }));
    }

    private static void BuildAc3PhaseItems(ContextMenu menu, Ac3PhaseSourceComponent ac3, Action<string> notify)
    {
        var vSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.OutputVoltage", $"{ac3.LineVoltage:0}VAC"));
        foreach (double v in new[] { 220.0, 380.0 })
        {
            double target = v;
            vSub.Items.Add(CreateCheckOption($"{target:0}V AC (3P)", Math.Abs(ac3.LineVoltage - target) < 1.0, () =>
            {
                ac3.LineVoltage = target;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac3.DisplayName, $"{target:0}VAC"));
            }));
        }
        menu.Items.Add(vSub);

        var lossSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.SimPhaseLoss"));
        lossSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LostL1"), ac3.IsL1Lost, () =>
        {
            ac3.IsL1Lost = !ac3.IsL1Lost;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac3.DisplayName, $"L1={(ac3.IsL1Lost ? "LOST" : "OK")}"));
        }));
        lossSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LostL2"), ac3.IsL2Lost, () =>
        {
            ac3.IsL2Lost = !ac3.IsL2Lost;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac3.DisplayName, $"L2={(ac3.IsL2Lost ? "LOST" : "OK")}"));
        }));
        lossSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LostL3"), ac3.IsL3Lost, () =>
        {
            ac3.IsL3Lost = !ac3.IsL3Lost;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ac3.DisplayName, $"L3={(ac3.IsL3Lost ? "LOST" : "OK")}"));
        }));
        menu.Items.Add(lossSub);
    }

    private static void BuildMcbItems(ContextMenu menu, ElectricalComponent comp, ICircuitBreaker mcb, Action<string> notify)
    {
        var ampSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.RatedAmps", mcb.RatedAmps));
        foreach (int a in Mcb1PhaseComponent.SupportedRatings)
        {
            int target = a;
            ampSub.Items.Add(CreateCheckOption($"{target}A", mcb.RatedAmps == target, () =>
            {
                mcb.RatedAmps = target;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, $"{mcb.CurveType}{target}"));
            }));
        }
        menu.Items.Add(ampSub);

        var curveSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CurveType", mcb.CurveType));
        (McbCurveType C, string Key)[] curves =
        [
            (McbCurveType.B, "Str.Menu.CurveB"),
            (McbCurveType.C, "Str.Menu.CurveC"),
            (McbCurveType.D, "Str.Menu.CurveD")
        ];
        foreach (var (c, key) in curves)
        {
            curveSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), mcb.CurveType == c, () =>
            {
                mcb.CurveType = c;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", comp.DisplayName, $"{c}{mcb.RatedAmps}"));
            }));
        }
        menu.Items.Add(curveSub);
    }

    private static void BuildPushButtonItems(ContextMenu menu, PushButtonComponent pb, bool isDesign, Action<string> notify)
    {
        var actSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ActionMode"));
        actSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Momentary"), pb.ActionMode == PushButtonActionMode.Momentary, () =>
        {
            pb.ActionMode = PushButtonActionMode.Momentary;
            pb.IsPressed = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", pb.DisplayName, "Momentary"));
        }));
        actSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Latching"), pb.ActionMode == PushButtonActionMode.Latching, () =>
        {
            pb.ActionMode = PushButtonActionMode.Latching;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", pb.DisplayName, "Latching"));
        }));
        menu.Items.Add(actSub);

        var contactSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ContactCfg"));
        contactSub.IsEnabled = isDesign;
        (PushButtonContactConfig Cfg, string Key)[] cfgs =
        [
            (PushButtonContactConfig.Dual1NO1NC, "Str.Menu.Dual1NO1NC_LA38"),
            (PushButtonContactConfig.Only1NO, "Str.Menu.Only1NO_LA38"),
            (PushButtonContactConfig.Only1NC, "Str.Menu.Only1NC_LA38")
        ];
        foreach (var (cfg, key) in cfgs)
        {
            contactSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), pb.ContactConfig == cfg, () =>
            {
                pb.SetContactConfig(cfg);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", pb.DisplayName, cfg.ToString()));
            }));
        }
        menu.Items.Add(contactSub);

        var colSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.Color"));
        (PilotLightColor Col, string Key)[] btnCols =
        [
            (PilotLightColor.Green, "Str.Color.Green"),
            (PilotLightColor.Red, "Str.Color.Red"),
            (PilotLightColor.Yellow, "Str.Color.Yellow"),
            (PilotLightColor.Blue, "Str.Color.Blue")
        ];
        foreach (var (col, key) in btnCols)
        {
            colSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), pb.ButtonColor == col, () =>
            {
                pb.ButtonColor = col;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", pb.DisplayName, LocalizationManager.Get(key)));
            }));
        }
        menu.Items.Add(colSub);
    }

    private static void BuildIlluminatedPushButtonItems(ContextMenu menu, IlluminatedPushButtonComponent ipb, Action<string> notify)
    {
        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(ipb.VoltageRating);
        var vSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", vSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_12V, CoilVoltageRating.DC_24V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            vSub.Items.Add(CreateCheckOption(lbl, ipb.VoltageRating == target, () =>
            {
                ipb.VoltageRating = target;
                ipb.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ipb.DisplayName, lbl));
            }));
        }
        menu.Items.Add(vSub);

        var actSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ActionMode"));
        actSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Momentary"), ipb.ActionMode == PushButtonActionMode.Momentary, () =>
        {
            ipb.ActionMode = PushButtonActionMode.Momentary;
            ipb.IsPressed = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ipb.DisplayName, "Momentary"));
        }));
        actSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Latching"), ipb.ActionMode == PushButtonActionMode.Latching, () =>
        {
            ipb.ActionMode = PushButtonActionMode.Latching;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ipb.DisplayName, "Latching"));
        }));
        menu.Items.Add(actSub);

        var colSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.Color"));
        (PilotLightColor Col, string Key)[] cols =
        [
            (PilotLightColor.Green, "Str.Color.Green"),
            (PilotLightColor.Red, "Str.Color.Red"),
            (PilotLightColor.Yellow, "Str.Color.Yellow"),
            (PilotLightColor.Blue, "Str.Color.Blue"),
            (PilotLightColor.White, "Str.Color.White")
        ];
        foreach (var (col, key) in cols)
        {
            colSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), ipb.ButtonColor == col, () =>
            {
                ipb.ButtonColor = col;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ipb.DisplayName, LocalizationManager.Get(key)));
            }));
        }
        menu.Items.Add(colSub);
    }

    private static void BuildEmergencyStopItems(ContextMenu menu, EmergencyStopComponent es, bool isDesign, Action<string> notify)
    {
        var cfgSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ContactCfg"));
        cfgSub.IsEnabled = isDesign;
        (EmergencyStopContactConfig Cfg, string Key)[] cfgs =
        [
            (EmergencyStopContactConfig.Dual1NC1NO, "Str.Menu.EStopDual1NC1NO"),
            (EmergencyStopContactConfig.Only1NC, "Str.Menu.EStopOnly1NC"),
            (EmergencyStopContactConfig.Dual2NC, "Str.Menu.EStopDual2NC")
        ];
        foreach (var (cfg, key) in cfgs)
        {
            cfgSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), es.ContactConfig == cfg, () =>
            {
                es.SetContactConfig(cfg);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", es.DisplayName, cfg.ToString()));
            }));
        }
        menu.Items.Add(cfgSub);
    }

    private static void BuildFuseItems(ContextMenu menu, FuseComponent fuse, Action<string> notify)
    {
        if (fuse.IsBlown)
        {
            menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.FuseReset"), () =>
            {
                fuse.ResetFuse();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", fuse.DisplayName, "RESET OK"));
            }, "#22C55E"));
            menu.Items.Add(new Separator());
        }

        var rSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.FuseRating", fuse.RatedAmps));
        foreach (int amps in FuseComponent.SupportedRatings)
        {
            int target = amps;
            rSub.Items.Add(CreateCheckOption($"{target}A", fuse.RatedAmps == target, () =>
            {
                fuse.RatedAmps = target;
                fuse.ResetFuse();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", fuse.DisplayName, $"{target}A"));
            }));
        }
        menu.Items.Add(rSub);
    }

    private static void BuildKnifeSwitchItems(ContextMenu menu, KnifeSwitchComponent knife, bool isDesign, Action<string> notify)
    {
        string curLbl = knife.SwitchKind switch
        {
            KnifeSwitchKind.SinglePhase2Pole => "1P",
            KnifeSwitchKind.Changeover3P_1_0_2 => "1-0-2",
            _ => "3P"
        };
        var kSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.KnifeType", curLbl));
        kSub.IsEnabled = isDesign;
        (KnifeSwitchKind K, string Key)[] kinds =
        [
            (KnifeSwitchKind.SinglePhase2Pole, "Str.Menu.Knife1P"),
            (KnifeSwitchKind.ThreePhase3Pole, "Str.Menu.Knife3P"),
            (KnifeSwitchKind.Changeover3P_1_0_2, "Str.Menu.KnifeChangeover")
        ];
        foreach (var (k, key) in kinds)
        {
            kSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), knife.SwitchKind == k, () =>
            {
                knife.SetSwitchKind(k);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", knife.DisplayName, k.ToString()));
            }));
        }
        menu.Items.Add(kSub);
    }

    private static void BuildCamSwitchItems(ContextMenu menu, CamSwitchComponent cam, bool isDesign, Action<string> notify)
    {
        string mLbl = cam.Mode switch
        {
            CamSwitchMode.Volt4Pos => "Volt 4P",
            CamSwitchMode.Amp4Pos => "Amp 4P",
            _ => "Volt 7P"
        };
        var mSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CamMode", mLbl));
        mSub.IsEnabled = isDesign;
        (CamSwitchMode M, string Key)[] modes =
        [
            (CamSwitchMode.Volt7Pos, "Str.Menu.CamVolt7"),
            (CamSwitchMode.Volt4Pos, "Str.Menu.CamVolt4"),
            (CamSwitchMode.Amp4Pos, "Str.Menu.CamAmp4")
        ];
        foreach (var (m, key) in modes)
        {
            mSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), cam.Mode == m, () =>
            {
                cam.SetMode(m);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", cam.DisplayName, m.ToString()));
            }));
        }
        menu.Items.Add(mSub);
    }

    private static void BuildRotaryTimerAh3Items(ContextMenu menu, RotaryTimerAh3Component ah3, Window? ownerWindow, Action<string> notify)
    {
        var (_, _, cSpec) = ElectricalAnalysisHelper.GetRatingSpec(ah3.CoilRating);
        var coilSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", cSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_12V, CoilVoltageRating.DC_24V, CoilVoltageRating.AC_110V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            coilSub.Items.Add(CreateCheckOption(lbl, ah3.CoilRating == target, () =>
            {
                ah3.CoilRating = target;
                ah3.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ah3.DisplayName, lbl));
            }));
        }
        menu.Items.Add(coilSub);

        var rSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.Ah3Range", ah3.RangeShortLabel));
        (Ah3TimeRange R, string Key)[] ranges =
        [
            (Ah3TimeRange.Range_10s, "Str.Menu.Ah3Range10s"),
            (Ah3TimeRange.Range_30s, "Str.Menu.Ah3Range30s"),
            (Ah3TimeRange.Range_60s, "Str.Menu.Ah3Range60s"),
            (Ah3TimeRange.Range_10m, "Str.Menu.Ah3Range10m")
        ];
        foreach (var (r, key) in ranges)
        {
            rSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), ah3.TimeRange == r, () =>
            {
                ah3.SetTimeRange(r);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ah3.DisplayName, ah3.RangeShortLabel));
            }));
        }
        menu.Items.Add(rSub);

        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.CustomDelay"), () =>
        {
            string? input = InputPromptDialog.Prompt(
                ownerWindow,
                LocalizationManager.Get("Str.Menu.CustomDelayTitle"),
                LocalizationManager.Format("Str.Menu.Ah3CustomPrompt", ah3.MaxRangeSeconds),
                ah3.DelaySeconds.ToString("0.#", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(input) &&
                double.TryParse(input.Replace(',', '.'), CultureInfo.InvariantCulture, out double val))
            {
                ah3.SetDelaySeconds(val);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ah3.DisplayName, ah3.FormatDuration(ah3.DelaySeconds)));
            }
        }));
    }

    private static void BuildMotorLinearTrackItems(ContextMenu menu, MotorLinearTrackComponent track, Action<string> notify)
    {
        string posLbl = track.InitialPosition switch
        {
            TrackInitialPosition.Center => "50%",
            TrackInitialPosition.RightAtLS2 => "LS2",
            _ => "LS1"
        };
        var posSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.TrackInitPos", posLbl));
        (TrackInitialPosition P, string Key)[] positions =
        [
            (TrackInitialPosition.LeftAtLS1, "Str.Menu.TrackPosLeft"),
            (TrackInitialPosition.Center, "Str.Menu.TrackPosCenter"),
            (TrackInitialPosition.RightAtLS2, "Str.Menu.TrackPosRight")
        ];
        foreach (var (p, key) in positions)
        {
            posSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), track.InitialPosition == p, () =>
            {
                track.InitialPosition = p;
                track.ApplyInitialPosition();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", track.DisplayName, p.ToString()));
            }));
        }
        menu.Items.Add(posSub);

        var durSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.TrackDuration", track.TravelDurationSeconds));
        foreach (double s in MotorLinearTrackComponent.SupportedTravelDurations)
        {
            double target = s;
            durSub.Items.Add(CreateCheckOption(LocalizationManager.Format("Str.Menu.SecUnit", target), Math.Abs(track.TravelDurationSeconds - target) < 0.1, () =>
            {
                track.TravelDurationSeconds = target;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", track.DisplayName, $"{target:0.#}s"));
            }));
        }
        menu.Items.Add(durSub);

        AddMotorPowerSubmenu(menu, track, notify);
    }

    private static void BuildSwitchItems(ContextMenu menu, SwitchComponent sw, bool isDesign, Action<string> notify)
    {
        var typeSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.SwitchType"));
        typeSub.IsEnabled = isDesign;
        (SwitchKind Kind, string Key)[] kinds =
        [
            (SwitchKind.OneWay_1_2, "Str.Menu.Sw1Way"),
            (SwitchKind.TwoWay_Staircase, "Str.Menu.Sw2Way"),
            (SwitchKind.Rotary3Pos_1_0_2, "Str.Menu.SwRotary")
        ];
        foreach (var (kind, key) in kinds)
        {
            typeSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), sw.SwitchKind == kind, () =>
            {
                sw.SetSwitchKind(kind);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sw.DisplayName, LocalizationManager.Get(key)));
            }));
        }
        menu.Items.Add(typeSub);
    }

    private static void BuildLimitSwitchItems(ContextMenu menu, LimitSwitchComponent ls, Action<string> notify)
    {
        var modeSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.LimitMode"));
        modeSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LimitHold"), ls.ActionMode == PushButtonActionMode.Momentary, () =>
        {
            ls.ActionMode = PushButtonActionMode.Momentary;
            ls.IsActuated = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ls.DisplayName, "Momentary"));
        }));
        modeSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.LimitLock"), ls.ActionMode == PushButtonActionMode.Latching, () =>
        {
            ls.ActionMode = PushButtonActionMode.Latching;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ls.DisplayName, "Latching"));
        }));
        menu.Items.Add(modeSub);
    }

    private static void BuildContactorItems(ContextMenu menu, ContactorComponent km, bool isDesign, Action<string> notify)
    {
        var (_, _, curSpec) = ElectricalAnalysisHelper.GetRatingSpec(km.CoilRating);
        var coilSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", curSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_24V, CoilVoltageRating.AC_24V, CoilVoltageRating.AC_110V, CoilVoltageRating.AC_220V, CoilVoltageRating.AC_380V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            coilSub.Items.Add(CreateCheckOption(lbl, km.CoilRating == target, () =>
            {
                km.CoilRating = target;
                km.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", km.DisplayName, lbl));
            }));
        }
        menu.Items.Add(coilSub);

        var auxSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.AuxContacts"));
        auxSub.IsEnabled = isDesign;
        (ContactorAuxConfig Cfg, string Key)[] auxList =
        [
            (ContactorAuxConfig.Only1NO, "Str.Menu.Aux1NO"),
            (ContactorAuxConfig.Only1NC, "Str.Menu.Aux1NC"),
            (ContactorAuxConfig.Both1NO1NC, "Str.Menu.AuxBoth")
        ];
        foreach (var (cfg, key) in auxList)
        {
            auxSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), km.AuxConfig == cfg, () =>
            {
                km.SetAuxConfig(cfg);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", km.DisplayName, cfg.ToString()));
            }));
        }
        menu.Items.Add(auxSub);
    }

    private static void BuildIntermediateRelayItems(ContextMenu menu, IntermediateRelayComponent my, bool isDesign, Action<string> notify)
    {
        var (_, _, curSpec) = ElectricalAnalysisHelper.GetRatingSpec(my.CoilRating);
        var coilSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", curSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_12V, CoilVoltageRating.DC_24V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            coilSub.Items.Add(CreateCheckOption(lbl, my.CoilRating == target, () =>
            {
                my.CoilRating = target;
                my.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", my.DisplayName, lbl));
            }));
        }
        menu.Items.Add(coilSub);

        var poleSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.RelayPoles"));
        poleSub.IsEnabled = isDesign;
        poleSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Relay8Pin"), my.PoleConfig == IntermediateRelayPoles.DPDT_8Pin, () =>
        {
            my.SetPoleConfig(IntermediateRelayPoles.DPDT_8Pin);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", my.DisplayName, "MY2N 8-Pin"));
        }));
        poleSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Relay14Pin"), my.PoleConfig == IntermediateRelayPoles.FourPDT_14Pin, () =>
        {
            my.SetPoleConfig(IntermediateRelayPoles.FourPDT_14Pin);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", my.DisplayName, "MY4N 14-Pin"));
        }));
        menu.Items.Add(poleSub);
    }

    private static void BuildThermalRelayItems(ContextMenu menu, ThermalOverloadRelayComponent rn, Action<string> notify)
    {
        var (_, _, rLbl) = rn.GetRangeSpec();
        var rangeSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.ThermalRange", rLbl));
        (ThermalCurrentRange R, string Label)[] ranges =
        [
            (ThermalCurrentRange.Range_4_6A, "4 – 6 A"),
            (ThermalCurrentRange.Range_9_13A, "9 – 13 A"),
            (ThermalCurrentRange.Range_12_18A, "12 – 18 A")
        ];
        foreach (var (r, lbl) in ranges)
        {
            rangeSub.Items.Add(CreateCheckOption(lbl, rn.CurrentRange == r, () =>
            {
                rn.CurrentRange = r;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", rn.DisplayName, lbl));
            }));
        }
        menu.Items.Add(rangeSub);

        var resetSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ResetMode"));
        resetSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ManualReset"), !rn.AutoReset, () =>
        {
            rn.AutoReset = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", rn.DisplayName, "Manual"));
        }));
        resetSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.AutoReset"), rn.AutoReset, () =>
        {
            rn.AutoReset = true;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", rn.DisplayName, "Auto"));
        }));
        menu.Items.Add(resetSub);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.TestTrip"), () =>
        {
            rn.TestTrip();
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", rn.DisplayName, "TEST TRIP"));
        }, "#F97316"));

        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.DoReset"), () =>
        {
            rn.ManualResetAction();
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", rn.DisplayName, "RESET"));
        }, "#38BDF8"));
    }

    private static void BuildTimerItems(ContextMenu menu, TimerOnDelayComponent timer, bool isDesign, Window? ownerWindow, Action<string> notify)
    {
        var modeSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.TimerMode"));
        modeSub.IsEnabled = isDesign;
        (TimerOperatingMode Mode, string Key)[] modes =
        [
            (TimerOperatingMode.OnDelay, "Str.Menu.OnDelay"),
            (TimerOperatingMode.OffDelay, "Str.Menu.OffDelay"),
            (TimerOperatingMode.StarDelta, "Str.Menu.StarDelta")
        ];
        foreach (var (m, key) in modes)
        {
            modeSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), timer.OperatingMode == m, () =>
            {
                timer.SetOperatingMode(m);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, m.ToString()));
            }));
        }
        menu.Items.Add(modeSub);

        var (_, _, cSpec) = ElectricalAnalysisHelper.GetRatingSpec(timer.CoilRating);
        var coilSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", cSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_24V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            coilSub.Items.Add(CreateCheckOption(lbl, timer.CoilRating == target, () =>
            {
                timer.CoilRating = target;
                timer.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, lbl));
            }));
        }
        menu.Items.Add(coilSub);

        var pinSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ContactCfg"));
        pinSub.IsEnabled = isDesign;
        pinSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Timer1C"), timer.ContactConfig == TimerContactConfig.Timed1C, () =>
        {
            timer.SetContactConfig(TimerContactConfig.Timed1C);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, "1C"));
        }));
        pinSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Timer8Pin"), timer.ContactConfig == TimerContactConfig.InstPlusTimed8Pin, () =>
        {
            timer.SetContactConfig(TimerContactConfig.InstPlusTimed8Pin);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, "8-Pin"));
        }));
        menu.Items.Add(pinSub);

        var delaySub = CreateSubMenu(LocalizationManager.Format("Str.Menu.DelayTime", timer.DelaySeconds));
        foreach (double s in new[] { 0.5, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0 })
        {
            double target = s;
            delaySub.Items.Add(CreateCheckOption(LocalizationManager.Format("Str.Menu.SecUnit", target), Math.Abs(timer.DelaySeconds - target) < 0.05, () =>
            {
                timer.SetDelaySeconds(target);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, $"{target:0.#}s"));
            }));
        }
        delaySub.Items.Add(new Separator());
        delaySub.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.CustomDelay"), () =>
        {
            string? input = InputPromptDialog.Prompt(
                ownerWindow,
                LocalizationManager.Get("Str.Menu.CustomDelayTitle"),
                LocalizationManager.Get("Str.Menu.CustomDelayPrompt"),
                timer.DelaySeconds.ToString("0.#", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(input) &&
                double.TryParse(input.Replace(',', '.'), CultureInfo.InvariantCulture, out double val))
            {
                timer.SetDelaySeconds(val);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", timer.DisplayName, $"{timer.DelaySeconds:0.#}s"));
            }
        }));
        menu.Items.Add(delaySub);
    }

    private static void BuildSensorItems(ContextMenu menu, IndustrialSensorComponent sens, bool isDesign, Action<string> notify)
    {
        string modeShort = sens.WiringMode switch
        {
            SensorWiringMode.ThreeWire_NPN => "3W NPN",
            SensorWiringMode.FourWire_NO_NC => "4W NO+NC",
            SensorWiringMode.TwoWire_AC => "2W AC",
            SensorWiringMode.TwoWire_DC => "2W DC",
            _ => "3W PNP"
        };

        var wiringSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.SensorWiringType", modeShort));
        wiringSub.IsEnabled = isDesign;
        wiringSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens3Pnp"), sens.WiringMode == SensorWiringMode.ThreeWire_PNP, () =>
        {
            sens.SetWiringMode(SensorWiringMode.ThreeWire_PNP);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "3W PNP"));
        }));
        wiringSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens3Npn"), sens.WiringMode == SensorWiringMode.ThreeWire_NPN, () =>
        {
            sens.SetWiringMode(SensorWiringMode.ThreeWire_NPN);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "3W NPN"));
        }));
        wiringSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens4NoNc"), sens.WiringMode == SensorWiringMode.FourWire_NO_NC, () =>
        {
            sens.SetWiringMode(SensorWiringMode.FourWire_NO_NC);
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "4W NO+NC"));
        }));
        if (sens.SupportsTwoWire)
        {
            wiringSub.Items.Add(new Separator());
            wiringSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens2Ac"), sens.WiringMode == SensorWiringMode.TwoWire_AC, () =>
            {
                sens.SetWiringMode(SensorWiringMode.TwoWire_AC);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "2W AC (90-250V)"));
            }));
            wiringSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens2Dc"), sens.WiringMode == SensorWiringMode.TwoWire_DC, () =>
            {
                sens.SetWiringMode(SensorWiringMode.TwoWire_DC);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "2W DC (10-30V)"));
            }));
        }
        menu.Items.Add(wiringSub);

        if (sens.IsTwoWireMode)
        {
            var stateSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.TwoWireState", sens.TwoWireNormallyClosed ? "NC" : "NO"));
            stateSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ContactNO"), !sens.TwoWireNormallyClosed, () =>
            {
                sens.TwoWireNormallyClosed = false;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "2W NO"));
            }));
            stateSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ContactNC"), sens.TwoWireNormallyClosed, () =>
            {
                sens.TwoWireNormallyClosed = true;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "2W NC"));
            }));
            menu.Items.Add(stateSub);
        }
        else
        {
            var polSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.SensorOutputType", sens.Polarity));
            polSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensorPNP"), sens.Polarity == SensorPolarity.PNP, () =>
            {
                sens.Polarity = SensorPolarity.PNP;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "PNP"));
            }));
            polSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensorNPN"), sens.Polarity == SensorPolarity.NPN, () =>
            {
                sens.Polarity = SensorPolarity.NPN;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "NPN"));
            }));
            menu.Items.Add(polSub);

            var outSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.SensorWiring"));
            outSub.IsEnabled = isDesign;
            outSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensNO"), sens.OutputConfig == SensorOutputConfig.NO_3Wire, () =>
            {
                sens.SetOutputConfig(SensorOutputConfig.NO_3Wire);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "NO 3W"));
            }));
            outSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensNC"), sens.OutputConfig == SensorOutputConfig.NC_3Wire, () =>
            {
                sens.SetOutputConfig(SensorOutputConfig.NC_3Wire);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "NC 3W"));
            }));
            outSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.Sens4Wire"), sens.OutputConfig == SensorOutputConfig.FourWire_NO_NC, () =>
            {
                sens.SetOutputConfig(SensorOutputConfig.FourWire_NO_NC);
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "NO+NC 4W"));
            }));
            menu.Items.Add(outSub);
        }

        var trigSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.SensorTrigger"));
        trigSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensHold"), sens.TriggerMode == PushButtonActionMode.Momentary, () =>
        {
            sens.TriggerMode = PushButtonActionMode.Momentary;
            sens.IsTargetDetected = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "Momentary"));
        }));
        trigSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.SensToggle"), sens.TriggerMode == PushButtonActionMode.Latching, () =>
        {
            sens.TriggerMode = PushButtonActionMode.Latching;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", sens.DisplayName, "Toggle"));
        }));
        menu.Items.Add(trigSub);
    }

    private static void BuildReedSwitchItems(ContextMenu menu, CylinderReedSwitchComponent reed, Action<string> notify)
    {
        var stateSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.TwoWireState", reed.IsNormallyClosed ? "NC" : "NO"));
        stateSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ContactNO"), !reed.IsNormallyClosed, () =>
        {
            reed.IsNormallyClosed = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", reed.DisplayName, "NO"));
        }));
        stateSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ContactNC"), reed.IsNormallyClosed, () =>
        {
            reed.IsNormallyClosed = true;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", reed.DisplayName, "NC"));
        }));
        menu.Items.Add(stateSub);

        var trigSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.ReedTrigger"));
        trigSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ReedHold"), reed.TriggerMode == PushButtonActionMode.Momentary, () =>
        {
            reed.TriggerMode = PushButtonActionMode.Momentary;
            reed.IsMagnetDetected = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", reed.DisplayName, "Momentary"));
        }));
        trigSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ReedToggle"), reed.TriggerMode == PushButtonActionMode.Latching, () =>
        {
            reed.TriggerMode = PushButtonActionMode.Latching;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", reed.DisplayName, "Toggle"));
        }));
        menu.Items.Add(trigSub);
    }

    private static void BuildThermalSwitchItems(ContextMenu menu, ThermalSwitchComponent ts, Action<string> notify)
    {
        var cSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.ThermContact", ts.IsNormallyClosed ? "NC" : "NO"));
        cSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ThermNC"), ts.IsNormallyClosed, () =>
        {
            ts.IsNormallyClosed = true;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ts.DisplayName, "NC"));
        }));
        cSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.ThermNO"), !ts.IsNormallyClosed, () =>
        {
            ts.IsNormallyClosed = false;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", ts.DisplayName, "NO"));
        }));
        menu.Items.Add(cSub);
    }

    private static void BuildFloatSwitchItems(ContextMenu menu, FloatSwitchComponent fs, Action<string> notify)
    {
        string modeLbl = fs.OperatingMode == FloatSwitchMode.WaterSupply ? "SUPPLY" : "DRAIN";
        var mSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.FloatMode", modeLbl));
        mSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.FloatSupply"), fs.OperatingMode == FloatSwitchMode.WaterSupply, () =>
        {
            fs.OperatingMode = FloatSwitchMode.WaterSupply;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", fs.DisplayName, "WaterSupply"));
        }));
        mSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.FloatDrain"), fs.OperatingMode == FloatSwitchMode.Drainage, () =>
        {
            fs.OperatingMode = FloatSwitchMode.Drainage;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", fs.DisplayName, "Drainage"));
        }));
        menu.Items.Add(mSub);
    }

    private static void BuildPlcItems(
        ContextMenu menu,
        PlcControllerComponent plc,
        Action<string> notify,
        Action<PlcControllerComponent, bool>? onOpenPlcLadder)
    {
        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.PlcOpenLadder"), () =>
        {
            onOpenPlcLadder?.Invoke(plc, false);
        }, "#38BDF8"));
        menu.Items.Add(CreateActionItem(LocalizationManager.Get("Str.Menu.PlcTagTable"), () =>
        {
            onOpenPlcLadder?.Invoke(plc, true);
        }, "#2DD4BF"));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.PlcRun"), plc.CpuState == PlcCpuState.Run, () =>
        {
            plc.SetRunMode();
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", plc.DisplayName, "CPU RUN"));
        }));
        menu.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.PlcStop"), plc.CpuState == PlcCpuState.Stop, () =>
        {
            plc.SetStopMode();
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", plc.DisplayName, "CPU STOP"));
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateCheckOption(
            LocalizationManager.Format("Str.Menu.PlcSysMem", plc.DraftProgram.SystemMemoryByte),
            plc.DraftProgram.EnableSystemMemory, () =>
            {
                plc.DraftProgram.EnableSystemMemory = !plc.DraftProgram.EnableSystemMemory;
                plc.LoadedProgram.EnableSystemMemory = plc.DraftProgram.EnableSystemMemory;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", plc.DisplayName,
                    $"System Memory={(plc.DraftProgram.EnableSystemMemory ? "ON" : "OFF")}"));
            }));
        menu.Items.Add(CreateCheckOption(
            LocalizationManager.Format("Str.Menu.PlcClkMem", plc.DraftProgram.ClockMemoryByte),
            plc.DraftProgram.EnableClockMemory, () =>
            {
                plc.DraftProgram.EnableClockMemory = !plc.DraftProgram.EnableClockMemory;
                plc.LoadedProgram.EnableClockMemory = plc.DraftProgram.EnableClockMemory;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", plc.DisplayName,
                    $"Clock Memory={(plc.DraftProgram.EnableClockMemory ? "ON" : "OFF")}"));
            }));
    }

    private static void BuildVoltmeterItems(ContextMenu menu, VoltmeterComponent vm, Action<string> notify)
    {
        var kSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.MeterCurrent", vm.MeasureKind));
        kSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterAC"), vm.MeasureKind == CurrentKind.AC, () =>
        {
            vm.MeasureKind = CurrentKind.AC;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", vm.DisplayName, "AC"));
        }));
        kSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterDC"), vm.MeasureKind == CurrentKind.DC, () =>
        {
            vm.MeasureKind = CurrentKind.DC;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", vm.DisplayName, "DC"));
        }));
        menu.Items.Add(kSub);

        var rSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.MeterRange", vm.GetRangeSpec().Label));
        (VoltmeterRange R, string Label)[] ranges =
        [
            (VoltmeterRange.Range_50V, "0–50V"),
            (VoltmeterRange.Range_300V, "0–300V"),
            (VoltmeterRange.Range_500V, "0–500V")
        ];
        foreach (var (r, lbl) in ranges)
        {
            rSub.Items.Add(CreateCheckOption(lbl, vm.Range == r, () =>
            {
                vm.Range = r;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", vm.DisplayName, lbl));
            }));
        }
        menu.Items.Add(rSub);

        var dSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.MeterDisplay"));
        dSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterDigital"), vm.DisplayStyle == MeterDisplayStyle.Digital, () =>
        {
            vm.DisplayStyle = MeterDisplayStyle.Digital;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", vm.DisplayName, "Digital"));
        }));
        dSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterAnalog"), vm.DisplayStyle == MeterDisplayStyle.Analog, () =>
        {
            vm.DisplayStyle = MeterDisplayStyle.Analog;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", vm.DisplayName, "Analog"));
        }));
        menu.Items.Add(dSub);
    }

    private static void BuildAmmeterItems(ContextMenu menu, AmmeterComponent am, Action<string> notify)
    {
        var kSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.MeterCurrent", am.MeasureKind));
        kSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterAC"), am.MeasureKind == CurrentKind.AC, () =>
        {
            am.MeasureKind = CurrentKind.AC;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", am.DisplayName, "AC"));
        }));
        kSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterDC"), am.MeasureKind == CurrentKind.DC, () =>
        {
            am.MeasureKind = CurrentKind.DC;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", am.DisplayName, "DC"));
        }));
        menu.Items.Add(kSub);

        var rSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.MeterRange", am.GetRangeSpec().Label));
        (AmmeterRange R, string Label)[] ranges =
        [
            (AmmeterRange.Direct_10A, LocalizationManager.Format("Str.Menu.DirectAmps", "0–10A")),
            (AmmeterRange.Direct_30A, LocalizationManager.Format("Str.Menu.DirectAmps", "0–30A")),
            (AmmeterRange.ViaCT_50_5, LocalizationManager.Format("Str.Menu.ViaCt", "50/5A")),
            (AmmeterRange.ViaCT_100_5, LocalizationManager.Format("Str.Menu.ViaCt", "100/5A")),
            (AmmeterRange.ViaCT_150_5, LocalizationManager.Format("Str.Menu.ViaCt", "150/5A")),
            (AmmeterRange.ViaCT_200_5, LocalizationManager.Format("Str.Menu.ViaCt", "200/5A"))
        ];
        foreach (var (r, lbl) in ranges)
        {
            rSub.Items.Add(CreateCheckOption(lbl, am.Range == r, () =>
            {
                am.Range = r;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", am.DisplayName, lbl));
            }));
        }
        menu.Items.Add(rSub);

        var dSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.MeterDisplay"));
        dSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterDigital"), am.DisplayStyle == MeterDisplayStyle.Digital, () =>
        {
            am.DisplayStyle = MeterDisplayStyle.Digital;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", am.DisplayName, "Digital"));
        }));
        dSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MeterAnalog"), am.DisplayStyle == MeterDisplayStyle.Analog, () =>
        {
            am.DisplayStyle = MeterDisplayStyle.Analog;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", am.DisplayName, "Analog"));
        }));
        menu.Items.Add(dSub);
    }

    private static void BuildCtItems(ContextMenu menu, CurrentTransformerComponent ct, Action<string> notify)
    {
        var rSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CtRatio", ct.GetRatioSpec().Label));
        (CtRatio R, string Label)[] ratios =
        [
            (CtRatio.Ratio_50_5, "50 / 5A"),
            (CtRatio.Ratio_100_5, "100 / 5A"),
            (CtRatio.Ratio_150_5, "150 / 5A"),
            (CtRatio.Ratio_200_5, "200 / 5A")
        ];
        foreach (var (r, lbl) in ratios)
        {
            rSub.Items.Add(CreateCheckOption(lbl, ct.Ratio == r, () =>
            {
                ct.Ratio = r;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", ct.DisplayName, lbl));
            }));
        }
        menu.Items.Add(rSub);
    }

    private static void BuildPilotLightItems(ContextMenu menu, PilotLightComponent pilot, Action<string> notify)
    {
        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(pilot.VoltageRating);
        var vSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", vSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_12V, CoilVoltageRating.DC_24V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            vSub.Items.Add(CreateCheckOption(lbl, pilot.VoltageRating == target, () =>
            {
                pilot.VoltageRating = target;
                pilot.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", pilot.DisplayName, lbl));
            }));
        }
        menu.Items.Add(vSub);

        var colSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.Color"));
        foreach (var (col, key) in GetFourLampColors())
        {
            colSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), pilot.LightColor == col, () =>
            {
                pilot.LightColor = col;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", pilot.DisplayName, LocalizationManager.Get(key)));
            }));
        }
        menu.Items.Add(colSub);
    }

    private static void BuildResidentialLampItems(ContextMenu menu, ResidentialLampComponent lamp, Action<string> notify)
    {
        var (_, _, vSpec) = ElectricalAnalysisHelper.GetRatingSpec(lamp.VoltageRating);
        var vSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.CoilVoltage", vSpec));
        foreach (var r in new[] { CoilVoltageRating.DC_12V, CoilVoltageRating.DC_24V, CoilVoltageRating.AC_220V })
        {
            var target = r;
            var (_, _, lbl) = ElectricalAnalysisHelper.GetRatingSpec(target);
            vSub.Items.Add(CreateCheckOption(lbl, lamp.VoltageRating == target, () =>
            {
                lamp.VoltageRating = target;
                lamp.RepairBurnout();
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", lamp.DisplayName, lbl));
            }));
        }
        menu.Items.Add(vSub);

        var colSub = CreateSubMenu(LocalizationManager.Get("Str.Menu.Color"));
        foreach (var (col, key) in GetFourLampColors())
        {
            colSub.Items.Add(CreateCheckOption(LocalizationManager.Get(key), lamp.LampColor == col, () =>
            {
                lamp.LampColor = col;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", lamp.DisplayName, LocalizationManager.Get(key)));
            }));
        }
        menu.Items.Add(colSub);
    }

    private static void BuildMotor3PinItems(ContextMenu menu, Motor3Phase3PinComponent m3, Action<string> notify)
    {
        string connLbl = m3.InternalConnection == Motor3PinConnection.Delta_220V ? "Δ 220V" : "Y 380V";
        var cSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.Motor3PinConn", connLbl));
        cSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MotorStar380"), m3.InternalConnection == Motor3PinConnection.Star_380V, () =>
        {
            m3.InternalConnection = Motor3PinConnection.Star_380V;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", m3.DisplayName, "Y 380V"));
        }));
        cSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MotorDelta220"), m3.InternalConnection == Motor3PinConnection.Delta_220V, () =>
        {
            m3.InternalConnection = Motor3PinConnection.Delta_220V;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", m3.DisplayName, "Δ 220V"));
        }));
        menu.Items.Add(cSub);

        AddMotorPowerSubmenu(menu, m3, notify);
    }

    private static void BuildMotor6PinItems(ContextMenu menu, Motor3Phase6PinComponent m6, Action<string> notify)
    {
        string wLbl = m6.WindingType == Motor6PinWindingType.Dahlander2Speed ? "Dahlander 2-Speed" : "Star-Delta (Y-Δ)";
        var wSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.Motor6PinWinding", wLbl));
        wSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MotorStarDelta"), m6.WindingType == Motor6PinWindingType.StarDelta, () =>
        {
            m6.WindingType = Motor6PinWindingType.StarDelta;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", m6.DisplayName, "Star-Delta"));
        }));
        wSub.Items.Add(CreateCheckOption(LocalizationManager.Get("Str.Menu.MotorDahlander"), m6.WindingType == Motor6PinWindingType.Dahlander2Speed, () =>
        {
            m6.WindingType = Motor6PinWindingType.Dahlander2Speed;
            notify(LocalizationManager.Format("Str.Status.UpdatedProp", m6.DisplayName, "Dahlander 2-Speed"));
        }));
        menu.Items.Add(wSub);

        AddMotorPowerSubmenu(menu, m6, notify);
    }

    private static void AddMotorPowerSubmenu(ContextMenu menu, Motor3PhaseBaseComponent motor, Action<string> notify)
    {
        var pSub = CreateSubMenu(LocalizationManager.Format("Str.Menu.MotorPower", motor.RatedPowerKw, motor.NominalCurrentAmps));
        foreach (double kw in Motor3PhaseBaseComponent.SupportedPowersKw)
        {
            double target = kw;
            double estAmps = target switch { <= 1.6 => 3.5, <= 3.2 => 6.8, <= 5.8 => 11.6, _ => 15.5 };
            pSub.Items.Add(CreateCheckOption($"{target:0.#} kW  ({estAmps:0.#} A @ 380V)", Math.Abs(motor.RatedPowerKw - target) < 0.1, () =>
            {
                motor.RatedPowerKw = target;
                notify(LocalizationManager.Format("Str.Status.UpdatedProp", motor.DisplayName, $"{target:0.#}kW"));
            }));
        }
        menu.Items.Add(pSub);
    }

    private static (PilotLightColor Color, string Key)[] GetFourLampColors() =>
    [
        (PilotLightColor.Green, "Str.Color.Green"),
        (PilotLightColor.Red, "Str.Color.Red"),
        (PilotLightColor.Yellow, "Str.Color.Yellow"),
        (PilotLightColor.White, "Str.Color.White")
    ];

    private static MenuItem CreateSectionHeader(string title)
    {
        var item = new MenuItem { Header = title, IsEnabled = false, FontWeight = FontWeights.Bold };
        item.SetResourceReference(Control.ForegroundProperty, "AccentCyanBrush");
        return item;
    }

    private static MenuItem CreateSubMenu(string header) => new() { Header = header };

    private static MenuItem CreateCheckOption(string text, bool isChecked, Action onSelect)
    {
        var item = new MenuItem { Header = text, IsChecked = isChecked };
        item.Click += (_, _) => onSelect();
        return item;
    }

    private static MenuItem CreateActionItem(string text, Action onClick, string? accentHex = null)
    {
        var item = new MenuItem { Header = text };
        if (!string.IsNullOrWhiteSpace(accentHex))
        {
            item.Foreground = (Brush)new BrushConverter().ConvertFromString(accentHex)!;
        }
        item.Click += (_, _) => onClick();
        return item;
    }
}
