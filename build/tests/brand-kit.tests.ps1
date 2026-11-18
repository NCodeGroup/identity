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

# Builds a kit folder under a named fixture and returns its path. $Files maps a relative path to the file's bytes and
# $Hashes maps a relative path to the SHA-256 that the lock holds for it. The kit holds the repository's
# vendor-brand.mjs, which the lock names, unless $Script gives the text of another script to hold in its place.
function New-Fixture([string]$Name, [hashtable]$Files, [hashtable]$Hashes, [string]$Script = '') {
    $kit = Join-Path $fixtureRoot $Name 'brand'
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
    return $kit
}

function Assert-Problems([string]$Name, [string]$Kit, [string[]]$ExpectedFragments) {
    $problems = @(& $checker -Kit $Kit)
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
        'brand/brand.lock.json: node brand/vendor-brand.mjs --check exited 3 and named no file.'
    )

    Assert-Problems 'the repository kit has no problems' (Join-Path $PSScriptRoot '..' '..' 'brand') @()

    Write-Host "$testCount brand-kit checks passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
