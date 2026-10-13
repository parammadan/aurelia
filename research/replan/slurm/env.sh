# Shared environment for the Explorer jobs. Everything lives on scratch and
# was installed/pulled off-GPU beforehand; jobs never download.
R=/scratch/madan.pa/aurelia-r2
export PATH=$R/dotnet:$PATH DOTNET_ROOT=$R/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1
# Models are copied to node-local disk first (NFS loads are slow and would
# leave an allocated GPU idle).
export OLLAMA_MODELS=${TMPDIR:-/tmp}/aurelia-r2-models-$SLURM_JOB_ID OLLAMA_HOST=127.0.0.1:$((11400 + SLURM_JOB_ID % 500))
export OLLAMA_KEEP_ALIVE=60m
export MODEL_ENDPOINT=http://$OLLAMA_HOST/v1
# The cluster sets http(s)_proxy; local servers must bypass it.
export no_proxy=127.0.0.1,localhost NO_PROXY=127.0.0.1,localhost
module load python/3.13.5 >/dev/null 2>&1 || true

start_ollama() {
    mkdir -p "$OLLAMA_MODELS" && cp -r $R/models/. "$OLLAMA_MODELS"/
    $R/ollama/bin/ollama serve > "$1" 2>&1 &
    OLLAMA_PID=$!
    for _ in $(seq 60); do
        curl -sf "http://$OLLAMA_HOST/api/tags" >/dev/null && return 0
        sleep 1
    done
    echo "ollama did not start"; return 1
}

warm_models() {
    for m in qwen2.5:3b llama3.2:3b; do
        curl -sf --max-time 300 "http://$OLLAMA_HOST/api/generate" \
            -d "{\"model\":\"$m\",\"prompt\":\"hi\",\"stream\":false,\"keep_alive\":\"60m\",\"options\":{\"num_predict\":1}}" >/dev/null \
            || { echo "warm $m failed"; return 1; }
    done
}

cleanup() {
    kill $OLLAMA_PID 2>/dev/null || true
    rm -rf "$OLLAMA_MODELS"
}
