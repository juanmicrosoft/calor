// Interval booking scheduler over parallel arrays.
// Booking i = [starts[i], ends[i]) with priority prios[i]; active[i] is 1
// while the booking holds its slot and 0 once cancelled. count is the number
// of booking records (including cancelled ones); capacity beyond count is
// scratch space. Invariant: no two active bookings overlap.
// Callers keep every time value in 0 .. 999999 so 32-bit arithmetic never wraps.
namespace SchedulerLib;

public static class Scheduler
{
    public static bool IsValidInterval(int s, int e)
    {
        return s >= 0 && s < e;
    }

    public static bool Overlaps(int s1, int e1, int s2, int e2)
    {
        return s1 < e2 && s2 < e1;
    }

    public static int ConflictIndex(int[] starts, int[] ends, int[] active, int count, int s, int e)
    {
        int i = 0;
        while (i < count)
        {
            if (active[i] == 1)
            {
                int bs = starts[i];
                int be = ends[i];
                bool ov = Overlaps(bs, be, s, e);
                if (ov)
                {
                    return i;
                }
            }
            i = i + 1;
        }
        return -1;
    }

    public static bool HasConflict(int[] starts, int[] ends, int[] active, int count, int s, int e)
    {
        int idx = ConflictIndex(starts, ends, active, count, s, e);
        return idx >= 0;
    }

    public static int AddBooking(int[] starts, int[] ends, int[] prios, int[] active, int count, int s, int e, int prio)
    {
        bool valid = IsValidInterval(s, e);
        if (!valid)
        {
            return -1;
        }
        bool conflict = HasConflict(starts, ends, active, count, s, e);
        if (conflict)
        {
            return -1;
        }
        starts[count] = s;
        ends[count] = e;
        prios[count] = prio;
        active[count] = 1;
        return count + 1;
    }

    public static bool Cancel(int[] active, int count, int index)
    {
        if (active[index] == 1)
        {
            active[index] = 0;
            return true;
        }
        return false;
    }

    public static int NextFreeStart(int[] starts, int[] ends, int[] active, int count, int duration, int earliest, int horizon)
    {
        int t = earliest;
        while (t + duration <= horizon)
        {
            int te = t + duration;
            int idx = ConflictIndex(starts, ends, active, count, t, te);
            if (idx < 0)
            {
                return t;
            }
            t = ends[idx];
        }
        return -1;
    }

    public static int ActiveCount(int[] active, int count)
    {
        int n = 0;
        int i = 0;
        while (i < count)
        {
            if (active[i] == 1)
            {
                n = n + 1;
            }
            i = i + 1;
        }
        return n;
    }

    public static int OverlapLength(int s, int e, int winFrom, int winTo)
    {
        int lo = s;
        if (winFrom > lo)
        {
            lo = winFrom;
        }
        int hi = e;
        if (winTo < hi)
        {
            hi = winTo;
        }
        if (hi <= lo)
        {
            return 0;
        }
        return hi - lo;
    }

    public static int BusyTime(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo)
    {
        int total = 0;
        int i = 0;
        while (i < count)
        {
            if (active[i] == 1)
            {
                int bs = starts[i];
                int be = ends[i];
                int part = OverlapLength(bs, be, winFrom, winTo);
                total = total + part;
            }
            i = i + 1;
        }
        return total;
    }

    public static int UtilizationPercent(int[] starts, int[] ends, int[] active, int count, int winFrom, int winTo)
    {
        int busy = BusyTime(starts, ends, active, count, winFrom, winTo);
        return busy * 100 / (winTo - winFrom);
    }

    public static int PriorityAt(int[] starts, int[] ends, int[] prios, int[] active, int count, int t)
    {
        int i = 0;
        while (i < count)
        {
            if (active[i] == 1)
            {
                int bs = starts[i];
                int be = ends[i];
                if (bs <= t && t < be)
                {
                    return prios[i];
                }
            }
            i = i + 1;
        }
        return -1;
    }
}
