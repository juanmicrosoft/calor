#!/usr/bin/env python3
"""D3.3 latency-fixture generator (loop plan WS3, M4 kickoff §3 PR 4).

Deterministically (seeded) generates:
  fixture-10k/src/*.calr   — a ~10k-line multi-module Calor project
  edits/edit-script.jsonl  — the seeded timed-edit sequence for M-L1

The fixture is COMMITTED and pinned: measurements run against the bytes in
git, never against a regenerator's output du jour. Regeneration policy and
content criteria live in fixture-10k/README.md (written by this script so
the two cannot drift).

Structure: one Main module (layer 0) over three layers of 35 modules each;
cross-module calls flow strictly downward (L0→L1→L2→L3), giving a
module-graph reference depth of 3. Each module holds two function kinds:

  calc  — contract-bearing (§Q on inputs, §S provable by Z3 over the pure
          arithmetic body), never called cross-module, so no call-site
          obligations enter verification. Their trailing constants are the
          safe-edit targets.
  flow  — contract-free; L1/L2 flow functions call one or two public flow
          functions of the layer below (the cross-module edges); L3 flow
          functions are pure arithmetic. Their definitions are the
          breaking-edit targets (a rename dangles every upstream caller).

All functions declare §E (effect density 1, the corpus constant); everything
is pure except Main's §E{cw}.
"""
import argparse
import json
import random
from pathlib import Path

HERE = Path(__file__).resolve().parent

SEED = 4537  # recorded default; --seed overrides
LAYERS = 3
MODULES_PER_LAYER = 35
FUNCS_PER_MODULE = 13     # ~p90 of corpus functions/module (13.0)
CALC_FRACTION = 0.6       # calc (contracted) vs flow (calling) split
SAFE_EDITS = 200
BREAKING_EDITS = 30
CONST_BASE = 20000        # safe-edit constants: unique, ≥5 digits, positive


def fn_name(layer, mod, idx):
    return f"l{layer}m{mod:02d}_fn{idx:02d}"


def module_name(layer, mod):
    return f"L{layer}M{mod:02d}"


