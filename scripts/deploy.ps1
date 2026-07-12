# SqlToAi Deployment Script
# Compiles the application as a self-contained single-file executable for Windows win-x64.
# Note: Native AOT is explicitly NOT used. Microsoft.Data.SqlClient and Dapper are
# fundamentally AOT-incompatible (IL3053/IL2104 hard blockers), violating the
# Zero-Warning directive. Self-contained single-file is the correct deployment strategy.

$ErrorActionPreference = "Stop"

# Define directories relative to the script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Get-Item $ScriptDir).Parent.FullName
$ProjectFile = Join-Path $ProjectRoot "src\SqlToAi\SqlToAi.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " SqlToAi Release Deployment Build" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"
Write-Host "Output Dir  : $PublishDir"
Write-Host ""

# 1. Check prerequisites
Write-Host "[1/5] Checking prerequisites..." -ForegroundColor Yellow
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK is not installed or not in PATH."
    exit 1
}
Write-Host "dotnet SDK found." -ForegroundColor Green

# 2. Stop any running instance so the publish step can overwrite the executable.
# A running MCP host holds SqlToAi.exe open for the lifetime of the stdio session,
# which would otherwise make the publish step fail with a file-lock error.
Write-Host "[2/5] Stopping any running SqlToAi.exe instance..." -ForegroundColor Yellow
$runningProcesses = Get-Process -Name "SqlToAi" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    $runningProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "Stopped $($runningProcesses.Count) running instance(s)." -ForegroundColor Green
}
else {
    Write-Host "No running instance found." -ForegroundColor Gray
}

# 3. Run Tests
Write-Host "[3/5] Running all unit and integration tests..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    dotnet test -c Debug
    Write-Host "All tests passed successfully!" -ForegroundColor Green
}
catch {
    Write-Error "Tests failed. Aborting deployment."
    Pop-Location
    exit 1
}
Pop-Location

# 4. Publish Self-Contained Single-File Release
Write-Host "[4/5] Publishing Self-Contained Single-File Release for win-x64..." -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Write-Host "Cleaning existing publish directory..." -ForegroundColor Gray
    Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

try {
    # Self-contained single-file publish for win-x64.
    # Native AOT is not used: Microsoft.Data.SqlClient and Dapper are fundamentally
    # AOT-incompatible (IL3053/IL2104 hard blockers), which would cause runtime crashes.
    dotnet publish $ProjectFile `
        -c Release `
        -r win-x64 `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -o $PublishDir `
        --self-contained

    Write-Host "Publish completed successfully!" -ForegroundColor Green
}
catch {
    Write-Error "Publish failed."
    exit 1
}

# 5. Verification and cleanup of build artifacts
Write-Host "[5/5] Verifying build output..." -ForegroundColor Yellow
$ExePath = Join-Path $PublishDir "SqlToAi.exe"
$ConfigPath = Join-Path $PublishDir "appsettings.json"

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable was not found in output directory."
    exit 1
}
if (-not (Test-Path $ConfigPath)) {
    Write-Error "appsettings.json was not found in output directory."
    exit 1
}

Write-Host ""
Write-Host "Deployment package created successfully in:" -ForegroundColor Green
Write-Host "  $PublishDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package contents:" -ForegroundColor Gray
Get-ChildItem -Path $PublishDir | Format-Table Name, Length, LastWriteTime
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "NOTE: If your AI client (Cursor, Antigravity IDE, etc.) already had an MCP" -ForegroundColor Yellow
Write-Host "session open against the old executable, its stdio channel is now stale." -ForegroundColor Yellow
Write-Host "Reload/restart the MCP server entry in the client (or restart the client) to reconnect." -ForegroundColor Yellow
