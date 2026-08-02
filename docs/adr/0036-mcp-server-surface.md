# 0036 — The MCP server is a stateless channel over durable sessions

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: the MCP server shipped 2026-08-02 with no ADR (audit 2026-08-02
> §1). This records the as-built decision for review.

## Context

Agent hosts (Claude Code, Copilot Studio via connector, any MCP client) need
to drive Aurelia sessions without linking the runtime. MCP is the emerging
standard surface, and it is also the practical Copilot Studio integration
path.

## Decision

`src/Aurelia.Mcp` is a stdio MCP server (official `ModelContextProtocol`
SDK) exposing six tools: `aurelia_start_session`, `aurelia_get_session`,
`aurelia_resolve_approval`, `aurelia_resume_session`, `aurelia_replay_session`,
`aurelia_send_turn`. Per the channel-decoupling rule (proposed ADR-0032), the
server holds no session state: sessions live in per-session directories under
`~/.aurelia-mcp` (env `AURELIA_MCP_DATA`, Cosmos via the standard env), with
the agent kind persisted in `meta.json` so *any* server process can re-drive
a session it didn't start. `.mcp.json` at the repo root registers the server
for local development.

## Consequences

The durability demo works over the protocol itself: pause in one MCP server
process, approve and complete in a different one. The Copilot Studio
connector configuration remains the 🗺️ item; the server it would call
exists. Stdio-only for now — an HTTP/SSE transport is a future decision.

## Verified by

Real-protocol verification: `tools/list`, durable session via `tools/call`,
cross-process approval resume; `aurelia_send_turn` driving multi-turn
conversations.
