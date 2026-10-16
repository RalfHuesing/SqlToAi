#requires -Version 7.0
<#
.SYNOPSIS
    Prepare and validate a release commit, then push main and its version tag.

.DESCRIPTION
    Commits the patch version before the shared audit/build/offline checks and
    ordinary SQL and dedicated TLS tests. Missing TLS cases remain unverified.
    Pushes only the unchanged, clean source. The tag workflow creates a draft;
    the maintainer records the local results and reviews it before publication.
    Git uses local authentication. Authenticated gh optionally monitors the draft.

.PARAMETER DryRun
    Show the plan without validation, file changes, commit, push or tag.
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectFile = 'src/SqlToAi/SqlToAi.csproj'
$VersionPattern = '(?<=<Version>)\d+\.\d+\.\d+(?=</Version>)'

function Get-RepoRoot {
    $root = git --no-pager rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $root) {
        throw 'Kein Git-Repository gefunden. Bitte im SqlToAi-Repo ausfuehren.'
    }

    return (Resolve-Path $root).Path
}

function Assert-CleanWorkingTree {
    $status = git --no-pager --no-optional-locks status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "git status fehlgeschlagen (Exitcode $LASTEXITCODE)."
    }
    if ($status) {
        throw "Working tree ist nicht sauber:`n$status"
    }
}

function Get-HeadCommit {
    $commit = git --no-pager rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or -not $commit) {
        throw 'Cannot determine HEAD.'
    }
    return $commit
}

function Assert-MainBranch {
    $branch = git --no-pager branch --show-current
    if ($LASTEXITCODE -ne 0 -or $branch -cne 'main') {
        throw 'Release requires the main branch.'
    }
}

function Assert-ReleaseSource {
    param([string]$Commit)

    Assert-MainBranch
    Assert-CleanWorkingTree
    if ((Get-HeadCommit) -cne $Commit) {
        throw 'HEAD changed after release preparation; validate the new source before releasing.'
    }
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    $content = Get-Content -Path $ProjectPath -Raw
    if ($content -notmatch $VersionPattern) {
        throw "Keine <Version> in $ProjectPath gefunden."
    }

    return [version]$Matches[0]
}

function Set-ProjectVersion {
    param(
        [string]$ProjectPath,
        [version]$NewVersion
    )

    $content = Get-Content -Path $ProjectPath -Raw
    $updated = [regex]::Replace(
        $content,
        $VersionPattern,
        $NewVersion.ToString(),
        1)

    if ($updated -eq $content) {
        throw "Version in $ProjectPath konnte nicht aktualisiert werden."
    }

    Set-Content -Path $ProjectPath -Value $updated -NoNewline -Encoding utf8
}

function Read-TestResult {
    param([string]$Path, [switch]$RequireAllPassed)

    # xUnit v3 can leave the TRX notExecuted counter at zero for skipped results.
    # Read their standard outcomes; never copy test output or profile contents.
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counts.total
    $executed = [int]$counts.executed
    $passed = [int]$counts.passed
    $failed = [int]$counts.failed
    $results = @($trx.TestRun.Results.UnitTestResult)
    $skipped = @($results | Where-Object outcome -eq 'NotExecuted').Count
    if ($total -le 0 -or $results.Count -ne $total -or $failed -ne 0 -or $passed -ne $executed -or
        $passed -ne @($results | Where-Object outcome -eq 'Passed').Count -or $total -ne ($passed + $skipped)) {
        throw 'Test results are empty, failed or incomplete.'
    }
    if ($RequireAllPassed -and ($skipped -ne 0 -or $passed -le 0)) {
        throw 'Ordinary SQL tests must execute and pass without skips.'
    }
    return $trx
}

function Write-TestSummary {
    param([string]$Name, [xml]$Trx)

    $counts = $Trx.TestRun.ResultSummary.Counters
    $skipped = @($Trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
    Write-Host "$Name`: total=$($counts.total), executed=$($counts.executed), passed=$($counts.passed), failed=$($counts.failed), skipped/unverified=$skipped"
}

function Invoke-GitStep {
    param(
        [string]$Description,
        [scriptblock]$Action
    )

    Write-Host "[INFO] $Description" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "       (DryRun)" -ForegroundColor DarkGray
        return
    }

    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Git-Befehl fehlgeschlagen: $Description"
    }
}

