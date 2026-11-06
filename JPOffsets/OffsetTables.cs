// 오프셋 표 (스테이션 = 행, 수선/버톡 = 열) 데이터 + DXF 표 그리기 + CSV 출력
// 표 형식 (선도 도면 관행):
//   HEIGHT ABOVE CENTER LINE : STATION No. | BOTTOM TANGENT LINE | 0.5 W.L. ... | DECK SIDE LINE | STATION No.   (값 = 중심선에서의 거리 y)
//   HEIGHT ABOVE BASE LINE   : STATION No. | CENTER CONTOUR | 0.5 B.T. ... | SIDE TANGENT LINE | STATION No.   (값 = 기선에서의 높이 z)
//   한 칸에 교점이 여러 개면(벌브 등) 오름차순으로 세로로 쌓아 표시. 선체가 없는 칸: 반폭 표 '-', 높이 표 '0'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OffsetGenPlugin
{
    public class OffsetData
    {
        // 스테이션 (표의 행 순서 = 스테이션 번호 오름차순)
        public List<double> StationX = new List<double>();
        public List<double> Waterlines = new List<double>();
        public List<double> Buttocks = new List<double>();    // 0 이하는 표 열로 쓰지 않음 (CENTER CONTOUR 가 대신함)
        public List<double> WaterlinesM = new List<double>(); // 열 머리글용 (미터)
        public List<double> ButtocksM = new List<double>();

        public List<double>[,] Wl;        // [스테이션, 수선] 중심선에서의 거리 목록
        public List<double>[,] Bt;        // [스테이션, 버톡] 기선에서의 높이 목록
        public List<double>[] Center;     // [스테이션] y = 0 버톡(CENTER CONTOUR) 높이 목록
        public double?[] BottomTangent;   // [스테이션] 바닥 평탄부 끝의 반폭
        public double?[] DeckSide;        // [스테이션] 갑판 현측 가장자리의 반폭
        public double?[] SideTangent;     // [스테이션] 현측이 최대 반폭에 닿는 높이
        public double?[] SideY;           // [스테이션] 그 지점의 반폭 (= 그 스테이션의 최대 반폭), 탄젠트 점 표시용

        public OffsetData(int nSta, int nWl, int nBt)
        {
            Wl = new List<double>[nSta, nWl];
            Bt = new List<double>[nSta, nBt];
            Center = new List<double>[nSta];
            BottomTangent = new double?[nSta];
            DeckSide = new double?[nSta];
            SideTangent = new double?[nSta];
            SideY = new double?[nSta];
        }

        // 값(길이)만 배율 곱한 복사본. 머리글용 미터 값은 그대로.
        public OffsetData Scaled(double sc)
        {
            int nSta = StationX.Count, nWl = Waterlines.Count, nBt = Buttocks.Count;
            var r = new OffsetData(nSta, nWl, nBt);
            foreach (double v in StationX) r.StationX.Add(v * sc);
            foreach (double v in Waterlines) r.Waterlines.Add(v * sc);
            foreach (double v in Buttocks) r.Buttocks.Add(v * sc);
            r.WaterlinesM.AddRange(WaterlinesM);
            r.ButtocksM.AddRange(ButtocksM);
            for (int s = 0; s < nSta; s++)
            {
                for (int w = 0; w < nWl; w++) r.Wl[s, w] = ScaleList(Wl[s, w], sc);
                for (int k = 0; k < nBt; k++) r.Bt[s, k] = ScaleList(Bt[s, k], sc);
                r.Center[s] = ScaleList(Center[s], sc);
                r.BottomTangent[s] = BottomTangent[s].HasValue ? BottomTangent[s] * sc : null;
                r.DeckSide[s] = DeckSide[s].HasValue ? DeckSide[s] * sc : null;
                r.SideTangent[s] = SideTangent[s].HasValue ? SideTangent[s] * sc : null;
                r.SideY[s] = SideY[s].HasValue ? SideY[s] * sc : null;
            }
            return r;
        }

        static List<double> ScaleList(List<double> src, double sc)
        {
            if (src == null) return null;
            var r = new List<double>(src.Count);
            foreach (double v in src) r.Add(v * sc);
            return r;
        }

        // 표에 쓰는 버톡 열 번호 (0 초과만)
        public List<int> ButtockColumns()
        {
            var r = new List<int>();
            for (int k = 0; k < Buttocks.Count; k++) if (Buttocks[k] > 1e-9) r.Add(k);
            return r;
        }
    }

    // 표 하나 (머리글/칸은 모두 문자열, 칸 안의 여러 줄은 string[])
    public class OffsetTableView
    {
        public string Title = "";
        public string UnitText = "";
        public List<string[]> Headers = new List<string[]>();          // 열마다 머리글 줄들
        public List<List<string[]>> Rows = new List<List<string[]>>();  // 행 -> 열 -> 줄들

        const double CharW = 0.9;     // 글자 폭 / 글자 높이 (여유 포함)
        const double HeadLine = 1.35, RowLine = 1.3;
        const double TitleUnits = 2.6;   // 제목 행 높이 (표 안, 박스로 감쌈)

        double[] ColUnits()
        {
            int nc = Headers.Count;
            var w = new double[nc];
            for (int c = 0; c < nc; c++)
            {
                int maxLen = 1;
                foreach (var l in Headers[c]) if (l.Length > maxLen) maxLen = l.Length;
                foreach (var row in Rows)
                    foreach (var l in row[c]) if (l.Length > maxLen) maxLen = l.Length;
                w[c] = maxLen * CharW + 1.4;
            }
            return w;
        }

        double HeaderUnits()
        {
            int m = 1;
            foreach (var h in Headers) if (h.Length > m) m = h.Length;
            return m * HeadLine + 1.0;
        }

        double RowUnits(List<string[]> row)
        {
            int m = 1;
            foreach (var c in row) if (c.Length > m) m = c.Length;
            return m * RowLine + 0.7;
        }

        // 글자 높이 1 기준 크기 (실제 크기 = 값 x 글자 높이)
        public double UnitsWidth() { double s = 0; foreach (double w in ColUnits()) s += w; return s; }
        public double UnitsHeight() { double s = TitleUnits + HeaderUnits(); foreach (var r in Rows) s += RowUnits(r); return s; }

        // 좌상단 (ox, oy), 아래로 그림. th = 글자 높이.
        // targetW / targetH > 0 이고 자연 크기보다 크면 열 폭 / 행 높이를 비율로 늘려 그 크기에 맞춤 (글자 크기는 그대로)
        public void Draw(DxfBuilder b, double ox, double oy, double th, string layerGrid, string layerText,
                         double targetW = 0.0, double targetH = 0.0)
        {
            double[] cu = ColUnits();
            int nc = cu.Length;
            double natW = 0; foreach (double w in cu) natW += w * th;
            double fw = (targetW > natW) ? targetW / natW : 1.0;
            var cw = new double[nc];
            for (int c = 0; c < nc; c++) cw[c] = cu[c] * th * fw;
            double totalW = natW * fw;

            double titleH = TitleUnits * th;
            double hh = HeaderUnits() * th;
            double rowsNat = 0; foreach (var r in Rows) rowsNat += RowUnits(r) * th;
            double fr = (targetH > titleH + hh + rowsNat) ? (targetH - titleH - hh) / rowsNat : 1.0;

            // 행 위치: [0] 표 윗선, [1] 제목 행 아래, [2] 머리글 아래, 이후 각 행 아래
            var ys = new List<double>();
            double y = oy;
            ys.Add(y);
            y -= titleH; ys.Add(y);
            y -= hh; ys.Add(y);
            foreach (var r in Rows) { y -= RowUnits(r) * th * fr; ys.Add(y); }
            double totalH = oy - y;

            // 제목은 표 안 맨 윗 행 (박스), 단위는 표 오른쪽 위 바깥
            b.Text(layerText, ox + totalW / 2.0, oy - titleH / 2.0, th * 1.3, Title, 1);
            if (!string.IsNullOrEmpty(UnitText)) b.Text(layerText, ox + totalW, oy + th * 1.2, th, UnitText, 2);

            // 가로선 (제목 행 윗선/아랫선 포함 전체)
            foreach (double yy in ys) b.Line(layerGrid, ox, yy, ox + totalW, yy);
            // 세로선: 바깥 좌/우는 전체 높이, 안쪽 열 구분선은 제목 행 아래부터
            double x = ox;
            for (int c = 0; c <= nc; c++)
            {
                bool outer = (c == 0 || c == nc);
                b.Line(layerGrid, x, outer ? oy : oy - titleH, x, oy - totalH);
                if (c < nc) x += cw[c];
            }

            // 머리글
            x = ox;
            for (int c = 0; c < nc; c++)
            {
                var lines = Headers[c];
                double hTop = ys[1];
                for (int i = 0; i < lines.Length; i++)
                {
                    double ty = hTop - hh / 2.0 + (lines.Length - 1) * 0.5 * HeadLine * th - i * HeadLine * th;
                    b.Text(layerText, x + cw[c] / 2.0, ty, th, lines[i], 1);
                }
                x += cw[c];
            }

            // 칸
            for (int r = 0; r < Rows.Count; r++)
            {
                double rTop = ys[r + 2], rBot = ys[r + 3];
                double mid = (rTop + rBot) / 2.0;
                x = ox;
                for (int c = 0; c < nc; c++)
                {
                    var lines = Rows[r][c];
                    for (int i = 0; i < lines.Length; i++)
                    {
                        double ty = mid + (lines.Length - 1) * 0.5 * RowLine * th - i * RowLine * th;
                        b.Text(layerText, x + cw[c] / 2.0, ty, th, lines[i], 1);
                    }
                    x += cw[c];
                }
            }
        }
    }

    public static class OffsetTableFactory
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // 칸 문자열: 값 여러 개는 오름차순으로 한 줄씩 (같은 글자 연속은 하나로). 없으면 empty
        static string[] Cell(List<double> v, string fmt, string empty)
        {
            if (v == null || v.Count == 0) return new[] { empty };
            var r = new List<string>();
            foreach (double x in v)
            {
                string t = x.ToString(fmt, Inv);
                if (t == "-0" || t == "-0.0" || t == "-0.00" || t == "-0.000") t = t.Substring(1);
                if (r.Count == 0 || r[r.Count - 1] != t) r.Add(t);
            }
            return r.ToArray();
        }

        static string[] Cell(double? v, string fmt, string empty)
        {
            if (!v.HasValue) return new[] { empty };
            return new[] { v.Value.ToString(fmt, Inv) };
        }

        public static string Head(double v) { return v.ToString("0.0##", Inv); }

        // HEIGHT ABOVE CENTER LINE: 중심선에서의 거리 (반폭)
        public static OffsetTableView CenterLineTable(OffsetData d, List<string> rowLabels, string fmt, string unitName)
        {
            var t = new OffsetTableView { Title = "HEIGHT ABOVE CENTER LINE", UnitText = "Unit : " + unitName };
            t.Headers.Add(new[] { "STATION", "No." });
            t.Headers.Add(new[] { "BOTTOM", "TANGENT", "LINE" });
            for (int w = 0; w < d.Waterlines.Count; w++) t.Headers.Add(new[] { Head(d.WaterlinesM[w]), "W.L." });
            t.Headers.Add(new[] { "DECK", "SIDE", "LINE" });
            t.Headers.Add(new[] { "STATION", "No." });
            for (int s = 0; s < d.StationX.Count; s++)
            {
                var row = new List<string[]>();
                row.Add(new[] { rowLabels[s] });
                row.Add(Cell(d.BottomTangent[s], fmt, "-"));
                for (int w = 0; w < d.Waterlines.Count; w++) row.Add(Cell(d.Wl[s, w], fmt, "-"));
                row.Add(Cell(d.DeckSide[s], fmt, "-"));
                row.Add(new[] { rowLabels[s] });
                t.Rows.Add(row);
            }
            return t;
        }

        // HEIGHT ABOVE BASE LINE: 기선에서의 높이
        public static OffsetTableView BaseLineTable(OffsetData d, List<string> rowLabels, string fmt, string unitName)
        {
            var t = new OffsetTableView { Title = "HEIGHT ABOVE BASE LINE", UnitText = "Unit : " + unitName };
            var cols = d.ButtockColumns();
            t.Headers.Add(new[] { "STATION", "No." });
            t.Headers.Add(new[] { "CENTER", "CONTOUR" });
            foreach (int k in cols) t.Headers.Add(new[] { Head(d.ButtocksM[k]), "B.T." });
            t.Headers.Add(new[] { "SIDE", "TANGENT", "LINE" });
            t.Headers.Add(new[] { "STATION", "No." });
            for (int s = 0; s < d.StationX.Count; s++)
            {
                var row = new List<string[]>();
                row.Add(new[] { rowLabels[s] });
                row.Add(Cell(d.Center[s], fmt, "0"));
                foreach (int k in cols) row.Add(Cell(d.Bt[s, k], fmt, "0"));
                row.Add(Cell(d.SideTangent[s], fmt, "0"));
                row.Add(new[] { rowLabels[s] });
                t.Rows.Add(row);
            }
            return t;
        }

        // CSV: 같은 형식의 표 두 개 (한 칸의 여러 값은 ; 로 구분)
        public static string ToCsv(OffsetTableView t1, OffsetTableView t2)
        {
            var sb = new StringBuilder();
            AppendCsv(sb, t1);
            sb.AppendLine();
            AppendCsv(sb, t2);
            return sb.ToString();
        }

        static void AppendCsv(StringBuilder sb, OffsetTableView t)
        {
            sb.AppendLine(t.Title + "," + t.UnitText);
            var h = new List<string>();
            foreach (var c in t.Headers) h.Add(string.Join(" ", c));
            sb.AppendLine(string.Join(",", h.ToArray()));
            foreach (var row in t.Rows)
            {
                var cells = new List<string>();
                foreach (var c in row) cells.Add(string.Join(";", c));
                sb.AppendLine(string.Join(",", cells.ToArray()));
            }
        }
    }
}
