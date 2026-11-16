#!/usr/bin/env pwsh
#requires -version 7

<#
.SYNOPSIS
    Checks that the doc-comment paragraphs a change touches are filled to 120 columns.

.DESCRIPTION
    A doc comment is filled when each line carries as many words as fit within 120 columns: no line runs past 120
    columns, and no line, except the last of its paragraph, leaves room for the next line's first word. The width is
    one for C# /// comments and TypeScript JSDoc. CSharpier and Prettier format code at 100 columns and never rewrap a
    comment, so this check decides what a formatter leaves open.

    The check covers *.cs files under src/, hosts/, analyzers/ and tests/, and *.ts and *.tsx files under frontend/.
    It skips a path with a bin, obj, Migrations, node_modules, dist, storybook-static, coverage or test-results folder.

    A paragraph is a run of doc lines that read as one block of prose. A blank doc line, a line that holds only an
    opening tag, a line that ends with a closing tag, a line that starts or closes a block tag (<para>, </param>,
    <list> and the like in C#; @tag, a list item or a table row in TypeScript) and every line of a <code> block or a
    fenced code block end it. A single-line /** ... */ comment is a paragraph of its own.

    The check judges the paragraphs a change touches: a paragraph is touched when any of its lines differs between the
    working tree and the merge base of HEAD and -Base, or when its file is untracked. -All judges every paragraph.

    Outputs one string per problem, sorted ordinally by path and then by line, and nothing when clean:
    - a line longer than 120 characters, unless its text is one token, such as a long <see cref="..."/>, or it is the
      <param name="cancellationToken"> sentence or a <response> line;
    - a line, not the last of its paragraph, that leaves room for the next line's first word.

.PARAMETER Root
    The repository root. Defaults to the parent of the build/ directory.

.PARAMETER Base
    The commit-ish whose merge base with HEAD the changes are measured from. Defaults to origin/main.

.PARAMETER Path
    Repository-relative files (with / or \ separators) to keep. Every other file is skipped.

.PARAMETER All
    Judges every paragraph of every file in scope, instead of the paragraphs the change touches.

.EXAMPLE
    ./build/doc-comment-fill.ps1

.EXAMPLE
    ./build/doc-comment-fill.ps1 -Path src/NCode.Identity.Time/TimeProviderExtensions.cs -Base origin/main
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Base = 'origin/main',
    [string[]]$Path,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = [IO.Path]::GetFullPath($Root)
$maxWidth = 120
$overLongMessage = 'a doc-comment line runs past 120 columns; reflow its paragraph.'
$underFilledMessage = "a doc-comment line leaves room for the next line's first word; fill its paragraph."

$skippedSegments = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@('bin', 'obj', 'Migrations', 'node_modules', 'dist', 'storybook-static', 'coverage', 'test-results'),
    [StringComparer]::Ordinal)
$scopePattern = '^(?:(?:src|hosts|analyzers|tests)/.+\.cs|frontend/.+\.tsx?)$'

$hunkHeader = [regex]::new('^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@')
$csDocLine = [regex]::new('^\s*/// ?(.*)$')
$csToken = [regex]::new('(?:<c>.*?</c>|<[^>]*>|[^\s<])+')
$tsToken = [regex]::new('(?:\{@[^}]*\}|`[^`]*`|\S)+')
$tsOpen = [regex]::new('^\s*/\*\*')
$tsMiddle = [regex]::new('^\s*\*(?: (.*)|$)')
$csOpeningTagOnly = '^<[A-Za-z]+(\s[^>]*)?>$'
$csClosingTagEnd = '</(summary|remarks|returns|para|param|typeparam|exception|item|description|value|response|list|code|term|example|listheader)>$'
$csBlockTagStart = '^(?:</?(?:summary|remarks|returns|para|list|item|description|term|value|example|code|listheader)\b|</?(?:param|typeparam|exception|inheritdoc|response|seealso|include)\b)'
$tsBlockStart = '^(?:@|- |\* |\+ |\d+[.)] |\||<)'

