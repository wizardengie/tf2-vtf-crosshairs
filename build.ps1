# Builds the app and publishes two single-file exes into dist\:
#   TF2Crosshairs.exe        self-contained, runs on any 64-bit Windows 10/11 with nothing installed (~70 MB)
#   TF2Crosshairs-slim.exe   framework-dependent, ~1 MB, needs the .NET 10 Desktop Runtime
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = "$root\src\TF2Crosshairs\TF2Crosshairs.csproj"
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null

dotnet publish $proj -c Release -r win-x64 --self-contained true -nologo -o "$dist\publish-sc"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item "$dist\publish-sc\TF2Crosshairs.exe" "$dist\TF2Crosshairs.exe" -Force

dotnet publish $proj -c Release -r win-x64 --self-contained false -nologo -o "$dist\publish-fdd"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item "$dist\publish-fdd\TF2Crosshairs.exe" "$dist\TF2Crosshairs-slim.exe" -Force

Get-ChildItem "$dist\*.exe" | ForEach-Object { "{0,-26} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
