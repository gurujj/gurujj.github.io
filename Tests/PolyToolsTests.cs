// 벌브 선수 버톡(다가 곡선) 시험 - 라이노 없이 실행
//  기존 방식(x마다 최저 z만 이음)이 벌브 윗면/선수 끝을 잃는 것과,
//  새 방식(곡선 자체 사용)의 교점 계산이 아랫면/윗면 모두를 찾는 것을 확인한다.
using System;
using System.Collections.Generic;
using System.Globalization;
using OffsetGenPlugin;

public static class PolyToolsTests
{
    static int pass = 0, fail = 0;
    static void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }
    static string F(double v) { return v.ToString("0.0000", CultureInfo.InvariantCulture); }

    // 합성 벌브 버톡 곡선 (x: 선미->선수, z: 위쪽):
    //  키일 z = -3 (x 0 ~ 98.5) -> 반지름 1.5 반원 벌브 앞면(중심 (98.5, -1.5)) -> 윗면 (98.5, 0) -> 기울어진 스템 (101, 8)
    static List<P2> BulbCurve()
    {
        var p = new List<P2>();
        for (double x = 0; x < 98.5; x += 2.0) p.Add(new P2(x, -3.0));
        for (int i = 0; i <= 120; i++)                    // 반원: 아래(-90도) -> 앞(0도) -> 위(+90도), 1.5도 간격
        {
            double a = (-90.0 + 1.5 * i) * Math.PI / 180.0;
            p.Add(new P2(98.5 + 1.5 * Math.Cos(a), -1.5 + 1.5 * Math.Sin(a)));
        }
        p.Add(new P2(101.0, 8.0));
        return p;
    }

    public static int Run()
    {
        var poly = BulbCurve();

        // 벌브 앞(x = 99.9): 아랫면, 윗면, 스템 3곳
        double xv = 99.9, d = xv - 98.5, h = Math.Sqrt(1.5 * 1.5 - d * d);
        var zs = PolyTools.CrossingsAtX(poly, xv, 1e-6);
        Console.WriteLine("crossings at x=99.9: " + string.Join(", ", zs.ConvertAll(z => F(z)).ToArray())
                          + "  (expected " + F(-1.5 - h) + ", " + F(-1.5 + h) + ", ~4.48)");
        Check(zs.Count == 3, "3 crossings at bulb front");
        Check(Math.Abs(zs[0] - (-1.5 - h)) < 0.003, "lower surface of bulb");
        Check(Math.Abs(zs[1] - (-1.5 + h)) < 0.003, "upper surface of bulb");

        // 기존 방식: x마다 최저 z만 사용 -> 윗면이 사라지고 벌브 끝~스템 사이를 직선으로 이음
        // (최저 z 곡선은 x=100 에서 z=-1.5, 그 앞은 스템의 z 로 갑자기 뜀: 벌브 윗면을 지나가지 못함)
        double xa = 99.99;
        var za = PolyTools.CrossingsAtX(poly, xa, 1e-6);
        Check(za.Count >= 2, "bulb top exists at x=99.99");
        double lowestOnly = za[0];
        double bulbTop = za[1];
        Check(bulbTop - lowestOnly > 0.1, "lowest-z-only drops the bulb top contour (gap " + F(bulbTop - lowestOnly) + ")");

        // 선수 끝 바로 앞(x = 100.01)은 벌브가 이미 끝나서 스템만 남음
        var zf = PolyTools.CrossingsAtX(poly, 100.01, 1e-6);
        Check(zf.Count == 1 && zf[0] > 4.0, "ahead of bulb nose only the stem remains");

        // 정리: 가까운 값은 하나로
        var dd = PolyTools.Distinct(new List<double> { 3.0, 1.0, 1.00000001, 2.0 }, 1e-6);
        Check(dd.Count == 3 && dd[0] == 1.0 && dd[2] == 3.0, "distinct sorted");

        // 수직 구간(선체 끝 평면)과 겹치는 경우
        var vert = new List<P2> { new P2(0, 0), new P2(5, 0), new P2(5, 3), new P2(10, 3) };
        var zv = PolyTools.CrossingsAtX(vert, 5.0, 1e-6);
        Check(zv.Count == 2 && zv[0] == 0.0 && zv[1] == 3.0, "vertical overlap gives both ends");

        // x 범위 판정
        var lst = new List<List<P2>> { poly };
        Check(PolyTools.InXRange(lst, 50, 0) && !PolyTools.InXRange(lst, 105, 0), "x range");

        // 폴리라인 점 간격: 벌브 반원은 1.5도 간격이므로 현과 호의 최대 오차가 작아야 함
        double sag = 1.5 * (1.0 - Math.Cos(0.75 * Math.PI / 180.0));
        Check(sag < 0.0003, "1.5 deg polyline sag small (" + F(sag) + ")");

        Console.WriteLine("PASS " + pass + ", FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
