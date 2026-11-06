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
    // 버톡 추적 결과
    // ---------------------------------------------------------------
    public class ButtockTraceResult
    {
        public List<List<Point3d>> Lines = new List<List<Point3d>>();   // x-z 폴리라인 (선미 -> 선수)
        public List<Curve> Curves;          // 정확한 교선 커브 (라이노 표시용). 대체 경로면 null
        public string Method = "none";      // "exact" | "sampled" | "none"
        public string Note = "";
        public int ChecksTotal;             // 오프셋 표 값과 대조한 점 수
        public int ChecksOk;                // 그중 추적 선 위에 놓인 점 수
        public double Ratio { get { return ChecksTotal == 0 ? 1.0 : (double)ChecksOk / ChecksTotal; } }
    }

    // ---------------------------------------------------------------
    // 버톡 라인 추적
    //  1) 선체 곡면과 평면 y = b 의 교선을 직접 구함 (스테이션 간격에 의존하지 않음 -> 벌브처럼
    //     x에 대해 z가 급변하거나 수직이 되는 곳, 윗면이 되돌아오는 곳도 놓치지 않음)
    //  2) 오프셋 표의 버톡 높이 값(스테이션 단면에서 따로 계산)과 대조해 선이 실제로 그 점을 지나는지 검증
    //  3) 교선이 비었거나 검증에 실패하면 적응 분할 샘플링(급변 구간 자동 세분)으로 대체하고 더 잘 맞는 쪽을 채택
    // ---------------------------------------------------------------
    public static class ButtockTracer
    {
        const double AngleRad = 0.0087266;   // 0.5도: 곡률이 큰 곳(벌브)은 점이 촘촘해짐
        const double AcceptRatio = 0.9;      // 표 값과 90% 이상 일치하면 정확 교선을 채택

        public static ButtockTraceResult Trace(List<Brep> breps, double b, double ySign, double tol,
                                               BoundingBox bbox, IList<double> stationX, IList<double?> tableZ,
                                               bool smoothSampled)
        {
            double yPlane = b * ySign;
            double hullLen = bbox.Max.X - bbox.Min.X;
            double checkTol = Math.Max(tol * 10.0, hullLen * 1e-4);

            // 1) 정확 교선
            ButtockTraceResult exact = TraceExact(breps, yPlane, tol, hullLen);
            Validate(exact, stationX, tableZ, checkTol);
            if (exact.Lines.Count > 0 && exact.Ratio >= AcceptRatio) return exact;

            // 2) 대체: 적응 분할 샘플링
            ButtockTraceResult sampled = TraceSampled(breps, yPlane, tol, bbox, smoothSampled);
            Validate(sampled, stationX, tableZ, checkTol);

            if (exact.Lines.Count == 0 && sampled.Lines.Count == 0)
            {
                exact.Note = "교선이 없음 (선체 폭 밖이거나 곡면과 만나지 않음)";
                return exact;
            }
            if (exact.Lines.Count > 0 && exact.Ratio >= sampled.Ratio)
            {
                exact.Note += (exact.Note.Length > 0 ? "; " : "") +
                              string.Format("표 값과 일치 {0}/{1} (대체 방식도 비교했으나 정확 교선 채택)", exact.ChecksOk, exact.ChecksTotal);
                return exact;
            }
            sampled.Note += (sampled.Note.Length > 0 ? "; " : "") +
                            (exact.Lines.Count == 0 ? "정확 교선 실패 -> 샘플링 대체" :
                             string.Format("정확 교선이 표 값과 {0}/{1}만 일치 -> 샘플링 대체", exact.ChecksOk, exact.ChecksTotal));
            return sampled;
        }

        // ---------- 정확 교선 ----------
        static ButtockTraceResult TraceExact(List<Brep> breps, double yPlane, double tol, double hullLen)
        {
            var res = new ButtockTraceResult { Method = "exact" };
            var plane = new Plane(new Point3d(0, yPlane, 0), Vector3d.YAxis);
            var curves = CurveUtil.SectionCurves(breps, plane, tol);
            if (curves.Count == 0) return res;

            // 곡면 조각마다 나뉜 교선을 이어 붙임
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
                foreach (var p in pts) piece.Add(new P3(p.X, yPlane, p.Z));   // 평면 위로 정확히 맞춤
                pieces.Add(piece);
            }

            // 남은 작은 틈은 끝점끼리 이어 붙임
            double maxGap;
            double gapTol = Math.Max(tol * 20.0, hullLen * 5e-5);
            var chained = PolyTools.Chain(pieces, gapTol, out maxGap);

            foreach (var piece in chained)
            {
                var clean = PolyTools.RemoveDuplicates(piece, tol * 0.1);
                if (clean.Count < 2) continue;
                if (PolyTools.Length(clean) < tol * 5.0) continue;      // 잡음 조각
                PolyTools.OrientAftToFwd(clean);
                var line = new List<Point3d>();
                foreach (var p in clean) line.Add(new Point3d(p.X, p.Y, p.Z));
                res.Lines.Add(line);
            }

            if (res.Lines.Count > 0)
            {
                bool bridged = maxGap > tol * 2.0;
                res.Curves = bridged ? null : use;     // 틈을 메웠으면 표시는 폴리라인으로
                if (bridged) res.Note = string.Format("교선 틈 {0:0.####} 를 이어 붙임", maxGap);
                if (anyClosed) res.Note += (res.Note.Length > 0 ? "; " : "") + "닫힌 교선 있음 (캡/갑판 면이 선택에 포함됐는지 확인)";
            }
            return res;
        }

        // ---------- 적응 분할 샘플링 (대체) ----------
        static ButtockTraceResult TraceSampled(List<Brep> breps, double yPlane, double tol,
                                               BoundingBox bbox, bool smooth)
        {
            var res = new ButtockTraceResult { Method = "sampled" };
            double hullLen = bbox.Max.X - bbox.Min.X;
            double hullH = bbox.Max.Z - bbox.Min.Z;
            if (hullLen <= 0 || hullH <= 0) return res;

            double zLo = bbox.Min.Z - hullH - 1.0;
            double zHi = bbox.Max.Z + hullH + 1.0;

            AdaptiveSampler.Eval f = delegate (double x)
            {
                var sec = CurveUtil.SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
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

            var pieces = AdaptiveSampler.Sample(f, bbox.Min.X, bbox.Max.X, 300,
                                                hullH * 0.01, Math.Max(tol, hullLen * 1e-4), 2500);
            foreach (var pc in pieces)
            {
                var line = new List<Point3d>();
                foreach (var p in pc) line.Add(new Point3d(p.X, yPlane, p.Y));
                if (smooth) CurveUtil.SmoothZ(line, 5);
                res.Lines.Add(line);
            }
            return res;
        }

        // ---------- 오프셋 표 값과 대조 ----------
        static void Validate(ButtockTraceResult r, IList<double> stationX, IList<double?> tableZ, double checkTol)
        {
            r.ChecksTotal = 0; r.ChecksOk = 0;
            if (stationX == null || tableZ == null || r.Lines.Count == 0) return;

            var polys = new List<List<P3>>();
            foreach (var l in r.Lines)
            {
                var p = new List<P3>();
                foreach (var q in l) p.Add(new P3(q.X, q.Y, q.Z));
                polys.Add(p);
            }
            for (int s = 0; s < stationX.Count && s < tableZ.Count; s++)
            {
                if (!tableZ[s].HasValue) continue;
                r.ChecksTotal++;
                if (PolyTools.DistanceXZ(polys, stationX[s], tableZ[s].Value) <= checkTol) r.ChecksOk++;
            }
        }
    }
}