function Invoke-Git {
    param([string[]]$Arguments, [string]$Failure)
    $output = & git -C $Root -c core.quotepath=false @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $Failure
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @($output)) {
        if ($line) {
            $lines.Add([string]$line)
        }
    }
    return $lines.ToArray()
}

function Test-InScope([string]$RelativePath) {
    if ($RelativePath -cnotmatch $scopePattern) {
        return $false
    }
    foreach ($segment in $RelativePath.Split('/')) {
        if ($skippedSegments.Contains($segment)) {
            return $false
        }
    }
    return $true
}

function New-DocLine([int]$Index, [int]$Block, [string]$Text, [int]$Width, [bool]$Single) {
    return [pscustomobject]@{
        Index  = $Index
        Block  = $Block
        Text   = $Text
        Width  = $Width
        Single = $Single
        Code   = $false
    }
}

# The /// lines of a C# file. A block is a run of consecutive /// lines; a <code> element is code, ends included.
function Get-CSharpDocLines([string[]]$Lines) {
    $records = [System.Collections.Generic.List[object]]::new()
    $block = 0
    $previous = -2
    $inCode = $false
    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $match = $csDocLine.Match($Lines[$i])
        if (-not $match.Success) {
            continue
        }
        if ($i -ne $previous + 1) {
            $block++
            $inCode = $false
        }
        $previous = $i
        $text = $match.Groups[1].Value.TrimEnd()
        $record = New-DocLine $i $block $text $Lines[$i].TrimEnd().Length $false
        if (-not $inCode -and $text.TrimStart().StartsWith('<code', [StringComparison]::Ordinal)) {
            $inCode = $true
        }
        if ($inCode) {
            $record.Code = $true
            if ($text.EndsWith('</code>', [StringComparison]::Ordinal)) {
                $inCode = $false
            }
        }
        $records.Add($record)
    }
    return $records
}

# The lines of the /** ... */ blocks of a TypeScript file. A fenced code block is code, fences included.
function Get-TypeScriptDocLines([string[]]$Lines) {
    $records = [System.Collections.Generic.List[object]]::new()
    $block = 0
    $i = 0
    while ($i -lt $Lines.Length) {
        $line = $Lines[$i]
        $open = $tsOpen.Match($line)
        if (-not $open.Success) {
            $i++
            continue
        }
        $block++
        $blockRecords = [System.Collections.Generic.List[object]]::new()
        $closeAt = $line.IndexOf('*/', $open.Length - 1, [StringComparison]::Ordinal)
        if ($closeAt -ge 0) {
            $content = if ($closeAt -gt $open.Length) { $line.Substring($open.Length, $closeAt - $open.Length) } else { '' }
            if ($content.StartsWith(' ', [StringComparison]::Ordinal)) {
                $content = $content.Substring(1)
            }
            $blockRecords.Add((New-DocLine $i $block $content.TrimEnd() $line.TrimEnd().Length $true))
            $i++
        }
        else {
            $content = $line.Substring($open.Length)
            if ($content.StartsWith(' ', [StringComparison]::Ordinal)) {
                $content = $content.Substring(1)
            }
            $blockRecords.Add((New-DocLine $i $block $content.TrimEnd() $line.TrimEnd().Length $false))
            $i++
            while ($i -lt $Lines.Length) {
                $line = $Lines[$i]
                $closeAt = $line.IndexOf('*/', [StringComparison]::Ordinal)
                if ($closeAt -ge 0) {
                    $before = $line.Substring(0, $closeAt)
                    $star = $tsMiddle.Match($before)
                    $content = if ($star.Success) { $star.Groups[1].Value } else { $before }
                    $blockRecords.Add((New-DocLine $i $block $content.TrimEnd() $line.TrimEnd().Length $false))
                    $i++
                    break
                }
                $star = $tsMiddle.Match($line)
                $content = if ($star.Success) { $star.Groups[1].Value } else { '' }
                $blockRecords.Add((New-DocLine $i $block $content.TrimEnd() $line.TrimEnd().Length $false))
                $i++
            }
        }
        $inFence = $false
        foreach ($record in $blockRecords) {
            if ($record.Text.TrimStart().StartsWith('```', [StringComparison]::Ordinal)) {
                $record.Code = $true
                $inFence = -not $inFence
            }
            elseif ($inFence) {
                $record.Code = $true
            }
            $records.Add($record)
        }
    }
    return $records
}

