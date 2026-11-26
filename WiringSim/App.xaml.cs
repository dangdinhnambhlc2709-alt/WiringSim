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

            // 2. Kiểm tra Metadata phiên bản v1.0_Release & UpdateService.NormalizeVersion
            string infoVer = UpdateService.GetCurrentInformationalVersion();
            Assert(string.Equals(infoVer, "v1.0_Release", StringComparison.Ordinal),
                $"Suite 2: InformationalVersion phải là 'v1.0_Release' (thực tế: '{infoVer}').");
            Assert(UpdateService.NormalizeVersion("v1.0_Release") == new Version(1, 0, 0),
                "Suite 2: NormalizeVersion('v1.0_Release') phải trả về 1.0.0.");
            Assert(UpdateService.NormalizeVersion("V2.1.4-beta") == new Version(2, 1, 4),
                "Suite 2: NormalizeVersion('V2.1.4-beta') phải trả về 2.1.4.");
            Assert(UpdateService.DefaultApiUrl.Contains("dangdinhnambhlc2709-alt/WiringSim", StringComparison.Ordinal),
                "Suite 2: DefaultApiUrl phải trỏ đúng repo dangdinhnambhlc2709-alt/WiringSim.");

            // 3. Kiểm tra Canvas khởi động TRỐNG HOÀN TOÀN
            LocalizationManager.SetLanguage("vi");
            ThemeManager.ApplyTheme(AppThemePreference.Dark);
            var startupWin = new MainWindow();
            Assert(startupWin.WiringCanvas.Components.Count == 0 && startupWin.WiringCanvas.Wires.Count == 0,
                "Suite 3: Canvas phải trống hoàn toàn khi vừa khởi động ứng dụng!");
            startupWin.Close();

            // 4. Kiểm tra Biến tần (VFD) tăng tốc ACC / giảm tốc DEC, đảo chiều REV, đa cấp tốc độ DI1 & quay động cơ 3 pha 3 chân!
            var vfdSrc = new Ac3PhaseSourceComponent();
            var vfd = new VfdComponent
            {
                P01_RunSource = 0,
                P02_FreqSource = 0,
                P03_MaxFreqHz = 50.0,
                P04_AccelSeconds = 2.0,
                P05_DecelSeconds = 2.0,
                P06_Di1FreqHz = 25.0,
                KeypadTargetFreqHz = 50.0
            };
            var m3Pin = new Motor3Phase3PinComponent { RatedPowerKw = 3.0, InternalConnection = Motor3PinConnection.Star_380V };
            var swDi1 = new SwitchComponent();

            var vfdWires = new List<WireModel>
            {
                new(vfdSrc.L1Terminal, vfd.R_In, WireColorPreset.Red_L1_DCPlus),
                new(vfdSrc.L2Terminal, vfd.S_In, WireColorPreset.Yellow_L2),
                new(vfdSrc.L3Terminal, vfd.T_In, WireColorPreset.Blue_L3),
                new(vfd.U_Out, m3Pin.U1, WireColorPreset.Red_L1_DCPlus),
                new(vfd.V_Out, m3Pin.V1, WireColorPreset.Yellow_L2),
                new(vfd.W_Out, m3Pin.W1, WireColorPreset.Blue_L3),
                new(vfd.Com, swDi1.ComOrInTerminal!, WireColorPreset.Black_N_DCMinus),
                new(swDi1.Out1Terminal!, vfd.Di1, WireColorPreset.Blue_L3)
            };

            engine.BindCircuit([vfdSrc, vfd, m3Pin, swDi1], vfdWires);
            engine.Start();
            Assert(vfd.IsPowered && !vfd.IsRunning, "Suite 4: Biến tần phải có nguồn nhưng đang STOP.");

            vfd.KeypadRunCommand = true;
            for (int i = 0; i < 20; i++) engine.StepSimulation(0.05); // 1.0s -> ~25Hz (ACC=2s cho 50Hz)
            Assert(vfd.IsRunning && vfd.CurrentOutputFreqHz > 20.0 && vfd.CurrentOutputFreqHz < 30.0,
                $"Suite 4: Biến tần phải tăng tốc mượt (tại 1s kỳ vọng ~25Hz, thực tế: {vfd.CurrentOutputFreqHz:0.0}Hz).");

            for (int i = 0; i < 25; i++) engine.StepSimulation(0.05); // đạt 50Hz
            Assert(Math.Abs(vfd.CurrentOutputFreqHz - 50.0) < 0.5 && m3Pin.RotationDirection == 1 && vfd.OutputCurrentAmps >= 6.0,
                $"Suite 4: Động cơ 3 chân qua VFD phải quay thuận 50Hz (Freq={vfd.CurrentOutputFreqHz:0.0}, Dir={m3Pin.RotationDirection}, I={vfd.OutputCurrentAmps:0.0}A).");

            // Kích DI1 -> giảm mượt về 25Hz
            swDi1.Position = 1;
            for (int i = 0; i < 30; i++) engine.StepSimulation(0.05);
            Assert(Math.Abs(vfd.CurrentOutputFreqHz - 25.0) < 0.5 && Math.Abs(m3Pin.AppliedFrequencyHz - 25.0) < 0.5,
                $"Suite 4: Khi đóng DI1-COM, tần số phải về 25Hz (thực tế: VFD={vfd.CurrentOutputFreqHz:0.0}Hz, Motor={m3Pin.AppliedFrequencyHz:0.0}Hz).");

            // Đảo chiều REV trên Keypad
            vfd.KeypadReverse = true;
            for (int i = 0; i < 50; i++) engine.StepSimulation(0.05);
            Assert(vfd.CurrentSignedFreqHz < -20.0 && m3Pin.RotationDirection == -1,
                $"Suite 4: Khi đảo REV, động cơ phải quay ngược (SignedHz={vfd.CurrentSignedFreqHz:0.0}, Dir={m3Pin.RotationDirection}).");
            engine.Stop();

            // 5. Kiểm tra Động cơ 3 pha 6 chân chế độ 2 cấp tốc độ Dahlander (Thấp Δ vs Cao YY)!
            var m6Dahlander = new Motor3Phase6PinComponent { WindingType = Motor6PinWindingType.Dahlander2Speed, RatedPowerKw = 5.5 };
            // 5A. Tốc độ THẤP: Cấp 3 pha vào U1,V1,W1; hở U2,V2,W2
            var lowWires = new List<WireModel>
            {
                new(vfdSrc.L1Terminal, m6Dahlander.U1, WireColorPreset.Red_L1_DCPlus),
                new(vfdSrc.L2Terminal, m6Dahlander.V1, WireColorPreset.Yellow_L2),
                new(vfdSrc.L3Terminal, m6Dahlander.W1, WireColorPreset.Blue_L3)
            };
            engine.BindCircuit([vfdSrc, m6Dahlander], lowWires);
            engine.Start();
            engine.StepSimulation(0.05);
            m6Dahlander.UpdateVisualAnimation(1.0, isSimulating: true);
            double lowVel = Math.Abs(m6Dahlander.CurrentAngularVelocity);
            Assert(m6Dahlander.RotationDirection == 1 && lowVel > 50.0,
                $"Suite 5A: Dahlander tốc độ thấp phải quay ổn định (Vel={lowVel:0.0}).");
            engine.Stop();

            // 5B. Tốc độ CAO: Cấp 3 pha vào U2,V2,W2; chụm U1-V1-W1
            var highWires = new List<WireModel>
            {
                new(vfdSrc.L1Terminal, m6Dahlander.U2, WireColorPreset.Red_L1_DCPlus),
                new(vfdSrc.L2Terminal, m6Dahlander.V2, WireColorPreset.Yellow_L2),
                new(vfdSrc.L3Terminal, m6Dahlander.W2, WireColorPreset.Blue_L3),
                new(m6Dahlander.U1, m6Dahlander.V1, WireColorPreset.Black_N_DCMinus),
                new(m6Dahlander.V1, m6Dahlander.W1, WireColorPreset.Black_N_DCMinus)
            };
            engine.BindCircuit([vfdSrc, m6Dahlander], highWires);
            engine.Start();
            engine.StepSimulation(0.05);
            m6Dahlander.UpdateVisualAnimation(1.0, isSimulating: true);
            double highVel = Math.Abs(m6Dahlander.CurrentAngularVelocity);
            Assert(m6Dahlander.RotationDirection == 1 && highVel > lowVel * 1.5,
                $"Suite 5B: Dahlander tốc độ CAO phải quay nhanh hơn rõ rệt so với tốc độ THẤP (High={highVel:0.0}, Low={lowVel:0.0}).");
            engine.Stop();

            // 6. Kiểm tra Xoay (90° CW/CCW) & Lật (FlipH/V) cập nhật chính xác tọa độ chân + Lưu/Mở JSON round-trip!
            var rotMcb = new Mcb1PhaseComponent { DisplayName = "Q_ROT", X = 200, Y = 100 }; // 80 x 120, center=(240,160), In1=(20,0)
            double origX = rotMcb.In1.CanvasX;
            double origY = rotMcb.In1.CanvasY;
            rotMcb.RotateCW(); // 90° CW: dx=-20, dy=-60 -> (240 - (-60), 160 + (-20)) = (300, 140), Side: Top -> Right!
            Assert(Math.Abs(rotMcb.In1.CanvasX - 300.0) < 0.1 && Math.Abs(rotMcb.In1.CanvasY - 140.0) < 0.1 && rotMcb.In1.EffectiveSide == TerminalSide.Right,
                $"Suite 6: Xoay 90° CW tính sai tọa độ chân (thực tế: {rotMcb.In1.CanvasX},{rotMcb.In1.CanvasY} Side={rotMcb.In1.EffectiveSide}).");
            rotMcb.FlipHorizontal();

            string json = CircuitSerializer.SerializeToJsonString([rotMcb, vfd, m3Pin, m6Dahlander], [], "TestTransformV1");
            var (loadedComps, _, _) = CircuitSerializer.DeserializeFromJsonString(json);
            var loadedMcb = loadedComps.OfType<Mcb1PhaseComponent>().First();
            Assert(loadedMcb.RotationAngle == 90 && loadedMcb.IsFlippedHorizontal && !loadedMcb.IsFlippedVertical,
                "Suite 6: Trạng thái Xoay/Lật phải được lưu & nạp lại chính xác từ tệp JSON!");
            var loadedVfd = loadedComps.OfType<VfdComponent>().First();
            Assert(Math.Abs(loadedVfd.P04_AccelSeconds - 2.0) < 0.01 && Math.Abs(loadedVfd.P06_Di1FreqHz - 25.0) < 0.01,
                "Suite 6: Thông số Biến tần P01–P06 phải lưu & nạp lại chính xác từ JSON!");
            rotMcb.FlipHorizontal();
            rotMcb.RotateCCW();
            Assert(Math.Abs(rotMcb.In1.CanvasX - origX) < 0.1 && Math.Abs(rotMcb.In1.CanvasY - origY) < 0.1,
                "Suite 6: Hoàn tác Xoay/Lật phải trả chân về đúng tọa độ ban đầu.");

            // 7. Kiểm tra Đồng hồ Volt, Ampe nối tiếp, Biến dòng CT & Đấu nhầm Ampe kế song song!
            var src3 = new Ac3PhaseSourceComponent();
            var mcb3 = new Mcb3PhaseComponent { IsOn = true };
            var ct = new CurrentTransformerComponent { Ratio = CtRatio.Ratio_50_5 };
            var amDirect = new AmmeterComponent { MeasureKind = CurrentKind.AC, Range = AmmeterRange.Direct_30A };
            var amViaCt = new AmmeterComponent { MeasureKind = CurrentKind.AC, Range = AmmeterRange.ViaCT_50_5 };
            var vm380 = new VoltmeterComponent { MeasureKind = CurrentKind.AC, Range = VoltmeterRange.Range_500V };
            var motor75 = new Motor3Phase3PinComponent { RatedPowerKw = 7.5 }; // In = 15.5A

            var meterWires = new List<WireModel>
            {
                new(src3.L1Terminal, mcb3.In1, WireColorPreset.Red_L1_DCPlus),
                new(src3.L2Terminal, mcb3.In3, WireColorPreset.Yellow_L2),
                new(src3.L3Terminal, mcb3.In5, WireColorPreset.Blue_L3),
                new(mcb3.Out2, ct.P1, WireColorPreset.Red_L1_DCPlus),
                new(ct.P2, motor75.U1, WireColorPreset.Red_L1_DCPlus),
                new(mcb3.Out4, amDirect.I1, WireColorPreset.Yellow_L2),
                new(amDirect.I2, motor75.V1, WireColorPreset.Yellow_L2),
                new(mcb3.Out6, motor75.W1, WireColorPreset.Blue_L3),
                new(ct.S1, amViaCt.I1, WireColorPreset.Blue_L3),
                new(ct.S2, amViaCt.I2, WireColorPreset.Black_N_DCMinus),
                new(mcb3.Out2, vm380.V1, WireColorPreset.Red_L1_DCPlus),
                new(mcb3.Out4, vm380.V2, WireColorPreset.Yellow_L2)
            };

            engine.BindCircuit([src3, mcb3, ct, amDirect, amViaCt, vm380, motor75], meterWires);
            engine.Start();
            engine.StepSimulation(0.05);
            Assert(Math.Abs(vm380.MeasuredVoltage - 380.0) <= 3.0 &&
                   Math.Abs(amDirect.DisplayedAmps - 15.5) < 0.2 &&
                   Math.Abs(amViaCt.DisplayedAmps - 15.5) < 0.2,
                $"Suite 7: Volt={vm380.MeasuredVoltage}, AmDirect={amDirect.DisplayedAmps}, AmCT={amViaCt.DisplayedAmps}.");
            engine.Stop();

            // 8. Kết xuất ảnh trực quan v1.0_Release (Dark VI có Biến tần VFD, Động cơ 3P/6P & thiết bị xoay 90°; Light EN)
            int previewIdx = Array.FindIndex(Environment.GetCommandLineArgs(), a => a == "--save-preview-dir");
            if (previewIdx >= 0 && previewIdx + 1 < Environment.GetCommandLineArgs().Length)
            {
                string outDir = Environment.GetCommandLineArgs()[previewIdx + 1];
                Directory.CreateDirectory(outDir);

                RenderDemoScene(Path.Combine(outDir, "preview_v1_release_dark_vi.png"), useLight: false, lang: "vi");
                RenderDemoScene(Path.Combine(outDir, "preview_v1_release_light_en.png"), useLight: true, lang: "en");
            }

            Console.WriteLine("SELF_VERIFY_PASSED: All v1.0_Release suites (Localization Parity, Version/UpdateService, Empty Startup, VFD Ramp+REV+DI1, 3-Pin & 6-Pin Dahlander Motors, Rotate/Flip+JSON, Meters/CT, Visual Render) passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF_VERIFY_FAILED: {ex}");
            return 1;
        }
    }

    private static void RenderDemoScene(string pngPath, bool useLight, string lang)
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

        if (!useLight)
        {
            // Trình diễn Biến tần VFD cấp nguồn Động cơ 3 chân, Động cơ 6 chân Dahlander và Aptomat xoay 90°!
            var src3 = new Ac3PhaseSourceComponent { DisplayName = "AC 3P", X = 80, Y = 40 };
            var mcb3 = new Mcb3PhaseComponent { DisplayName = "Q1", X = 80, Y = 180, IsOn = true };
            var vfd = new VfdComponent { DisplayName = "VFD1", X = 280, Y = 120, KeypadRunCommand = true, KeypadTargetFreqHz = 38.5 };
            var m3p = new Motor3Phase3PinComponent { DisplayName = "M1", X = 280, Y = 380, RatedPowerKw = 3.0 };
            var m6p = new Motor3Phase6PinComponent { DisplayName = "M2", X = 600, Y = 360, WindingType = Motor6PinWindingType.Dahlander2Speed };
            var rotSw = new SwitchComponent { DisplayName = "S1", X = 640, Y = 160 };
            rotSw.RotateCW();

            var wires = new List<WireModel>
            {
                new(src3.L1Terminal, mcb3.In1, WireColorPreset.Red_L1_DCPlus),
                new(src3.L2Terminal, mcb3.In3, WireColorPreset.Yellow_L2),
                new(src3.L3Terminal, mcb3.In5, WireColorPreset.Blue_L3),
                new(mcb3.Out2, vfd.R_In, WireColorPreset.Red_L1_DCPlus),
                new(mcb3.Out4, vfd.S_In, WireColorPreset.Yellow_L2),
                new(mcb3.Out6, vfd.T_In, WireColorPreset.Blue_L3),
                new(vfd.U_Out, m3p.U1, WireColorPreset.Red_L1_DCPlus),
                new(vfd.V_Out, m3p.V1, WireColorPreset.Yellow_L2),
                new(vfd.W_Out, m3p.W1, WireColorPreset.Blue_L3),
                new(vfd.Com, rotSw.ComOrInTerminal!, WireColorPreset.Black_N_DCMinus),
                new(rotSw.Out1Terminal!, vfd.Fwd, WireColorPreset.Blue_L3)
            };

            var sim = new CircuitSimulationEngine();
            sim.BindCircuit([src3, mcb3, vfd, m3p, m6p, rotSw], wires);
            sim.Start();
            for (int i = 0; i < 60; i++) sim.StepSimulation(0.05);
            m3p.UpdateVisualAnimation(0.4, true);

            win.WiringCanvas.LoadCircuit([src3, mcb3, vfd, m3p, m6p, rotSw], wires, autoFit: true, raiseModified: false);
            win.WiringCanvas.SetMode(AppMode.Simulation);
        }
        else
        {
            var (c3, w3) = PresetCircuitFactory.Presets[2].Builder();
            win.WiringCanvas.LoadCircuit(c3, w3, autoFit: true, raiseModified: false);
            win.WiringCanvas.SetRoutingStyle(WireRoutingStyle.Curved);
        }

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
