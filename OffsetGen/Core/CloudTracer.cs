using System;
using System.Collections.Generic;

namespace OffsetGenPlugin
{
    // 곡선 위의 점 구름(x, z) -> 순서 있는 폴리라인.
    // 스테이션 단면의 "모든" 교차점과 워터라인 단면의 교차점을 함께 모으면 벌브처럼 x에 대해 z가 수직이 되는 곳도
    // 점이 촘촘히 잡힌다. 점들을 최소신장트리(MST)로 잇고 가장 긴 경로를 취해 순서를 복원한다
    // (최근접 이웃 방식과 달리 뒤로 되돌아가는 지그재그가 생기지 않음).
    public static class CloudTracer
    {
        public static List<List<P2>> Order(List<P2> input, double dedupEps, double linkMax, int minPoints)
        {
            var result = new List<List<P2>>();
            if (input == null || input.Count < 2) return result;

            // 1) 중복점 제거 (x 정렬 후 창 검사)
            var sorted = new List<P2>(input);
            sorted.Sort(delegate (P2 a, P2 b) { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Y.CompareTo(b.Y); });
            var pts = new List<P2>();
            for (int i = 0; i < sorted.Count; i++)
            {
                bool dup = false;
                for (int j = pts.Count - 1; j >= 0 && sorted[i].X - pts[j].X <= dedupEps; j--)
                    if (Dist(sorted[i], pts[j]) <= dedupEps) { dup = true; break; }
                if (!dup) pts.Add(sorted[i]);
            }
            int n = pts.Count;
            if (n < 2) return result;

            // 2) 성분별 Prim MST (간선 길이 <= linkMax)
            var comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            int nComp = 0;
            var key = new double[n];
            var parent = new int[n];

            for (int s = 0; s < n; s++)
            {
                if (comp[s] >= 0) continue;
                // Prim, 이 성분의 노드만
                var members = new List<int>();
                for (int i = 0; i < n; i++) { key[i] = double.MaxValue; parent[i] = -1; }
                key[s] = 0.0;
                var inTree = new bool[n];
                while (true)
                {
                    int u = -1; double best = double.MaxValue;
                    for (int i = 0; i < n; i++)
                        if (comp[i] < 0 && !inTree[i] && key[i] < best) { best = key[i]; u = i; }
                    if (u < 0) break;
                    inTree[u] = true; comp[u] = nComp; members.Add(u);
                    if (parent[u] >= 0) { adj[u].Add(parent[u]); adj[parent[u]].Add(u); }
                    for (int v = 0; v < n; v++)
                    {
                        if (comp[v] >= 0 || inTree[v]) continue;
                        double d = Dist(pts[u], pts[v]);
                        if (d <= linkMax && d < key[v]) { key[v] = d; parent[v] = u; }
                    }
                }
                nComp++;
            }

            // 3) 성분마다 트리의 가장 긴 경로 (지름)
            for (int c = 0; c < nComp; c++)
            {
                int any = -1, count = 0;
                for (int i = 0; i < n; i++) if (comp[i] == c) { if (any < 0) any = i; count++; }
                if (count < minPoints) continue;

                int a = Farthest(any, adj, pts, n, null);
                var prev = new int[n];
                int b = Farthest(a, adj, pts, n, prev);

                var path = new List<P2>();
                for (int v = b; v >= 0; v = prev[v]) path.Add(pts[v]);   // b -> a
                if (path.Count < minPoints) continue;
                if (path[0].X > path[path.Count - 1].X) path.Reverse();   // 선미 -> 선수
                result.Add(path);
            }
            return result;
        }

        // 트리에서 start로부터 가장 먼 노드 (prev가 주어지면 경로 복원용 부모 기록)
        static int Farthest(int start, List<int>[] adj, List<P2> pts, int n, int[] prevOut)
        {
            var dist = new double[n];
            var prev = prevOut ?? new int[n];
            for (int i = 0; i < n; i++) { dist[i] = -1.0; prev[i] = -1; }
            var stack = new Stack<int>();
            dist[start] = 0.0; stack.Push(start);
            int far = start;
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (dist[u] > dist[far]) far = u;
                foreach (int v in adj[u])
                {
                    if (dist[v] >= 0.0) continue;
                    dist[v] = dist[u] + Dist(pts[u], pts[v]);
                    prev[v] = u;
                    stack.Push(v);
                }
            }
            return far;
        }

        static double Dist(P2 a, P2 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
