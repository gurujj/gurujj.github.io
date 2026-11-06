// 스테이션 번호 <-> x 위치 (라이노 비의존, 시험 가능)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OffsetGenPlugin
{
    public class StationNumbering
    {
        public bool Valid;
        public double ApX;
        public double FpX;
        public double Spacing = 1.0;   // 스테이션 1개의 간격 (x 길이 단위)
        public bool FpOrigin = true;   // true: FP = 0, 번호가 선미(AP) 쪽으로 증가 (도면 방식) / false: AP = 0, 선수 쪽으로 증가

        // 스테이션 번호 (소수 4자리 반올림). x 위치 -> 번호
        public double Number(double x)
        {
            double n = FpOrigin ? (FpX - x) / Spacing : (x - ApX) / Spacing;
            n = Math.Round(n, 4);
            if (Math.Abs(n) < 1e-9) n = 0.0;
            return n;
        }

        // 번호 -> x 위치
        public double PositionOf(double no)
        {
            return FpOrigin ? FpX - no * Spacing : ApX + no * Spacing;
        }

        // 표시 문자열: AP, FP 위치는 "A.P." / "F.P.", 나머지는 번호
        public string Label(double x)
        {
            if (!Valid || Spacing <= 0) return "";
            double n = Number(x);
            double div = (FpX - ApX) / Spacing;
            bool atZero = Math.Abs(n) < 1e-9;
            bool atEnd = Math.Abs(n - div) < 1e-6;
            if (atZero) return FpOrigin ? "F.P." : "A.P.";
            if (atEnd) return FpOrigin ? "A.P." : "F.P.";
            return n.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
