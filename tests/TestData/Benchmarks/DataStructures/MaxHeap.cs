using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class MaxHeap
    {
        private List<int> data = new List<int>();

        public void Insert(int value)
        {
            data.Add(value);
            int i = data.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (data[i] > data[parent]) { (data[i], data[parent]) = (data[parent], data[i]); i = parent; }
                else break;
            }
        }

        public int ExtractMax()
        {
            if (data.Count == 0) throw new InvalidOperationException("Heap is empty");
            int max = data[0];
            data[0] = data[data.Count - 1];
            data.RemoveAt(data.Count - 1);
            Heapify(0);
            return max;
        }

        private void Heapify(int i)
        {
            int left = 2 * i + 1, right = 2 * i + 2, largest = i;
            if (left < data.Count && data[left] > data[largest]) largest = left;
            if (right < data.Count && data[right] > data[largest]) largest = right;
            if (largest != i) { (data[i], data[largest]) = (data[largest], data[i]); Heapify(largest); }
        }

        public int Peek() => data.Count > 0 ? data[0] : throw new InvalidOperationException("Heap is empty");
        public int Count => data.Count;
    }
}
