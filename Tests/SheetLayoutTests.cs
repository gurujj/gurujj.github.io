// 도면 블록 배치 시험: 세 블록이 서로 겹치지 않고, 모든 블록이 테두리 안에 들어오고, 마진이 지켜지는지
using System;
using OffsetGenPlugin;

public static class SheetLayoutTests
{
    static int pass = 0, fail = 0;
    static void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }

    static SheetLayoutInput Input(double tableW1, double tableW2, double tableH)
    {
        double u = 220000;
        double tH = u * 0.0055;
        return new SheetLayoutInput
        {
            XMin = 0, XMax = u, ZLow = 0, ZMax = 23750, HalfBeam = 16150, TH = tH,
            LabW = 10.5 * tH * 0.6, LabMargin = 1.5 * tH,
            TableW1 = tableW1, TableH1 = tableH, TableW2 = tableW2, TableH2 = tableH * 0.9,
            PartW = 0.28 * u, PartH = 0.12 * u, TitleW = 0.17 * u, TitleH = 0.09 * u, RevW = 0.15 * u, RevH = 0.03 * u
        };
    }

    public static int Run()
    {
        // 여러 크기의 표로 시험: 좁은 표, 선체보다 훨씬 넓은 표
        foreach (var w in new[] { 0.3, 0.6, 0.9 })
        {
            var i = Input(w * 220000, w * 0.8 * 220000, 0.25 * 220000);
            var s = SheetLayout.Compute(i);
            string tag = " (table width " + w + "u)";
            Check(!s.Table1.Intersects(s.Body, s.Gap * 0.99), "table1 and body keep the margin" + tag);
            Check(!s.Body.Intersects(s.Table2, s.Gap * 0.99), "body and table2 keep the margin" + tag);
            Check(!s.Table1.Intersects(s.Table2, s.Gap), "tables keep the margin" + tag);
            Check(Math.Abs(s.Table1.B - s.Body.B) < 1e-6 && Math.Abs(s.Table1.T - s.Body.T) < 1e-6 &&
                  Math.Abs(s.Table2.T - s.Body.T) < 1e-6, "top row aligned top and bottom" + tag);
            Check(s.Frame.Contains(s.Table1) && s.Frame.Contains(s.Body) && s.Frame.Contains(s.Table2), "top row inside frame" + tag);
            Check(s.Frame.Contains(s.Hull) && s.Frame.Contains(s.Particulars), "hull and particulars inside frame" + tag);
            Check(s.Frame.Contains(s.Revision) && s.Frame.Contains(s.TitleBlock), "revision and title block inside frame" + tag);
            Check(!s.Revision.Intersects(s.Table2, i.TH * 2.5), "revision clear of right table" + tag);
            Check(s.Hull.T < s.BandBottom, "hull below the top row" + tag);
            Check(!s.Hull.Intersects(s.Particulars, 10.0 * i.TH), "particulars keep 10 text heights from the plans" + tag);
            Check(!s.TitleBlock.Intersects(s.Particulars, i.TH), "title block clear of particulars" + tag);
            Check(!s.TitleBlock.Intersects(s.Hull, i.TH), "title block clear of the plans" + tag);
            // 도면은 내용 크기에서 결정: 위쪽 띠가 선체보다 넓으면 테두리도 그만큼 넓어짐
            double top = s.Table2.R - s.Table1.L;
            Check(s.Frame.Width >= top && s.Frame.Width >= 220000, "frame is at least as wide as the widest block" + tag);
            // Body plan 은 위쪽 띠 높이를 채우도록 확대 (1~3배), 상자 안에 들어옴
            Check(s.BodyScale >= 1.0 && s.BodyScale <= 3.0 + 1e-9, "body scale within 1..3" + tag);
            double bodyTop = s.BodyBase + (i.ZMax - i.ZLow) * s.BodyScale;
            Check(s.BodyBase >= s.Body.B && bodyTop <= s.Body.T, "scaled body plan inside its box" + tag);
            double halfW = i.HalfBeam * s.BodyScale;
            Check(s.CxBody - halfW >= s.Body.L && s.CxBody + halfW <= s.Body.R, "scaled body plan width inside its box" + tag);
            if (i.TableH1 > (i.ZMax - i.ZLow) + 12.0 * i.TH)
                Check(s.BodyScale > 1.2, "body plan enlarged when tables are taller" + tag);
            // 선체 평면도는 테두리 가운데
            Check(Math.Abs((s.Hull.L + s.Hull.R) / 2.0 - (s.Frame.L + s.Frame.R) / 2.0) < 1e-6, "plans centered in frame" + tag);
        }
        Console.WriteLine("SheetLayout PASS " + pass + ", FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
