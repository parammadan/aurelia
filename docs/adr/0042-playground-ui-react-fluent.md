# 0042 — Playground UI: React + Fluent UI

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Approved task (post-freeze #1). This ADR precedes implementation:
> no UI code is written until this PR is approved and the feature freeze is
> lifted.

## Context

The playground is the pitch artifact, and the current UI is a hand-rolled
static page. The demo's story — kill a process mid-side-effect, watch the
resume pick up from the durable intent — deserves a UI that tells it
deliberately: loading states, empty states, and a kill transition that reads
as an event, not a glitch.

## Decision

Rebuild the playground UI as a React SPA:

- **Stack:** React 18 + TypeScript + Vite. **Fluent UI React v9**
  (`@fluentui/react-components`) is the only component library — no
  Tailwind, no MUI. Light/dark themes via `FluentProvider`
  (`webLightTheme`/`webDarkTheme`).
- **Architecture:** the SPA is served by the existing playground backend on
  :5199 and talks to it via REST plus Server-Sent Events (or WebSocket) for
  live run streaming. The backend API stays unchanged unless a further ADR
  says otherwise. `Core/` is untouched.
- **Screens:**
  1. **Run view** — start an agent run; live event stream rendered as a
     timeline of Fluent Cards (event-type badge, turn/step coordinates,
     timing); a prominent chaos button that kills mid-run, after which the
     timeline shows "💥 process killed" as an event followed by the resume
     events — the durability story told visually.
  2. **Trace viewer** — pick any past session, scrub its event log as a
     timeline, expand any event to the full payload (prompt, model,
     idempotency key); crashed sessions clearly mark kill point and resume
     point.
  3. **Sessions list** — every session with log-derived status (running /
     parked-awaiting-approval / completed / failed-compensated) and
     approve/deny actions on parked sessions, moving the human-in-the-loop
     demo into the UI.
- **Quality bar:** pitch artifact. No jank — deliberate loading, empty, and
  chaos-kill states.

## Consequences

The UI becomes a real frontend project (node toolchain in the repo, a build
step wired into the playground's publish). The existing static pages retire
once the SPA reaches parity. Live streaming pushes the backend toward an
SSE/WebSocket endpoint — additive, so within the API-unchanged rule; any
non-additive change needs its own ADR.

## Verified by

(Upon implementation) playground endpoint tests extended to serve the SPA;
the chaos-kill → resume transition exercised in the existing real-worker
E2E test.
