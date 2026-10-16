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
    [string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'

$source = & (Join-Path $PSScriptRoot 'release-source.ps1') -Tag $Tag -SourceCommit $SourceCommit
$directory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDirectory)
$zip = Join-Path $directory 'SqlToAi-win-x64.zip'
$checksum = "$zip.sha256"
$notes = Join-Path $directory 'release-notes.md'
foreach ($path in $zip, $checksum, $notes) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing draft input: $path" }
}
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $ExpectedSha256 -or
    [System.IO.File]::ReadAllText($checksum) -cne "$hash  SqlToAi-win-x64.zip`n") {
    throw 'Downloaded ZIP/checksum does not match the smoke-checked SHA-256.'
}

# Paginate rather than treating a failed lookup or an incomplete release list as absence.
$existing = gh api --paginate "repos/$Repository/releases?per_page=100" `
    --jq ".[] | select(.tag_name == `"$Tag`") | if .draft then `"draft`" else `"public`" end"
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing releases; no draft will be created.' }
if ($existing) { throw "Release $Tag already exists ($existing). Refusing to replace assets or reuse its review." }

# gh release create fails if this tag acquires a release concurrently; it does not update it.
gh release create $Tag --repo $Repository --draft --verify-tag --target $source.Commit `
    --title "SqlToAi $($source.Version)" --notes-file $notes $zip $checksum
if ($LASTEXITCODE -ne 0) {
    throw 'Draft creation/upload failed. Inspect any partial draft before retrying; reruns refuse existing releases.'
}
Write-Host "Created draft $Tag from $($source.Commit) with checked ZIP SHA-256 $hash. Maintainer review is pending."
