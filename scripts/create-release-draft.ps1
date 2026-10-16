#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$SourceCommit,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$ExpectedSha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$ExpectedMcpbSha256
)
$ErrorActionPreference = 'Stop'

$source = & (Join-Path $PSScriptRoot 'release-source.ps1') -Tag $Tag -SourceCommit $SourceCommit
$directory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDirectory)
$zip = Join-Path $directory 'SqlToAi-win-x64.zip'
$checksum = "$zip.sha256"
$mcpb = Join-Path $directory 'SqlToAi-win-x64.mcpb'
$mcpbChecksum = "$mcpb.sha256"
$notes = Join-Path $directory 'release-notes.md'
$inputs = @($zip, $checksum, $mcpb, $mcpbChecksum, $notes)
foreach ($path in $inputs) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing draft input: $path" }
}
$locks = [System.Collections.Generic.List[System.IO.FileStream]]::new()
try {
    # Keep checked inputs readable by gh, but deny replacement/writes until upload ends.
    foreach ($path in $inputs) {
        $locks.Add([System.IO.File]::Open($path, 'Open', 'Read', 'Read'))
    }
    foreach ($package in @(
        @{ Path = $zip; Hash = $ExpectedSha256 },
        @{ Path = $mcpb; Hash = $ExpectedMcpbSha256 }
    )) {
        $name = [System.IO.Path]::GetFileName($package.Path)
        $hash = (Get-FileHash -LiteralPath $package.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -cne $package.Hash -or [System.IO.File]::ReadAllText("$($package.Path).sha256") -cne "$hash  $name`n") {
            throw "Downloaded $name/checksum does not match the smoke-checked SHA-256."
        }
    }
    $body = [System.IO.File]::ReadAllText($notes)
    foreach ($line in @("Source commit: $($source.Commit)", "Version: $($source.Version) (tag $($source.Tag))",
        "ZIP SHA-256: $ExpectedSha256", "MCPB SHA-256: $ExpectedMcpbSha256")) {
        if ([regex]::Matches($body, "(?m)^$([regex]::Escape($line))\r?$").Count -ne 1) {
            throw 'Release notes do not uniquely identify the checked source/version/hashes.'
        }
    }
    $bundle = [System.IO.Compression.ZipFile]::OpenRead($mcpb)
    try {
        $manifests = @($bundle.Entries | Where-Object FullName -CEQ 'manifest.json')
        if ($manifests.Count -ne 1) { throw 'Checked MCPB must contain one manifest.' }
        $reader = [System.IO.StreamReader]::new($manifests[0].Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.version -cne $source.Version) { throw 'MCPB version differs from release source.' }
    }
    finally { $bundle.Dispose() }

    # Paginate rather than treating a failed lookup or an incomplete release list as absence.
    $existing = gh api --paginate "repos/$Repository/releases?per_page=100" `
        --jq ".[] | select(.tag_name == `"$Tag`") | if .draft then `"draft`" else `"public`" end"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing releases; no draft will be created.' }
    if ($existing) { throw "Release $Tag already exists ($existing). Refusing to replace assets or reuse its review." }

    # gh release create fails if this tag acquires a release concurrently; it does not update it.
    gh release create $Tag --repo $Repository --draft --verify-tag --target $source.Commit `
        --title "SqlToAi $($source.Version)" --notes-file $notes $zip $checksum $mcpb $mcpbChecksum
    if ($LASTEXITCODE -ne 0) {
        throw 'Draft creation/upload failed. Inspect any partial draft before retrying; reruns refuse existing releases.'
    }
    Write-Host "Created draft $Tag from $($source.Commit) with checked ZIP SHA-256 $ExpectedSha256 and MCPB SHA-256 $ExpectedMcpbSha256. Maintainer review is pending."
}
finally {
    foreach ($stream in $locks) { $stream.Dispose() }
}
