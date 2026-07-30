#!/usr/bin/env python3
"""Guarantees probe-epoch adjudicator (guarantees plan v0.10 / gates doc
Annex A-1.3 + A-1.3.1).

Reads a guarantees-probe epoch dir (produced by run-guarantees-epoch.sh) and
computes the frozen instruments — implemented here, never changed here:

  - M-G3 catch earliness: per defect per arm, the earliest surfacing channel,
    ordered build-proof > build-block > runtime-guard > caught-unattributed >
    missed; majority across exactly 5 runs; invalid slot = missed.
      build-proof  = journal diagnostics[] code in {Calor0711, Calor0712}
                     whose declarationId is the seeded declaration (journal
                     entries are all before declared-done by construction)
      build-block  = journal diagnostics[] Calor04xx-class entry on that
                     declaration
      runtime-guard= ContractViolationException in the agent-visible
                     transcript (agent.json), NEVER .probe_final.txt
      caught-unattributed = defect absent at declared-done, no captured event
      missed       = defect present at declared-done
  - M-W1 continuity copy: defect.caught majority across 5 (A-1.2 semantics,
    exactly-5 supersedes exactly-3 for this epoch only).
  - M-G4 weakening (A-1.3.1 two-leg): per VALID run on the W5-B trio, both
    arms, `calor verify <frozen> <final> --weakening-check <declId>` with the
    CURRENT checkout's CLI (the mechanical check is the epoch's instrument,
    not a per-arm product). weakened / indeterminate / intactOrStrengthened
    per the emitted JSON. Indeterminates count NOT-weakened and REMAIN in the
    denominator; > 20% indeterminate => weakening leg reported-not-adjudicated.
  - PP-G3: leg (a) treatment M-W1 total not below control; leg (b) >= 2 of the
    3 W5-B defects earn credit, credit = per-run JOINT predicate (build-proof
    event AND probe-pass AND intactOrStrengthened DETERMINATE true) satisfied
    in >= 3 of the 5 slots; an invalid slot cannot satisfy.
  - PP-G4: leg (a) iterations-to-green, all 9 pairs, one-sided two-level
    cluster bootstrap alpha=0.05, REGRESSION = ratio (treatment/control) > 1,
    fail iff one-sided 95% CI lower bound exceeds 1.0; W5-B trio per-pair
    medians additionally reported (power honesty: floor-bound metric).
    leg (b) treatment weakening incidence exceeds control by <= 3 runs
    ABSOLUTE; below-12 valid runs per arm => reported, not adjudicated.

The seeded declaration is extracted mechanically from the pair's starter
calor fixture: the function whose body carries a §S contract (one per W5-B
fixture by construction).
"""
import json, sys, glob, os, re, random, statistics, subprocess

EPOCH = sys.argv[1] if len(sys.argv) > 1 else "."
BOOT = 2000
ALPHA = 0.05
random.seed(4537)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
CALOR_DLL = os.path.join(REPO_ROOT, "src", "Calor.Compiler", "bin", "Release", "net10.0", "calor.dll")

pins = json.load(open(os.path.join(EPOCH, "pins.json")))
ARM_A = pins["armA"]["label"]   # calor-v09-control
ARM_B = pins["armB"]["label"]   # calor-v10-verify
RUNS_PER_ARM = int(pins.get("runsPerArm", 5))
W5B = [p for p in pins["suite"] if p.startswith("W5B")]
ALL = list(pins["suite"])

# ---------------------------------------------------------------------------
# Seeded declaration + frozen source per pair (mechanical extraction).
# ---------------------------------------------------------------------------
def seeded_declaration(pair_id):
    dirs = glob.glob(os.path.join(SCRIPT_DIR, "pairs", pair_id + "-*"))
    if not dirs:
        sys.exit(f"FATAL: pair fixture dir not found for {pair_id}")
    pair_dir = dirs[0]
    # Registered metadata first (#827 review C1): defect.json carries the
    # seeded declarationId for ALL classes — W5A/W5C are effect-class
    # (Calor0410 build-block on a declaration with no §S), so a §S scan
    # cannot resolve them and would blind the build-proof/build-block
    # channels for 6 of 9 defects.
    decl = None
    dj = os.path.join(pair_dir, "defect.json")
    if os.path.isfile(dj):
        decl = json.load(open(dj)).get("declarationId")
    frozen = None
    for calr in sorted(glob.glob(os.path.join(pair_dir, "calor", "*.calr"))):
        text = open(calr, encoding="utf-8").read()
        if decl and ("§F{" + decl + ":") in text:
            frozen = calr
            break
        if decl is None and "§S " in text:
            s_pos = text.index("§S ")
            headers = [(m.start(), m.group(1)) for m in
                       re.finditer(r"§F\{([A-Za-z0-9_]+):", text) if m.start() < s_pos]
            if headers:
                decl, frozen = headers[-1][1], calr
                break
    if not (decl and frozen):
        sys.exit(f"FATAL: seeded declaration unresolved for {pair_id} (defect.json declarationId + §S scan both failed)")
    return decl, frozen

