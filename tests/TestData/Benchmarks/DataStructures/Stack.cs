using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class Stack
    {
        private List<int> items = new List<int>();
        private int count = 0;

        public void Push(int item)
        {
            items.Add(item);
            count++;
        }

        public int Pop()
        {
            if (count <= 0)
                throw new InvalidOperationException("Stack is empty");

            var result = items[count - 1];
            items.RemoveAt(count - 1);
            count--;
            return result;
        }

        public int Peek()
        {
            if (count <= 0)
                throw new InvalidOperationException("Stack is empty");

            return items[count - 1];
        }

        public bool IsEmpty()
        {
            return count == 0;
        }

        public int Count()
        {
            return count;
        }
    }
}
