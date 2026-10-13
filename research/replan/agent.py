"""A model-driven tool-calling agent for the replanning study (Paper 1).

The model -- not a script -- decides which tools to call and with what
arguments, against the harness-owned tool server. Two knobs define the
condition under test:

--granularity  where durable state is written
    turn   the conversation is persisted only when the run finishes (the
           common "save chat history at end of turn" pattern). Recovery
           re-runs the turn from the goal, so the model is RE-ASKED.
    step   every model response is persisted BEFORE its tool calls execute,
           every tool result after. Recovery reuses the recorded responses and
           re-executes only unfinished tool calls -- the model is never
           re-asked for a decision it already made. (Aurelia's semantics.)

--keys         how idempotency keys are derived
    tool_call_id   the provider-assigned tool call id (what most examples use)
    args_hash      hash of task id + tool name + canonical arguments
    position       task id + tool name + per-run ordinal of that tool

Recovery is the same command re-run with the same --task-id.

Observation (not durable state): every model call and tool call is appended
to --trace, flushed per line, so the harness can see what the killed process
did. The agent never reads its own trace.
"""
import argparse
import hashlib
import json
import os
import sys
import time
import urllib.request

SYSTEM = (
    "You are a customer-service agent. Resolve the customer's request using the "
    "tools, one tool call at a time: first lookup_case, then issue_service_credit "
    "with the caseId and entitledCreditUsd from the lookup result, then "
    "notify_customer with the creditId and caseId from the credit result. "
    "When the customer has been notified, reply with a one-sentence summary."
)

TOOLS = [
    {"type": "function", "function": {
        "name": "lookup_case",
        "description": "Look up a support case by its number.",
        "parameters": {"type": "object",
                       "properties": {"caseId": {"type": "string"}},
                       "required": ["caseId"]}}},
    {"type": "function", "function": {
        "name": "issue_service_credit",
        "description": "Issue the entitled service credit for a case. Side effect: writes to the ledger.",
        "parameters": {"type": "object",
                       "properties": {"caseId": {"type": "string"},
                                      "entitledCreditUsd": {"type": "number"}},
                       "required": ["caseId", "entitledCreditUsd"]}}},
    {"type": "function", "function": {
        "name": "notify_customer",
        "description": "Tell the customer their credit was issued.",
        "parameters": {"type": "object",
                       "properties": {"creditId": {"type": "string"},
                                      "caseId": {"type": "string"}},
                       "required": ["creditId", "caseId"]}}},
]

# Argument coercion a careful engineer writes against the declared schema
# (small models emit 42 for "42"). Applied identically in every condition.
STRING_ARGS = {"caseId", "creditId"}
NUMBER_ARGS = {"entitledCreditUsd"}
MAX_MODEL_CALLS = 8


class Trace:
    def __init__(self, path, invocation):
        self.path, self.invocation = path, invocation

    def write(self, kind, **fields):
        with open(self.path, "a") as f:
            f.write(json.dumps({"inv": self.invocation, "kind": kind,
                                "t": time.time(), **fields}) + "\n")
            f.flush()
            os.fsync(f.fileno())


def post(url, body, timeout):
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as response:
        return json.loads(response.read())


def ask_model(args, messages, seed):
    body = {"model": args.model, "messages": messages, "tools": TOOLS,
            "temperature": args.temperature, "seed": seed}
    return post(f"{args.model_endpoint}/chat/completions", body, 300)["choices"][0]["message"]


def coerce(arguments):
    out = {}
    for name, value in arguments.items():
        if name in STRING_ARGS:
            value = str(value)
        elif name in NUMBER_ARGS:
            try:
                value = float(value)
            except (TypeError, ValueError):
                pass
        out[name] = value
    return out


def derive_key(strategy, task_id, call, name, arguments, ordinal):
    if strategy == "tool_call_id":
        return call["id"]
    if strategy == "args_hash":
        canonical = json.dumps(arguments, sort_keys=True, separators=(",", ":"))
        return hashlib.sha256(f"{task_id}|{name}|{canonical}".encode()).hexdigest()[:24]
    if strategy == "position":
        return f"{task_id}-{name}-{ordinal}"
    raise ValueError(strategy)


def run_tool(server, name, arguments, key):
    body = {"argumentsJson": json.dumps(arguments), "idempotencyKey": key}
    try:
        return json.dumps(json.loads(post(f"{server}/tools/{name}", body, 300)["resultJson"]))
    except urllib.error.HTTPError as exc:
        return json.dumps({"error": exc.read().decode(errors="replace")[:300]})


