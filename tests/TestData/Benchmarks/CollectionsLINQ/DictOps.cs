using System;
using System.Collections.Generic;
namespace CollectionsLINQ
{
    public static class DictOps
    {
        public static bool ContainsKey<K, V>(Dictionary<K, V> dict, K key) where K : notnull => dict.ContainsKey(key);
        public static int EntryCount<K, V>(Dictionary<K, V> dict) where K : notnull => dict.Count;
        public static V GetOrDefault<K, V>(Dictionary<K, V> dict, K key, V def) where K : notnull =>
            dict.TryGetValue(key, out var val) ? val : def;
    }
}
