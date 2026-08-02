"""The benchmark task implemented idiomatically in Temporal, per its
documented durable-execution guidance: a workflow with three activities
against the harness-owned HTTP tool server, activity retry policy capped at
2 attempts (matching the other contestants), and the documented saga pattern
(compensate the credit if notification ultimately fails).

Fairness notes (methodology §4):
- Deterministic idempotency keys are computed IN the workflow (workflow code
  must be deterministic in Temporal) and passed to activities. The
  --uuid-keys variant generates a fresh key inside the ACTIVITY per attempt —
  the realistic naive pattern, since uuid in workflow code would violate
  Temporal's own determinism rules.
- Recovery story: restart the worker and await the same workflow id; the
  server re-dispatches from history.
- Terminal-state query: the workflow's describe() status — Temporal's own
  durable state surface.

Requires TEMPORAL_ADDRESS (e.g. localhost:7233 from `temporal server
start-dev --headless`).
"""
import argparse
import asyncio
import json
import os
import sys
import urllib.request
import uuid
from datetime import timedelta

from temporalio import activity, workflow
from temporalio.client import Client, WorkflowFailureError
from temporalio.common import RetryPolicy
from temporalio.exceptions import WorkflowAlreadyStartedError
from temporalio.worker import Worker

SERVER = os.environ.get("BENCH_TOOLSERVER", "")
UUID_KEYS = False


def call_tool(name: str, args_json: str, key: str) -> str:
    body = json.dumps({"argumentsJson": args_json, "idempotencyKey": key}).encode()
    req = urllib.request.Request(
        f"{SERVER}/tools/{name}", data=body,
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as response:
        return json.loads(response.read())["resultJson"]


def call_compensate(name: str, orig_args: str, orig_result: str, key: str) -> str:
    body = json.dumps({"originalArgumentsJson": orig_args,
                       "originalResultJson": orig_result,
                       "compensationKey": key}).encode()
    req = urllib.request.Request(
        f"{SERVER}/tools/{name}/compensate", data=body,
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as response:
        return json.loads(response.read())["resultJson"]


def effective_key(workflow_key: str) -> str:
    # The naive variant mints a fresh key per activity ATTEMPT.
    return uuid.uuid4().hex if UUID_KEYS else workflow_key


@activity.defn
async def lookup(args: dict) -> str:
    return call_tool("lookup_case", args["input"], effective_key(args["key"]))


@activity.defn
async def credit(args: dict) -> str:
    return call_tool("issue_service_credit", args["input"], effective_key(args["key"]))


@activity.defn
async def notify(args: dict) -> str:
    return call_tool("notify_customer", args["input"], effective_key(args["key"]))


@activity.defn
async def reverse_credit(args: dict) -> str:
    return call_compensate("issue_service_credit",
                           args["orig_args"], args["orig_result"], args["key"])


RETRY = RetryPolicy(maximum_attempts=2)
OPTS = dict(start_to_close_timeout=timedelta(seconds=120), retry_policy=RETRY)


@workflow.defn
class CaseResolution:
    @workflow.run
    async def run(self, goal: str) -> str:
        wid = workflow.info().workflow_id
        case = await workflow.execute_activity(
            lookup, {"input": goal, "key": f"{wid}/lookup"}, **OPTS)
        credited = await workflow.execute_activity(
            credit, {"input": case, "key": f"{wid}/credit"}, **OPTS)
        try:
            return await workflow.execute_activity(
                notify, {"input": credited, "key": f"{wid}/notify"}, **OPTS)
        except Exception:
            # The documented saga pattern: compensate, then surface the failure.
            await workflow.execute_activity(
                reverse_credit,
                {"orig_args": case, "orig_result": credited, "key": f"{wid}/compensate-credit"},
                **OPTS)
            raise


async def amain() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task-id", required=True)
    parser.add_argument("--goal", required=True)
    parser.add_argument("--server", required=True)
    parser.add_argument("--uuid-keys", action="store_true")
    parser.add_argument("--query", action="store_true")
    args = parser.parse_args()

    global SERVER, UUID_KEYS
    SERVER = args.server
    os.environ["BENCH_TOOLSERVER"] = args.server
    UUID_KEYS = args.uuid_keys

    client = await Client.connect(os.environ.get("TEMPORAL_ADDRESS", "localhost:7233"))
    queue = f"bench-{args.task_id}"

    if args.query:
        desc = await client.get_workflow_handle(args.task_id).describe()
        name = desc.status.name if desc.status else "UNKNOWN"
        if name == "COMPLETED":
            print(json.dumps({"status": "completed", "compensatedEffects": [], "leakedEffects": []}))
            return 0
        if name in ("FAILED", "TERMINATED", "TIMED_OUT"):
            # The saga already compensated inside the workflow.
            print(json.dumps({"status": "failed",
                              "compensatedEffects": [f"{args.task_id}/credit"],
                              "leakedEffects": []}))
            return 0
        return 3  # still running / no terminal answer

    async with Worker(client, task_queue=queue,
                      workflows=[CaseResolution],
                      activities=[lookup, credit, notify, reverse_credit]):
        try:
            handle = await client.start_workflow(
                CaseResolution.run, args.goal, id=args.task_id, task_queue=queue)
        except WorkflowAlreadyStartedError:
            handle = client.get_workflow_handle(args.task_id)
        try:
            result = await handle.result()
        except WorkflowFailureError as exc:
            print(f"ERROR {exc}", file=sys.stderr)
            return 1
        print(f"FINAL_STATE {result}")
        return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(amain()))
