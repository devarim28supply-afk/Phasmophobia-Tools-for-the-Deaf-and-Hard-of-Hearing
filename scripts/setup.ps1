<#
.SYNOPSIS
    One-shot setup for Phasmophobia Tools for the Deaf and Hard of Hearing.

.DESCRIPTION
    Builds the overlay and tools into app\, creates the Python environment for the local voice and
    caption servers, downloads the models, and writes a starting config.json.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -SkipModels
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -WhisperModel medium.en
#>
[CmdletBinding()]
param(
    [switch]$SkipModels,                 # build only; models download on first run instead
    [switch]$SkipPython,                 # skip the voice/caption environment entirely
    [string]$WhisperModel = "small.en"   # tiny.en | base.en | small.en | medium.en | large-v3
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app  = Join-Path $root "app"

function Step($n, $msg) { Write-Host "`n[$n/5] $msg" -ForegroundColor Cyan }
function Ok($msg)       { Write-Host "      $msg" -ForegroundColor Green }
function Warn($msg)     { Write-Host "      $msg" -ForegroundColor Yellow }

Write-Host "Phasmophobia Tools for the Deaf and Hard of Hearing - setup" -ForegroundColor White
Write-Host "Repository: $root"

# ---------------------------------------------------------------- 1. prerequisites
Step 1 "Checking prerequisites"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not installed. Get .NET 10 from https://dotnet.microsoft.com/download"
}
$sdk = (dotnet --version)
Ok ".NET SDK $sdk"
if ($sdk -notmatch '^(1[0-9])\.') { Warn "This project targets .NET 10. Build may fail on $sdk." }

$hasUv = [bool](Get-Command uv -ErrorAction SilentlyContinue)
if (-not $hasUv -and -not $SkipPython) {
    Warn "uv is not installed - it manages Python for the voice and caption servers."
    Warn "Install it with:  winget install astral-sh.uv"
    Warn "Continuing without the Python side; re-run this script after installing uv."
    $SkipPython = $true
} elseif ($hasUv) {
    Ok "uv $((uv --version) -replace 'uv ', '')"
}

$gpu = $null
if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    $gpu = (nvidia-smi --query-gpu=name --format=csv,noheader 2>$null | Select-Object -First 1)
}
if ($gpu) { Ok "GPU: $gpu (captions will use it)" } else { Warn "No NVIDIA GPU found - captions will run on the CPU (slower but fine)" }

# ---------------------------------------------------------------- 2. build
Step 2 "Building the overlay and tools"
New-Item -ItemType Directory -Force -Path $app | Out-Null
Push-Location (Join-Path $root "PhasmoSound")
try {
    dotnet publish -c Release -r win-x64 --self-contained false -o $app | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "PhasmoSound failed to build" }
    Ok "PhasmoSound.exe"
} finally { Pop-Location }

