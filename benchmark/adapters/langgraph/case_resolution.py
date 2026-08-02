"""The benchmark task implemented idiomatically in LangGraph, per its
documented durable-execution guidance: a StateGraph with a Sqlite
checkpointer, thread_id = task id, and recovery by resuming the thread
(invoke(None, config) continues from the last checkpoint).

Fairness notes (methodology §4):
- Tools are the harness-owned HTTP tool server; no framework internals leak.
- Idempotency keys are DETERMINISTIC (task_id/node) — the best-effort
  implementation a careful engineer following the docs would write. Pass
  --uuid-keys to get the fresh-key-per-attempt variant instead (what most
  published examples do), for comparison.
- Terminal-state query (--query) answers from the checkpoint, LangGraph's
  own durable state surface.
"""
import argparse
import json
import sys
import urllib.request
import uuid

from langgraph.checkpoint.sqlite import SqliteSaver
from langgraph.graph import END, START, StateGraph
from typing_extensions import TypedDict


def call_tool(server: str, name: str, args_json: str, key: str) -> str:
    body = json.dumps({"argumentsJson": args_json, "idempotencyKey": key}).encode()
    req = urllib.request.Request(
        f"{server}/tools/{name}", data=body,
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as response:
        return json.loads(response.read())["resultJson"]


class State(TypedDict, total=False):
    goal: str
    case: str
    credit: str
    notified: str


def build_graph(server: str, task_id: str, deterministic_keys: bool):
    def key(node: str) -> str:
        return f"{task_id}/{node}" if deterministic_keys else uuid.uuid4().hex

    def lookup(state: State):
        return {"case": call_tool(server, "lookup_case", state["goal"], key("lookup"))}

    def credit(state: State):
        return {"credit": call_tool(server, "issue_service_credit", state["case"], key("credit"))}

    def notify(state: State):
        return {"notified": call_tool(server, "notify_customer", state["credit"], key("notify"))}

    graph = StateGraph(State)
    graph.add_node("lookup", lookup)
    graph.add_node("credit", credit)
    graph.add_node("notify", notify)
    graph.add_edge(START, "lookup")
    graph.add_edge("lookup", "credit")
    graph.add_edge("credit", "notify")
    graph.add_edge("notify", END)
    return graph


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task-id", required=True)
    parser.add_argument("--goal", required=True)
    parser.add_argument("--server", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--uuid-keys", action="store_true")
    parser.add_argument("--query", action="store_true")
    args = parser.parse_args()

    config = {"configurable": {"thread_id": args.task_id}}
    with SqliteSaver.from_conn_string(args.db) as saver:
        app = build_graph(args.server, args.task_id, not args.uuid_keys) \
            .compile(checkpointer=saver)

        if args.query:
            # LangGraph's durable answer to "what happened?": the checkpoint.
            snapshot = app.get_state(config)
            values = snapshot.values if snapshot else {}
            if values.get("notified"):
                print(json.dumps({"status": "completed",
                                  "compensatedEffects": [], "leakedEffects": []}))
                return 0
            return 3  # no terminal answer available

        # Recovery per the docs: a thread with prior checkpoints resumes with
        # input=None; a fresh thread starts with the goal.
        existing = app.get_state(config)
        has_checkpoint = bool(existing and existing.values)
        try:
            result = app.invoke(None if has_checkpoint else {"goal": args.goal}, config)
        except Exception as exc:  # noqa: BLE001 — the harness reads stderr
            print(f"ERROR {exc}", file=sys.stderr)
            return 1
        print(f"FINAL_STATE {result.get('notified', '')}")
        return 0


if __name__ == "__main__":
    sys.exit(main())
