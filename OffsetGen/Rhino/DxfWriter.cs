using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace OffsetGenPlugin
{
    // ---------------------------------------------------------------
    // DXF 도면 작성 (오프셋 표 + 선도 + 표제란)
    // ---------------------------------------------------------------
    public static class DxfWriter
    {
        const string LayerGrid = "OFFSET_GRID";
        const string LayerText = "OFFSET_TEXT";
        const string LinesGrid = "LINES_GRID";
        const string LinesStation = "LINES_STATION";
        const string LinesWaterline = "LINES_WATERLINE";
        const string LinesButtock = "LINES_BUTTOCK";
        const string LinesProfile = "LINES_PROFILE";
        const string LinesDeck = "LINES_DECK";
        const string LinesRef = "LINES_REF";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // lines가 null이면 표만 그림
        public static void Write(string path, List<double> stations, List<double> waterlines,
                                 double?[,] wlTable, List<double> buttocks, double?[,] btTable,
                                 string fmt, double textH, StationNumbering num, LinesData lines,
                                 string unitName)
        {
            var b = new DxfBuilder();
            b.AddLayer(LayerGrid, 7);
            b.AddLayer(LayerText, 3);
            b.AddLayer(LinesGrid, 8);
            b.AddLayer(LinesStation, 5);
            b.AddLayer(LinesWaterline, 1);
            b.AddLayer(LinesButtock, 4);
            b.AddLayer(LinesProfile, 2);
            b.AddLayer(LinesDeck, 6);
            b.AddLayer(LinesRef, 30);

            double ox = 0.0, oy = 0.0;
            bool hasLines = lines != null && lines.Bbox.IsValid;
            double gap = 0.0;
            if (hasLines)
            {
                double length = lines.Bbox.Max.X - lines.Bbox.Min.X;
                gap = 0.1 * length + 8.0 * textH;
                DrawLines(b, lines, num, stations, waterlines, buttocks, fmt, textH, gap);
                // 표는 Sheer plan 아래
                ox = lines.Bbox.Min.X;
                oy = lines.Bbox.Min.Z - gap;
            }

            double w, h;

            // 표 1: 워터라인 반폭
            var wlLabels = new List<string>();
            foreach (double z in waterlines) wlLabels.Add(z.ToString(fmt, Inv));
            string unitTag = string.IsNullOrEmpty(unitName) ? "" : " (UNIT: " + unitName.ToUpperInvariant() + ")";
            DrawTable(b, ox, oy, "OFFSET TABLE - HALF-BREADTH AT WATERLINES" + unitTag,
                      MakeHeaders(stations, num, fmt, "WL \\ ST"), wlLabels, wlTable, fmt, textH, out w, out h);
            double nextY = oy - h - 5.0 * textH;

            // 표 2: 버톡 높이
            if (btTable != null && buttocks != null && buttocks.Count > 0)
            {
                var btLabels = new List<string>();
                foreach (double y in buttocks) btLabels.Add(y.ToString(fmt, Inv));
                DrawTable(b, ox, nextY, "OFFSET TABLE - HEIGHT AT BUTTOCKS" + unitTag,
                          MakeHeaders(stations, num, fmt, "BT \\ ST"), btLabels, btTable, fmt, textH, out w, out h);
                nextY = nextY - h - 5.0 * textH;
            }

            // 표제란 + 주요 치수
            if (hasLines)
            {
                var rows = new List<string[]>();
                rows.Add(new[] { "SHIP", string.IsNullOrEmpty(lines.ShipName) ? "-" : lines.ShipName });
                rows.Add(new[] { "DRAWING", "LINES PLAN" });
                rows.Add(new[] { "SCALE", string.IsNullOrEmpty(lines.ScaleText) ? "-" : lines.ScaleText });
                rows.Add(new[] { "DATE", DateTime.Now.ToString("yyyy-MM-dd", Inv) });
                rows.Add(new[] { "UNIT", string.IsNullOrEmpty(unitName) ? "-" : unitName.ToUpperInvariant() });
                rows.Add(new[] { "LOA", (lines.Bbox.Max.X - lines.Bbox.Min.X).ToString("0.###", Inv) });
                if (num != null && num.Valid)
                    rows.Add(new[] { "LPP (AP-FP)", (num.FpX - num.ApX).ToString("0.###", Inv) });
                double beam = 2.0 * Math.Max(Math.Abs(lines.Bbox.Min.Y), Math.Abs(lines.Bbox.Max.Y));
                rows.Add(new[] { "B (MAX)", beam.ToString("0.###", Inv) });
                rows.Add(new[] { "D (FROM BL)", (lines.Bbox.Max.Z - lines.Bbox.Min.Z).ToString("0.###", Inv) });
                rows.Add(new[] { "NOTE", "DIMENSIONS FROM MODEL BBOX" });
                DrawInfoBlock(b, ox, nextY, rows, textH);
            }

            b.Save(path);
        }

        // 스테이션 번호(있으면) + 스테이션 x 값 헤더 행들
        static List<string[]> MakeHeaders(List<double> stations, StationNumbering num, string fmt, string corner)
        {
            int nCols = stations.Count + 1;
            var rows = new List<string[]>();
            if (num != null && num.Valid)
            {
                var r0 = new string[nCols];
                r0[0] = "ST NO.";
                for (int s = 0; s < stations.Count; s++) r0[s + 1] = num.Label(stations[s]);
                rows.Add(r0);
            }
            var r1 = new string[nCols];
            r1[0] = corner;
            for (int s = 0; s < stations.Count; s++) r1[s + 1] = stations[s].ToString(fmt, Inv);
            rows.Add(r1);
            return rows;
        }

        // -------------------- 표 --------------------
        static void DrawTable(DxfBuilder b, double ox, double oy, string title,
                              List<string[]> headers, List<string> rowLabels, double?[,] values,
                              string fmt, double textH, out double totalW, out double totalH)
        {
            int nHead = headers.Count;
            int nBody = rowLabels.Count;
            int nRows = nHead + nBody;
            int nCols = headers[0].Length;

            var cells = new string[nRows, nCols];
            for (int r = 0; r < nHead; r++)
                for (int c = 0; c < nCols; c++) cells[r, c] = headers[r][c];
            for (int r = 0; r < nBody; r++)
            {
                cells[nHead + r, 0] = rowLabels[r];
                for (int c = 1; c < nCols; c++)
                    cells[nHead + r, c] = values[r, c - 1].HasValue ? values[r, c - 1].Value.ToString(fmt, Inv) : "";
            }

            int maxLen = 1;
            foreach (var cell in cells) if (cell != null && cell.Length > maxLen) maxLen = cell.Length;
            double rowH = textH * 2.0;
            double colW = textH * 0.9 * maxLen + textH * 1.5;
            totalW = colW * nCols;
            totalH = rowH * nRows;

            // 제목
            b.Text(LayerText, ox, oy + textH * 1.5, textH * 1.2, title, 0);

            // 표의 좌상단이 (ox, oy), 아래로 내려감
            for (int r = 0; r <= nRows; r++)
                b.Line(LayerGrid, ox, oy - rowH * r, ox + totalW, oy - rowH * r);
            for (int c = 0; c <= nCols; c++)
                b.Line(LayerGrid, ox + colW * c, oy, ox + colW * c, oy - totalH);

            for (int r = 0; r < nRows; r++)
                for (int c = 0; c < nCols; c++)
                {
                    string t = cells[r, c];
                    if (string.IsNullOrEmpty(t)) continue;
                    b.Text(LayerText, ox + colW * c + colW / 2.0, oy - rowH * r - rowH / 2.0, textH, t, 1);
                }
        }

        // -------------------- 표제란 / 주요 치수 --------------------
        static void DrawInfoBlock(DxfBuilder b, double ox, double oy, List<string[]> rows, double tH)
        {
            int maxLen = 1;
            int maxKey = 1;
            foreach (var r in rows)
            {
                if (r[1].Length > maxLen) maxLen = r[1].Length;
                if (r[0].Length > maxKey) maxKey = r[0].Length;
            }
            double rowH = tH * 2.0;
            double keyW = tH * 0.9 * maxKey + tH * 2.0;
            double valW = tH * 0.9 * maxLen + tH * 2.0;
            double totalW = keyW + valW;
            double totalH = rowH * rows.Count;

            for (int r = 0; r <= rows.Count; r++)
                b.Line(LayerGrid, ox, oy - rowH * r, ox + totalW, oy - rowH * r);
            b.Line(LayerGrid, ox, oy, ox, oy - totalH);
            b.Line(LayerGrid, ox + keyW, oy, ox + keyW, oy - totalH);
            b.Line(LayerGrid, ox + totalW, oy, ox + totalW, oy - totalH);

            for (int r = 0; r < rows.Count; r++)
            {
                double ty = oy - rowH * r - rowH / 2.0;
                b.Text(LayerText, ox + tH * 0.5, ty, tH, rows[r][0], 3);
                b.Text(LayerText, ox + keyW + tH * 0.5, ty, tH, rows[r][1], 3);
            }
        }

        // -------------------- 선도 --------------------
        static void DrawLines(DxfBuilder b, LinesData d, StationNumbering num,
                              List<double> stations, List<double> waterlines, List<double> buttocks,
                              string fmt, double tH, double gap)
        {
            double xMin = d.Bbox.Min.X, xMax = d.Bbox.Max.X;
            double zMin = d.Bbox.Min.Z, zMax = d.Bbox.Max.Z;

            double ySign = CurveUtil.YSign(d.Bbox);
            double halfBeam = Math.Max(Math.Abs(d.Bbox.Min.Y), Math.Abs(d.Bbox.Max.Y));
            if (halfBeam < 1e-9) halfBeam = (xMax - xMin) * 0.1;

            double xMid = (xMin + xMax) / 2.0;
            double yHB = zMax + gap;                    // Half-breadth plan 중심선의 y
            double cxBody = xMax + gap + halfBeam;      // Body plan 중심선의 x
            bool numbered = num != null && num.Valid;

            // ---- 뷰 제목 ----
            b.Text(LayerText, xMin, zMax + tH * 3.5, tH * 1.2, "SHEER PLAN", 0);
            b.Text(LayerText, xMin, yHB + halfBeam + tH * 3.5, tH * 1.2, "HALF-BREADTH PLAN", 0);
            b.Text(LayerText, cxBody - halfBeam, zMax + tH * 3.5, tH * 1.2, "BODY PLAN", 0);
            b.Text(LayerText, cxBody - halfBeam / 2.0, zMax + tH * 1.2, tH, "AFT", 1);
            b.Text(LayerText, cxBody + halfBeam / 2.0, zMax + tH * 1.2, tH, "FWD", 1);

            // ---- 기준선 / 중심선 ----
            b.Line(LinesRef, xMin, zMin, xMax, zMin);                       // BL (Sheer plan)
            b.Text(LayerText, xMax + tH * 0.5, zMin, tH, "BL", 3);
            b.Line(LinesRef, xMin, yHB, xMax, yHB);                         // CL (Half-breadth plan)
            b.Text(LayerText, xMax + tH * 0.5, yHB, tH, "CL", 3);
            b.Line(LinesRef, cxBody, zMin, cxBody, zMax);                   // CL (Body plan)
            b.Text(LayerText, cxBody, zMax + tH * 1.2 + tH * 1.6, tH, "CL", 1);

            // ---- 스테이션 격자 + 라벨 ----
            foreach (double x in stations)
            {
                b.Line(LinesGrid, x, zMin, x, zMax);
                b.Line(LinesGrid, x, yHB, x, yHB + halfBeam);
                if (numbered)
                {
                    b.Text(LayerText, x, zMin - tH * 1.5, tH, num.Label(x), 1);
                    b.Text(LayerText, x, zMin - tH * 3.2, tH * 0.7, x.ToString(fmt, Inv), 1);
                }
                else
                {
                    b.Text(LayerText, x, zMin - tH * 1.5, tH, x.ToString(fmt, Inv), 1);
                }
            }

            // ---- AP / FP ----
            if (numbered)
            {
                b.Line(LinesRef, num.ApX, zMin, num.ApX, zMax);
                b.Line(LinesRef, num.FpX, zMin, num.FpX, zMax);
                b.Line(LinesRef, num.ApX, yHB, num.ApX, yHB + halfBeam);
                b.Line(LinesRef, num.FpX, yHB, num.FpX, yHB + halfBeam);
                b.Text(LayerText, num.ApX, zMax + tH * 1.2, tH, "AP", 1);
                b.Text(LayerText, num.FpX, zMax + tH * 1.2, tH, "FP", 1);
            }

            // ---- 워터라인 격자 (Sheer, Body plan) + 라벨 ----
            foreach (double z in waterlines)
            {
                b.Line(LinesGrid, xMin, z, xMax, z);
                b.Line(LinesGrid, cxBody - halfBeam, z, cxBody + halfBeam, z);
                b.Text(LayerText, xMin - tH * 0.5, z, tH, z.ToString(fmt, Inv), 2);
                b.Text(LayerText, cxBody + halfBeam + tH * 0.5, z, tH, z.ToString(fmt, Inv), 3);
            }

            // ---- 버톡 격자 (Half-breadth, Body plan) + 라벨 ----
            if (buttocks != null)
                foreach (double yb in buttocks)
                {
                    b.Line(LinesGrid, xMin, yHB + yb, xMax, yHB + yb);
                    b.Text(LayerText, xMin - tH * 0.5, yHB + yb, tH, yb.ToString(fmt, Inv), 2);
                    b.Line(LinesGrid, cxBody + yb, zMin, cxBody + yb, zMax);
                    b.Line(LinesGrid, cxBody - yb, zMin, cxBody - yb, zMax);
                    b.Text(LayerText, cxBody + yb, zMin - tH * 1.5, tH, yb.ToString(fmt, Inv), 1);
                    b.Text(LayerText, cxBody - yb, zMin - tH * 1.5, tH, yb.ToString(fmt, Inv), 1);
                }

            // ---- Sheer plan: 프로파일 (y=0 단면), 없으면 단면 최저점으로 바닥선 대체 ----
            bool profileDrawn = false;
            foreach (var pts in CurveUtil.Prepare(d.Profile, ySign, d.Tol, false))
            {
                var pl = new List<P2>();
                foreach (var p in pts) pl.Add(new P2(p.X, p.Z));
                b.AddPolyline(LinesProfile, pl);
                profileDrawn = true;
            }
            if (!profileDrawn && d.Outline != null && d.Outline.Count >= 2)
            {
                var keel = new List<P2>();
                foreach (var o in d.Outline) keel.Add(new P2(o.X, o.ZBot));
                b.AddPolyline(LinesProfile, keel);
            }

            // ---- 갑판선(Sheer line): Sheer plan + Half-breadth plan ----
            if (d.DeckLine && d.Outline != null && d.Outline.Count >= 2)
            {
                var sheer = new List<P2>();
                var deckEdge = new List<P2>();
                foreach (var o in d.Outline)
                {
                    sheer.Add(new P2(o.X, o.ZTop));
                    deckEdge.Add(new P2(o.X, yHB + o.YTop));
                }
                b.AddPolyline(LinesDeck, sheer);
                b.AddPolyline(LinesDeck, deckEdge);
            }

            // ---- Sheer plan: 버톡 커브 ----
            if (d.Buttocks != null)
                foreach (var bs in d.Buttocks)
                    foreach (var pts in bs.Lines)
                    {
                        var pl = new List<P2>();
                        foreach (var p in pts) pl.Add(new P2(p.X, p.Z));
                        b.AddPolyline(LinesButtock, pl);
                    }

            // ---- Half-breadth plan: 워터라인 커브 ----
            foreach (var ws in d.Waterlines)
                foreach (var pts in CurveUtil.Prepare(ws.Curves, ySign, d.Tol, true))
                {
                    var pl = new List<P2>();
                    foreach (var p in pts) pl.Add(new P2(p.X, yHB + p.Y * ySign));
                    b.AddPolyline(LinesWaterline, pl);
                }

            // ---- Body plan: 스테이션 단면 (선수 쪽은 오른쪽, 선미 쪽은 왼쪽) ----
            foreach (var ss in d.Stations)
            {
                double sgn = ss.X >= xMid ? 1.0 : -1.0;
                foreach (var pts in CurveUtil.Prepare(ss.Curves, ySign, d.Tol, true))
                {
                    var pl = new List<P2>();
                    foreach (var p in pts) pl.Add(new P2(cxBody + sgn * p.Y * ySign, p.Z));
                    b.AddPolyline(LinesStation, pl);
                }
            }
        }
    }
}
