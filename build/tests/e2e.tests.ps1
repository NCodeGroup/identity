#!/usr/bin/env pwsh
#requires -version 7
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$buildFolder = Join-Path $PSScriptRoot '..'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-e2e-$([guid]::NewGuid().ToString('N'))"
$testCount = 0

function Write-FixtureFile([string]$Path, [string]$Text) {
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Write-Project([string]$Root, [string]$Path, [string[]]$References) {
    $items = ($References | ForEach-Object { "<ProjectReference Include=`"$_`" />" }) -join ''
    Write-FixtureFile (Join-Path $Root $Path) "<Project Sdk=`"Microsoft.NET.Sdk`"><ItemGroup>$items</ItemGroup></Project>`n"
}

function Invoke-Git([string]$Root, [string[]]$Arguments) {
    & git -C $Root @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$Root'."
    }
}

# A committed repository holding the scripts e2e.ps1 runs, so the scripts themselves are not a change.
function New-Fixture([string]$Name, [bool]$WithEndToEnd, [bool]$WithFrontendSuite) {
    $root = Join-Path $fixtureRoot $Name
    foreach ($script in 'e2e.ps1', 'test-projects.ps1', 'affected-tests.ps1') {
        Write-FixtureFile (Join-Path $root 'build' $script) (Get-Content -LiteralPath (Join-Path $buildFolder $script) -Raw)
    }
    Write-Project $root 'src/A/A.csproj' @()
    Write-Project $root 'tests/A.Tests/A.Tests.csproj' @('..\..\src\A\A.csproj')
    if ($WithEndToEnd) {
        Write-Project $root 'tests/A.EndToEnd.Tests/A.EndToEnd.Tests.csproj' @('..\..\src\A\A.csproj')
    }
    if ($WithFrontendSuite) {
        Write-FixtureFile (Join-Path $root 'tests/F.EndToEnd.Tests/F.EndToEnd.Tests.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><EndToEndFrontendPackages>@x/app;@x/other</EndToEndFrontendPackages></PropertyGroup><ItemGroup><ProjectReference Include=`"..\..\src\A\A.csproj`" /></ItemGroup></Project>`n"
    }
    Invoke-Git $root @('init', '-q')
    Invoke-Git $root @('config', 'user.name', 'E2E Tests')
    Invoke-Git $root @('config', 'user.email', 'e2e-tests@example.invalid')
    Invoke-Git $root @('config', 'core.autocrlf', 'false')
    Invoke-Git $root @('config', 'commit.gpgsign', 'false')
    Invoke-Git $root @('add', '-A')
    Invoke-Git $root @('commit', '-q', '-m', 'fixture')
    return $root
}

# A child process, so the script's strict mode, return and throw stay in it. A -Stubs folder goes first on its PATH.
function Invoke-EndToEnd([string]$Root, [string[]]$Arguments, [string]$Stubs) {
    $path = $env:PATH
    try {
        if ($Stubs) {
            $env:PATH = "$Stubs$([System.IO.Path]::PathSeparator)$path"
        }
        $output = & pwsh -NoProfile -File (Join-Path $Root 'build' 'e2e.ps1') @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally {
        $env:PATH = $path
    }
}

# A folder holding one stub command per entry of Commands, whose values hold the Windows and Posix text of the stub.
function New-Stubs([string]$Name, [hashtable]$Commands) {
    $folder = Join-Path $fixtureRoot "stubs-$Name"
    foreach ($command in $Commands.Keys) {
        if ($IsWindows) {
            Write-FixtureFile (Join-Path $folder "$command.cmd") $Commands[$command].Windows
        }
        else {
            $stub = Join-Path $folder $command
            Write-FixtureFile $stub $Commands[$command].Posix
            & chmod +x $stub
        }
    }
    return $folder
}

$workingStub = @{ Windows = "@exit /b 0`r`n"; Posix = "#!/bin/sh`nexit 0`n" }
$failingStub = @{ Windows = "@exit /b 1`r`n"; Posix = "#!/bin/sh`nexit 1`n" }
$recordingPnpm = @{
    Windows = "@echo pnpm %*`r`n@echo %* | findstr /c:`"turbo`" >nul && exit /b 1`r`n@exit /b 0`r`n"
    Posix   = "#!/bin/sh`necho `"pnpm `$*`"`ncase `"`$*`" in *turbo*) exit 1;; esac`nexit 0`n"
}

# A folder holding a docker command that always fails, as an engine that is not running does.
function New-FailingDocker {
    return New-Stubs 'docker' @{ docker = $failingStub }
}

function Assert-Run([string]$Name, [object]$Run, [bool]$Succeeds, [string[]]$Expected) {
    $succeeded = $Run.ExitCode -eq 0
    $missing = @($Expected | Where-Object { -not $Run.Output.Contains($_) })
    if ($succeeded -ne $Succeeds -or $missing.Count -gt 0) {
        throw "Test '$Name' expected success=$Succeeds and output containing '$($Expected -join "', '")', got exit code $($Run.ExitCode):$([Environment]::NewLine)$($Run.Output)"
    }
    $script:testCount++
    Write-Host "PASS: $Name"
}

try {
    $fixture = New-Fixture 'affected' $true $false
    $run = Invoke-EndToEnd $fixture @('-Affected', '-Base', 'HEAD')
    Assert-Run 'no change affects no suite and passes' $run $true @(
        'No end-to-end suite is affected; skipping.',
        'End-to-end: PASSED'
    )

    Write-FixtureFile (Join-Path $fixture '.github/workflows/ci.yml') "name: CI`n"
    $run = Invoke-EndToEnd $fixture @('-Affected', '-Base', 'HEAD')
    Assert-Run 'a change under no project affects no suite and passes' $run $true @(
        '.github/workflows/ci.yml -> nothing (no project)',
        'No end-to-end suite is affected; skipping.',
        'End-to-end: PASSED'
    )

    $fixture = New-Fixture 'none' $false $false
    $run = Invoke-EndToEnd $fixture @()
    Assert-Run 'a repository without a suite fails before it needs Docker' $run $false @(
        'No end-to-end suite exists under tests/.'
    )

    $docker = New-FailingDocker
    $dockerMissing = 'The end-to-end suites need a running Docker engine'

    $fixture = New-Fixture 'selected' $true $false
    Write-FixtureFile (Join-Path $fixture 'src/A/X.cs') "class X { }`n"
    $run = Invoke-EndToEnd $fixture @('-Affected', '-Base', 'HEAD') $docker
    Assert-Run 'a change that reaches a suite requires Docker' $run $false @(
        'src/A/X.cs -> src/A/A.csproj',
        $dockerMissing
    )

    $run = Invoke-EndToEnd $fixture @('-Suite', 'Missing.EndToEnd.Tests') $docker
    Assert-Run 'an unknown suite name fails before it needs Docker' $run $false @(
        "No end-to-end suite is named 'Missing.EndToEnd.Tests'."
    )
    if ($run.Output.Contains($dockerMissing)) {
        throw "Test 'an unknown suite name fails before it needs Docker' expected no Docker message, got:$([Environment]::NewLine)$($run.Output)"
    }

    $fixture = New-Fixture 'global' $true $false
    Write-FixtureFile (Join-Path $fixture 'build/x.ps1') "exit 0`n"
    $run = Invoke-EndToEnd $fixture @('-Affected', '-Base', 'HEAD') $docker
    Assert-Run 'a global build input selects every suite and requires Docker' $run $false @(
        'build/x.ps1 -> all tests (global trigger)',
        $dockerMissing
    )

    $fixture = New-Fixture 'frontend' $false $true
    $stubs = New-Stubs 'no-pnpm' @{ docker = $workingStub; pnpm = $failingStub }
    $run = Invoke-EndToEnd $fixture @('-Suite', 'F.EndToEnd.Tests') $stubs
    Assert-Run 'a suite that serves built bundles needs pnpm' $run $false @(
        'need pnpm on PATH'
    )

    $stubs = New-Stubs 'recording-pnpm' @{ docker = $workingStub; pnpm = $recordingPnpm }
    $run = Invoke-EndToEnd $fixture @('-Suite', 'F.EndToEnd.Tests') $stubs
    Assert-Run 'a suite that serves built bundles builds them first' $run $false @(
        'install --frozen-lockfile',
        'turbo run build --filter=@x/app --filter=@x/other',
        'Building the frontend packages @x/app, @x/other failed'
    )

    $fixture = New-Fixture 'both' $true $true
    $stubs = New-Stubs 'no-dotnet' @{ docker = $workingStub; pnpm = $recordingPnpm; dotnet = $failingStub }
    $run = Invoke-EndToEnd $fixture @('-Suite', 'A.EndToEnd.Tests') $stubs
    Assert-Run 'a named suite that serves no bundles builds no frontend' $run $false @(
        'Building tests/A.EndToEnd.Tests/A.EndToEnd.Tests.csproj failed'
    )
    if ($run.Output.Contains('pnpm ')) {
        throw "Test 'a named suite that serves no bundles builds no frontend' expected no pnpm call, got:$([Environment]::NewLine)$($run.Output)"
    }

    Write-Host "$testCount e2e checks passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
