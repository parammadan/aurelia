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
GOAL = "Hi, case 42 has been broken all week. I'd like the service credit I'm owed."


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
    return [sys.executable, "-I", str(HERE / "agent.py"),
            "--task-id", TASK_ID, "--goal", GOAL, "--server", server,
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
        notified = any(e["kind"] == "tool_result" and e["name"] == "notify_customer"
                       and '"notified": true' in e["result"] and e["inv"] == 1 for e in trace)
        reasked = any(e["kind"] == "model" and e["inv"] == 1 and
                      any(c["name"] == "issue_service_credit" for c in e["tool_calls"]) for e in trace)
        completed = recovery_exit == 0 and notified
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
            "correct": completed and len(credits) == 1,
            "duplicate": len(credits) > 1,
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
    for model, gran, keys, temp in itertools.product(
            f["model"], f["granularity"], f["keys"], f["temperature"]):
        yield {"model": model, "granularity": gran, "keys": keys, "temperature": temp}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--config", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--only-cell", type=int)
    p.add_argument("--n", type=int, help="override N (smoke tests only)")
    args = p.parse_args()
    config = json.loads(pathlib.Path(args.config).read_text())
    if not TOOLSERVER.exists():
        sys.exit(f"build first: dotnet build -c Release src/Aurelia.ToolServer  ({TOOLSERVER})")

    n = args.n or config["n_per_cell"]
    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    done = {(r["model"], r["granularity"], r["keys"], r["temperature"], r["seed"])
            for r in read_jsonl(out)}  # resumable
    all_cells = list(cells(config))
    chosen = [all_cells[args.only_cell]] if args.only_cell is not None else all_cells
    for ci, cell in enumerate(chosen):
        for i in range(n):
            seed = config["base_seed"] + i
            if (cell["model"], cell["granularity"], cell["keys"], cell["temperature"], seed) in done:
                continue
            row = run_once(cell, seed, config["kill_wait_s"], config["recovery_timeout_s"])
            with out.open("a") as f:
                f.write(json.dumps(row) + "\n")
            print(f"[{ci + 1}/{len(chosen)} run {i + 1}/{n}] {cell} kill={row['kill_triggered']} "
                  f"credits={row['credits']} correct={row['correct']}", flush=True)


if __name__ == "__main__":
    main()
