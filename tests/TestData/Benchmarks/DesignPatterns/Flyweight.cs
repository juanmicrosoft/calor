using System;
using System.Collections.Generic;

namespace DesignPatterns
{
    public class CharacterStyle
    {
        public string Font { get; }
        public int Size { get; }
        public CharacterStyle(string font, int size) { Font = font; Size = size; }
    }

    public class StyleFactory
    {
        private Dictionary<string, CharacterStyle> pool = new Dictionary<string, CharacterStyle>();

        public CharacterStyle GetStyle(string font, int size)
        {
            string key = $"{font}_{size}";
            if (!pool.ContainsKey(key))
                pool[key] = new CharacterStyle(font, size);
            return pool[key];
        }

        public int PoolSize => pool.Count;
    }
}
