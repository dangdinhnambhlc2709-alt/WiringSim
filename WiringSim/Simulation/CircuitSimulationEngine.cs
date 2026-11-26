using System.Windows;
using System.Windows.Threading;
using WiringSim.Models;
using WiringSim.Models.Components.Control;
using WiringSim.Models.Components.Loads;
using WiringSim.Models.Enums;
using WiringSim.UI.Services;

namespace WiringSim.Simulation;

/// <summary>
/// LỚP 3: SIMULATION ENGINE (v3.0)
/// - Duyệt đồ thị mạch điện 50ms BFS/DFS.
/// - Phát hiện ngắn mạch tổng quát &amp; bắt lỗi chuyên biệt khi đấu nhầm Đồng hồ Ampe song song vào nguồn!
/// - Giải bài toán dòng điện nhánh nối tiếp cho Đồng hồ Ampe (I1-I2) và Biến dòng đo lường CT (P1-P2 -> S1-S2 /5A).
/// - Kiểm tra quá áp cháy cuộn hút / cháy đèn / cháy cảm biến &amp; bảo vệ quá tải Rơ-le nhiệt.
/// </summary>
public sealed class CircuitSimulationEngine
{
    public const int TickIntervalMs = 50;
    public const double TickIntervalSeconds = TickIntervalMs / 1000.0;

    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ElectricalComponent> _components = Array.Empty<ElectricalComponent>();
    private IReadOnlyList<WireModel> _wires = Array.Empty<WireModel>();

    private bool _unprotectedFaultLockout;

    public bool IsRunning { get; private set; }
    public ShortCircuitFaultInfo? ActiveFault { get; private set; }

    public event Action? SimulationTickCompleted;
    public event Action<ShortCircuitFaultInfo>? ShortCircuitDetected;
    public event Action<string>? ComponentFaultAlert;

    private readonly record struct GraphEdge(
        Terminal To,
        WireModel? Wire,
        ElectricalComponent? InternalComponent,
        string Label);

