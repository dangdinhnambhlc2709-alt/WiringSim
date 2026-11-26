using System.Windows;
using System.Windows.Media;
using WiringSim.Models;
using WiringSim.Models.Enums;

namespace WiringSim.UI.Helpers;

public readonly record struct CubicBezierSpan(
    Point P0,
    Point C1,
    Point C2,
    Point P3,
    Point MidPoint);

/// <summary>
/// Bộ định tuyến &amp; dựng hình dây dẫn (WireRouter &amp; Spline Renderer):
/// - Chế độ Vuông góc (Orthogonal): Tự động né vật cản, phân làn song song, bo góc mượt 6px; tôn trọng Waypoints thủ công.
/// - Chế độ Đường cong (Smooth Curve): Áp dụng nội suy spline Catmull-Rom / Cubic Bezier trơn đi qua CHÍNH XÁC
///   điểm đầu, toàn bộ các điểm uốn Waypoints và điểm cuối cho TẤT CẢ dây trên màn hình (kể cả dây đã chỉnh sửa &amp; đang chọn!).
/// </summary>
public static class WireRouter
{
    private const double BaseStubLength = 18.0;
    private const double LaneSpacing = 7.0;
    private const double ObstaclePadding = 10.0;
    private const double CornerRadius = 6.0;

    private readonly record struct HorizontalSegment(double Y, double MinX, double MaxX);
    private readonly record struct VerticalSegment(double X, double MinY, double MaxY);

    public static void RouteAllWires(
        IReadOnlyList<WireModel> wires,
        IReadOnlyList<ElectricalComponent> components)
    {
        var usedHorizontals = new List<HorizontalSegment>();
        var usedVerticals = new List<VerticalSegment>();

        var obstacleRects = components
            .Select(c =>
            {
                Rect vb = c.GetVisualBounds();
                return new Rect(vb.X - ObstaclePadding, vb.Y - ObstaclePadding, vb.Width + ObstaclePadding * 2, vb.Height + ObstaclePadding * 2);
            })
            .ToList();

        foreach (var wire in wires)
        {
            if (wire.HasManualWaypoints)
            {
                wire.SyncRoutedPointsFromWaypoints();
                RegisterSegments(wire.RoutedPoints, usedHorizontals, usedVerticals);
            }
        }

        foreach (var wire in wires)
        {
            if (!wire.HasManualWaypoints)
            {
                wire.RoutedPoints = ComputeSingleWireRoute(wire, obstacleRects, usedHorizontals, usedVerticals);
            }
        }
    }

    /// <summary>
    /// Lấy danh sách điểm neo thực tế để vẽ và chỉnh sửa theo kiểu dây hiện hành:
    /// - Ở chế độ Đường cong: dùng [Start, ...Waypoints..., End] để không bị gãy thành góc vuông khi bấm chọn!
    /// - Ở chế độ Vuông góc: dùng toàn bộ RoutedPoints.
    /// </summary>
    public static List<Point> GetEffectiveAnchorPoints(WireModel wire, WireRoutingStyle style)
    {
        if (style == WireRoutingStyle.Curved)
        {
            var list = new List<Point>(wire.Waypoints.Count + 2)
            {
                new(wire.TerminalA.CanvasX, wire.TerminalA.CanvasY)
            };
            list.AddRange(wire.Waypoints);
            list.Add(new Point(wire.TerminalB.CanvasX, wire.TerminalB.CanvasY));
            return list;
        }

        return wire.RoutedPoints.Count >= 2
            ? wire.RoutedPoints
            : [new Point(wire.TerminalA.CanvasX, wire.TerminalA.CanvasY), new Point(wire.TerminalB.CanvasX, wire.TerminalB.CanvasY)];
    }