foreach ($tool in @("PhasmoAudioExtract", "SetDefaultMic")) {
    Push-Location (Join-Path $root $tool)
    try {
        dotnet publish -c Release -r win-x64 --self-contained false -o $app | Out-Null
        if ($LASTEXITCODE -eq 0) { Ok "$tool.exe" } else { Warn "$tool failed to build (not fatal)" }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------- 3. sound classifier
Step 3 "Sound classifier (YAMNet, 16 MB)"
$modelDir = Join-Path $root "model"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null
$onnx = Join-Path $modelDir "yamnet.onnx"
$csv  = Join-Path $modelDir "yamnet_class_map.csv"
if (-not (Test-Path $onnx)) {
    Write-Host "      downloading yamnet.onnx ..."
    Invoke-WebRequest -Uri "https://huggingface.co/andrelgomes/yamnet-onnx/resolve/main/yamnet.onnx" -OutFile $onnx -UseBasicParsing
}
if (-not (Test-Path $csv)) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/tensorflow/models/master/research/audioset/yamnet/yamnet_class_map.csv" -OutFile $csv -UseBasicParsing
}
Copy-Item $onnx (Join-Path $app "model\yamnet.onnx") -Force
Copy-Item $csv  (Join-Path $app "model\yamnet_class_map.csv") -Force
Ok "model\yamnet.onnx ($([int]((Get-Item $onnx).Length / 1MB)) MB)"

# ---------------------------------------------------------------- 4. voice + captions
if ($SkipPython) {
    Step 4 "Voice and captions - SKIPPED"
    Warn "Install uv, then re-run this script to enable speaking and captions."
} else {
    Step 4 "Voice and caption servers (Python 3.12)"
    $py = Join-Path $app "python"
    New-Item -ItemType Directory -Force -Path $py | Out-Null
    Copy-Item (Join-Path $root "python\*") $py -Force -Recurse
    Push-Location $py
    try {
        if (-not (Test-Path ".venv")) { uv venv .venv --python 3.12 | Out-Null }
        Ok "virtual environment"
        Write-Host "      installing kokoro + faster-whisper (this is the long part) ..."
        uv pip install --python .venv\Scripts\python.exe -r requirements.txt 2>&1 | Select-Object -Last 1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "pip install failed" }
        Ok "packages installed"

        if (-not $SkipModels) {
            Write-Host "      downloading the voice model (Kokoro, ~350 MB) ..."
            & .venv\Scripts\python.exe -c @"
import espeakng_loader; espeakng_loader.make_library_available()
from kokoro import KPipeline
p = KPipeline(lang_code='a')
list(p('warm up', voice='am_adam'))
print('kokoro ok')
"@ 2>&1 | Select-String "kokoro ok" | Out-Null
            Ok "voice model"

            Write-Host "      downloading the caption model (Whisper $WhisperModel) ..."
            & .venv\Scripts\python.exe -c @"
from faster_whisper import WhisperModel
import numpy as np
try:
    m = WhisperModel('$WhisperModel', device='cuda', compute_type='float16'); dev='GPU'
except Exception:
    m = WhisperModel('$WhisperModel', device='cpu', compute_type='int8'); dev='CPU'
list(m.transcribe(np.zeros(16000, dtype='float32'), language='en')[0])
print('whisper ok on', dev)
"@ 2>&1 | Select-String "whisper ok" | ForEach-Object { Ok $_.ToString().Trim() }
        } else {
            Warn "models will download on first run"
        }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------- 5. config
Step 5 "Configuration"
$cfgPath = Join-Path $app "config.json"
if (Test-Path $cfgPath) {
    Ok "app\config.json already exists, left alone"
} else {
    $cfg = Get-Content (Join-Path $root "config\config.example.json") -Raw | ConvertFrom-Json
    $cfg.PSObject.Properties.Remove("_comment")
    $cfg.WhisperModel = $WhisperModel
    $cfg | ConvertTo-Json -Depth 5 | Set-Content $cfgPath -Encoding utf8
    Ok "app\config.json written"
}
New-Item -ItemType Directory -Force -Path (Join-Path $app "phrases") | Out-Null
$phrases = Join-Path $app "phrases\phrases.json"
if (-not (Test-Path $phrases)) {
    Copy-Item (Join-Path $root "config\phrases.example.json") $phrases -Force
    Ok "app\phrases\phrases.json written (63 lines)"
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host @"

Next:
  1. Start Phasmophobia (windowed or borderless), then run:
         $app\PhasmoSound.exe
     Seeing sounds and reading captions work right away.

  2. To speak to your team, install VB-CABLE from https://vb-audio.com/Cable/
     (run the installer as administrator, then reboot), and set
     Phasmophobia -> Options -> Audio -> Microphone = CABLE Output.

  Keys:  middle mouse = phrase wheel | F6 = type to speak | F8 = hide | Ctrl+F8 = quit
  Docs:  docs\INSTALL.md  docs\AUDIO-ROUTING.md  docs\CONFIG.md
"@
