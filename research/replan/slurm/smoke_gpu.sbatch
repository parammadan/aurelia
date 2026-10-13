#!/bin/bash
# GPU smoke test of the R2 code path: 3 runs, output to scratch only.
# Submitted only with Param's explicit OK for this job (HPC etiquette rule).
# Resumable: a time-out just means resubmit; finished rows are skipped.
#SBATCH -J r2-gpusmoke -p gpu --gres=gpu:a100:1 -c 16 --mem=48G -t 00:10:00 -o /scratch/madan.pa/aurelia-r2/gpusmoke-%j.log
set -euo pipefail
source /scratch/madan.pa/aurelia-r2/code/research/replan/slurm/env.sh
export OLLAMA_NUM_PARALLEL=16 OLLAMA_MAX_LOADED_MODELS=2
cd /scratch/madan.pa/aurelia-r2/code/research/replan
nvidia-smi --query-gpu=name,driver_version --format=csv,noheader
start_ollama /scratch/madan.pa/aurelia-r2/r2-ollama-$SLURM_JOB_ID.log || exit 1
trap cleanup EXIT
warm_models || exit 1

OUT=/scratch/madan.pa/aurelia-r2/gpusmoke-$SLURM_JOB_ID.jsonl
for c in 41 80 89; do python3 -I run_study.py --config config_r2.json --out $OUT --only-cell $c --n 1 || exit 1; done
cat $OUT
