#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks build/brand-watch.ps1 against fixture locks and a stubbed gh.

.DESCRIPTION
    Never calls GitHub: each case defines a global gh function that records its arguments and answers from the case's
    data, and the function is removed afterwards. Needs no Node.js.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$watcher = Join-Path $PSScriptRoot '..' 'brand-watch.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-brand-watch-$([guid]::NewGuid().ToString('N'))"
$now = [DateTimeOffset]'2026-12-01T00:00:00Z'
$issueUrl = 'https://github.com/NCodeGroup/identity/issues/900'
$testCount = 0

function Write-Lock([string]$Path, [string]$Version) {
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    $json = ([ordered]@{ version = $Version; files = [ordered]@{} } | ConvertTo-Json -Depth 3) + "`n"
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

# Builds a repository root that holds the two brand locks and returns its path.
function New-Root([string]$Name, [string]$BrandVersion, [string]$ThemeVersion = $BrandVersion) {
    $root = Join-Path $fixtureRoot $Name
    Write-Lock (Join-Path $root 'brand' 'brand.lock.json') $BrandVersion
    Write-Lock (Join-Path $root 'frontend' 'packages' 'brand' 'brand.lock.json') $ThemeVersion
    return $root
}

function New-Release([string]$Tag, [string]$Published, [bool]$Draft = $false, [bool]$Prerelease = $false) {
    return [ordered]@{ tagName = $Tag; publishedAt = $Published; isDraft = $Draft; isPrerelease = $Prerelease }
}

function New-Issue([int]$Number, [string]$Title, [string]$Body) {
    return [ordered]@{ number = $Number; title = $Title; body = $Body }
}

# The JSON array gh prints for a list command: the data as an array, and `[]` when there is none.
function ConvertTo-Answer($Data) {
    if ($Data) { $Data | ConvertTo-Json -AsArray -Depth 5 } else { '[]' }
}

# Stands in for GitHub's command line: it records each call and the text of each --body-file, and answers the three list
# commands from the global data and issue create with a URL.
function global:gh {
    $global:BrandWatchCalls.Add(($args -join ' '))
    $global:LASTEXITCODE = 0
    $file = [array]::IndexOf([object[]]$args, '--body-file')
    if ($file -ge 0) {
        $global:BrandWatchBodies.Add([System.IO.File]::ReadAllText("$($args[$file + 1])"))
    }
    switch ("$($args[0]) $($args[1])") {
        'release list' { ConvertTo-Answer $global:BrandWatchReleases }
        'issue list' { ConvertTo-Answer $global:BrandWatchIssues }
        'label list' { ConvertTo-Answer $global:BrandWatchLabels }
        'issue create' { $global:BrandWatchUrl }
    }
}

# Runs the watcher against a fixture root with the stubbed gh and returns what it output and what it called.
function Invoke-Watch(
    [string]$Root,
    [object[]]$Releases = @(),
    [object[]]$Issues = @(),
    [object[]]$Labels = @(),
    [switch]$DryRun
) {
    $global:BrandWatchCalls = [System.Collections.Generic.List[string]]::new()
    $global:BrandWatchBodies = [System.Collections.Generic.List[string]]::new()
    $global:BrandWatchReleases = $Releases
    $global:BrandWatchIssues = $Issues
    $global:BrandWatchLabels = $Labels
    $global:BrandWatchUrl = $issueUrl
    $output = @(& $watcher -Root $Root -Now $now -DryRun:$DryRun)
    return [pscustomobject]@{
        Output = $output
        Calls = @($global:BrandWatchCalls)
        Bodies = @($global:BrandWatchBodies)
        Writes = @($global:BrandWatchCalls | Where-Object { $_ -match '^(issue (create|edit|close)|label create) ' })
    }
}

function Assert-True([string]$Name, $Condition, [string]$Detail) {
    if (-not $Condition) { throw "Test '$Name' failed: $Detail" }
}

# The run output exactly one line, equal to $Expected or, with -Like, matching it as a wildcard.
function Assert-Output([string]$Name, $Result, [string]$Expected, [switch]$Like) {
    $same = if ($Like) { $Result.Output.Count -eq 1 -and $Result.Output[0] -like $Expected }
    else { $Result.Output.Count -eq 1 -and $Result.Output[0] -eq $Expected }
    Assert-True $Name $same "output was: $($Result.Output -join ' | ')"
}

# Some call matches the regular expression.
function Assert-Call([string]$Name, $Result, [string]$Pattern) {
    Assert-True $Name ($Result.Calls -match $Pattern) "calls were: $($Result.Calls -join ' | ')"
}

function Assert-NoWrites([string]$Name, $Result) {
    Assert-True $Name ($Result.Writes.Count -eq 0) "writes were: $($Result.Writes -join ' | ')"
}

function Pass([string]$Name) {
    $script:testCount++
    Write-Host "PASS: $Name"
}

$old = '2026-11-01T00:00:00Z'
$settled = '2026-11-20T00:00:00Z'
$title = 'Update NCode Brand to 0.12.0'
$labelCall = 'label create brand-update --description A newer NCodeGroup/brand release is out --color 0033CC'
$pinnedText = 'The brand locks pin NCode Brand 0.11.0, and no release at least three days old is newer.'
$vendorCall = 'node brand/vendor-brand.mjs --update 0.12.0 --lock'

try {
    $root = New-Root 'behind' '0.11.0'
    $name = 'opens an issue and its label when the locks are behind'
    $result = Invoke-Watch $root -Releases @(
        (New-Release 'v0.11.0' $old),
        (New-Release 'v0.12.0' '2026-11-28T00:00:00Z'),
        (New-Release 'v0.9.0' $old),
        (New-Release 'nightly' $old)
    )
    Assert-Output $name $result "Opened ${issueUrl}: $title."
    Assert-True $name ($result.Calls -contains $labelCall) "calls were: $($result.Calls -join ' | ')"
    Assert-Call $name $result '^issue create --title Update NCode Brand to 0\.12\.0 --label brand-update --body-file '
    Assert-True $name ($result.Bodies.Count -eq 1) "bodies were: $($result.Bodies.Count)"
    $body = $result.Bodies[0]
    $firstLine = 'The brand locks pin NCode Brand 0.11.0. The newest release at least three days old is ' +
    '[0.12.0](https://github.com/NCodeGroup/brand/releases/tag/v0.12.0).'
    Assert-True $name $body.StartsWith("$firstLine`n`n", [StringComparison]::Ordinal) "body was: $body"
    Assert-True $name ($body.EndsWith("release.`n", [StringComparison]::Ordinal)) 'the body does not end with a newline'
    Assert-True $name (-not $body.EndsWith("`n`n")) 'the body ends with more than one newline'
    Assert-True $name (-not $body.Contains("`r")) 'the body has a carriage return'
    Assert-True $name $body.Contains("$vendorCall brand/brand.lock.json") "body was: $body"
    Assert-True $name $body.Contains("$vendorCall frontend/packages/brand/brand.lock.json") "body was: $body"
    Pass $name
    $openedBody = $body

    $name = 'leaves the label alone when the repository has it'
    $result = Invoke-Watch $root -Releases @((New-Release 'v0.12.0' $settled)) -Labels @(@{ name = 'brand-update' })
    Assert-True $name (-not ($result.Calls -match '^label create ')) "calls were: $($result.Calls -join ' | ')"
    Assert-Output $name $result "Opened ${issueUrl}: $title."
    Pass $name

    $name = 'edits the oldest open issue whose text differs'
    $result = Invoke-Watch $root -Releases @((New-Release 'v0.12.0' $settled)) -Issues @(
        (New-Issue 12 'Update NCode Brand to 0.10.0' 'older text'),
        (New-Issue 7 'Update NCode Brand to 0.11.0' 'older text')
    )
    Assert-Output $name $result "Updated #7: $title."
    Assert-Call $name $result '^issue edit 7 --title Update NCode Brand to 0\.12\.0 --body-file '
    Assert-True $name (-not ($result.Calls -match '^issue create ')) "calls were: $($result.Calls -join ' | ')"
    Assert-True $name ($result.Bodies.Count -eq 1 -and $result.Bodies[0] -eq $openedBody) 'the edit wrote another body'
    Pass $name

    $name = 'leaves a current issue alone'
    $result = Invoke-Watch $root -Releases @((New-Release 'v0.12.0' $settled)) -Issues @(
        (New-Issue 5 $title $openedBody.Replace("`n", "`r`n").TrimEnd())
    )
    Assert-Output $name $result "#5 already says: $title."
    Assert-NoWrites $name $result
    Pass $name

    $name = 'ignores a release younger than three days'
    $result = Invoke-Watch $root -Releases @(
        (New-Release 'v0.11.0' $old),
        (New-Release 'v0.12.0' '2026-11-28T00:00:01Z')
    )
    Assert-Output $name $result $pinnedText
    Assert-NoWrites $name $result
    Pass $name

    $name = 'ignores drafts and pre-releases'
    $result = Invoke-Watch $root -Releases @(
        (New-Release 'v0.11.0' $old),
        (New-Release 'v0.12.0' $settled $true),
        (New-Release 'v0.13.0' $settled $false $true)
    )
    Assert-Output $name $result $pinnedText
    Assert-NoWrites $name $result
    Pass $name

    $name = 'reports a repository without a settled release'
    $result = Invoke-Watch $root -Releases @((New-Release 'v0.12.0' '2026-11-30T00:00:00Z'))
    Assert-Output $name $result 'NCodeGroup/brand has no release at least three days old.'
    Assert-NoWrites $name $result
    Pass $name

    $name = 'closes the open issue once the locks pin the newest release'
    $pinnedRoot = New-Root 'pinned' '0.12.0'
    $result = Invoke-Watch $pinnedRoot -Releases @((New-Release 'v0.12.0' $settled)) -Issues @(
        (New-Issue 5 $title 'text')
    )
    Assert-Output $name $result 'Closed #5: the brand locks pin NCode Brand 0.12.0, and no release*' -Like
    Assert-Call $name $result '^issue close 5 --comment The brand locks pin NCode Brand 0\.12\.0, and no release '
    Pass $name

    $name = 'does nothing when the locks pin a release younger than three days'
    $youngRoot = New-Root 'young' '0.12.0'
    $result = Invoke-Watch $youngRoot -Releases @(
        (New-Release 'v0.11.0' $old),
        (New-Release 'v0.12.0' '2026-11-30T00:00:00Z')
    )
    Assert-Output $name $result `
        'The brand locks pin NCode Brand 0.12.0, and no release at least three days old is newer.'
    Assert-NoWrites $name $result
    Pass $name

    $name = 'does nothing once the locks pin the newest release and no issue is open'
    $result = Invoke-Watch $pinnedRoot -Releases @((New-Release 'v0.12.0' $settled))
    Assert-Output $name $result 'The brand locks pin NCode Brand 0.12.0*' -Like
    Assert-NoWrites $name $result
    Pass $name

    $name = '-DryRun makes no write call'
    $releases = @((New-Release 'v0.12.0' $settled))
    $result = Invoke-Watch $root -Releases $releases -DryRun
    Assert-Output $name $result "Would open: $title."
    Assert-NoWrites $name $result
    $result = Invoke-Watch $root -Releases $releases -DryRun -Issues @(
        (New-Issue 7 'Update NCode Brand to 0.11.0' 'text')
    )
    Assert-Output $name $result "Would update #7: $title."
    Assert-NoWrites $name $result
    $result = Invoke-Watch $pinnedRoot -Releases $releases -DryRun -Issues @((New-Issue 5 $title 'text'))
    Assert-Output $name $result 'Would close #5.'
    Assert-NoWrites $name $result
    Pass $name

    $name = 'locks naming different releases fail'
    $split = New-Root 'split' '0.11.0' '0.10.0'
    $said = @(& pwsh -NoProfile -File $watcher -Root $split 2>&1) -join ' '
    Assert-True $name ($LASTEXITCODE -ne 0) 'the watcher exited with code 0'
    Assert-True $name $said.Contains('name different releases') "it said: $said"
    Pass $name

    Write-Host "$testCount brand-watch checks passed."
}
finally {
    Remove-Item Function:\global:gh -ErrorAction SilentlyContinue
    $variables = 'BrandWatchCalls', 'BrandWatchBodies', 'BrandWatchReleases', 'BrandWatchIssues', 'BrandWatchLabels'
    foreach ($variable in $variables + 'BrandWatchUrl') {
        Remove-Variable -Name $variable -Scope Global -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
