param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root 'AnchorFeeds\AnchorFeeds.csproj'
$current = ([regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value

if (-not $Version) { $Version = Read-Host "Release version (current $current, e.g. 1.0.1)" }
$Version = $Version.Trim().TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must look like 1.2.3." }

$tag = "v$Version"
if (git tag --list $tag) { throw "Tag $tag already exists." }
$repo = gh repo view --json nameWithOwner --jq .nameWithOwner
if ($LASTEXITCODE -ne 0 -or -not $repo) { throw 'Could not determine the GitHub repository.' }

$previousErrorActionPreference = $ErrorActionPreference
try {
    # Windows PowerShell treats native stderr as an error even when it is redirected.
    $ErrorActionPreference = 'Continue'
    $releaseResponse = @(& gh api --include "repos/$repo/releases/tags/$tag" 2>&1)
    $releaseExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($releaseExitCode -eq 0) { throw "Release $tag already exists on GitHub." }
if (-not ($releaseResponse | Where-Object { "$_" -match '^HTTP/\S+\s+404(?:\s|$)' })) {
    throw "Could not check for release $tag on GitHub:`n$($releaseResponse -join "`n")"
}

Get-Process -Name AnchorFeeds -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith((Join-Path $root 'AnchorFeeds\bin'), [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.Id -Force }

$text = Get-Content $csproj -Raw
$text = $text -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
[System.IO.File]::WriteAllText($csproj, $text)

dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$msi = Join-Path $root 'AnchorFeeds-Setup.msi'
if (-not (Test-Path $msi)) { throw 'AnchorFeeds-Setup.msi was not produced.' }

git add -- AnchorFeeds\AnchorFeeds.csproj AnchorFeeds-Setup.msi
git commit -m "Release $tag`n`nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
if ($LASTEXITCODE -ne 0) { throw 'Commit failed.' }
git push
if ($LASTEXITCODE -ne 0) { throw 'Push failed.' }

gh release create $tag $msi --target (git rev-parse HEAD) --title "AnchorFeeds $tag" --generate-notes
if ($LASTEXITCODE -ne 0) { throw 'Release creation failed.' }

Write-Host "Published $tag"
