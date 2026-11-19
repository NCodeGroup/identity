#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks build/brand-kit.ps1 against fixture kits and against the repository's own kit.

.DESCRIPTION
    Needs Node.js on PATH, the major frontend/.node-version pins.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$checker = Join-Path $PSScriptRoot '..' 'brand-kit.ps1'
$vendorScript = Join-Path $PSScriptRoot '..' '..' 'brand' 'vendor-brand.mjs'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-brand-kit-$([guid]::NewGuid().ToString('N'))"
$testCount = 0

function Write-FixtureFile([string]$Path, [byte[]]$Bytes) {
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [System.IO.File]::WriteAllBytes($Path, $Bytes)
}

function Get-Sha256([byte[]]$Bytes) {
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

# Builds a repository root under a named fixture, with a brand kit and a theme kit, and returns its path. $Files maps a
# relative path to the file's bytes and $Hashes maps a relative path to the SHA-256 that the lock holds for it. The kit
# holds the repository's vendor-brand.mjs, which the lock names, unless $Script gives the text of another script to
# hold in its place. $ThemeFiles and $ThemeHashes do the same for frontend/packages/brand, whose lock names each path
# as it is and takes its source without the leading vendor/, and $ThemeVersion is the release that lock names.
function New-Fixture(
    [string]$Name,
    [hashtable]$Files,
    [hashtable]$Hashes,
    [string]$Script = '',
    [hashtable]$ThemeFiles = @{},
    [hashtable]$ThemeHashes = @{},
    [string]$ThemeVersion = '0.11.0'
) {
    $root = Join-Path $fixtureRoot $Name
    $kit = Join-Path $root 'brand'
    $null = [System.IO.Directory]::CreateDirectory($kit)
    foreach ($relative in $Files.Keys) {
        Write-FixtureFile (Join-Path $kit $relative) $Files[$relative]
    }

    $vendorBytes = if ($Script) {
        [System.Text.UTF8Encoding]::new($false).GetBytes($Script)
    }
    else {
        [System.IO.File]::ReadAllBytes($vendorScript)
    }
    Write-FixtureFile (Join-Path $kit 'vendor-brand.mjs') $vendorBytes

    $entries = [ordered]@{
        'vendor-brand.mjs' = [ordered]@{
            source = 'vendoring/vendor-brand.mjs'
            sha256 = Get-Sha256 $vendorBytes
        }
    }
    foreach ($relative in $Hashes.Keys) {
        $entries[$relative] = [ordered]@{
            source = "logo/identity/$relative"
            sha256 = $Hashes[$relative]
        }
    }
    $lock = [ordered]@{ version = '0.11.0'; files = $entries }
    $json = ($lock | ConvertTo-Json -Depth 5) + "`n"
    Write-FixtureFile (Join-Path $kit 'brand.lock.json') ([System.Text.UTF8Encoding]::new($false).GetBytes($json))

    $themeKit = Join-Path $root 'frontend' 'packages' 'brand'
    Write-FixtureFile (Join-Path $themeKit 'package.json') ([System.Text.UTF8Encoding]::new($false).GetBytes("{}`n"))
    foreach ($relative in $ThemeFiles.Keys) {
        Write-FixtureFile (Join-Path $themeKit $relative) $ThemeFiles[$relative]
    }

    $themeEntries = [ordered]@{}
    foreach ($relative in $ThemeHashes.Keys) {
        $themeEntries[$relative] = [ordered]@{
            source = $relative -replace '^vendor/', ''
            sha256 = $ThemeHashes[$relative]
        }
    }
    $themeLock = [ordered]@{ version = $ThemeVersion; files = $themeEntries }
    $themeJson = ($themeLock | ConvertTo-Json -Depth 5) + "`n"
    Write-FixtureFile (Join-Path $themeKit 'brand.lock.json') ([System.Text.UTF8Encoding]::new($false).GetBytes($themeJson))
    return $root
}

function Assert-Problems([string]$Name, [string]$Root, [string[]]$ExpectedFragments) {
    $problems = @(& $checker -Kit (Join-Path $Root 'brand') -ThemeKit (Join-Path $Root 'frontend' 'packages' 'brand'))
    if ($problems.Count -ne $ExpectedFragments.Count) {
        throw "Test '$Name' expected $($ExpectedFragments.Count) problem(s), got $($problems.Count): $($problems -join ' | ')"
    }
    for ($i = 0; $i -lt $ExpectedFragments.Count; $i++) {
        if (-not $problems[$i].Contains($ExpectedFragments[$i], [StringComparison]::Ordinal)) {
            throw "Test '$Name' expected problem $i to contain '$($ExpectedFragments[$i])', got '$($problems[$i])'."
        }
    }
    $script:testCount++
    Write-Host "PASS: $Name"
}

$svg = [System.Text.Encoding]::UTF8.GetBytes('<svg/>')
$other = [System.Text.Encoding]::UTF8.GetBytes('<svg id="x"/>')
$hash = Get-Sha256 $svg
$readme = [System.Text.Encoding]::UTF8.GetBytes("# Brand`n")

try {
    $kit = New-Fixture 'matches' @{
        'README.md' = $readme
        'mark/a.svg' = $svg
    } @{
        'mark/a.svg' = $hash
    }
    Assert-Problems 'a kit that matches its lock has no problems' $kit @()

    $kit = New-Fixture 'differs' @{
        'mark/a.svg' = $other
    } @{
        'mark/a.svg' = $hash
    }
    Assert-Problems 'a file with other bytes is reported' $kit @(
        'brand/mark/a.svg: differs from logo/identity/mark/a.svg'
    )

    $kit = New-Fixture 'missing' @{} @{
        'mark/b.svg' = $hash
    }
    Assert-Problems 'a missing file is reported' $kit @(
        'brand/mark/b.svg: missing'
    )

    $kit = New-Fixture 'unnamed' @{
        'mark/a.svg' = $svg
        'extra.png' = $svg
    } @{
        'mark/a.svg' = $hash
    }
    Assert-Problems 'a file the lock does not name is reported' $kit @(
        'brand/extra.png: brand/brand.lock.json does not name it'
    )

    $kit = New-Fixture 'no-hash' @{
        'mark/a.svg' = $svg
    } @{
        'mark/a.svg' = ''
    }
    Assert-Problems 'a lock entry without a SHA-256 is reported' $kit @(
        'brand/mark/a.svg: the lock has no SHA-256 for it'
    )

    $kit = New-Fixture 'leaves' @{
        'mark/a.svg' = $svg
    } @{
        'mark/a.svg' = $hash
        '../x.svg' = $hash
    }
    Assert-Problems 'a lock entry that leaves the kit is reported' $kit @(
        'leaves its folder'
    )

    $kit = New-Fixture 'silent' @{
        'mark/a.svg' = $svg
    } @{
        'mark/a.svg' = $hash
    } -Script "process.exit(3);`n"
    Assert-Problems 'a failed check that names no file is reported' $kit @(
        'brand/brand.lock.json: node brand/vendor-brand.mjs --check exited 3 and named no file.',
        'frontend/packages/brand/brand.lock.json: node brand/vendor-brand.mjs --check exited 3 and named no file.'
    )

    $kit = New-Fixture 'theme-differs' @{} @{} -ThemeFiles @{
        'vendor/mui/a.json' = $other
    } -ThemeHashes @{
        'vendor/mui/a.json' = $hash
    }
    Assert-Problems 'a theme file with other bytes is reported' $kit @(
        'frontend/packages/brand/vendor/mui/a.json: differs from mui/a.json'
    )

    $kit = New-Fixture 'theme-unnamed' @{} @{} -ThemeFiles @{
        'vendor/mui/a.json' = $svg
        'vendor/extra.json' = $svg
    } -ThemeHashes @{
        'vendor/mui/a.json' = $hash
    }
    Assert-Problems 'a theme file the lock does not name is reported' $kit @(
        'frontend/packages/brand/vendor/extra.json: frontend/packages/brand/brand.lock.json does not name it'
    )

    $kit = New-Fixture 'theme-release' @{} @{} -ThemeVersion '0.10.0'
    Assert-Problems 'locks naming different releases are reported' $kit @(
        'names NCodeGroup/brand 0.10.0, but brand/brand.lock.json names 0.11.0'
    )

    Assert-Problems 'the repository kits have no problems' (Join-Path $PSScriptRoot '..' '..') @()

    Write-Host "$testCount brand-kit checks passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
