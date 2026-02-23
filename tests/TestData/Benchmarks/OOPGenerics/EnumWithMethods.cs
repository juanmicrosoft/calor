using System;
namespace OOPGenerics
{
    public enum LogLevel { Debug, Info, Warning, Error }
    public static class LogLevelExtensions
    {
        public static int Priority(this LogLevel l) => (int)l + 1;
        public static bool IsUrgent(this LogLevel l) => l >= LogLevel.Warning;
    }
}