    public CircuitSimulationEngine()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(TickIntervalMs)
        };
        _timer.Tick += (_, _) => StepSimulation(TickIntervalSeconds);
    }

    public void BindCircuit(IReadOnlyList<ElectricalComponent> components, IReadOnlyList<WireModel> wires)
    {
        _components = components;
        _wires = wires;
    }

    public void Start()
    {
        if (IsRunning) return;
        _unprotectedFaultLockout = false;
        ActiveFault = null;
        IsRunning = true;
        StepSimulation(0.0);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        IsRunning = false;
        _unprotectedFaultLockout = false;
        ActiveFault = null;

        foreach (var wire in _wires)
            wire.ResetSimulationState();

        foreach (var comp in _components)
            comp.ResetSimulationState();

        SimulationTickCompleted?.Invoke();
    }

    public void ClearFaultAlert()
    {
        ActiveFault = null;
        _unprotectedFaultLockout = false;
        SimulationTickCompleted?.Invoke();
    }

    public void StepSimulation(double deltaSeconds)
    {
        if (!IsRunning) return;

        foreach (var comp in _components)
        {
            foreach (var term in comp.Terminals)
                term.ResetElectricalState();
        }

        foreach (var wire in _wires)
        {
            wire.ResetSimulationState();
        }

        if (_unprotectedFaultLockout && ActiveFault is not null)
        {
            foreach (var w in _wires)
            {
                if (ActiveFault.FaultedWireIds.Contains(w.Id))
                    w.IsShortCircuitPath = true;
            }
            foreach (var comp in _components)
                comp.EvaluateSimulationStep(deltaSeconds, _wires);

            SimulationTickCompleted?.Invoke();
            return;
        }

        var allSources = new List<(Terminal Terminal, PotentialToken Token)>();
        foreach (var comp in _components)
        {
            allSources.AddRange(comp.GetSourceOutputs());
        }

        // Tiền kiểm tra Cảm biến / Công tắc 2 dây (AC/DC, phân cực DC, mức điện áp) trước khi xây đồ thị dẫn điện chính
        EvaluateTwoWireSensorStates(allSources);

        var adj = BuildConductiveGraph();
        var nets = FindConnectedNetsBfs(adj);

        foreach (var net in nets)
        {
            var netSources = allSources.Where(s => s.Terminal.NetId == net.NetId).ToList();
            if (netSources.Count >= 2 && TryFindConflict(netSources, out var srcA, out var srcB, out string conflictReason))
            {
                HandleShortCircuit(adj, net.Terminals, srcA, srcB, conflictReason);

                if (!_unprotectedFaultLockout)
                {
                    foreach (var comp in _components)
                        foreach (var term in comp.Terminals)
                            term.NetId = -1;

                    adj = BuildConductiveGraph();
                    nets = FindConnectedNetsBfs(adj);
                    if (ActiveFault is not null)
                    {
                        foreach (var w in _wires)
                        {
                            if (ActiveFault.FaultedWireIds.Contains(w.Id))
                                w.IsShortCircuitPath = true;
                        }
                    }
                }
                else
                {
                    SimulationTickCompleted?.Invoke();
                    return;
                }
                break;
            }
        }

        if (ActiveFault is not null)
        {
            foreach (var w in _wires)
            {
                if (ActiveFault.FaultedWireIds.Contains(w.Id))
                    w.IsShortCircuitPath = true;
            }
        }

        // Truyền thế điện áp cho mọi chân và dây trong từng Net hợp lệ (tính kèm sụt áp nối tiếp của cảm biến 2 dây!)
        foreach (var net in nets)
        {
            var netSources = allSources.Where(s => s.Terminal.NetId == net.NetId).ToList();
            if (netSources.Count == 0) continue;

            var distinctTokens = netSources.Select(s => s.Token).Distinct().ToList();
            var primary = PickPrimaryDisplayToken(distinctTokens);
            bool isHotPotential = distinctTokens.Any(IsHotPhase);
            double baseDisplayVolts = termVoltageDisplay(primary, distinctTokens, isHotPotential);

            var dropMap = ComputeTwoWireVoltageDrops(net, netSources, adj, primary.CurrentKind);

            foreach (var term in net.Terminals)
            {
                double drop = dropMap.GetValueOrDefault(term, 0.0);
                if (drop > 0.001)
                {
                    foreach (var tok in distinctTokens)
                        term.ActivePotentials.Add(tok with { SeriesVoltageDrop = tok.SeriesVoltageDrop + drop });
                }
                else
                {
                    term.ActivePotentials.AddRange(distinctTokens);
                }

                term.CurrentKind = primary.CurrentKind;
                term.Phase = primary.Phase;
                term.Voltage = isHotPotential
                    ? Math.Round(Math.Max(0.0, baseDisplayVolts - drop), 1)
                    : baseDisplayVolts;
                term.IsEnergized = isHotPotential;
            }

            foreach (var wire in _wires)
            {
                if (wire.TerminalA.NetId == net.NetId)
                {
                    double wireDrop = Math.Max(
                        dropMap.GetValueOrDefault(wire.TerminalA, 0.0),
                        dropMap.GetValueOrDefault(wire.TerminalB, 0.0));
                    wire.CarriedPhase = primary.Phase;
                    wire.CarriedVoltage = isHotPotential
                        ? Math.Round(Math.Max(0.0, baseDisplayVolts - wireDrop), 1)
                        : baseDisplayVolts;
                    wire.IsEnergized = isHotPotential;
                }
            }
        }

        var previouslyBurned = _components.Where(c => c.IsBurnedOut).ToHashSet();

        // 1. Đánh giá các tải tiêu thụ & điều khiển trước để biết tải nào đang hoạt động và tiêu thụ bao nhiêu Ampe
        foreach (var comp in _components)
        {
            if (comp is AmmeterComponent or CurrentTransformerComponent or ThermalOverloadRelayComponent or FuseComponent)
                continue;

            comp.EvaluateSimulationStep(deltaSeconds, _wires);
            if (comp.IsBurnedOut && !previouslyBurned.Contains(comp) && !string.IsNullOrWhiteSpace(comp.BurnoutReason))
            {
                ComponentFaultAlert?.Invoke(comp.BurnoutReason!);
            }
        }

        // 2. Cập nhật dòng tải đầu ra Biến tần (VFD), Rơ-le nhiệt & bảo vệ quá tải Cầu chì (RT18)
        UpdateVfdOutputCurrents();
        UpdateThermalOverloadRelayCurrents();
        foreach (var rn in _components.OfType<ThermalOverloadRelayComponent>())
        {
            rn.EvaluateSimulationStep(deltaSeconds, _wires);
        }

        EvaluateFuseOverloadProtection(adj, allSources, deltaSeconds);

        // 3. Giải bài toán dòng điện nhánh nối tiếp cho Biến dòng CT (P1-P2) và Đồng hồ Ampe (I1-I2)
        SolveCurrentTransformersAndAmmeters(adj, allSources, deltaSeconds);

        // 4. Đánh dấu hiệu ứng dòng điện chạy (Live Wire)
        MarkActiveCurrentFlowWires(adj, nets, allSources);

        SimulationTickCompleted?.Invoke();
    }

    private static double termVoltageDisplay(PotentialToken primary, List<PotentialToken> tokens, bool isHot) =>
        primary.CurrentKind == CurrentKind.AC && isHot && tokens.Any(t => t.Phase is PhaseType.L2 or PhaseType.L3)
            ? Math.Round(primary.Voltage * Math.Sqrt(3.0), 0)
            : primary.Voltage;

    /// <summary>
    /// Đánh giá trước điều kiện dẫn điện của các Cảm biến / Công tắc 2 dây:
    /// - Cảm biến 2 dây DC (10–30VDC): kiểm tra đúng phân cực (BN về phía +, BU về phía tải/-); nếu ngược cực thì không dẫn!
    /// - Cảm biến 2 dây AC (90–250VAC): chỉ dẫn với nguồn AC hợp lệ (trừ trường hợp đấu trực tiếp 2 đầu nguồn gây ngắn mạch).
    /// - Công tắc từ xi-lanh (SMC D-A93), Công tắc nhiệt (KSD301), Phao nước 2 dây: hỗ trợ AC/DC không phân cực.
    /// </summary>
    private void EvaluateTwoWireSensorStates(List<(Terminal Terminal, PotentialToken Token)> allSources)
    {
        var activeTwoWire = _components
            .OfType<ITwoWireSensorDevice>()
            .Where(d => d.IsTwoWireActiveMode)
            .ToList();

        if (activeTwoWire.Count == 0) return;

        foreach (var dev in activeTwoWire)
        {
            dev.IsConductingNow = false;
            dev.IsReversePolarity = false;
        }

        var preAdj = BuildConductiveGraph();
        _ = FindConnectedNetsBfs(preAdj);

        var tokensByNet = new Dictionary<int, List<PotentialToken>>();
        foreach (var (term, tok) in allSources)
        {
            if (term.NetId < 0) continue;
            if (!tokensByNet.TryGetValue(term.NetId, out var list))
            {
                list = new List<PotentialToken>();
                tokensByNet[term.NetId] = list;
            }
            list.Add(tok);
        }

        foreach (var dev in activeTwoWire)
        {
            if (dev.IsDamaged || !dev.WantsToConduct)
                continue;

            var tokensA = tokensByNet.GetValueOrDefault(dev.PinPositiveOrLine.NetId) ?? [];
            var tokensB = tokensByNet.GetValueOrDefault(dev.PinNegativeOrLoad.NetId) ?? [];

            bool aHasPlus = tokensA.Any(t => t.CurrentKind == CurrentKind.DC && t.Phase == PhaseType.DC_Plus);
            bool aHasMinus = tokensA.Any(t => t.CurrentKind == CurrentKind.DC && t.Phase == PhaseType.DC_Minus);
            bool bHasPlus = tokensB.Any(t => t.CurrentKind == CurrentKind.DC && t.Phase == PhaseType.DC_Plus);
            bool bHasMinus = tokensB.Any(t => t.CurrentKind == CurrentKind.DC && t.Phase == PhaseType.DC_Minus);

            bool aHasAc = tokensA.Any(t => t.CurrentKind == CurrentKind.AC);
            bool bHasAc = tokensB.Any(t => t.CurrentKind == CurrentKind.AC);
            bool anyAc = aHasAc || bHasAc;
            bool anyDc = aHasPlus || aHasMinus || bHasPlus || bHasMinus;

            if (dev.RequiresDcPolarity)
            {
                // Cảm biến 2 dây DC: bắt buộc + vào BN, tải/- ra BU
                if (bHasPlus || aHasMinus)
                {
                    dev.IsReversePolarity = true;
                    dev.IsConductingNow = false;
                    continue;
                }

                if (anyAc && !anyDc)
                {
                    // Nếu đấu thẳng 2 dây vào nguồn AC không qua tải vẫn cho đóng để kích hoạt bảo vệ ngắn mạch/cháy
                    dev.IsConductingNow = aHasAc && bHasAc;
                    continue;
                }

                if (aHasPlus)
                {
                    double maxDcV = tokensA.Where(t => t.CurrentKind == CurrentKind.DC).Max(t => t.Voltage);
                    if (maxDcV < 9.5 && !bHasMinus)
                    {
                        dev.IsConductingNow = false;
                        continue;
                    }
                }

                dev.IsConductingNow = true;
            }
            else if (!dev.AcceptsDc && dev.AcceptsAc)
            {
                // Cảm biến 2 dây AC (90–250VAC)
                if (anyDc && !anyAc)
                {
                    dev.IsConductingNow = (aHasPlus || aHasMinus) && (bHasPlus || bHasMinus);
                    continue;
                }

                if (anyAc && !(aHasAc && bHasAc))
                {
                    double maxAcV = tokensA.Concat(tokensB)
                        .Where(t => t.CurrentKind == CurrentKind.AC)
                        .Select(t => t.Voltage)
                        .DefaultIfEmpty(0.0)
                        .Max();
                    if (maxAcV > 0.1 && maxAcV < 80.0)
                    {
                        dev.IsConductingNow = false;
                        continue;
                    }
                }

                dev.IsConductingNow = true;
            }
            else
            {
                // Công tắc từ xi-lanh (AC/DC 5-240V), Công tắc nhiệt KSD301, Phao mức nước 2 dây: không phân cực
                dev.IsConductingNow = true;
            }
        }

        // Xóa NetId tạm trước khi chạy FindConnectedNetsBfs chính thức
        foreach (var comp in _components)
            foreach (var term in comp.Terminals)
                term.NetId = -1;
    }

    /// <summary>
    /// Tính sụt áp nhỏ nhất (Dijkstra) từ các chân nguồn tới mọi chân trong Net khi dòng đi qua Cảm biến 2 dây
    /// (2.5V cho DC và 3.5V cho AC).
    /// </summary>
    private static Dictionary<Terminal, double> ComputeTwoWireVoltageDrops(
        ConnectedNet net,
        List<(Terminal Terminal, PotentialToken Token)> netSources,
        Dictionary<Terminal, List<GraphEdge>> adj,
        CurrentKind netKind)
    {
        var dist = new Dictionary<Terminal, double>(net.Terminals.Count);
        var pq = new PriorityQueue<Terminal, double>();

        foreach (var src in netSources)
        {
            if (!dist.ContainsKey(src.Terminal))
            {
                dist[src.Terminal] = 0.0;
                pq.Enqueue(src.Terminal, 0.0);
            }
        }

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var u, out double dU);
            if (u is null || dU > dist.GetValueOrDefault(u, double.MaxValue) + 1e-6)
                continue;

            if (!adj.TryGetValue(u, out var edges)) continue;
            foreach (var edge in edges)
            {
                double edgeDrop = 0.0;
                if (edge.InternalComponent is ITwoWireSensorDevice dev && dev.IsTwoWireActiveMode)
                {
                    edgeDrop = netKind == CurrentKind.DC ? dev.VoltageDropDc : dev.VoltageDropAc;
                }

                double nd = dU + edgeDrop;
                if (!dist.TryGetValue(edge.To, out double curD) || nd < curD - 1e-6)
                {
                    dist[edge.To] = nd;
                    pq.Enqueue(edge.To, nd);
                }
            }
        }

        return dist;
    }

    /// <summary>
    /// Tính toán dòng điện thực tế đi qua Biến dòng CT (P1-P2 -> quy đổi sang thứ cấp S1-S2 /5A)
    /// và Đồng hồ Ampe (I1-I2 trực tiếp hoặc qua mạch vòng thứ cấp CT).
    /// </summary>
    private void SolveCurrentTransformersAndAmmeters(
        Dictionary<Terminal, List<GraphEdge>> adj,
        List<(Terminal Terminal, PotentialToken Token)> allSources,
        double deltaSeconds)
    {
        foreach (var am in _components.OfType<AmmeterComponent>())
        {
            am.RawInputAmps = 0;
            am.RawInputKind = CurrentKind.None;
        }

        // A. Tính dòng sơ cấp P1-P2 và thứ cấp S1-S2 cho mọi Biến dòng CT
        foreach (var ct in _components.OfType<CurrentTransformerComponent>())
        {
            var (amps, kind) = ComputeSeriesBranchCurrentThroughComponent(ct, ct.P1, ct.P2, adj, allSources);
            // Biến dòng từ (CT) chỉ cảm ứng dòng điện xoay chiều (AC)
            if (kind == CurrentKind.AC && amps > 0.001)
            {
                ct.PrimaryCurrentAmps = Math.Round(amps, 2);
                var (primaryRated, _) = ct.GetRatioSpec();
                // Dòng thứ cấp quy đổi theo tỷ số N1/5A (giới hạn bão hòa từ lõi sắt ~7.5A)
                ct.SecondaryCurrentAmps = Math.Min(7.5, ct.PrimaryCurrentAmps * (5.0 / primaryRated));
            }
            else
            {
                ct.PrimaryCurrentAmps = 0;
                ct.SecondaryCurrentAmps = 0;
            }

            // Nếu thứ cấp S1 - S2 được đấu khép kín qua Đồng hồ Ampe, truyền dòng thứ cấp sang Ampe kế!
            if (ct.SecondaryCurrentAmps > 0.001 && ct.S1.NetId >= 0 && ct.S1.NetId == ct.S2.NetId)
            {
                PropagateCtSecondaryLoop(ct, adj);
            }
        }

        // B. Tính dòng trực tiếp cho các Đồng hồ Ampe đấu nối tiếp trên nhánh tải
        foreach (var am in _components.OfType<AmmeterComponent>())
        {
            if (am.RawInputAmps <= 0.001)
            {
                var (amps, kind) = ComputeSeriesBranchCurrentThroughComponent(am, am.I1, am.I2, adj, allSources);
                if (amps > 0.001)
                {
                    am.RawInputAmps = amps;
                    am.RawInputKind = kind;
                }
            }
            am.EvaluateSimulationStep(deltaSeconds, _wires);
        }
    }

    /// <summary>
    /// Tạm thời tách phần tử đo nối tiếp (Ampe kế I1-I2 hoặc CT P1-P2) khỏi Net dẫn điện để xác định tập tải nằm phía sau phần tử đo.
    /// </summary>
    private (double Amps, CurrentKind Kind) ComputeSeriesBranchCurrentThroughComponent(
        ElectricalComponent seriesDevice,
        Terminal pinA,
        Terminal pinB,
        Dictionary<Terminal, List<GraphEdge>> adj,
        List<(Terminal Terminal, PotentialToken Token)> allSources)
    {
        int netId = pinA.NetId;
        if (netId < 0 || pinB.NetId != netId)
            return (0.0, CurrentKind.None);

        var netSources = allSources.Where(s => s.Terminal.NetId == netId && s.Token.Phase != PhaseType.PE).ToList();
        if (netSources.Count == 0)
            return (0.0, CurrentKind.None);

        // Duyệt BFS từ các chân nguồn trong Net nhưng KHÔNG đi qua cạnh nội bộ của seriesDevice
        var sourceReachable = new HashSet<Terminal>();
        var q = new Queue<Terminal>();
        foreach (var src in netSources)
        {
            if (sourceReachable.Add(src.Terminal))
                q.Enqueue(src.Terminal);
        }

        while (q.Count > 0)
        {
            var u = q.Dequeue();
            foreach (var edge in adj[u])
            {
                if (edge.InternalComponent == seriesDevice) continue;
                if (sourceReachable.Add(edge.To))
                    q.Enqueue(edge.To);
            }
        }

        // Nếu cả 2 đầu đều chạm nguồn (bị đấu song song mạch vòng tắt) hoặc không đầu nào chạm nguồn -> dòng nhánh qua đồng hồ = 0
        bool aOnSource = sourceReachable.Contains(pinA);
        bool bOnSource = sourceReachable.Contains(pinB);
        if (aOnSource == bOnSource)
            return (0.0, CurrentKind.None);

        // Tìm toàn bộ chân nằm ở phía tải (sau đồng hồ) trong cùng Net
        Terminal loadStartPin = aOnSource ? pinB : pinA;
        var loadSideTerminals = new HashSet<Terminal> { loadStartPin };
        q.Enqueue(loadStartPin);
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            foreach (var edge in adj[u])
            {
                if (edge.InternalComponent == seriesDevice) continue;
                if (loadSideTerminals.Add(edge.To))
                    q.Enqueue(edge.To);
            }
        }

        double totalAmps = 0.0;
        CurrentKind detectedKind = netSources[0].Token.CurrentKind;

        // 1. Cộng dòng của Động cơ 3 pha (3 đầu dây & 6 đầu dây) có pha cấp qua nhánh này
        foreach (var m3 in _components.OfType<Motor3PhaseBaseComponent>())
        {
            if (m3.OperatingCurrentAmps <= 0.01) continue;
            bool feedsMotor = m3.Terminals.Any(t => t != m3.Pe && loadSideTerminals.Contains(t));
            if (feedsMotor)
            {
                totalAmps += m3.OperatingCurrentAmps;
                detectedKind = CurrentKind.AC;
            }
        }

        // 2. Cộng dòng của tất cả các tải tiêu thụ khác (Động cơ 1P, Bóng đèn, Đèn báo, Cuộn hút, Cảm biến, Biến tần...)
        foreach (var comp in _components)
        {
            if (comp is Motor3PhaseBaseComponent) continue;
            foreach (var load in comp.GetActiveLoadPaths())
            {
                if (!load.IsActive) continue;
                bool aIn = loadSideTerminals.Contains(load.TerminalA);
                bool bIn = loadSideTerminals.Contains(load.TerminalB);
                if (aIn ^ bIn)
                {
                    totalAmps += load.CurrentAmps;
                    detectedKind = load.CurrentKind;
                }
            }
        }

        return (totalAmps, detectedKind);
    }

    private static void PropagateCtSecondaryLoop(
        CurrentTransformerComponent ct,
        Dictionary<Terminal, List<GraphEdge>> adj)
    {
        var parent = new Dictionary<Terminal, (Terminal From, GraphEdge Edge)>();
        var visited = new HashSet<Terminal> { ct.S1 };
        var q = new Queue<Terminal>();
        q.Enqueue(ct.S1);

        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (u == ct.S2) break;
            foreach (var e in adj[u])
            {
                if (visited.Add(e.To))
                {
                    parent[e.To] = (u, e);
                    q.Enqueue(e.To);
                }
            }
        }

        var cur = ct.S2;
        while (cur != ct.S1 && parent.TryGetValue(cur, out var step))
        {
            if (step.Edge.InternalComponent is AmmeterComponent am)
            {
                am.RawInputAmps += ct.SecondaryCurrentAmps;
                am.RawInputKind = CurrentKind.AC;
            }
            if (step.Edge.Wire is not null)
            {
                step.Edge.Wire.IsEnergized = true;
                step.Edge.Wire.HasCurrentFlow = true;
            }
            cur = step.From;
        }
    }

    private void EvaluateFuseOverloadProtection(
        Dictionary<Terminal, List<GraphEdge>> adj,
        List<(Terminal Terminal, PotentialToken Token)> allSources,
        double deltaSeconds)
    {
        foreach (var fuse in _components.OfType<FuseComponent>())
        {
            if (fuse.IsBlown) continue;

            var (amps, _) = ComputeSeriesBranchCurrentThroughComponent(fuse, fuse.In1, fuse.Out2, adj, allSources);
            fuse.MeasuredCurrentAmps = Math.Round(amps, 2);

            bool wasBlown = fuse.IsBlown;
            fuse.EvaluateSimulationStep(deltaSeconds, _wires);
            if (!wasBlown && fuse.IsBlown && !string.IsNullOrWhiteSpace(fuse.BlownReason))
            {
                ComponentFaultAlert?.Invoke(fuse.BlownReason!);
            }
        }
    }

    private void UpdateVfdOutputCurrents()
    {
        var motors = _components.OfType<Motor3PhaseBaseComponent>().ToList();
        foreach (var vfd in _components.OfType<VfdComponent>())
        {
            if (!vfd.IsRunning)
            {
                vfd.OutputCurrentAmps = 0.0;
                continue;
            }

            int[] outNets = [vfd.U_Out.NetId, vfd.V_Out.NetId, vfd.W_Out.NetId];
            double totalAmps = 0.0;
            foreach (var m in motors)
            {
                if (m.OperatingCurrentAmps <= 0.01) continue;
                bool fedByVfd = m.Terminals.Any(t => t != m.Pe && t.NetId >= 0 && outNets.Contains(t.NetId));
                if (fedByVfd)
                {
                    totalAmps += m.OperatingCurrentAmps;
                }
            }

            vfd.OutputCurrentAmps = Math.Round(totalAmps > 0.01 ? totalAmps : 0.3, 1);
        }
    }

    private void UpdateThermalOverloadRelayCurrents()
    {
        var motors = _components.OfType<Motor3PhaseBaseComponent>().ToList();
        foreach (var relay in _components.OfType<ThermalOverloadRelayComponent>())
        {
            double maxCurrent = 0.0;
            int[] relayNets = [relay.T1Out.NetId, relay.T2Out.NetId, relay.T3Out.NetId, relay.L1In.NetId, relay.L2In.NetId, relay.L3In.NetId];

            foreach (var m in motors)
            {
                if (m.OperatingCurrentAmps <= 0.01) continue;

                bool connected = m.Terminals.Any(t => t != m.Pe && t.NetId >= 0 && relayNets.Contains(t.NetId));
                if (connected)
                {
                    maxCurrent += m.OperatingCurrentAmps;
                }
            }

            relay.MeasuredCurrentAmps = Math.Round(maxCurrent, 1);
        }
    }

    private Dictionary<Terminal, List<GraphEdge>> BuildConductiveGraph()
    {
        var adj = new Dictionary<Terminal, List<GraphEdge>>();
        foreach (var comp in _components)
        {
            foreach (var t in comp.Terminals)
            {
                adj[t] = new List<GraphEdge>();
            }
        }

        foreach (var wire in _wires)
        {
            if (!adj.ContainsKey(wire.TerminalA) || !adj.ContainsKey(wire.TerminalB))
                continue;

            string label = $"{wire.TerminalA.Owner.DisplayName}:{wire.TerminalA.Name} ↔ {wire.TerminalB.Owner.DisplayName}:{wire.TerminalB.Name}";
            adj[wire.TerminalA].Add(new GraphEdge(wire.TerminalB, wire, null, label));
            adj[wire.TerminalB].Add(new GraphEdge(wire.TerminalA, wire, null, label));
        }

        foreach (var comp in _components)
        {
            foreach (var path in comp.GetInternalConductivePaths())
            {
                if (!adj.ContainsKey(path.TerminalA) || !adj.ContainsKey(path.TerminalB))
                    continue;

                adj[path.TerminalA].Add(new GraphEdge(path.TerminalB, null, comp, path.PathLabel));
                adj[path.TerminalB].Add(new GraphEdge(path.TerminalA, null, comp, path.PathLabel));
            }
        }

        return adj;
    }

    private sealed record ConnectedNet(int NetId, List<Terminal> Terminals);

    private static List<ConnectedNet> FindConnectedNetsBfs(Dictionary<Terminal, List<GraphEdge>> adj)
    {
        var nets = new List<ConnectedNet>();
        var queue = new Queue<Terminal>();
        int nextNetId = 0;

        foreach (var startTerm in adj.Keys)
        {
            if (startTerm.NetId >= 0) continue;

            int currentNetId = nextNetId++;
            var list = new List<Terminal>();

            startTerm.NetId = currentNetId;
            queue.Enqueue(startTerm);

            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                list.Add(u);

                foreach (var edge in adj[u])
                {
                    if (edge.To.NetId < 0)
                    {
                        edge.To.NetId = currentNetId;
                        queue.Enqueue(edge.To);
                    }
                }
            }

            nets.Add(new ConnectedNet(currentNetId, list));
        }

        return nets;
    }

    private static bool TryFindConflict(
        List<(Terminal Terminal, PotentialToken Token)> netSources,
        out (Terminal Terminal, PotentialToken Token) srcA,
        out (Terminal Terminal, PotentialToken Token) srcB,
        out string reason)
    {
        for (int i = 0; i < netSources.Count; i++)
        {
            for (int j = i + 1; j < netSources.Count; j++)
            {
                var a = netSources[i];
                var b = netSources[j];

                if (ArePotentialsShorting(a.Token, b.Token, out reason))
                {
                    srcA = a;
                    srcB = b;
                    return true;
                }
            }
        }

        srcA = default;
        srcB = default;
        reason = string.Empty;
        return false;
    }

    private static bool ArePotentialsShorting(PotentialToken a, PotentialToken b, out string reason)
    {
        reason = string.Empty;

        if (a.CurrentKind != b.CurrentKind && a.CurrentKind != CurrentKind.None && b.CurrentKind != CurrentKind.None)
        {
            reason = LocalizationManager.Format("Str.Fault.AcDcMix", a.CurrentKind, b.CurrentKind);
            return true;
        }

        if (a.CurrentKind == CurrentKind.AC && b.CurrentKind == CurrentKind.AC)
        {
            if (a.Phase == b.Phase && Math.Abs(a.Voltage - b.Voltage) < 2.0)
                return false;

            bool aIsLine = a.Phase is PhaseType.L1 or PhaseType.L2 or PhaseType.L3;
            bool bIsLine = b.Phase is PhaseType.L1 or PhaseType.L2 or PhaseType.L3;

            if (aIsLine && bIsLine)
            {
                reason = LocalizationManager.Format("Str.Fault.PhaseToPhase", a.Phase, b.Phase);
                return true;
            }

            if ((aIsLine && b.Phase == PhaseType.N) || (bIsLine && a.Phase == PhaseType.N))
            {
                var linePhase = aIsLine ? a.Phase : b.Phase;
                reason = LocalizationManager.Format("Str.Fault.PhaseToNeutral", linePhase);
                return true;
            }

            if ((aIsLine && b.Phase == PhaseType.PE) || (bIsLine && a.Phase == PhaseType.PE))
            {
                var linePhase = aIsLine ? a.Phase : b.Phase;
                reason = LocalizationManager.Format("Str.Fault.PhaseToEarth", linePhase);
                return true;
            }
        }

        if (a.CurrentKind == CurrentKind.DC && b.CurrentKind == CurrentKind.DC)
        {
            if ((a.Phase == PhaseType.DC_Plus && b.Phase == PhaseType.DC_Minus) ||
                (a.Phase == PhaseType.DC_Minus && b.Phase == PhaseType.DC_Plus))
            {
                reason = LocalizationManager.Get("Str.Fault.DcShort");
                return true;
            }

            if (a.Phase == PhaseType.DC_Plus && b.Phase == PhaseType.DC_Plus && Math.Abs(a.Voltage - b.Voltage) > 0.5)
            {
                reason = LocalizationManager.Format("Str.Fault.DcDiffVolt", a.Voltage, b.Voltage);
                return true;
            }
        }

        return false;
    }

    private void HandleShortCircuit(
        Dictionary<Terminal, List<GraphEdge>> adj,
        List<Terminal> netTerminals,
        (Terminal Terminal, PotentialToken Token) srcA,
        (Terminal Terminal, PotentialToken Token) srcB,
        string reason)
    {
        var parentMap = new Dictionary<Terminal, (Terminal From, GraphEdge Edge)>();
        var visited = new HashSet<Terminal> { srcA.Terminal };
        var queue = new Queue<Terminal>();
        queue.Enqueue(srcA.Terminal);

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (u == srcB.Terminal) break;

            foreach (var edge in adj[u])
            {
                if (visited.Add(edge.To))
                {
                    parentMap[edge.To] = (u, edge);
                    queue.Enqueue(edge.To);
                }
            }
        }

        var pathEdges = new List<(Terminal From, Terminal To, GraphEdge Edge)>();
        var cur = srcB.Terminal;
        while (cur != srcA.Terminal && parentMap.TryGetValue(cur, out var step))
        {
            pathEdges.Add((step.From, cur, step.Edge));
            cur = step.From;
        }
        pathEdges.Reverse();

        var faultedWireIds = new HashSet<string>();
        var breakersToTrip = new HashSet<ICircuitBreaker>();
        var fusesToBlow = new HashSet<FuseComponent>();
        AmmeterComponent? shortedAmmeter = null;
        ITwoWireSensorDevice? shortedTwoWire = null;

        foreach (var step in pathEdges)
        {
            if (step.Edge.Wire is not null)
            {
                step.Edge.Wire.IsShortCircuitPath = true;
                faultedWireIds.Add(step.Edge.Wire.Id);
            }
            if (step.Edge.InternalComponent is ICircuitBreaker cb && cb.IsOn)
            {
                breakersToTrip.Add(cb);
            }
            if (step.Edge.InternalComponent is FuseComponent fuse && !fuse.IsBlown)
            {
                fusesToBlow.Add(fuse);
            }
            if (step.Edge.InternalComponent is AmmeterComponent am)
            {
                shortedAmmeter = am;
            }
            if (step.Edge.InternalComponent is ITwoWireSensorDevice tw && tw.IsTwoWireActiveMode)
            {
                shortedTwoWire = tw;
            }
        }

        if (breakersToTrip.Count == 0 && fusesToBlow.Count == 0)
        {
            foreach (var term in netTerminals)
            {
                if (term.Owner is ICircuitBreaker cb && cb.IsOn)
                {
                    breakersToTrip.Add(cb);
                }
                else if (term.Owner is FuseComponent fuse && !fuse.IsBlown)
                {
                    fusesToBlow.Add(fuse);
                }
            }
        }

        if (shortedTwoWire is not null)
        {
            shortedTwoWire.DamageByNoLoadShort(LocalizationManager.Get("Str.Fault.TwoWireNoLoadShort"));
        }

        var trippedNames = new List<string>();
        var blownFuseNames = new List<string>();
        foreach (var fuse in fusesToBlow)
        {
            fuse.Blow(reason);
            blownFuseNames.Add(fuse.DisplayName);
        }
        foreach (var cb in breakersToTrip)
        {
            cb.TripShortCircuit();
            if (cb is ElectricalComponent comp)
                trippedNames.Add(comp.DisplayName);
        }

        Point faultPoint;
        if (shortedTwoWire is ElectricalComponent twComp)
        {
            faultPoint = new Point(twComp.X + twComp.Width / 2.0, twComp.Y + twComp.Height / 2.0);
        }
        else if (shortedAmmeter is not null)
        {
            faultPoint = new Point(shortedAmmeter.X + shortedAmmeter.Width / 2.0, shortedAmmeter.Y + shortedAmmeter.Height / 2.0);
        }
        else
        {
            faultPoint = new Point(
                (srcA.Terminal.CanvasX + srcB.Terminal.CanvasX) / 2.0,
                (srcA.Terminal.CanvasY + srcB.Terminal.CanvasY) / 2.0);
        }

        string locationDesc = $"{srcA.Terminal.Owner.DisplayName}:{srcA.Terminal.Name} ↔ {srcB.Terminal.Owner.DisplayName}:{srcB.Terminal.Name}";

        var wireSteps = pathEdges.Where(p => p.Edge.Wire is not null).ToList();
        if (wireSteps.Count > 0)
        {
            var focalStep = wireSteps[wireSteps.Count / 2];
            var w = focalStep.Edge.Wire!;
            if (shortedAmmeter is null && shortedTwoWire is null)
            {
                if (w.RoutedPoints.Count >= 2)
                {
                    int midIdx = w.RoutedPoints.Count / 2;
                    Point p1 = w.RoutedPoints[midIdx - 1];
                    Point p2 = w.RoutedPoints[midIdx];
                    faultPoint = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                }
                else
                {
                    faultPoint = new Point(
                        (focalStep.From.CanvasX + focalStep.To.CanvasX) / 2.0,
                        (focalStep.From.CanvasY + focalStep.To.CanvasY) / 2.0);
                }
            }

            locationDesc = LocalizationManager.Format(
                "Str.Fault.LocationWire",
                focalStep.From.Owner.DisplayName,
                focalStep.From.Name,
                focalStep.To.Owner.DisplayName,
                focalStep.To.Name,
                faultPoint.X,
                faultPoint.Y);
        }

        _unprotectedFaultLockout = trippedNames.Count == 0 && blownFuseNames.Count == 0 && shortedTwoWire is null;

        // Cảnh báo chuyên biệt nếu ngắn mạch qua cảm biến 2 dây không có tải hoặc đấu nhầm đồng hồ Ampe song song!
        string headerTitle = shortedTwoWire is not null
            ? LocalizationManager.Get("Str.Fault.TwoWireNoLoadShort")
            : (shortedAmmeter is not null
                ? LocalizationManager.Get("Str.Fault.AmmeterParallelShort")
                : LocalizationManager.Get("Str.Fault.ShortHeader"));

        string actionText;
        if (blownFuseNames.Count > 0 && trippedNames.Count > 0)
        {
            actionText = $"{LocalizationManager.Format("Str.Fault.FuseBlownShort", string.Join(", ", blownFuseNames))} {LocalizationManager.Format("Str.Fault.TrippedMcb", string.Join(", ", trippedNames))}";
        }
        else if (blownFuseNames.Count > 0)
        {
            actionText = LocalizationManager.Format("Str.Fault.FuseBlownShort", string.Join(", ", blownFuseNames));
        }
        else if (trippedNames.Count > 0)
        {
            actionText = LocalizationManager.Format("Str.Fault.TrippedMcb", string.Join(", ", trippedNames));
        }
        else
        {
            actionText = LocalizationManager.Get("Str.Fault.NoMcbLockout");
        }

        ActiveFault = new ShortCircuitFaultInfo
        {
            Title = headerTitle,
            DetailMessage = $"{reason}. {actionText}",
            LocationDescription = locationDesc,
            FaultCanvasPoint = faultPoint,
            FaultedWireIds = faultedWireIds,
            TrippedBreakerNames = trippedNames
        };

        ShortCircuitDetected?.Invoke(ActiveFault);
    }

    private void MarkActiveCurrentFlowWires(
        Dictionary<Terminal, List<GraphEdge>> adj,
        List<ConnectedNet> nets,
        List<(Terminal Terminal, PotentialToken Token)> allSources)
    {
        var activeLoadTerminalsByNet = new Dictionary<int, HashSet<Terminal>>();

        foreach (var comp in _components)
        {
            foreach (var load in comp.GetActiveLoadPaths())
            {
                if (!load.IsActive) continue;

                AddNetEndpoint(activeLoadTerminalsByNet, load.TerminalA);
                AddNetEndpoint(activeLoadTerminalsByNet, load.TerminalB);
            }
        }

        foreach (var net in nets)
        {
            if (!activeLoadTerminalsByNet.TryGetValue(net.NetId, out var requiredEndpoints) || requiredEndpoints.Count == 0)
                continue;

            foreach (var src in allSources)
            {
                if (src.Terminal.NetId == net.NetId && src.Token.Phase != PhaseType.PE)
                {
                    requiredEndpoints.Add(src.Terminal);
                }
            }

            if (requiredEndpoints.Count < 2)
                continue;

            var degree = new Dictionary<Terminal, int>();
            var removed = new HashSet<Terminal>();
            var pruneQueue = new Queue<Terminal>();

            foreach (var t in net.Terminals)
            {
                int deg = adj[t].Count;
                degree[t] = deg;
                if (deg <= 1 && !requiredEndpoints.Contains(t))
                {
                    pruneQueue.Enqueue(t);
                }
            }

            while (pruneQueue.Count > 0)
            {
                var leaf = pruneQueue.Dequeue();
                if (!removed.Add(leaf)) continue;

                foreach (var edge in adj[leaf])
                {
                    if (removed.Contains(edge.To)) continue;
                    degree[edge.To]--;
                    if (degree[edge.To] <= 1 && !requiredEndpoints.Contains(edge.To))
                    {
                        pruneQueue.Enqueue(edge.To);
                    }
                }
            }

            foreach (var t in net.Terminals)
            {
                if (removed.Contains(t)) continue;
                t.HasCurrentFlow = true;

                foreach (var edge in adj[t])
                {
                    if (!removed.Contains(edge.To) && edge.Wire is not null)
                    {
                        edge.Wire.HasCurrentFlow = true;
                    }
                }
            }
        }
    }

    private static void AddNetEndpoint(Dictionary<int, HashSet<Terminal>> dict, Terminal t)
    {
        if (t.NetId < 0) return;
        if (!dict.TryGetValue(t.NetId, out var set))
        {
            set = new HashSet<Terminal>();
            dict[t.NetId] = set;
        }
        set.Add(t);
    }

    private static bool IsHotPhase(PotentialToken token) =>
        token.Phase is PhaseType.L1 or PhaseType.L2 or PhaseType.L3 or PhaseType.DC_Plus;

    private static PotentialToken PickPrimaryDisplayToken(List<PotentialToken> tokens)
    {
        var hot = tokens.FirstOrDefault(IsHotPhase);
        return hot.CurrentKind != CurrentKind.None ? hot : tokens[0];
    }
}
