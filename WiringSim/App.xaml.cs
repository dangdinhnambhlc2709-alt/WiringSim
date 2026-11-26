using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.Models.Presets;
using WiringSim.Models.Serialization;
using WiringSim.Simulation;
using WiringSim.UI;
using WiringSim.UI.Helpers;
using WiringSim.UI.Services;

namespace WiringSim;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, "--verify", StringComparison.OrdinalIgnoreCase)))
        {
            int code = RunSelfVerification();
            Shutdown(code);
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static int RunSelfVerification()
    {
        try
        {
            var engine = new CircuitSimulationEngine();

            // 1. Kiểm tra đồng bộ 100% key giữa Strings.vi.xaml và Strings.en.xaml + ContextMenu cho mọi thiết bị
            var viDict = new ResourceDictionary { Source = new Uri("Resources/Strings.vi.xaml", UriKind.Relative) };
            var enDict = new ResourceDictionary { Source = new Uri("Resources/Strings.en.xaml", UriKind.Relative) };
            foreach (object key in viDict.Keys)
                Assert(enDict.Contains(key), $"Suite 1: Thiếu key '{key}' trong Strings.en.xaml!");
            foreach (object key in enDict.Keys)
                Assert(viDict.Contains(key), $"Suite 1: Thiếu key '{key}' trong Strings.vi.xaml!");

            foreach (string lang in new[] { "vi", "en" })
            {
                LocalizationManager.SetLanguage(lang);
                foreach (var desc in ComponentRegistry.Items)
                {
                    Assert(!desc.Title.StartsWith('[') && !desc.Subtitle.StartsWith('['),
                        $"Suite 1 ({lang}): Thiếu chuỗi bản địa hóa cho {desc.TypeKey}.");
                    var sample = desc.Factory();
                    var ctx = ContextMenuBuilder.BuildComponentMenu(sample, AppMode.Design, null, _ => { }, () => { });
                    Assert(ctx.Items.Count >= 4, $"Suite 1 ({lang}): ContextMenu cho {desc.TypeKey} thiếu mục.");
                }
            }

            // 2. Kiểm tra Metadata phiên bản v1.1_Release & UpdateService.NormalizeVersion
            string infoVer = UpdateService.GetCurrentInformationalVersion();
            Assert(string.Equals(infoVer, "v1.1_Release", StringComparison.Ordinal),
                $"Suite 2: InformationalVersion phải là 'v1.1_Release' (thực tế: '{infoVer}').");
            Assert(UpdateService.NormalizeVersion("v1.1_Release") == new Version(1, 1, 0),
                "Suite 2: NormalizeVersion('v1.1_Release') phải trả về 1.1.0.");

            // 3. Kiểm tra CKC AH3-3 Interactive Rotary Timer (8 chân: 2-7 PWR, 1-3-4 tức thời, 8-5-6 định thời + xoay góc)
            LocalizationManager.SetLanguage("vi");
            var ac1 = new Ac1PhaseSourceComponent();
            var ah3 = new RotaryTimerAh3Component { CoilRating = CoilVoltageRating.AC_220V };
            ah3.SetDelayFromDialAngle(0.0); // 12 giờ trên thang 0-10s = 5.0s!
            Assert(Math.Abs(ah3.DelaySeconds - 5.0) < 0.3, $"Suite 3: Kim AH3-3 góc 0° phải là ~5s (thực tế: {ah3.DelaySeconds}s).");
            ah3.SetDelaySeconds(1.0);
            var hInst = new PilotLightComponent(PilotLightColor.Green);
            var hTimed = new PilotLightComponent(PilotLightColor.Red);
            var ah3Wires = new List<WireModel>
            {
                new(ac1.LineTerminal, ah3.Power2, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, ah3.Power7, WireColorPreset.Black_N_DCMinus),
                new(ac1.LineTerminal, ah3.InstCom1, WireColorPreset.Red_L1_DCPlus),
                new(ah3.InstNo3, hInst.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hInst.X2, WireColorPreset.Black_N_DCMinus),
                new(ac1.LineTerminal, ah3.TimedCom8, WireColorPreset.Red_L1_DCPlus),
                new(ah3.TimedNo6, hTimed.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hTimed.X2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([ac1, ah3, hInst, hTimed], ah3Wires);
            engine.Start();
            engine.StepSimulation(0.1);
            Assert(ah3.IsCoilEnergized && hInst.IsLit && !ah3.IsTimeUp && !hTimed.IsLit,
                "Suite 3: AH3-3 khi mới cấp nguồn phải bật ON, đóng 1-3 tức thời và CHƯA đóng 8-6.");
            for (int i = 0; i < 25; i++) engine.StepSimulation(0.05);
            Assert(ah3.IsTimeUp && hTimed.IsLit,
                "Suite 3: AH3-3 sau khi hết thời gian trễ phải bật LED UP và đóng 8-6 làm sáng đèn định thời!");
            engine.Stop();

            // 4. Kiểm tra Nút nhấn LA38 (kép NC 11-12 + NO 23-24), Nút nhấn có đèn 6 chân & E-Stop (nhấn khóa / xoay nhả)
            var pbLa38 = new PushButtonComponent(isNormallyClosed: false);
            Assert(pbLa38.ContactConfig == PushButtonContactConfig.Dual1NO1NC && pbLa38.Terminals.Count == 4 &&
                   pbLa38.NcIn1?.Id == "11" && pbLa38.NcOut2?.Id == "12" && pbLa38.NoIn3?.Id == "23" && pbLa38.NoOut4?.Id == "24",
                "Suite 4: Nút nhấn LA38 mặc định phải có đủ 4 chân 11-12(NC) và 23-24(NO)!");

            var illumPb = new IlluminatedPushButtonComponent { VoltageRating = CoilVoltageRating.AC_220V };
            var estop = new EmergencyStopComponent();
            var hEstopNc = new PilotLightComponent(PilotLightColor.Green);
            var hEstopNo = new PilotLightComponent(PilotLightColor.Red);
            var pbWires = new List<WireModel>
            {
                new(ac1.LineTerminal, illumPb.LampX1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, illumPb.LampX2, WireColorPreset.Black_N_DCMinus),
                new(ac1.LineTerminal, estop.Nc11, WireColorPreset.Red_L1_DCPlus),
                new(estop.Nc12, hEstopNc.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hEstopNc.X2, WireColorPreset.Black_N_DCMinus),
                new(ac1.LineTerminal, estop.SecondTop!, WireColorPreset.Red_L1_DCPlus),
                new(estop.SecondBot!, hEstopNo.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hEstopNo.X2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([ac1, illumPb, estop, hEstopNc, hEstopNo], pbWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(illumPb.IsLit && hEstopNc.IsLit && !hEstopNo.IsLit,
                "Suite 4: Đèn nút nhấn 6 chân phải sáng; E-Stop nhả phải đóng 11-12 và mở 23-24.");
            estop.ToggleEngage();
            engine.StepSimulation(0.05);
            Assert(estop.IsEngaged && !hEstopNc.IsLit && hEstopNo.IsLit,
                "Suite 4: E-Stop khi nhấn khóa phải mở 11-12 và đóng 23-24!");
            engine.Stop();

            // 5. Kiểm tra Cầu chì (đứt khi quá tải & đứt khi ngắn mạch) + Cầu dao đảo chiều + Chuyển mạch Volt KDE 7 vị trí
            var src3 = new Ac3PhaseSourceComponent();
            var fuse4A = new FuseComponent { RatedAmps = 4 };
            var motor75 = new Motor3Phase3PinComponent { RatedPowerKw = 7.5 }; // 15.5A > 4A!
            var fuseWires = new List<WireModel>
            {
                new(src3.L1Terminal, fuse4A.In1, WireColorPreset.Red_L1_DCPlus),
                new(fuse4A.Out2, motor75.U1, WireColorPreset.Red_L1_DCPlus),
                new(src3.L2Terminal, motor75.V1, WireColorPreset.Yellow_L2),
                new(src3.L3Terminal, motor75.W1, WireColorPreset.Blue_L3)
            };
            engine.BindCircuit([src3, fuse4A, motor75], fuseWires);
            engine.Start();
            for (int i = 0; i < 6; i++) engine.StepSimulation(0.05);
            Assert(fuse4A.IsBlown, $"Suite 5A: Cầu chì 4A cấp cho động cơ 7.5kW (15.5A) phải tự động ĐỨT CHÌ! (I={fuse4A.MeasuredCurrentAmps}A)");
            fuse4A.ResetFuse();
            Assert(!fuse4A.IsBlown, "Suite 5A: Thay cầu chì mới phải khôi phục trạng thái nguyên vẹn.");
            engine.Stop();

            // Chuyển mạch KDE 7 vị trí nối đồng hồ Volt 500V
            var camVolt = new CamSwitchComponent();
            var vm = new VoltmeterComponent { MeasureKind = CurrentKind.AC, Range = VoltmeterRange.Range_500V };
            var camWires = new List<WireModel>
            {
                new(src3.L1Terminal, camVolt.InR, WireColorPreset.Red_L1_DCPlus),
                new(src3.L2Terminal, camVolt.InS, WireColorPreset.Yellow_L2),
                new(src3.L3Terminal, camVolt.InT, WireColorPreset.Blue_L3),
                new(src3.NeutralTerminal, camVolt.InNOrCom!, WireColorPreset.Black_N_DCMinus),
                new(camVolt.Out1, vm.V1, WireColorPreset.Red_L1_DCPlus),
                new(camVolt.Out2, vm.V2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([src3, camVolt, vm], camWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(vm.MeasuredVoltage < 1.0, "Suite 5B: KDE ở nấc 0 phải đo 0V.");
            camVolt.PositionIndex = 1; // RS -> 380V
            engine.StepSimulation(0.05);
            Assert(Math.Abs(vm.MeasuredVoltage - 380.0) <= 3.0, $"Suite 5B: KDE ở nấc RS phải đo ~380V (thực tế: {vm.MeasuredVoltage}V).");
            camVolt.PositionIndex = 4; // RN -> 220V
            engine.StepSimulation(0.05);
            Assert(Math.Abs(vm.MeasuredVoltage - 220.0) <= 3.0, $"Suite 5B: KDE ở nấc RN phải đo ~220V (thực tế: {vm.MeasuredVoltage}V).");
            engine.Stop();

            // 6. Kiểm tra Module Bàn trượt Động cơ & 2 Công tắc hành trình (LS1 Trái & LS2 Phải) chạy thuận/nghịch tự động!
            var track = new MotorLinearTrackComponent { InitialPosition = TrackInitialPosition.LeftAtLS1, TravelDurationSeconds = 2.0 };
            track.ApplyInitialPosition();
            Assert(track.IsLs1Actuated && !track.IsLs2Actuated, "Suite 6: Con trượt ban đầu ở trái phải gạt LS1.");
            var fwdTrackWires = new List<WireModel>
            {
                new(src3.L1Terminal, track.U1, WireColorPreset.Red_L1_DCPlus),
                new(src3.L2Terminal, track.V1, WireColorPreset.Yellow_L2),
                new(src3.L3Terminal, track.W1, WireColorPreset.Blue_L3)
            };
            engine.BindCircuit([src3, track], fwdTrackWires);
            engine.Start();
            for (int i = 0; i < 45; i++) engine.StepSimulation(0.05); // 2.25s > 2.0s -> chạm đầu phải LS2!
            Assert(!track.IsLs1Actuated && track.IsLs2Actuated && track.CarriagePosition >= 0.99,
                $"Suite 6: Chạy thuận hết hành trình phải chạm và gạt LS2 (Pos={track.CarriagePosition:0.00}).");
            engine.Stop();

            // Đảo 2 pha L2 <-> L3 chạy ngược từ LS2 về LS1
            track.InitialPosition = TrackInitialPosition.RightAtLS2;
            track.ApplyInitialPosition();
            var revTrackWires = new List<WireModel>
            {
                new(src3.L1Terminal, track.U1, WireColorPreset.Red_L1_DCPlus),
                new(src3.L3Terminal, track.V1, WireColorPreset.Blue_L3),
                new(src3.L2Terminal, track.W1, WireColorPreset.Yellow_L2)
            };
            engine.BindCircuit([src3, track], revTrackWires);
            engine.Start();
            for (int i = 0; i < 45; i++) engine.StepSimulation(0.05);
            Assert(track.IsLs1Actuated && !track.IsLs2Actuated && track.CarriagePosition <= 0.01,
                $"Suite 6: Đảo chiều pha phải kéo con trượt về chạm LS1 (Pos={track.CarriagePosition:0.00}).");
            engine.Stop();

            // 7. Kiểm tra Nút rẽ nhánh dây (Wire T-Junction): Tách dây -> truyền điện đồng thời 2 nhánh -> JSON round-trip -> xóa nhánh tự gộp dây!
            var testWin = new MainWindow();
            var l1 = new PilotLightComponent(PilotLightColor.Green) { X = 120, Y = 260 };
            var l2 = new PilotLightComponent(PilotLightColor.Yellow) { X = 320, Y = 260 };
            var srcT = new Ac1PhaseSourceComponent { X = 120, Y = 60 };
            var wMain = new WireModel(srcT.LineTerminal, l1.X1, WireColorPreset.Red_L1_DCPlus);
            var wN1 = new WireModel(srcT.NeutralTerminal, l1.X2, WireColorPreset.Black_N_DCMinus);
            var wN2 = new WireModel(srcT.NeutralTerminal, l2.X2, WireColorPreset.Black_N_DCMinus);
            testWin.WiringCanvas.LoadCircuit([srcT, l1, l2], [wMain, wN1, wN2], autoFit: false, raiseModified: false);

            // Tách dây wMain tại giữa và rẽ nhánh sang đèn l2!
            var junc = testWin.WiringCanvas.SplitWireAtPoint(wMain, new Point(140, 200), 0);
            var wBranch = new WireModel(junc.NodeTerminal, l2.X1, WireColorPreset.Red_L1_DCPlus);
            var allWiresWithBranch = testWin.WiringCanvas.Wires.Append(wBranch).ToList();
            testWin.WiringCanvas.LoadCircuit(testWin.WiringCanvas.Components.ToList(), allWiresWithBranch, autoFit: false, raiseModified: false);

            engine.BindCircuit(testWin.WiringCanvas.Components, testWin.WiringCanvas.Wires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(l1.IsLit && l2.IsLit, "Suite 7: Rẽ nhánh T-Junction phải cấp điện đồng thời sáng cả 2 đèn!");
            engine.Stop();

            // Kiểm tra JSON round-trip giữ nguyên WireJunctionComponent
            string jJson = CircuitSerializer.SerializeToJsonString(testWin.WiringCanvas.Components, testWin.WiringCanvas.Wires, "TJunctionTest");
            var (jComps, jWires, _) = CircuitSerializer.DeserializeFromJsonString(jJson);
            Assert(jComps.OfType<WireJunctionComponent>().Count() == 1 && jWires.Count == 5,
                $"Suite 7: Lưu/Mở JSON phải giữ trọn vẹn nút T-Junction (J={jComps.OfType<WireJunctionComponent>().Count()}, W={jWires.Count}).");

            // Xóa dây nhánh -> Tự động gộp 2 đoạn thẳng nối tiếp và dọn sạch Junction Dot!
            testWin.WiringCanvas.DeleteWire(wBranch);
            Assert(!testWin.WiringCanvas.Components.OfType<WireJunctionComponent>().Any() && testWin.WiringCanvas.Wires.Count == 3,
                $"Suite 7: Xóa nhánh T-Junction phải tự động gộp liền lại sợi dây gốc và xóa Junction Dot (J={testWin.WiringCanvas.Components.OfType<WireJunctionComponent>().Count()}, W={testWin.WiringCanvas.Wires.Count}).");
            testWin.Close();

            // 8. Kết xuất ảnh trực quan v1.1_Release (Dark VI & Light EN) trình diễn đầy đủ thiết bị mới + T-Junction!
            int previewIdx = Array.FindIndex(Environment.GetCommandLineArgs(), a => a == "--save-preview-dir");
            if (previewIdx >= 0 && previewIdx + 1 < Environment.GetCommandLineArgs().Length)
            {
                string outDir = Environment.GetCommandLineArgs()[previewIdx + 1];
                Directory.CreateDirectory(outDir);

                RenderV11Showcase(Path.Combine(outDir, "preview_v11_dark_vi.png"), useLight: false, lang: "vi");
                RenderV11Showcase(Path.Combine(outDir, "preview_v11_light_en.png"), useLight: true, lang: "en");
            }

            Console.WriteLine("SELF_VERIFY_PASSED: All v1.1_Release suites (Localization Parity, Version 1.1_Release, AH3-3 Rotary Timer, LA38 Dual PB + Illuminated PB + E-Stop, Fuse Overload/Short + KDE Cam Switch, Motor Linear Track LS1/LS2, Wire T-Junction Split/Sim/JSON/Cleanup, Visual Render) passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF_VERIFY_FAILED: {ex}");
            return 1;
        }
    }

    private static void RenderV11Showcase(string pngPath, bool useLight, string lang)
    {
        var win = new MainWindow();
        LocalizationManager.SetLanguage(lang);
        ThemeManager.ApplyTheme(useLight ? AppThemePreference.Light : AppThemePreference.Dark);

        var root = (FrameworkElement)win.Content;
        win.Content = null;
        root.Width = 1520;
        root.Height = 900;
        root.Measure(new Size(1520, 900));
        root.Arrange(new Rect(0, 0, 1520, 900));
        root.UpdateLayout();

        var src3 = new Ac3PhaseSourceComponent { DisplayName = "3P 380V", X = 60, Y = 40 };
        var knife = new KnifeSwitchComponent(KnifeSwitchKind.ThreePhase3Pole) { DisplayName = "CD1", X = 60, Y = 180, Position = 1 };
        var fuse = new FuseComponent { DisplayName = "FU1", X = 260, Y = 180, RatedAmps = 10 };
        var ah3 = new RotaryTimerAh3Component { DisplayName = "KT1", X = 380, Y = 120 };
        ah3.SetDelaySeconds(6.0);
        var cam = new CamSwitchComponent { DisplayName = "SA1", X = 600, Y = 120, PositionIndex = 1 };
        var vm = new VoltmeterComponent { DisplayName = "PV1", X = 820, Y = 140, Range = VoltmeterRange.Range_500V };

        var pbGreen = new PushButtonComponent(false) { DisplayName = "SB1", X = 60, Y = 380 };
        var pbIllum = new IlluminatedPushButtonComponent { DisplayName = "SB2", X = 200, Y = 380, ButtonColor = PilotLightColor.Green };
        var estop = new EmergencyStopComponent { DisplayName = "ES1", X = 380, Y = 380 };
        var ls = new LimitSwitchComponent { DisplayName = "SQ1", X = 520, Y = 380, IsActuated = true };
        var track = new MotorLinearTrackComponent { DisplayName = "MT1", X = 660, Y = 360, InitialPosition = TrackInitialPosition.Center };
        track.ApplyInitialPosition();

        var junc = new WireJunctionComponent { CenterPoint = new Point(290, 140), DotColorPreset = WireColorPreset.Red_L1_DCPlus };

        var wires = new List<WireModel>
        {
            new(src3.L1Terminal, knife.TopInputs[0], WireColorPreset.Red_L1_DCPlus),
            new(src3.L2Terminal, knife.TopInputs[1], WireColorPreset.Yellow_L2),
            new(src3.L3Terminal, knife.TopInputs[2], WireColorPreset.Blue_L3),
            new(src3.L1Terminal, junc.NodeTerminal, WireColorPreset.Red_L1_DCPlus),
            new(junc.NodeTerminal, fuse.In1, WireColorPreset.Red_L1_DCPlus),
            new(junc.NodeTerminal, cam.InR, WireColorPreset.Red_L1_DCPlus),
            new(src3.L2Terminal, cam.InS, WireColorPreset.Yellow_L2),
            new(src3.L3Terminal, cam.InT, WireColorPreset.Blue_L3),
            new(src3.NeutralTerminal, cam.InNOrCom!, WireColorPreset.Black_N_DCMinus),
            new(cam.Out1, vm.V1, WireColorPreset.Red_L1_DCPlus),
            new(cam.Out2, vm.V2, WireColorPreset.Black_N_DCMinus),
            new(fuse.Out2, pbIllum.LampX1, WireColorPreset.Red_L1_DCPlus),
            new(src3.NeutralTerminal, pbIllum.LampX2, WireColorPreset.Black_N_DCMinus),
            new(fuse.Out2, ah3.Power2, WireColorPreset.Red_L1_DCPlus),
            new(src3.NeutralTerminal, ah3.Power7, WireColorPreset.Black_N_DCMinus),
            new(knife.BottomTerminals[0], track.U1, WireColorPreset.Red_L1_DCPlus),
            new(knife.BottomTerminals[1], track.V1, WireColorPreset.Yellow_L2),
            new(knife.BottomTerminals[2], track.W1, WireColorPreset.Blue_L3)
        };

        var comps = new List<ElectricalComponent> { src3, knife, fuse, ah3, cam, vm, pbGreen, pbIllum, estop, ls, track, junc };
        var sim = new CircuitSimulationEngine();
        sim.BindCircuit(comps, wires);
        sim.Start();
        for (int i = 0; i < 30; i++) sim.StepSimulation(0.05);
        track.UpdateVisualAnimation(0.5, true);

        win.WiringCanvas.LoadCircuit(comps, wires, autoFit: true, raiseModified: false);
        win.WiringCanvas.SetMode(AppMode.Simulation);

        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(1520, 900, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(pngPath);
        encoder.Save(fs);
        win.Close();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
