#requires -Version 7.0
<#
.SYNOPSIS
    Prepare and validate a release commit, then push main and its version tag.

.DESCRIPTION
    Commits the patch version before the shared audit/build/offline checks and
    ordinary SQL and dedicated TLS tests. Missing TLS cases remain unverified.
    Pushes only the unchanged, clean source. The tag workflow creates a draft;
    verifies the downloaded assets and records sanitized local results in its notes.
    The maintainer reviews TLS/dependency limitations and publishes manually.
    Git authentication and authenticated gh are required before any mutation.

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
        [string]$Commit,
        [int]$TimeoutMinutes = 20
    )

    Write-Host "[INFO] Warte auf Release-Workflow fuer $TagName..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)

    while ((Get-Date) -lt $deadline) {
        $runJson = gh run list --workflow release.yml --branch $TagName --commit $Commit --event push --limit 5 --json databaseId,headBranch,headSha,status,conclusion,url
        if ($LASTEXITCODE -ne 0) {
            throw "gh run list fehlgeschlagen (Exitcode $LASTEXITCODE)."
        }
        $runs = @($runJson | ConvertFrom-Json)
        foreach ($candidate in $runs) {
            if ($candidate.headBranch -cne $TagName -or $candidate.headSha -cne $Commit) {
                throw 'Release workflow query returned an unexpected tag/source.'
            }
        }
        $run = $runs | Select-Object -First 1

        if ($run) {
            if ($run.status -eq 'completed') {
                if ($run.conclusion -eq 'success') {
                    return $run
                }

                throw "Release-Workflow fehlgeschlagen: $($run.url)"
            }

            Write-Host "       Status: $($run.status) – $($run.url)" -ForegroundColor DarkGray
        }

        Start-Sleep -Seconds 15
    }

    throw "Timeout beim Warten auf den Release-Workflow fuer $TagName."
}

function Get-CheckedDraft {
    param([string]$TagName, [string]$Commit, [string]$Version, [string]$RunUrl)

    $json = gh release view $TagName --json url,isDraft,tagName,targetCommitish,body,assets
    if ($LASTEXITCODE -ne 0) { throw "gh release view failed (exit $LASTEXITCODE)." }
    $draft = $json | ConvertFrom-Json
    if ($draft.isDraft -ne $true -or $draft.tagName -cne $TagName -or
        $draft.targetCommitish -cne $Commit -or -not $draft.url) {
        throw 'Expected draft tag/source/target does not match; refusing notes changes.'
    }
    foreach ($line in @("Source commit: $Commit", "Version: $Version (tag $TagName)")) {
        if ($draft.body -cnotmatch "(?m)^$([regex]::Escape($line))\r?$") {
            throw 'Draft notes do not identify the expected source/version.'
        }
    }
    if ($draft.body -cnotmatch "(?m)^Automated run: $([regex]::Escape($RunUrl)) \(attempt \d+\)\r?$") {
        throw 'Draft does not belong to the successful release workflow.'
    }
    foreach ($format in @('ZIP', 'MCPB')) {
        if ([regex]::Matches($draft.body, "(?m)^$format SHA-256: ([0-9a-f]{64})\r?$").Count -ne 1) {
            throw "Draft is missing its unique workflow-generated $format hash."
        }
    }
    if (@($draft.assets).Count -ne 4) { throw 'Draft must contain exactly the two checked archives and their checksums.' }
    foreach ($name in @('SqlToAi-win-x64.zip', 'SqlToAi-win-x64.zip.sha256', 'SqlToAi-win-x64.mcpb', 'SqlToAi-win-x64.mcpb.sha256')) {
        if (@($draft.assets | Where-Object name -CEQ $name).Count -ne 1) {
            throw "Draft is missing its unique expected asset: $name"
        }
    }
    return $draft
}

