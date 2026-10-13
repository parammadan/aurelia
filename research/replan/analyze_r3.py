"""Round 3 analysis, as pre-registered (docs/research/preregistration-r3.md):
crash-recovered runs (R2) vs crash-free runs (R3), pooled over models and
temperatures, per task x keys, with Newcombe's hybrid-score 95% interval for
the difference of two proportions. Denominators: R2 runs where the kill
fired; R3 runs where the model reached the credit tool.

Usage: python3 analyze_r3.py data/r2.jsonl data/r3.jsonl > ../../docs/research/results-r3.md
"""
import json
import sys

from analyze import wilson

TASKS = ("credit", "memo", "two")
KEYS = ("tool_call_id", "args_hash", "position")


def load(path):
    return [json.loads(l) for l in open(path) if l.strip()]


def newcombe(k1, n1, k2, n2):
    """Newcombe (1998) method 10: difference p1 - p2 with Wilson-score limits."""
    p1, p2 = k1 / n1, k2 / n2
    l1, u1 = wilson(k1, n1)
    l2, u2 = wilson(k2, n2)
    d = p1 - p2
    lo = d - ((p1 - l1) ** 2 + (u2 - p2) ** 2) ** 0.5
    hi = d + ((u1 - p1) ** 2 + (p2 - l2) ** 2) ** 0.5
    return d, lo, hi


def rate(rows, field):
    return sum(r[field] for r in rows), len(rows)


def fmt(k, n):
    return "—" if n == 0 else f"{k}/{n} ({100 * k / n:.0f}%)"


def diff_cell(a, b):
    (k1, n1), (k2, n2) = a, b
    if not n1 or not n2:
        return "—", None
    d, lo, hi = newcombe(k1, n1, k2, n2)
    return f"{100 * d:+.0f} pp [{100 * lo:+.0f}, {100 * hi:+.0f}]", (lo, hi)


def main(r2_path, r3_path):
    r2 = load(r2_path)
    r3 = load(r3_path)

    def rec(gran, task, keys):
        return [r for r in r2 if r["granularity"] == gran and r["task"] == task
                and r["keys"] == keys and r["kill_triggered"]]

    def free(task, keys):
        return [r for r in r3 if r["task"] == task and r["keys"] == keys and r["reached_credit"]]

    print("# Paper 1 — round 3 results (crash-free control)\n")
    print(f"Sources: `{r2_path}` (recovered) · `{r3_path}` ({len(r3)} crash-free runs). "
          "Pooled over both models and all temperatures. Intervals: Newcombe hybrid score, 95%.\n")

    results = {}
    for gran in ("step", "turn"):
        print(f"## {gran}-recovered (R2) vs crash-free (R3)\n")
        print("| task | keys | dup recovered | dup crash-free | Δ dup [95% CI] | lost recovered | lost crash-free | Δ lost [95% CI] |")
        print("|---|---|---|---|---|---|---|---|")
        for task in TASKS:
            for keys in KEYS:
                a, b = rec(gran, task, keys), free(task, keys)
                dd, dci = diff_cell(rate(a, "duplicate"), rate(b, "duplicate"))
                ld, lci = diff_cell(rate(a, "lost_effect"), rate(b, "lost_effect"))
                results[(gran, task, keys)] = (dci, lci, a, b)
                print(f"| {task} | {keys} | {fmt(*rate(a, 'duplicate'))} | {fmt(*rate(b, 'duplicate'))} | {dd} | "
                      f"{fmt(*rate(a, 'lost_effect'))} | {fmt(*rate(b, 'lost_effect'))} | {ld} |")
        print()

    def verdict(ok):
        return "⏳ pending" if ok is None else "✅ held" if ok else "❌ falsified"

    # R3-H1: step-recovered vs crash-free, both intervals include 0, every task x keys.
    h1 = []
    for task in TASKS:
        for keys in KEYS:
            dci, lci, _, _ = results[("step", task, keys)]
            if dci is None or lci is None:
                h1.append(None)
            else:
                h1.append(dci[0] <= 0 <= dci[1] and lci[0] <= 0 <= lci[1])
    h1v = None if any(x is None for x in h1) else all(h1)
    # R3-H2: turn + tool_call_id duplicate rate higher by >= 30 pp in credit and memo.
    h2 = []
    for task in ("credit", "memo"):
        _, _, a, b = results[("turn", task, "tool_call_id")]
        ka, na = rate(a, "duplicate")
        kb, nb = rate(b, "duplicate")
        h2.append(None if not na or not nb else (ka / na - kb / nb) >= 0.30)
    h2v = None if any(x is None for x in h2) else all(h2)
    # R3-H3: crash-free dup rate under args_hash <= under tool_call_id, every task.
    h3 = []
    for task in TASKS:
        ka, na = rate(free(task, "args_hash"), "duplicate")
        kb, nb = rate(free(task, "tool_call_id"), "duplicate")
        h3.append(None if not na or not nb else ka / na <= kb / nb)
    h3v = None if any(x is None for x in h3) else all(h3)

    failing = [f"{t}/{k}" for (t, k), ok in zip([(t, k) for t in TASKS for k in KEYS], h1) if ok is False]
    print("## Pre-registered hypotheses\n")
    print("| # | prediction | held? | detail |")
    print("|---|---|---|---|")
    print(f"| R3-H1 | step-recovered vs crash-free: both Δ intervals include 0, every task × keys | {verdict(h1v)} | "
          f"{'cells excluding 0: ' + ', '.join(failing) if failing else 'all cells include 0'} |")
    print(f"| R3-H2 | turn + tool_call_id: duplicates ≥ 30 pp above crash-free in credit and memo | {verdict(h2v)} | |")
    print(f"| R3-H3 | crash-free: args_hash duplicate rate ≤ tool_call_id, every task | {verdict(h3v)} | |")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
