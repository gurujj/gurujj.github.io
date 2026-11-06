// 콘솔 시험 진입점: 종료 코드 0 = 모두 통과
public static class Program
{
    public static int Main()
    {
        int a = AdaptiveSamplerTests.Run();
        int b = PolyToolsTests.Run();
        return (a == 0 && b == 0) ? 0 : 1;
    }
}
