using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class Queue
    {
        private List<int> items = new List<int>();

        public void Enqueue(int item)
        {
            items.Add(item);
        }

        public int Dequeue()
        {
            if (items.Count <= 0)
                throw new InvalidOperationException("Queue is empty");

            var result = items[0];
            items.RemoveAt(0);
            return result;
        }

        public int Peek()
        {
            if (items.Count <= 0)
                throw new InvalidOperationException("Queue is empty");

            return items[0];
        }

        public bool IsEmpty()
        {
            return items.Count == 0;
        }

        public int Count()
        {
            return items.Count;
        }
    }
}
