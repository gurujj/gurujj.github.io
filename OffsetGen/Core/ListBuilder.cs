using System;
using System.Collections.Generic;
using System.Globalization;

namespace OffsetGenPlugin
{
    // 스테이션 / 워터라인 / 버톡 목록 만들기 (입력 창 문자열 -> 숫자 목록)
    public static class ListBuilder
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly char[] Seps = new[] { ' ', ',', ';', '\t', '\r', '\n' };

        // 쉼표/공백/세미콜론 구분 숫자 목록. 형식 오류면 null
        public static List<double> ParseNumbers(string text)
        {
            var r = new List<double>();
            if (string.IsNullOrWhiteSpace(text)) return r;
            foreach (var part in text.Split(Seps, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (!double.TryParse(part, NumberStyles.Float, Inv, out v)) return null;
                r.Add(v);
            }
            return r;
        }

        // 범위(시작/끝/간격)와 추가 값을 합쳐 오름차순 정렬, 중복 제거.
        // 범위 칸이 모두 비어 있으면 추가 값만 사용. 형식 오류면 null.
        // 끝값이 간격의 정확한 배수가 아니면 끝값은 포함되지 않음.
        public static List<double> ParseRange(string extra, string start, string end, string step)
        {
            var result = new List<double>();

            bool anyRange = !(string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end) &&
                              string.IsNullOrWhiteSpace(step));
            if (anyRange)
            {
                double a, b, d;
                if (!double.TryParse(start, NumberStyles.Float, Inv, out a)) return null;
                if (!double.TryParse(end, NumberStyles.Float, Inv, out b)) return null;
                if (!double.TryParse(step, NumberStyles.Float, Inv, out d)) return null;
                if (d <= 0 || b < a) return null;

                int n = (int)Math.Floor((b - a) / d + 1e-9);
                if (n > 5000) return null;
                for (int i = 0; i <= n; i++) result.Add(a + d * i);
            }

            var more = ParseNumbers(extra);
            if (more == null) return null;
            result.AddRange(more);

            return SortUnique(result, 1e-9);
        }

        public static List<double> SortUnique(List<double> v, double eps)
        {
            v.Sort();
            for (int k = v.Count - 1; k > 0; k--)
                if (Math.Abs(v[k] - v[k - 1]) < eps) v.RemoveAt(k);
            return v;
        }

        // 스테이션 번호(시작..끝, 1 간격) + 추가 번호 -> 정렬/중복 제거 -> x = AP + 번호 * 간격
        // 오류면 null을 돌려주고 error에 이유를 적음
        public static List<double> BuildStations(double ap, double spacing, double startNo, double endNo,
                                                 string customNumbers, out string error)
        {
            error = null;
            if (spacing <= 0) { error = "스테이션 간격은 0보다 커야 합니다."; return null; }
            if (endNo < startNo) { error = "시작 스테이션 번호가 끝보다 클 수 없습니다."; return null; }

            int count = (int)Math.Floor(endNo - startNo + 1e-9) + 1;
            if (count > 2000) { error = "스테이션이 너무 많습니다."; return null; }

            var numbers = new List<double>();
            for (int i = 0; i < count; i++) numbers.Add(startNo + i);

            var extra = ParseNumbers(customNumbers);
            if (extra == null) { error = "추가 스테이션 번호를 확인하세요."; return null; }
            numbers.AddRange(extra);

            SortUnique(numbers, 1e-6);
            var xs = new List<double>();
            foreach (double no in numbers) xs.Add(ap + no * spacing);
            return xs;
        }
    }
}