function Test-Joinable([object]$Previous, [object]$Next, [bool]$IsTypeScript) {
    if ($Previous.Block -ne $Next.Block -or $Next.Index -ne $Previous.Index + 1) {
        return $false
    }
    if ($Previous.Code -or $Next.Code -or $Previous.Text.Length -eq 0 -or $Next.Text.Length -eq 0) {
        return $false
    }
    $nextText = $Next.Text.TrimStart()
    if ($IsTypeScript) {
        if ($Previous.Single -or $Next.Single) {
            return $false
        }
        return $nextText -cnotmatch $tsBlockStart
    }
    return -not ($Previous.Text.TrimStart() -cmatch $csOpeningTagOnly) -and
        -not ($Previous.Text -cmatch $csClosingTagEnd) -and
        -not ($nextText -cmatch $csBlockTagStart)
}

function Get-FirstToken([string]$Text, [bool]$IsTypeScript) {
    $token = if ($IsTypeScript) { $tsToken } else { $csToken }
    $match = $token.Match($Text.TrimStart())
    if ($match.Success) {
        return $match.Value
    }
    return $null
}

function Test-OverLongExempt([object]$Record, [bool]$IsTypeScript) {
    $token = if ($IsTypeScript) { $tsToken } else { $csToken }
    $trimmed = $Record.Text.Trim()
    $match = $token.Match($trimmed)
    if ($match.Success -and $match.Length -eq $trimmed.Length) {
        return $true
    }
    if ($IsTypeScript) {
        return $false
    }
    return $trimmed.Contains('<param name="cancellationToken">', [StringComparison]::Ordinal) -or
        $trimmed.StartsWith('<response', [StringComparison]::Ordinal)
}

# Adds the problems of one paragraph, a list of consecutive joinable doc lines.
function Add-ParagraphProblems(
    [object[]]$Paragraph, [bool]$IsTypeScript, [string]$RelativePath, [System.Collections.Generic.List[string]]$Problems) {
    for ($k = 0; $k -lt $Paragraph.Length; $k++) {
        $record = $Paragraph[$k]
        if ($record.Width -gt $maxWidth) {
            if (-not (Test-OverLongExempt $record $IsTypeScript)) {
                $Problems.Add("${RelativePath}($($record.Index + 1)): $overLongMessage")
            }
        }
        elseif ($k -lt $Paragraph.Length - 1) {
            $next = $Paragraph[$k + 1]
            $word = Get-FirstToken $next.Text $IsTypeScript
            if ($null -ne $word -and $record.Width + 1 + $word.Length -le $maxWidth) {
                $Problems.Add("${RelativePath}($($record.Index + 1)): $underFilledMessage")
            }
        }
    }
}

# The doc lines of a file grouped into paragraphs, each judged when any of its lines is touched.
function Get-FileProblems(
    [object[]]$Records, [bool]$IsTypeScript, [string]$RelativePath, [object]$Touched,
    [System.Collections.Generic.List[string]]$Problems) {
    $paragraph = [System.Collections.Generic.List[object]]::new()
    $paragraphTouched = $false
    foreach ($record in $Records) {
        $endsParagraph = $record.Code -or $record.Text.Length -eq 0
        if ($paragraph.Count -gt 0 -and
            ($endsParagraph -or -not (Test-Joinable $paragraph[$paragraph.Count - 1] $record $IsTypeScript))) {
            if ($paragraphTouched) {
                $lines = $paragraph.ToArray()
                Add-ParagraphProblems $lines $IsTypeScript $RelativePath $Problems
            }
            $paragraph.Clear()
            $paragraphTouched = $false
        }
        if ($endsParagraph) {
            continue
        }
        $paragraph.Add($record)
        if ($null -eq $Touched -or $Touched[$record.Index + 1]) {
            $paragraphTouched = $true
        }
    }
    if ($paragraph.Count -gt 0 -and $paragraphTouched) {
        $lines = $paragraph.ToArray()
        Add-ParagraphProblems $lines $IsTypeScript $RelativePath $Problems
    }
}

