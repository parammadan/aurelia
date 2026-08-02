# 0027 — Retry policy: declared attempts with exponential backoff

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

Until now the retry behavior was a provisional constant (`maxAttempts = 2`,
immediate) with no ADR — flagged in the defense pack, and load-bearing:
experiment 002 showed recovery rate is a direct function of it
(`(1 − r^attempts)^calls`). Immediate retries also hammer a dependency that
is already struggling, which is the failure mode backoff exists to soften.

## Decision

Retries are a declarative, driver-level policy:
`RetryPolicy(MaxAttempts, InitialDelay, BackoffFactor)`, default
**(2, 200 ms, ×2)**. The delay before attempt *n+1* is
`InitialDelay × BackoffFactor^(n−1)`, deterministic — **no jitter yet**
(jitter is randomness; when it arrives it goes through the recorded-random
path or stays perimeter-only, its own small decision). All non-cancellation
exceptions are retryable in v1; a retryable-vs-terminal error taxonomy on the
tool contract is deferred until a real tool demands it (noted in `ITool`).
Attempt failures remain recorded events (ADR-0010), so the policy changes
pacing, never semantics: crash recovery mid-backoff simply resumes with the
attempt count the log shows.

## Consequences

Recovery rate becomes tunable per driver and the experiment math stays
closed-form. Backoff adds latency to failing paths by design. Because the
delay is derived from the attempt count in the log, a resumed session
continues the schedule rather than restarting it.
