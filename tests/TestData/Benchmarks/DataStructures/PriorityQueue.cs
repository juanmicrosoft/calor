using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class PriorityQueue
    {
        private List<int> heap = new List<int>();

        public void Enqueue(int value)
        {
            heap.Add(value);
            BubbleUp(heap.Count - 1);
        }

        public int Dequeue()
        {
            if (heap.Count == 0) throw new InvalidOperationException("Queue is empty");
            int min = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0) BubbleDown(0);
            return min;
        }

        private void BubbleUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[i] < heap[parent]) { Swap(i, parent); i = parent; }
                else break;
            }
        }

        private void BubbleDown(int i)
        {
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left < heap.Count && heap[left] < heap[smallest]) smallest = left;
                if (right < heap.Count && heap[right] < heap[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest); i = smallest;
            }
        }

        private void Swap(int a, int b) { int t = heap[a]; heap[a] = heap[b]; heap[b] = t; }
    }
}