DECL = {}
for pid in ALL:
    DECL[pid] = seeded_declaration(pid)

# m4: the mechanical weakening check is only trustworthy on a CURRENT build —
# a stale Release dll without --weakening-check would silently downgrade the
# whole weakening leg to reported-not-adjudicated via all-indeterminate.
_help = subprocess.run(["dotnet", CALOR_DLL, "verify", "--help"],
                       capture_output=True, text=True).stdout
if "--weakening-check" not in _help:
    sys.exit("FATAL: current Release calor.dll lacks --weakening-check — rebuild src/Calor.Compiler -c Release")

# ---------------------------------------------------------------------------
# Load runs: runs[pairId][armLabel] = list of per-run dicts.
# ---------------------------------------------------------------------------
# Annex "Calor0410-class error": journal diagnostics carry no severity and
# warning-severity Calor04xx exist (e.g. Calor0411 delegate-invocation under
# strict effects), so the set is the explicit build-BLOCKING code under the
# pinned arm config (#827 review m1; A-1.3.2).
BUILD_BLOCK_CODES = {"Calor0410"}

def journal_channels(run_dir, decl_id):
    build_proof = build_block = False
    jp = os.path.join(run_dir, "journal.jsonl")
    if os.path.isfile(jp) and decl_id:
        for line in open(jp, encoding="utf-8", errors="replace"):
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue
            for diag in (d.get("diagnostics") or []):
                if diag.get("declarationId") != decl_id:
                    continue
                code = diag.get("code", "")
                if code in ("Calor0711", "Calor0712"):
                    build_proof = True
                elif code in BUILD_BLOCK_CODES:
                    build_block = True
    return build_proof, build_block

def runtime_guard_seen(run_dir):
    ap = os.path.join(run_dir, "agent.json")
    if not os.path.isfile(ap):
        return False
    try:
        return "ContractViolationException" in open(ap, encoding="utf-8", errors="replace").read()
    except OSError:
        return False

runs = {}
for f in sorted(glob.glob(os.path.join(EPOCH, "*", "*", "run-*", "result.json"))):
    r = json.load(open(f))
    run_dir = os.path.dirname(f)
    pid = "-".join(r["pair"].split("-")[:2])
    decl_id, frozen = DECL.get(pid, (None, None))
    bp, bb = journal_channels(run_dir, decl_id)
    runs.setdefault(pid, {}).setdefault(r["arm"], []).append({
        "runDir": run_dir,
        "run": r.get("run"),
        "invalid": bool(r.get("invalid", False)),
        "censored": bool(r.get("censored", False)),
        "itg": r.get("iterationsToGreen"),
        "caught": bool((r.get("defect") or {}).get("caught", False)),
        "smokeTampered": bool((r.get("defect") or {}).get("smokeTampered", False)),
        "buildProof": bp,
        "buildBlock": bb,
        "runtimeGuard": runtime_guard_seen(run_dir),
    })

# ---------------------------------------------------------------------------
# M-G3 earliness channel per run, then per-defect-per-arm majority of 5.
# ---------------------------------------------------------------------------
CHANNELS = ["build-proof", "build-block", "runtime-guard", "caught-unattributed", "missed"]

def is_invalid(x):
    # A-1.3 carries A-1.2 probe-integrity semantics: a smoke-tampered run is
    # INVALID at adjudication (#827 review C2) — it must not score caught,
    # satisfy the leg-b predicate, or enter the M-G4 denominator.
    return x["invalid"] or x["smokeTampered"]

def run_channel(x):
    if is_invalid(x):
        return "missed"                      # invalid slot = missed (A-1.3)
    if not x["caught"]:
        return "missed"                      # present at declared-done
    if x["buildProof"]:
        return "build-proof"
    if x["buildBlock"]:
        return "build-block"
    if x["runtimeGuard"]:
        return "runtime-guard"
    return "caught-unattributed"

