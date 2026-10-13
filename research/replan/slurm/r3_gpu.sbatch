#!/bin/bash
# Round 3 (crash-free control) run: ONE A100, 16 concurrent runs against one Ollama.
# Starts with a 3-run smoke test of the no-fault path and exits if it fails.
# Submitted only with Param's explicit OK for this job (HPC etiquette rule).
# Resumable: a time-out just means resubmit; finished rows are skipped.
#SBATCH -J r3-control -p gpu --gres=gpu:a100:1 -c 16 --mem=48G -t 00:40:00 -o /scratch/madan.pa/aurelia-r2/r3-%j.log
set -euo pipefail
source /scratch/madan.pa/aurelia-r2/code/research/replan/slurm/env.sh
export OLLAMA_NUM_PARALLEL=16 OLLAMA_MAX_LOADED_MODELS=2
cd /scratch/madan.pa/aurelia-r2/code/research/replan
nvidia-smi --query-gpu=name,driver_version --format=csv,noheader
start_ollama /scratch/madan.pa/aurelia-r2/r2-ollama-$SLURM_JOB_ID.log || exit 1
trap cleanup EXIT
# Refuse to run if Ollama sees any GPU besides the one Slurm allocated.
L=/scratch/madan.pa/aurelia-r2/r2-ollama-$SLURM_JOB_ID.log; sleep 5  # GPU discovery logs just after startup
if grep -q "library=Vulkan" $L || [ "$(grep -c 'inference compute' $L)" -ne 1 ]; then
    echo "unexpected GPU visibility"; grep "inference compute" $L; exit 1
fi
warm_models || exit 1
# Refuse to run unless every loaded model is fully on the GPU.
if ! awk '/offloaded [0-9]+\/[0-9]+ layers to GPU/ { split($0, a, "offloaded "); split(a[2], b, /[\/ ]/); n++; if (b[1] != b[2]) bad=1 } END { exit (n >= 2 && !bad) ? 0 : 1 }' $L; then
    echo "model not fully on GPU"; grep -E 'offloaded [0-9]+/[0-9]+ layers|n_ctx_slot' $L; exit 1
fi
grep -E 'offloaded [0-9]+/[0-9]+ layers|n_ctx_slot =' $L | sort -u
SMOKE=/scratch/madan.pa/aurelia-r2/r3smoke-$SLURM_JOB_ID.jsonl
for c in 0 18 36; do python3 -I run_study.py --config config_r3.json --out $SMOKE --only-cell $c --n 1 || exit 1; done
python3 -I -c "
import json, sys
rows = [json.loads(l) for l in open('$SMOKE')]
ok = len(rows) == 3 and all(r['recovery_exit'] == 0 and r['reached_credit'] for r in rows)
print('smoke', 'ok' if ok else 'FAILED', [(r['task'], r['recovery_exit'], r['reached_credit'], r['credits_per_case']) for r in rows])
sys.exit(0 if ok else 1)" || exit 1
mkdir -p data
python3 -I run_study.py --config config_r3.json --out data/r3.jsonl --workers 16
