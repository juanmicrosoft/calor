using System;

namespace DesignPatterns
{
    public class Singleton
    {
        private static Singleton instance = null;
        private int value = 0;

        private Singleton() { }

        public static Singleton GetInstance()
        {
            if (instance == null)
                instance = new Singleton();
            return instance;
        }

        public int GetValue()
        {
            return value;
        }

        public void SetValue(int newValue)
        {
            value = newValue;
        }
    }
}
