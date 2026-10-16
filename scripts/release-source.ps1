#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $headCommit = git --no-pager rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the checked-out source commit.' }
    $tagCommit = git --no-pager rev-parse --verify "$Tag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Cannot resolve release tag $Tag." }
    if ($headCommit -cne $SourceCommit -or $tagCommit -cne $SourceCommit) {
        throw 'Tag, checked-out source and expected source commit must agree.'
    }
    [xml]$project = Get-Content -LiteralPath 'src/SqlToAi/SqlToAi.csproj' -Raw
    $versions = @($project.Project.PropertyGroup.Version | Where-Object { $_ })
    if ($versions.Count -ne 1 -or $Tag -cne "v$($versions[0])") {
        throw 'Release tag must equal v followed by the project Version.'
    }
    [pscustomobject]@{ Tag = $Tag; Version = [string]$versions[0]; Commit = $SourceCommit }
}
finally {
    Pop-Location
}