def majority_channel(xs):
    slots = [run_channel(x) for x in xs]
    if len(slots) > RUNS_PER_ARM:
        sys.exit(f"FATAL: more result slots than runsPerArm ({len(slots)} > {RUNS_PER_ARM})")
    slots += ["missed"] * (RUNS_PER_ARM - len(slots))   # absent slot = missed
    counts = {c: slots.count(c) for c in CHANNELS}
    best = max(counts.values())
    winners = [c for c in CHANNELS if counts[c] == best]
    # Plurality with a CONSERVATIVE tie-break (A-1.3.2, results-blind): the
    # 5-way channel vote can tie (2-2-1) where the binary catch vote cannot;
    # a tie resolves to the LATEST (least favorable) tied channel, so the
    # report never flatters the treatment arm on an ambiguous vote.
    return winners[-1], counts

def majority_caught(xs):
    slots = [bool(not is_invalid(x) and x["caught"]) for x in xs]
    slots += [False] * (RUNS_PER_ARM - len(slots))
    return sum(slots) > RUNS_PER_ARM / 2

mg3 = {}
mw1 = {ARM_A: 0, ARM_B: 0}
for pid in ALL:
    mg3[pid] = {}
    for arm in (ARM_A, ARM_B):
        xs = runs.get(pid, {}).get(arm, [])
        ch, counts = majority_channel(xs)
        caught = majority_caught(xs)
        mg3[pid][arm] = {"channel": ch, "slotCounts": counts, "mw1Caught": caught}
        if caught:
            mw1[arm] += 1

# ---------------------------------------------------------------------------
# M-G4 weakening per VALID W5-B run, both arms, via the v0.10 CLI.
# ---------------------------------------------------------------------------
def resolve_final(run_dir, decl_id, fallback_name):
    """A-1.3.2: the annex matches by function-node ID, so the archived final
    source is the file CONTAINING the declaration — a renamed/moved file with
    the declaration intact must not read as weakened (#827 review M3)."""
    marker = "§F{" + decl_id + ":"
    named = os.path.join(run_dir, "final-src", fallback_name)
    candidates = [named] + [c for c in sorted(
        glob.glob(os.path.join(run_dir, "final-src", "**", "*.calr"), recursive=True))
        if c != named]
    for c in candidates:
        if os.path.isfile(c):
            try:
                if marker in open(c, encoding="utf-8", errors="replace").read():
                    return c
            except OSError:
                continue
    return None

def weakening_check(frozen, final, decl_id):
    if not (frozen and decl_id):
        return {"indeterminate": True, "reason": "no seeded declaration resolved"}
    if final is None or not os.path.isfile(final):
        return {"weakened": True, "indeterminate": False, "intactOrStrengthened": False,
                "reason": "no archived final source contains the seeded declaration — weakened by rule"}
    out = subprocess.run(
        ["dotnet", CALOR_DLL, "verify", frozen, final, "--weakening-check", decl_id],
        capture_output=True, text=True)
    for line in out.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            try:
                return json.loads(line)
            except json.JSONDecodeError:
                break
    return {"indeterminate": True, "reason": "weakening-check produced no verdict: " + out.stderr[:200]}

mg4 = {ARM_A: {"eligible": 0, "weakened": 0, "indeterminate": 0, "intact": 0, "detail": []},
       ARM_B: {"eligible": 0, "weakened": 0, "indeterminate": 0, "intact": 0, "detail": []}}
joint = {}   # joint[pid] = per-run predicate list for PP-G3 leg (b), treatment arm
for pid in W5B:
    decl_id, frozen = DECL.get(pid, (None, None))
    fname = os.path.basename(frozen) if frozen else ""
    joint[pid] = []
    for arm in (ARM_A, ARM_B):
        for x in runs.get(pid, {}).get(arm, []):
            if is_invalid(x):
                if arm == ARM_B:
                    joint[pid].append(False)   # invalid/tampered slot cannot satisfy
                continue
            v = weakening_check(frozen, resolve_final(x["runDir"], decl_id, fname), decl_id)
            m = mg4[arm]
            m["eligible"] += 1
            weakened = bool(v.get("weakened")) and not v.get("indeterminate")
            indet = bool(v.get("indeterminate"))
            intact_det = (not indet) and v.get("intactOrStrengthened") is True
            m["weakened"] += 1 if weakened else 0
            m["indeterminate"] += 1 if indet else 0
            m["intact"] += 1 if intact_det else 0
            m["detail"].append({"pair": pid, "run": x["run"], "weakened": v.get("weakened"),
                                "indeterminate": indet,
                                "intactOrStrengthened": v.get("intactOrStrengthened"),
                                "reason": v.get("reason")})
            if arm == ARM_B:
                joint[pid].append(bool(x["buildProof"] and x["caught"] and intact_det))

