using System;

namespace DataStructures
{
    public class LinkedList
    {
        private class Node
        {
            public int Value { get; set; }
            public Node Next { get; set; }

            public Node(int value, Node next = null)
            {
                Value = value;
                Next = next;
            }
        }

        private Node head = null;
        private Node tail = null;
        private int size = 0;

        public void AddFirst(int value)
        {
            var newNode = new Node(value, head);
            head = newNode;
            if (size == 0)
                tail = newNode;
            size++;
        }

        public void AddLast(int value)
        {
            var newNode = new Node(value);
            if (size == 0)
            {
                head = newNode;
                tail = newNode;
            }
            else
            {
                tail.Next = newNode;
                tail = newNode;
            }
            size++;
        }

        public int RemoveFirst()
        {
            if (size <= 0)
                throw new InvalidOperationException("List is empty");

            var value = head.Value;
            head = head.Next;
            size--;
            if (size == 0)
                tail = null;
            return value;
        }

        public int GetFirst()
        {
            if (size <= 0)
                throw new InvalidOperationException("List is empty");
            return head.Value;
        }

        public int GetLast()
        {
            if (size <= 0)
                throw new InvalidOperationException("List is empty");
            return tail.Value;
        }

        public int Size()
        {
            return size;
        }

        public bool IsEmpty()
        {
            return size == 0;
        }
    }
}
