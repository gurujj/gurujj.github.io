// 폴리라인 교차/요약 도구 (라이노 비의존, 콘솔 시험 가능)
// 버톡 커브는 벌브 선수에서 x에 대해 다가(多價)이므로 z = f(x) 로 다루지 않고
// "x = 일정한 수직선과 폴리라인의 모든 교차점"으로 다룬다.
using System;
using System.Collections.Generic;

namespace OffsetGenPlugin
{
    public static class PolyTools
    {
        // 폴리라인(점 x,z)과 수직선 x = xv 의 모든 교차 z (오름차순, dedupTol 이내는 하나로 합침)
        public static List<double> CrossingsAtX(List<P2> poly, double xv, double dedupTol)
        {
            var zs = new List<double>();
            AddCrossings(zs, poly, xv);
            return Dedup(zs, dedupTol);
        }

        public static List<double> CrossingsAtX(IEnumerable<List<P2>> polys, double xv, double dedupTol)
        {
            var zs = new List<double>();
            foreach (var p in polys) AddCrossings(zs, p, xv);
            return Dedup(zs, dedupTol);
        }

        static void AddCrossings(List<double> zs, List<P2> poly, double xv)
        {
            if (poly == null) return;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                P2 a = poly[i], b = poly[i + 1];
                double da = a.X - xv, db = b.X - xv;
                if (da == 0.0 && db == 0.0) { zs.Add(a.Y); zs.Add(b.Y); continue; }   // 수직선과 겹치는 구간
                if ((da < 0.0 && db < 0.0) || (da > 0.0 && db > 0.0)) continue;
                double t = da / (da - db);
                zs.Add(a.Y + t * (b.Y - a.Y));
            }
        }

        static List<double> Dedup(List<double> zs, double tol)
        {
            zs.Sort();
            var r = new List<double>();
            foreach (double z in zs)
                if (r.Count == 0 || z - r[r.Count - 1] > tol) r.Add(z);
            return r;
        }

        // z 값들을 오름차순 정렬하고 tol 이내는 하나로 합침 (스테이션 단면과 버톡 평면의 교점 목록 정리용)
        public static List<double> Distinct(List<double> zs, double tol)
        {
            var c = new List<double>(zs);
            return Dedup(c, tol);
        }

        // 폴리라인들의 x 범위 안에서 xv 위치에 교차가 있는지
        public static bool InXRange(IEnumerable<List<P2>> polys, double xv, double slack)
        {
            foreach (var p in polys)
            {
                if (p == null || p.Count < 2) continue;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var q in p) { if (q.X < lo) lo = q.X; if (q.X > hi) hi = q.X; }
                if (xv >= lo - slack && xv <= hi + slack) return true;
            }
            return false;
        }

        // 연속한 같은 점 제거
        public static List<P2> RemoveDuplicates(List<P2> pts, double tol)
        {
            var r = new List<P2>();
            foreach (var p in pts)
            {
                if (r.Count > 0)
                {
                    double dx = p.X - r[r.Count - 1].X, dy = p.Y - r[r.Count - 1].Y;
                    if (dx * dx + dy * dy <= tol * tol) continue;
                }
                r.Add(p);
            }
            return r;
        }
    }
}
