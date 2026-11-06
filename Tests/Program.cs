// 콘솔 시험 진입점: 종료 코드 0 = 모두 통과
public static class Program
{
    public static int Main()
    {
        int r = 0;
        r |= AdaptiveSamplerTests.Run();
        r |= PolyToolsTests.Run();
        r |= StationNumberingTests.Run();
        r |= OffsetTablesTests.Run();
        return r == 0 ? 0 : 1;
    }
}
