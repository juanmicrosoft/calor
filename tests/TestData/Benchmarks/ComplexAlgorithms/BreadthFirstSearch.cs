using System;
using System.Collections.Generic;

namespace ComplexAlgorithms
{
    public static class BreadthFirstSearch
    {
        public static int Search(Dictionary<int, List<int>> graph, int start, int target)
        {
            var visited = new HashSet<int>();
            var queue = new Queue<(int node, int dist)>();
            queue.Enqueue((start, 0));
            visited.Add(start);
            while (queue.Count > 0)
            {
                var (node, dist) = queue.Dequeue();
                if (node == target) return dist;
                if (graph.ContainsKey(node))
                {
                    foreach (var neighbor in graph[node])
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue((neighbor, dist + 1));
                        }
                    }
                }
            }
            return -1;
        }
    }
}
