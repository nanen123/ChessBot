param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')][string]$RunId = 'chess-v1',
    [switch]$Smoke,
    [switch]$Resume,
    [switch]$Editor,
    [int]$Seed = 12345,
    [string]$EnvironmentPath = ''
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$trainer = Join-Path $project '.venv-training/Scripts/mlagents-learn.exe'
if (!(Test-Path -LiteralPath $trainer)) { throw 'Run Codex/SetupTraining.ps1 first.' }
if ($RunId -eq 'metadata') { throw 'The run id metadata is reserved.' }
$config = Join-Path $project $(if ($Smoke) { 'config/chess_sac_smoke.yaml' } else { 'config/chess_sac.yaml' })
$schema = Join-Path $project 'config/chess_v1.schema.json'
$results = Join-Path $project 'results'
$runDirectory = Join-Path $results $RunId
$metadataDirectory = Join-Path $results "metadata/$RunId"
$savedSchema = Join-Path $metadataDirectory 'chess_v1.schema.json'
if ($Resume) {
    if (!(Test-Path -LiteralPath $savedSchema) -or !(Test-Path -LiteralPath $runDirectory)) { throw 'Resume requires an existing run and its schema metadata.' }
    if ((Get-FileHash $schema).Hash -ne (Get-FileHash $savedSchema).Hash) { throw 'Observation/action/reward schema changed. Use a new run id.' }
} elseif ((Test-Path -LiteralPath $runDirectory) -or (Test-Path -LiteralPath $metadataDirectory)) {
    throw 'This run id already exists. Choose a new run id or explicitly use -Resume.'
}
if (!$Editor) {
    if (!$EnvironmentPath) { $EnvironmentPath = Join-Path $project 'output/chess-training/ChessTraining.exe' }
    if (!(Test-Path -LiteralPath $EnvironmentPath)) { throw 'Build the training player with ChessBot/Training/Build Windows Training Player, or use -Editor.' }
}
New-Item -ItemType Directory -Force -Path $metadataDirectory | Out-Null
if (!$Resume) {
    Copy-Item -LiteralPath $schema -Destination $savedSchema
    Copy-Item -LiteralPath $config -Destination (Join-Path $metadataDirectory 'trainer.yaml')
    Copy-Item -LiteralPath (Join-Path $project 'config/requirements-training-lock.txt') -Destination $metadataDirectory
    [IO.File]::WriteAllText((Join-Path $metadataDirectory 'run.json'), (@{ runId = $RunId; seed = $Seed; createdUtc = [DateTime]::UtcNow.ToString('O'); schema = 1; engine = '6000.3.10f1'; mlAgentsUnity = '4.1.0'; mode = $(if ($Editor) { 'Editor' } else { 'Standalone' }) } | ConvertTo-Json))
}
$trainerArgs = @($config, '--run-id', $RunId, '--results-dir', $results, '--seed', $Seed, '--max-lifetime-restarts', '0')
if ($Resume) { $trainerArgs += '--resume' }
if (!$Editor) { $trainerArgs += @('--env', $EnvironmentPath, '--no-graphics') }
$oldOmp = $env:OMP_NUM_THREADS
$oldMkl = $env:MKL_NUM_THREADS
try {
    $env:OMP_NUM_THREADS = '4'; $env:MKL_NUM_THREADS = '4'
    & $trainer @trainerArgs
    if ($LASTEXITCODE -ne 0) { throw "ML-Agents exited with code $LASTEXITCODE. See the run logs; existing results were preserved." }
} finally { $env:OMP_NUM_THREADS = $oldOmp; $env:MKL_NUM_THREADS = $oldMkl }
