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

            // 2. Kiểm tra Metadata phiên bản v1.2_Release, UpdateService.NormalizeVersion, SkipVersion & Asset Selection
            string infoVer = UpdateService.GetCurrentInformationalVersion();
            Assert(string.Equals(infoVer, "v1.2_Release", StringComparison.Ordinal),
                $"Suite 2: InformationalVersion phải là 'v1.2_Release' (thực tế: '{infoVer}').");
            Assert(UpdateService.NormalizeVersion("v1.2_Release") == new Version(1, 2, 0),
                "Suite 2: NormalizeVersion('v1.2_Release') phải trả về 1.2.0.");
            Assert(UpdateService.IsVersionSkipped("v1.3.0_Release", "v1.3.0"),
                "Suite 2: IsVersionSkipped phải nhận diện trùng phiên bản đã bỏ qua.");
            var mockRelease = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                infoVer,
                LatestTagName: "v1.3.0_Release",
                SetupAssetUrl: "https://example.com/WiringSim_Setup_v1.3.0.exe",
                SetupAssetFileName: "WiringSim_Setup_v1.3.0.exe",
                PortableZipUrl: "https://example.com/WiringSim_v1.3.0_win-x64.zip",
                PortableZipFileName: "WiringSim_v1.3.0_win-x64.zip");
            var preferred = UpdateService.SelectPreferredAsset(mockRelease);
            Assert(!string.IsNullOrWhiteSpace(preferred.Url) && !string.IsNullOrWhiteSpace(preferred.FileName),
                "Suite 2: SelectPreferredAsset phải chọn được gói cập nhật phù hợp (Setup/Portable).");

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

            // 8. Kiểm tra toàn diện v1.2: Cảm biến 2 dây AC/DC (sụt áp 3.5V AC / 2.5V DC, phân cực DC), Xi-lanh D-A93, KSD301, Phao nước, Preset 4 & Lỗi đấu 2 dây không qua tải!
            // 8A. Preset 4: Cảm biến tiệm cận 2 dây AC 220V -> Rơ-le K1 + Voltmeter (216.5V) + Đèn H1
            var p4Def = PresetCircuitFactory.Presets.Single(p => p.Key == "PRESET_2WIRE_AC_RELAY");
            var (p4Comps, p4Wires) = p4Def.Builder();
            var p4Sensor = p4Comps.OfType<IndustrialSensorComponent>().Single();
            var p4Relay = p4Comps.OfType<IntermediateRelayComponent>().Single();
            var p4Vm = p4Comps.OfType<VoltmeterComponent>().Single();
            var p4Lamp = p4Comps.OfType<PilotLightComponent>().Single();
            Assert(p4Sensor.WiringMode == SensorWiringMode.TwoWire_AC && p4Sensor.Terminals.Count == 2 && p4Sensor.BkOut1 is null,
                "Suite 8A: Cảm biến 2 dây AC trong Preset 4 phải chỉ có đúng 2 chân BN và BU.");
            engine.BindCircuit(p4Comps, p4Wires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(!p4Relay.IsCoilEnergized && p4Vm.MeasuredVoltage < 1.0 && !p4Lamp.IsLit,
                "Suite 8A: Khi chưa có vật trước cảm biến 2 dây AC, Rơ-le K1 và Đèn H1 phải tắt.");
            p4Sensor.IsTargetDetected = true;
            engine.StepSimulation(0.05);
            engine.StepSimulation(0.05); // Tick kế để tiếp điểm K1 đóng cấp đèn H1
            Assert(p4Relay.IsCoilEnergized && p4Lamp.IsLit && Math.Abs(p4Vm.MeasuredVoltage - 216.5) <= 0.2,
                $"Suite 8A: Cảm biến 2 dây AC tác động phải hút K1, sáng H1 và sụt áp 3.5V -> Voltmeter đo ~216.5V (thực tế: {p4Vm.MeasuredVoltage:0.0}V).");
            engine.Stop();

            // 8B. Cảm biến 2 dây DC 24V: Đúng cực (+ vào BN, tải ra BU -> 21.5V) vs Ngược cực (không dẫn, IsReversePolarity=true)
            var dc24 = new DcSourceComponent();
            dc24.SetVoltage(24.0);
            var sens2Dc = new IndustrialSensorComponent(SensorKind.Capacitive) { IsTargetDetected = true };
            sens2Dc.SetWiringMode(SensorWiringMode.TwoWire_DC);
            var lamp24 = new PilotLightComponent(PilotLightColor.Green) { VoltageRating = CoilVoltageRating.DC_24V };
            var vmDc = new VoltmeterComponent { MeasureKind = CurrentKind.DC, Range = VoltmeterRange.Range_50V };
            var dcFwdWires = new List<WireModel>
            {
                new(dc24.PlusTerminal, sens2Dc.BnPlus, WireColorPreset.Red_L1_DCPlus),
                new(sens2Dc.BuMinus, lamp24.X1, WireColorPreset.Yellow_L2),
                new(lamp24.X2, dc24.MinusTerminal, WireColorPreset.Black_N_DCMinus),
                new(sens2Dc.BuMinus, vmDc.V1, WireColorPreset.Red_L1_DCPlus),
                new(dc24.MinusTerminal, vmDc.V2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([dc24, sens2Dc, lamp24, vmDc], dcFwdWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(lamp24.IsLit && !sens2Dc.IsReversePolarity && Math.Abs(vmDc.MeasuredVoltage - 21.5) <= 0.2,
                $"Suite 8B (Đúng cực DC): Cảm biến 2 dây DC 24V phải sáng tải và sụt áp 2.5V -> đo ~21.5V (thực tế: {vmDc.MeasuredVoltage:0.0}V).");
            engine.Stop();

            // Đấu ngược cực DC (+ vào BU, BN ra tải) -> Không dẫn & báo ngược cực!
            var dcRevWires = new List<WireModel>
            {
                new(dc24.PlusTerminal, sens2Dc.BuMinus, WireColorPreset.Red_L1_DCPlus),
                new(sens2Dc.BnPlus, lamp24.X1, WireColorPreset.Yellow_L2),
                new(lamp24.X2, dc24.MinusTerminal, WireColorPreset.Black_N_DCMinus),
                new(sens2Dc.BnPlus, vmDc.V1, WireColorPreset.Red_L1_DCPlus),
                new(dc24.MinusTerminal, vmDc.V2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([dc24, sens2Dc, lamp24, vmDc], dcRevWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(!lamp24.IsLit && sens2Dc.IsReversePolarity && vmDc.MeasuredVoltage < 0.5,
                $"Suite 8B (Ngược cực DC): Đấu ngược cực cảm biến 2 dây DC phải chặn dẫn điện (Rev={sens2Dc.IsReversePolarity}, V={vmDc.MeasuredVoltage:0.0}V).");
            engine.Stop();

            // 8C. Kiểm tra 3 thiết bị 2 dây mới: Cảm biến xi-lanh SMC D-A93, Công tắc nhiệt KSD301 & Phao mức nước 2 dây
            var reed = new CylinderReedSwitchComponent();
            var ksd = new ThermalSwitchComponent { IsNormallyClosed = true };
            var flt = new FloatSwitchComponent { OperatingMode = FloatSwitchMode.WaterSupply, IsWaterFull = false };
            var hReed = new PilotLightComponent(PilotLightColor.Green);
            var hKsd = new PilotLightComponent(PilotLightColor.Yellow);
            var hFlt = new PilotLightComponent(PilotLightColor.Red);
            var new2Wires = new List<WireModel>
            {
                new(ac1.LineTerminal, reed.PinBn, WireColorPreset.Red_L1_DCPlus),
                new(reed.PinBu, hReed.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hReed.X2, WireColorPreset.Black_N_DCMinus),

                new(ac1.LineTerminal, ksd.Pin1, WireColorPreset.Red_L1_DCPlus),
                new(ksd.Pin2, hKsd.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hKsd.X2, WireColorPreset.Black_N_DCMinus),

                new(ac1.LineTerminal, flt.PinCom, WireColorPreset.Red_L1_DCPlus),
                new(flt.PinOut, hFlt.X1, WireColorPreset.Red_L1_DCPlus),
                new(ac1.NeutralTerminal, hFlt.X2, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([ac1, reed, ksd, flt, hReed, hKsd, hFlt], new2Wires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(!hReed.IsLit && hKsd.IsLit && hFlt.IsLit,
                "Suite 8C: Ban đầu D-A93(NO) tắt, KSD301(NC bình thường) sáng, Phao(Cấp nước - Cạn) sáng.");
            reed.IsMagnetDetected = true;
            ksd.ToggleOverheat();
            flt.ToggleWaterLevel(); // Đầy nước -> ngắt bơm cấp
            engine.StepSimulation(0.05);
            Assert(hReed.IsLit && !hKsd.IsLit && !hFlt.IsLit,
                "Suite 8C: Sau khi kích từ D-A93, quá nhiệt KSD301 và đầy nước Phao phải đảo trạng thái tương ứng!");
            engine.Stop();

            // 8D. Phát hiện lỗi đấu cảm biến 2 dây trực tiếp vào nguồn KHÔNG QUA TẢI -> Cảnh báo đỏ, đánh dấu DAMAGED! và nhảy MCB!
            var mcbProt = new Mcb1PhaseComponent { IsOn = true };
            var badSens = new IndustrialSensorComponent(SensorKind.Inductive) { IsTargetDetected = true };
            badSens.SetWiringMode(SensorWiringMode.TwoWire_AC);
            var shortWires = new List<WireModel>
            {
                new(ac1.LineTerminal, mcbProt.In1, WireColorPreset.Red_L1_DCPlus),
                new(mcbProt.Out2, badSens.BnPlus, WireColorPreset.Red_L1_DCPlus),
                new(badSens.BuMinus, ac1.NeutralTerminal, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([ac1, mcbProt, badSens], shortWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(engine.ActiveFault is not null &&
                   engine.ActiveFault.Title == LocalizationManager.Get("Str.Fault.TwoWireNoLoadShort") &&
                   badSens.IsDamaged && mcbProt.IsTripped,
                $"Suite 8D: Đấu cảm biến 2 dây trực tiếp L-N không qua tải phải phát cảnh báo lỗi 2 dây, hỏng cảm biến (DAMAGED={badSens.IsDamaged}) và nhảy MCB (Trip={mcbProt.IsTripped})!");
            badSens.RepairDevice();
            Assert(!badSens.IsDamaged, "Suite 8D: Thay cảm biến mới phải xóa trạng thái DAMAGED.");
            engine.Stop();

            // 9. Kết xuất ảnh trực quan v1.2_Release (Dark VI & Light EN) trình diễn Preset 4 + 3 Cảm biến/Công tắc 2 dây mới!
            int previewIdx = Array.FindIndex(Environment.GetCommandLineArgs(), a => a == "--save-preview-dir");
            if (previewIdx >= 0 && previewIdx + 1 < Environment.GetCommandLineArgs().Length)
            {
                string outDir = Environment.GetCommandLineArgs()[previewIdx + 1];
                Directory.CreateDirectory(outDir);

                RenderV12Showcase(Path.Combine(outDir, "preview_v12_dark_vi.png"), useLight: false, lang: "vi");
                RenderV12Showcase(Path.Combine(outDir, "preview_v12_light_en.png"), useLight: true, lang: "en");
            }

            Console.WriteLine("SELF_VERIFY_PASSED: All v1.2_Release suites (Localization Parity, Version v1.2_Release, AH3-3, LA38+E-Stop, Fuse+KDE, Linear Track, Wire T-Junction, 2-Wire AC/DC Sensors + Voltage Drop + Polarity + D-A93 + KSD301 + Float Switch + No-Load Fault Detection) passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF_VERIFY_FAILED: {ex}");
            return 1;
        }
    }

    private static void RenderV12Showcase(string pngPath, bool useLight, string lang)
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

        // Preset 4 đang chạy thực tế (Cảm biến 2 dây AC 220V -> K1 + Voltmeter 216.5V + H1) + 3 thiết bị 2 dây mới!
        var (p4Comps, p4Wires) = PresetCircuitFactory.Presets.Single(p => p.Key == "PRESET_2WIRE_AC_RELAY").Builder();
        var comps = new List<ElectricalComponent>(p4Comps);
        var wires = new List<WireModel>(p4Wires);
        var p4Ac = comps.OfType<Ac1PhaseSourceComponent>().Single();
        var p4Sens = comps.OfType<IndustrialSensorComponent>().Single();
        p4Sens.IsTargetDetected = true;

        var reed = new CylinderReedSwitchComponent { DisplayName = "RS1", X = 120, Y = 500, IsMagnetDetected = true };
        var ksd = new ThermalSwitchComponent { DisplayName = "TS1", X = 320, Y = 490, IsNormallyClosed = true, IsOverheated = true };
        var flt = new FloatSwitchComponent { DisplayName = "FS1", X = 500, Y = 480, OperatingMode = FloatSwitchMode.WaterSupply, IsWaterFull = false };
        var sens2Dc = new IndustrialSensorComponent(SensorKind.Photoelectric) { DisplayName = "B2", X = 680, Y = 480, IsTargetDetected = true };
        sens2Dc.SetWiringMode(SensorWiringMode.TwoWire_DC);

        var hReed = new PilotLightComponent(PilotLightColor.Green) { DisplayName = "H2", X = 135, Y = 640 };
        var hFlt = new PilotLightComponent(PilotLightColor.Yellow) { DisplayName = "H3", X = 515, Y = 640 };

        comps.AddRange([reed, ksd, flt, sens2Dc, hReed, hFlt]);
        wires.AddRange([
            new(p4Ac.LineTerminal, reed.PinBn, WireColorPreset.Red_L1_DCPlus),
            new(reed.PinBu, hReed.X1, WireColorPreset.Yellow_L2),
            new(p4Ac.NeutralTerminal, hReed.X2, WireColorPreset.Black_N_DCMinus),
            new(p4Ac.LineTerminal, flt.PinCom, WireColorPreset.Red_L1_DCPlus),
            new(flt.PinOut, hFlt.X1, WireColorPreset.Yellow_L2),
            new(p4Ac.NeutralTerminal, hFlt.X2, WireColorPreset.Black_N_DCMinus)
        ]);

        var sim = new CircuitSimulationEngine();
        sim.BindCircuit(comps, wires);
        sim.Start();
        for (int i = 0; i < 6; i++) sim.StepSimulation(0.05);

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
