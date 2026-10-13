"""Round 2 analysis, as pre-registered (docs/research/preregistration-r2.md):
per-task duplicate and lost-effect rates over kill-triggered runs with Wilson
95% CIs, exclusions per cell, R2-H1..H5 checked against their stated bounds.
The "credit" task replicates R1 on the cluster hardware and is reported
beside R1, not pooled with it.

Usage: python3 analyze_r2.py data/r2.jsonl > ../../docs/research/results-r2.md
"""
import collections
import json
import sys

from analyze import pct, wilson


def main(path):
    rows = [json.loads(l) for l in open(path) if l.strip()]
    stats = collections.OrderedDict()
    for r in rows:
        k = (r["task"], r["model"], r["granularity"], r["keys"], r["temperature"])
        s = stats.setdefault(k, {"runs": 0, "n": 0, "dup": 0, "lost": 0, "reask": 0, "completed": 0, "correct": 0})
        s["runs"] += 1
        if not r["kill_triggered"]:
            continue
        s["n"] += 1
        s["dup"] += r["duplicate"]
        s["lost"] += r["lost_effect"]
        s["reask"] += r["recovery_reasked_for_credit"]
        s["completed"] += r["completed"]
        s["correct"] += r["correct"]

    def ci(k, n):
        if n == 0:
            return "—"
        lo, hi = wilson(k, n)
        return f"{pct(k, n)} ({100 * lo:.0f}–{100 * hi:.0f})"

    print("# Paper 1 — round 2 results\n")
    print(f"Source: `{path}` · {len(rows)} runs · analysis per the approved R2 pre-registration.\n")
    print("| task | model | granularity | keys | T | runs | excl. | duplicate (95% CI) | lost effect (95% CI) | re-ask | completed | correct |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for (task, m, g, k, t), s in stats.items():
        print(f"| {task} | {m} | {g} | {k} | {t} | {s['runs']} | {s['runs'] - s['n']} | {ci(s['dup'], s['n'])} | "
              f"{ci(s['lost'], s['n'])} | {pct(s['reask'], s['n'])} | {pct(s['completed'], s['n'])} | {pct(s['correct'], s['n'])} |")

    def cells(**f):
        names = ("task", "model", "granularity", "keys", "temperature")
        return [s for key, s in stats.items() if s["n"]
                and all(dict(zip(names, key))[a] == b for a, b in f.items())]

    def rates(field, **f):
        return [s[field] / s["n"] for s in cells(**f)]

    def verdict(ok):
        return "⏳ pending" if ok is None else "✅ held" if ok else "❌ falsified"

    checks = []
    step = cells(granularity="step")
    checks.append(("R2-H1", "step: 0 duplicates and 0 lost effects, every cell",
                   (all(s["dup"] == 0 and s["lost"] == 0 for s in step) if step else None),
                   f"{sum(s['dup'] for s in step)} dup, {sum(s['lost'] for s in step)} lost"))
    t0 = rates("dup", task="memo", granularity="turn", keys="args_hash", temperature=0.0)
    t1 = rates("dup", task="memo", granularity="turn", keys="args_hash", temperature=1.0)
    checks.append(("R2-H2", "memo, turn+args_hash: ≤ 10% at T=0, ≥ 50% at T=1",
                   (all(r <= 0.1 for r in t0) and all(r >= 0.5 for r in t1)) if t0 and t1 else None,
                   f"T=0 {[f'{r:.0%}' for r in t0]}, T=1 {[f'{r:.0%}' for r in t1]}"))
    h3 = rates("dup", task="memo", granularity="turn", keys="position")
    checks.append(("R2-H3", "memo, turn+position: ≤ 10% every cell",
                   all(r <= 0.1 for r in h3) if h3 else None,
                   f"{min(h3):.0%}–{max(h3):.0%}" if h3 else "no data"))
    h4 = [s for s in cells(task="two", granularity="turn", keys="position")]
    hot = [s for (task, m, g, k, t), s in stats.items()
           if s["n"] and task == "two" and g == "turn" and k == "position" and t >= 0.7]
    checks.append(("R2-H4", "two, turn+position: lost effect > 0 at T ≥ 0.7",
                   (sum(s["lost"] for s in hot) > 0) if hot else None,
                   f"{sum(s['lost'] for s in h4)} lost across {sum(s['n'] for s in h4)} runs"))
    h5 = rates("dup", task="two", granularity="turn", keys="args_hash")
    checks.append(("R2-H5", "two, turn+args_hash: ≤ 10% duplicates every cell",
                   all(r <= 0.1 for r in h5) if h5 else None,
                   f"{min(h5):.0%}–{max(h5):.0%}" if h5 else "no data"))

    print("\n## Pre-registered hypotheses\n")
    print("| # | prediction | held? | observed |")
    print("|---|---|---|---|")
    for h, desc, ok, obs in checks:
        print(f"| {h} | {desc} | {verdict(ok)} | {obs} |")


if __name__ == "__main__":
    main(sys.argv[1])
