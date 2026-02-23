using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class SortedList
    {
        private List<int> items = new List<int>();

        public void Insert(int value)
        {
            int pos = 0;
            while (pos < items.Count && items[pos] < value) pos++;
            items.Insert(pos, value);
        }

        public bool Contains(int value) => items.BinarySearch(value) >= 0;
        public int Count => items.Count;
        public int this[int index] => items[index];
    }
}
