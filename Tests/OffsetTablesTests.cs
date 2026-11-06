// 오프셋 표(스테이션 행, 수선/버톡 열, 복수 교점 쌓기)와 DXF 출력 시험 (라이노 없이)
using System;
using System.Collections.Generic;
using OffsetGenPlugin;

public static class OffsetTablesTests
{
    static int pass = 0, fail = 0;
    static void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }

    public static int Run()
    {
        // 스테이션 3개 (-0.5, F.P., 0.5), 수선 2개, 버톡 3개 (0 은 표 열에서 제외)
        var d = new OffsetData(3, 2, 3);
        d.StationX.AddRange(new double[] { 205, 200, 195 });
        d.Waterlines.AddRange(new double[] { 500, 1000 }); d.WaterlinesM.AddRange(new double[] { 0.5, 1.0 });
        d.Buttocks.AddRange(new double[] { 0, 500, 1000 }); d.ButtocksM.AddRange(new double[] { 0, 0.5, 1.0 });
        d.Wl[1, 0] = new List<double> { 718 }; d.Wl[1, 1] = new List<double> { 1096, 3164 };
        d.Bt[1, 1] = new List<double> { 35, 7999, 8500 };
        d.Center[0] = new List<double> { 727, 7496, 17042 };
        d.Center[1] = new List<double> { 35, 7999, 8500 };
        d.BottomTangent[2] = 118; d.DeckSide[1] = 8264; d.SideTangent[2] = 21030;
        var labels = new List<string> { "-0.5", "F.P.", "0.5" };

        var t1 = OffsetTableFactory.CenterLineTable(d, labels, "F0", "mm");
        var t2 = OffsetTableFactory.BaseLineTable(d, labels, "F0", "mm");
        Check(t1.Title == "HEIGHT ABOVE CENTER LINE" && t2.Title == "HEIGHT ABOVE BASE LINE", "table titles");
        Check(t1.Rows.Count == 3 && t2.Rows.Count == 3, "stations are rows");
        // 반폭 표: 라벨 | BOTTOM TANGENT | WL x2 | DECK SIDE | 라벨 = 6열
        Check(t1.Headers.Count == 6 && t1.Headers[0][0] == "STATION" && t1.Headers[1][0] == "BOTTOM", "half-breadth table columns");
        Check(t1.Headers[2][0] == "0.5" && t1.Headers[2][1] == "W.L." && t1.Headers[3][0] == "1.0", "W.L. headers in meters");
        // 높이 표: 라벨 | CENTER CONTOUR | B.T. x2 (0 제외) | SIDE TANGENT | 라벨 = 6열
        Check(t2.Headers.Count == 6 && t2.Headers[1][0] == "CENTER" && t2.Headers[2][0] == "0.5" && t2.Headers[2][1] == "B.T.", "base-line table columns");
        // 복수 교점은 한 칸에 오름차순으로 쌓음
        Check(t2.Rows[0][1].Length == 3 && t2.Rows[0][1][0] == "727," && t2.Rows[0][1][1] == "7496," && t2.Rows[0][1][2] == "17042", "stacked values: comma after all but last");
        Check(t1.Rows[1][3].Length == 2 && t1.Rows[1][3][1] == "3164", "half-breadth stacked values");
        // 없는 칸: 반폭 표 '-', 높이 표 '0'
        Check(t1.Rows[0][2][0] == "-" && t2.Rows[0][2][0] == "0", "empty cells: '-' vs '0'");
        Check(t1.Rows[0][0][0] == "-0.5" && t1.Rows[1][0][0] == "F.P." && t1.Rows[1][5][0] == "F.P.", "station labels on both sides");

        string csv = OffsetTableFactory.ToCsv(t1, t2);
        Check(csv.Contains("HEIGHT ABOVE CENTER LINE") && csv.Contains("35;7999;8500"), "csv contains title and stacked values");

        // DXF: 표 그리기 -> TEXT/LINE 이 나오고 제목 행이 박스 안에 있음
        var b = new DxfBuilder();
        b.AddLayer("OFFSET_GRID", 3); b.AddLayer("OFFSET_TEXT", 7);
        t1.Draw(b, 0, 0, 10, "OFFSET_GRID", "OFFSET_TEXT", 600, 300);
        string dxf = b.Build();
        Check(dxf.Contains("HEIGHT ABOVE CENTER LINE") && dxf.Contains("TEXT") && dxf.Contains("LINE"), "dxf has table text and lines");
        Check(dxf.Contains("8264") && dxf.Contains("W.L."), "dxf has cell values");
        Check(t1.UnitsHeight() > 0 && t1.UnitsWidth() > 0, "table has size");
        var fb = new DxfBuilder(); fb.AddLayer("L", 7); fb.FilledRect("L", 5, 5, 2, 2);
        Check(fb.Build().Contains("SOLID"), "filled rect is a SOLID");

        // 척도 복사: 길이만 곱하고 머리글(m)은 그대로
        var s = d.Scaled(2.0);
        Check(s.Wl[1, 1][1] == 6328 && s.WaterlinesM[0] == 0.5 && s.BottomTangent[2] == 236, "scaled copy");

        Console.WriteLine("OffsetTables PASS " + pass + ", FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
