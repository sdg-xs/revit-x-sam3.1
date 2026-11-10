$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$buildOutput = Join-Path $projectRoot 'src\RevitSam.Addin\bin\Release\net10.0-windows'
$addinFolder = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Close Revit before installing the add-in, then run this script again.'
}
if (-not (Test-Path -LiteralPath (Join-Path $buildOutput 'RevitSam.Addin.dll'))) {
    throw 'Build the Revit add-in in Release mode first.'
}

New-Item -ItemType Directory -Path $addinFolder -Force | Out-Null
Get-ChildItem -LiteralPath $buildOutput -File | Copy-Item -Destination $addinFolder -Force
Write-Host "SAM 3.1 add-in installed to $addinFolder. Start Revit 2027 to load it."

