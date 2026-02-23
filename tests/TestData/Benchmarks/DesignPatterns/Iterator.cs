using System;
using System.Collections;
using System.Collections.Generic;

namespace DesignPatterns
{
    public class RangeIterator : IEnumerable<int>
    {
        private int start, end, step;

        public RangeIterator(int start, int end, int step = 1)
        {
            this.start = start; this.end = end; this.step = step;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = start; i < end; i += step)
                yield return i;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
