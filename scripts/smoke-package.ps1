#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Zip')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Zip')]
    [string]$ZipPath,
    [Parameter(Mandatory, ParameterSetName = 'Mcpb')]
    [string]$McpbPath,
    [switch]$DisposableProfile,
    [ValidateRange(1, 120)]
    [int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'

function Start-PackageProcess([string]$Executable, [string]$WorkingDirectory, [string[]]$Arguments) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # No inherited application overrides or environment-specific settings in this check.
    foreach ($key in @($info.Environment.Keys)) {
        if ($key -like 'SqlToAi__*' -or $key -like 'SqlToAi:*' -or $key -eq 'DOTNET_ENVIRONMENT') {
            [void]$info.Environment.Remove($key)
        }
    }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    return $process
}

function Resolve-BundleLaunch([string]$Installation) {
    $manifestPath = Join-Path $Installation 'manifest.json'
    $schema = Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging/mcpb-manifest-v0.3.schema.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'MCPB is missing manifest.json.' }
    $json = Get-Content -LiteralPath $manifestPath -Raw
    if (-not (Test-Json -Json $json -SchemaFile $schema -ErrorAction Stop)) {
        throw 'MCPB manifest does not conform to the pinned official schema.'
    }
    $manifest = $json | ConvertFrom-Json -AsHashtable
    $config = $manifest.server.mcp_config
    if ($manifest.manifest_version -cne '0.3' -or $manifest.server.type -cne 'binary' -or
        $manifest.compatibility.platforms -cnotcontains 'win32' -or
        [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        $manifest.ContainsKey('user_config') -or @($config.Keys | Where-Object { $_ -cnotin @('command', 'args') }).Count -ne 0) {
        throw 'MCPB smoke supports Windows x64 binary bundles without user_config, environment or platform overrides.'
    }
    # This is the SqlToAi stdio check, not a general bundle interpreter or CLI runner.
    [string[]]$arguments = @()
    if ($config.ContainsKey('args')) { $arguments = [string[]]$config.args }
    if ($arguments.Count -gt 1 -or ($arguments.Count -eq 1 -and $arguments[0] -cne 'server')) {
        throw 'MCPB smoke supports only empty arguments or the server argument.'
    }
    $executable = Join-Path $Installation 'SqlToAi.exe'
    if ($manifest.server.entry_point -cne 'SqlToAi.exe' -or $config.command -cne '${__dirname}/SqlToAi.exe' -or
        -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'MCPB entry point and command must resolve to the included SqlToAi.exe.'
    }
    $stream = [System.IO.File]::OpenRead($executable)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { throw 'MCPB entry point is not a Windows x64 PE binary.' }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadUInt32()
        if ($peOffset -gt $stream.Length - 6) { throw 'MCPB entry point has an invalid PE header.' }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw 'MCPB entry point is not a Windows x64 PE binary.'
        }
    }
    finally { $reader.Dispose() }
    return [pscustomobject]@{ Executable = $executable; Arguments = [string[]]$arguments }
}

function Stop-OwnedProcess([System.Diagnostics.Process]$Process) {
    try {
        if (-not $Process.HasExited) {
            $Process.Kill($true)
            if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
                throw 'Packaged child did not stop after termination.'
            }
        }
    }
    finally { $Process.Dispose() }
}

function Receive-ProcessText([System.Threading.Tasks.Task[string]]$Read) {
    if (-not $Read.Wait($TimeoutSeconds * 1000)) { throw 'Timed out draining packaged process output.' }
    return $Read.GetAwaiter().GetResult()
}

function Read-ProtocolMessage([string]$Line) {
    try { $message = ConvertFrom-Json -InputObject $Line -AsHashtable -ErrorAction Stop }
    catch { throw 'Packaged stdout contains invalid JSON instead of MCP protocol.' }
    if ($message -isnot [System.Collections.IDictionary] -or $message.jsonrpc -cne '2.0') {
        throw 'Packaged stdout contains an invalid JSON-RPC envelope.'
    }
    return $message
}

