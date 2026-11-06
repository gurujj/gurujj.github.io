using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OffsetGenPlugin
{
    // 선체 범위(바운딩 박스)에서 입력 창 기본값을 계산 (라이노 비의존)
    public class AutoDefaults
    {
        public string Ap, Fp, Spacing, StartNo, EndNo;
        public string WlStart, WlEnd, WlStep;
        public string BtStart, BtEnd, BtStep;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // 범위를 target개 안팎으로 나누는 "깔끔한" 간격 (1, 2, 2.5, 5 x 10^k)
        public static double NiceStep(double range, double target)
        {
            if (range <= 0 || target <= 0) return 1.0;
            double raw = range / target;
            double mag = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
            double[] c = { 1.0, 2.0, 2.5, 5.0, 10.0 };
            foreach (double m in c)
                if (m * mag >= raw * 0.8) return m * mag;   // 목표 개수보다 최대 25% 더 촘촘한 간격까지 허용
            return 10.0 * mag;
        }

        static string F(double v)
        {
            if (Math.Abs(v) < 1e-12) v = 0.0;
            return Math.Round(v, 6).ToString("0.######", Inv);
        }

        public static AutoDefaults Compute(double minX, double maxX, double minZ, double maxZ, double halfBeam)
        {
            var d = new AutoDefaults();
            double len = maxX - minX;
            d.Ap = F(minX);
            d.Fp = F(maxX);
            d.Spacing = F(len > 0 ? len / 20.0 : 1.0);
            d.StartNo = "0";
            d.EndNo = "20";

            double h = maxZ - minZ;
            double wlStep = NiceStep(h, 20.0);
            double wlStart = Math.Floor(minZ / wlStep + 1e-9) * wlStep;
            double wlEnd = Math.Floor(maxZ / wlStep + 1e-9) * wlStep;
            d.WlStart = F(wlStart); d.WlEnd = F(wlEnd); d.WlStep = F(wlStep);

            double btStep = NiceStep(halfBeam, 12.0);
            double btEnd = Math.Floor(halfBeam * 0.999 / btStep) * btStep;   // 최대 폭보다 작게
            if (btEnd < 0) btEnd = 0;
            d.BtStart = "0"; d.BtEnd = F(btEnd); d.BtStep = F(btStep);
            return d;
        }
    }

    // 간단한 key=value 직렬화 (마지막 입력 저장용)
    public static class KeyValueCodec
    {
        public static string Serialize(IDictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d)
                sb.Append(Esc(kv.Key)).Append('\t').Append(Esc(kv.Value)).Append('\n');
            return sb.ToString();
        }

        public static Dictionary<string, string> Parse(string text)
        {
            var r = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return r;
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                int t = line.IndexOf('\t');
                if (t <= 0) continue;
                r[Unesc(line.Substring(0, t))] = Unesc(line.Substring(t + 1));
            }
            return r;
        }

        static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // 선체 서명(범위)이 같은지: 저장값은 같은 선체일 때만 복원
        public static string Signature(double minX, double maxX, double minZ, double maxZ, double halfBeam)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.####};{1:0.####};{2:0.####};{3:0.####};{4:0.####}",
                                 minX, maxX, minZ, maxZ, halfBeam);
        }
    }
}
