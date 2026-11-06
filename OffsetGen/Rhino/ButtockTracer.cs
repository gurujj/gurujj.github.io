using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace OffsetGenPlugin
{
    // ---------------------------------------------------------------
    // 버톡 추적용 조밀 단면 (한 번만 만들어 모든 버톡이 공유)
    // ---------------------------------------------------------------
    public class TraceContext
    {
        public List<Brep> Breps;
        public BoundingBox Bbox;
        public double Tol;
        public double HullLen, HullH;
        public int N;
        public List<double> StaX = new List<double>();
        public List<List<Curve>> StaCurves = new List<List<Curve>>();
        public List<double> WlZ = new List<double>();
        public List<List<Curve>> WlCurves = new List<List<Curve>>();

        // x, z 방향으로 각각 n개의 조밀한 단면을 만듦 (양 끝의 접하는 단면을 피하려고 반 칸 안쪽에서 시작)
        public static TraceContext Build(List<Brep> breps, BoundingBox bbox, double tol, int n)
        {
            var c = new TraceContext { Breps = breps, Bbox = bbox, Tol = tol, N = n };
            c.HullLen = bbox.Max.X - bbox.Min.X;
            c.HullH = bbox.Max.Z - bbox.Min.Z;
            if (c.HullLen <= 0 || c.HullH <= 0) return c;

            for (int i = 0; i < n; i++)
            {
                double x = bbox.Min.X + c.HullLen * (i + 0.5) / n;
                var sx = CurveUtil.SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
                if (sx.Count > 0) { c.StaX.Add(x); c.StaCurves.Add(sx); }

                double z = bbox.Min.Z + c.HullH * (i + 0.5) / n;
                var sz = CurveUtil.SectionCurves(breps, new Plane(new Point3d(0, 0, z), Vector3d.ZAxis), tol);
                if (sz.Count > 0) { c.WlZ.Add(z); c.WlCurves.Add(sz); }
            }
            return c;
        }
    }

    // ---------------------------------------------------------------
    // 버톡 추적 결과
    // ---------------------------------------------------------------
    public class ButtockTraceResult
    {
        public List<List<Point3d>> Lines = new List<List<Point3d>>();   // x-z 폴리라인 (선미 -> 선수)
        public List<Curve> Curves;          // 정확한 교선 커브 (라이노 표시용). 아니면 null
        public string Method = "none";      // "exact" | "cloud" | "sampled" | "none"
        public string Note = "";
        public int ChecksTotal;             // 독립 점(표 값 + 조밀 단면 교차점)과 대조한 수
        public int ChecksOk;                // 그중 추적 선 위에 놓인 수
        public double ZMin = double.MaxValue, ZMax = double.MinValue;   // 추적된 선의 z 범위
        public double Ratio { get { return ChecksTotal == 0 ? 1.0 : (double)ChecksOk / ChecksTotal; } }

        public void ComputeRange()
        {
            ZMin = double.MaxValue; ZMax = double.MinValue;
            foreach (var l in Lines) foreach (var p in l) { if (p.Z < ZMin) ZMin = p.Z; if (p.Z > ZMax) ZMax = p.Z; }
        }
    }

    // ---------------------------------------------------------------
    // 버톡 라인 추적 (3단계, 앞 단계가 검증을 통과하면 거기서 끝)
    //  1) 정확 교선: 곡면과 평면 y = b 의 교선을 직접 구함 (스테이션 간격과 무관)
    //  2) 점구름 복원: 조밀한 스테이션 단면의 "모든" 교차점 + 워터라인 단면 교차점을 모아 순서를 복원
    //     (벌브 선수 끝처럼 x에 대해 z가 수직이 되는 곳, 윗면이 되돌아오는 곳도 촘촘히 잡힘)
    //  3) 최저 z 샘플링: 마지막 수단 (바닥 쪽 최저점만 잇기 때문에 윗부분이 없음)
    //  검증: 오프셋 표의 버톡 높이 값과, 조밀 단면 교차점이 추적 선 위에 놓이는지 확인
    // ---------------------------------------------------------------
    public static class ButtockTracer
    {
        const double AngleRad = 0.0087266;   // 0.5도
        const double AcceptRatio = 0.95;

        public static ButtockTraceResult Trace(TraceContext ctx, double b, double ySign,
                                               IList<double> stationX, IList<double?> tableZ, bool smoothSampled)
        {
            double yPlane = b * ySign;
            double checkTol = Math.Max(ctx.Tol * 10.0, ctx.HullLen * 1e-4);

            var cloud = CollectCloud(ctx, yPlane);
            var checkPts = new List<P2>();
            int stride = Math.Max(1, cloud.Count / 2000);
            for (int i = 0; i < cloud.Count; i += stride) checkPts.Add(cloud[i]);
            if (stationX != null && tableZ != null)
                for (int s = 0; s < stationX.Count && s < tableZ.Count; s++)
                    if (tableZ[s].HasValue) checkPts.Add(new P2(stationX[s], tableZ[s].Value));

            // 1) 정확 교선
            ButtockTraceResult exact = TraceExact(ctx, yPlane);
            Validate(exact, checkPts, checkTol);
            if (exact.Lines.Count > 0 && exact.Ratio >= AcceptRatio) { exact.ComputeRange(); return exact; }

            string exactNote = exact.Lines.Count == 0 ? "정확 교선 없음"
                : string.Format("정확 교선 점 일치 {0}/{1}", exact.ChecksOk, exact.ChecksTotal);

            // 2) 점구름 복원
            ButtockTraceResult cl = TraceCloud(ctx, cloud, yPlane);
            Validate(cl, checkPts, checkTol);
            if (cl.Lines.Count > 0)
            {
                cl.Note = exactNote + " -> 점구름 복원" + (cl.Note.Length > 0 ? "; " + cl.Note : "");
                cl.ComputeRange();
                return cl;
            }

            // 3) 최저 z 샘플링
            ButtockTraceResult sm = TraceSampled(ctx, yPlane, smoothSampled);
            Validate(sm, checkPts, checkTol);
            sm.Note = exactNote + ", 점구름 실패 -> 최저 z 샘플링 (윗부분 없음)";
            sm.ComputeRange();
            return sm;
        }

        // ---------- 점구름 수집: 스테이션 단면의 모든 교차점 + 워터라인 단면의 교차점 ----------
        public static List<P2> CollectCloud(TraceContext ctx, double yPlane)
        {
            var pts = new List<P2>();
            double tol = ctx.Tol;
            double zLo = ctx.Bbox.Min.Z - ctx.HullH - 1.0, zHi = ctx.Bbox.Max.Z + ctx.HullH + 1.0;
            double xLo = ctx.Bbox.Min.X - ctx.HullLen - 1.0, xHi = ctx.Bbox.Max.X + ctx.HullLen + 1.0;

            for (int i = 0; i < ctx.StaX.Count; i++)
            {
                double x = ctx.StaX[i];
                var vline = new Line(new Point3d(x, yPlane, zLo), new Point3d(x, yPlane, zHi));
                foreach (var c in ctx.StaCurves[i])
                {
                    var ev = Rhino.Geometry.Intersect.Intersection.CurveLine(c, vline, tol, tol);
                    if (ev == null) continue;
                    foreach (var e in ev)
                    {
                        pts.Add(new P2(x, e.PointA.Z));
                        if (e.IsOverlap) pts.Add(new P2(x, e.PointA2.Z));
                    }
                }
            }
            for (int i = 0; i < ctx.WlZ.Count; i++)
            {
                double z = ctx.WlZ[i];
                var hline = new Line(new Point3d(xLo, yPlane, z), new Point3d(xHi, yPlane, z));
                foreach (var c in ctx.WlCurves[i])
                {
                    var ev = Rhino.Geometry.Intersect.Intersection.CurveLine(c, hline, tol, tol);
                    if (ev == null) continue;
                    foreach (var e in ev)
                    {
                        pts.Add(new P2(e.PointA.X, z));
                        if (e.IsOverlap) pts.Add(new P2(e.PointA2.X, z));
                    }
                }
            }
            return pts;
        }

        // ---------- 1) 정확 교선 ----------
        static ButtockTraceResult TraceExact(TraceContext ctx, double yPlane)
        {
            double tol = ctx.Tol;
            var res = new ButtockTraceResult { Method = "exact" };
            var plane = new Plane(new Point3d(0, yPlane, 0), Vector3d.YAxis);
            var curves = CurveUtil.SectionCurves(ctx.Breps, plane, tol);
            if (curves.Count == 0) return res;

            Curve[] joined = Curve.JoinCurves(curves, Math.Max(tol * 2.0, 1e-9));
            var use = new List<Curve>();
            if (joined != null && joined.Length > 0) use.AddRange(joined); else use.AddRange(curves);

            var pieces = new List<List<P3>>();
            bool anyClosed = false;
            foreach (var c in use)
            {
                if (c.IsClosed) anyClosed = true;
                var pts = CurveUtil.CurveToPoints(c, tol, AngleRad);
                if (pts.Count < 2) continue;
                var piece = new List<P3>();
                foreach (var p in pts) piece.Add(new P3(p.X, yPlane, p.Z));
                pieces.Add(piece);
            }

            double maxGap;
            double gapTol = Math.Max(tol * 20.0, ctx.HullLen * 5e-5);
            var chained = PolyTools.Chain(pieces, gapTol, out maxGap);

            foreach (var piece in chained)
            {
                var clean = PolyTools.RemoveDuplicates(piece, tol * 0.1);
                if (clean.Count < 2 || PolyTools.Length(clean) < tol * 5.0) continue;
                PolyTools.OrientAftToFwd(clean);
                var line = new List<Point3d>();
                foreach (var p in clean) line.Add(new Point3d(p.X, p.Y, p.Z));
                res.Lines.Add(line);
            }

            if (res.Lines.Count > 0)
            {
                bool bridged = maxGap > tol * 2.0;
                res.Curves = bridged ? null : use;
                if (bridged) res.Note = string.Format("교선 틈 {0:0.####} 를 이어 붙임", maxGap);
                if (anyClosed) res.Note += (res.Note.Length > 0 ? "; " : "") + "닫힌 교선 있음 (캡/갑판 면이 선택에 포함됐는지 확인)";
            }
            return res;
        }

        // ---------- 2) 점구름 복원 ----------
        static ButtockTraceResult TraceCloud(TraceContext ctx, List<P2> cloud, double yPlane)
        {
            var res = new ButtockTraceResult { Method = "cloud" };
            if (cloud.Count < 8) return res;
            double dx = ctx.HullLen / ctx.N, dz = ctx.HullH / ctx.N;
            double eps = Math.Max(ctx.Tol * 2.0, 0.2 * Math.Min(dx, dz));
            double link = 2.4 * Math.Max(dx, dz);

            var chains = CloudTracer.Order(cloud, eps, link, 8);
            foreach (var ch in chains)
            {
                var line = new List<Point3d>();
                foreach (var p in ch) line.Add(new Point3d(p.X, yPlane, p.Y));
                res.Lines.Add(line);
            }
            return res;
        }

        // ---------- 3) 최저 z 샘플링 (마지막 수단) ----------
        static ButtockTraceResult TraceSampled(TraceContext ctx, double yPlane, bool smooth)
        {
            var res = new ButtockTraceResult { Method = "sampled" };
            double tol = ctx.Tol;
            if (ctx.HullLen <= 0 || ctx.HullH <= 0) return res;
            double zLo = ctx.Bbox.Min.Z - ctx.HullH - 1.0, zHi = ctx.Bbox.Max.Z + ctx.HullH + 1.0;

            AdaptiveSampler.Eval f = delegate (double x)
            {
                var sec = CurveUtil.SectionCurves(ctx.Breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
                if (sec.Count == 0) return null;
                var vline = new Line(new Point3d(x, yPlane, zLo), new Point3d(x, yPlane, zHi));
                bool found = false; double zMin = 0.0;
                foreach (var c in sec)
                {
                    var events = Rhino.Geometry.Intersect.Intersection.CurveLine(c, vline, tol, tol);
                    if (events == null) continue;
                    foreach (var ev in events)
                    {
                        if (!found || ev.PointA.Z < zMin) { zMin = ev.PointA.Z; found = true; }
                        if (ev.IsOverlap && ev.PointA2.Z < zMin) zMin = ev.PointA2.Z;
                    }
                }
                return found ? (double?)zMin : null;
            };

            var pieces = AdaptiveSampler.Sample(f, ctx.Bbox.Min.X, ctx.Bbox.Max.X, 300,
                                                ctx.HullH * 0.01, Math.Max(tol, ctx.HullLen * 1e-4), 2500);
            foreach (var pc in pieces)
            {
                var line = new List<Point3d>();
                foreach (var p in pc) line.Add(new Point3d(p.X, yPlane, p.Y));
                if (smooth) CurveUtil.SmoothZ(line, 5);
                res.Lines.Add(line);
            }
            return res;
        }

        // ---------- 독립 점들이 추적 선 위에 있는지 대조 ----------
        static void Validate(ButtockTraceResult r, List<P2> checkPts, double checkTol)
        {
            r.ChecksTotal = 0; r.ChecksOk = 0;
            if (r.Lines.Count == 0) return;
            var polys = new List<List<P3>>();
            foreach (var l in r.Lines)
            {
                var p = new List<P3>();
                foreach (var q in l) p.Add(new P3(q.X, q.Y, q.Z));
                polys.Add(p);
            }
            foreach (var cp in checkPts)
            {
                r.ChecksTotal++;
                if (PolyTools.DistanceXZ(polys, cp.X, cp.Y) <= checkTol) r.ChecksOk++;
            }
        }
    }
}
