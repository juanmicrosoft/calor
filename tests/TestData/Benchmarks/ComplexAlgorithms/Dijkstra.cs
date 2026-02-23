using System;
using System.Collections.Generic;

namespace ComplexAlgorithms
{
    public static class Dijkstra
    {
        public static int[] ShortestPaths(Dictionary<int, List<(int to, int weight)>> graph, int start, int nodeCount)
        {
            var dist = new int[nodeCount];
            var visited = new bool[nodeCount];
            Array.Fill(dist, int.MaxValue);
            dist[start] = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                int u = -1;
                for (int v = 0; v < nodeCount; v++)
                    if (!visited[v] && (u == -1 || dist[v] < dist[u])) u = v;
                if (dist[u] == int.MaxValue) break;
                visited[u] = true;
                if (graph.ContainsKey(u))
                    foreach (var (to, w) in graph[u])
                        if (dist[u] + w < dist[to])
                            dist[to] = dist[u] + w;
            }
            return dist;
        }
    }
}
