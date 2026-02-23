// C# equivalent - no effect system
using System.Diagnostics;

public static class TimerService
{
    public static int ElapsedMs(int startMs, int endMs) => endMs - startMs;
    public static bool ShouldTimeout(int elapsedMs, int timeoutMs) => elapsedMs >= timeoutMs;
    public static int RemainingMs(int timeoutMs, int elapsedMs) => Math.Max(0, timeoutMs - elapsedMs);
}
