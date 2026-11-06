using System;
using System.Collections.Generic;

namespace OffsetGenPlugin
{
    // 함수 z(x) (없으면 null)를 급변 구간에서 자동으로 촘촘하게 표본 추출.
    // 정확한 교선(곡면-평면)이 실패했을 때의 대체 경로이며, 벌브처럼 z가 x에 대해 급변하는 곳을 놓치지 않게 함.
    public static class AdaptiveSampler
    {
        public delegate double? Eval(double x);

        // x0..x1을 n등분으로 시작해, 이웃 표본의 z 차이가 maxDz를 넘거나 한쪽만 값이 있는 구간(가장자리)은
        // 간격이 minDx가 될 때까지 이분 분할. 값이 없는 구간에서 끊어 여러 조각을 돌려줌 (각 점은 x, z).
        public static List<List<P2>> Sample(Eval f, double x0, double x1, int n, double maxDz, double minDx, int maxEvals)
        {
            if (n < 1) n = 1;
            var xs = new List<double>();
            var zs = new List<double?>();
            int evals = 0;
            for (int i = 0; i <= n; i++)
            {
                double x = x0 + (x1 - x0) * i / n;
                xs.Add(x); zs.Add(f(x)); evals++;
            }

            // 구간을 앞에서부터 보며 필요하면 중간점을 끼워 넣음
            int k = 0;
            while (k < xs.Count - 1)
            {
                double dx = xs[k + 1] - xs[k];
                double? za = zs[k], zb = zs[k + 1];
                bool split = false;
                if (dx > minDx && evals < maxEvals)
                {
                    if (za.HasValue && zb.HasValue) split = Math.Abs(za.Value - zb.Value) > maxDz;
                    else if (za.HasValue != zb.HasValue) split = true;   // 가장자리 찾기
                }
                if (split)
                {
                    double xm = 0.5 * (xs[k] + xs[k + 1]);
                    xs.Insert(k + 1, xm); zs.Insert(k + 1, f(xm)); evals++;
                    continue;   // 같은 위치에서 다시 검사
                }
                k++;
            }

            var result = new List<List<P2>>();
            var cur = new List<P2>();
            for (int i = 0; i < xs.Count; i++)
            {
                if (zs[i].HasValue) cur.Add(new P2(xs[i], zs[i].Value));
                else
                {
                    if (cur.Count >= 2) result.Add(cur);
                    cur = new List<P2>();
                }
            }
            if (cur.Count >= 2) result.Add(cur);
            return result;
        }
    }
}
