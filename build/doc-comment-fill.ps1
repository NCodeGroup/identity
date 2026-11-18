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
    working tree and the merge base of HEAD and -Base, when a deletion removed lines right before or after one of its
    lines, or when its file is untracked. -All judges every paragraph.

    Outputs one string per problem, sorted ordinally by path and then by line, and nothing when clean:
    - a line longer than 120 characters, unless its text is one token, such as a long <see cref="..."/>, or it is the
      <param name="cancellationToken"> sentence or a <response> line;
    - a line, not the last of its paragraph, that leaves room for the next line's first word.

    -Fix reflows each touched paragraph that has a problem, then outputs the problems that remain. A reflow takes the
    paragraph's tokens in order, keeps a <c>...</c> element, a tag, a {@...} inline tag and a backtick code span whole,
    and fills each line with as many tokens as fit within 120 columns. The first line keeps its own prefix and the
    leading white space of its text, and a continuation line takes the second line's prefix and leading white space. A
    single-line /** ... */ comment over 120 columns becomes an opening line, filled * lines and a closing line. Every
    other line stays byte-for-byte, and a file keeps its line endings and final newline and is written as UTF-8
    without a byte order mark.

.PARAMETER Root
    The repository root. Defaults to the parent of the build/ directory.

.PARAMETER Base
    The commit-ish whose merge base with HEAD the changes are measured from. Defaults to origin/main.

.PARAMETER Path
    Repository-relative files (with / or \ separators) to keep. Every other file is skipped.

.PARAMETER All
    Judges every paragraph of every file in scope, instead of the paragraphs the change touches.

.PARAMETER Fix
    Reflows each judged paragraph that has a problem, writes the file, and outputs the problems that remain.

.EXAMPLE
    ./build/doc-comment-fill.ps1

.EXAMPLE
    ./build/doc-comment-fill.ps1 -Path src/NCode.Identity.Time/TimeProviderExtensions.cs -Base origin/main

.EXAMPLE
    ./build/doc-comment-fill.ps1 -Path src/NCode.Identity.Time/TimeProviderExtensions.cs -Fix
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Base = 'origin/main',
    [string[]]$Path,
    [switch]$All,
    [switch]$Fix
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

