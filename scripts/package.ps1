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
$registryTemplate = Join-Path $repository 'packaging/server.template.json'
$registrySchema = Join-Path $repository 'packaging/registry-server-2025-12-11.schema.json'
$required = @('SqlToAi.exe', 'appsettings.json', 'demo-profile.json', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'notices/dotnet-9.0.13.txt', 'notices/dotnet-10.0.11.txt')

# Validate source inputs before creating output or invoking the application build.
foreach ($path in @($project, $template, $schema, $registryTemplate, $registrySchema) + @('SETUP.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'packaging/notices/dotnet-9.0.13.txt', 'packaging/notices/dotnet-10.0.11.txt', 'src/SqlToAi/appsettings.json', 'src/SqlToAi/demo-profile.json' | ForEach-Object { Join-Path $repository $_ })) {
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

$registryTemplateJson = Get-Content -LiteralPath $registryTemplate -Raw
$registryConfig = $registryTemplateJson | ConvertFrom-Json -AsHashtable
$repositoryUrl = $registryConfig.repository.url
if ($registryConfig.repository.source -cne 'github' -or
    $repositoryUrl -cnotmatch '^https://github\.com/([A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)/([A-Za-z0-9._-]+)$' -or
    $Matches[2] -in @('.', '..')) {
    throw 'Registry metadata requires a concrete https://github.com/OWNER/REPOSITORY URL and github source in server.template.json.'
}
$serverName = 'io.github.' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2]
$mcpbName = 'SqlToAi-win-x64.mcpb'
$packageUrl = "$repositoryUrl/releases/download/v$version/$mcpbName"

function New-RegistryMetadata([string]$BundleHash) {
    if ($BundleHash -cnotmatch '^[a-f0-9]{64}$') {
        throw 'Registry metadata requires the final MCPB SHA-256.'
    }
    $json = $registryTemplateJson.Replace('__VERSION__', $version).
        Replace('__SERVER_NAME__', $serverName).Replace('__PACKAGE_URL__', $packageUrl).
        Replace('__MCPB_SHA256__', $BundleHash)
    if ($json -match '__[A-Za-z0-9_]+__|\{[^{}\r\n"]+\}') {
        throw 'Registry metadata contains unresolved placeholders.'
    }
    if (-not (Test-Json -Json $json -SchemaFile $registrySchema -ErrorAction Stop)) {
        throw 'Registry metadata does not conform to the pinned official schema.'
    }
    $metadata = $json | ConvertFrom-Json -AsHashtable
    $package = $metadata.packages[0]
    if ($metadata.'$schema' -cne 'https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json' -or
        $metadata.name -cne $serverName -or $metadata.version -cne $version -or
        $metadata.packages.Count -ne 1 -or $metadata.ContainsKey('remotes') -or
        $package.registryType -cne 'mcpb' -or $package.identifier -cne $packageUrl -or
        $package.version -cne $version -or $package.fileSha256 -cne $BundleHash -or
        $package.transport.type -cne 'stdio' -or $package.transport.Keys.Count -ne 1) {
        throw 'Registry metadata must retain the derived GitHub identity, versioned MCPB URL, final hash and stdio transport.'
    }
    return $json
}

# Reject invalid templates before publishing; replace the provisional hash only after bundling.
$null = New-RegistryMetadata ('0' * 64)

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

# The source setup guide keeps useful checkout-relative links. Resolve those links
# in the extracted package README to the matching versioned repository sources.
$packageReadme = Join-Path $publish 'README.md'
$readmeText = Get-Content -LiteralPath $packageReadme -Raw
$versionedDocs = "https://github.com/RalfHuesing/SqlToAi/blob/v$version/"
foreach ($relativePath in @('docs/configuration.md', 'docs/security.md', 'docs/tools.md', 'docs/development.md#support-and-contributions', 'docs/development.md#release', 'SECURITY.md')) {
    $readmeText = $readmeText.Replace("]($relativePath)", "]($versionedDocs$relativePath)")
}
if ($readmeText -match '\]\((?:docs/|SECURITY\.md)') {
    throw 'Packaged README contains unresolved checkout-relative documentation links.'
}
[System.IO.File]::WriteAllText($packageReadme, $readmeText)

$zip = Join-Path $output 'SqlToAi-win-x64.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)

# Copy the finished ZIP and add only metadata: every application entry retains its bytes.
$manifestPath = Join-Path $output 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, $manifestJson)
$mcpb = Join-Path $output $mcpbName
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
$serverJsonPath = Join-Path $output 'server.json'
[System.IO.File]::WriteAllText($serverJsonPath, (New-RegistryMetadata $mcpbHash))
Write-Host "Created $zip (SHA-256 $hash)."
Write-Host "Created $mcpb (SHA-256 $mcpbHash)."
[pscustomobject]@{
    ZipPath = $zip; Sha256 = $hash; ChecksumPath = $checksum
    McpbPath = $mcpb; McpbSha256 = $mcpbHash; McpbChecksumPath = $mcpbChecksum
    ServerJsonPath = $serverJsonPath
}
