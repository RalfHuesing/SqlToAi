#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/SqlToAi/SqlToAi.csproj'
$template = Join-Path $repository 'packaging/manifest.json'
$schema = Join-Path $repository 'packaging/mcpb-manifest-v0.3.schema.json'
$required = @('SqlToAi.exe', 'appsettings.json', 'demo-profile.json', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'notices/dotnet-9.0.13.txt', 'notices/dotnet-10.0.11.txt')

# Validate source inputs before creating output or invoking the application build.
foreach ($path in @($project, $template, $schema) + @('SETUP.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'packaging/notices/dotnet-9.0.13.txt', 'packaging/notices/dotnet-10.0.11.txt', 'src/SqlToAi/appsettings.json', 'src/SqlToAi/demo-profile.json' | ForEach-Object { Join-Path $repository $_ })) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package input is missing: $path"
    }
}
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versions = @($projectXml.SelectNodes('/Project/PropertyGroup/Version'))
if ($versions.Count -ne 1 -or $versions[0].InnerText -cnotmatch '^\d+\.\d+\.\d+$') {
    throw 'Packaging requires one explicit X.Y.Z project Version.'
}
$version = $versions[0].InnerText
$manifestJson = (Get-Content -LiteralPath $template -Raw).Replace('__VERSION__', $version)
if (-not (Test-Json -Json $manifestJson -SchemaFile $schema -ErrorAction Stop)) {
    throw 'MCPB manifest does not conform to the pinned official schema.'
}
$manifest = $manifestJson | ConvertFrom-Json -AsHashtable
if ($manifest.version -cne $version -or $manifest.manifest_version -cne '0.3' -or
    $manifest.server.type -cne 'binary' -or $manifest.server.entry_point -cne 'SqlToAi.exe' -or
    $manifest.server.mcp_config.command -cne '${__dirname}/SqlToAi.exe' -or
    -not $manifest.server.mcp_config.ContainsKey('args') -or $manifest.server.mcp_config.args.Count -ne 0 -or
    $manifest.server.mcp_config.Keys.Count -ne 2 -or $manifest.ContainsKey('user_config') -or
    $manifest.compatibility.Keys.Count -ne 1 -or $manifest.compatibility.platforms.Count -ne 1 -or
    $manifest.compatibility.platforms[0] -cne 'win32' -or $manifest.description -notmatch '\bx64\b' -or
    $manifestJson -match '__VERSION__') {
    throw 'MCPB manifest must retain the Windows x64 binary launch and manual-settings contract.'
}

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Package output must not exist: $output"
}
[void][System.IO.Directory]::CreateDirectory($output)
$publish = Join-Path $output 'publish'

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $publish | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Package publish failed with exit code $LASTEXITCODE. Partial output remains at $output."
}
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
        throw "Published package is missing $name."
    }
}
if (Test-Path -LiteralPath (Join-Path $publish 'manifest.json')) {
    throw 'Published application payload must not contain bundle metadata.'
}

$zip = Join-Path $output 'SqlToAi-win-x64.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)

# Copy the finished ZIP and add only metadata: every application entry retains its bytes.
$manifestPath = Join-Path $output 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, $manifestJson)
$mcpb = Join-Path $output 'SqlToAi-win-x64.mcpb'
[System.IO.File]::Copy($zip, $mcpb)
$bundle = [System.IO.Compression.ZipFile]::Open($mcpb, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $manifestPath, 'manifest.json')
} finally {
    $bundle.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = "$zip.sha256"
[System.IO.File]::WriteAllText($checksum, "$hash  $([System.IO.Path]::GetFileName($zip))`n")
$mcpbHash = (Get-FileHash -LiteralPath $mcpb -Algorithm SHA256).Hash.ToLowerInvariant()
$mcpbChecksum = "$mcpb.sha256"
[System.IO.File]::WriteAllText($mcpbChecksum, "$mcpbHash  $([System.IO.Path]::GetFileName($mcpb))`n")
Write-Host "Created $zip (SHA-256 $hash)."
Write-Host "Created $mcpb (SHA-256 $mcpbHash)."
[pscustomobject]@{
    ZipPath = $zip; Sha256 = $hash; ChecksumPath = $checksum
    McpbPath = $mcpb; McpbSha256 = $mcpbHash; McpbChecksumPath = $mcpbChecksum
}