$selectedPaths = $null
if ($PSBoundParameters.ContainsKey('Path')) {
    $selectedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Path) {
        if (-not [string]::IsNullOrWhiteSpace($item)) {
            $full = [IO.Path]::GetFullPath($item, $Root)
            $null = $selectedPaths.Add([IO.Path]::GetRelativePath($Root, $full).Replace('\', '/'))
        }
    }
}

# Each candidate file, keyed by its repository-relative path: true when every line of it is touched.
$candidates = [ordered]@{}
$mergeBase = $null
if ($All) {
    foreach ($name in @(Invoke-Git -Arguments @('ls-files', '--cached', '--others', '--exclude-standard') `
                -Failure 'git ls-files failed.')) {
        $candidates[$name] = $true
    }
}
else {
    $baseFailure = "Cannot find the merge base of HEAD and '$Base'. Fetch it first (git fetch origin)."
    $mergeBases = @(Invoke-Git -Arguments @('merge-base', 'HEAD', $Base) -Failure $baseFailure)
    if ($mergeBases.Count -eq 0) {
        throw $baseFailure
    }
    $mergeBase = $mergeBases[0].Trim()
    foreach ($name in @(Invoke-Git -Arguments @('diff', '--name-only', '--diff-filter=AM', '--no-renames', $mergeBase) `
                -Failure "git diff against '$mergeBase' failed.")) {
        $candidates[$name] = $false
    }
    foreach ($name in @(Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard') `
                -Failure 'git ls-files failed.')) {
        $candidates[$name] = $true
    }
}

$files = [string[]]@($candidates.Keys)
[Array]::Sort($files, [StringComparer]::Ordinal)

$problems = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $files) {
    if (-not (Test-InScope $relative)) {
        continue
    }
    if ($null -ne $selectedPaths -and -not $selectedPaths.Contains($relative)) {
        continue
    }
    $fullPath = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    $isTypeScript = $relative.EndsWith('.ts', [StringComparison]::Ordinal) -or
        $relative.EndsWith('.tsx', [StringComparison]::Ordinal)
    $text = [IO.File]::ReadAllText($fullPath)
    if ($text.IndexOf($(if ($isTypeScript) { '/**' } else { '///' }), [StringComparison]::Ordinal) -lt 0) {
        continue
    }
    $lines = [regex]::Split($text, '\r?\n')
    $records = @(if ($isTypeScript) { Get-TypeScriptDocLines $lines } else { Get-CSharpDocLines $lines })
    if ($records.Count -eq 0) {
        continue
    }

    # $null marks every line as touched; otherwise a 1-based flag per line.
    $touched = $null
    if (-not $candidates[$relative]) {
        $touched = [bool[]]::new($lines.Length + 2)
        $diff = @(Invoke-Git -Arguments @('diff', '-U0', '--no-color', '--no-renames', $mergeBase, '--', $relative) `
            -Failure "git diff of '$relative' failed.")
        foreach ($diffLine in $diff) {
            $header = $hunkHeader.Match($diffLine)
            if (-not $header.Success) {
                continue
            }
            $start = [int]$header.Groups[1].Value
            $count = if ($header.Groups[2].Success) { [int]$header.Groups[2].Value } else { 1 }
            for ($n = $start; $n -lt $start + $count -and $n -lt $touched.Length; $n++) {
                $touched[$n] = $true
            }
        }
    }

    Get-FileProblems -Records $records -IsTypeScript $isTypeScript -RelativePath $relative -Touched $touched `
        -Problems $problems
}

$problems
