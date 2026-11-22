#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Lists the NCID0006 reports left in the projects a sweep step names. A temporary script, removed once the tree
    follows the rule.
.DESCRIPTION
    Builds each project without its dependencies and with -p:NamedLocalSweep=true, which reports NCID0006 as a warning
    that does not fail the build, and prints each distinct report under the given paths with its file, line and column.
    The script deletes the project's compiled assembly under obj/Debug first, so the compiler runs and reports again.
    It exits 0 and prints NCID0006: none when no report remains, or prints NCID0006 remains: <n> and exits 1.
.PARAMETER Project
    One or more project files, relative to the repository root or absolute. Each value may hold several, separated by
    commas.
.PARAMETER Path
    One or more folders, relative to the repository root, that a report must lie under to be listed. Each
    value may hold several, separated by commas. Without a path, every report of the projects is listed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Project,
    [string[]]$Path = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$env:MSBUILDDISABLENODEREUSE = '1'

$projects = @($Project | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$paths = @(
    $Path |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { ($_.Trim() -replace '\\', '/').Trim('/') } |
        Where-Object { $_ }
)

$reports = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($item in $projects) {
    $projectPath = if ([IO.Path]::IsPathRooted($item)) { $item } else { Join-Path $root $item }
    $projectPath = [IO.Path]::GetFullPath($projectPath)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "The project '$item' does not exist."
    }

    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $debugFolder = Join-Path (Split-Path -Parent $projectPath) 'obj/Debug'
    if (Test-Path -LiteralPath $debugFolder -PathType Container) {
        Get-ChildItem -LiteralPath $debugFolder -Recurse -File -Filter "$assemblyName.dll" |
            Remove-Item -Force
    }

    $output = @(dotnet build $projectPath --no-dependencies -m:2 -p:NamedLocalSweep=true 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $tail = (@($output | Select-Object -Last 40) | ForEach-Object { "$_" }) -join [Environment]::NewLine
        throw "dotnet build of $projectPath failed with exit code ${LASTEXITCODE}:$([Environment]::NewLine)$tail"
    }

    foreach ($line in @($output | ForEach-Object { "$_" })) {
        if ($line -notmatch 'warning NCID0006') { continue }

        $report = $line -replace '\s+\[[^\[\]]*\]\s*$', ''
        $normalized = $report -replace '\\', '/'
        $selected = $paths.Count -eq 0
        foreach ($folder in $paths) {
            if ($normalized.Contains("/$folder/", [StringComparison]::Ordinal)) { $selected = $true }
        }
        if ($selected) { $null = $reports.Add($report.Trim()) }
    }
}

$sorted = @($reports | Sort-Object)
if ($sorted.Count -gt 0) {
    $sorted
    "NCID0006 remains: $($sorted.Count)"
    exit 1
}

'NCID0006: none'
exit 0
