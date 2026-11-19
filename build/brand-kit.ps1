#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks that brand/ and frontend/packages/brand each hold exactly the files of one NCodeGroup/brand release that
    their locks pin.

.DESCRIPTION
    Every file under brand/ but README.md and brand.lock.json is vendored from a release of NCodeGroup/brand
    (ADR-1492), and so is every file under frontend/packages/brand/vendor (ADR-1516). A file a lock does not name is a
    hand-made addition, and a file a lock names must match its SHA-256. The two locks name one release.

    Outputs one string per problem, and nothing when clean:
    - a file under brand/ or frontend/packages/brand/vendor that its brand.lock.json does not name;
    - each file problem `vendor-brand.mjs --check` names for either lock, and a failed check that names none;
    - the two locks naming different releases.

    Needs Node.js on PATH, the major frontend/.node-version pins.

.PARAMETER Kit
    The brand kit's folder. Defaults to brand under the repository root.

.PARAMETER ThemeKit
    The theme kit's folder, the frontend's brand package. Defaults to frontend/packages/brand under the repository root.

.EXAMPLE
    ./build/brand-kit.ps1
#>
[CmdletBinding()]
param(
    [string]$Kit = (Join-Path (Split-Path -Parent $PSScriptRoot) 'brand'),
    [string]$ThemeKit = (Join-Path (Split-Path -Parent $PSScriptRoot) 'frontend' 'packages' 'brand')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Checks one folder against its lock and returns one string per problem. $Label prefixes each path. $Scanned, when set,
# is the subfolder whose files the lock must name (skipped when it is missing); $Own lists the files of $Folder, by
# relative path, that the lock need not name.
function Test-Kit([string]$Folder, [string]$Label, [string]$Scanned, [string[]]$Own) {
    $problems = [System.Collections.Generic.List[string]]::new()

    $lockPath = Join-Path $Folder 'brand.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable

    $scanFolder = if ($Scanned) { Join-Path $Folder $Scanned } else { $Folder }
    $names = @()
    if (Test-Path -LiteralPath $scanFolder -PathType Container) {
        $names = @(
            Get-ChildItem -LiteralPath $scanFolder -File -Recurse |
                ForEach-Object { [IO.Path]::GetRelativePath($Folder, $_.FullName).Replace('\', '/') }
        )
    }
    [Array]::Sort($names, [StringComparer]::Ordinal)

    foreach ($name in $names) {
        if ($name -in $Own) {
            continue
        }

        if (-not $lock.files.Contains($name)) {
            $problems.Add("${Label}/${name}: ${Label}/brand.lock.json does not name it; every vendored file comes from an NCodeGroup/brand release.")
        }
    }

    $output = @(& node (Join-Path $Kit 'vendor-brand.mjs') --check --lock $lockPath 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        # A file problem reads `<path>: <problem>`, and so does the error the script throws (`vendor-brand.mjs: <error>`).
        # Every other line, such as the hint to restore the files, is dropped, so no sentence's wording matters.
        $kept = 0
        foreach ($line in $output) {
            $text = "$line".Trim()
            if ($text -match '^[^\s:]+: ') {
                $problems.Add("${Label}/$text")
                $kept++
            }
        }

        if ($kept -eq 0) {
            $problems.Add("${Label}/brand.lock.json: node brand/vendor-brand.mjs --check exited $exitCode and named no file.")
        }
    }

    return $problems.ToArray()
}

if (-not (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'build/brand-kit.ps1 needs Node.js on PATH, the major frontend/.node-version pins.'
}

$problems = @(
    Test-Kit $Kit 'brand' '' @('README.md', 'brand.lock.json')
    Test-Kit $ThemeKit 'frontend/packages/brand' 'vendor' @()
)

$kitLock = Get-Content -LiteralPath (Join-Path $Kit 'brand.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$themeLock = Get-Content -LiteralPath (Join-Path $ThemeKit 'brand.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$kitVersion = $kitLock.version
$themeVersion = $themeLock.version
if ($kitVersion -ne $themeVersion) {
    $problems += "frontend/packages/brand/brand.lock.json: names NCodeGroup/brand $themeVersion, " +
        "but brand/brand.lock.json names $kitVersion; take one release for both."
}

$problems
