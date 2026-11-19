#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Runs the end-to-end suites — the tests/*.EndToEnd.Tests projects — the single entrypoint CI and developers both run.

.DESCRIPTION
    Builds each end-to-end suite, installs the Playwright browsers it drives (Chromium, Firefox and WebKit) and runs
    its tests. Before it builds a suite whose project file sets EndToEndFrontendPackages, it installs the pnpm
    workspace and builds those frontend packages with turbo, so the suite serves bundles built from the tree; that
    needs pnpm on PATH (./build/frontend-install.ps1 installs it). The suites start containers, so a running Docker
    engine is required (Docker Desktop on Windows and macOS). The first run downloads the browsers and the container
    images, so it can take several minutes. On Linux in GitHub Actions the browsers are installed with their
    operating-system dependencies.

    Every run writes its JUnit reports to artifacts/e2e-results (cleared first, so a report never mixes in an earlier
    run), prints a test digest, and ends with 'End-to-end: PASSED' when every suite passes. build/dod.ps1 never runs
    these suites.

.PARAMETER Configuration
    The build configuration. Defaults to Release.

.PARAMETER Filter
    A `dotnet test --filter` expression; runs only the matching tests (for example
    `FullyQualifiedName~SessionCookieTests`). Every suite that runs must match at least one test, so name the suite
    with -Suite.

.PARAMETER Suite
    The names of the end-to-end suites to run, such as NCode.Identity.Frontend.EndToEnd.Tests (a project's file name
    without .csproj). With -Affected, only those of them the changes reach run. An unknown name fails before anything
    is built.

.PARAMETER Affected
    Runs only the end-to-end suites that build/affected-tests.ps1 selects for the changes since the merge base of HEAD
    and -Base: every suite when a global build input changed, and none when no suite is affected. With none selected,
    it prints 'End-to-end: PASSED' without building or checking for Docker.

.PARAMETER Base
    The commit-ish whose merge base with HEAD -Affected measures the changes from. Defaults to origin/main.

.EXAMPLE
    ./build/e2e.ps1

.EXAMPLE
    ./build/e2e.ps1 -Affected

.EXAMPLE
    ./build/e2e.ps1 -Suite NCode.Identity.Bff.EndToEnd.Tests -Filter 'FullyQualifiedName~SessionCookieTests'
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Filter,
    [string[]]$Suite,
    [switch]$Affected,
    [string]$Base = 'origin/main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'artifacts/e2e-results'