function Read-McpResponse([System.Diagnostics.Process]$Process, [int]$Id) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $remaining = $TimeoutSeconds * 1000 - [int]$timer.ElapsedMilliseconds
        $read = $Process.StandardOutput.ReadLineAsync()
        if ($remaining -le 0 -or -not $read.Wait($remaining)) {
            throw "Timed out waiting for MCP response $Id."
        }
        if ($null -eq $read.Result) { throw "Packaged process exited before MCP response $Id." }
        $message = Read-ProtocolMessage $read.Result
        if ($message.ContainsKey('id')) {
            if ($message.id -ne $Id -or $message.ContainsKey('error') -or -not $message.ContainsKey('result')) {
                throw "Unexpected or failed MCP response while waiting for $Id."
            }
            return $message.result
        }
        if (-not ($message.method -is [string]) -or $message.ContainsKey('result') -or $message.ContainsKey('error')) {
            throw 'Packaged stdout contains an invalid MCP notification.'
        }
    }
}

function Send-McpMessage([System.Diagnostics.Process]$Process, [hashtable]$Message) {
    $Process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 20 -Compress))
    $Process.StandardInput.Flush()
}

if (-not $IsWindows) { throw 'The package smoke check requires Windows.' }
if (-not $DisposableProfile) {
    throw 'Use -DisposableProfile only in a fresh GitHub Windows runner or another already disposable Windows profile.'
}
$localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localData)) { throw 'Windows Local Application Data is unavailable.' }
$userRoot = [System.IO.Path]::GetFullPath((Join-Path $localData 'SqlToAi'))
if (Test-Path -LiteralPath $userRoot) {
    throw "Refusing to use an existing user configuration root: $userRoot"
}
$isBundle = $PSCmdlet.ParameterSetName -eq 'Mcpb'
$archive = (Resolve-Path -LiteralPath $(if ($isBundle) { $McpbPath } else { $ZipPath })).ProviderPath
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "SqlToAi-smoke-$([guid]::NewGuid().ToString('N'))"
$installation = Join-Path $temporary 'install path with spaces'
$working = Join-Path $temporary 'different working directory'
$ownsUserRoot = $false
try {
    [void][System.IO.Directory]::CreateDirectory($working)
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $installation)
    foreach ($name in 'SqlToAi.exe', 'appsettings.json', 'demo-profile.json', 'README.md', 'LICENSE') {
        if (-not (Test-Path -LiteralPath (Join-Path $installation $name) -PathType Leaf)) {
            throw "Package is missing $name."
        }
    }
    $launch = if ($isBundle) { Resolve-BundleLaunch $installation } else {
        [pscustomobject]@{ Executable = (Join-Path $installation 'SqlToAi.exe'); Arguments = [string[]]@() }
    }
    $settings = Join-Path $userRoot 'appsettings.json'
    $process = Start-PackageProcess $launch.Executable $working $launch.Arguments
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw 'Missing-settings startup timed out.' }
        if ($process.ExitCode -ne 1 -or (Receive-ProcessText $stdout) -ne '') {
            throw 'Missing settings must produce exit 1 and empty stdout.'
        }
        $diagnostic = Receive-ProcessText $stderr
        if (-not $diagnostic.Contains($settings) -or $diagnostic -notmatch '(?i)missing' -or
            $diagnostic -notmatch '(?i)template' -or $diagnostic -notmatch '(?i)manually' -or
            $diagnostic -notmatch '(?i)restart') {
            throw 'Missing-settings stderr lacks the expected path and manual setup/restart guidance.'
        }
        if (Test-Path -LiteralPath $userRoot) { throw 'Missing-settings startup unexpectedly created the user root.' }
        Write-Host 'Missing settings: exit 1, actionable stderr, empty stdout, no settings created.'
    }
    finally { Stop-OwnedProcess $process }

    [void][System.IO.Directory]::CreateDirectory($userRoot)
    $ownsUserRoot = $true
    # Begin with the shipped conservative template; turn off optional enrichment and file sinks.
    $prepared = Get-Content -LiteralPath (Join-Path $installation 'appsettings.json') -Raw | ConvertFrom-Json -AsHashtable
    $prepared.SqlToAi.Databases.ReadWrite = @()
    $prepared.SqlToAi.Databases.ReadWriteAnonymized = @()
    $prepared.SqlToAi.Databases.ReadOnly = @()
    $prepared.SqlToAi.Databases.ReadOnlyAnonymized = @()
    $prepared.SqlToAi.Databases.SchemaOnly = @()
    $prepared.SqlToAi.Servers.primary.Server = 'smoke-host'
    $prepared.SqlToAi.Servers.primary.UserId = $null
    $prepared.SqlToAi.Servers.primary.Password = $null
    $prepared.SqlToAi.Servers.primary.IntegratedSecurity = $false
    $prepared.SqlToAi.Servers.primary.MetadataProvider.Enabled = $false
    $prepared.SqlToAi.Servers.primary.AnonymizationRules.Enabled = $false
    $prepared.SqlToAi.Logging.AppLog.Enabled = $false
    $prepared.SqlToAi.Logging.ErrorLog.Enabled = $false
    $prepared.SmokePreservationProbe = 'Keep unknown settings byte-identical.'
    [System.IO.File]::WriteAllText($settings, ($prepared | ConvertTo-Json -Depth 20))
    $settingsHash = (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash

    $process = Start-PackageProcess $launch.Executable $working $launch.Arguments
    try {
        $stderr = $process.StandardError.ReadToEndAsync()
        Send-McpMessage $process @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{
            protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'SqlToAi-package-smoke'; version = '1.0' }
        } }
        $initialized = Read-McpResponse $process 1
        if ($initialized.protocolVersion -cne '2025-06-18' -or -not $initialized.serverInfo.name -or
            -not $initialized.capabilities.ContainsKey('tools')) {
            throw 'MCP initialization did not negotiate the requested protocol and tools capability.'
        }
        Send-McpMessage $process @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
        Send-McpMessage $process @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} }
        $discovery = Read-McpResponse $process 2
        $expected = @(
            'sql_list_servers',
            'sql_list_databases', 'sql_search_databases', 'sql_get_environment', 'sql_validate_query', 'sql_search_objects',
            'sql_get_schema', 'sql_export_schema', 'sql_get_schema_foreign_keys', 'sql_get_schema_indexes', 'sql_get_schema_constraints',
            'sql_get_trigger_definition', 'sql_get_object_references', 'sql_get_routine_parameters',
            'sql_execute_query', 'sql_export_data', 'sql_execute_file', 'sql_compare_queries', 'sql_measure_performance',
            'sql_benchmark_optimization', 'sql_suggest_indexes'
        )
        if ($discovery.nextCursor -or @($discovery.tools).Count -ne $expected.Count -or
            (Compare-Object ($expected | Sort-Object) (@($discovery.tools.name) | Sort-Object) -CaseSensitive)) {
            throw 'Packaged tools/list does not match the expected 21 SQL tools.'
        }
        $process.StandardInput.Close()
        $tail = $process.StandardOutput.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw 'Packaged process did not exit after stdin closed.' }
        if ($process.ExitCode -ne 0) { throw "Packaged MCP process exited with code $($process.ExitCode)." }
        $reader = [System.IO.StringReader]::new((Receive-ProcessText $tail))
        while ($null -ne ($line = $reader.ReadLine())) {
            $message = Read-ProtocolMessage $line.TrimEnd("`r")
            if ($message.ContainsKey('id') -or -not ($message.method -is [string])) {
                throw 'Unexpected MCP stdout after tool discovery.'
            }
        }
        Write-Verbose "Packaged stderr (separate from protocol stdout): $(Receive-ProcessText $stderr)"
    }
    finally { Stop-OwnedProcess $process }
    if ((Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -ne $settingsHash) {
        throw 'Packaged startup changed existing settings bytes.'
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
        throw 'Archive bytes changed during the smoke check.'
    }
    Write-Host "Package smoke passed: initialization, $($expected.Count) tools, protocol-only stdout, separate stderr, spaces/different CWD, unchanged settings. SHA-256 $hash"
    if ($isBundle) { [pscustomobject]@{ McpbPath = $archive; Sha256 = $hash } }
    else { [pscustomobject]@{ ZipPath = $archive; Sha256 = $hash } }
}
finally {
    if ($ownsUserRoot) {
        if ([System.IO.Path]::GetFullPath($userRoot) -ne (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'SqlToAi')) {
            throw 'Refusing cleanup outside the owned user configuration root.'
        }
        Remove-Item -LiteralPath $userRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $temporary) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\\') + '\'
        if (-not [System.IO.Path]::GetFullPath($temporary).StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing cleanup outside the temporary directory.'
        }
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