def generate(seed):
    rng = random.Random(seed)
    files = {}            # relpath -> text
    const_of = {}         # (layer, mod, idx) -> current constant
    flow_fns = {l: [] for l in range(1, LAYERS + 1)}  # layer -> [(mod, idx)]
    calc_fns = []         # [(layer, mod, idx)]
    called = set()        # (layer, mod, idx) of functions with ≥1 caller
    next_const = CONST_BASE

    # Assign function kinds per module up front so call targets exist.
    kinds = {}            # (layer, mod) -> [kind per idx]
    for layer in range(1, LAYERS + 1):
        for mod in range(1, MODULES_PER_LAYER + 1):
            ks = []
            for idx in range(1, FUNCS_PER_MODULE + 1):
                calc = layer == LAYERS or rng.random() < CALC_FRACTION
                # L3 keeps a few flow fns as rename targets: force ~40% flow.
                if layer == LAYERS:
                    calc = rng.random() < CALC_FRACTION
                ks.append("calc" if calc else "flow")
                if calc:
                    calc_fns.append((layer, mod, idx))
                else:
                    flow_fns[layer].append((mod, idx))
            kinds[(layer, mod)] = ks

    for layer in range(1, LAYERS + 1):
        for mod in range(1, MODULES_PER_LAYER + 1):
            lines = [f"§M{{m{layer}{mod:02d}:{module_name(layer, mod)}}}"]
            for idx in range(1, FUNCS_PER_MODULE + 1):
                kind = kinds[(layer, mod)][idx - 1]
                name = fn_name(layer, mod, idx)
                fid = f"f{idx:03d}"
                lines.append("")
                lines.append(f"  §F{{{fid}:{name}:pub}} (i32:x, i32:y) -> i32")
                lines.append("    §E{}")
                if kind == "calc":
                    const = next_const
                    next_const += 7
                    const_of[(layer, mod, idx)] = const
                    scale = rng.choice([2, 3, 5])
                    # Contract forms are drawn from the verifier's
                    # proven-outcome surface (D1.5 corpus, Outcomes/proven
                    # .calr): single-parameter inequality chains. Postconds
                    # over `result` or over parameter arithmetic are
                    # excluded — the current verifier refutes both (result
                    # left unconstrained / bitvector overflow), see the
                    # fixture README. Q lower bounds never bind callers:
                    # calc functions have none.
                    bound = rng.randrange(1, 9)
                    if rng.random() < 0.5:
                        lines.append(f"    §Q (> x {bound - 1})")
                        lines.append(f"    §S (>= x {bound})")
                    else:
                        lines.append(f"    §Q (>= x {bound})")
                        lines.append(f"    §Q (>= y 0)")
                        lines.append(f"    §S (>= x {max(bound - 2, 0)})")
                    lines.append(f"    §B{{acc:i32}} (+ (* x {scale}) y)")
                    lines.append(f"    §IF{{if{idx:02d}}} (> acc {const})")
                    lines.append(f"      §R (+ acc {const})")
                    lines.append("    §EL")
                    lines.append(f"      §R (+ (* acc 2) {const})")
                else:
                    if layer < LAYERS:
                        n_calls = rng.choice([1, 2])
                        targets = rng.sample(flow_fns[layer + 1], n_calls)
                        for c, (tmod, tidx) in enumerate(targets, 1):
                            called.add((layer + 1, tmod, tidx))
                            tname = fn_name(layer + 1, tmod, tidx)
                            arg = rng.randrange(1, 90)
                            lines.append(
                                f"    §B{{v{c}:i32}} §C{{{tname}}} §A x §A INT:{arg} §/C")
                        acc = " ".join(f"v{c}" for c in range(1, n_calls + 1))
                        expr = acc if n_calls == 1 else f"(+ {acc})"
                        lines.append(f"    §R (+ {expr} y)")
                    else:
                        shift = rng.randrange(1, 90)
                        lines.append(f"    §B{{base:i32}} (- (* x 4) y)")
                        lines.append(f"    §R (+ base {shift})")
            files[f"src/{module_name(layer, mod).lower()}.calr"] = "\n".join(lines) + "\n"

    # Main module: layer 0, calls into L1 flow functions, the only effects.
    lines = ["§M{m001:Main}", "", "  §F{f001:Main:pub} () -> void", "    §E{cw}"]
    entries = random.Random(seed + 1).sample(flow_fns[1], 6)
    for c, (tmod, tidx) in enumerate(entries, 1):
        called.add((1, tmod, tidx))
        tname = fn_name(1, tmod, tidx)
        lines.append(f"    §B{{r{c}:i32}} §C{{{tname}}} §A INT:{c * 3} §A INT:{c} §/C")
    for c in range(1, len(entries) + 1):
        lines.append(f"    §P r{c}")
    files["src/main.calr"] = "\n".join(lines) + "\n"

    return files, const_of, called, calc_fns


def generate_edits(seed, files, const_of, called, calc_fns):
    """Seeded timed-edit sequence: mostly safe constant bumps on calc
    functions, a stated minority of breaking renames of flow functions that
    have ≥1 caller (so every rename really dangles). A safe edit's `find`
    constant occurs only inside its target function (constants are globally
    unique and ≥5 digits) and a renamed definition marker occurs once per
    file — application is replace-ALL on current content."""
    rng = random.Random(seed + 2)
    edits = []
    current = dict(const_of)

    rename_pool = rng.sample(sorted(called), min(BREAKING_EDITS, len(called)))

    safe_stream = [calc_fns[rng.randrange(len(calc_fns))]
                   for _ in range(SAFE_EDITS)]
    slots = sorted(rng.sample(range(SAFE_EDITS + len(rename_pool)), len(rename_pool)))

    seq = 0
    safe_i = 0
    rename_i = 0
    for pos in range(SAFE_EDITS + len(rename_pool)):
        seq += 1
        if rename_i < len(slots) and pos == slots[rename_i]:
            layer, mod, idx = rename_pool[rename_i]
            rename_i += 1
            name = fn_name(layer, mod, idx)
            edits.append({
                "seq": seq,
                "path": f"src/{module_name(layer, mod).lower()}.calr",
                "kind": "breaking",
                "find": f":{name}:pub}}",
                "replace": f":{name}x:pub}}",
                "expectApplied": False,
            })
        else:
            layer, mod, idx = safe_stream[safe_i]
            safe_i += 1
            old = current[(layer, mod, idx)]
            new = old + 1
            current[(layer, mod, idx)] = new
            edits.append({
                "seq": seq,
                "path": f"src/{module_name(layer, mod).lower()}.calr",
                "kind": "safe",
                "find": str(old),
                "replace": str(new),
                "expectApplied": True,
            })
    return edits


