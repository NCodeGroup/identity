#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Keep one open GitHub issue that says a newer NCodeGroup/brand release than the brand locks pin is out.

.DESCRIPTION
    The brand-watch workflow (.github/workflows/brand-watch.yml) runs this script every week (ADR-1516). It reads the
    release that brand/brand.lock.json and frontend/packages/brand/brand.lock.json pin, and fails when the two locks
    name different releases. It lists the releases of NCodeGroup/brand and takes the newest vX.Y.Z release that is
    published, not a draft, not a pre-release, and at least three days old, which is the cooldown that project-template
    ADR-0009 sets. Dependabot cannot see this release, because the locks are not a package manifest.

    When the newest such release is newer than the locks pin, it keeps one open issue labeled brand-update, titled
    "Update NCode Brand to <release>", that says how to take it: it opens the issue when there is none, and otherwise
    edits the open one (the oldest, when there are several), retitling it to the newest release. It creates the label
    when the repository lacks it. A run that would change nothing leaves the issue alone.

    When the locks pin the newest such release, it closes the open brand-update issue with a comment, and does nothing
    when there is none.

    It talks to GitHub through gh, which needs GH_TOKEN (the workflow passes GITHUB_TOKEN, with issues: write) and
    reads the repository from GH_REPO or the clone's remote. Reading the releases needs no token for a public
    repository.

.PARAMETER Root
    The repository root that holds the two locks. Defaults to the parent of the build folder.

.PARAMETER Now
    The time a release's age is measured from. Defaults to the current time.

.PARAMETER DryRun
    Reads GitHub and the locks, and outputs what a run would close, update or open without writing to GitHub.

.EXAMPLE
    ./build/brand-watch.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [DateTimeOffset]$Now = [DateTimeOffset]::UtcNow,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$label = 'brand-update'
$brandRepo = 'NCodeGroup/brand'
$brandLock = 'brand/brand.lock.json'
$themeLock = 'frontend/packages/brand/brand.lock.json'

function Invoke-Gh {
    $output = & gh @args
    if ($LASTEXITCODE) { throw "gh $($args[0]) $($args[1]) exited with code $LASTEXITCODE." }
    $output
}

function Get-LockVersion([string]$RelativePath) {
    $file = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $file)) { throw "$RelativePath does not exist under $Root." }
    $version = (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).version
    if (-not $version) { throw "$RelativePath names no version." }
    "$version"
}

$brandVersion = Get-LockVersion $brandLock
$themeVersion = Get-LockVersion $themeLock
if ($brandVersion -ne $themeVersion) {
    throw "$brandLock names $brandVersion and $themeLock names $themeVersion; the brand locks name different releases."
}
$pinned = [version]$brandVersion

$cutoff = $Now.AddDays(-3)
$releases = @(Invoke-Gh release list --repo $brandRepo --json 'tagName,publishedAt,isDraft,isPrerelease' --limit 100 |
        ConvertFrom-Json)
$candidates = @(foreach ($release in $releases) {
        if ($release.isDraft -or $release.isPrerelease -or $release.tagName -notmatch '^v(\d+\.\d+\.\d+)$') { continue }
        $published = $release.publishedAt
        if ($published -is [datetime]) { $published = [DateTimeOffset]::new($published.ToUniversalTime()) }
        else { $published = [DateTimeOffset]::Parse("$published", [Globalization.CultureInfo]::InvariantCulture) }
        if ($published -le $cutoff) { [pscustomobject]@{ Version = [version]$Matches[1] } }
    })
if (-not $candidates) { 'NCodeGroup/brand has no release at least three days old.'; return }
$newest = ($candidates | Sort-Object Version | Select-Object -Last 1).Version

$open = @(Invoke-Gh issue list --label $label --state open --json 'number,title,body' --limit 100 |
        ConvertFrom-Json | Sort-Object number)
$issue = $open | Select-Object -First 1

if ($newest -le $pinned) {
    $said = "brand locks pin NCode Brand $pinned, the newest release at least three days old"
    if (-not $issue) { "The $said."; return }
    if ($DryRun) { "Would close #$($issue.number)."; return }
    Invoke-Gh issue close $issue.number --comment ("The brand locks pin NCode Brand $pinned, the newest release at " +
        'least three days old, so this issue is done. The brand-watch workflow opens a new one when a newer release ' +
        'is three days old.') | Out-Null
    "Closed #$($issue.number): the $said."
    return
}

$title = "Update NCode Brand to $newest"
$fence = '```'
$body = @(
    "The brand locks pin NCode Brand $pinned. The newest release at least three days old is " +
    "[$newest](https://github.com/$brandRepo/releases/tag/v$newest)."
    ''
    'Take it from the repository root, with the GitHub CLI signed in:'
    ''
    "${fence}powershell"
    "node brand/vendor-brand.mjs --update $newest --lock $brandLock"
    "node brand/vendor-brand.mjs --update $newest --lock $themeLock"
    $fence
    ''
    'Review the diff of the vendored files, copy each changed file of brand/favicon/ into every app''s favicon/ ' +
    'folder, and run ./build/dod.ps1 -Fast and the frontend gate.'
    ''
    'The brand-watch workflow keeps this issue current every week: it retitles it and rewrites this text when a ' +
    'newer release is three days old, and closes it once the locks pin the newest release.'
) -join "`n"
$body = $body.TrimEnd() + "`n"

if ($issue -and $issue.title -eq $title -and "$($issue.body)".Replace("`r`n", "`n").Trim() -eq $body.Trim()) {
    "#$($issue.number) already says: $title."
    return
}
if ($DryRun) {
    if ($issue) { "Would update #$($issue.number): $title." } else { "Would open: $title." }
    return
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) "brand-watch-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
try {
    New-Item -ItemType Directory -Force $scratch | Out-Null
    $bodyFile = Join-Path $scratch 'issue.md'
    [IO.File]::WriteAllText($bodyFile, $body, [Text.UTF8Encoding]::new($false))
    if ($issue) {
        Invoke-Gh issue edit $issue.number --title $title --body-file $bodyFile | Out-Null
        "Updated #$($issue.number): $title."
        return
    }
    $labels = @(Invoke-Gh label list --search $label --json name --limit 100 | ConvertFrom-Json |
            ForEach-Object name)
    if ($label -notin $labels) {
        Invoke-Gh label create $label --description 'A newer NCodeGroup/brand release is out' --color 0033CC | Out-Null
    }
    $url = Invoke-Gh issue create --title $title --label $label --body-file $bodyFile
    "Opened $(@($url)[-1]): $title."
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
