using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OffsetGenPlugin
{
    // ---------------------------------------------------------------
    // DXF 빌더 (R12 ASCII: LINE, POLYLINE, TEXT)
    // ---------------------------------------------------------------
    public class DxfBuilder
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder ents = new StringBuilder();
        readonly List<KeyValuePair<string, int>> layers = new List<KeyValuePair<string, int>>();

        public void AddLayer(string name, int color)
        {
            layers.Add(new KeyValuePair<string, int>(name, color));
        }

        public void Line(string layer, double x1, double y1, double x2, double y2)
        {
            G(ents, 0, "LINE"); G(ents, 8, layer);
            G(ents, 10, x1); G(ents, 20, y1); G(ents, 30, 0.0);
            G(ents, 11, x2); G(ents, 21, y2); G(ents, 31, 0.0);
        }

        public void AddPolyline(string layer, List<P2> pts)
        {
            if (pts == null || pts.Count < 2) return;
            G(ents, 0, "POLYLINE"); G(ents, 8, layer);
            G(ents, 66, "1");
            G(ents, 10, 0.0); G(ents, 20, 0.0); G(ents, 30, 0.0);
            G(ents, 70, "0");
            foreach (var p in pts)
            {
                G(ents, 0, "VERTEX"); G(ents, 8, layer);
                G(ents, 10, p.X); G(ents, 20, p.Y); G(ents, 30, 0.0);
            }
            G(ents, 0, "SEQEND"); G(ents, 8, layer);
        }

        // hAlign: 0=왼쪽(기준선), 1=가운데(세로 중간), 2=오른쪽(세로 중간), 3=왼쪽(세로 중간)
        public void Text(string layer, double x, double y, double h, string text, int hAlign)
        {
            G(ents, 0, "TEXT"); G(ents, 8, layer);
            G(ents, 10, x); G(ents, 20, y); G(ents, 30, 0.0);
            G(ents, 40, h);
            G(ents, 1, Ascii(text));
            if (hAlign != 0)
            {
                int h72 = hAlign == 3 ? 0 : hAlign;
                G(ents, 72, h72.ToString(Inv));
                G(ents, 73, "2");
                G(ents, 11, x); G(ents, 21, y); G(ents, 31, 0.0);
            }
        }

        // R12 ASCII에서 깨지는 문자는 ?로 바꿈
        static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(c >= 32 && c <= 126 ? c : '?');
            return sb.ToString();
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();

            // HEADER
            G(sb, 0, "SECTION"); G(sb, 2, "HEADER");
            G(sb, 9, "$ACADVER"); G(sb, 1, "AC1009");
            G(sb, 0, "ENDSEC");

            // TABLES (선종류 + 레이어)
            G(sb, 0, "SECTION"); G(sb, 2, "TABLES");
            G(sb, 0, "TABLE"); G(sb, 2, "LTYPE"); G(sb, 70, "1");
            G(sb, 0, "LTYPE"); G(sb, 2, "CONTINUOUS"); G(sb, 70, "64");
            G(sb, 3, "Solid line"); G(sb, 72, "65"); G(sb, 73, "0"); G(sb, 40, "0.0");
            G(sb, 0, "ENDTAB");
            G(sb, 0, "TABLE"); G(sb, 2, "LAYER"); G(sb, 70, layers.Count.ToString(Inv));
            foreach (var l in layers)
            {
                G(sb, 0, "LAYER"); G(sb, 2, l.Key); G(sb, 70, "0");
                G(sb, 62, l.Value.ToString(Inv)); G(sb, 6, "CONTINUOUS");
            }
            G(sb, 0, "ENDTAB");
            G(sb, 0, "ENDSEC");

            // ENTITIES
            G(sb, 0, "SECTION"); G(sb, 2, "ENTITIES");
            sb.Append(ents.ToString());
            G(sb, 0, "ENDSEC");
            G(sb, 0, "EOF");

            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        }

        static void G(StringBuilder sb, int code, string value)
        {
            sb.Append(code.ToString(Inv)).Append("\r\n").Append(value).Append("\r\n");
        }

        static void G(StringBuilder sb, int code, double value)
        {
            G(sb, code, value.ToString("0.######", Inv));
        }
    }
}
