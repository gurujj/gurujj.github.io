using System;
using System.Collections.Generic;

namespace OffsetGenPlugin
{
    // 폴리라인 처리 도구 (라이노 비의존)
    public static class PolyTools
    {
        public static double Dist(P3 a, P3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static double Length(IList<P3> p)
        {
            double s = 0.0;
            for (int i = 1; i < p.Count; i++) s += Dist(p[i - 1], p[i]);
            return s;
        }

        // 연속한 중복점(거리 < eps) 제거
        public static List<P3> RemoveDuplicates(IList<P3> p, double eps)
        {
            var r = new List<P3>();
            for (int i = 0; i < p.Count; i++)
                if (r.Count == 0 || Dist(r[r.Count - 1], p[i]) >= eps) r.Add(p[i]);
            return r;
        }

        // 선미(x 작은 쪽) -> 선수(x 큰 쪽) 방향으로 정렬
        public static void OrientAftToFwd(List<P3> p)
        {
            if (p.Count >= 2 && p[0].X > p[p.Count - 1].X) p.Reverse();
        }

        // 끝점 사이 거리가 gapTol 이하인 조각들을 이어 붙임. 가장 가까운 쌍부터 합침.
        // 이어 붙인 틈의 최대 길이를 maxGap으로 돌려줌.
        public static List<List<P3>> Chain(List<List<P3>> pieces, double gapTol, out double maxGap)
        {
            maxGap = 0.0;
            var work = new List<List<P3>>();
            foreach (var p in pieces) if (p != null && p.Count >= 2) work.Add(new List<P3>(p));

            while (true)
            {
                int bi = -1, bj = -1; bool bRevI = false, bRevJ = false;
                double best = gapTol;
                for (int i = 0; i < work.Count; i++)
                    for (int j = 0; j < work.Count; j++)
                    {
                        if (i == j) continue;
                        // i의 끝점(또는 reverse한 i의 끝점 = 시작점) -> j의 시작점(또는 끝점)
                        for (int ri = 0; ri < 2; ri++)
                            for (int rj = 0; rj < 2; rj++)
                            {
                                P3 a = ri == 0 ? work[i][work[i].Count - 1] : work[i][0];
                                P3 b = rj == 0 ? work[j][0] : work[j][work[j].Count - 1];
                                double d = Dist(a, b);
                                if (d <= best && (bi < 0 || d < best))
                                {
                                    best = d; bi = i; bj = j; bRevI = ri == 1; bRevJ = rj == 1;
                                }
                            }
                    }
                if (bi < 0) break;

                var A = work[bi]; var B = work[bj];
                if (bRevI) A.Reverse();
                if (bRevJ) B.Reverse();
                if (best > maxGap) maxGap = best;
                if (best < 1e-12) B.RemoveAt(0);   // 같은 점이면 중복 제거
                A.AddRange(B);
                work.RemoveAt(bj);
            }
            return work;
        }

        // 점 (x, z)에서 폴리라인(x-z 평면으로 본 거리)까지의 최단 거리
        public static double DistanceXZ(IList<P3> poly, double x, double z)
        {
            double best = double.MaxValue;
            if (poly.Count == 1) return Math.Sqrt((poly[0].X - x) * (poly[0].X - x) + (poly[0].Z - z) * (poly[0].Z - z));
            for (int i = 1; i < poly.Count; i++)
            {
                double ax = poly[i - 1].X, az = poly[i - 1].Z, bx = poly[i].X, bz = poly[i].Z;
                double dx = bx - ax, dz = bz - az;
                double L2 = dx * dx + dz * dz;
                double t = L2 < 1e-30 ? 0.0 : ((x - ax) * dx + (z - az) * dz) / L2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double px = ax + t * dx - x, pz = az + t * dz - z;
                double d = Math.Sqrt(px * px + pz * pz);
                if (d < best) best = d;
            }
            return best;
        }

        // 여러 폴리라인 중 가장 가까운 거리
        public static double DistanceXZ(IList<List<P3>> polys, double x, double z)
        {
            double best = double.MaxValue;
            foreach (var p in polys)
            {
                double d = DistanceXZ(p, x, z);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
