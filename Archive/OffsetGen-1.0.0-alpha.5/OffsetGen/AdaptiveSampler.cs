// 급변 구간 자동 세분 표본 추출 (라이노 비의존, 콘솔 시험 가능)
using System;
using System.Collections.Generic;

namespace OffsetGenPlugin
{
    public static class AdaptiveSampler
    {
        public delegate double? Eval(double x);

        // 초기 표본(xs, zs; zs[i] = null이면 값 없음)을 받아서 필요한 곳에 중간점을 끼워 넣음.
        //  - 이웃 표본의 z 차이가 maxDz를 넘으면 이분 분할 (벌브 선수 끝처럼 z가 급변하는 곳)
        //  - 한쪽만 값이 있으면 (선체 끝 가장자리) 간격이 minDx가 될 때까지 이분 분할해 끝점을 찾음
        //  - 새로 평가하는 횟수는 maxEvals까지
        // 값이 없는 곳에서 끊어 여러 조각 (각 점은 x, z)으로 돌려줌.
        public static List<List<P2>> Refine(Eval f, List<double> xs, List<double?> zs,
                                            double maxDz, double minDx, int maxEvals)
        {
            int evals = 0;
            int k = 0;
            while (k < xs.Count - 1)
            {
                double dx = xs[k + 1] - xs[k];
                double? za = zs[k], zb = zs[k + 1];
                bool split = false;
                if (dx > minDx && evals < maxEvals)
                {
                    if (za.HasValue && zb.HasValue) split = Math.Abs(za.Value - zb.Value) > maxDz;
                    else if (za.HasValue != zb.HasValue) split = true;
                }
                if (split)
                {
                    double xm = 0.5 * (xs[k] + xs[k + 1]);
                    xs.Insert(k + 1, xm); zs.Insert(k + 1, f(xm)); evals++;
                    continue;
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

        // x0..x1을 n등분한 초기 표본으로 시작하는 편의 함수
        public static List<List<P2>> Sample(Eval f, double x0, double x1, int n, double maxDz, double minDx, int maxEvals)
        {
            if (n < 1) n = 1;
            var xs = new List<double>();
            var zs = new List<double?>();
            for (int i = 0; i <= n; i++)
            {
                double x = x0 + (x1 - x0) * i / n;
                xs.Add(x); zs.Add(f(x));
            }
            return Refine(f, xs, zs, maxDz, minDx, maxEvals);
        }
    }

    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
    }
}
