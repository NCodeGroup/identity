---
description: "NCode.Identity PowerShell conventions — the build scripts under build/ and their fixture tests."
applyTo: "**/*.ps1"
---

# PowerShell scripts

The build scripts under `build/` are PowerShell 7 and run under `Set-StrictMode -Version Latest` with
`$ErrorActionPreference = 'Stop'`.

- **An `if`, `switch` or loop used as a value unrolls its output.** An empty array it yields becomes `$null`, and a
  single item becomes that item, so `$x.Count` throws under strict mode. Wrap the whole statement in `@(...)` when the
  value must be an array: `$projects = @(if ($all) { $every } else { $selected })`. Wrapping each branch is not enough.
- **An argument to a native command expands only a bare variable.** `gh api -f body=$replies[$id]` passes the
  dictionary's type name followed by `[$id]`, because `[...]` and `.Member` stay literal text. Pass an indexed or member
  value as a subexpression: `-f "body=$($replies[$id])"`.
- **A script checks its prerequisites after it knows it has work.** A run that selects nothing, such as
  `./build/e2e.ps1 -Affected` with no suite affected, reports its verdict without requiring Docker or a build.
- **A prerequisite check tells a missing tool from a broken one.** `Get-Command` alone decides that a tool is not on
  `PATH`; a check that runs the tool captures its output (`@(pnpm --version 2>&1)`) and puts its first non-empty line in
  the thrown message, so a broken install is reported with its own error, never as a missing one (`build/e2e.ps1`'s pnpm
  check).
- **A script's fixture test** lives under `build/tests/` as `<script>.tests.ps1`, and `build/dod.ps1` runs it as an
  early, build-free stage. A fixture is a temporary directory, and a git repository when the script reads git, that the
  test deletes in a `finally` block. Run a script whose `return` or `throw` the test asserts in a child
  `pwsh -NoProfile -File` process.
- **A child's error message wraps at its console's width.** PowerShell's error view breaks a thrown message at spaces to
  fit the console and starts each continuation line with a `|` gutter, so a fixture test compares a child run's output
  with the gutters removed and each whitespace run collapsed to one space (`Get-ComparableText` in
  `build/tests/e2e.tests.ps1`), never with `Contains` on the raw output.
