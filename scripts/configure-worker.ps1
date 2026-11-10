param(
    [Parameter(Mandatory = $true)]
    [string]$PythonExecutable,
    [Parameter(Mandatory = $true)]
    [string]$SamRepository
)

$python = (Resolve-Path -LiteralPath $PythonExecutable).Path
$samRepo = (Resolve-Path -LiteralPath $SamRepository).Path
$workerDir = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\worker')).Path
$settingsDir = Join-Path $env:LOCALAPPDATA 'RevitSam31'
$settingsPath = Join-Path $settingsDir 'settings.json'

if (-not (Test-Path -LiteralPath (Join-Path $samRepo 'sam3\model_builder.py'))) {
    throw "The selected folder does not look like Meta's SAM 3 repository: $samRepo"
}
if (-not $python.EndsWith('python.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Choose the Python 3.12 virtual-environment python.exe."
}
$pythonVersion = & $python --version 2>&1
if ($pythonVersion -notmatch '^Python 3\.12\.') {
    throw "SAM worker requires Python 3.12; found: $pythonVersion"
}

New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
@{
    pythonExecutable = $python
    workerDirectory = $workerDir
    samRepository = $samRepo
} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

Write-Host "Worker settings saved to $settingsPath"
