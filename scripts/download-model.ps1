<#
.SYNOPSIS
    Download the YAMNet sound classifier into model\ and copy it into app\model\.

.DESCRIPTION
    scripts\setup.ps1 does this as part of a full install. Use this on its own if the model is
    missing or corrupt and you do not want to re-run everything.
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = "Stop"
$root  = Split-Path -Parent $PSScriptRoot
$model = Join-Path $root "model"
$app   = Join-Path $root "app\model"
New-Item -ItemType Directory -Force -Path $model | Out-Null

$files = @(
    @{ Name = "yamnet.onnx";          Url = "https://huggingface.co/andrelgomes/yamnet-onnx/resolve/main/yamnet.onnx" },
    @{ Name = "yamnet_class_map.csv"; Url = "https://raw.githubusercontent.com/tensorflow/models/master/research/audioset/yamnet/yamnet_class_map.csv" }
)

foreach ($f in $files) {
    $dest = Join-Path $model $f.Name
    if ((Test-Path $dest) -and -not $Force) {
        Write-Host "have    $($f.Name) ($([int]((Get-Item $dest).Length / 1KB)) KB)" -ForegroundColor Green
    } else {
        Write-Host "getting $($f.Name) ..."
        Invoke-WebRequest -Uri $f.Url -OutFile $dest -UseBasicParsing
        Write-Host "done    $($f.Name) ($([int]((Get-Item $dest).Length / 1KB)) KB)" -ForegroundColor Green
    }
    if (Test-Path (Split-Path $app -Parent)) {
        New-Item -ItemType Directory -Force -Path $app | Out-Null
        Copy-Item $dest (Join-Path $app $f.Name) -Force
    }
}

Write-Host "`nYAMNet is ready. Start the overlay with app\PhasmoSound.exe" -ForegroundColor Cyan
