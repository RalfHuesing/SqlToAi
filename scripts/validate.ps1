#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    Write-Host 'Restoring and auditing direct and transitive dependencies (all severities)...'
    dotnet restore SqlToAi.slnx --force --no-http-cache `
        -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low `
        -p:TreatWarningsAsErrors=true
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency restore/audit failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Building...'
    dotnet build SqlToAi.slnx --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Running database-independent tests...'
    dotnet test SqlToAi.slnx --no-build --no-restore `
        --filter 'Category!=Integration&Category!=TlsIntegration' `
        -- RunConfiguration.TreatNoTestsAsError=true
    if ($LASTEXITCODE -ne 0) {
        throw "Offline tests failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Dependency audit, build and offline tests passed. Review any NuGetAuditSuppress exceptions in the source.'
}
finally {
    Pop-Location
}
