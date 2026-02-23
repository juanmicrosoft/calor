using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class MinStack
    {
        private Stack<int> stack = new Stack<int>();
        private Stack<int> minStack = new Stack<int>();

        public void Push(int val)
        {
            stack.Push(val);
            if (minStack.Count == 0 || val <= minStack.Peek())
                minStack.Push(val);
        }

        public int Pop()
        {
            if (stack.Count == 0) throw new InvalidOperationException("Stack is empty");
            int val = stack.Pop();
            if (val == minStack.Peek()) minStack.Pop();
            return val;
        }

        public int Min() => minStack.Count > 0 ? minStack.Peek() : throw new InvalidOperationException("Stack is empty");
        public int Count => stack.Count;
    }
}
