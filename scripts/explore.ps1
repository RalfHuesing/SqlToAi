#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Scenario,
    [switch]$List,
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'tools/SqlToAi.Exploration/SqlToAi.Exploration.csproj'
if ($List) {
    & dotnet run --project $project -- --list
} elseif ($Scenario) {
    & dotnet run --project $project -- $repoRoot $Scenario $TimeoutSeconds
} else {
    throw 'Supply -Scenario <name> or -List.'
}
exit $LASTEXITCODE