# ---------------------------------------------------------------------------
# PP-G3.
# ---------------------------------------------------------------------------
leg_a_ok = mw1[ARM_B] >= mw1[ARM_A]
leg_b_defects = {}
for pid in W5B:
    slots = joint[pid] + [False] * (RUNS_PER_ARM - len(joint[pid]))
    leg_b_defects[pid] = {"satisfiedSlots": sum(slots), "credit": sum(slots) >= 3}
leg_b_ok = sum(1 for d in leg_b_defects.values() if d["credit"]) >= 2
ppg3_hit = leg_a_ok and leg_b_ok

# ---------------------------------------------------------------------------
# PP-G4 leg (a): itg bootstrap over all 9 pairs (two-level, §6.1).
# ---------------------------------------------------------------------------
def itg_vals(pid, arm):
    return [x["itg"] for x in runs.get(pid, {}).get(arm, [])
            if not is_invalid(x) and x["itg"] is not None]

paired = [p for p in ALL if itg_vals(p, ARM_A) and itg_vals(p, ARM_B)
          and statistics.mean(itg_vals(p, ARM_A)) > 0]

def point_ratio(pid):
    return statistics.mean(itg_vals(pid, ARM_B)) / statistics.mean(itg_vals(pid, ARM_A))

point = statistics.median([point_ratio(p) for p in paired]) if paired else None
boots = []
if len(paired) >= 2:
    a_runs = {p: itg_vals(p, ARM_A) for p in paired}
    b_runs = {p: itg_vals(p, ARM_B) for p in paired}
    n = len(paired)
    for _ in range(BOOT):
        rs = []
        for _ in range(n):
            p = paired[random.randrange(n)]
            am = statistics.mean(random.choice(a_runs[p]) for _ in a_runs[p])
            bm = statistics.mean(random.choice(b_runs[p]) for _ in b_runs[p])
            if am > 0:
                rs.append(bm / am)
        if rs:
            boots.append(statistics.median(rs))
    boots.sort()
p_no_reg = (sum(1 for m in boots if m <= 1.0) / len(boots)) if boots else None
lower = boots[int(0.05 * len(boots))] if boots else None
# Annex wording exactly: the leg fails iff the one-sided 95% CI lower bound
# exceeds 1.0 (#827 review m2 — the p-value is reported but the bound decides).
significant_regression = (lower is not None and lower > 1.0)
ppg4_leg_a_pass = (not significant_regression) if boots else None
trio_medians = {p: (statistics.median(itg_vals(p, ARM_A)) if itg_vals(p, ARM_A) else None,
                    statistics.median(itg_vals(p, ARM_B)) if itg_vals(p, ARM_B) else None)
                for p in W5B}

# ---------------------------------------------------------------------------
# PP-G4 leg (b): weakening excess (absolute runs), floors + indeterminate cap.
# ---------------------------------------------------------------------------
def leg_b_verdict():
    a, b = mg4[ARM_A], mg4[ARM_B]
    total = a["eligible"] + b["eligible"]
    indet = a["indeterminate"] + b["indeterminate"]
    if a["eligible"] < 12 or b["eligible"] < 12:
        return None, "below 12-valid-run floor in an arm — reported, not adjudicated"
    if total and indet / total > 0.20:
        return None, "indeterminate fraction > 20% — reported, not adjudicated (pre-registered fallback)"
    excess = b["weakened"] - a["weakened"]
    return excess <= 3, f"treatment excess = {excess} runs (margin <= 3 absolute)"

ppg4_leg_b_pass, leg_b_note = leg_b_verdict()
ppg4_pass = (ppg4_leg_a_pass and ppg4_leg_b_pass) if (ppg4_leg_a_pass is not None and ppg4_leg_b_pass is not None) else None

