using System;
using System.Collections.Generic;

namespace DataStructures
{
    public class SimpleHashMap<TKey, TValue>
    {
        private List<(TKey key, TValue value)>[] buckets;
        private int count = 0;

        public SimpleHashMap(int capacity = 16)
        {
            buckets = new List<(TKey, TValue)>[capacity];
            for (int i = 0; i < capacity; i++)
                buckets[i] = new List<(TKey, TValue)>();
        }

        public void Put(TKey key, TValue value)
        {
            int idx = Math.Abs(key!.GetHashCode()) % buckets.Length;
            for (int i = 0; i < buckets[idx].Count; i++)
            {
                if (EqualityComparer<TKey>.Default.Equals(buckets[idx][i].key, key))
                {
                    buckets[idx][i] = (key, value);
                    return;
                }
            }
            buckets[idx].Add((key, value));
            count++;
        }

        public TValue? Get(TKey key)
        {
            int idx = Math.Abs(key!.GetHashCode()) % buckets.Length;
            foreach (var (k, v) in buckets[idx])
                if (EqualityComparer<TKey>.Default.Equals(k, key)) return v;
            return default;
        }

        public double LoadFactor => (double)count / buckets.Length;
    }
}