    /// <summary>
    /// Nội suy chuỗi đường cong Cubic Bezier trơn (Catmull-Rom Spline) đi qua chính xác mọi điểm neo [P_0, P_1, ..., P_{n-1}].
    /// Đồng thời tính sẵn tọa độ trung điểm thực tế t = 0.5 trên từng nhịp cong để hiển thị nút điều khiển [+].
    /// </summary>
    public static List<CubicBezierSpan> ComputeSmoothCurveSpans(
        IReadOnlyList<Point> rawAnchors,
        TerminalSide startSide,
        TerminalSide endSide,
        int bundleOffsetIndex = 0)
    {
        var spans = new List<CubicBezierSpan>();
        var pts = DeduplicatePoints(rawAnchors);
        if (pts.Count < 2) return spans;

        Vector vStartExit = SideToVector(startSide);
        Vector vEndExit = SideToVector(endSide);

        // Trường hợp chỉ có 2 điểm đầu-cuối (chưa đặt Waypoint): đường cong S mượt thoát theo hướng chân đấu nối
        if (pts.Count == 2)
        {
            Point p0 = pts[0];
            Point p3 = pts[1];
            double dist = (p3 - p0).Length;
            double tangentLen = Math.Clamp(dist * 0.42, 24.0, 150.0);

            Vector perp = new(-(p3.Y - p0.Y), p3.X - p0.X);
            if (perp.Length > 0.1) perp.Normalize();
            double spread = ((bundleOffsetIndex % 5) - 2) * 4.5;

            Point c1 = p0 + vStartExit * tangentLen + perp * spread;
            Point c2 = p3 + vEndExit * tangentLen + perp * spread;
            Point mid = EvaluateCubicBezier(p0, c1, c2, p3, 0.5);
            spans.Add(new CubicBezierSpan(p0, c1, c2, p3, mid));
            return spans;
        }

        // Trường hợp có >= 3 điểm (có Waypoints): Nội suy Catmull-Rom Spline đi qua CHÍNH XÁC từng điểm uốn!
        int n = pts.Count;
        double startStub = Math.Clamp((pts[1] - pts[0]).Length * 0.5, 18.0, 70.0);
        double endStub = Math.Clamp((pts[n - 1] - pts[n - 2]).Length * 0.5, 18.0, 70.0);

        // Điểm ảo mở rộng hai đầu theo hướng thoát chân để tiếp tuyến đầu/cuối tự nhiên
        Point pMinus1 = pts[0] - vStartExit * startStub;
        Point pPlusN = pts[n - 1] - vEndExit * endStub;

        for (int i = 0; i < n - 1; i++)
        {
            Point pPrev = i == 0 ? pMinus1 : pts[i - 1];
            Point pCurr = pts[i];
            Point pNext = pts[i + 1];
            Point pNext2 = i + 2 < n ? pts[i + 2] : pPlusN;

            double segLen = (pNext - pCurr).Length;
            Vector t1 = (pNext - pPrev) / 6.0;
            Vector t2 = (pNext2 - pCurr) / 6.0;

            // Giới hạn độ dài tay đòn tiếp tuyến tránh vọt lố khi 2 Waypoints quá gần nhau
            double maxHandle = segLen * 0.45;
            if (t1.Length > maxHandle && t1.Length > 0.001) t1 = t1 / t1.Length * maxHandle;
            if (t2.Length > maxHandle && t2.Length > 0.001) t2 = t2 / t2.Length * maxHandle;

            Point c1 = pCurr + t1;
            Point c2 = pNext - t2;
            Point mid = EvaluateCubicBezier(pCurr, c1, c2, pNext, 0.5);
            spans.Add(new CubicBezierSpan(pCurr, c1, c2, pNext, mid));
        }

        return spans;
    }

    public static StreamGeometry BuildWireGeometry(
        WireModel wire,
        WireRoutingStyle style,
        int bundleOffsetIndex = 0)
    {
        if (style == WireRoutingStyle.Curved)
        {
            var anchors = GetEffectiveAnchorPoints(wire, WireRoutingStyle.Curved);
            var spans = ComputeSmoothCurveSpans(anchors, wire.TerminalA.EffectiveSide, wire.TerminalB.EffectiveSide, bundleOffsetIndex);
            return BuildGeometryFromBezierSpans(spans);
        }

        return BuildOrthogonalGeometry(wire.RoutedPoints);
    }

