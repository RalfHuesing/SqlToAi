#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Package output must not exist: $output"
}
[void][System.IO.Directory]::CreateDirectory($output)
$publish = Join-Path $output 'publish'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/SqlToAi/SqlToAi.csproj'

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $publish | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Package publish failed with exit code $LASTEXITCODE. Partial output remains at $output."
}
foreach ($name in 'SqlToAi.exe', 'appsettings.json', 'demo-profile.json', 'README.md', 'LICENSE') {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
        throw "Published package is missing $name."
    }
}

$zip = Join-Path $output 'SqlToAi-win-x64.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = "$zip.sha256"
[System.IO.File]::WriteAllText($checksum, "$hash  $([System.IO.Path]::GetFileName($zip))`n")
Write-Host "Created $zip (SHA-256 $hash)."
[pscustomobject]@{ ZipPath = $zip; Sha256 = $hash; ChecksumPath = $checksum }
