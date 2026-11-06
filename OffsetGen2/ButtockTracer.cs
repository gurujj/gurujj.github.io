// 정밀 버톡 추적 (Rhino 의존)
//
// [기존 방식의 한계]  단면(x = 일정)마다 수직선 y = b 와의 교점 중 "최저 z"만 이어서 버톡을 만들었다.
//   벌브 선수에서는 버톡 평면(y = b)이 벌브를 잘라 만든 곡선이 x에 대해 다가(多價)다:
//   벌브 아랫면을 따라 선수 쪽으로 가다가 끝에서 위로 돌아 올라가 벌브 윗면 -> 스템으로 이어진다.
//   "x마다 최저 z"는 이 곡선의 아랫면만 보므로, 벌브 끝(앞)에서 위쪽 윤곽을 잃고
//   아랫면 끝과 스템 사이를 대각선으로 이어 버린다 (분할을 아무리 촘촘히 해도 수직으로 뛰는 선이 남음).
//
// [새 방식]  버톡 평면 y = b 를 선체 Brep과 직접 교차시켜 얻은 정확한 곡선을 그대로 사용한다.
//   - 교차 곡선은 x, z 의 함수가 아니라 곡선 자체이므로 벌브가 아무리 휘어도 선형을 그대로 따라간다.
//   - 폴리라인 변환은 각도(1.5도)와 현 오차(허용오차) 기준 -> 곡률이 큰 벌브 끝은 자동으로 촘촘해짐.
//   - 닫힌 곡선(갑판/바닥이 있는 솔리드)이 나오면 이 방식을 쓰지 않고 호출한 쪽이 기존 방식으로 되돌아간다.
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace OffsetGenPlugin
{
    public static class ButtockTracer
    {
        const double AngleRad = 0.0261799;   // 1.5도

        // yPlane 위치의 버톡 곡선을 만든다. 성공하면 true.
        //  curves: 정확한 곡선 (라이노 표시용), lines: 폴리라인 점 (DXF용, y = yPlane)
        //  실패(교차 없음, 닫힌 곡선)하면 false, note 에 이유.
        public static bool TryTrace(List<Brep> breps, double yPlane, double tol, double hullLen,
                                    out List<Curve> curves, out List<List<Point3d>> lines, out string note)
        {
            curves = new List<Curve>();
            lines = new List<List<Point3d>>();
            note = "";

            var plane = new Plane(new Point3d(0, yPlane, 0), Vector3d.YAxis);
            var raw = CurveUtil.SectionCurves(breps, plane, tol);
            if (raw.Count == 0) { note = "버톡 평면과 선체가 교차하지 않음"; return false; }

            // 서피스 경계에서 끊어진 조각들을 이음
            double joinTol = Math.Max(tol * 5.0, hullLen * 1e-6);
            Curve[] joined = null;
            try { joined = Curve.JoinCurves(raw, joinTol); } catch (Exception) { joined = null; }
            var list = new List<Curve>();
            if (joined != null && joined.Length > 0) list.AddRange(joined); else list.AddRange(raw);

            double minLen = Math.Max(tol * 10.0, hullLen * 1e-5);
            foreach (var c in list)
            {
                if (c == null) continue;
                if (c.IsClosed) { note = "닫힌 교차 곡선(솔리드 선체)"; curves.Clear(); lines.Clear(); return false; }
                if (c.GetLength() < minLen) continue;

                var pts = ToPoints(c, tol);
                if (pts.Count < 2) continue;

                // 점의 y를 평면 값으로 정리하고, 같은 점 제거
                var cleaned = new List<Point3d>();
                foreach (var p in pts)
                {
                    var q = new Point3d(p.X, yPlane, p.Z);
                    if (cleaned.Count > 0 && cleaned[cleaned.Count - 1].DistanceTo(q) <= tol * 0.05) continue;
                    cleaned.Add(q);
                }
                if (cleaned.Count < 2) continue;

                // 선미 -> 선수 방향으로 통일
                if (cleaned[0].X > cleaned[cleaned.Count - 1].X)
                {
                    cleaned.Reverse();
                    var rc = c.DuplicateCurve();
                    rc.Reverse();
                    curves.Add(rc);
                }
                else curves.Add(c);
                lines.Add(cleaned);
            }

            if (lines.Count == 0) { note = "유효한 교차 곡선 없음"; return false; }
            return true;
        }

        static List<Point3d> ToPoints(Curve c, double tol)
        {
            var pts = new List<Point3d>();
            var pc = c.ToPolyline(0, 0, AngleRad, 0.0, 0.0, tol, 0.0, 0.0, true);
            if (pc != null)
            {
                Polyline pl;
                if (pc.TryGetPolyline(out pl))
                    foreach (var p in pl) pts.Add(p);
            }
            if (pts.Count < 2)
            {
                pts.Clear();
                double[] t = c.DivideByCount(400, true);
                if (t != null)
                    foreach (double ti in t) pts.Add(c.PointAt(ti));
            }
            return pts;
        }

        // 폴리라인(x, y, z) -> (x, z) 점
        public static List<P2> ToXZ(List<Point3d> pts)
        {
            var r = new List<P2>(pts.Count);
            foreach (var p in pts) r.Add(new P2(p.X, p.Z));
            return r;
        }
    }
}