    public static StreamGeometry BuildPreviewGeometry(
        IReadOnlyList<Point> previewPoints,
        TerminalSide startSide,
        TerminalSide endSide,
        WireRoutingStyle style)
    {
        if (style == WireRoutingStyle.Curved)
        {
            var spans = ComputeSmoothCurveSpans(previewPoints, startSide, endSide, 0);
            return BuildGeometryFromBezierSpans(spans);
        }

        return BuildOrthogonalGeometry(previewPoints);
    }

    private static StreamGeometry BuildGeometryFromBezierSpans(IReadOnlyList<CubicBezierSpan> spans)
    {
        var geometry = new StreamGeometry();
        if (spans.Count == 0) return geometry;

        using var ctx = geometry.Open();
        ctx.BeginFigure(spans[0].P0, isFilled: false, isClosed: false);
        foreach (var sp in spans)
        {
            ctx.BezierTo(sp.C1, sp.C2, sp.P3, isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry BuildOrthogonalGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count < 2) return geometry;

        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], isFilled: false, isClosed: false);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Point prev = points[i - 1];
            Point curr = points[i];
            Point next = points[i + 1];

            Vector v1 = curr - prev;
            Vector v2 = next - curr;
            double len1 = v1.Length;
            double len2 = v2.Length;

