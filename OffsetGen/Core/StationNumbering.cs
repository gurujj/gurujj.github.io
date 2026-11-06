using System;
using System.Globalization;

namespace OffsetGenPlugin
{
    // 스테이션 x 위치 -> 번호 라벨 (AP = 0)
    public class StationNumbering
    {
        public bool Valid;
        public double ApX;
        public double FpX;
        public double Spacing = 1.0;   // 스테이션 1개의 간격 (x 길이 단위)

        public string Label(double x)
        {
            if (!Valid || Spacing <= 0) return "";
            double n = (x - ApX) / Spacing;
            n = Math.Round(n, 4);
            if (Math.Abs(n) < 1e-9) n = 0.0;
            return n.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
