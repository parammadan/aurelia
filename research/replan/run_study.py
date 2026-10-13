"""Harness for the replanning study (Paper 1).

Per run: start the harness-owned tool server (src/Aurelia.ToolServer) with the
after-effect kill window armed; start the agent; wait until the server has
WRITTEN the credit to the ledger and is holding the response; SIGKILL the
agent; release the window; run the agent's recovery (same command); then
score from the ledger (ground truth) and the observation trace.

Usage:
  python3 run_study.py --config config.json --out data/<label>.jsonl
  python3 run_study.py --config config.json --out ... --only-cell 0 --n 2   # smoke
"""
import argparse
import concurrent.futures
import itertools
import json
import os
import pathlib
import shutil
import signal
import subprocess
import sys
import tempfile
import time

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parent.parent
TOOLSERVER = REPO / "src/Aurelia.ToolServer/bin/Release/net10.0/Aurelia.ToolServer.dll"
TASK_ID = "case42"
GOALS = {
    "credit": "Hi, case 42 has been broken all week. I'd like the service credit I'm owed.",
    "memo": "Hi, case 42 has been broken all week. I'd like the service credit I'm owed.",
    "two": "Hi, cases 42 and 7 have both been broken all week. I'd like the service credits I'm owed on both.",
}
EXPECTED_CASES = {"credit": ["42"], "memo": ["42"], "two": ["42", "7"]}
MODEL_ENDPOINT = os.environ.get("MODEL_ENDPOINT", "http://127.0.0.1:11434/v1")


