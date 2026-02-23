using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class Deque<T>
    {
        private LinkedList<T> items = new LinkedList<T>();

        public void AddFront(T item) => items.AddFirst(item);
        public void AddBack(T item) => items.AddLast(item);

        public T RemoveFront()
        {
            if (items.Count == 0) throw new InvalidOperationException("Deque is empty");
            T val = items.First!.Value;
            items.RemoveFirst();
            return val;
        }

        public T RemoveBack()
        {
            if (items.Count == 0) throw new InvalidOperationException("Deque is empty");
            T val = items.Last!.Value;
            items.RemoveLast();
            return val;
        }

        public int Count => items.Count;
        public bool IsEmpty => items.Count == 0;
    }
}
