# Why agents need durable execution

> **DRAFT** — for Param's rewrite before publishing anywhere. Structure and
> arguments are grounded in the actual repo; the voice needs to become yours.

Every agent demo looks the same: a chat window, a plan, some tool calls, a
happy ending. Every agent *incident* also looks the same: the model call timed
out, the tool returned a 500, the pod got rescheduled, the phone call dropped —
and the agent, along with everything it knew and everything it had half-done,
simply ceased to exist.

We solved this problem for workflows a decade ago. We have not solved it for
agents. Most agent frameworks today keep the session — the conversation, the
plan, the record of what's already been executed — in process memory. The
process is the agent. Kill the process and there is no agent, only a customer
mid-refund talking to a dead socket.

## Retry-from-scratch is not recovery

The usual answer is "wrap it in retries and re-run the task." That answer
quietly assumes agents are pure functions. They aren't; they perform side
effects.

Consider the canonical case: an order-support agent that has verified the
customer, decided a refund is warranted, and called the refund API — and the
process dies before the response lands. Re-run the whole task and one of two
things happens. Either the agent refunds the customer twice, or (if someone
bolted on a "did I already do this?" heuristic) it hallucinates its way to a
different decision the second time, because nothing about an LLM guarantees the
same plan twice. Both outcomes are worse than the crash.

There is also a subtler loss: the context. A long-running session accumulates
state that cannot be reconstructed by re-prompting — what the user said, what
the agent observed, which approaches already failed. Losing it mid-task isn't a
performance bug, it's a correctness bug.

## What durable execution actually means

Durable workflow engines (Temporal, Azure Durable Functions) got the core idea
right years ago: the unit of truth is not the process, it's the **history**.
Applied to agents, that translates into a few concrete commitments:

**1. The session is an event log.** Every user turn, model call, tool
invocation, and decision is an immutable event; the current state of a session
is a fold over its log. A process hosting a session is disposable by
construction — any process can rebuild the session from the log.

**2. Intent is durable before execution.** An external call is two events, not
one: the *intent* (with its full inputs) is persisted before the call runs, and
the *outcome* is persisted after. This ordering is what makes crashes legible:
an intent without an outcome tells the recovering process exactly where death
occurred — and, crucially, that the call *may* have had side effects.

**3. Effects carry idempotency keys.** Every logical call gets a deterministic
key that stays identical across retries. Recovery re-executes the unfinished
call under the same key, and downstream systems deduplicate on it. That — not
hope — is what makes "issue refund" exactly-once. Where dedupe isn't possible,
the key anchors saga-style compensation instead.

**4. Nondeterminism is captured, not trusted.** Here agents diverge from
classic durable workflows. Temporal's replay assumes deterministic
orchestration code; an agent's "orchestration code" includes an LLM, which is
nondeterministic even at temperature zero. So the log must record every
nondeterminism source — model outputs, tool results, injected time and
randomness — and replay becomes reconstruction: a deterministic fold with every
external result substituted from the recording. You get a debugger for agent
runs; re-running with a *different* model becomes a controlled experiment
rather than an accident.

## If it isn't tested, it isn't true

Durability claims are cheap, which is why the first thing in this project's CI
— before any feature — is a test that starts the runtime as a real OS process,
waits until a tool call is provably in flight, kills the process with SIGKILL,
starts a fresh process, and asserts the session resumes from the log and
completes with the correct final state. Every commit, every branch. The runtime
under that test is embarrassingly minimal today; the point is that it can never
become less recoverable than it is right now.

That test grows up to be something bigger: a failure-injection benchmark —
tool timeouts, malformed model output, mid-run kills, poison messages — that
can score *any* agent framework on recovery rate and correctness after
recovery. As far as I can tell, nobody has published one. The industry is
shipping agents into production on frameworks whose crash behavior has never
been measured.

Agents are about to be trusted with real side effects in real businesses. The
runtimes underneath them should meet the bar we already set for workflow
engines ten years ago: the process is allowed to die; the work is not.

---

*Aurelia is a durable agent orchestration runtime being built in the open on
.NET, Orleans, and Azure. The crash-recovery test above, and the architecture
decision records behind everything in this post, are in the repo.*
