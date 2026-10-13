# Aurelia as a research prototype

Research theme: **correct crash recovery for programs whose control flow is
chosen by a language model.**

| Paper | Claim | Status |
|---|---|---|
| 1 — measurement | Recovery that re-asks the model duplicates side effects; recording granularity, not key choice, is what makes recovery exactly-once | [question](question.md) · [pre-registration R1](preregistration.md) (draft) · harness in [`research/replan/`](../../research/replan/) |
| 2 — system | A correctness condition for recovered agent runs ("every recovered run is equivalent to some crash-free run"), specified in TLA+, model-checked, with Aurelia as reference implementation | not started |

## Reproduce

```bash
ollama pull qwen2.5:3b && ollama pull llama3.2:3b && ollama serve &
dotnet build -c Release src/Aurelia.ToolServer
python3 research/replan/run_study.py --config research/replan/config.json \
    --out research/replan/data/r1.jsonl
```

The study agent is deliberately **not** Aurelia: both recording disciplines
live in one neutral agent (`agent.py`), so the experiment compares
disciplines, not products. The harness reuses only Aurelia's tool server
(ledger + physical kill window).

## Open items

- Related-work bibliography — pending the LibKey connector (every cited
  article resolved before it enters the paper).
- Faculty advisor at Northeastern (co-author, letter writer) — Param.
- Round 2 task families; RQ4 survey of official framework examples.