Push-Location $root
try {
    # 1. Select the projects.
    $tiers = & (Join-Path $PSScriptRoot 'test-projects.ps1') -Root $root
    if (-not $Affected -and @($tiers.EndToEnd).Count -eq 0) {
        throw 'No end-to-end suite exists under tests/.'
    }
    if ($Suite) {
        $suiteNames = @($tiers.EndToEnd | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })
        foreach ($name in $Suite) {
            if ($suiteNames -notcontains $name) {
                throw "No end-to-end suite is named '$name'."
            }
        }
    }
    if (-not $Affected) {
        $projects = @($tiers.EndToEnd)
    }
    else {
        $selection = & (Join-Path $PSScriptRoot 'affected-tests.ps1') -Root $root -Base $Base
        foreach ($reason in $selection.Reasons) {
            Write-Host $reason
        }
        # An if statement unrolls its output, so the array subexpression wraps it: an empty selection stays an array.
        $projects = @(if ($selection.All) { $tiers.EndToEnd } else { $selection.EndToEndProjects })
    }
    if ($Suite) {
        $projects = @($projects | Where-Object { $Suite -contains [System.IO.Path]::GetFileNameWithoutExtension($_) })
    }
    if ($Affected -and $projects.Count -eq 0) {
        Write-Host 'No end-to-end suite is affected; skipping.'
        Write-Host "`nEnd-to-end: PASSED" -ForegroundColor Green
        return
    }

    # 2. The suites start containers.
    $dockerAvailable = $false
    try {
        docker info --format '{{.ServerVersion}}' *> $null
        $dockerAvailable = $LASTEXITCODE -eq 0
    }
    catch {
        $dockerAvailable = $false
    }
    if (-not $dockerAvailable) {
        throw 'The end-to-end suites need a running Docker engine (Docker Desktop on Windows and macOS).'
    }

    # 3. Build the frontend packages the suites serve.
    $frontendPackages = @(@(foreach ($project in $projects) { if ($tiers.FrontendPackages.Contains($project)) { $tiers.FrontendPackages[$project] } }) | Select-Object -Unique)
    if ($frontendPackages.Count -gt 0) {
        $pnpmAvailable = $false
        try {
            pnpm --version *> $null
            $pnpmAvailable = $LASTEXITCODE -eq 0
        }
        catch {
            $pnpmAvailable = $false
        }
        if (-not $pnpmAvailable) {
            throw 'The end-to-end suites that serve built frontend bundles need pnpm on PATH; ./build/frontend-install.ps1 installs the version frontend/package.json pins.'
        }

        $frontend = Join-Path $root 'frontend'
        pnpm --dir $frontend install --frozen-lockfile --config.confirm-modules-purge=false
        if ($LASTEXITCODE -ne 0) {
            throw "Installing the frontend workspace failed with exit code $LASTEXITCODE."
        }

        pnpm --dir $frontend exec turbo run build @(foreach ($package in $frontendPackages) { "--filter=$package" })
        if ($LASTEXITCODE -ne 0) {
            throw "Building the frontend packages $($frontendPackages -join ', ') failed with exit code $LASTEXITCODE."
        }
    }

    # 4. Clear the earlier reports.
    if (Test-Path -LiteralPath $results) {
        Remove-Item -LiteralPath $results -Recurse -Force
    }

    # 5. Build, install the browsers and test each suite.
    $testsFailed = $false
    foreach ($relativeProject in $projects) {
        $project = Join-Path $root $relativeProject
        $projectDir = Split-Path -Parent $project

        Write-Host "=== $relativeProject ===" -ForegroundColor Cyan

        dotnet build $project --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Building $relativeProject failed with exit code $LASTEXITCODE."
        }

        $targetFramework = (dotnet msbuild $project -getProperty:TargetFramework | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetFramework)) {
            throw "Reading the TargetFramework property of $relativeProject failed."
        }

        $playwrightScript = Join-Path $projectDir "bin/$Configuration/$targetFramework/playwright.ps1"
        # Only a suite that drives a browser has the script; the others need no browsers.
        if (Test-Path -LiteralPath $playwrightScript) {
            $browserArguments = @('install', 'chromium', 'firefox', 'webkit')
            if ($IsLinux -and $env:GITHUB_ACTIONS -eq 'true') {
                $browserArguments += '--with-deps'
            }
            & pwsh -NoProfile -File $playwrightScript @browserArguments
            if ($LASTEXITCODE -ne 0) {
                throw "Installing the Playwright browsers for $relativeProject failed with exit code $LASTEXITCODE."
            }
        }

        $junitLogger = "junit;LogFilePath=$(Join-Path $results '{assembly}-{framework}.junit.xml')"
        $testArguments = @(
            $project,
            '--configuration', $Configuration,
            '--no-build',
            '--results-directory', $results,
            "--logger:$junitLogger"
        )
        if ($Filter) {
            $testArguments += '--filter', $Filter
        }
        $testArguments += '--', 'RunConfiguration.TreatNoTestsAsError=true'
        dotnet test @testArguments

        # Keep the test exit code until the report is written: failures are exactly when the digest matters.
        if ($LASTEXITCODE -ne 0) {
            $testsFailed = $true
        }
    }

    # 6. Report whatever ran, then let the test runs decide the verdict.
    & (Join-Path $PSScriptRoot 'test-report.ps1') -Path 'artifacts/e2e-results/*.junit.xml' -Title 'End-to-end tests' -Root $root
    if ($testsFailed) {
        throw 'The end-to-end tests failed.'
    }

    # 7. Done.
    Write-Host "`nEnd-to-end: PASSED" -ForegroundColor Green
}
finally {
    Pop-Location
}
