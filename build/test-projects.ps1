#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Lists the repository's test projects by tier.

.DESCRIPTION
    Finds every *.csproj under the tests/ folder, skipping bin/ and obj/ folders. A project whose file name ends with
    .EndToEnd.Tests.csproj belongs to the end-to-end tier, one whose file name ends with .Tests.csproj is a test
    project, and any other project under tests/ is a support project that a suite references, which neither tier lists.
    An end-to-end project whose project file sets EndToEndFrontendPackages (pnpm package names separated by ;) serves
    those frontend packages' built bundles: FrontendPackages maps its path to the names, which ./build/e2e.ps1 builds
    before it runs the suite and build/affected-tests.ps1 selects the suite for. An end-to-end project whose project
    file sets EndToEndContainers to true starts containers: ContainerSuites lists its path, and ./build/e2e.ps1 checks
    for a running Docker engine before it runs such a suite.

    Outputs one object with Tests (repository-relative .csproj paths of the test projects), EndToEnd
    (repository-relative .csproj paths of the end-to-end projects), FrontendPackages (each end-to-end project that names
    frontend packages, mapped to those names) and ContainerSuites (the end-to-end projects that start containers). Paths
    use / separators and each list is sorted ordinal.

.PARAMETER Root
    The repository root. Defaults to the parent of the build/ directory.

.EXAMPLE
    ./build/test-projects.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = [IO.Path]::GetFullPath($Root)
$testsFolder = Join-Path $Root 'tests'

$tests = [System.Collections.Generic.List[string]]::new()
$endToEnd = [System.Collections.Generic.List[string]]::new()

if (Test-Path -LiteralPath $testsFolder -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $testsFolder -Recurse -File -Filter '*.csproj') {
        $relativePath = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        if ($relativePath -match '/(bin|obj)/') {
            continue
        }
        if ($file.Name.EndsWith('.EndToEnd.Tests.csproj', [StringComparison]::Ordinal)) {
            $endToEnd.Add($relativePath)
        }
        elseif ($file.Name.EndsWith('.Tests.csproj', [StringComparison]::Ordinal)) {
            $tests.Add($relativePath)
        }
    }
}

$tests.Sort([StringComparer]::Ordinal)
$endToEnd.Sort([StringComparer]::Ordinal)

$frontendPackages = [ordered]@{}
$containerSuites = [System.Collections.Generic.List[string]]::new()
foreach ($project in $endToEnd) {
    [xml]$xml = Get-Content -LiteralPath (Join-Path $Root $project) -Raw
    foreach ($node in $xml.SelectNodes('//EndToEndContainers')) {
        if ($node.InnerText.Trim().Equals('true', [StringComparison]::OrdinalIgnoreCase)) {
            $containerSuites.Add($project)
            break
        }
    }
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($node in $xml.SelectNodes('//EndToEndFrontendPackages')) {
        foreach ($name in $node.InnerText.Split(';')) {
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $names.Add($name.Trim())
            }
        }
    }
    if ($names.Count -gt 0) {
        $frontendPackages[$project] = [string[]]$names.ToArray()
    }
}

[pscustomobject]@{
    Tests            = [string[]]$tests.ToArray()
    EndToEnd         = [string[]]$endToEnd.ToArray()
    FrontendPackages = $frontendPackages
    ContainerSuites  = [string[]]$containerSuites.ToArray()
}
