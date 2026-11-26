using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Components.Sources;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
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

            // 2. Kiểm tra Metadata phiên bản v1.3.3_Release, UpdateService.NormalizeVersion, SkipVersion & Asset Selection
            string infoVer = UpdateService.GetCurrentInformationalVersion();
            Assert(string.Equals(infoVer, "v1.3.3_Release", StringComparison.Ordinal),
                $"Suite 2: InformationalVersion phải là 'v1.3.3_Release' (thực tế: '{infoVer}').");
            Assert(UpdateService.NormalizeVersion("v1.3.3_Release") == new Version(1, 3, 3),
                "Suite 2: NormalizeVersion('v1.3.3_Release') phải trả về 1.3.3.");
            Assert(UpdateService.IsVersionSkipped("v1.4.0_Release", "v1.4.0"),
                "Suite 2: IsVersionSkipped phải nhận diện trùng phiên bản đã bỏ qua.");
            var mockRelease = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                infoVer,
                LatestTagName: "v1.4.0_Release",
                SetupAssetUrl: "https://example.com/WiringSim_Setup_v1.4.0.exe",
                SetupAssetFileName: "WiringSim_Setup_v1.4.0.exe",
                PortableZipUrl: "https://example.com/WiringSim_v1.4.0_win-x64.zip",
                PortableZipFileName: "WiringSim_v1.4.0_win-x64.zip");
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

            // 9. Kiểm tra toàn diện v1.3: PLC (14 DI / 10 DQ / 2 AI), Trình biên dịch Ladder, PNP/NPN 1M, Preset 5 (TON + %MW), Counter, Math & JSON Round-Trip!
            // 9A. Kiểm tra phần cứng 32 chân chuẩn PLC (14 DI / 10 DQ / 2 AI)
            var plcCheck = new PlcControllerComponent();
            Assert(plcCheck.Terminals.Count == 32 && plcCheck.DiTerminals.Length == 14 && plcCheck.DqTerminals.Length == 10,
                $"Suite 9A: PLC phải có đủ 32 chân (14 DI, 10 DQ, 2 AI, nguồn L+/M/PE/1M, phụ L+/M). Thực tế: {plcCheck.Terminals.Count}");

            // 9B. Kiểm tra Compiler (bắt lỗi nhánh hở, sai địa chỉ, trùng %DB, sai PT; và biên dịch sạch 0 lỗi cho Preset 5)
            var badProg = new PlcProgramModel();
            var badNw1 = new LadderNetwork { Title = "Open Branch" };
            badNw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%I2.0" }); // Ngoài dải + không có cuộn hút!
            badProg.Networks.Add(badNw1);
            var badNw2 = new LadderNetwork { Title = "Duplicate DB & Bad PT" };
            badNw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.TimerTON, Address = "%DB1", Param1 = "T#2s" });
            badProg.Networks.Add(badNw2);
            var badNw3 = new LadderNetwork { Title = "Dup DB" };
            badNw3.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.TimerTON, Address = "%DB1", Param1 = "BAD_TIME" });
            badProg.Networks.Add(badNw3);
            var badRes = PlcProgramCompiler.Compile(badProg);
            Assert(!badRes.Success && badRes.ErrorCount >= 4,
                $"Suite 9B: Compiler phải bắt đầy đủ lỗi nhánh hở, địa chỉ ngoài dải, trùng %DB1 và sai định dạng PT (Errors={badRes.ErrorCount}).");

            var p5Prog = PresetCircuitFactory.BuildSequentialLightsProgram();
            var p5Res = PlcProgramCompiler.Compile(p5Prog);
            Assert(p5Res.Success && p5Res.ErrorCount == 0 && p5Res.WarningCount == 0,
                $"Suite 9B: Chương trình Preset 5 phải biên dịch sạch 0 lỗi, 0 cảnh báo (E={p5Res.ErrorCount}, W={p5Res.WarningCount}).");

            // 9C. Kiểm tra NPN (1M nối +24V, I0.0 nối 0V -> DI[0] = true!)
            var npnPsu = new DcSourceComponent();
            npnPsu.SetVoltage(24.0);
            var npnPlc = new PlcControllerComponent();
            var npnWires = new List<WireModel>
            {
                new(npnPsu.PlusTerminal, npnPlc.SupplyLPlus, WireColorPreset.Red_L1_DCPlus),
                new(npnPsu.MinusTerminal, npnPlc.SupplyM, WireColorPreset.Black_N_DCMinus),
                new(npnPsu.PlusTerminal, npnPlc.InputCommon1M, WireColorPreset.Red_L1_DCPlus), // 1M = +24V (NPN mode)
                new(npnPsu.MinusTerminal, npnPlc.DiTerminals[0], WireColorPreset.Black_N_DCMinus) // I0.0 = 0V -> ACTIVE!
            };
            engine.BindCircuit([npnPsu, npnPlc], npnWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(npnPlc.IsPowered && npnPlc.Runtime.Di[0], "Suite 9C: Chế độ 1M=+24V (NPN) phải nhận tín hiệu 0V tại I0.0!");
            engine.Stop();

            // 9D. Kiểm tra Preset 5 chạy thực tế: Nhấn START -> Đèn H1 sáng (MW2=1) -> sau 3s sang H2 (MW2=2) -> sau 3s sang H3 (MW2=3) -> quay lại H1 -> Nhấn STOP tắt toàn bộ!
            var p5Def = PresetCircuitFactory.Presets.Single(p => p.Key == "PRESET_PLC_SEQ");
            var (p5Comps, p5Wires) = p5Def.Builder();
            var p5Plc = p5Comps.OfType<PlcControllerComponent>().Single();
            var p5Start = p5Comps.OfType<PushButtonComponent>().Single(b => b.DisplayName == "START");
            var p5Stop = p5Comps.OfType<PushButtonComponent>().Single(b => b.DisplayName == "STOP");
            var p5H1 = p5Comps.OfType<PilotLightComponent>().Single(h => h.DisplayName.StartsWith("H1"));
            var p5H2 = p5Comps.OfType<PilotLightComponent>().Single(h => h.DisplayName.StartsWith("H2"));
            var p5H3 = p5Comps.OfType<PilotLightComponent>().Single(h => h.DisplayName.StartsWith("H3"));

            engine.BindCircuit(p5Comps, p5Wires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(p5Plc.IsPowered && !p5H1.IsLit && !p5H2.IsLit && !p5H3.IsLit,
                "Suite 9D: Trước khi nhấn START, cả 3 đèn tuần tự phải tắt.");

            // Nhấn rồi nhả nút START -> tự giữ %M10.0 và bật bước 1 (H1)
            p5Start.IsPressed = true;
            engine.StepSimulation(0.05);
            p5Start.IsPressed = false;
            engine.StepSimulation(0.05);
            engine.StepSimulation(0.05);
            Assert(p5Plc.Runtime.ReadBit("%M10.0") && p5Plc.Runtime.ReadWordOrConstant("%MW20") == 1 && p5H1.IsLit && !p5H2.IsLit && !p5H3.IsLit,
                $"Suite 9D: Sau nhấn START phải tự giữ và sáng H1 (MW20={p5Plc.Runtime.ReadWordOrConstant("%MW20")}, H1={p5H1.IsLit}).");

            // Chờ 3.15s (63 tick x 50ms) -> chuyển sang đèn H2 (%MW20 = 2)
            for (int t = 0; t < 63; t++) engine.StepSimulation(0.05);
            Assert(p5Plc.Runtime.ReadWordOrConstant("%MW20") == 2 && !p5H1.IsLit && p5H2.IsLit && !p5H3.IsLit,
                $"Suite 9D: Sau 3 giây phải chuyển sang đèn H2 (MW20={p5Plc.Runtime.ReadWordOrConstant("%MW20")}, H2={p5H2.IsLit}).");

            // Chờ thêm 3.15s -> chuyển sang đèn H3 (%MW20 = 3)
            for (int t = 0; t < 63; t++) engine.StepSimulation(0.05);
            Assert(p5Plc.Runtime.ReadWordOrConstant("%MW20") == 3 && !p5H1.IsLit && !p5H2.IsLit && p5H3.IsLit,
                $"Suite 9D: Sau 6 giây phải chuyển sang đèn H3 (MW20={p5Plc.Runtime.ReadWordOrConstant("%MW20")}, H3={p5H3.IsLit}).");

            // Chờ thêm 3.15s -> quay vòng về H1 (%MW20 = 1)
            for (int t = 0; t < 63; t++) engine.StepSimulation(0.05);
            Assert(p5Plc.Runtime.ReadWordOrConstant("%MW20") == 1 && p5H1.IsLit && !p5H2.IsLit && !p5H3.IsLit,
                $"Suite 9D: Sau 9 giây phải quay vòng về đèn H1 (MW20={p5Plc.Runtime.ReadWordOrConstant("%MW20")}, H1={p5H1.IsLit}).");

            // Nhấn STOP -> dừng và tắt toàn bộ đèn
            p5Stop.IsPressed = true;
            engine.StepSimulation(0.05);
            engine.StepSimulation(0.05);
            p5Stop.IsPressed = false;
            engine.StepSimulation(0.05);
            Assert(!p5Plc.Runtime.ReadBit("%M10.0") && p5Plc.Runtime.ReadWordOrConstant("%MW20") == 0 && !p5H1.IsLit && !p5H2.IsLit && !p5H3.IsLit,
                "Suite 9D: Nhấn STOP phải xóa %M10.0, đặt %MW20=0 và tắt toàn bộ 3 đèn!");
            engine.Stop();

            // 9E. Kiểm tra JSON round-trip lưu & mở lại nguyên vẹn chương trình Ladder + Tag Table của PLC
            string plcJson = CircuitSerializer.SerializeToJsonString(p5Comps, p5Wires, "PlcPreset5Test");
            var (rtComps, rtWires, _) = CircuitSerializer.DeserializeFromJsonString(plcJson);
            var rtPlc = rtComps.OfType<PlcControllerComponent>().Single();
            Assert(rtPlc.DraftProgram.Networks.Count == p5Plc.DraftProgram.Networks.Count &&
                   rtPlc.DraftProgram.Tags.Count == p5Plc.DraftProgram.Tags.Count &&
                   rtWires.Count == p5Wires.Count,
                $"Suite 9E: Lưu/Mở JSON phải giữ nguyên vẹn {p5Plc.DraftProgram.Networks.Count} Network, {p5Plc.DraftProgram.Tags.Count} Tag và {p5Wires.Count} dây đấu của PLC!");

            // 9F. Kiểm tra đầy đủ 13 nhóm hàm/lệnh chuẩn IEC (10 nhóm cơ bản + Extended + Technology + Communication)
            Assert(PlcInstructionCatalog.Groups.Count == 13 && PlcInstructionCatalog.All.Count >= 115,
                $"Suite 9F: Danh mục lệnh PLC phải có đủ 13 nhóm và >= 115 lệnh (thực tế: {PlcInstructionCatalog.Groups.Count} nhóm, {PlcInstructionCatalog.All.Count} lệnh).");
            foreach (string lang in new[] { "vi", "en" })
            {
                LocalizationManager.SetLanguage(lang);
                foreach (var meta in PlcInstructionCatalog.All)
                {
                    string d = PlcInstructionCatalog.GetDescription(meta);
                    Assert(!string.IsNullOrWhiteSpace(d) && !d.StartsWith('['),
                        $"Suite 9F ({lang}): Thiếu mô tả cho lệnh {meta.Symbol} ({meta.DescKey}).");
                }
            }
            LocalizationManager.SetLanguage("vi");
            Assert(PlcInstructionCatalog.TryResolveByName("NORM_X", out var resKind) && resKind == LadderElementKind.NormX &&
                   PlcInstructionCatalog.TryResolveByName("ROL", out var rolKind) && rolKind == LadderElementKind.RotateLeft,
                "Suite 9F: Empty box [F8] phải phân giải đúng tên lệnh NORM_X và ROL!");

            // Kiểm tra Compiler bắt lỗi JMP tới LABEL không tồn tại & vòng lặp nhảy vô hạn
            var jmpErrProg = new PlcProgramModel();
            var jNw1 = new LadderNetwork { Title = "Missing label" };
            jNw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Jump, Address = "NO_SUCH_LBL" });
            var jNw2 = new LadderNetwork { Title = "Infinite loop" };
            jNw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Label, Address = "LOOP1" });
            jNw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Jump, Address = "LOOP1" });
            jmpErrProg.Networks.AddRange([jNw1, jNw2]);
            var jmpErrRes = PlcProgramCompiler.Compile(jmpErrProg);
            Assert(!jmpErrRes.Success && jmpErrRes.ErrorCount >= 2,
                $"Suite 9F: Compiler phải bắt lỗi nhảy tới LABEL không tồn tại & vòng lặp vô hạn (Errors={jmpErrRes.ErrorCount}).");

            // Kiểm tra thực thi xuyên suốt các nhóm lệnh (SET_BF, IN_Range, CALCULATE, NORM_X + SCALE_X, GATHER, SWAP, DECO, ENCO, SHL, ROR, JMP/LABEL)
            var fullProg = new PlcProgramModel();
            var nwSetBf = new LadderNetwork { Title = "SET_BF Bit Field" };
            nwSetBf.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.SetBitField, Address = "%M30.0", Param1 = "4" }); // Nhóm 1: M30.0..M30.3 = 1
            var nwAll = new LadderNetwork { Title = "All Groups Runtime Test" };
            nwAll.MainBranch.Nodes.AddRange([
                new LadderNode { Kind = LadderElementKind.Calculate, DataType = "Int", Param1 = "10", Param2 = "5", ExtraParam = "IN1 * 2 + IN2", OutputAddress = "%MW10" }, // Nhóm 5: MW10 = 25
                new LadderNode { Kind = LadderElementKind.InRange, DataType = "Int", Param1 = "%MW10", Param2 = "0", ExtraParam = "50" }, // Nhóm 4: 25 in [0..50] -> true
                new LadderNode { Kind = LadderElementKind.NormX, DataType = "Int", Param1 = "13824", Param2 = "0", ExtraParam = "27648", OutputAddress = "%MD40" }, // Nhóm 7: 0.5
                new LadderNode { Kind = LadderElementKind.ScaleX, DataType = "Real", Param1 = "%MD40", Param2 = "0.0", ExtraParam = "100.0", OutputAddress = "%MD44" }, // Nhóm 7: 50.0
                new LadderNode { Kind = LadderElementKind.Gather, Param1 = "%M30.0", Param2 = "4", OutputAddress = "%MW12" }, // Nhóm 6: 0b1111 = 15
                new LadderNode { Kind = LadderElementKind.Swap, DataType = "Word", Param1 = "258", OutputAddress = "%MW14" },             // Nhóm 6: 0x0102 -> 0x0201 = 513
                new LadderNode { Kind = LadderElementKind.WordDeco, DataType = "Word", Param1 = "3", OutputAddress = "%MW16" },           // Nhóm 9: 1 << 3 = 8
                new LadderNode { Kind = LadderElementKind.WordEnco, DataType = "Word", Param1 = "%MW16", OutputAddress = "%MW18" },       // Nhóm 9: bit 3 -> 3
                new LadderNode { Kind = LadderElementKind.ShiftLeft, DataType = "Word", Param1 = "1", Param2 = "4", OutputAddress = "%MW20" }, // Nhóm 10: 16
                new LadderNode { Kind = LadderElementKind.RotateRight, DataType = "Word", Param1 = "1", Param2 = "1", OutputAddress = "%MW22" }, // Nhóm 10: 0x8000 = 32768
                new LadderNode { Kind = LadderElementKind.Jump, Address = "END_SKIP" }                                 // Nhóm 8: nhảy qua Network 3!
            ]);
            var nwSkipped = new LadderNetwork { Title = "Must Be Skipped By JMP" };
            nwSkipped.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Move, Param1 = "999", OutputAddress = "%MW24" });
            var nwEnd = new LadderNetwork { Title = "Target Label" };
            nwEnd.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Label, Address = "END_SKIP" });
            nwEnd.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.Move, Param1 = "77", OutputAddress = "%MW26" });
            fullProg.Networks.AddRange([nwSetBf, nwAll, nwSkipped, nwEnd]);

            var fullComp = PlcProgramCompiler.Compile(fullProg);
            Assert(fullComp.Success && fullComp.ErrorCount == 0,
                $"Suite 9F: Chương trình kiểm thử các nhóm lệnh phải biên dịch 0 lỗi (Errors={fullComp.ErrorCount}: {string.Join("; ", fullComp.Diagnostics.Select(d => d.Message))}).");

            var rtEngine = new PlcRuntimeEngine();
            rtEngine.ExecuteScanCycle(fullProg, 0.05);
            Assert(rtEngine.ReadWordOrConstant("%MW10") == 25 &&
                   Math.Abs(rtEngine.ReadNumber("%MD40", PlcDataType.Real) - 0.5) < 1e-3 &&
                   Math.Abs(rtEngine.ReadNumber("%MD44", PlcDataType.Real) - 50.0) < 1e-3 &&
                   rtEngine.ReadWordOrConstant("%MW12") == 15 &&
                   rtEngine.ReadWordOrConstant("%MW14") == 513 &&
                   rtEngine.ReadWordOrConstant("%MW16") == 8 &&
                   rtEngine.ReadWordOrConstant("%MW18") == 3 &&
                   rtEngine.ReadWordOrConstant("%MW20") == 16 &&
                   rtEngine.ReadTypedValue("%MW22", PlcDataType.Word).AsRawUInt32 == 32768 &&
                   rtEngine.ReadWordOrConstant("%MW24") == 0 &&
                   rtEngine.ReadWordOrConstant("%MW26") == 77,
                $"Suite 9F: Kết quả thực thi các nhóm lệnh sai (MW10={rtEngine.ReadWordOrConstant("%MW10")}, MD44={rtEngine.ReadNumber("%MD44", PlcDataType.Real)}, MW12={rtEngine.ReadWordOrConstant("%MW12")}, MW24={rtEngine.ReadWordOrConstant("%MW24")}, MW26={rtEngine.ReadWordOrConstant("%MW26")}).");

            // 10. Kiểm tra cửa sổ nổi PlcLadderWindow (MinWidth 680, MinHeight 460, ShowInTaskbar)
            var ladderWinCheck = new WiringSim.UI.Dialogs.PlcLadderWindow();
            Assert(ladderWinCheck.MinWidth == 680 && ladderWinCheck.MinHeight == 460 && ladderWinCheck.ShowInTaskbar && !ladderWinCheck.LadderEditor.ShowEmbeddedHeader,
                "Suite 10: PlcLadderWindow phải là cửa sổ nổi MinWidth=680, MinHeight=460, ShowInTaskbar=true và ẩn header trùng lặp.");
            ladderWinCheck.ForceClose();

            // 11. Kiểm tra nâng cấp TIA Portal V21: Xóa sạch thương hiệu hãng, PLC Tags auto-increment + Address Picker, Call options & Truy xuất thuộc tính DB ("TD11".Q)!
            string[] forbiddenBrands = ["S7-1200", "CPU 1214C", "SIMATIC", "SIEMENS"];
            foreach (var dict in new[] { viDict, enDict })
            {
                foreach (object k in dict.Keys)
                {
                    if (dict[k] is string val)
                    {
                        foreach (string brand in forbiddenBrands)
                            Assert(!val.Contains(brand, StringComparison.OrdinalIgnoreCase),
                                $"Suite 11A: Còn sót thương hiệu '{brand}' trong key '{k}': '{val}'!");
                    }
                }
            }
            foreach (var desc in ComponentRegistry.Items)
            {
                foreach (string brand in forbiddenBrands)
                    Assert(!desc.Title.Contains(brand, StringComparison.OrdinalIgnoreCase) && !desc.Subtitle.Contains(brand, StringComparison.OrdinalIgnoreCase),
                        $"Suite 11A: Còn sót thương hiệu '{brand}' trong ComponentRegistry '{desc.TypeKey}'!");
            }

            // 11B. Kiểm tra tự động thêm % và tăng địa chỉ tuần tự theo kiểu dữ liệu + Address Picker
            Assert(PlcAddressHelper.NormalizeAddress("I0.0") == "%I0.0" &&
                   PlcAddressHelper.NormalizeAddress("q0.1") == "%Q0.1" &&
                   PlcAddressHelper.ComputeNextAddress("%I0.0", "Bool") == "%I0.1" &&
                   PlcAddressHelper.ComputeNextAddress("%I0.7", "Bool") == "%I1.0" &&
                   PlcAddressHelper.ComputeNextAddress("Q0.0", "Bool") == "%Q0.1" &&
                   PlcAddressHelper.ComputeNextAddress("%MB0", "Byte") == "%MB1" &&
                   PlcAddressHelper.ComputeNextAddress("%MW0", "Int") == "%MW2" &&
                   PlcAddressHelper.ComputeNextAddress("%MW2", "Word") == "%MW4" &&
                   PlcAddressHelper.ComputeNextAddress("%MD0", "Real") == "%MD4" &&
                   PlcAddressHelper.ComputeNextAddress("%MD4", "Time") == "%MD8" &&
                   PlcAddressHelper.BuildAddress("Q", "Bit", 0, 3) == "%Q0.3" &&
                   PlcAddressHelper.BuildAddress("M", "Word", 6, 0) == "%MW6",
                "Suite 11B: Logic chuẩn hóa & tự động tăng địa chỉ PLC Tags (Bool/Byte/Int/Word/DInt/Real/Time) không khớp chuẩn TIA Portal V21!");

            // 11C. Kiểm tra hộp thoại Call options
            var callOptCheck = new WiringSim.UI.Dialogs.PlcCallOptionsDialog("TON", "IEC_Timer_0_DB", 2);
            Assert(callOptCheck.SelectedDbName == "IEC_Timer_0_DB" && callOptCheck.SelectedDbNumber == 2 && callOptCheck.IsAutomaticMode,
                "Suite 11C: Hộp thoại Call options phải mặc định Automatic với tên IEC_Timer_0_DB và số DB kế tiếp!");
            callOptCheck.Close();

            // 11D. Kiểm tra truy xuất thuộc tính DB bằng ký hiệu ("TD11".Q) và bắt lỗi <???> chưa gán DB!
            var dbSymProg = new PlcProgramModel
            {
                Tags =
                [
                    new PlcTagEntry { Name = "ON/OFF", Address = "%I0.0", DataType = "Bool" },
                    new PlcTagEntry { Name = "LAMP_OUT", Address = "%Q0.0", DataType = "Bool" },
                    new PlcTagEntry { Name = "TD11", Address = "%DB2", DataType = "IEC_Timer" }
                ]
            };
            var dbNw1 = new LadderNetwork { Title = "Timer DB TD11" };
            dbNw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "%I0.0", SymbolName = "ON/OFF" });
            dbNw1.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.TimerTON, Address = "%DB2", SymbolName = "TD11", Param1 = "T#1s" });
            var dbNw2 = new LadderNetwork { Title = "Use \"TD11\".Q" };
            dbNw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.ContactNO, Address = "\"TD11\".Q", SymbolName = "\"TD11\".Q" });
            dbNw2.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.CoilOut, Address = "%Q0.0", SymbolName = "LAMP_OUT" });
            dbSymProg.Networks.AddRange([dbNw1, dbNw2]);

            var dbDisplayPair = dbSymProg.FormatDisplayPair("\"TD11\".Q", "\"TD11\".Q");
            Assert(dbDisplayPair.AbsoluteAddr == "%DB2.Q" && dbDisplayPair.QuotedSymbol == "\"TD11\".Q",
                $"Suite 11D: FormatDisplayPair cho \"TD11\".Q phải trả về ('%DB2.Q', '\"TD11\".Q') (thực tế: {dbDisplayPair}).");

            var dbSymCompile = PlcProgramCompiler.Compile(dbSymProg);
            Assert(dbSymCompile.Success && dbSymCompile.ErrorCount == 0,
                $"Suite 11D: Chương trình dùng tiếp điểm \"TD11\".Q phải biên dịch 0 lỗi (Errors={dbSymCompile.ErrorCount}: {string.Join("; ", dbSymCompile.Diagnostics.Select(d => d.Message))}).");

            var dbRt = new PlcRuntimeEngine();
            dbRt.Di[0] = true;
            for (int t = 0; t < 10; t++) dbRt.ExecuteScanCycle(dbSymProg, 0.05); // 0.5s < 1.0s
            Assert(!dbRt.Dq[0], "Suite 11D: Trước 1s, tiếp điểm \"TD11\".Q phải chưa tác động (%Q0.0 = 0).");
            for (int t = 0; t < 14; t++) dbRt.ExecuteScanCycle(dbSymProg, 0.05); // 1.2s >= 1.0s
            Assert(dbRt.Dq[0], "Suite 11D: Sau 1s, tiếp điểm \"TD11\".Q phải tự đóng và bật %Q0.0 = 1!");

            var unassignedProg = new PlcProgramModel();
            var uNw = new LadderNetwork { Title = "Unassigned DB" };
            uNw.MainBranch.Nodes.Add(new LadderNode { Kind = LadderElementKind.TimerTON, Address = "<???>", Param1 = "T#2s" });
            unassignedProg.Networks.Add(uNw);
            var unassignedRes = PlcProgramCompiler.Compile(unassignedProg);
            Assert(!unassignedRes.Success && unassignedRes.ErrorCount >= 1,
                "Suite 11D: Khối Timer có <???> chưa gán DB phải bị Compiler báo lỗi rõ ràng!");

            int previewIdx = Array.FindIndex(Environment.GetCommandLineArgs(), a => a == "--save-preview-dir");
            if (previewIdx >= 0 && previewIdx + 1 < Environment.GetCommandLineArgs().Length)
            {
                string outDir = Environment.GetCommandLineArgs()[previewIdx + 1];
                Directory.CreateDirectory(outDir);

                RenderV131MainShowcase(Path.Combine(outDir, "preview_v131_main_dark_vi.png"), 1280, 800, useLight: false, lang: "vi");
                RenderV131MainShowcase(Path.Combine(outDir, "preview_v131_main_light_en.png"), 1160, 720, useLight: true, lang: "en");
                RenderV131LadderShowcase(Path.Combine(outDir, "preview_v131_ladder_dark_vi.png"), 1020, 680, useLight: false, lang: "vi", openTags: false);
                RenderV131LadderShowcase(Path.Combine(outDir, "preview_v131_tags_dark_vi.png"), 1060, 620, useLight: false, lang: "vi", openTags: true);
                RenderCallOptionsShowcase(Path.Combine(outDir, "preview_v131_call_options_dark_vi.png"), useLight: false, lang: "vi");
            }

            Console.WriteLine("SELF_VERIFY_PASSED: All v1.3.3_Release suites passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF_VERIFY_FAILED: {ex}");
            return 1;
        }
    }

    private static void RenderV131MainShowcase(string pngPath, int width, int height, bool useLight, string lang)
    {
        var win = new MainWindow { Width = width, Height = height };
        LocalizationManager.SetLanguage(lang);
        ThemeManager.ApplyTheme(useLight ? AppThemePreference.Light : AppThemePreference.Dark);

        var (p5Comps, p5Wires) = PresetCircuitFactory.Presets.Single(p => p.Key == "PRESET_PLC_SEQ").Builder();
        var btnStart = p5Comps.OfType<PushButtonComponent>().Single(b => b.DisplayName == "START");

        var sim = new CircuitSimulationEngine();
        sim.BindCircuit(p5Comps, p5Wires);
        sim.Start();
        btnStart.IsPressed = true;
        sim.StepSimulation(0.05);
        btnStart.IsPressed = false;
        for (int i = 0; i < 28; i++) sim.StepSimulation(0.05);

        win.WiringCanvas.LoadCircuit(p5Comps, p5Wires, autoFit: false, raiseModified: false);
        win.WiringCanvas.SetMode(AppMode.Simulation);

        var root = (FrameworkElement)win.Content;
        win.Content = null;
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        win.WiringCanvas.ResetOrFitView();
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(pngPath);
        encoder.Save(fs);
        win.Close();
    }

    private static void RenderV131LadderShowcase(string pngPath, int width, int height, bool useLight, string lang, bool openTags)
    {
        LocalizationManager.SetLanguage(lang);
        ThemeManager.ApplyTheme(useLight ? AppThemePreference.Light : AppThemePreference.Dark);

        var (p5Comps, p5Wires) = PresetCircuitFactory.Presets.Single(p => p.Key == "PRESET_PLC_SEQ").Builder();
        var plc = p5Comps.OfType<PlcControllerComponent>().Single();
        var btnStart = p5Comps.OfType<PushButtonComponent>().Single(b => b.DisplayName == "START");

        var sim = new CircuitSimulationEngine();
        sim.BindCircuit(p5Comps, p5Wires);
        sim.Start();
        btnStart.IsPressed = true;
        sim.StepSimulation(0.05);
        btnStart.IsPressed = false;
        for (int i = 0; i < 28; i++) sim.StepSimulation(0.05);

        var ladderWin = new WiringSim.UI.Dialogs.PlcLadderWindow { Width = width, Height = height };
        ladderWin.LadderEditor.BindPlc(plc, openTagTable: openTags);
        ladderWin.LadderEditor.SetSimulationMode(true);
        ladderWin.LadderEditor.CompileCurrentProgram();
        ladderWin.LadderEditor.RefreshOnlineMonitoring();
        ladderWin.UpdateWindowHeaderText();

        var root = (FrameworkElement)ladderWin.Content;
        ladderWin.Content = null;
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(pngPath);
        encoder.Save(fs);
        ladderWin.ForceClose();
    }

    private static void RenderCallOptionsShowcase(string pngPath, bool useLight, string lang)
    {
        LocalizationManager.SetLanguage(lang);
        ThemeManager.ApplyTheme(useLight ? AppThemePreference.Light : AppThemePreference.Dark);

        var dlg = new WiringSim.UI.Dialogs.PlcCallOptionsDialog("TON", "IEC_Timer_0_DB", 2);
        int width = 520, height = 340;
        var root = (FrameworkElement)dlg.Content;
        dlg.Content = null;

        var host = new System.Windows.Controls.Border
        {
            Width = width,
            Height = height,
            Child = root
        };
        host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AppBgBrush");
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(pngPath);
        encoder.Save(fs);
        dlg.Close();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
