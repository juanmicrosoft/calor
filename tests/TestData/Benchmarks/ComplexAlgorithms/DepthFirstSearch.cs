using System;
using System.Collections.Generic;

namespace ComplexAlgorithms
{
    public static class DepthFirstSearch
    {
        public static List<int> Search(Dictionary<int, List<int>> graph, int start)
        {
            var visited = new HashSet<int>();
            var result = new List<int>();
            DFS(graph, start, visited, result);
            return result;
        }

        private static void DFS(Dictionary<int, List<int>> graph, int node, HashSet<int> visited, List<int> result)
        {
            if (visited.Contains(node)) return;
            visited.Add(node);
            result.Add(node);
            if (graph.ContainsKey(node))
            {
                foreach (var neighbor in graph[node])
                    DFS(graph, neighbor, visited, result);
            }
        }
    }
}