README = """# D3.3 latency fixture (loop plan WS3, M4)

Pinned ~10k-line multi-module Calor project for M-L1 / PP-L1 (edit→envelope
latency, P50/P99 over the committed edit script, warm session). Generated by
`../generate-fixture.py --seed {seed}` and **committed**: measurements run
against these bytes in git, never against a regenerator's output.

## Governance (plan §2 D3.3)

- **Owner:** Juan Rivera (repo owner).
- **Regeneration policy:** regenerate and re-baseline on minor version bumps
  of the compiler (Directory.Build.props), or when the syntax the generator
  emits is deprecated. Regeneration is a reviewed PR that re-runs
  `corpus-stats.py`, updates this README's numbers, and re-baselines any
  published M-L1 result.
- **Sample count:** every P50/P99 measurement uses the full committed edit
  script — {n_edits} timed edits ({n_safe} safe, {n_breaking} breaking), ≥ the
  plan's 200 minimum. The breaking minority is deliberate: rejects are part
  of the latency the loop actually experiences.

## Content criteria vs corpus ({corpus_files} files: samples/ + bench pairs)

| Metric | Corpus p50 | Corpus p90 | Fixture | Note |
|---|---|---|---|---|
| lines per module file | {c_lines_p50:.0f} | {c_lines_p90:.0f} | ~{f_lines_per_file:.0f} | sized near p90 so ~10k lines stays ≤ {n_files} files (session cap 2000) |
| functions per module | {c_fpm_p50:.0f} | {c_fpm_p90:.0f} | {funcs_per_module} | p90 |
| contract markers per function | {c_cd_p50:.1f} | {c_cd_p90:.1f} | {f_contract_density:.1f} | corpus is bimodal (p50 0, p90 3); fixture sits between, via ~{calc_pct:.0f}% calc functions carrying 2–3 markers |
| effect markers per function | {c_ed_p50:.1f} | {c_ed_p90:.1f} | 1.0 | corpus constant |
| calls per function | {c_cpf_p50:.1f} | {c_cpf_p90:.1f} | {f_calls_density:.2f} | between p50 and p90 |
| cross-module ref depth | 0 | 0 | 3 | **deliberate deviation, recorded**: the corpus contains no multi-module project at all, while D3.3 mandates one — depth 3 (Main→L1→L2→L3) is the minimum that exercises the session's project-wide reference checks with real fan-in |

Corpus numbers from `../corpus-stats.py` (snapshot in `../corpus-stats.json`).

## Contract forms (constraint recorded 2026-07-27)

Every contract in the fixture verifies **Proven** under `calor verify` at
generation time (checked in the generating PR). The forms are restricted to
the verifier's current proven surface — single-parameter inequality chains,
as in `tests/TestData/Verification/Outcomes/proven.calr` — because at the
time of generation the verifier refutes (a) any postcondition over `result`
(the counterexamples show `result` unconstrained by the body, e.g.
`samples/Verification/proven-contracts.calr` Identity: `§S (== result x)`,
`§R x`, "violated" at x=0/result=-1) and (b) parameter arithmetic that can
overflow i32 bitvectors (honest refutation). (a) is a verifier limitation,
not a fixture choice — if it is fixed, regenerating with richer forms is a
re-baseline per the policy above.

## Layout

- `src/main.calr` — Main module (only effectful function, §E{{cw}}).
- `src/l<layer>m<NN>.calr` — {n_modules} modules, {layers} layers ×
  {mods_per_layer}. Calls flow strictly downward; `calc` functions carry
  Z3-provable contracts and are never called cross-module (no call-site
  obligations); `flow` functions carry the cross-module edges, and the
  breaking-edit targets are drawn only from flow functions with ≥1 caller
  (so every rename really dangles).
- `../edits/edit-script.jsonl` — the timed-edit sequence: one JSONL record
  per edit `{{seq, path, kind, find, replace, expectApplied}}`. Apply by
  replace-ALL string replacement on current file content: a safe edit's
  constant occurs only within its target function (globally unique, ≥5
  digits) and a renamed definition marker occurs once per file. Safe edits
  chain (each bumps the constant the previous left), so the script must run
  in order from the committed fixture state; breaking edits are rejected
  and leave no state behind.

Fixture totals: {total_lines} non-blank lines, {n_files} files,
{total_funcs} functions.
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=SEED)
    ap.add_argument("--out", type=Path, default=HERE / "fixture-10k")
    ap.add_argument("--edits-out", type=Path, default=HERE / "edits")
    args = ap.parse_args()

    files, const_of, called, calc_fns = generate(args.seed)
    edits = generate_edits(args.seed, files, const_of, called, calc_fns)

    for rel, text in files.items():
        target = args.out / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    args.edits_out.mkdir(parents=True, exist_ok=True)
    with open(args.edits_out / "edit-script.jsonl", "w", encoding="utf-8") as f:
        for e in edits:
            f.write(json.dumps(e) + "\n")

    total_lines = sum(len([l for l in t.splitlines() if l.strip()]) for t in files.values())
    total_funcs = sum(t.count("§F{") for t in files.values())
    total_contracts = sum(t.count("§Q") + t.count("§S") for t in files.values())
    total_calls = sum(t.count("§C{") for t in files.values())

    stats = json.loads((HERE / "corpus-stats.json").read_text(encoding="utf-8"))
    m = {row["metric"]: row for row in stats["metrics"]}
    readme = README.format(
        seed=args.seed,
        n_edits=len(edits),
        n_safe=sum(1 for e in edits if e["kind"] == "safe"),
        n_breaking=sum(1 for e in edits if e["kind"] == "breaking"),
        corpus_files=stats["files"],
        c_lines_p50=m["lines_per_module_file"]["p50"],
        c_lines_p90=m["lines_per_module_file"]["p90"],
        f_lines_per_file=total_lines / len(files),
        c_fpm_p50=m["functions_per_module"]["p50"],
        c_fpm_p90=m["functions_per_module"]["p90"],
        funcs_per_module=FUNCS_PER_MODULE,
        c_cd_p50=m["contract_markers_per_function"]["p50"],
        c_cd_p90=m["contract_markers_per_function"]["p90"],
        f_contract_density=total_contracts / total_funcs,
        c_ed_p50=m["effect_markers_per_function"]["p50"],
        c_ed_p90=m["effect_markers_per_function"]["p90"],
        c_cpf_p50=m["calls_per_function"]["p50"],
        c_cpf_p90=m["calls_per_function"]["p90"],
        f_calls_density=total_calls / total_funcs,
        calc_pct=CALC_FRACTION * 100,
        total_lines=total_lines,
        n_files=len(files),
        total_funcs=total_funcs,
        n_modules=LAYERS * MODULES_PER_LAYER,
        layers=LAYERS,
        mods_per_layer=MODULES_PER_LAYER,
    )
    (args.out / "README.md").write_text(readme, encoding="utf-8")

    print(f"fixture: {len(files)} files, {total_lines} non-blank lines, "
          f"{total_funcs} functions, {total_contracts} contract markers, "
          f"{total_calls} calls")
    print(f"edits: {len(edits)} ({sum(1 for e in edits if e['kind'] == 'safe')} safe, "
          f"{sum(1 for e in edits if e['kind'] == 'breaking')} breaking)")


if __name__ == "__main__":
    main()
