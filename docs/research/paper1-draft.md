# Recovery That Re-Asks the Model: Duplicate and Lost Side Effects in Crash-Recovered LLM Agents

*Draft v0 — workshop format (4–6 pages). Results sections are filled only from
`results-r1.md` / `results-r2.md`; no number appears here that is not in those
files. Citations are added only after LibKey resolution.*

## Abstract (to be finalized after R2)

Durable-execution systems make crash recovery correct by replaying
deterministic code against recorded results. LLM agents break this premise:
the code that decides which side effects to perform is a model whose answer
is not reproducible across requests. We show that **recovery schemes that
re-ask the model** for a decision the crashed process had already received
can execute side effects twice — or, depending on the idempotency-key scheme,
silently drop them — and that **no key-derivation strategy is safe on its
own**. We separate two design axes that practitioners conflate — *recording
granularity* (is the model's decision durable before it is acted on?) and
*key derivation* (provider tool-call id, argument hash, or position) — and
measure their interaction under a physical after-effect crash across
[N] runs, [M] task shapes, two open models, and three sampling temperatures.
[RESULT SENTENCE — from data.] A survey of [K] agent frameworks' official
documentation finds [RQ4 RESULT]. We argue that recording the model's
decision before acting should be treated as a correctness requirement, and we
outline a correctness condition for recovered agent runs.

## 1. Introduction

- The failure in one paragraph: the credit was issued, the process died
  before recording it, recovery asked the model again, and the model's new
  tool call carried a new id → second credit.
- Why it is not "just use idempotency keys": keys identify *decisions*; if
  the decision is re-made, the key is re-made. Each common key scheme fails
  on a common task shape (§5).
- Contributions: (1) the granularity × key taxonomy with the condition under
  which each pair is exactly-once; (2) a pre-registered measurement;
  (3) a documentation survey; (4) an open harness.

## 2. Background

Durable execution and replay; sagas and compensation; idempotency keys;
tool-calling agents and provider-assigned call ids. *(Citations pending
LibKey.)*

## 3. A taxonomy of agent recovery

| | tool_call_id | args_hash | position |
|---|---|---|---|
| **step** (decision recorded before acting) | safe | safe | safe |
| **turn** (decision re-made on recovery) | unsafe: new id per request | unsafe when the model writes any argument | unsafe when the number or order of effects can change: drops effects |

Claim to state precisely: under step recording, recovery re-executes only
calls whose decision is already in the record, so every key scheme sees the
same key on retry. Under turn recording, the key is a function of a *new*
decision, so exactly-once needs the new decision to be identical to the old
one *in whatever the key scheme hashes*.

## 4. Method

Harness (tool server owns the ledger; the after-effect kill window; one
recovery); neutral study agent implementing both granularities; models;
temperatures; seeds; N; outcomes; exclusions. All of it as pre-registered
(`preregistration.md`, `preregistration-r2.md`), including the fact that
the predictions were drafted before data.

## 5. Results

### 5.1 Round 1 — arguments copied from tool results
[Table from results-r1.md. Report each pre-registered hypothesis as held or
falsified, falsified ones first.]

### 5.2 Round 2 — model-authored arguments and multi-effect turns
[Pending R2 approval and runs.]

### 5.3 What frameworks document (RQ4)
[From rq4-survey.md, official documentation only, access date stated.]

## 6. Discussion

- Implication: frameworks should record model outputs before executing tool
  calls by default, and say so in their docs; key choice is secondary.
- Cost of step recording: one durable write per model response (measure
  in R3).
- Toward a correctness condition (Paper 2): a recovered run is correct iff
  its effect history equals the effect history of some crash-free run of the
  same agent; formalize in TLA+; Aurelia as the reference implementation.

## 7. Threats to validity

3B local models; one domain; the study agent is ours (we do not run the
framework code itself, only the patterns its docs prescribe); the author
also builds a durable runtime that uses step recording, which is why the
study agent is a neutral ~200-line program and the full code, config, raw
data, and pre-registrations are published.

## 8. Related work
*(Pending LibKey: durable-execution systems, workflow replay, sagas,
exactly-once processing, agent reliability benchmarks.)*
