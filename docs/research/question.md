# Paper 1 — research question

**Working title:** *Recovery That Re-Asks the Model: Duplicate Side Effects in Crash-Recovered LLM Agents*

## The gap

Durable execution (Temporal, Durable Task, DBOS, Restate) makes crash recovery
correct by **replaying deterministic code** against recorded results. Agents
violate the premise: their control flow is chosen by a model whose output is
not reproducible across requests. Any recovery scheme that re-issues a model
request the crashed process had already answered lets the model **make the
decision again** — and a different decision gets a different idempotency key.

Exactly-once side effects then depend on two things nobody reports together:

1. **Recording granularity** — is the model's response durable *before* the
   tool calls it chose execute (step), or only at the end of a turn (turn)?
2. **Key derivation** — provider tool-call id, argument hash, or position.

## Questions

- **RQ1.** Under a crash *after* a side effect lands but *before* its result is
  recorded, how often does recovery duplicate the effect, per
  granularity × key strategy × temperature × model?
- **RQ2.** Is step-level recording of model outputs *sufficient* for zero
  duplicates regardless of key strategy — i.e., does it make the key choice
  irrelevant?
- **RQ3.** Under turn-level recovery, which key strategies survive re-asking,
  and how does survival decay with sampling temperature? (Hash keys survive
  only identical re-decisions; position keys survive any re-decision with the
  same tool-call structure; provider ids survive none.)
- **RQ4 (survey).** Which granularity do frameworks' *official* agent examples
  and tutorials actually use? This is what turns a lab effect into a
  prevalence claim.

## What would falsify the thesis

- Turn-level + `position` keys show ≈0 duplicates at all temperatures **and**
  the RQ4 survey finds turn-level recovery rare → the problem is a
  documentation footnote, not a paper. We would report that honestly.
- Step-level recording shows *any* duplicate → our recording protocol (or
  Aurelia's, which it models) is wrong, which is a finding in itself.

## Contribution claimed (if it holds)

1. A taxonomy: recording granularity × key derivation, with the condition
   under which each pair is exactly-once.
2. The measurement (RQ1–3), reproducible from `research/replan/`.
3. The prevalence survey (RQ4).
4. Paper 2 builds on this: a correctness condition for recovered agent runs,
   formalized and model-checked, with Aurelia as the reference implementation.

## Threats to validity (state them up front)

- Small local models (3B) misbehave more than frontier models; we report task
  completion separately from duplication, and add a hosted model in a later
  round if spend is approved.
- One task family so far (service credit). The paper needs ≥3 task families
  (refund, booking, email/file) before submission.
- The author of the harness also authors Aurelia. The study harness here
  deliberately does **not** run Aurelia: both granularities are implemented in
  one neutral ~200-line agent, so the comparison is between *recording
  disciplines*, not products.