function Complete-DraftHandoff {
    param([string]$TagName, [string]$Commit, [string]$Version, [string]$RunUrl,
        [xml]$Offline, [xml]$Sql, [xml]$Tls)

    $draft = Get-CheckedDraft -TagName $TagName -Commit $Commit -Version $Version -RunUrl $RunUrl
    $directory = Join-Path ([System.IO.Path]::GetTempPath()) ('SqlToAi-draft-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    try {
        gh release download $TagName --pattern SqlToAi-win-x64.zip --pattern SqlToAi-win-x64.zip.sha256 `
            --pattern SqlToAi-win-x64.mcpb --pattern SqlToAi-win-x64.mcpb.sha256 --dir $directory | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "gh release download failed (exit $LASTEXITCODE)." }
        foreach ($format in @('zip', 'mcpb')) {
            $name = "SqlToAi-win-x64.$format"
            $hash = (Get-FileHash -LiteralPath (Join-Path $directory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
            $expected = [regex]::Match($draft.body, "(?m)^$($format.ToUpperInvariant()) SHA-256: ([0-9a-f]{64})\r?$").Groups[1].Value
            if ($hash -cne $expected -or [System.IO.File]::ReadAllText((Join-Path $directory "$name.sha256")) -cne "$hash  $name`n") {
                throw "Downloaded $name/checksum differs from the workflow-checked hash."
            }
        }
        # Re-read before editing so a concurrent publication or notes change is refused.
        $current = Get-CheckedDraft -TagName $TagName -Commit $Commit -Version $Version -RunUrl $RunUrl
        if ($current.body -cne $draft.body -or (Get-AssetIdentity $current.assets) -cne (Get-AssetIdentity $draft.assets)) {
            throw 'Draft notes/assets changed during verification; inspect before continuing.'
        }
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add('## Local validation recorded by release.ps1')
        $lines.Add('')
        $lines.Add("Tested source: $Commit; version: $Version; tag: $TagName")
        $lines.Add('Maintainer-supplied local execution facts; local test binaries do not prove CI archive byte identity.')
        foreach ($selection in @(
            @{ Name = 'Offline'; Trx = $Offline; Command = 'scripts/validate.ps1 -ResultsDirectory <temporary-directory> (dependency audit, build, offline tests)' },
            @{ Name = 'Ordinary SQL'; Trx = $Sql; Command = "dotnet test SqlToAi.slnx --no-build --no-restore --filter 'Category=Integration' --results-directory <temporary-directory> --logger 'trx;LogFileName=Integration.trx' -- RunConfiguration.TreatNoTestsAsError=true" },
            @{ Name = 'Dedicated TLS'; Trx = $Tls; Command = "dotnet test SqlToAi.slnx --no-build --no-restore --filter 'Category=TlsIntegration' --results-directory <temporary-directory> --logger 'trx;LogFileName=TlsIntegration.trx' -- RunConfiguration.TreatNoTestsAsError=true" }
        )) {
            $counts = $selection.Trx.TestRun.ResultSummary.Counters
            $skipped = @($selection.Trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
            $lines.Add('')
            $lines.Add("Command: ``$($selection.Command)``")
            $lines.Add("$($selection.Name): total=$($counts.total), executed=$($counts.executed), passed=$($counts.passed), failed=$($counts.failed), skipped/unverified=$skipped")
        }
        foreach ($test in @($Tls.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted')) {
            # These TLS cases use endpoint labels/booleans, never profile contents. Keep
            # their case names distinct while excluding controls and Markdown delimiters.
            $name = [regex]::Replace($test.testName, '[^A-Za-z0-9_.+():, "-]', '_')
            $lines.Add("- Unverified TLS: ``$name``")
        }
        $lines.Add('')
        $lines.Add('TLS limitations and dependency exceptions still require explicit maintainer review before manual publication. These facts do not approve limitations or exceptions.')
        $body = [regex]::Replace($draft.body, '(?m)^- Local ordinary SQL: PENDING\.[^\r\n]*', '- Local ordinary SQL: recorded below for this exact source; inspect the local validation facts before publication.')
        $body = [regex]::Replace($body, '(?m)^- Dedicated TLS: PENDING review\.[^\r\n]*', '- Dedicated TLS: execution facts and unverified cases recorded below; limitation review remains PENDING. Trusted/untrusted TCP endpoint evidence remains required or explicitly disclosed under the accepted limitation. Failed configured TLS blocks publication.')
        $body = $body.TrimEnd() + "`n`n" + ($lines -join "`n") + "`n"
        $notes = Join-Path $directory 'release-notes.md'
        [System.IO.File]::WriteAllText($notes, $body)
        gh release edit $TagName --notes-file $notes | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "gh release edit failed (exit $LASTEXITCODE)." }
        $updated = Get-CheckedDraft -TagName $TagName -Commit $Commit -Version $Version -RunUrl $RunUrl
        if ($updated.body -cne $body -or (Get-AssetIdentity $updated.assets) -cne (Get-AssetIdentity $draft.assets)) {
            throw 'Draft notes/assets differ after the handoff; inspect the release manually.'
        }
        return $updated.url
    }
    finally {
        $resolved = (Resolve-Path -LiteralPath $directory).Path
        if ([System.IO.Path]::GetDirectoryName($resolved) -ine [System.IO.Path]::GetTempPath().TrimEnd('\', '/')) {
            throw 'Refusing cleanup outside the owned temporary draft directory.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Get-AssetIdentity {
    param([object[]]$Assets)

    # Downloads increment downloadCount; compare only stable asset identity/content fields.
    return ConvertTo-Json -InputObject @($Assets | Sort-Object name | Select-Object id,name,size,state,digest) -Compress
}

$repoRoot = Get-RepoRoot
Push-Location $repoRoot
try {
    Assert-MainBranch
    Assert-CleanWorkingTree
    if (-not $DryRun -and -not (Test-GhAuthenticated)) {
        throw 'Authenticated GitHub CLI (gh auth login) is required before preparing a release. No version/Git changes were made.'
    }

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
        Write-Host '[PLAN] Require authenticated gh before mutations; require unchanged HEAD, clean working tree and main before push/tag. Wait for the exact tag/source workflow, verify both downloaded draft archives/checksums and record sanitized local results. Publication remains manual.'
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
            Write-Host 'Review/disclose missing TLS evidence and source dependency exceptions in the draft. Local test binaries are not proof of CI archive byte identity.'
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

    $run = Wait-ReleaseWorkflow -TagName $tagName -Commit $releaseCommit
    $url = Complete-DraftHandoff -TagName $tagName -Commit $releaseCommit -Version $newVersion.ToString() -RunUrl $run.url -Offline $offline -Sql $sql -Tls $tls
    Write-Host "[OK] Verified draft with local results ready for maintainer review: $url. Review TLS limitations and dependency exceptions, then publish the existing checked assets manually." -ForegroundColor Green
}
catch {
    Write-Warning 'Release stopped. Earlier operations are not rolled back; inspect the version commit, pushed state, tag and draft before recovery. Do not blindly rerun: that increments the version again.'
    throw
}
finally {
    Pop-Location
}
