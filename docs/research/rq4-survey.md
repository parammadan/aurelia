# RQ4 survey: where do agent frameworks record the model's tool-call decision?

**Accessed:** 2026-10-12. Every quote below comes from a page or file fetched that day.
**Method:** I used only official vendor documentation sites and official GitHub repositories. No academic papers, blogs, or third-party tutorials were used. In a few places the docs prose was silent, and I read the framework's own source in its official repo; the row says so when that happened. If I could not confirm a claim on an official page, the cell says **unverified**.

## The question

Suppose an LLM agent's process crashes and is then recovered. Is the model's response — the tool calls it chose — durably recorded **before** those tool calls run? This survey calls that **step** granularity. Or is state persisted only at the end of a turn or run, so that recovery has to ask the model again? This survey calls that **turn** granularity. Second question: what do the docs say about idempotency keys for tools with side effects?

Terms:
- **step**: the model response (with its tool calls) is durable before any tool runs. Tool results are durable on their own as each tool completes.
- **turn**: state is persisted per agent run, task, or user message. A crash mid-run loses the model's decision, and recovery re-runs the model.
- **none**: nothing survives a process restart. Examples: an in-memory store, or no persistence configured.
- **quickstart granularity**: what the first or default example in the docs actually gives you.

## Summary table

| Framework | Documented durable granularity | Quickstart / default granularity | Key idempotency guidance | Sources |
|---|---|---|---|---|
| LangGraph (Graph API, `create_agent` / `create_react_agent`) | **step**. A checkpoint is written at each super-step. `create_agent` puts `model` and `tools` in separate nodes, so the AIMessage holding the tool_calls is checkpointed before the tools node runs. Durability mode `"sync"` guarantees this; the default `"async"` carries a "small risk"; `"exit"` gives **turn** | Quickstart uses `InMemorySaver`, which gives **none** across restarts (step only within one process). Default durability is `"async"` | Functional API page: "Use idempotency keys or verify existing results". No specific key named | [checkpointers](https://docs.langchain.com/oss/python/langgraph/checkpointers), [persistence](https://docs.langchain.com/oss/python/langgraph/persistence), [short-term-memory](https://docs.langchain.com/oss/python/langchain/short-term-memory), [factory.py](https://github.com/langchain-ai/langchain/blob/master/libs/langchain_v1/langchain/agents/factory.py), [pregel/main.py](https://github.com/langchain-ai/langgraph/blob/main/libs/langgraph/langgraph/pregel/main.py) |
| LangGraph Functional API (`@entrypoint` / `@task`) | **step, if you wrap the work in tasks.** Completed `@task` results are restored on replay. Work outside a task re-runs | Depends on the user. Nothing forces the LLM call into a task | "A task that started but did not finish may run again ... Use idempotency keys or verify existing results" | [functional-api](https://docs.langchain.com/oss/python/langgraph/functional-api) |
| OpenAI Agents SDK (Sessions) | **turn**. Items are stored "After each run" | **turn** (SQLiteSession; in-memory variant gives **none**) | None found on the Sessions page | [sessions](https://openai.github.io/openai-agents-python/sessions/), [running_agents](https://openai.github.io/openai-agents-python/running_agents/) |
| OpenAI Agents SDK + Temporal | **step** for model calls ("model invocations are automatically routed through Temporal activities"). Tools are durable only if built with `activity_as_tool`; plain `@function_tool` tools run in the workflow | Hello World uses the plugin, so model calls are **step** | The integration README says nothing. Temporal core docs: use "Workflow Run ID and the Activity ID as an idempotency key" | [temporalio/ai-integrations openai_agents README](https://github.com/temporalio/ai-integrations/tree/main/python/openai_agents), [activity-definition](https://docs.temporal.io/activity-definition) |
| Temporal (AI cookbook) | **step**. The LLM call and each tool call are separate Activities | Same (the cookbook *is* the durable pattern) | Cookbook recipes say nothing. Core docs: "combination of the Workflow Run ID and the Activity ID as an idempotency key" | [agentic-loop OpenAI](https://docs.temporal.io/ai/cookbook/agentic-loop-tool-call-openai-python), [agentic-loop Claude](https://docs.temporal.io/ai/cookbook/agentic-loop-tool-call-claude-python), [activity-definition](https://docs.temporal.io/activity-definition) |
| Microsoft Agent Framework, Durable Task extension | **turn**. The extension "internally implements entity-based agent loops". The entity persists after the agent SDK's whole tool loop returns. In orchestrations, "Each agent call is checkpointed" — still one checkpoint per agent run. The docs describe a separate, hand-written "orchestration-based agent loop" (LLM and tools as activities) that gives **step**, but it is not what the extension uses | **turn** | None found on the pages read | [durable-agents-microsoft-agent-framework](https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-agents-microsoft-agent-framework), [durable-agents-patterns](https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-agents-patterns), [agent-framework durable-extension](https://learn.microsoft.com/en-us/agent-framework/integrations/durable-extension) |
| Microsoft Agent Framework Workflows (non-durable checkpoints) | **turn** for agent executors. Checkpoints come "at the end of each superstep", and an AgentExecutor's saved state holds "outputs from the latest run" | In-memory storage gives **none** | None found | [workflows/checkpoints](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints) |
| DBOS (agent docs; OpenAI Agents integration) | **step**. "annotate ... each LLM and tool call it makes as a step". `DBOSRunner` wraps each `get_response()` as a step (per the source) | Same, but tools must be hand-decorated with `@DBOS.step` | Workflow ID "acts as an idempotency key" (workflow-level). Steps are "at least once" | [ai-quickstart](https://docs.dbos.dev/ai/ai-quickstart), [integrations/openai-agents](https://docs.dbos.dev/integrations/openai-agents), [workflow-tutorial](https://docs.dbos.dev/python/tutorials/workflow-tutorial), [dbos-openai-agents runner.py](https://github.com/dbos-inc/dbos-openai-agents/blob/main/dbos_openai_agents/runner.py) |
| Restate | **step**. "Every LLM call, tool execution, and routing decision is durably persisted" | Same. The integrations (Vercel `durableCalls`, OpenAI `DurableRunner`, Pydantic `RestateAgent`) journal LLM responses. Tools need `ctx.run` / `@durable_function_tool`. The LangChain middleware "does not auto-journal tool calls" | Generate the key durably: `ctx.uuid()` / `ctx.rand.uuidv4()`, labeled "Idempotency key generation". Agent page claims ctx.run tool side effects "are executed exactly once" | [ai/patterns/durable-agents](https://docs.restate.dev/ai/patterns/durable-agents), [foundations/actions](https://docs.restate.dev/foundations/actions) |
| Pydantic AI (Temporal / DBOS / Restate) | **step**. "Each engine wraps every model request and tool call as its own durable unit". Caveat: under DBOS, custom function tools are "not automatically wrapped" | Base quickstart (no durability capability) gives **none** (unverified: I did not fetch the base quickstart) | "Tools and decorated capability operations should therefore be idempotent unless the engine provides a suitable at-most-once mode." No specific key named | [durable_execution/overview](https://pydantic.dev/docs/ai/integrations/durable_execution/overview/), [temporal](https://github.com/pydantic/pydantic-ai/blob/main/docs/durable_execution/temporal.md), [dbos](https://github.com/pydantic/pydantic-ai/blob/main/docs/durable_execution/dbos.md), [restate](https://github.com/pydantic/pydantic-ai/blob/main/docs/durable_execution/restate.md), [backends](https://github.com/pydantic/pydantic-ai/blob/main/docs/durable_execution/backends.md) |
| Inngest (core durable agents) / AgentKit | **step**. "Each model call runs in `step.run()` ... Each tool call runs in its own `step.run()`". AgentKit: "step.ai automatically retries on failure and caches the result for durability" | AgentKit running inside an Inngest function: **step** for inference. Behavior outside a function: **unverified** | "Use idempotency keys for writes, sends, and payments." | [durable-agents](https://www.inngest.com/docs/durable-execution/durable-agents), [agent-tool-loops](https://www.inngest.com/docs/ai-patterns/agent-tool-loops), [AgentKit agents](https://agentkit.inngest.com/concepts/agents), [AgentKit multi-steps-tools](https://agentkit.inngest.com/advanced-patterns/multi-steps-tools) |
| Google ADK (Sessions + Resume) | **step**, with a persistent SessionService and `ResumabilityConfig(is_resumable=True)`. The Runner commits each yielded Event (including "a request to use a tool") before agent logic continues. Resume re-runs only the unfinished tool request | `InMemorySessionService` gives **none** ("All conversation data is lost if the application restarts"). Resume is opt-in | "Tools ... are run ***at least once*** ... you should modify the Tool to check for and prevent duplicate runs." No key named | [runtime/resume](https://adk.dev/runtime/resume/), [runtime/event-loop](https://adk.dev/runtime/event-loop/), [sessions/session](https://adk.dev/sessions/session/) |
| CrewAI (Checkpointing) | **depends**. Checkpoints are event-driven. Default `task_completed` is coarser than turn (one per task). `llm_call_completed` / `tool_usage_finished` are available for "fine-grained recovery". Whether resuming from an `llm_call_completed` checkpoint skips the model and runs its pending tool calls: **unverified** | Checkpointing is opt-in. Default trigger is `task_completed`. Auto-checkpoint writes are "best-effort" | None found | [concepts/checkpointing](https://docs.crewai.com/v1.15.27/en/concepts/checkpointing), [mastering-flow-state](https://docs.crewai.com/v1.15.27/en/guides/flows/mastering-flow-state) |
| AutoGen (AgentChat) | **turn** (manual). `save_state()` is called explicitly after a run | **none** unless you call `save_state` | None found | [tutorial/state](https://microsoft.github.io/autogen/stable/user-guide/agentchat-user-guide/tutorial/state.html), [microsoft/autogen README](https://github.com/microsoft/autogen) (maintenance mode) |
| LlamaIndex Workflows (AgentWorkflow / FunctionAgent) | **step-capable, user-driven.** No built-in checkpointer: "There is no built-in checkpointer to enable". You snapshot at step boundaries. In `AgentWorkflow`, the LLM call (`run_agent_step`) and `call_tool` are separate steps (per the source). The DBOS runtime plugin "journals step transitions" | **none** ("Workflows are ephemeral by default") | "Resume is at-least-once, and step side effects need to be safe to repeat." DBOS plugin: "Design steps to be idempotent where possible". No key named | [durable_workflows](https://developers.llamaindex.ai/python/llamaagents/workflows/durable_workflows/), [workflows/dbos](https://developers.llamaindex.ai/python/llamaagents/workflows/dbos/), [agent_workflow_basic](https://developers.llamaindex.ai/python/examples/agent/agent_workflow_basic/), [base_agent.py](https://github.com/run-llama/llama_index/blob/main/llama-index-core/llama_index/core/agent/workflow/base_agent.py) |

**Headline.**
- **Step** granularity is what the docs prescribe for the durable-execution engines and their integrations: Temporal, DBOS, Restate, Inngest, Pydantic AI-on-engines, OpenAI SDK + Temporal, and ADK with resumability. LangGraph also gets there, through its node split and super-step checkpoints.
- **Turn** granularity comes with the session/state stores of the agent SDKs: OpenAI Agents SDK Sessions, AutoGen `save_state`, and MAF agent executors. Notably, it also comes with Microsoft's *Durable Task extension for Agent Framework*, which persists per agent run (entity-based), even though the same docs describe a step-granular orchestration loop as an alternative.
- **Quickstarts usually give none or turn.** They default to in-memory stores (LangGraph `InMemorySaver`, ADK `InMemorySessionService`, MAF `InMemoryCheckpointStorage`), or durability is opt-in (CrewAI, ADK resumability, LlamaIndex).
- **No framework names `tool_call_id` as the idempotency key.** Temporal names Run ID + Activity ID. DBOS names the workflow ID (workflow-level). Restate names a journaled random UUID. LangGraph, Inngest, Pydantic AI, ADK, and LlamaIndex say "use idempotency keys" / "make idempotent" / "check for duplicates" without naming a key. The rest say nothing.

---

## Per-framework notes

### 1. LangGraph

Sources: https://docs.langchain.com/oss/python/langgraph/checkpointers , https://docs.langchain.com/oss/python/langgraph/persistence (the old `/durable-execution` URL now serves this Persistence page), https://docs.langchain.com/oss/python/langgraph/functional-api , https://docs.langchain.com/oss/python/langchain/agents , https://docs.langchain.com/oss/python/langchain/short-term-memory , source: `langchain_v1/langchain/agents/factory.py`, `langgraph/pregel/main.py` (official repos).

**Granularity (Graph API).**
- "A checkpointer saves a snapshot of graph state at each super-step"
- "In addition to super-step checkpoints, LangGraph also persists writes at the **node (task) level**. As each node within a super-step finishes, its outputs are written to the checkpointer's `checkpoint_writes` table"
- Durability modes:
  - `"exit"`: "persists changes only when graph execution exits ... you cannot recover from system failures (like process crashes) mid-execution."
  - `"async"`: "small risk that LangGraph does not write checkpoints if the process crashes during execution."
  - `"sync"`: "persists changes synchronously before the next step starts."
- The default is `"async"`. That comes from the official source docstring: "durability: The durability mode for the graph execution, defaults to `"async"`." (pregel/main.py). The docs page itself does not state the default.

**Prebuilt agent.** In `factory.py`, `create_agent` builds `graph.add_node("model", ...)` and `graph.add_node("tools", tool_node)` as separate nodes, so the model's tool-call message is a node output checkpointed before the tools node. The short-term-memory doc says: "Short-term memory updates when the agent is invoked or a step (like a tool call) is completed". The agents quickstart passes `checkpointer=InMemorySaver()`, and the persistence page warns: "`MemorySaver` and `InMemorySaver` store checkpoints in RAM. When the process restarts, all checkpoints are lost." I did not check `create_react_agent` (langgraph.prebuilt) separately. The current docs point to `create_agent`.

**Functional API.**
- Granularity: "when tasks are executed, their results are saved to an existing checkpoint associated with the given entrypoint"
- Resume: "replay starts at the beginning of the **entrypoint** while LangGraph restores completed task ... results"
- Side effects: "you must place non-deterministic work ... and side effects ... in tasks"
- Idempotency: "A **task** that started but did not finish may run again on that resume, so design side effects to be idempotent. Use idempotency keys or verify existing results to avoid unintended duplication."
- No specific key, and no mention of tool_call_id.

### 2. OpenAI Agents SDK

Sources: https://openai.github.io/openai-agents-python/sessions/ (raw: `docs/sessions/index.md`), https://openai.github.io/openai-agents-python/running_agents/ , https://openai.github.io/openai-agents-python/human_in_the_loop/ .

**Sessions = turn.**
- "**Before each run**: The runner automatically retrieves the conversation history ... **After each run**: All new items generated during the run (user input, assistant responses, tool calls, etc.) are automatically stored in the session."
- The page does not describe recovering a run that crashed partway. Nothing about idempotency.
- HITL: "`RunState` is designed to be durable. Use `state.to_json()` ... to store pending work". This is an explicit pause/serialize at approval points, not automatic crash durability.
- For crash durability, the running-agents page points outward: "The integrations below are for durable orchestration when runs may span long waits, retries, or process restarts". It lists Dapr, Temporal, Restate, and DBOS.

**Temporal integration.** The integration has moved: `temporalio/contrib/openai_agents/README.md` says it "has moved to the independently versioned `temporalio-openai-agents` package". The README is now at https://github.com/temporalio/ai-integrations/tree/main/python/openai_agents .
- "In this integration, model invocations are automatically routed through Temporal activities, while the logic that coordinates them lives in the workflow."
- "User-defined `FunctionTool`s, including tools created with `@function_tool`, are not automatically converted into Temporal activities; they execute in the workflow unless explicitly backed by a Temporal activity." Use `activity_as_tool()`.
- Model decisions are therefore **step**-durable, because an activity result sits in Event History before the workflow continues. Tools are step-durable only via `activity_as_tool`.
- The README has no idempotency guidance. I searched for "idempot", "call_id", and "at least".

### 3. Temporal (AI cookbook)

Sources: https://docs.temporal.io/ai-cookbook (index), https://docs.temporal.io/ai/cookbook/agentic-loop-tool-call-openai-python , https://docs.temporal.io/ai/cookbook/agentic-loop-tool-call-claude-python , https://docs.temporal.io/activity-definition .

**Granularity = step.**
- "Being external API calls, invoking the LLM and invoking any functions/tools are done within a Temporal Activity."
- The LLM call is `workflow.execute_activity(openai_responses.create, ...)`. Each tool runs as `workflow.execute_activity(item.name, args, ...)` through a dynamic activity.
- The recipe does not say in words that "the decision is persisted before tools". This follows from Temporal semantics: the LLM activity's result is recorded in Event History when it returns, before the workflow schedules the tool activity. Core docs: "Activities won't record to the Event History until they return or produce an error."
- Retries: "Retries are handled by Temporal and not by the underlying libraries" (client created with `max_retries=0`).

**Idempotency.**
- The cookbook recipes don't mention idempotency. The `call_id` / `tool_use_id` fields are used only to thread messages.
- Core docs: "Temporal recommends that Activities be idempotent." and "You can use a combination of the Workflow Run ID and the Activity ID as an idempotency key ... since this is guaranteed to be consistent across retry attempts but unique among Workflow Executions."

### 4. Microsoft Agent Framework / Durable Task

Sources: https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-agents-microsoft-agent-framework , https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-agents-patterns (raw markdown from MicrosoftDocs/azure-docs), https://learn.microsoft.com/en-us/agent-framework/integrations/durable-extension , https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints .

The patterns page describes two approaches:
- "**Orchestration-based** | The agent loop is a durable orchestration. Tool calls are activities". The Python sample does `llm_response = yield context.call_activity("call_llm_agent", ...)` and then `context.call_activity("execute_tool", tc)`. That is **step**.
- "**Entity-based** | Each agent instance is a durable entity. The agent framework manages the loop; the entity provides session persistence." The sample calls the SDK "for the LLM call (with tool loop)" and only then does "Persist the response in the entity state". That is **turn**.

Which one the extension uses:
- "The Durable Task extension for Microsoft Agent Framework uses this approach [entity-based]"
- "The extension internally implements entity-based agent loops, where each agent session is a durable entity"
- Inside orchestrations: "Each agent call is checkpointed ... Completed agent calls aren't re-executed on recovery." An agent call is a whole run, including its internal tool loop.

So the documented MAF durable-agent pattern is **turn**. A crash mid-tool-loop re-runs the agent call. I did not find that recovery behavior stated in words; it follows from the entity sample, where the state is set after the full tool loop returns.

**MAF Workflows checkpoints.**
- "Checkpoints are created at the end of each superstep"
- `AgentExecutorCheckpointState` holds "`full_conversation`: Prior inputs and the assistant or tool outputs from the latest run".

**Idempotency.** None of the four pages mention idempotency. I searched for "idempot".

### 5. DBOS

Sources: https://docs.dbos.dev/ai/ai-quickstart , https://docs.dbos.dev/integrations/openai-agents , https://docs.dbos.dev/python/tutorials/workflow-tutorial , https://docs.dbos.dev/python/tutorials/step-tutorial , https://github.com/dbos-inc/dbos-openai-agents (README + `runner.py`).

**Granularity = step.**
- AI quickstart: "annotate your main agentic loop as a durable workflow and each LLM and tool call it makes as a step. This causes DBOS to checkpoint the progress of your agent".
- TypeScript / Vercel AI: "Wrap your model with `durableCalls` middleware so every model call is checkpointed ... replaying completed model calls from their checkpoints instead of re-contacting the provider."
- OpenAI Agents integration doc: "Annotate your agent's tool call functions and guardrail functions with `@DBOS.step`". The doc does not say that model calls become steps. The official integration source does: `class DBOSModelWrapper(Model): """Wraps a Model so each get_response() call is a durable DBOS step."""`.

**Guarantees / idempotency.**
- "Steps are tried _at least once_ but are never re-executed after they complete."
- "An assigned workflow ID acts as an idempotency key: if a workflow is called multiple times with the same ID, it executes only once. This is useful if your operations have side effects like making a payment or sending an email."
- I found no guidance on per-step or per-tool-call keys.

### 6. Restate

Sources: https://docs.restate.dev/ai/patterns/durable-agents , https://docs.restate.dev/ai/patterns/error-handling , https://docs.restate.dev/ai/patterns/sessions , https://docs.restate.dev/foundations/actions , https://docs.restate.dev/develop/python/durable-steps .

**Granularity = step.**
- "Every LLM call, tool execution, and routing decision is durably persisted."
- "every step is recorded in a journal, so if the process crashes, the agent picks up exactly where it left off."
- "LLM calls: Persisted so responses are not re-fetched on recovery".
- Per integration:
  - OpenAI: "Use `DurableRunner` so every LLM response is saved in the Restate Server and replayed during recovery" and "Annotate tools with `@durable_function_tool`".
  - LangChain: "The middleware does not auto-journal tool calls — you choose which steps are durable."
  - Raw SDK loop: "LLM calls in `ctx.run()`: Every LLM response is persisted ... Tool executions in `ctx.run()`: Side effects are executed exactly once."

That "exactly once" is the vendor's claim. The durable-steps page says failed `ctx.run` actions are retried, and I did not find a stated at-least-once caveat for a crash inside an action.

**Idempotency key.** The foundations/actions page shows a journaled random value under the comment "Idempotency key generation": `id = ctx.uuid()` (Python), `ctx.rand.uuidv4()` (TypeScript). This is a deterministic, replay-stable key. It is not tool_call_id.

### 7. Pydantic AI

Sources: https://pydantic.dev/docs/ai/integrations/durable_execution/overview/ (ai.pydantic.dev redirects here), plus repo docs `docs/durable_execution/{overview,temporal,dbos,restate,backends}.md` (github.com/pydantic/pydantic-ai).

**Granularity = step.**
- Overview: "Each engine wraps every model request and tool call as its own durable unit".
- Temporal: "inside a workflow the capability routes model requests, tool calls, and MCP server communication through Temporal activities."
- DBOS: "routes model requests and MCP communication through DBOS steps". Also: "Custom tool functions ... are **not automatically wrapped** by DBOS. If they involve non-deterministic behavior or perform I/O, you should explicitly decorate them with `@DBOS.step`."
- Restate: "every LLM response is saved in the Restate Server and replayed during recovery."

**Idempotency.**
- backends.md: "A durable unit can run more than once when a worker fails after its side effect but before the checkpoint commits. Tools and decorated capability operations should therefore be idempotent unless the engine provides a suitable at-most-once mode."
- No key is named. `tool_call_id` appears only as a RunContext field available inside Temporal activities (temporal.md); it is not presented as an idempotency key.

### 8. Inngest / AgentKit

Sources: https://www.inngest.com/docs/durable-execution/durable-agents , https://www.inngest.com/docs/ai-patterns/agent-tool-loops , https://agentkit.inngest.com/concepts/agents , https://agentkit.inngest.com/advanced-patterns/multi-steps-tools , https://agentkit.inngest.com/advanced-patterns/retries .

**Granularity = step.**
- "Each model call runs in `step.run()`... Each tool call runs in its own `step.run()`. When a step succeeds, Inngest saves its result as a **checkpoint**."
- "A model call that chose `search` returns that same choice, so the agent takes the same path until it reaches new work". This is the clearest statement in the survey that the model decision is replayed, not re-asked.
- Tool-loop guide: "❌ Not durable — if this fails, you lose all prior work" for an unwrapped LLM call.
- AgentKit: "step.ai automatically retries on failure and caches the result for durability" and "The step.ai.infer() call will offload the LLM requests to the Inngest infrastructe [sic] which will also handle retries."

**Idempotency.** "Make tool effects safe to repeat. A tool can change an external system and then fail before its result is saved. Use idempotency keys for writes, sends, and payments." No specific key is named. The examples pass `tool_use_id: toolCall.id` only as message threading.

### 9. Google ADK

Sources: https://adk.dev/runtime/resume/ (google.github.io/adk-docs redirects to adk.dev; raw from github.com/google/adk-docs `docs/runtime/resume.md`), https://adk.dev/runtime/event-loop/ , https://adk.dev/sessions/session/ .

**Event loop.**
- "The `Agent` ... runs until it has something to report (like a response, a request to use a tool, or a state change) – it then **yields** ... an `Event`."
- "The `Agent`'s logic **resumes** from where it paused only *after* the `Runner` has processed the event". The Runner does `await session_service.append_event(session, event) # Commits state/artifact deltas`.

**Resume (opt-in, ADK Python ≥1.16).**
- Enabled with `ResumabilityConfig(is_resumable=True)`.
- "if an agent successfully executed Function Tools A and B, and then failed during execution of tool C, the system reinstates the results from the tools A and B, and resumes the workflow by re-running the tool C request."
- The docs don't say explicitly whether the model is re-queried in that case. "re-running the tool C request" implies the logged function-call event is reused.

**Default.** `InMemorySessionService`: "Persistence: None. All conversation data is lost if the application restarts". "Resume is not supported by default for Custom Agents."

**Idempotency.** "the Resume feature ensures that the Tools in an agent are run ***at least once***, and may run more than once ... you should modify the Tool to check for and prevent duplicate runs." No key is named.

### 10. CrewAI

Sources: https://docs.crewai.com/v1.15.27/en/concepts/checkpointing , https://docs.crewai.com/v1.15.27/en/guides/flows/mastering-flow-state .

**Event-driven checkpoints.**
- "The runtime subscribes to events you select via `on_events` and writes a checkpoint each time one fires. The default `task_completed` produces one checkpoint per finished task ... Higher-frequency events like `llm_call_completed` are available for fine-grained recovery".
- "Auto-checkpoint writes (event-driven) are best-effort: a failed write is logged and the run continues."
- Flows: "When applied at the class level, `@persist()` saves state after every method execution".

**Unverified.** I could not confirm that a checkpoint taken on `llm_call_completed`, when resumed, executes the model's pending tool calls without calling the model again. The page lists the event but does not describe resume semantics inside an agent's loop.

**Idempotency.** The page doesn't mention idempotency or duplicate side effects.

### 11. AutoGen

Sources: https://microsoft.github.io/autogen/stable/user-guide/agentchat-user-guide/tutorial/state.html , https://github.com/microsoft/autogen .

- "We can get the state of an agent by calling save_state() method". "State is a dictionary that can be serialized to a file or written to a database."
- The examples call it explicitly after a run completes. There is no automatic or mid-run persistence, no crash recovery, and no idempotency guidance.
- README: "AutoGen is now in maintenance mode." "New users should start with Microsoft Agent Framework."

### 12. LlamaIndex Workflows

Sources: https://developers.llamaindex.ai/python/llamaagents/workflows/durable_workflows/ , https://developers.llamaindex.ai/python/llamaagents/workflows/dbos/ , https://developers.llamaindex.ai/python/examples/agent/agent_workflow_basic/ , source `llama-index-core/llama_index/core/agent/workflow/base_agent.py`.

**Snapshots are manual.**
- "Workflows are ephemeral by default." "There is no built-in checkpointer to enable." You snapshot on `StepStateChanged` / `NOT_RUNNING`.
- "Completed steps don't re-run ... A step that was mid-execution when you captured the state is rewound and runs again from the top. Resume is at-least-once, and step side effects need to be safe to repeat."
- AgentWorkflow example: "Any functions/steps that were in-progress will start from the beginning when the workflow is restored."

**Step split.** In source, `AgentWorkflow` has separate `@step`s `run_agent_step` (LLM) and `call_tool`. If you snapshot at step boundaries, the ToolCall events are captured before tools run. This is my reading of the source, not a statement in the docs prose.

**DBOS runtime plugin.** "Step completions and stream events are journaled to the database" and "steps may execute more than once if they were interrupted before the journal entry was committed. Design steps to be idempotent where possible".

---

## Limitations

1. **Docs vs. behavior.** This survey records what the docs and official examples *say*. It does not test crash behavior. Several step/turn calls rest on documented mechanisms plus engine semantics rather than an explicit sentence of the form "the tool-call decision is persisted before tools execute":
   - Temporal cookbook
   - MAF entity-based recovery
   - LangGraph `create_agent`

   Only Inngest ("A model call that chose `search` returns that same choice") and Restate/DBOS/Pydantic AI ("replayed during recovery", "instead of re-contacting the provider") state it nearly directly.
2. **Source-code reads.** For four claims I read official repo source because the docs prose was silent:
   - LangGraph default durability `"async"`
   - `create_agent` model/tools node split
   - DBOS `DBOSModelWrapper` wrapping model calls as steps
   - LlamaIndex `run_agent_step` / `call_tool` split

   These are marked in the notes above. The source can change without the docs changing.
3. **Unverified items:**
   - CrewAI resume semantics from `llm_call_completed` checkpoints
   - Inngest AgentKit behavior outside an Inngest function
   - Pydantic AI base (non-durable) quickstart persistence (not fetched)
   - LangGraph `langgraph.prebuilt.create_react_agent` (not re-checked separately from `create_agent`)
   - MAF idempotency guidance in general Durable Functions docs (I only read the agent pages)
   - OpenAI Agents SDK Dapr integration (lives on diagrid.io; not read)
4. **Moving targets.**
   - Several URLs redirected or moved during this survey: LangGraph `/durable-execution` → Persistence page; `ai.pydantic.dev` → `pydantic.dev/docs/ai`; `google.github.io/adk-docs` → `adk.dev`; Temporal's OpenAI Agents integration moved from `sdk-python/contrib` to `temporalio/ai-integrations`.
   - CrewAI docs are versioned (v1.15.27 read).
   - Results reflect 2026-10-12 only.
5. **Fetch tooling.** Some pages were read through an LLM-summarizing fetcher. Where possible I re-read the raw markdown (`.md` endpoints, raw GitHub) and quoted from that. The Temporal cookbook and AgentKit quotes came through the summarizer and should be spot-checked before they go into the paper.
6. **Idempotency scope.** "None found" means I found nothing on the pages listed. It does not mean the vendor's docs say nothing anywhere.
