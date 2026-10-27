# Builds the app and copies the single exe into dist\.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet build "$root\src\TF2Crosshairs\TF2Crosshairs.csproj" -c Release -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item "$root\src\TF2Crosshairs\bin\Release\net48\TF2Crosshairs.exe" "$root\dist\TF2Crosshairs.exe" -Force
Write-Host "Built $root\dist\TF2Crosshairs.exe"