function Test-GhAuthenticated {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        return $false
    }

    gh auth status 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Wait-ReleaseWorkflow {
    param(
        [string]$TagName,
        [int]$TimeoutMinutes = 20
    )

    if (-not (Test-GhAuthenticated)) {
        Write-Host '[INFO] gh nicht angemeldet – Workflow-Status manuell pruefen:' -ForegroundColor Yellow
        Write-Host '       https://github.com/RalfHuesing/SqlToAi/actions/workflows/release.yml'
        return
    }

    Write-Host "[INFO] Warte auf Release-Workflow fuer $TagName..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)

    while ((Get-Date) -lt $deadline) {
        $runJson = gh run list --workflow release.yml --limit 5 --json databaseId,headBranch,status,conclusion,url
        if ($LASTEXITCODE -ne 0) {
            throw "gh run list fehlgeschlagen (Exitcode $LASTEXITCODE)."
        }
        $run = $runJson | ConvertFrom-Json | Where-Object { $_.headBranch -eq $TagName } | Select-Object -First 1

        if ($run) {
            if ($run.status -eq 'completed') {
                if ($run.conclusion -eq 'success') {
                    $releaseJson = gh release view $TagName --json url,isDraft,tagName 2>$null
                    if ($LASTEXITCODE -ne 0) {
                        throw "gh release view fehlgeschlagen (Exitcode $LASTEXITCODE)."
                    }
                    $release = $releaseJson | ConvertFrom-Json
                    if ($release.isDraft -ne $true -or $release.tagName -cne $TagName -or -not $release.url) {
                        throw 'Expected draft release is not ready for review.'
                    }
                    Write-Host "[OK] Draft ready for maintainer review: $($release.url)" -ForegroundColor Green
                    Write-Host "     Workflow: $($run.url)"
                    return
                }

                throw "Release-Workflow fehlgeschlagen: $($run.url)"
            }

            Write-Host "       Status: $($run.status) – $($run.url)" -ForegroundColor DarkGray
        }

        Start-Sleep -Seconds 15
    }

    throw "Timeout beim Warten auf den Release-Workflow fuer $TagName."
}

