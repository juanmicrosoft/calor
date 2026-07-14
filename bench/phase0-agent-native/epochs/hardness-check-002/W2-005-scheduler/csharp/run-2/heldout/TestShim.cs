// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace Scheduling.HeldOut;

internal static class TestShim
{
    public static bool IsValidInterval(int s, int e) => SchedulerLib.Scheduler.IsValidInterval(s, e);
    public static bool Overlaps(int s1, int e1, int s2, int e2) => SchedulerLib.Scheduler.Overlaps(s1, e1, s2, e2);
    public static int ConflictIndex(int[] starts, int[] ends, int[] active, int count, int s, int e) => SchedulerLib.Scheduler.ConflictIndex(starts, ends, active, count, s, e);
    public static bool HasConflict(int[] starts, int[] ends, int[] active, int count, int s, int e) => SchedulerLib.Scheduler.HasConflict(starts, ends, active, count, s, e);
    public static int AddBooking(int[] starts, int[] ends, int[] prios, int[] active, int count, int s, int e, int prio) => SchedulerLib.Scheduler.AddBooking(starts, ends, prios, active, count, s, e, prio);
    public static bool Cancel(int[] active, int count, int index) => SchedulerLib.Scheduler.Cancel(active, count, index);
    public static int NextFreeStart(int[] starts, int[] ends, int[] active, int count, int duration, int earliest, int horizon) => SchedulerLib.Scheduler.NextFreeStart(starts, ends, active, count, duration, earliest, horizon);
    public static int ActiveCount(int[] active, int count) => SchedulerLib.Scheduler.ActiveCount(active, count);
    public static int OverlapLength(int s, int e, int winFrom, int winTo) => SchedulerLib.Scheduler.OverlapLength(s, e, winFrom, winTo);
    public static int BusyTime(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo) => SchedulerLib.Scheduler.BusyTime(starts, ends, active, count, winFrom, winTo);
    public static int UtilizationPercent(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo) => SchedulerLib.Scheduler.UtilizationPercent(starts, ends, active, count, winFrom, winTo);
    public static int PriorityAt(int[] starts, int[] ends, int[] prios, int[] active, int count, int t) => SchedulerLib.Scheduler.PriorityAt(starts, ends, prios, active, count, t);
    public static int AddWithPreemption(int[] starts, int[] ends, int[] prios, int[] active, int count, int s, int e, int prio, int horizon) => SchedulerLib.Scheduler.AddWithPreemption(starts, ends, prios, active, count, s, e, prio, horizon);
}
