// 도면 배치 계산 (라이노 비의존, 시험 가능)
// 글자 크기를 먼저 정하고 -> 각 블록(표, Body plan, Sheer/Half-breadth plan, 제원, 개정란, 표제란)의 실제 크기를 구한 뒤
// -> 고정 마진으로 배치하고 -> 도면 테두리 크기는 블록들 크기에서 결정한다 (선체 길이 안에 억지로 끼워 넣지 않음).
// 좌표계: x 오른쪽 / y 위쪽, 모든 값은 출력 단위. tH = 기본 글자 높이 (모든 간격의 기준)
using System;

namespace OffsetGenPlugin
{
    public struct RectD
    {
        public double L, B, R, T;
        public RectD(double l, double b, double r, double t) { L = l; B = b; R = r; T = t; }
        public double Width { get { return R - L; } }
        public double Height { get { return T - B; } }

        // margin 만큼 띄워도 겹치는가 (margin 0 이면 맞닿기만 해도 겹치지 않음)
        public bool Intersects(RectD o, double margin)
        {
            return L < o.R + margin && o.L < R + margin && B < o.T + margin && o.B < T + margin;
        }

        public bool Contains(RectD o)
        {
            return o.L >= L - 1e-9 && o.R <= R + 1e-9 && o.B >= B - 1e-9 && o.T <= T + 1e-9;
        }
    }

    public class SheetLayoutInput
    {
        public double XMin, XMax, ZLow, ZMax, HalfBeam;   // 선체 범위 (z 아래쪽은 기선 z = 0 이하)
        public double TH;                  // 기본 글자 높이
        public double LabW;                // 시안 라벨 글자 폭 ("23.0 W.L.")
        public double LabMargin;           // 도면과 시안 라벨 사이 마진
        public double TableW1, TableH1, TableW2, TableH2;   // 표의 자연 크기 (글자 높이 고정 상태)
        public double PartW, PartH;        // 제원 블록 크기
        public double TitleW, TitleH;      // 표제란 크기
        public double RevW, RevH;          // 개정란 크기
    }

    public class SheetLayout
    {
        // 위쪽 띠
        public RectD Table1, Body, Table2;       // Body = 시안 라벨 포함한 바깥 상자
        public double Gap;                       // 세 블록 사이 마진
        public double BandBottom, BandTop;
        public double BodyBase, CxBody;          // Body plan 기선 y, 중심선 x
        public double BodyScale = 1.0;           // Body plan 확대 배율 (위쪽 띠 높이에 맞춤, 1 이상)
        // 선도
        public RectD Hull;                       // Sheer + Half-breadth plan (라벨, 번호 포함)
        public double YHB, PlanGap, LineBottom;
        // 아래쪽
        public RectD Particulars, TitleBlock, Revision;
        public RectD Frame;                      // 안쪽 테두리

        public static SheetLayout Compute(SheetLayoutInput i)
        {
            var s = new SheetLayout();
            double tH = i.TH;
            double u = i.XMax - i.XMin;
            double cx0 = (i.XMin + i.XMax) / 2.0;

            // ---- 위쪽 띠: 왼쪽 표 | Body plan | 오른쪽 표, 사이는 항상 Gap ----
            s.Gap = Math.Max(0.03 * u, 6.0 * tH);
            double hullH = i.ZMax - i.ZLow;
            double hullW = u + 2.0 * (i.LabMargin + i.LabW);

            // 위쪽 띠 높이는 표(글자 크기 고정)가 정하고, Body plan 은 그 높이를 채우도록 확대 (최소 1배, 최대 3배)
            double bodyPad = 12.0 * tH;                                  // 아래 제목 + 위 여백
            double bandH = Math.Max(hullH + bodyPad, Math.Max(i.TableH1, i.TableH2));
            s.BodyScale = Math.Max(1.0, Math.Min(3.0, (bandH - bodyPad) / hullH));
            double bodyOuterW = 2.0 * i.HalfBeam * s.BodyScale + 2.0 * (i.LabMargin + i.LabW);
            double wTop = i.TableW1 + s.Gap + bodyOuterW + s.Gap + i.TableW2;
            double contentW = Math.Max(wTop, hullW);
            double x0 = cx0 - wTop / 2.0;

            s.BandBottom = i.ZMax + 12.0 * tH;                         // Sheer plan 위 빨간 라벨 줄 자리
            s.BandTop = s.BandBottom + bandH;
            s.Table1 = new RectD(x0, s.BandBottom, x0 + i.TableW1, s.BandTop);
            s.Body = new RectD(s.Table1.R + s.Gap, s.BandBottom, s.Table1.R + s.Gap + bodyOuterW, s.BandTop);
            s.Table2 = new RectD(s.Body.R + s.Gap, s.BandBottom, s.Body.R + s.Gap + i.TableW2, s.BandTop);
            s.CxBody = (s.Body.L + s.Body.R) / 2.0;
            double bodyH = hullH * s.BodyScale;
            s.BodyBase = s.BandBottom + 5.5 * tH + (bandH - bodyPad - bodyH) / 2.0;

            // ---- 선도 (Sheer plan 아래에 Half-breadth plan) ----
            s.PlanGap = 22.0 * tH;                                       // 스테이션 번호 + 빨간 라벨 두 줄씩 자리
            s.YHB = i.ZLow - s.PlanGap - i.HalfBeam;
            s.LineBottom = s.YHB - 2.5 * tH;
            double hullBottom = s.LineBottom - 5.5 * tH;                 // 아래 스테이션 번호 두 줄까지
            s.Hull = new RectD(cx0 - hullW / 2.0, hullBottom, cx0 + hullW / 2.0, i.ZMax + 6.0 * tH);

            // ---- 제원: 가운데, 도면과 충분한 마진 (12 tH) ----
            double partTop = hullBottom - 12.0 * tH;
            s.Particulars = new RectD(cx0 - i.PartW / 2.0, partTop - i.PartH, cx0 + i.PartW / 2.0, partTop);

            // ---- 테두리: 블록들에서 결정 ----
            double side = 4.0 * tH;
            double frameL = cx0 - contentW / 2.0 - side, frameR = cx0 + contentW / 2.0 + side;
            double frameT = s.BandTop + 3.0 * tH + i.RevH + 2.0 * tH;    // 개정란은 표 단위 글자 위
            double frameB = s.Particulars.B - 8.0 * tH;
            s.Frame = new RectD(frameL, frameB, frameR, frameT);

            // 개정란 = 오른쪽 위 모서리, 표제란 = 오른쪽 아래 모서리 (테두리에 붙임)
            s.Revision = new RectD(frameR - i.RevW, frameT - i.RevH, frameR, frameT);
            s.TitleBlock = new RectD(frameR - i.TitleW, frameB, frameR, frameB + i.TitleH);
            return s;
        }
    }
}
