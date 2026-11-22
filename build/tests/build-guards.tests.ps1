#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks the guards of Directory.Build.targets (ADR-1197, ADR-1385, ADR-1501, ADR-1519) against fixture projects.

.DESCRIPTION
    Copies the repository's Directory.Build.targets into a temporary folder and writes SDK-less projects under its src/
    and tests/ folders that set the properties and items of each case and import the copy. A project without an SDK
    needs no restore, so the check runs no build of the repository: each case runs one guard's target on one project
    and asserts the exit code and, for a refusal, the ADR the target's message cites.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryTargets = Join-Path $PSScriptRoot '..' '..' 'Directory.Build.targets'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-build-guards-$([guid]::NewGuid().ToString('N'))"
$testCount = 0
$projectCount = 0

# Writes a project that sets the given lines and then imports the copied targets, which sit two folders above it.
function New-GuardProject([string]$Folder, [string[]]$Lines, [string]$ProjectName) {
    $directory = Join-Path $fixtureRoot $Folder 'Guard'
    $null = [System.IO.Directory]::CreateDirectory($directory)
    if ($ProjectName -eq '') {
        $script:projectCount++
        $ProjectName = "Case$($script:projectCount)"
    }
    $path = Join-Path $directory "$ProjectName.proj"
    $body = ($Lines | ForEach-Object { "  $_`n" }) -join ''
    [System.IO.File]::WriteAllText($path, "<Project>`n$body  <Import Project=`"..\..\Directory.Build.targets`" />`n</Project>`n")
    return $path
}

# A child process, so the project's errors stay in it. A wrapped error is joined onto one line.
function Invoke-Guard([string]$Path, [string]$Target) {
    $output = & dotnet msbuild $Path "-t:$Target" -nologo -v:q 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -replace '\r?\n\s*', ' ').Trim() }
}

function Pass([string]$Name) {
    $script:testCount++
    Write-Host "PASS: $Name"
}

function Assert-Passes([string]$Name, [string]$Target, [string]$Folder, [string[]]$Lines, [string]$ProjectName = '') {
    $path = New-GuardProject $Folder $Lines $ProjectName
    $run = Invoke-Guard $path $Target
    if ($run.ExitCode -ne 0) {
        throw "Test '$Name' expected $Target to pass, got exit code $($run.ExitCode): $($run.Output)"
    }
    Pass $Name
}

function Assert-Refuses([string]$Name, [string]$Target, [string]$Folder, [string[]]$Lines, [string]$Adr) {
    $path = New-GuardProject $Folder $Lines ''
    $run = Invoke-Guard $path $Target
    if ($run.ExitCode -eq 0 -or -not $run.Output.Contains($Adr, [StringComparison]::Ordinal)) {
        throw "Test '$Name' expected $Target to refuse with $Adr, got exit code $($run.ExitCode): $($run.Output)"
    }
    Pass $Name
}

try {
    $null = [System.IO.Directory]::CreateDirectory($fixtureRoot)
    Copy-Item -LiteralPath $repositoryTargets -Destination (Join-Path $fixtureRoot 'Directory.Build.targets')

    $openTelemetry = '<ItemGroup><ReferencePath Include="OpenTelemetry.dll" NuGetPackageId="OpenTelemetry" /></ItemGroup>'
    $target = 'RejectOpenTelemetryOutsideItsPackage'
    Assert-Refuses 'a src project that references OpenTelemetry is refused' $target 'src' @($openTelemetry) 'ADR-1197'
    Assert-Passes 'the OpenTelemetry package may reference OpenTelemetry' $target 'src' @($openTelemetry) 'NCode.Identity.AspNetCore.OpenTelemetry'
    Assert-Passes 'a tests project may reference OpenTelemetry' $target 'tests' @('<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>', $openTelemetry)
    Assert-Passes 'a src project without OpenTelemetry passes' $target 'src' @()

    $analyzer = '<ItemGroup><Analyzer Include="x.dll" NuGetPackageId="Microsoft.Extensions.Telemetry.Abstractions" /></ItemGroup>'
    $disable = '<PropertyGroup><DisableMicrosoftExtensionsLoggingSourceGenerator>true</DisableMicrosoftExtensionsLoggingSourceGenerator></PropertyGroup>'
    $target = 'RejectLoggingGeneratorReplacement'
    Assert-Refuses 'a src project with the telemetry analyzer is refused' $target 'src' @($analyzer) 'ADR-1385'
    Assert-Refuses 'a src project that disables the logging generator is refused' $target 'src' @($disable) 'ADR-1385'
    Assert-Passes 'a src project that keeps the logging generator passes' $target 'src' @()
    Assert-Passes 'a tests project may carry the telemetry analyzer' $target 'tests' @('<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>', $analyzer)

    $net10 = '<PropertyGroup><TargetFrameworkIdentifier>.NETCoreApp</TargetFrameworkIdentifier><TargetFrameworkVersion>v10.0</TargetFrameworkVersion></PropertyGroup>'
    $diAbstractions11 = '<ItemGroup><ReferencePath Include="x.dll" NuGetPackageId="Microsoft.Extensions.DependencyInjection.Abstractions" NuGetPackageVersion="11.0.0" /></ItemGroup>'
    $systemTextJson11 = '<ItemGroup><ReferencePath Include="y.dll" NuGetPackageId="System.Text.Json" NuGetPackageVersion="11.0.0" /></ItemGroup>'
    $target = 'RequireRuntimeBand'
    Assert-Refuses 'a src project on a band package of another major is refused' $target 'src' @($net10, $diAbstractions11) 'ADR-1519'
    Assert-Refuses 'a src project on System.Text.Json of another major is refused' $target 'src' @($net10, $systemTextJson11) 'ADR-1519'
    Assert-Passes 'a src project on the band passes' $target 'src' @($net10, '<ItemGroup><ReferencePath Include="x.dll" NuGetPackageId="Microsoft.Extensions.DependencyInjection.Abstractions" NuGetPackageVersion="10.0.15" /></ItemGroup>')
    Assert-Passes 'a dotnet/extensions package keeps its own minor' $target 'src' @($net10, '<ItemGroup><ReferencePath Include="x.dll" NuGetPackageId="Microsoft.Extensions.TimeProvider.Testing" NuGetPackageVersion="10.11.0" /></ItemGroup>')
    Assert-Passes 'a package outside the band may be of another major' $target 'src' @($net10, '<ItemGroup><ReferencePath Include="x.dll" NuGetPackageId="NCode.PropertyBag" NuGetPackageVersion="11.0.0" /></ItemGroup>')
    Assert-Passes 'a reference that is no package passes' $target 'src' @($net10, '<ItemGroup><ReferencePath Include="x.dll" /></ItemGroup>')
    Assert-Passes 'a project that is not .NETCoreApp passes' $target 'src' @('<PropertyGroup><TargetFrameworkIdentifier>.NETStandard</TargetFrameworkIdentifier><TargetFrameworkVersion>v2.0</TargetFrameworkVersion></PropertyGroup>', $diAbstractions11)
    $exceptions = '<PropertyGroup><RuntimeBandExceptions>Microsoft.Extensions.DependencyInjection;Microsoft.Extensions.DependencyInjection.Abstractions</RuntimeBandExceptions></PropertyGroup>'
    Assert-Passes 'a listed exception may be off the band' $target 'src' @($net10, $exceptions, $diAbstractions11)
    Assert-Refuses 'a package the exceptions do not list is refused' $target 'src' @($net10, $exceptions, $diAbstractions11, $systemTextJson11) 'ADR-1519'

    $notPackable = '<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>'
    $target = 'RequirePackingFollowsFolder'
    Assert-Refuses 'a src project that does not pack is refused' $target 'src' @($notPackable) 'ADR-1501'
    Assert-Refuses 'a tests project that leaves packing unset is refused' $target 'tests' @() 'ADR-1501'
    Assert-Passes 'a src project that leaves packing unset passes' $target 'src' @()
    Assert-Passes 'a tests project that does not pack passes' $target 'tests' @($notPackable)
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host "Build guards: $testCount tests passed."
