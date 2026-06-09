# Phase 2 §10 Gate — Monitoring Tick Setup

This document is the operator runbook for the 4-hour monitoring tick
required by v6 §3.3 and `phase-2-gate-config.json` `monitoring` block.
The tick runs `scripts/monitor_phase_2_gate.py` against
`results/<run>/runs.jsonl` and writes a structured record to
`monitor.log`, exiting non-zero if any abort trigger trips.

The tick MUST be scheduled and verified BEFORE the kickoff command is
executed (validation-criteria §8.1 row 8). It MUST keep running until
the gate driver exits cleanly (success) or the operator halts it after
a tripped trigger.

## §1 — One-shot verification (run before scheduling)

Confirm the script runs end-to-end against a stub `runs.jsonl`:

```bash
mkdir -p /tmp/gate-monitor-test
cat > /tmp/gate-monitor-test/runs.jsonl <<'EOF'
{"task_id":"t1","arm":"A","seed":0,"success":true,"turn_count":3,"total_output_tokens":1200,"harness_crash":false,"raw_log_path":"x"}
{"task_id":"t1","arm":"B","seed":0,"success":true,"turn_count":4,"total_output_tokens":1500,"harness_crash":false,"raw_log_path":"x"}
{"task_id":"t1","arm":"C","seed":0,"success":false,"turn_count":1,"total_output_tokens":200,"harness_crash":true,"raw_log_path":"x"}
EOF

python3 scripts/monitor_phase_2_gate.py \
    --config docs/plans/phase-2-gate-config.json \
    --runs /tmp/gate-monitor-test/runs.jsonl \
    --log  /tmp/gate-monitor-test/monitor.log

# Expected: exit 0, prints per-arm summary, writes one record to monitor.log.
cat /tmp/gate-monitor-test/monitor.log
rm -rf /tmp/gate-monitor-test
```

If this fails, do NOT proceed to schedule the tick — fix the script
first.

## §2 — Schedule on Windows (the operator's primary host)

The operator's machine is Windows. Use Task Scheduler.

```powershell
# Run as Administrator. Replace <RUN_DIR> with the actual gate output
# directory chosen for kickoff (e.g., results/phase-2-gate-2026-06-01).

$RunDir   = 'C:\path\to\calor\results\phase-2-gate-2026-06-01'
$Python   = 'python'   # or full path to the python.exe used elsewhere
$Repo     = 'C:\Users\juanrivera\sources\repos\juanmicrosoft\calor-2'
$Config   = "$Repo\docs\plans\phase-2-gate-config.json"
$Runs     = "$RunDir\runs.jsonl"
$Log      = "$RunDir\monitor.log"

$Action = New-ScheduledTaskAction `
    -Execute $Python `
    -Argument "$Repo\scripts\monitor_phase_2_gate.py --config $Config --runs $Runs --log $Log" `
    -WorkingDirectory $Repo

$Trigger = New-ScheduledTaskTrigger `
    -Once -At (Get-Date).AddMinutes(5) `
    -RepetitionInterval (New-TimeSpan -Hours 4) `
    -RepetitionDuration (New-TimeSpan -Days 7)

$Principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME -RunLevel Limited -LogonType S4U

$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

Register-ScheduledTask `
    -TaskName 'Calor-Phase2-Gate-Monitor' `
    -Action $Action -Trigger $Trigger `
    -Principal $Principal -Settings $Settings `
    -Description 'v6 §3.3 4-hour monitoring tick for the Phase 2 §10 gate'
```

To unregister after the gate completes:

```powershell
Unregister-ScheduledTask -TaskName 'Calor-Phase2-Gate-Monitor' -Confirm:$false
```

To trigger a tick on demand (operator decision to peek between scheduled
ticks):

```powershell
Start-ScheduledTask -TaskName 'Calor-Phase2-Gate-Monitor'
```

### §2.1 — Alerting on Windows

Task Scheduler does not auto-alert on non-zero exit. Two options:

