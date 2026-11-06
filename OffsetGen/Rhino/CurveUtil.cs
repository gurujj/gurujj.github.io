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
    // 커브 유틸
    // ---------------------------------------------------------------
    public static class CurveUtil
    {
        // 한쪽 현 기준: 더 큰 쪽의 y 부호를 양(+)으로 사용
        public static double YSign(BoundingBox bb)
        {
            return Math.Abs(bb.Min.Y) > Math.Abs(bb.Max.Y) ? -1.0 : 1.0;
        }

        // 모든 Brep과 평면의 교차 커브
        public static List<Curve> SectionCurves(List<Brep> breps, Plane plane, double tol)
        {
            var result = new List<Curve>();
            foreach (var brep in breps)
            {
                Curve[] curves;
                Point3d[] pts;
                if (Rhino.Geometry.Intersect.Intersection.BrepPlane(brep, plane, tol, out curves, out pts)
                    && curves != null)
                    result.AddRange(curves);
            }
            return result;
        }

        // 커브들을 점 목록으로 바꿈. clip이 true면 양현 모델의 반대쪽(y<0 쪽)을 잘라냄.
        public static List<List<Point3d>> Prepare(IEnumerable<Curve> curves, double ySign, double tol, bool clip)
        {
            var result = new List<List<Point3d>>();
            if (curves == null) return result;

            foreach (var c in curves)
            {
                var pts = CurveToPoints(c, tol);
                if (pts.Count < 2) continue;
                if (clip) ClipPositiveSide(result, pts, ySign, tol);
                else result.Add(pts);
            }
            return result;
        }

        public static List<Point3d> CurveToPoints(Curve c, double tol)
        {
            return CurveToPoints(c, tol, 0.0523599);   // 3도
        }

        public static List<Point3d> CurveToPoints(Curve c, double tol, double maxAngleRadians)
        {
            var pts = new List<Point3d>();

            // 각도 기준으로 폴리라인화 (곡률이 큰 곳은 자동으로 촘촘해짐)
            var pc = c.ToPolyline(0, 0, maxAngleRadians, 0.0, 0.0, tol, 0.0, 0.0, true);
            if (pc != null)
            {
                Polyline pl;
                if (pc.TryGetPolyline(out pl))
                    foreach (var p in pl) pts.Add(p);
            }

            // 실패하면 등분 점으로 대체
            if (pts.Count < 2)
            {
                pts.Clear();
                double[] t = c.DivideByCount(80, true);
                if (t != null)
                    foreach (double ti in t) pts.Add(c.PointAt(ti));
            }
            return pts;
        }

        // 폴리라인에서 y*ySign >= 0 쪽만 남김 (중심선을 가로지르는 구간은 y=0에서 자름)
        static void ClipPositiveSide(List<List<Point3d>> output, List<Point3d> pts, double ySign, double tol)
        {
            var cur = new List<Point3d>();
            for (int i = 0; i < pts.Count; i++)
            {
                Point3d p = pts[i];
                double sp = p.Y * ySign;
                bool pin = sp >= -tol;

                if (i == 0)
                {
                    if (pin) cur.Add(p);
                    continue;
                }

                Point3d q = pts[i - 1];
                double sq = q.Y * ySign;
                bool qin = sq >= -tol;

                if (pin && qin)
                {
                    cur.Add(p);
                }
                else if (pin && !qin)
                {
                    cur.Add(CrossAtZero(q, p, sq, sp));
                    cur.Add(p);
                }
                else if (!pin && qin)
                {
                    cur.Add(CrossAtZero(q, p, sq, sp));
                    if (cur.Count >= 2) output.Add(cur);
                    cur = new List<Point3d>();
                }
            }
            if (cur.Count >= 2) output.Add(cur);
        }

        static Point3d CrossAtZero(Point3d q, Point3d p, double sq, double sp)
        {
            double d = sq - sp;
            double t = Math.Abs(d) < 1e-12 ? 0.0 : sq / d;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return new Point3d(q.X + t * (p.X - q.X), q.Y + t * (p.Y - q.Y), q.Z + t * (p.Z - q.Z));
        }

        // 조밀한 스테이션(n+1개)의 단면을 폴리라인으로 미리 만들어 둠 (윤곽/버톡 계산에 공통 사용)
        public static List<DenseSection> ComputeDenseSections(List<Brep> breps, BoundingBox bb,
                                                              double ySign, double tol, int n)
        {
            var res = new List<DenseSection>();
            double len = bb.Max.X - bb.Min.X;
            if (len <= 0 || n < 1) return res;

            for (int i = 0; i <= n; i++)
            {
                double x = bb.Min.X + len * i / n;
                var secs = SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
                if (secs.Count == 0) continue;

                var polys = Prepare(secs, ySign, tol, true);
                if (polys.Count == 0) continue;
                res.Add(new DenseSection { X = x, Curves = secs, Polys = polys });
            }
            return res;
        }

        // 단면의 최상단(갑판 가장자리)과 최저점을 뽑음.
        // 갑판 가장자리 = 단면에서 가장 높은 점들 중 중심선에서 가장 먼 점 (추정)
        public static List<OutlinePoint> ComputeOutline(List<DenseSection> dense, BoundingBox bb,
                                                        double ySign, double tol)
        {
            var res = new List<OutlinePoint>();
            double dz = Math.Max(tol * 5.0, (bb.Max.Z - bb.Min.Z) * 0.002);

            foreach (var ds in dense)
            {
                bool any = false;
                double zTop = double.MinValue, zBot = double.MaxValue;
                foreach (var pl in ds.Polys)
                    foreach (var p in pl)
                    {
                        any = true;
                        if (p.Z > zTop) zTop = p.Z;
                        if (p.Z < zBot) zBot = p.Z;
                    }
                if (!any) continue;

                double yTop = 0.0;
                foreach (var pl in ds.Polys)
                    foreach (var p in pl)
                        if (p.Z >= zTop - dz)
                        {
                            double yy = p.Y * ySign;
                            if (yy > yTop) yTop = yy;
                        }

                res.Add(new OutlinePoint { X = ds.X, YTop = yTop, ZTop = zTop, ZBot = zBot });
            }
            return res;
        }

        // z만 가볍게 평활화 ([1 2 1]/4 필터 반복, 양 끝점은 고정). 선도/표시용 - 표의 값은 그대로 둠.
        public static void SmoothZ(List<Point3d> pts, int passes)
        {
            if (pts == null || pts.Count < 3) return;
            for (int k = 0; k < passes; k++)
            {
                double prev = pts[0].Z;
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    double cz = pts[i].Z;
                    double nz = 0.25 * prev + 0.5 * cz + 0.25 * pts[i + 1].Z;
                    prev = cz;
                    Point3d q = pts[i];
                    pts[i] = new Point3d(q.X, q.Y, nz);
                }
            }
        }
    }
}
