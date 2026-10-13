#!/bin/bash
# CPU-only smoke test of the R2 code path (no GPU). N=1 on a handful of
# cells; output goes to scratch, never to data/.
#SBATCH -J r2-smoke -p short -c 8 --mem=16G -t 00:40:00 -o /scratch/madan.pa/aurelia-r2/smoke-%j.log
set -euo pipefail
source /scratch/madan.pa/aurelia-r2/code/research/replan/slurm/env.sh
cd /scratch/madan.pa/aurelia-r2/code/research/replan
start_ollama /scratch/madan.pa/aurelia-r2/smoke-ollama-$SLURM_JOB_ID.log
OUT=/scratch/madan.pa/aurelia-r2/smoke-$SLURM_JOB_ID.jsonl
# cells: memo/qwen/turn/args_hash/T=1 (idx 41), two/qwen/turn/position/T=1 (idx 80), two/qwen/step/position/T=1 (idx 89)
for c in 41 80 89; do
    python3 -I run_study.py --config config_r2.json --out $OUT --only-cell $c --n 1
done
python3 -I -c "
import json
for l in open('$OUT'):
    r=json.loads(l); print({k:r.get(k) for k in ['task','granularity','keys','kill_triggered','credits_per_case','duplicate','lost_effect','completed','recovery_tail']})"
kill $OLLAMA_PID
