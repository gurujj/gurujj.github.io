// 버톡 내삽 개선(AdaptiveSampler) 콘솔 시험 - 라이노 없이 실행
using System;
using System.Collections.Generic;
using System.Globalization;
using OffsetGenPlugin;

public static class AdaptiveSamplerTests
{
    static int pass = 0, fail = 0;
    static void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }
    static string F(double v) { return v.ToString("0.0000", CultureInfo.InvariantCulture); }

    // 벌브 모양 버톡의 최저 z: x < 100(선수 끝) 에서는 값이 없고, 끝에서 z가 거의 수직으로 솟음
    static double? Bulb(double x)
    {
        const double xt = 100.0;
        if (x < xt) return null;
        double u = x - xt;
        return -3.0 + 2.5 * Math.Sqrt(u / 0.5) / (1.0 + Math.Sqrt(u / 0.5)) + 0.01 * u;
    }

    // 폴리라인을 x에서 선형 보간한 z
    static double InterpZ(List<P2> poly, double x)
    {
        int i = 1; while (i < poly.Count - 1 && poly[i].X < x) i++;
        double t = (x - poly[i - 1].X) / (poly[i].X - poly[i - 1].X);
        return poly[i - 1].Y + t * (poly[i].Y - poly[i - 1].Y);
    }

    static double MaxErr(List<P2> poly)
    {
        double e = 0.0;
        for (double x = poly[0].X; x < poly[poly.Count - 1].X; x += 0.0007)
            e = Math.Max(e, Math.Abs(InterpZ(poly, x) - Bulb(x).Value));
        return e;
    }

    public static int Run()
    {
        // 원본 방식: 200구간 고정 격자(선체 길이 200 가정 -> 간격 1.0)를 직선으로 이음.
        // 선수 끝(x=100)이 격자점 사이(99.6~100.6)에 놓이도록 시작점을 어긋나게 잡음
        double x0 = 0.4, x1 = 200.4;
        var coarse = AdaptiveSampler.Sample(Bulb, x0, x1, 200, 1e9, 1e9, 0);
        double coarseTip = coarse[0][0].X;
        double coarseErr = MaxErr(coarse[0]);

        var fine = AdaptiveSampler.Sample(Bulb, x0, x1, 200, 0.02, 1e-3, 600);
        double fineTip = fine[0][0].X;
        double fineErr = MaxErr(fine[0]);

        Console.WriteLine("ORIGINAL: start x=" + F(coarseTip) + ", max err " + F(coarseErr) + ", points " + coarse[0].Count);
        Console.WriteLine("REFINED : start x=" + F(fineTip) + ", max err " + F(fineErr) + ", points " + fine[0].Count);

        Check(coarseTip > 100.3, "baseline misses the nose tip");
        Check(fineTip < 100.0 + 2e-3, "refined finds the nose tip");
        Check(fineErr < coarseErr / 10.0, "refined error at least 10x smaller");
        Check(fineErr < 0.03, "refined max error < 0.03");
        Check(fine[0].Count < 400, "evaluation count bounded (" + fine[0].Count + ")");

        // maxEvals 한도가 지켜지는지
        int calls = 0;
        AdaptiveSampler.Eval counting = delegate (double x) { calls++; return Bulb(x); };
        var xs = new List<double>(); var zs = new List<double?>();
        for (int i = 0; i <= 200; i++) { double x = 0.4 + i; xs.Add(x); zs.Add(Bulb(x)); }
        AdaptiveSampler.Refine(counting, xs, zs, 1e-6, 1e-9, 50);
        Check(calls <= 50, "maxEvals respected (" + calls + ")");

        // 초기 표본 중간에 값이 없는 구간 -> 조각이 나뉘고, 경계는 minDx까지 찾음
        AdaptiveSampler.Eval gap = x => (x > 4.3 && x < 6.1) ? (double?)null : x * 0.1;
        var two = AdaptiveSampler.Sample(gap, 0, 10, 10, 1, 0.01, 10000);
        Check(two.Count == 2, "gap splits pieces");
        Check(Math.Abs(two[0][two[0].Count - 1].X - 4.3) < 0.02 && Math.Abs(two[1][0].X - 6.1) < 0.02, "gap edges located");

        Console.WriteLine("PASS " + pass + ", FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