$repoRoot = Get-RepoRoot
Push-Location $repoRoot
try {
    Assert-MainBranch
    Assert-CleanWorkingTree

    $projectPath = Join-Path $repoRoot $ProjectFile
    if (-not (Test-Path $projectPath)) {
        throw "Projektdatei nicht gefunden: $projectPath"
    }

    $currentVersion = Get-ProjectVersion -ProjectPath $projectPath
    $newVersion = [version]"$($currentVersion.Major).$($currentVersion.Minor).$($currentVersion.Build + 1)"
    $tagName = "v$newVersion"
    $commitMessage = "chore(release): Bereite Version $newVersion vor"

    Write-Host ''
    Write-Host "SqlToAi Release" -ForegroundColor White
    Write-Host "  Aktuell : $currentVersion"
    Write-Host "  Neu     : $newVersion"
    Write-Host "  Tag     : $tagName"
    Write-Host "  DryRun  : $DryRun"
    Write-Host ''

    $existingTag = git --no-pager tag --list $tagName
    if ($LASTEXITCODE -ne 0) {
        throw "git tag --list fehlgeschlagen (Exitcode $LASTEXITCODE)."
    }
    if ($existingTag) {
        throw "Tag $tagName existiert bereits lokal."
    }

    if (-not $DryRun) {
        Set-ProjectVersion -ProjectPath $projectPath -NewVersion $newVersion
    }

    Invoke-GitStep "git add $ProjectFile" { git --no-pager add -- $ProjectFile }
    Invoke-GitStep "git commit" { git --no-pager commit -m $commitMessage }
    if ($DryRun) {
        Write-Host '[PLAN] Validate the release commit: scripts/validate.ps1 (audit/build/offline), then Category=Integration (no skips), then Category=TlsIntegration (missing cases unverified).'
        Write-Host '[PLAN] Print local results; require unchanged HEAD, clean working tree and main before push/tag.'
    }
    else {
        $releaseCommit = Get-HeadCommit
        Assert-ReleaseSource -Commit $releaseCommit
        if ((Get-ProjectVersion -ProjectPath $projectPath) -ne $newVersion) {
            throw 'Committed project version differs from the prepared release version.'
        }
        $resultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('SqlToAi-release-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
        try {
            & (Join-Path $PSScriptRoot 'validate.ps1') -ResultsDirectory $resultsDirectory
            $offline = Read-TestResult -Path (Join-Path $resultsDirectory 'offline.trx')
            foreach ($category in @('Integration', 'TlsIntegration')) {
                dotnet test SqlToAi.slnx --no-build --no-restore --filter "Category=$category" `
                    --results-directory $resultsDirectory --logger "trx;LogFileName=$category.trx" `
                    -- RunConfiguration.TreatNoTestsAsError=true
                if ($LASTEXITCODE -ne 0) {
                    throw "$category tests failed with exit code $LASTEXITCODE."
                }
                $result = Read-TestResult -Path (Join-Path $resultsDirectory "$category.trx") -RequireAllPassed:($category -eq 'Integration')
                if ($category -eq 'Integration') { $sql = $result } else { $tls = $result }
            }
            Assert-ReleaseSource -Commit $releaseCommit
            Write-Host ''
            Write-Host 'Local validation for draft notes (maintainer-supplied evidence):'
            Write-Host "Source commit: $releaseCommit; version: $newVersion; tag: $tagName"
            Write-Host 'Command: scripts/validate.ps1 (dependency audit, build, offline tests)'
            Write-TestSummary -Name 'Offline' -Trx $offline
            Write-Host "Command: dotnet test SqlToAi.slnx --no-build --no-restore --filter 'Category=Integration' -- RunConfiguration.TreatNoTestsAsError=true"
            Write-TestSummary -Name 'Ordinary SQL' -Trx $sql
            Write-Host "Command: dotnet test SqlToAi.slnx --no-build --no-restore --filter 'Category=TlsIntegration' -- RunConfiguration.TreatNoTestsAsError=true"
            Write-TestSummary -Name 'Dedicated TLS' -Trx $tls
            foreach ($test in @($tls.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted')) {
                $name = [regex]::Replace($test.testName, '[\p{Cc}\p{Cf}]', ' ')
                Write-Host "Unverified TLS: $name"
            }
            Write-Host 'Review/disclose missing TLS evidence and source dependency exceptions in the draft. Local test binaries are not proof of CI ZIP byte identity.'
        }
        finally {
            $resolvedResults = (Resolve-Path -LiteralPath $resultsDirectory).Path
            if ([System.IO.Path]::GetDirectoryName($resolvedResults) -ine [System.IO.Path]::GetTempPath().TrimEnd('\', '/')) {
                throw 'Refusing cleanup outside the temporary results directory.'
            }
            Remove-Item -LiteralPath $resolvedResults -Recurse -Force
        }
        Assert-ReleaseSource -Commit $releaseCommit
    }
    Invoke-GitStep 'git push origin main' { git --no-pager push origin main }
    if (-not $DryRun) { Assert-ReleaseSource -Commit $releaseCommit }
    Invoke-GitStep "git tag $tagName" { git --no-pager tag $tagName $releaseCommit }
    if (-not $DryRun) { Assert-ReleaseSource -Commit $releaseCommit }
    Invoke-GitStep "git push origin $tagName" { git --no-pager push origin $tagName }

    if ($DryRun) {
        Write-Host '[OK] DryRun abgeschlossen – keine Aenderungen vorgenommen.' -ForegroundColor Green
        return
    }

    Wait-ReleaseWorkflow -TagName $tagName
    Write-Host "[OK] Draft workflow triggered for $tagName. Complete the documented local-result, TLS and dependency review before publishing the existing checked ZIP." -ForegroundColor Green
}
finally {
    Pop-Location
}
