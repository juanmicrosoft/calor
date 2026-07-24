// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace Scheduling.HeldOut;

internal static class TestShim
{
    public static bool IsValidInterval(int s, int e) => global::Scheduler.SchedulerModule.IsValidInterval(s, e);
    public static bool Overlaps(int s1, int e1, int s2, int e2) => global::Scheduler.SchedulerModule.Overlaps(s1, e1, s2, e2);
    public static int ConflictIndex(int[] starts, int[] ends, int[] active, int count, int s, int e) => global::Scheduler.SchedulerModule.ConflictIndex(starts, ends, active, count, s, e);
    public static bool HasConflict(int[] starts, int[] ends, int[] active, int count, int s, int e) => global::Scheduler.SchedulerModule.HasConflict(starts, ends, active, count, s, e);
    public static int AddBooking(int[] starts, int[] ends, int[] prios, int[] active, int count, int s, int e, int prio) => global::Scheduler.SchedulerModule.AddBooking(starts, ends, prios, active, count, s, e, prio);
    public static bool Cancel(int[] active, int count, int index) => global::Scheduler.SchedulerModule.Cancel(active, count, index);
    public static int NextFreeStart(int[] starts, int[] ends, int[] active, int count, int duration, int earliest, int horizon) => global::Scheduler.SchedulerModule.NextFreeStart(starts, ends, active, count, duration, earliest, horizon);
    public static int ActiveCount(int[] active, int count) => global::Scheduler.SchedulerModule.ActiveCount(active, count);
    public static int OverlapLength(int s, int e, int winFrom, int winTo) => global::Scheduler.SchedulerModule.OverlapLength(s, e, winFrom, winTo);
    public static int BusyTime(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo) => global::Scheduler.SchedulerModule.BusyTime(starts, ends, active, count, winFrom, winTo);
    public static int UtilizationPercent(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo) => global::Scheduler.SchedulerModule.UtilizationPercent(starts, ends, active, count, winFrom, winTo);
    public static int PriorityAt(int[] starts, int[] ends, int[] prios, int[] active, int count, int t) => global::Scheduler.SchedulerModule.PriorityAt(starts, ends, prios, active, count, t);
    public static int AddWithPreemption(int[] starts, int[] ends, int[] prios, int[] active, int count, int s, int e, int prio, int horizon) => global::Scheduler.SchedulerModule.AddWithPreemption(starts, ends, prios, active, count, s, e, prio, horizon);
}
