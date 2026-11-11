# Windows launcher for DriveTool (PowerShell). Same behaviour as scripts/drivetool (bash): rebuilds when sources are newer.
# Project location: $env:DRIVETOOL_PROJECT (default: drivetool/ in this skill).
$ErrorActionPreference = 'Stop'
$project = if ($env:DRIVETOOL_PROJECT) { $env:DRIVETOOL_PROJECT } else { Join-Path $PSScriptRoot '..\drivetool' }
if (-not (Test-Path $project)) { [Console]::Error.WriteLine("DriveTool project not found at $project (set DRIVETOOL_PROJECT)"); exit 2 }
$dll = Join-Path $project 'bin\Debug\net10.0\drivetool.dll'
$stale = -not (Test-Path $dll)
if (-not $stale) {
  $t = (Get-Item $dll).LastWriteTime
  $stale = [bool](Get-ChildItem $project -Recurse -Include *.cs,*.csproj,*.json -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $t } | Select-Object -First 1)
}
if ($stale) {
  [Console]::Error.WriteLine('Building DriveTool ...')
  dotnet build $project -nologo -v q | ForEach-Object { [Console]::Error.WriteLine($_) }
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet $dll @args
exit $LASTEXITCODE
