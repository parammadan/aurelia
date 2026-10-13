# Shared environment for the Explorer jobs. Everything lives on scratch and
# was installed/pulled off-GPU beforehand; jobs never download.
R=/scratch/madan.pa/aurelia-r2
export PATH=$R/dotnet:$PATH DOTNET_ROOT=$R/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1
export OLLAMA_MODELS=$R/models OLLAMA_HOST=127.0.0.1:$((11400 + SLURM_JOB_ID % 500))
export MODEL_ENDPOINT=http://$OLLAMA_HOST/v1
module load python/3.13.5 >/dev/null 2>&1 || true

start_ollama() {
    $R/ollama/bin/ollama serve > "$1" 2>&1 &
    OLLAMA_PID=$!
    for _ in $(seq 60); do
        curl -sf "http://$OLLAMA_HOST/api/tags" >/dev/null && return 0
        sleep 1
    done
    echo "ollama did not start"; return 1
}
