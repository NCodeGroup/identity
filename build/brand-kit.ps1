#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks that brand/ holds exactly the files of an NCodeGroup/brand release that brand/brand.lock.json pins.

.DESCRIPTION
    Every file under brand/ but README.md and brand.lock.json is vendored from a release of NCodeGroup/brand
    (ADR-1492). A file the lock does not name is a hand-made addition, and a file the lock names must match its
    SHA-256.

    Outputs one string per problem, and nothing when clean:
    - a file under brand/ that brand/brand.lock.json does not name;
    - each line `node brand/vendor-brand.mjs --check` prints when it fails.

    Needs Node.js on PATH, the major frontend/.node-version pins.

.PARAMETER Kit
    The brand kit's folder. Defaults to brand under the repository root.

.EXAMPLE
    ./build/brand-kit.ps1
#>
[CmdletBinding()]
param(
    [string]$Kit = (Join-Path (Split-Path -Parent $PSScriptRoot) 'brand')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$problems = [System.Collections.Generic.List[string]]::new()

$lockPath = Join-Path $Kit 'brand.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable

$names = @(
    Get-ChildItem -LiteralPath $Kit -File -Recurse |
        ForEach-Object { [IO.Path]::GetRelativePath($Kit, $_.FullName).Replace('\', '/') }
)
[Array]::Sort($names, [StringComparer]::Ordinal)

foreach ($name in $names) {
    if ($name -eq 'README.md' -or $name -eq 'brand.lock.json') {
        continue
    }

    if (-not $lock.files.Contains($name)) {
        $problems.Add("brand/${name}: brand/brand.lock.json does not name it; every file here but README.md comes from an NCodeGroup/brand release.")
    }
}

if (-not (Get-Command node -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'build/brand-kit.ps1 needs Node.js on PATH, the major frontend/.node-version pins.'
}

$output = @(& node (Join-Path $Kit 'vendor-brand.mjs') --check --lock $lockPath 2>&1)
if ($LASTEXITCODE -ne 0) {
    foreach ($line in $output) {
        $text = "$line".Trim()
        if ($text -and -not $text.StartsWith('Run "node vendor-brand.mjs')) {
            $problems.Add("brand/$text")
        }
    }
}

$problems
