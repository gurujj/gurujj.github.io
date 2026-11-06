// OffsetGen - 순수 계산부(Core): 라이노에 의존하지 않는 점 구조체
namespace OffsetGenPlugin
{
    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
    }

    public struct P3
    {
        public double X, Y, Z;
        public P3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}
