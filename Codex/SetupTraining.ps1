param([string]$UvPath = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$UvPath) {
    $command = Get-Command uv -ErrorAction SilentlyContinue
    $UvPath = if ($command) { $command.Source } else { Join-Path $env:USERPROFILE '.local/bin/uv.exe' }
}
if (!(Test-Path -LiteralPath $UvPath)) { throw 'Install uv from https://docs.astral.sh/uv/getting-started/installation/ or supply -UvPath.' }
$previousInstallDirectory = $env:UV_PYTHON_INSTALL_DIR
try {
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $project 'output/python-runtime'
    & $UvPath python install 3.10.12
    if ($LASTEXITCODE -ne 0) { throw 'Python installation failed.' }
    $python = Join-Path $env:UV_PYTHON_INSTALL_DIR 'cpython-3.10.12-windows-x86_64-none/python.exe'
    $environment = Join-Path $project '.venv-training'
    if (!(Test-Path -LiteralPath "$environment/Scripts/python.exe")) {
        & $UvPath venv $environment --python $python
        if ($LASTEXITCODE -ne 0) { throw 'Training environment creation failed.' }
    }
    & $UvPath pip sync --python "$environment/Scripts/python.exe" (Join-Path $project 'config/requirements-training-lock.txt') --index https://download.pytorch.org/whl/cpu --index https://pypi.org/simple --index-strategy unsafe-best-match
    if ($LASTEXITCODE -ne 0) { throw 'Training dependency installation failed.' }
    & "$environment/Scripts/python.exe" -c 'import sys, torch, mlagents; print(sys.version); print("PyTorch:", torch.__version__)'
    if ($LASTEXITCODE -ne 0) { throw 'Training environment verification failed.' }
} finally { $env:UV_PYTHON_INSTALL_DIR = $previousInstallDirectory }
