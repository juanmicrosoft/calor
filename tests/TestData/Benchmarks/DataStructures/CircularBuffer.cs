using System;

namespace DataStructures
{
    public class CircularBuffer<T>
    {
        private T[] buffer;
        private int head = 0, count = 0;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be positive");
            buffer = new T[capacity];
        }

        public void Write(T item)
        {
            if (count == buffer.Length) throw new InvalidOperationException("Buffer is full");
            buffer[(head + count) % buffer.Length] = item;
            count++;
        }

        public T Read()
        {
            if (count == 0) throw new InvalidOperationException("Buffer is empty");
            T item = buffer[head];
            head = (head + 1) % buffer.Length;
            count--;
            return item;
        }

        public bool IsFull => count == buffer.Length;
        public bool IsEmpty => count == 0;
        public int Count => count;
    }
}