1. **Manual review** — operator opens `monitor.log` at each ScheduleWakeup
   tick (which on the operator's setup is daily). This is the v6 §3.3
   baseline.
2. **Email-on-failure** — wrap the action in a script that emails the
   operator's address on exit 1. See `scripts/monitor_phase_2_gate.ps1`
   below for a wrapper.

A minimal wrapper that surfaces non-zero exits to the Windows event log
(which the operator can subscribe to via Get-WinEvent):

```powershell
# scripts/monitor_phase_2_gate.ps1 — runs the Python monitor and writes
# its exit to the Application event log so the operator can subscribe.
$exit = & python `
    $PSScriptRoot\monitor_phase_2_gate.py `
    --config $args[0] --runs $args[1] --log $args[2]

$source = 'CalorPhase2GateMonitor'
if (-not [System.Diagnostics.EventLog]::SourceExists($source)) {
    New-EventLog -LogName Application -Source $source
}
if ($LASTEXITCODE -eq 0) {
    Write-EventLog -LogName Application -Source $source `
        -EventId 1 -EntryType Information `
        -Message "Phase 2 gate monitor tick: all green"
} else {
    Write-EventLog -LogName Application -Source $source `
        -EventId 2 -EntryType Warning `
        -Message "Phase 2 gate monitor: HALT RECOMMENDED. See $($args[2])"
}
exit $LASTEXITCODE
```

## §3 — Schedule on POSIX (Linux / macOS fallback)

If the gate is moved to a Linux/macOS host (recommended for the bash
adapter, see protocol v2 §10.1.a note), use cron:

```cron
# /etc/cron.d/calor-phase2-gate-monitor — runs every 4 hours.
# Replace <REPO> and <RUN_DIR> with the actual paths.
0 */4 * * * <user>  python3 <REPO>/scripts/monitor_phase_2_gate.py \
    --config <REPO>/docs/plans/phase-2-gate-config.json \
    --runs   <REPO>/<RUN_DIR>/runs.jsonl \
    --log    <REPO>/<RUN_DIR>/monitor.log \
    || logger -t calor-gate-monitor 'HALT RECOMMENDED (see monitor.log)'
```

Validate with:

```bash
sudo crontab -u <user> -l | grep calor
journalctl -t calor-gate-monitor   # check after first tick
```

## §4 — What the operator does when a tick goes red

When the monitor exits 1:

1. Read the latest record in `monitor.log` (last line is JSONL).
2. Identify which trigger(s) tripped: `harness_crash_rate_pct`,
   `harness_error_rate_pct`, or `consecutive_failures`.
3. Inspect the most recent ~30 records in `runs.jsonl` to see WHICH
   trials and arms are failing. A burst confined to one arm signals
   a real signal (or a real bug in that arm). A burst spread across
   all arms signals infrastructure (model availability, network).
4. Consult `phase-2-spend-authorisation.md` §4. Halt or override per
   that document's discipline.
5. If halting, send `SIGTERM` (or Ctrl-C) to the driver process. The
   driver writes records to `runs.jsonl` incrementally; the partial
   run is recoverable for diagnosis but NOT for a continued statistical
   analysis (the seed grid is no longer balanced).
6. Decide: full retry under v6 §9.c, or pivot to v3 protocol with a
   smaller grid.

## §5 — Why this script is conservative

The thresholds in `phase-2-gate-config.json abort_triggers` are
deliberately loose:

- `harness_crash_rate_pct: 10` — analyser already warns at 5%; 10%
  is the structural-failure line.
- `harness_error_rate_pct: 20` — production should be ~0%; 20% means
  the substrate has regressed.
- `consecutive_failures: 30` — equals one full trial × all 30 seeds
  failing, which is a structural pattern, not noise.

The monitor will NOT halt the gate for: low success rates (those are
the *measurement*, not a failure), one arm losing badly (that's the
signal we want), or high spend (the operator manages spend separately
against the §3 ceiling).

## §6 — Cross-references

- [`phase-2-gate-config.json`](phase-2-gate-config.json) — source of
  abort thresholds.
- [`phase-2-spend-authorisation.md`](phase-2-spend-authorisation.md) §4
  — what to do when a trigger trips.
- [`phase-2-measurement-protocol-v2.md`](phase-2-measurement-protocol-v2.md)
  §10.1.a — the kickoff command this monitor observes.
- [`path-2-drop-ids-v6-implementation.md`](path-2-drop-ids-v6-implementation.md)
  §3.3 — the v6 spec that requires this tick.
