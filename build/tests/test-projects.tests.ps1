#!/usr/bin/env pwsh
#requires -version 7
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lister = Join-Path $PSScriptRoot '..' 'test-projects.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-test-projects-$([guid]::NewGuid().ToString('N'))"
$testCount = 0

function Write-FixtureFile([string]$Path, [string]$Text) {
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Write-Project([string]$Root, [string]$Path) {
    Write-FixtureFile (Join-Path $Root $Path) "<Project Sdk=`"Microsoft.NET.Sdk`"></Project>`n"
}

function Write-FrontendSuite([string]$Root, [string]$Path, [string]$Packages) {
    Write-FixtureFile (Join-Path $Root $Path) "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><EndToEndFrontendPackages>$Packages</EndToEndFrontendPackages></PropertyGroup></Project>`n"
}

function Assert-Tiers([string]$Name, [object]$Result, [string]$Tests, [string]$EndToEnd) {
    $actualTests = $Result.Tests -join ','
    $actualEndToEnd = $Result.EndToEnd -join ','
    if ($actualTests -ne $Tests -or $actualEndToEnd -ne $EndToEnd) {
        throw "Test '$Name' expected Tests='$Tests' EndToEnd='$EndToEnd', got Tests='$actualTests' EndToEnd='$actualEndToEnd'."
    }
    if ($Result.FrontendPackages.Count -ne 0) {
        throw "Test '$Name' expected no FrontendPackages, got $($Result.FrontendPackages.Count)."
    }
    $script:testCount++
    Write-Host "PASS: $Name"
}

try {
    $tiered = Join-Path $fixtureRoot 'tiered'
    Write-Project $tiered 'src/A/A.csproj'
    Write-Project $tiered 'tests/A.Tests/A.Tests.csproj'
    Write-Project $tiered 'tests/A.EndToEnd.Tests/A.EndToEnd.Tests.csproj'
    Write-Project $tiered 'tests/A.EndToEnd.App/A.EndToEnd.App.csproj'
    Write-Project $tiered 'tests/A.Tests/obj/X/X.csproj'
    $result = & $lister -Root $tiered
    Assert-Tiers 'projects are split by tier, and support projects and build output are skipped' $result `
        'tests/A.Tests/A.Tests.csproj' 'tests/A.EndToEnd.Tests/A.EndToEnd.Tests.csproj'

    $none = Join-Path $fixtureRoot 'none'
    Write-Project $none 'src/A/A.csproj'
    $result = & $lister -Root $none
    Assert-Tiers 'a repository without tests lists nothing' $result '' ''

    $frontend = Join-Path $fixtureRoot 'frontend'
    Write-FrontendSuite $frontend 'tests/F.EndToEnd.Tests/F.EndToEnd.Tests.csproj' '@x/a; @x/b;'
    Write-Project $frontend 'tests/G.EndToEnd.Tests/G.EndToEnd.Tests.csproj'
    Write-FrontendSuite $frontend 'tests/H.Tests/H.Tests.csproj' '@x/c'
    $result = & $lister -Root $frontend
    $keys = @($result.FrontendPackages.Keys) -join ','
    $names = @($result.FrontendPackages['tests/F.EndToEnd.Tests/F.EndToEnd.Tests.csproj']) -join ','
    if ($keys -ne 'tests/F.EndToEnd.Tests/F.EndToEnd.Tests.csproj' -or $names -ne '@x/a,@x/b') {
        throw "Test 'an end-to-end project names the frontend packages it serves' expected F with '@x/a,@x/b', got '$keys' with '$names'."
    }
    $testCount++
    Write-Host "PASS: an end-to-end project names the frontend packages it serves"

    Write-Host "$testCount test-projects checks passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