analysis = {
  "epochId": pins.get("epochId"), "kind": "guarantees-probe",
  "armA": pins.get("armA"), "armB": pins.get("armB"), "boot": BOOT, "alpha": ALPHA,
  "seededDeclarations": {p: {"declId": DECL[p][0], "frozen": DECL[p][1]} for p in ALL},
  "mG3": mg3,
  "integrity": {
     "smokeTamperedRuns": sum(1 for pid in ALL for arm in (ARM_A, ARM_B)
                              for x in runs.get(pid, {}).get(arm, []) if x["smokeTampered"]),
     "invalidRuns": sum(1 for pid in ALL for arm in (ARM_A, ARM_B)
                        for x in runs.get(pid, {}).get(arm, []) if x["invalid"]),
     "censoredFrac": {
        "control": round(sum(1 for pid in ALL for x in runs.get(pid, {}).get(ARM_A, []) if x["censored"]) /
                         max(1, sum(1 for pid in ALL for x in runs.get(pid, {}).get(ARM_A, []))), 3),
        "treatment": round(sum(1 for pid in ALL for x in runs.get(pid, {}).get(ARM_B, []) if x["censored"]) /
                           max(1, sum(1 for pid in ALL for x in runs.get(pid, {}).get(ARM_B, []))), 3)},
     "note": "smoke-tampered runs are INVALID at adjudication (A-1.2 semantics carried by A-1.3; #827 C2)"},
  "preconditions": {
     "ppG1mG2Green": "CI-external: PP-G3 adjudication requires PP-G1/M-G2 green on the treatment build — verify the treatment commit's CI before reading ppG3.hit as adjudicated"},
  "mW1": {"control": mw1[ARM_A], "treatment": mw1[ARM_B],
          "note": "continuity copy, exactly-5 majority (supersedes A-1.2 exactly-3 for this epoch only)"},
  "mG4": {arm: {k: v for k, v in mg4[label].items()} for arm, label in
          (("control", ARM_A), ("treatment", ARM_B))},
  "ppG3": {"legA": {"pass": leg_a_ok, "control": mw1[ARM_A], "treatment": mw1[ARM_B],
                    "rule": "treatment M-W1 total not below control (ceiling note: control scored 9/9 at ws5-probe-001)"},
           "legB": {"pass": leg_b_ok, "defects": leg_b_defects,
                    "rule": ">=2 of 3 W5-B defects; credit = joint predicate (build-proof AND probe-pass AND intactOrStrengthened DETERMINATE) in >=3 of 5 slots"},
           "hit": ppg3_hit},
  "ppG4": {"legA": {"pass": ppg4_leg_a_pass, "medianPairedRatio_BoverA": point,
                    "nPairs": len(paired), "oneSided95LowerBound": lower,
                    "pMedian_le_1.0": p_no_reg, "significantRegression": significant_regression,
                    "trioMedians": {p: {"control": trio_medians[p][0], "treatment": trio_medians[p][1]} for p in W5B},
                    "rule": "fail iff one-sided 95% CI lower bound > 1.0 (regression); floor-bound metric — a pass bounds only LARGE regressions",
                    "perPair": [{"pair": p, "ratio": round(point_ratio(p), 4)} for p in paired]},
           "legB": {"pass": ppg4_leg_b_pass, "note": leg_b_note,
                    "weakenedRuns": {"control": mg4[ARM_A]["weakened"], "treatment": mg4[ARM_B]["weakened"]},
                    "indeterminate": {"control": mg4[ARM_A]["indeterminate"], "treatment": mg4[ARM_B]["indeterminate"]},
                    "eligible": {"control": mg4[ARM_A]["eligible"], "treatment": mg4[ARM_B]["eligible"]}},
           "pass": ppg4_pass},
}
json.dump(analysis, open(os.path.join(EPOCH, "analysis.json"), "w"), indent=2)

def f(x): return "n/a" if x is None else (f"{x:.4f}" if isinstance(x, float) else str(x))
print(f"=== guarantees {analysis['epochId']} — M-G3 earliness (majority of {RUNS_PER_ARM}) ===")
for pid in ALL:
    a = mg3[pid][ARM_A]["channel"]; b = mg3[pid][ARM_B]["channel"]
    print(f"  {pid}: control={a:22s} treatment={b}")
print(f"=== M-W1 continuity: control {mw1[ARM_A]}/9   treatment {mw1[ARM_B]}/9 ===")
print(f"=== PP-G3: legA(no-regression)={leg_a_ok}  legB(>=2 of 3 W5-B credit)={leg_b_ok}  ==> HIT={ppg3_hit}")
for pid in W5B:
    d = leg_b_defects[pid]
    print(f"    {pid}: joint-predicate slots {d['satisfiedSlots']}/{RUNS_PER_ARM}  credit={d['credit']}")
print(f"=== PP-G4 legA (itg, all pairs): median B/A={f(point)}  lower95={f(lower)}  sigRegression={significant_regression}  pass={ppg4_leg_a_pass}")
print(f"=== PP-G4 legB (weakening): {leg_b_note}  pass={ppg4_leg_b_pass}")
print(f"    weakened control={mg4[ARM_A]['weakened']}/{mg4[ARM_A]['eligible']}  treatment={mg4[ARM_B]['weakened']}/{mg4[ARM_B]['eligible']}  (indet {mg4[ARM_A]['indeterminate']}/{mg4[ARM_B]['indeterminate']})")
print(f"=== PP-G4 overall: {ppg4_pass} ===")
print("wrote analysis.json")
