# W2-005 — Booking Scheduler with Priority Preemption (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic. Callers keep every time value in
`0 .. 999999`, so no 32-bit overflow occurs; overflow behavior is outside the
contract.

## Data model

The scheduler holds bookings in parallel arrays. Booking `i` occupies the
half-open interval `[starts[i], ends[i])` with priority `prios[i]`;
`active[i]` is `1` while the booking holds its slot and `0` once cancelled.
`count` is the number of booking records (including cancelled ones); array
capacity beyond `count` is scratch space. Two intervals *overlap* when
`s1 < e2 && s2 < e1`; touching intervals (`[0,5)` and `[5,10)`) do not
overlap.

**Core invariant: no two active bookings ever overlap.** Every query below
(`ConflictIndex`, `BusyTime`, `UtilizationPercent`, `PriorityAt`) is
specified — and correct — only under this invariant, and every mutating
operation must maintain it.

## Existing behavior (already implemented in the starting fixture — must be preserved)

- `IsValidInterval(s, e)` — `s >= 0 && s < e`.
- `Overlaps(s1, e1, s2, e2)` — half-open interval overlap as defined above.
- `ConflictIndex(starts, ends, active, count, s, e)` — index of the first
  (lowest-index) active booking overlapping `[s, e)`, else `-1`.
- `HasConflict(...)` — whether any active booking overlaps `[s, e)`.
- `AddBooking(starts, ends, prios, active, count, s, e, prio)` — rejects
  (returns `-1`, no changes) when the interval is invalid or conflicts with
  an active booking; otherwise records the booking at index `count` and
  returns `count + 1`. Requires capacity for one record.
- `Cancel(active, count, index)` — deactivates booking `index` if active,
  returning whether anything changed.
- `NextFreeStart(starts, ends, active, count, duration, earliest, horizon)`
  — the smallest `t >= earliest` such that `[t, t + duration)` conflicts
  with no active booking and `t + duration <= horizon`; `-1` if none exists.
- `ActiveCount(active, count)` — number of active bookings.
- `OverlapLength(s, e, winFrom, winTo)` — length of the part of `[s, e)`
  inside the window `[winFrom, winTo)` (0 when disjoint).
- `BusyTime(starts, ends, active, count, winFrom, winTo)` — total time
  inside the window covered by active bookings.
- `UtilizationPercent(...)` — `BusyTime * 100 / (winTo - winFrom)` (integer
  division). Callers pass `0 <= winFrom < winTo`.
- `PriorityAt(starts, ends, prios, active, count, t)` — priority of the
  active booking covering instant `t`, else `-1`.

## Task: add priority preemption

`AddWithPreemption(starts, ends, prios, active, count, s, e, prio, horizon)`
→ integer.

1. If `[s, e)` is invalid, return `-1` with no changes.
2. If any active booking overlapping `[s, e)` has priority **greater than or
   equal to** `prio`, return `-1` with no changes.
3. Otherwise the add succeeds and returns `count + 1`; the conflicting
   bookings (all strictly lower priority) are *displaced*:
   a. every displaced booking is first removed from the timetable;
   b. the new booking is recorded at index `count` (interval `[s, e)`,
      priority `prio`, active);
   c. each displaced booking is then re-placed **in increasing index
      order**, keeping its duration, priority, and record index: it moves to
      the smallest start `t >= e` such that its interval `[t, t + duration)`
      conflicts with no currently active booking (including the new booking
      and any displaced bookings already re-placed) and
      `t + duration <= horizon`. If no such `t` exists, the booking is
      cancelled.
4. When nothing overlaps `[s, e)`, the operation behaves exactly like
   `AddBooking` (the new booking itself is not checked against `horizon`).

The no-overlap invariant must hold after every call — the statistics and
lookup functions above must remain correct on any state produced by any mix
of `AddBooking`, `Cancel`, and `AddWithPreemption`.

### Constraints

- Do **not** change the observable behavior of any existing function.
- Pure integer computation only: no I/O, no global state, no floating point.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `IsValidInterval(int, int) → bool`,
`Overlaps(int, int, int, int) → bool`,
`ConflictIndex(int[], int[], int[], int, int, int) → int`,
`HasConflict(int[], int[], int[], int, int, int) → bool`,
`AddBooking(int[], int[], int[], int[], int, int, int, int) → int`,
`Cancel(int[], int, int) → bool`,
`NextFreeStart(int[], int[], int[], int, int, int, int) → int`,
`ActiveCount(int[], int) → int`,
`OverlapLength(int, int, int, int) → int`,
`BusyTime(int[], int[], int[], int, int, int) → int`,
`UtilizationPercent(int[], int[], int[], int, int, int) → int`,
`PriorityAt(int[], int[], int[], int[], int, int) → int`,
`AddWithPreemption(int[], int[], int[], int[], int, int, int, int, int) → int`,
reachable through the arm's `TestShim.cs` (provided by the harness; not
editable by the agent).
