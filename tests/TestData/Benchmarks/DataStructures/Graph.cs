using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class Graph
    {
        private Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();

        public void AddNode(int node)
        {
            if (!adjacency.ContainsKey(node))
                adjacency[node] = new List<int>();
        }

        public void AddEdge(int from, int to)
        {
            AddNode(from);
            AddNode(to);
            adjacency[from].Add(to);
        }

        public List<int> Neighbors(int node) =>
            adjacency.ContainsKey(node) ? adjacency[node] : new List<int>();

        public int Degree(int node) => Neighbors(node).Count;
        public int NodeCount => adjacency.Count;
        public bool HasNode(int node) => adjacency.ContainsKey(node);
    }
}