def start_server(store, signals):
    env = dict(os.environ, AURELIA_SIGNAL_DIR=str(signals), AURELIA_SIGNAL_AFTER_EFFECT="1")
    proc = subprocess.Popen(["dotnet", str(TOOLSERVER), "--data-dir", str(store)],
                            env=env, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    deadline = time.time() + 30
    while time.time() < deadline:
        line = proc.stdout.readline()
        if line.startswith("LISTENING "):
            return proc, line.split(" ", 1)[1].strip()
    proc.kill()
    raise RuntimeError("tool server did not start")


def agent_cmd(cell, run_seed, server, work, invocation):
    task = cell.get("task", "credit")
    return [sys.executable, "-I", str(HERE / "agent.py"),
            "--task-id", TASK_ID, "--goal", GOALS[task], "--task", task,
            "--model-endpoint", MODEL_ENDPOINT, "--server", server,
            "--state", str(work / "state.json"), "--trace", str(work / "trace.jsonl"),
            "--granularity", cell["granularity"], "--keys", cell["keys"],
            "--model", cell["model"], "--temperature", str(cell["temperature"]),
            "--seed", str(run_seed), "--invocation", str(invocation)]


def read_jsonl(path):
    if not path.exists():
        return []
    return [json.loads(l) for l in path.read_text().splitlines() if l.strip()]


def run_once(cell, run_seed, kill_wait_s, recovery_timeout_s):
    work = pathlib.Path(tempfile.mkdtemp(prefix="replan-"))
    store, signals = work / "store", work / "signals"
    store.mkdir(), signals.mkdir()
    (signals / "block").write_text("")
    server, url = start_server(store, signals)
    try:
        victim = subprocess.Popen(agent_cmd(cell, run_seed, url, work, 0),
                                  stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        deadline = time.time() + kill_wait_s
        killed = False
        while time.time() < deadline and victim.poll() is None:
            if any(signals.glob("*.started")):
                victim.send_signal(signal.SIGKILL)
                victim.wait()
                killed = True
                break
            time.sleep(0.05)
        if not killed and victim.poll() is None:
            victim.kill()
            victim.wait()
        (signals / "block").unlink()
        time.sleep(0.3)  # let the held request finish at the server

        recovery_exit, recovery_out = None, ""
        if killed:
            try:
                r = subprocess.run(agent_cmd(cell, run_seed, url, work, 1),
                                   capture_output=True, text=True, timeout=recovery_timeout_s)
                recovery_exit, recovery_out = r.returncode, r.stdout + r.stderr
            except subprocess.TimeoutExpired:
                recovery_exit = -2

        ledger = read_jsonl(store / "ledger.jsonl")
        trace = read_jsonl(work / "trace.jsonl")
        credits = [e for e in ledger if e["type"] == "credit"]
        credit_calls = [e for e in trace if e["kind"] == "tool_call" and e["name"] == "issue_service_credit"]
        expected = EXPECTED_CASES[cell.get("task", "credit")]
        per_case = {c: sum(1 for e in credits if str(e["caseId"]) == c) for c in expected}
        notified_cases = set()
        for e in trace:
            if e["kind"] == "tool_result" and e["name"] == "notify_customer":
                try:
                    res = json.loads(e["result"])
                except json.JSONDecodeError:
                    continue
                if res.get("notified") is True:
                    notified_cases.add(str(res.get("caseId")))
        notified = all(c in notified_cases for c in expected)
        reasked = any(e["kind"] == "model" and e["inv"] == 1 and
                      any(c["name"] == "issue_service_credit" for c in e["tool_calls"]) for e in trace)
        completed = recovery_exit == 0 and notified
        lost = completed and any(n == 0 for n in per_case.values())
        return {
            **cell, "seed": run_seed,
            "kill_triggered": killed,
            "credits": len(credits),
            "credit_keys": [c["key"] for c in credits],
            "credit_amounts": sorted({c["amountUsd"] for c in credits}),
            "credit_calls": [{"inv": c["inv"], "key": c["key"], "arguments": c["arguments"]}
                             for c in credit_calls],
            "recovery_reasked_for_credit": reasked,
            "recovery_exit": recovery_exit,
            "completed": completed,
            "credits_per_case": per_case,
            "correct": completed and all(n == 1 for n in per_case.values()) and len(credits) == len(expected),
            "duplicate": any(n > 1 for n in per_case.values()),
            "lost_effect": lost,
            "model_calls": sum(e["kind"] == "model" for e in trace),
            "victim_tail": "" if killed else victim.stdout.read()[-300:],
            "recovery_tail": recovery_out[-300:],
        }
    finally:
        server.kill()
        server.wait()
        shutil.rmtree(work, ignore_errors=True)


def cells(config):
    f = config["factors"]
    for task, model, gran, keys, temp in itertools.product(
            f.get("task", ["credit"]), f["model"], f["granularity"], f["keys"], f["temperature"]):
        cell = {"model": model, "granularity": gran, "keys": keys, "temperature": temp}
        if "task" in f:
            cell = {"task": task, **cell}
        yield cell


def row_key(r):
    return (r.get("task", "credit"), r["model"], r["granularity"], r["keys"], r["temperature"], r["seed"])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--config", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--only-cell", type=int)
    p.add_argument("--n", type=int, help="override N (smoke tests only)")
    p.add_argument("--workers", type=int, default=1, help="concurrent runs (isolated dirs and servers)")
    args = p.parse_args()
    config = json.loads(pathlib.Path(args.config).read_text())
    if not TOOLSERVER.exists():
        sys.exit(f"build first: dotnet build -c Release src/Aurelia.ToolServer  ({TOOLSERVER})")

    n = args.n or config["n_per_cell"]
    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    done = {row_key(r) for r in read_jsonl(out)}  # resumable
    all_cells = list(cells(config))
    chosen = [all_cells[args.only_cell]] if args.only_cell is not None else all_cells
    jobs = [(cell, config["base_seed"] + i) for cell in chosen for i in range(n)
            if row_key({**cell, "seed": config["base_seed"] + i}) not in done]
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(run_once, cell, seed, config["kill_wait_s"],
                               config["recovery_timeout_s"]): (cell, seed) for cell, seed in jobs}
        for count, fut in enumerate(concurrent.futures.as_completed(futures), 1):
            row = fut.result()
            with out.open("a") as f:
                f.write(json.dumps(row) + "\n")
            print(f"[{count}/{len(jobs)}] {futures[fut][0]} seed={row['seed']} kill={row['kill_triggered']} "
                  f"credits={row['credits']} correct={row['correct']}", flush=True)


if __name__ == "__main__":
    main()
