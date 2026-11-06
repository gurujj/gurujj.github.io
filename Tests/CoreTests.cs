// OffsetGen 순수 계산부 콘솔 시험 (라이노 없이 실행)
using System;
using System.Collections.Generic;
using System.Globalization;
using OffsetGenPlugin;

public static class CoreTests
{
    static int pass = 0, fail = 0;

    static void Check(bool ok, string name)
    {
        if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); }
    }

    static bool Near(double a, double b, double e) { return Math.Abs(a - b) <= e; }

    public static int Main()
    {
        TestParse();
        TestStations();
        TestChain();
        TestDistance();
        TestSamplerBulb();
        TestCsv();
        TestDxf();
        Console.WriteLine("PASS {0}, FAIL {1}", pass, fail);
        return fail == 0 ? 0 : 1;
    }

    static void TestParse()
    {
        var r = ListBuilder.ParseRange("0.4", "0", "3", "1");
        Check(r != null && r.Count == 5 && Near(r[1], 0.4, 1e-12) && Near(r[4], 3, 1e-12), "range+extra sorted");
        Check(ListBuilder.ParseRange("", "0", "3", "0") == null, "step 0 rejected");
        Check(ListBuilder.ParseRange("", "5", "3", "1") == null, "end<start rejected");
        Check(ListBuilder.ParseRange("a", "", "", "") == null, "bad extra rejected");
        var e = ListBuilder.ParseRange("1.5, 0.5", "", "", "");
        Check(e != null && e.Count == 2 && e[0] == 0.5, "extra only sorted");
        var d = ListBuilder.ParseRange("1", "0", "2", "1");
        Check(d.Count == 3, "dup removed");
        var n = ListBuilder.ParseRange("", "0", "0.9", "0.3");
        Check(n.Count == 4, "float step end inclusive (0,0.3,0.6,0.9)");
    }

    static void TestStations()
    {
        string err;
        var xs = ListBuilder.BuildStations(0, 10, 0, 20, "-0.5, 0.5, 1.5, 18.5, 19.5", out err);
        Check(xs != null && err == null && xs.Count == 26, "station count 21+5");
        Check(xs[0] == -5 && xs[1] == 0 && Near(xs[2], 5, 1e-9), "sorted from -0.5");
        Check(Near(xs[xs.Count - 1], 200, 1e-9), "last = FP");
        xs = ListBuilder.BuildStations(0, 10, 0, 20, "5", out err);
        Check(xs.Count == 21, "custom dup merged");
        xs = ListBuilder.BuildStations(100, 2.5, 0, 4, "", out err);
        Check(Near(xs[4], 110, 1e-9), "AP offset and spacing");
        Check(ListBuilder.BuildStations(0, 0, 0, 20, "", out err) == null && err != null, "spacing 0 error");
        Check(ListBuilder.BuildStations(0, 10, 0, 20, "x", out err) == null, "bad custom");
        var num = new StationNumbering { Valid = true, ApX = 0, FpX = 200, Spacing = 10 };
        Check(num.Label(-5) == "-0.5" && num.Label(0) == "0" && num.Label(200) == "20", "labels");
    }

    static List<P3> Seg(double x0, double z0, double x1, double z1)
    {
        return new List<P3> { new P3(x0, 0, z0), new P3(x1, 0, z1) };
    }

    static void TestChain()
    {
        // 순서/방향이 섞인 3조각 + 작은 틈
        var pieces = new List<List<P3>>();
        pieces.Add(Seg(2, 0, 3, 0));
        pieces.Add(Seg(1.0005, 0, 0, 0));      // 반대 방향
        pieces.Add(Seg(1.0, 0, 2.0002, 0));
        double gap;
        var r = PolyTools.Chain(pieces, 0.01, out gap);
        Check(r.Count == 1, "chain to one piece");
        Check(r[0].Count == 6, "chain keeps all points");
        PolyTools.OrientAftToFwd(r[0]);
        Check(r[0][0].X == 0 && r[0][r[0].Count - 1].X == 3, "oriented aft->fwd");
        Check(gap > 0 && gap < 0.001, "gap reported");
        // 틈이 크면 이어 붙이지 않음
        pieces = new List<List<P3>> { Seg(0, 0, 1, 0), Seg(2, 0, 3, 0) };
        r = PolyTools.Chain(pieces, 0.5, out gap);
        Check(r.Count == 2, "large gap not bridged");
        // 정확히 만나는 점은 중복 제거
        pieces = new List<List<P3>> { Seg(0, 0, 1, 0), Seg(1, 0, 2, 0) };
        r = PolyTools.Chain(pieces, 0.01, out gap);
        Check(r.Count == 1 && r[0].Count == 3, "touching point merged");
    }

    static void TestDistance()
    {
        var p = new List<P3> { new P3(0, 0, 0), new P3(10, 0, 0) };
        Check(Near(PolyTools.DistanceXZ(p, 5, 3), 3, 1e-9), "perpendicular distance");
        Check(Near(PolyTools.DistanceXZ(p, 13, 4), 5, 1e-9), "end point distance");
    }

    // 벌브 모양: x < xt 에서는 값 없음, 선수 끝(xt)에서 z가 거의 수직으로 솟음, 이후 완만
    static double? Bulb(double x)
    {
        const double xt = 100.0;
        if (x < xt) return null;
        double u = x - xt;
        return -3.0 + 2.5 * Math.Sqrt(u / 0.5) / (1.0 + Math.Sqrt(u / 0.5)) + 0.01 * u;
    }

    static void TestSamplerBulb()
    {
        // 거친 기본 표본(200구간 상당)만으로는 xt 근처를 놓친다 -> 적응 분할로 해결되는지
        var coarse = AdaptiveSampler.Sample(Bulb, 89.5, 109.5, 20, 1e9, 1e9, 1000);   // 분할 없음
        var fine = AdaptiveSampler.Sample(Bulb, 89.5, 109.5, 20, 0.02, 1e-4, 100000);

        Check(fine.Count == 1, "single piece");
        double minX = double.MaxValue; foreach (var p in fine[0]) minX = Math.Min(minX, p.X);
        Check(minX < 100.0 + 2e-4, "edge (nose tip) located within minDx");
        double cMin = double.MaxValue; foreach (var p in coarse[0]) cMin = Math.Min(cMin, p.X);
        Check(cMin > 100.4, "coarse sampling misses nose tip (baseline)");

        // 폴리라인과 실제 함수의 최대 오차 (z 방향)
        double maxErr = 0.0;
        var poly = fine[0];
        for (double x = poly[0].X; x < 110; x += 0.0007)
        {
            int i = 1; while (i < poly.Count - 1 && poly[i].X < x) i++;
            double t = (x - poly[i - 1].X) / (poly[i].X - poly[i - 1].X);
            double zi = poly[i - 1].Y + t * (poly[i].Y - poly[i - 1].Y);
            maxErr = Math.Max(maxErr, Math.Abs(zi - Bulb(x).Value));
        }
        Check(maxErr < 0.02, "polyline follows steep bulb (max err " + maxErr.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
        Check(poly.Count < 5000, "point count bounded (" + poly.Count + ")");

        // 중간이 끊기는 함수 -> 2조각
        AdaptiveSampler.Eval gap = x => (x > 4 && x < 6) ? (double?)null : x * 0.1;
        var two = AdaptiveSampler.Sample(gap, 0, 10, 10, 1, 0.01, 10000);
        Check(two.Count == 2, "gap splits pieces");
    }

    static void TestCsv()
    {
        var st = new List<double> { 0, 10 };
        var wl = new List<double> { 1, 2 };
        var t = new double?[2, 2]; t[0, 0] = 1.5; t[1, 1] = 3.25;
        var bt = new List<double> { 0.5 };
        var b = new double?[1, 2]; b[0, 1] = 0.75;
        string s = CsvWriter.Build(st, wl, t, bt, b, "F3", "mm");
        var lines = s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Check(lines[0] == "Waterline Z \\ Station X (mm),0.000,10.000", "csv header");
        Check(lines[1] == "1.000,1.500,", "csv blank cell");
        Check(lines[2] == "2.000,,3.250", "csv row 2");
        Check(lines[3] == "", "csv blank separator");
        Check(lines[5] == "0.500,,0.750", "csv buttock row");
    }

    static void TestDxf()
    {
        var b = new DxfBuilder();
        b.AddLayer("A", 1);
        b.Line("A", 0, 0, 1, 1);
        b.AddPolyline("A", new List<P2> { new P2(0, 0), new P2(1, 2), new P2(2, 0) });
        b.Text("A", 0, 0, 2.5, "ST 한글 1", 1);
        string path = System.IO.Path.GetTempFileName();
        b.Save(path);
        string txt = System.IO.File.ReadAllText(path);
        Check(txt.Contains("SECTION") && txt.Contains("EOF") && txt.Contains("POLYLINE") && txt.Contains("SEQEND"), "dxf structure");
        Check(txt.Contains("ST ? 1") || txt.Contains("ST ?? 1"), "non-ascii replaced");
        int v = 0, idx = 0; while ((idx = txt.IndexOf("VERTEX", idx)) >= 0) { v++; idx += 6; }
        Check(v == 3, "3 vertices");
        System.IO.File.Delete(path);
    }
}