def load_state(path):
    if not os.path.exists(path):
        return None
    with open(path) as f:
        return json.load(f)


def save_state(path, state):
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        json.dump(state, f)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--task-id", required=True)
    p.add_argument("--goal", required=True)
    p.add_argument("--server", required=True)
    p.add_argument("--state", required=True)
    p.add_argument("--trace", required=True)
    p.add_argument("--granularity", choices=["turn", "step"], required=True)
    p.add_argument("--keys", choices=["tool_call_id", "args_hash", "position"], required=True)
    p.add_argument("--model", required=True)
    p.add_argument("--model-endpoint", default="http://127.0.0.1:11434/v1")
    p.add_argument("--temperature", type=float, required=True)
    p.add_argument("--seed", type=int, required=True)
    p.add_argument("--invocation", type=int, default=0)
    args = p.parse_args()
    trace = Trace(args.trace, args.invocation)

    state = load_state(args.state)
    if state and state.get("done"):
        print(f"FINAL {state['final']}")
        return 0
    if state is None or args.granularity == "turn":
        # turn: nothing durable mid-run, so recovery starts the turn over.
        state = {"messages": [{"role": "system", "content": SYSTEM},
                              {"role": "user", "content": args.goal}],
                 "model_calls": 0}
    trace.write("start", resumed_messages=len(state["messages"]))

    def persist_step():
        if args.granularity == "step":
            save_state(args.state, state)

    def ordinal(name, upto):
        # Calls of this tool earlier in the conversation (per-run, not per-attempt).
        n = 0
        for m in state["messages"][:upto]:
            for c in m.get("tool_calls") or []:
                n += c["function"]["name"] == name
        return n

    while True:
        last = state["messages"][-1]
        pending = []
        if last["role"] == "assistant" and last.get("tool_calls"):
            done_ids = set()
            pending = list(last["tool_calls"])
        elif last["role"] == "tool":
            # Find the assistant message whose calls are not all answered.
            i = len(state["messages"]) - 1
            while state["messages"][i]["role"] == "tool":
                i -= 1
            done_ids = {m["tool_call_id"] for m in state["messages"][i + 1:]}
            pending = [c for c in state["messages"][i].get("tool_calls") or []
                       if c["id"] not in done_ids]

        if pending:
            idx = len(state["messages"]) - 1
            while state["messages"][idx]["role"] != "assistant":
                idx -= 1
            for call in pending:
                name = call["function"]["name"]
                try:
                    arguments = coerce(json.loads(call["function"]["arguments"] or "{}"))
                except json.JSONDecodeError:
                    arguments = {}
                prior = ordinal(name, idx)
                same_msg = [c for c in state["messages"][idx]["tool_calls"]
                            if c["function"]["name"] == name]
                key = derive_key(args.keys, args.task_id, call, name, arguments,
                                 prior + same_msg.index(call))
                trace.write("tool_call", name=name, arguments=arguments, key=key)
                result = run_tool(args.server, name, arguments, key)
                trace.write("tool_result", name=name, key=key, result=result)
                state["messages"].append({"role": "tool", "tool_call_id": call["id"],
                                          "content": result})
                persist_step()
            continue

        if state["model_calls"] >= MAX_MODEL_CALLS:
            trace.write("give_up")
            print("ERROR model-call budget exhausted", file=sys.stderr)
            return 1

        # Seed differs per invocation: a recovery is a new request to the
        # provider, as in production (no provider guarantees replay-stable
        # sampling). Reproducible from --seed.
        seed = args.seed * 1000 + args.invocation * 100 + state["model_calls"]
        message = ask_model(args, state["messages"], seed)
        state["model_calls"] += 1
        calls = message.get("tool_calls") or []
        trace.write("model", tool_calls=[{"id": c["id"], "name": c["function"]["name"],
                                          "arguments": c["function"]["arguments"]}
                                         for c in calls],
                    content=(message.get("content") or "")[:300])
        state["messages"].append({"role": "assistant",
                                  "content": message.get("content") or "",
                                  **({"tool_calls": calls} if calls else {})})
        persist_step()
        if not calls:
            state["done"] = True
            state["final"] = (message.get("content") or "").replace("\n", " ")[:300]
            save_state(args.state, state)  # end of turn: both granularities persist
            trace.write("final")
            print(f"FINAL {state['final']}")
            return 0


if __name__ == "__main__":
    sys.exit(main())