            if (len1 > 3.0 && len2 > 3.0)
            {
                double r = Math.Min(CornerRadius, Math.Min(len1, len2) * 0.38);
                v1.Normalize();
                v2.Normalize();

                Point cornerEnter = curr - v1 * r;
                Point cornerExit = curr + v2 * r;

                ctx.LineTo(cornerEnter, isStroked: true, isSmoothJoin: true);
                ctx.QuadraticBezierTo(curr, cornerExit, isStroked: true, isSmoothJoin: true);
            }
            else
            {
                ctx.LineTo(curr, isStroked: true, isSmoothJoin: true);
            }
        }

        ctx.LineTo(points[^1], isStroked: true, isSmoothJoin: true);
        geometry.Freeze();
        return geometry;
    }

    public static int FindNearestSpanIndex(
        Point clickPt,
        WireModel wire,
        WireRoutingStyle style,
        out Point bestPointOnWire)
    {
        bestPointOnWire = clickPt;
        if (style == WireRoutingStyle.Curved)
        {
            var anchors = GetEffectiveAnchorPoints(wire, WireRoutingStyle.Curved);
            var spans = ComputeSmoothCurveSpans(anchors, wire.TerminalA.EffectiveSide, wire.TerminalB.EffectiveSide);
            int bestSpan = 0;
            double minDist = double.MaxValue;

            for (int s = 0; s < spans.Count; s++)
            {
                var sp = spans[s];
                for (int step = 0; step <= 16; step++)
                {
                    double t = step / 16.0;
                    Point sample = EvaluateCubicBezier(sp.P0, sp.C1, sp.C2, sp.P3, t);
                    double d = (clickPt - sample).Length;
                    if (d < minDist)
                    {
                        minDist = d;
                        bestSpan = s;
                        bestPointOnWire = sample;
                    }
                }
            }
            return bestSpan;
        }

        return FindNearestSegmentIndex(clickPt, wire.RoutedPoints, out bestPointOnWire);
    }

    public static int FindNearestSegmentIndex(Point p, IReadOnlyList<Point> poly, out Point projected)
    {
        projected = p;
        if (poly.Count < 2) return 0;

        int bestIdx = 0;
        double bestDist = double.MaxValue;

        for (int i = 0; i < poly.Count - 1; i++)
        {
            Point a = poly[i];
            Point b = poly[i + 1];
            Vector ab = b - a;
            double lenSq = ab.LengthSquared;
            Point proj = lenSq < 0.0001
                ? a
                : a + ab * Math.Clamp(((p - a) * ab) / lenSq, 0.0, 1.0);

            double d = (p - proj).Length;
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
                projected = proj;
            }
        }

        return bestIdx;
    }

    private static Point EvaluateCubicBezier(Point p0, Point c1, Point c2, Point p3, double t)
    {
        double u = 1.0 - t;
        double b0 = u * u * u;
        double b1 = 3.0 * u * u * t;
        double b2 = 3.0 * u * t * t;
        double b3 = t * t * t;
        return new Point(
            b0 * p0.X + b1 * c1.X + b2 * c2.X + b3 * p3.X,
            b0 * p0.Y + b1 * c1.Y + b2 * c2.Y + b3 * p3.Y);
    }

    private static List<Point> DeduplicatePoints(IReadOnlyList<Point> raw)
    {
        var res = new List<Point>(raw.Count);
        foreach (var pt in raw)
        {
            if (res.Count == 0 || (pt - res[^1]).Length > 0.5)
                res.Add(pt);
        }
        return res;
    }

    private static List<Point> ComputeSingleWireRoute(
        WireModel wire,
        List<Rect> obstacles,
        List<HorizontalSegment> usedH,
        List<VerticalSegment> usedV)
    {
        var tA = wire.TerminalA;
        var tB = wire.TerminalB;
        Point pA = new(tA.CanvasX, tA.CanvasY);
        Point pB = new(tB.CanvasX, tB.CanvasY);

        TerminalSide sideA = tA.EffectiveSide;
        TerminalSide sideB = tB.EffectiveSide;
        Vector dirA = SideToVector(sideA);
        Vector dirB = SideToVector(sideB);

        if (Math.Abs(pA.X - pB.X) < 0.5 &&
            ((sideA == TerminalSide.Bottom && sideB == TerminalSide.Top && pB.Y > pA.Y) ||
             (sideA == TerminalSide.Top && sideB == TerminalSide.Bottom && pA.Y > pB.Y)))
        {
            double minY = Math.Min(pA.Y, pB.Y) + 4;
            double maxY = Math.Max(pA.Y, pB.Y) - 4;
            if (!IntersectsVerticalObstacle(pA.X, minY, maxY, obstacles, tA.Owner, tB.Owner) &&
                !ConflictsVertical(pA.X, minY, maxY, usedV))
            {
                usedV.Add(new VerticalSegment(pA.X, minY, maxY));
                return [pA, pB];
            }
        }

        if (sideA == sideB && Math.Abs(pA.Y - pB.Y) < 0.5 &&
            sideA is TerminalSide.Top or TerminalSide.Bottom)
        {
            double sign = sideA == TerminalSide.Top ? -1.0 : 1.0;
            double candidateY = pA.Y + sign * BaseStubLength;
            candidateY = FindFreeHorizontalLane(candidateY, Math.Min(pA.X, pB.X), Math.Max(pA.X, pB.X), usedH, obstacles, sign);

            usedH.Add(new HorizontalSegment(candidateY, Math.Min(pA.X, pB.X), Math.Max(pA.X, pB.X)));
            return [pA, new Point(pA.X, candidateY), new Point(pB.X, candidateY), pB];
        }

        Point stubA = pA + dirA * BaseStubLength;
        Point stubB = pB + dirB * BaseStubLength;

        double midY = (stubA.Y + stubB.Y) / 2.0;
        double minX = Math.Min(stubA.X, stubB.X);
        double maxX = Math.Max(stubA.X, stubB.X);

        bool horizontalBlocked = IntersectsHorizontalObstacle(midY, minX, maxX, obstacles);
        bool verticalA_Blocked = IntersectsVerticalObstacle(stubA.X, Math.Min(stubA.Y, midY), Math.Max(stubA.Y, midY), obstacles, tA.Owner, tB.Owner);
        bool verticalB_Blocked = IntersectsVerticalObstacle(stubB.X, Math.Min(midY, stubB.Y), Math.Max(midY, stubB.Y), obstacles, tA.Owner, tB.Owner);

        if (!horizontalBlocked && !verticalA_Blocked && !verticalB_Blocked)
        {
            double laneY = FindFreeHorizontalLane(midY, minX, maxX, usedH, obstacles, stubB.Y >= stubA.Y ? 1.0 : -1.0);
            double laneXA = stubA.X;
            double laneXB = stubB.X;

            if (Math.Abs(laneY - stubA.Y) > 28 && ConflictsVertical(laneXA, Math.Min(stubA.Y, laneY), Math.Max(stubA.Y, laneY), usedV))
                laneXA = FindFreeVerticalLane(stubA.X, Math.Min(stubA.Y, laneY), Math.Max(stubA.Y, laneY), usedV, obstacles);
            if (Math.Abs(stubB.Y - laneY) > 28 && ConflictsVertical(laneXB, Math.Min(laneY, stubB.Y), Math.Max(laneY, stubB.Y), usedV))
                laneXB = FindFreeVerticalLane(stubB.X, Math.Min(laneY, stubB.Y), Math.Max(laneY, stubB.Y), usedV, obstacles);

            var raw = new List<Point> { pA };
            if (Math.Abs(laneXA - pA.X) > 0.5)
            {
                raw.Add(stubA);
                raw.Add(new Point(laneXA, stubA.Y));
            }
            raw.Add(new Point(laneXA, laneY));
            raw.Add(new Point(laneXB, laneY));
            if (Math.Abs(laneXB - pB.X) > 0.5)
            {
                raw.Add(new Point(laneXB, stubB.Y));
                raw.Add(stubB);
            }
            raw.Add(pB);

            RegisterSegments(raw, usedH, usedV);
            return SimplifyCollinear(raw);
        }

        double candidateSideY1 = FindClearHorizontalCorridor(stubA.Y, minX, maxX, obstacles);
        double laneSideY1 = FindFreeHorizontalLane(candidateSideY1, minX, maxX, usedH, obstacles, 1.0);

        double corridorX = FindClearVerticalCorridor((stubA.X + stubB.X) / 2.0, Math.Min(laneSideY1, stubB.Y), Math.Max(laneSideY1, stubB.Y), obstacles);
        corridorX = FindFreeVerticalLane(corridorX, Math.Min(laneSideY1, stubB.Y), Math.Max(laneSideY1, stubB.Y), usedV, obstacles);

        double laneSideY2 = FindFreeHorizontalLane(stubB.Y, Math.Min(corridorX, stubB.X), Math.Max(corridorX, stubB.X), usedH, obstacles, dirB.Y >= 0 ? 1.0 : -1.0);

        var detour = SimplifyCollinear(
        [
            pA,
            new Point(pA.X, laneSideY1),
            new Point(corridorX, laneSideY1),
            new Point(corridorX, laneSideY2),
            new Point(pB.X, laneSideY2),
            pB
        ]);

        RegisterSegments(detour, usedH, usedV);
        return detour;
    }

    private static Vector SideToVector(TerminalSide side) => side switch
    {
        TerminalSide.Top => new Vector(0, -1),
        TerminalSide.Bottom => new Vector(0, 1),
        TerminalSide.Left => new Vector(-1, 0),
        TerminalSide.Right => new Vector(1, 0),
        _ => new Vector(0, 1)
    };

    private static double FindFreeHorizontalLane(double preferredY, double minX, double maxX, List<HorizontalSegment> usedH, List<Rect> obstacles, double preferredSign)
    {
        if (maxX - minX < 1.0) return preferredY;
        for (int step = 0; step <= 14; step++)
        {
            int[] signs = step == 0 ? [0] : [(int)preferredSign, -(int)preferredSign];
            foreach (int s in signs)
            {
                double y = preferredY + s * step * LaneSpacing;
                if (!ConflictsHorizontal(y, minX, maxX, usedH) && (step == 0 || !IntersectsHorizontalObstacle(y, minX, maxX, obstacles)))
                    return y;
            }
        }
        return preferredY;
    }

    private static double FindFreeVerticalLane(double preferredX, double minY, double maxY, List<VerticalSegment> usedV, List<Rect> obstacles)
    {
        if (maxY - minY < 1.0) return preferredX;
        for (int step = 0; step <= 14; step++)
        {
            int[] signs = step == 0 ? [0] : [1, -1];
            foreach (int s in signs)
            {
                double x = preferredX + s * step * LaneSpacing;
                if (!ConflictsVertical(x, minY, maxY, usedV) && (step == 0 || !IntersectsVerticalObstacle(x, minY, maxY, obstacles, null, null)))
                    return x;
            }
        }
        return preferredX;
    }

    private static double FindClearHorizontalCorridor(double startY, double minX, double maxX, List<Rect> obstacles)
    {
        for (int step = 0; step <= 25; step++)
        {
            foreach (int sign in step == 0 ? new[] { 0 } : new[] { 1, -1 })
            {
                double y = startY + sign * step * 12.0;
                if (!IntersectsHorizontalObstacle(y, minX, maxX, obstacles)) return y;
            }
        }
        return startY;
    }

    private static double FindClearVerticalCorridor(double startX, double minY, double maxY, List<Rect> obstacles)
    {
        for (int step = 0; step <= 35; step++)
        {
            foreach (int sign in step == 0 ? new[] { 0 } : new[] { -1, 1 })
            {
                double x = startX + sign * step * 14.0;
                if (!IntersectsVerticalObstacle(x, minY, maxY, obstacles, null, null)) return x;
            }
        }
        return startX;
    }

    private static bool ConflictsHorizontal(double y, double minX, double maxX, List<HorizontalSegment> usedH) =>
        usedH.Any(seg => Math.Abs(seg.Y - y) < LaneSpacing - 0.8 && Math.Max(minX, seg.MinX) < Math.Min(maxX, seg.MaxX) - 1.0);

    private static bool ConflictsVertical(double x, double minY, double maxY, List<VerticalSegment> usedV) =>
        usedV.Any(seg => Math.Abs(seg.X - x) < LaneSpacing - 0.8 && Math.Max(minY, seg.MinY) < Math.Min(maxY, seg.MaxY) - 1.0);

    private static bool IntersectsHorizontalObstacle(double y, double minX, double maxX, List<Rect> obstacles) =>
        obstacles.Any(r => y > r.Top + 2 && y < r.Bottom - 2 && maxX > r.Left + 2 && minX < r.Right - 2);

    private static bool IntersectsVerticalObstacle(double x, double minY, double maxY, List<Rect> obstacles, ElectricalComponent? ignoreA, ElectricalComponent? ignoreB)
    {
        Rect? rectA = ignoreA?.GetVisualBounds();
        Rect? rectB = ignoreB?.GetVisualBounds();
        foreach (var r in obstacles)
        {
            if (rectA.HasValue && Math.Abs(r.Left - (rectA.Value.X - ObstaclePadding)) < 0.5 && Math.Abs(r.Top - (rectA.Value.Y - ObstaclePadding)) < 0.5) continue;
            if (rectB.HasValue && Math.Abs(r.Left - (rectB.Value.X - ObstaclePadding)) < 0.5 && Math.Abs(r.Top - (rectB.Value.Y - ObstaclePadding)) < 0.5) continue;
            if (x > r.Left + 2 && x < r.Right - 2 && maxY > r.Top + 2 && minY < r.Bottom - 2) return true;
        }
        return false;
    }

    private static void RegisterSegments(List<Point> pts, List<HorizontalSegment> usedH, List<VerticalSegment> usedV)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Point a = pts[i];
            Point b = pts[i + 1];
            if (Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.X - b.X) > 2.0)
                usedH.Add(new HorizontalSegment(a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X)));
            else if (Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 2.0)
                usedV.Add(new VerticalSegment(a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
        }
    }

    public static List<Point> SimplifyCollinear(List<Point> pts)
    {
        if (pts.Count <= 2) return pts;
        var clean = new List<Point> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            if ((pts[i] - clean[^1]).Length < 0.5) continue;
            if (clean.Count >= 2)
            {
                Point p0 = clean[^2];
                Point p1 = clean[^1];
                Point p2 = pts[i];
                bool sameX = Math.Abs(p0.X - p1.X) < 0.5 && Math.Abs(p1.X - p2.X) < 0.5;
                bool sameY = Math.Abs(p0.Y - p1.Y) < 0.5 && Math.Abs(p1.Y - p2.Y) < 0.5;
                if (sameX || sameY) { clean[^1] = p2; continue; }
            }
            clean.Add(pts[i]);
        }
        return clean;
    }
}
