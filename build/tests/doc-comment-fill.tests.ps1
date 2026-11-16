#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks build/doc-comment-fill.ps1 against fixture git repositories.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$checker = Join-Path $PSScriptRoot '..' 'doc-comment-fill.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "identity-doc-comment-fill-$([guid]::NewGuid().ToString('N'))"
$testCount = 0
$overLong = 'a doc-comment line runs past 120 columns'
$underFilled = "a doc-comment line leaves room for the next line's first word"

function Invoke-Git([string]$Repository, [string[]]$Arguments) {
    $output = & git -C $Repository @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$Repository'."
    }
    return $output
}

function Write-FixtureFile([string]$Path, [string]$Text) {
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

# Lines joined with LF endings and a final newline.
function New-Text([string[]]$Lines) {
    return ($Lines -join "`n") + "`n"
}

# Text of exactly $Length characters: words of up to eight letters separated by single spaces.
function New-Prose([int]$Length) {
    $text = ('abcdefgh ' * ([int][math]::Ceiling($Length / 9.0) + 1)).Substring(0, $Length)
    if ($text.EndsWith(' ')) {
        $text = $text.Substring(0, $Length - 1) + 'x'
    }
    return $text
}

# Returns the line after checking that it runs past 120 columns.
function Assert-Longer([string]$Line) {
    if ($Line.Length -le 120) {
        throw "Fixture line is $($Line.Length) characters, not more than 120: $Line"
    }
    return $Line
}

# A C# file whose doc comment starts on line 3, after the namespace and a blank line.
function New-CSharp([string[]]$DocLines = @(), [string]$Declaration = 'public sealed class Widget { }') {
    return New-Text (@('namespace A;', '') + $DocLines + @($Declaration))
}

function Save-Commit([string]$Repository) {
    $null = Invoke-Git $Repository @('add', '-A')
    $null = Invoke-Git $Repository @(
        '-c', 'user.name=fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-q', '-m', 'fixture')
}

# Creates a git repository with one commit of the given files (relative path -> text) and returns its path.
function New-Repository([string]$Name, [hashtable]$Files) {
    $repository = Join-Path $fixtureRoot $Name
    $null = [System.IO.Directory]::CreateDirectory($repository)
    $null = Invoke-Git $repository @('init', '-q')
    $null = Invoke-Git $repository @('config', 'core.autocrlf', 'false')
    $null = Invoke-Git $repository @('config', 'commit.gpgsign', 'false')
    foreach ($relative in $Files.Keys) {
        Write-FixtureFile (Join-Path $repository $relative) $Files[$relative]
    }
    Save-Commit $repository
    return $repository
}

function Assert-Problems(
    [string]$Name, [string]$Repository, [string[]]$ExpectedFragments, [hashtable]$Parameters = @{}) {
    $arguments = @{ Root = $Repository; Base = 'HEAD' }
    foreach ($key in $Parameters.Keys) {
        $arguments[$key] = $Parameters[$key]
    }
    $problems = @(& $checker @arguments)
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

$underFilledCSharp = New-CSharp @(
    '/// <summary>',
    '/// A short first line.',
    '/// A second line that continues it.',
    '/// </summary>')
$underFilledTypeScript = New-Text @(
    '/**',
    ' * A short first line.',
    ' * A second line that continues it.',
    ' */',
    'export const widget = 1;')

try {
    $repository = New-Repository 'filled' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp @(
            '/// <summary>',
            "/// $(New-Prose 116)",
            '/// A closing sentence.',
            '/// </summary>'))
    Assert-Problems 'a filled C# summary has no problems' $repository @()

    $repository = New-Repository 'under-filled' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') $underFilledCSharp
    Assert-Problems 'an under-filled C# line is reported' $repository @("src/A/Widget.cs(4): $underFilled")

    $repository = New-Repository 'over-long' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp @(
            '/// <summary>',
            "/// $(New-Prose 117)",
            '/// </summary>'))
    Assert-Problems 'a C# line over 120 columns is reported' $repository @("src/A/Widget.cs(4): $overLong")

    $repository = New-Repository 'exempt' @{ 'src/A/Widget.cs' = New-CSharp }
    $cancellation = 'A token that cancels the call when the caller no longer waits for the long running work to finish.'
    $response = 'The tenant exists, and the body holds the tenant with every one of its settings and each of its members.'
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp @(
            '/// <summary>',
            '/// Runs the work.',
            '/// </summary>',
            (Assert-Longer "/// <param name=`"cancellationToken`">$cancellation</param>"),
            (Assert-Longer "/// <response code=`"200`">$response</response>"),
            '/// <remarks>',
            (Assert-Longer '/// <see cref="Widget.Run(System.Threading.CancellationToken, System.String, System.Int32, System.Boolean, System.Object)"/>'),
            '/// </remarks>'))
    Assert-Problems 'the cancellationToken sentence, a response line and a lone see element are not reported' $repository @()

    $oldParagraph = @(
        '/// <summary>',
        '/// Old short line one.',
        '/// Old short line two.',
        '/// </summary>')
    $repository = New-Repository 'untouched' @{ 'src/A/Widget.cs' = New-CSharp $oldParagraph }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp $oldParagraph 'public sealed class Widget { public int X; }')
    Assert-Problems 'an old under-filled paragraph that no changed line touches is not reported' $repository @()
    Assert-Problems 'an old under-filled paragraph is reported with -All' $repository @("src/A/Widget.cs(4): $underFilled") @{ All = $true }

    $paragraph = @(
        '/// <summary>',
        "/// $(New-Prose 116)",
        '/// An old short line.',
        '/// Last line.',
        '/// </summary>')
    $repository = New-Repository 'edited-paragraph' @{ 'src/A/Widget.cs' = New-CSharp $paragraph }
    $paragraph[3] = '/// Last line, edited.'
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp $paragraph)
    Assert-Problems 'an old under-filled line in a paragraph with an edited line is reported' $repository @(
        "src/A/Widget.cs(5): $underFilled")

    $repository = New-Repository 'boundaries' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp @(
            '/// <summary>',
            '/// First paragraph.',
            '/// <para>',
            '/// Second paragraph.',
            '/// </para>',
            '/// <code>',
            '/// var a = 1;',
            '/// var b = 2;',
            '/// </code>',
            '/// </summary>',
            '/// <param name="a">The a.</param>',
            '/// <param name="b">The b.</param>',
            '/// <returns>The result.</returns>'))
    Assert-Problems 'para, param and code boundaries end a C# paragraph' $repository @()

    $repository = New-Repository 'block-tags' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') (New-CSharp @(
            '/// <summary>',
            '/// Runs the work.',
            '/// </summary>',
            '/// <typeparam name="T">',
            '/// The type.',
            '/// </typeparam>',
            '/// <param name="c">',
            '/// The c.',
            '/// </param>',
            '/// <exception cref="System.InvalidOperationException">',
            '/// Thrown when the work cannot run.',
            '/// </exception>',
            '/// <returns>The result.</returns>'))
    Assert-Problems 'a closing param, typeparam or exception tag on its own line ends a C# paragraph' $repository @()

    $repository = New-Repository 'typescript' @{ 'frontend/web/src/widget.ts' = "export const widget = 1;`n" }
    Write-FixtureFile (Join-Path $repository 'frontend/web/src/widget.ts') $underFilledTypeScript
    Assert-Problems 'an under-filled TypeScript JSDoc line is reported' $repository @(
        "frontend/web/src/widget.ts(2): $underFilled")

    $repository = New-Repository 'typescript-list' @{ 'frontend/web/src/widget.ts' = "export const widget = 1;`n" }
    Write-FixtureFile (Join-Path $repository 'frontend/web/src/widget.ts') (New-Text @(
            '/**',
            ' * Applies the filter:',
            " * - $(New-Prose 113)",
            ' *   continues here.',
            ' * @param value the value.',
            ' * @returns the result.',
            ' */',
            'export const widget = 1;'))
    Assert-Problems 'a list item with an indented continuation and a tag line are not reported' $repository @()

    $repository = New-Repository 'typescript-fence' @{ 'frontend/web/src/widget.ts' = "export const widget = 1;`n" }
    Write-FixtureFile (Join-Path $repository 'frontend/web/src/widget.ts') (New-Text @(
            '/**',
            ' * Usage:',
            ' * ```ts',
            ' * const a = 1;',
            ' * const b = 2;',
            ' * ```',
            ' */',
            'export const widget = 1;'))
    Assert-Problems 'a fenced code block is not reported' $repository @()

    $repository = New-Repository 'typescript-single' @{ 'frontend/web/src/widget.ts' = "export const widget = 1;`n" }
    Write-FixtureFile (Join-Path $repository 'frontend/web/src/widget.ts') (New-Text @(
            "/** $(New-Prose 115) */",
            '/** A short note. */',
            'export const widget = 1;'))
    Assert-Problems 'a single-line JSDoc over 120 columns is reported and a short one is not' $repository @(
        "frontend/web/src/widget.ts(1): $overLong")

    $repository = New-Repository 'untracked' @{ 'src/A/Widget.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/Fresh.cs') $underFilledCSharp
    Write-FixtureFile (Join-Path $repository 'frontend/web/src/fresh.ts') $underFilledTypeScript
    Assert-Problems 'every line of an untracked file is checked' $repository @(
        "frontend/web/src/fresh.ts(2): $underFilled",
        "src/A/Fresh.cs(4): $underFilled")

    $repository = New-Repository 'skipped' @{ 'src/A/Widget.cs' = New-CSharp }
    foreach ($relative in @('src/A/bin/X.cs', 'src/A/obj/X.cs', 'src/A/Migrations/X.cs', 'tools/Out.cs')) {
        Write-FixtureFile (Join-Path $repository $relative) $underFilledCSharp
    }
    foreach ($folder in @('node_modules/p', 'dist', 'storybook-static', 'coverage', 'test-results')) {
        Write-FixtureFile (Join-Path $repository "frontend/web/$folder/x.ts") $underFilledTypeScript
    }
    Write-FixtureFile (Join-Path $repository 'src/A/Control.cs') $underFilledCSharp
    Assert-Problems 'generated, ignored and out-of-scope paths are skipped' $repository @(
        "src/A/Control.cs(4): $underFilled")

    $repository = New-Repository 'path' @{ 'src/A/One.cs' = New-CSharp; 'src/A/Two.cs' = New-CSharp }
    Write-FixtureFile (Join-Path $repository 'src/A/One.cs') $underFilledCSharp
    Write-FixtureFile (Join-Path $repository 'src/A/Two.cs') $underFilledCSharp
    Assert-Problems 'without -Path every changed file is reported in path order' $repository @(
        "src/A/One.cs(4): $underFilled",
        "src/A/Two.cs(4): $underFilled")
    Assert-Problems '-Path keeps the named file' $repository @("src/A/Two.cs(4): $underFilled") @{ Path = 'src/A/Two.cs' }
    Assert-Problems '-Path accepts backslash separators' $repository @("src/A/One.cs(4): $underFilled") @{ Path = 'src\A\One.cs' }

    $repository = New-Repository 'committed' @{ 'src/A/Widget.cs' = New-CSharp }
    $baseCommit = (Invoke-Git $repository @('rev-parse', 'HEAD') | Select-Object -First 1).Trim()
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') $underFilledCSharp
    Save-Commit $repository
    Assert-Problems 'a committed change is reported against the base commit' $repository @(
        "src/A/Widget.cs(4): $underFilled") @{ Base = $baseCommit }
    Assert-Problems 'a committed change is not reported against HEAD' $repository @()

    $repository = New-Repository 'crlf' @{ 'src/A/Widget.cs' = New-CSharp }
    $crlf = (New-CSharp @(
            '/// <summary>',
            "/// $(New-Prose 116)",
            '/// A closing sentence.',
            '/// </summary>')).Replace("`n", "`r`n")
    Write-FixtureFile (Join-Path $repository 'src/A/Widget.cs') $crlf
    Assert-Problems 'a CRLF line ending is not part of a line width' $repository @()

    $repository = New-Repository 'missing-base' @{ 'src/A/Widget.cs' = New-CSharp }
    $output = (& pwsh -NoProfile -NonInteractive -File $checker -Root $repository -Base 'refs/heads/missing' 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0) {
        throw 'Test ''a missing merge base fails'' expected a non-zero exit code.'
    }
    $expected = "Cannot find the merge base of HEAD and 'refs/heads/missing'. Fetch it first (git fetch origin)."
    if (-not ($output -replace '\s+', ' ').Contains($expected, [StringComparison]::Ordinal)) {
        throw "Test 'a missing merge base fails' expected '$expected', got '$output'."
    }
    $testCount++
    Write-Host 'PASS: a missing merge base fails with a fetch hint'

    Write-Host "$testCount doc-comment-fill checks passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
