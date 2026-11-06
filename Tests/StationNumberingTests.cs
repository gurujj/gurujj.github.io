// 스테이션 번호 <-> 위치, F.P./A.P. 표시 시험 (라이노 없이)
using System;
using OffsetGenPlugin;

public static class StationNumberingTests
{
    static int pass = 0, fail = 0;
    static void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }

    public static int Run()
    {
        // AP = 0, FP = 200 (20등분: 간격 10)
        var fpO = new StationNumbering { Valid = true, ApX = 0, FpX = 200, Spacing = 10, FpOrigin = true };
        var apO = new StationNumbering { Valid = true, ApX = 0, FpX = 200, Spacing = 10, FpOrigin = false };

        // FP 원점: FP -> AP 로 갈수록 번호 증가, 선수 앞은 음수
        Check(fpO.Label(200) == "F.P.", "FP origin: FP label");
        Check(fpO.Label(0) == "A.P.", "FP origin: AP label");
        Check(fpO.Label(100) == "10", "FP origin: midship 10");
        Check(fpO.Label(205) == "-0.5", "FP origin: ahead of FP is -0.5");
        Check(fpO.Label(195) == "0.5", "FP origin: 0.5");
        Check(fpO.Label(5) == "19.5", "FP origin: 19.5");
        Check(Math.Abs(fpO.PositionOf(0) - 200) < 1e-9 && Math.Abs(fpO.PositionOf(20) - 0) < 1e-9, "FP origin: PositionOf");
        Check(Math.Abs(fpO.PositionOf(-0.5) - 205) < 1e-9, "FP origin: PositionOf(-0.5)");
        Check(fpO.Number(fpO.PositionOf(7.5)) == 7.5, "FP origin: round trip");

        // AP 원점: AP -> FP 로 갈수록 번호 증가
        Check(apO.Label(0) == "A.P.", "AP origin: AP label");
        Check(apO.Label(200) == "F.P.", "AP origin: FP label");
        Check(apO.Label(10) == "1", "AP origin: station 1");
        Check(apO.Label(-5) == "-0.5", "AP origin: aft of AP is -0.5");
        Check(Math.Abs(apO.PositionOf(20) - 200) < 1e-9, "AP origin: PositionOf");

        // 간격 계산 (FP - AP) / 분할 수: AP 100, FP 400, 30등분
        var odd = new StationNumbering { Valid = true, ApX = 100, FpX = 400, Spacing = 10, FpOrigin = true };
        Check(odd.Label(400) == "F.P." && odd.Label(100) == "A.P." && odd.Label(250) == "15", "non-zero AP origin offsets");

        Check(new StationNumbering { Valid = false }.Label(5) == "", "invalid numbering -> empty label");
        Console.WriteLine("StationNumbering PASS " + pass + ", FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
