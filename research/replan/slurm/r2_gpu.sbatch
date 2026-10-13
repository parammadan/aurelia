#!/bin/bash
# Round 2 measured run: ONE A100, 16 concurrent runs against one Ollama.
# Submitted only with Param's explicit OK for this job (HPC etiquette rule).
# Resumable: a time-out just means resubmit; finished rows are skipped.
#SBATCH -J r2-replan -p gpu --gres=gpu:a100:1 -c 16 --mem=48G -t 01:00:00 -o /scratch/madan.pa/aurelia-r2/r2-%j.log
set -euo pipefail
source /scratch/madan.pa/aurelia-r2/code/research/replan/slurm/env.sh
export OLLAMA_NUM_PARALLEL=16 OLLAMA_MAX_LOADED_MODELS=2
cd /scratch/madan.pa/aurelia-r2/code/research/replan
nvidia-smi --query-gpu=name,driver_version --format=csv,noheader
start_ollama /scratch/madan.pa/aurelia-r2/r2-ollama-$SLURM_JOB_ID.log || exit 1
# Warm both models so the first runs don't time out on load.
for m in qwen2.5:3b llama3.2:3b; do
    curl -sf "http://$OLLAMA_HOST/api/generate" -d "{\"model\":\"$m\",\"prompt\":\"hi\",\"stream\":false}" >/dev/null || exit 1
done
mkdir -p data
python3 -I run_study.py --config config_r2.json --out data/r2.jsonl --workers 16
kill $OLLAMA_PID