function New-DocLine(
    [int]$Index, [int]$Block, [string]$Text, [int]$Width, [bool]$Single, [string]$Prefix, [string]$Suffix) {
    return [pscustomobject]@{
        Index        = $Index
        Block        = $Block
        Text         = $Text
        Width        = $Width
        Single       = $Single
        Code         = $false
        Prefix       = $Prefix
        Suffix       = $Suffix
        Continuation = $Prefix
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
        $prefix = $Lines[$i].Substring(0, $match.Groups[1].Index)
        $record = New-DocLine $i $block $text $Lines[$i].TrimEnd().Length $false $prefix ''
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
            $skip = if ($content.StartsWith(' ', [StringComparison]::Ordinal)) { 1 } else { 0 }
            $text = $content.Substring($skip).TrimEnd()
            $prefixLength = $open.Length + $skip
            $suffix = $line.Substring($prefixLength + $text.Length).TrimEnd()
            $blockRecords.Add((New-DocLine $i $block $text $line.TrimEnd().Length $true $line.Substring(0, $prefixLength) $suffix))
            $i++
        }
        else {
            $content = $line.Substring($open.Length)
            $skip = if ($content.StartsWith(' ', [StringComparison]::Ordinal)) { 1 } else { 0 }
            $prefixLength = $open.Length + $skip
            $record = New-DocLine $i $block $content.Substring($skip).TrimEnd() $line.TrimEnd().Length $false `
                $line.Substring(0, $prefixLength) ''
            $record.Continuation = $line.Substring(0, $open.Length - 3) + ' * '
            $blockRecords.Add($record)
            $i++
            while ($i -lt $Lines.Length) {
                $line = $Lines[$i]
                $closeAt = $line.IndexOf('*/', [StringComparison]::Ordinal)
                $body = if ($closeAt -ge 0) { $line.Substring(0, $closeAt) } else { $line }
                $star = $tsMiddle.Match($body)
                if ($star.Success) {
                    $prefixLength = if ($star.Groups[1].Success) { $star.Groups[1].Index } else { $star.Length }
                    $content = $star.Groups[1].Value
                }
                elseif ($closeAt -ge 0) {
                    $prefixLength = 0
                    $content = $body
                }
                else {
                    $prefixLength = $line.Length
                    $content = ''
                }
                $text = $content.TrimEnd()
                $suffix = if ($closeAt -ge 0) { $line.Substring($prefixLength + $text.Length).TrimEnd() } else { '' }
                $blockRecords.Add((New-DocLine $i $block $text $line.TrimEnd().Length $false $line.Substring(0, $prefixLength) $suffix))
                $i++
                if ($closeAt -ge 0) {
                    break
                }
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

# The problems of one paragraph, a list of consecutive joinable doc lines: the line index and message of each.
function Get-ParagraphProblems([object[]]$Paragraph, [bool]$IsTypeScript) {
    $found = [System.Collections.Generic.List[object]]::new()
    for ($k = 0; $k -lt $Paragraph.Length; $k++) {
        $record = $Paragraph[$k]
        if ($record.Width -gt $maxWidth) {
            if (-not (Test-OverLongExempt $record $IsTypeScript)) {
                $found.Add([pscustomobject]@{ Index = $record.Index; Message = $overLongMessage })
            }
        }
        elseif ($k -lt $Paragraph.Length - 1) {
            $next = $Paragraph[$k + 1]
            $word = Get-FirstToken $next.Text $IsTypeScript
            if ($null -ne $word -and $record.Width + 1 + $word.Length -le $maxWidth) {
                $found.Add([pscustomobject]@{ Index = $record.Index; Message = $underFilledMessage })
            }
        }
    }
    return $found
}

# The doc lines of a file grouped into paragraphs, each kept when any of its lines is touched.
function Get-TouchedParagraphs([object[]]$Records, [bool]$IsTypeScript, [object]$Touched) {
    $paragraphs = [System.Collections.Generic.List[object]]::new()
    $paragraph = [System.Collections.Generic.List[object]]::new()
    $paragraphTouched = $false
    foreach ($record in $Records) {
        $endsParagraph = $record.Code -or $record.Text.Length -eq 0
        if ($paragraph.Count -gt 0 -and
            ($endsParagraph -or -not (Test-Joinable $paragraph[$paragraph.Count - 1] $record $IsTypeScript))) {
            if ($paragraphTouched) {
                $paragraphs.Add([pscustomobject]@{ Lines = $paragraph.ToArray() })
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
        $paragraphs.Add([pscustomobject]@{ Lines = $paragraph.ToArray() })
    }
    return $paragraphs
}

function Get-LeadingWhiteSpace([string]$Text) {
    return $Text.Substring(0, $Text.Length - $Text.TrimStart().Length)
}

# The lines a paragraph is rewritten to: its tokens filled greedily to 120 columns. A single-line /** ... */ comment
# becomes a three-part block. Returns $null when the tokens do not cover the paragraph's text, so a reflow cannot
# drop any of it.
function Get-FilledLines([object[]]$Paragraph, [bool]$IsTypeScript, [string]$Indent) {
    $token = if ($IsTypeScript) { $tsToken } else { $csToken }
    $words = [System.Collections.Generic.List[string]]::new()
    foreach ($record in $Paragraph) {
        $covered = [System.Text.StringBuilder]::new()
        foreach ($match in $token.Matches($record.Text)) {
            $words.Add($match.Value)
            $null = $covered.Append($match.Value)
        }
        if (($covered.ToString() -replace '\s', '') -cne ($record.Text -replace '\s', '')) {
            return $null
        }
    }
    if ($words.Count -eq 0) {
        return $null
    }

    $first = $Paragraph[0]
    $single = $first.Single
    $firstIndent = Get-LeadingWhiteSpace $first.Text
    $firstPrefix = if ($single) { "$Indent * " } else { $first.Prefix }
    if ($Paragraph.Length -gt 1) {
        $continuation = $Paragraph[1].Prefix + (Get-LeadingWhiteSpace $Paragraph[1].Text)
    }
    else {
        $continuation = if ($single) { "$Indent * " } else { $first.Continuation }
        $continuation += $firstIndent
        if ($IsTypeScript -and $first.Text.TrimStart() -cmatch '^(?:[-*+] |\d+[.)] )') {
            $continuation += '  '
        }
    }
    if (-not $single) {
        $words[$words.Count - 1] += $Paragraph[$Paragraph.Length - 1].Suffix
    }

    $filled = [System.Collections.Generic.List[string]]::new()
    if ($single) {
        $filled.Add("$Indent/**")
    }
    $current = $firstPrefix + $firstIndent
    $isEmpty = $true
    foreach ($word in $words) {
        if ($isEmpty) {
            $current += $word
            $isEmpty = $false
        }
        elseif ($current.Length + 1 + $word.Length -le $maxWidth) {
            $current += ' ' + $word
        }
        else {
            $filled.Add($current)
            $current = $continuation + $word
        }
    }
    $filled.Add($current)
    if ($single) {
        $filled.Add("$Indent */")
    }
    return , $filled.ToArray()
}

# The file's lines with every touched paragraph that has a problem refilled, or $null when no line changes.
function Get-RepairedLines([string[]]$Lines, [object[]]$Paragraphs, [bool]$IsTypeScript) {
    $repaired = [System.Collections.Generic.List[string]]::new()
    $position = 0
    foreach ($paragraph in $Paragraphs) {
        if (@(Get-ParagraphProblems $paragraph.Lines $IsTypeScript).Count -eq 0) {
            continue
        }
        $start = $paragraph.Lines[0].Index
        $end = $paragraph.Lines[$paragraph.Lines.Length - 1].Index
        $filled = Get-FilledLines $paragraph.Lines $IsTypeScript (Get-LeadingWhiteSpace $Lines[$start])
        if ($null -eq $filled -or ($filled -join "`n") -ceq ($Lines[$start..$end] -join "`n")) {
            continue
        }
        for ($n = $position; $n -lt $start; $n++) {
            $repaired.Add($Lines[$n])
        }
        $repaired.AddRange([string[]]$filled)
        $position = $end + 1
    }
    if ($position -eq 0) {
        return $null
    }
    for ($n = $position; $n -lt $Lines.Length; $n++) {
        $repaired.Add($Lines[$n])
    }
    return , $repaired.ToArray()
}

# A file's text, lines, doc-line records and touched lines, or $null when it holds no doc line.
function Read-DocFile([string]$FullPath, [string]$RelativePath, [bool]$IsTypeScript, [bool]$AllLines, [string]$MergeBase) {
    $text = [IO.File]::ReadAllText($FullPath)
    if ($text.IndexOf($(if ($IsTypeScript) { '/**' } else { '///' }), [StringComparison]::Ordinal) -lt 0) {
        return $null
    }
    $lines = [regex]::Split($text, '\r?\n')
    $records = @(if ($IsTypeScript) { Get-TypeScriptDocLines $lines } else { Get-CSharpDocLines $lines })
    if ($records.Count -eq 0) {
        return $null
    }

    # $null marks every line as touched; otherwise a 1-based flag per line.
    $touched = $null
    if (-not $AllLines) {
        $touched = [bool[]]::new($lines.Length + 2)
        $diff = @(Invoke-Git -Arguments @('diff', '-U0', '--no-color', '--no-renames', $MergeBase, '--', $RelativePath) `
            -Failure "git diff of '$RelativePath' failed.")
        foreach ($diffLine in $diff) {
            $header = $hunkHeader.Match($diffLine)
            if (-not $header.Success) {
                continue
            }
            $start = [int]$header.Groups[1].Value
            $count = if ($header.Groups[2].Success) { [int]$header.Groups[2].Value } else { 1 }
            # A pure deletion (+N,0) leaves no line of its own: it touches the lines on either side of it, N and N + 1,
            # so a paragraph it shortens is judged again.
            if ($count -eq 0) {
                foreach ($n in @($start, ($start + 1))) {
                    if ($n -ge 1 -and $n -le $lines.Length) {
                        $touched[$n] = $true
                    }
                }
                continue
            }
            for ($n = $start; $n -lt $start + $count -and $n -lt $touched.Length; $n++) {
                $touched[$n] = $true
            }
        }
    }

    return [pscustomobject]@{
        Text    = $text
        Lines   = $lines
        Records = $records
        Touched = $touched
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
    $state = Read-DocFile $fullPath $relative $isTypeScript $candidates[$relative] $mergeBase
    if ($null -eq $state) {
        continue
    }

    if ($Fix) {
        $paragraphs = @(Get-TouchedParagraphs $state.Records $isTypeScript $state.Touched)
        $repaired = Get-RepairedLines $state.Lines $paragraphs $isTypeScript
        if ($null -ne $repaired) {
            $newline = if ($state.Text.Contains("`r`n", [StringComparison]::Ordinal)) { "`r`n" } else { "`n" }
            [IO.File]::WriteAllText($fullPath, ($repaired -join $newline), [System.Text.UTF8Encoding]::new($false))
            $state = Read-DocFile $fullPath $relative $isTypeScript $candidates[$relative] $mergeBase
            if ($null -eq $state) {
                continue
            }
        }
    }

    foreach ($paragraph in @(Get-TouchedParagraphs $state.Records $isTypeScript $state.Touched)) {
        foreach ($problem in @(Get-ParagraphProblems $paragraph.Lines $isTypeScript)) {
            $problems.Add("${relative}($($problem.Index + 1)): $($problem.Message)")
        }
    }
}

$problems
