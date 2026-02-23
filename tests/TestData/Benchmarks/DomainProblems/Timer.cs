using System;

namespace DomainProblems
{
    public class Timer
    {
        public int Remaining { get; private set; }

        public Timer(int seconds)
        {
            if (seconds < 0) throw new ArgumentException("Seconds cannot be negative");
            Remaining = seconds;
        }

        public void Tick() { if (Remaining > 0) Remaining--; }
        public bool IsExpired => Remaining <= 0;
        public int Minutes => Remaining / 60;
        public int Seconds => Remaining % 60;
        public string Display() => $"{Minutes:D2}:{Seconds:D2}";
    }
}
