"""Analysis for the replanning study, exactly as pre-registered
(docs/research/preregistration.md): duplicate rate over kill-triggered runs
with Wilson 95% CIs, exclusions reported per cell, hypotheses H1-H6 checked
mechanically against their stated bounds.

Usage: python3 analyze.py data/r1.jsonl > ../../docs/research/results-r1.md
"""
import collections
import json
import math
import sys


def wilson(k, n, z=1.96):
    if n == 0:
        return (float("nan"), float("nan"))
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (max(0.0, c - h), min(1.0, c + h))


def pct(k, n):
    return "—" if n == 0 else f"{100 * k / n:.0f}%"


def main(path):
    rows = [json.loads(l) for l in open(path) if l.strip()]
    cells = collections.OrderedDict()
    for r in rows:
        cells.setdefault((r["model"], r["granularity"], r["keys"], r["temperature"]), []).append(r)

    stats = {}
    for key, rs in cells.items():
        killed = [r for r in rs if r["kill_triggered"]]
        stats[key] = {
            "runs": len(rs),
            "excluded": len(rs) - len(killed),
            "n": len(killed),
            "dup": sum(r["duplicate"] for r in killed),
            "reask": sum(r["recovery_reasked_for_credit"] for r in killed),
            "completed": sum(r["completed"] for r in killed),
            "correct": sum(r["correct"] for r in killed),
        }

    print("# Paper 1 — round 1 results\n")
    print(f"Source: `{path}` · {len(rows)} runs · analysis per the approved pre-registration.\n")
    print("| model | granularity | keys | T | runs | excl. | duplicate rate (95% CI) | re-ask | completed | correct |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for (m, g, k, t), s in stats.items():
        lo, hi = wilson(s["dup"], s["n"])
        ci = "—" if s["n"] == 0 else f"{pct(s['dup'], s['n'])} ({100 * lo:.0f}–{100 * hi:.0f})"
        print(f"| {m} | {g} | {k} | {t} | {s['runs']} | {s['excluded']} | {ci} | "
              f"{pct(s['reask'], s['n'])} | {pct(s['completed'], s['n'])} | {pct(s['correct'], s['n'])} |")

    def sel(**f):
        return [s for (m, g, k, t), s in stats.items()
                if all({"model": m, "granularity": g, "keys": k}[a] == b for a, b in f.items())]

    def rate(s, field):
        return s[field] / s["n"] if s["n"] else None

    checks = []
    step = sel(granularity="step")
    step_n = [s for s in step if s["n"]]
    checks.append(("H1", "step: duplicate rate = 0 in all cells",
                   all(s["dup"] == 0 for s in step_n) if step_n else None,
                   sum(s["dup"] for s in step_n), "duplicates total"))
    for h, keys, test, desc in [
        ("H2", "tool_call_id", lambda r: r >= 0.9, "turn + tool_call_id: ≥ 90% every cell"),
        ("H3", "args_hash", lambda r: r <= 0.1, "turn + args_hash: ≤ 10% every cell"),
        ("H4", "position", lambda r: r <= 0.1, "turn + position: ≤ 10% every cell"),
    ]:
        rs = [rate(s, "dup") for s in sel(granularity="turn", keys=keys) if s["n"]]
        checks.append((h, desc, all(test(r) for r in rs) if rs else None,
                       f"{min(rs):.0%}–{max(rs):.0%}" if rs else "no data", "cell range"))
    turn_reask = [rate(s, "reask") for s in sel(granularity="turn") if s["n"]]
    step_reask = [rate(s, "reask") for s in step if s["n"]]
    checks.append(("H5", "re-ask: turn ≥ 90%, step = 0%",
                   (min(turn_reask) >= 0.9 and all(r == 0 for r in step_reask))
                   if turn_reask and step_reask else None,
                   f"turn min {min(turn_reask):.0%}, step max {max(step_reask):.0%}"
                   if turn_reask and step_reask else "no data", ""))
    for model, bound in [("qwen2.5:3b", 0.8), ("llama3.2:3b", 0.6)]:
        ss = sel(model=model)
        n = sum(s["n"] for s in ss)
        c = sum(s["completed"] for s in ss) / n if n else None
        checks.append(("H6", f"completion {model} ≥ {bound:.0%} (descriptive)",
                       None if c is None else c >= bound, f"{c:.0%}" if c is not None else "no data", ""))

    print("\n## Pre-registered hypotheses\n")
    print("| # | prediction | held? | observed |")
    print("|---|---|---|---|")
    for h, desc, ok, obs, unit in checks:
        print(f"| {h} | {desc} | {'⏳ pending' if ok is None else '✅ held' if ok else '❌ falsified'} | {obs} {unit} |")


if __name__ == "__main__":
    main(sys.argv[1])
