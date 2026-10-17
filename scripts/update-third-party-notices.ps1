#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates THIRD-PARTY-NOTICES.txt from the current project dependencies.

.DESCRIPTION
    Uses dotnet-project-licenses to inspect runtime dependencies for net10.0,
    formats author and copyright attribution notices, and merges them with
    the static license texts and .NET runtime notices in packaging/license-terms.txt.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/SqlToAi/SqlToAi.csproj'
$outputFile = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt'
$licenseTerms = Join-Path $repoRoot 'packaging/license-terms.txt'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project file not found: $project"
}
if (-not (Test-Path -LiteralPath $licenseTerms -PathType Leaf)) {
    throw "License terms file not found: $licenseTerms"
}

# Ensure .NET 7 tool rolls forward on .NET 8/9/10
$env:DOTNET_ROLL_FORWARD = 'Major'

# Check if dotnet-project-licenses is installed
$toolCmd = Get-Command 'dotnet-project-licenses' -ErrorAction SilentlyContinue
if (-not $toolCmd) {
    $testRun = dotnet tool list -g
    if ($testRun -notmatch 'dotnet-project-licenses') {
        Write-Host 'Installing dotnet-project-licenses tool globally...'
        dotnet tool install --global dotnet-project-licenses
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to install dotnet-project-licenses.'
        }
    }
}

$tempJson = Join-Path ([System.IO.Path]::GetTempPath()) ("licenses-" + [guid]::NewGuid().ToString('N') + ".json")
try {
    Write-Host 'Extracting NuGet project licenses...'
    & dotnet project-licenses -i $project -u -t --use-project-assets-json -j --outfile $tempJson -l Error
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tempJson -PathType Leaf)) {
        throw "dotnet project-licenses execution failed with exit code $LASTEXITCODE."
    }

    $jsonContent = Get-Content -LiteralPath $tempJson -Raw | ConvertFrom-Json
    if (-not $jsonContent) {
        throw 'Failed to parse licenses JSON output.'
    }

    # Filter out build-only analyzers and test dependencies
    $runtimePackages = $jsonContent | Where-Object {
        $_.PackageName -notmatch 'Analyzer' -and
        $_.PackageName -notmatch 'xunit' -and
        $_.PackageName -notmatch 'coverlet' -and
        $_.PackageName -notmatch 'Test\.Sdk'
    } | Sort-Object -Property PackageName

    $dependencyLines = $runtimePackages | ForEach-Object {
        $authors = if ($_.Authors) { ($_.Authors | ForEach-Object { $_.Trim() }) -join ',' } else { '' }
        $copy = if ($_.Copyright) { $_.Copyright.Trim() } else { '' }
        "* $($_.PackageName)/$($_.PackageVersion) — $($_.LicenseType); $authors; $copy"
    }

    $dateStr = (Get-Date).ToString('yyyy-MM-dd')
    $header = @"
SqlToAi third-party notices
==========================

SqlToAi itself is licensed under the separate LICENSE file. Dependencies retain
their own terms below. The self-contained executable includes the .NET runtime.
Unmodified .NET third-party notices are included below.
Review this file and upstream terms when dependencies change.

Runtime dependency review: resolved net10.0 graph, $dateStr.
Version references describe this review; the built artifacts determine shipped bytes.
Build analyzers and test-only packages are not distributed.

$($dependencyLines -join "`r`n")
"@

    $staticTerms = Get-Content -LiteralPath $licenseTerms -Raw
    $fullNoticeText = $header.TrimEnd() + "`r`n`r`n`r`n" + $staticTerms.Trim() + "`r`n"

    # Write UTF-8 without BOM
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($outputFile, $fullNoticeText, $utf8NoBom)
    Write-Host "Updated $outputFile ($($runtimePackages.Count) dependencies resolved)."
}
finally {
    if (Test-Path -LiteralPath $tempJson -PathType Leaf) {
        Remove-Item -LiteralPath $tempJson -Force -ErrorAction SilentlyContinue
    }
}
