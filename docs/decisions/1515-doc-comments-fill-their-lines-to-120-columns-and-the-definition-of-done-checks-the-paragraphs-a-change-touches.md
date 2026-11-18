# 1515. Doc comments fill their lines to 120 columns, and the Definition of Done checks the paragraphs a change touches

- **Status:** Accepted
- **Date:** 2026-11-16
- **Deciders:** NCode Group

## Context

A doc comment is prose that a formatter leaves alone, and its width and fill are a recurring finding in review. Four
forces shape the rule:

- **Review alone enforces it.** Review rounds on pull request
  [#851](https://github.com/NCodeGroup/identity/pull/851) spent many findings on the width and the fill of doc
  comments, work a tool can decide.
- **No formatter rewraps a comment.** CSharpier and Prettier format code at 100 columns and leave comments as written.
- **The frontend's dominant comment width is 120.** More frontend files wrap their comments at 120 columns than at any
  other width, and 120 is the width the C# rule names.
- **The tree holds lines no check has seen.** About 2,258 `///` lines are under-filled and about 1,804 run past 120
  columns, so a gate over the whole tree fails on lines no change touches.

## Decision

**A doc comment fills its lines to 120 columns, and the Definition of Done checks the paragraphs a change adds or
edits.**

1. **One width, 120, for C# `///` and TypeScript JSDoc.** Each paragraph fills its lines with as many whole words as
   fit, and no line runs past 120 columns. A `{@link …}` and a backtick code span count as one word, and a code block
   is neither checked nor reflowed. Two C# forms may run past 120 columns on one line: the standard
   `<param name="cancellationToken">` sentence, which reads the same on every member, and a `<response>` element, whose
   text is the response description in the served OpenAPI document.
2. **`build/doc-comment-fill.ps1` checks the paragraphs a change adds or edits.** It measures from the merge base of
   `HEAD` and `-Base`, and `-Fix` reflows the paragraphs it reports. `-All` checks every paragraph in scope.
3. **The Definition of Done runs it as a build-free stage.** The `doc comment fill` stage of `build/dod.ps1` runs the
   script after its own fixture tests.
4. **`/audit-conventions` runs `-All`,** so the paragraphs no change has touched surface in an audit, not in review.
5. **Markdown prose fills by the same rule, and review checks it.** A paragraph, list item or blockquote in a `.md` file
   fills its lines to 120 columns, with a link or a code span as one word, and a link longer than the room a line has
   sits alone on its line. A heading, a table row, a fenced code block, the front matter and a hard break keep their
   lines, and a file the project template owns keeps the template's layout. No gate checks the fill of Markdown:
   Prettier, which the frontend gate runs over `frontend`'s Markdown, keeps prose line breaks as written, and the tree
   holds paragraphs that no change has refilled. `.github/instructions/markdown.instructions.md` holds the rule.

## Options considered

- **Review only.** Rejected: every round spends findings on a rule a tool decides.
- **Gate the whole tree.** Rejected: the gate would fail on thousands of lines outside any change, so the rule is
  checked where a change touches a paragraph and the rest is refilled by a follow-up.
- **100 columns for the frontend.** Rejected: C# and TypeScript would hold two widths, and most frontend comments sit
  at 120.
- **An analyzer or an ESLint rule.** Rejected: none fills a comment, and C# and TypeScript would need two rules.

## Consequences

- A change refills the paragraphs it touches, and `-Fix` does it without a hand edit.
- The gate needs the merge base, so it runs on a checkout with full history; every workflow that runs the gate checks
  out with `fetch-depth: 0`.
- The paragraphs no change touches stay as written until a follow-up refills them, and `/audit-conventions` lists
  them.
- A code block is neither checked nor reflowed, so its author keeps a sample's lines within 120 columns by hand.

## References

- `build/doc-comment-fill.ps1`
- `build/dod.ps1`
- `.github/instructions/csharp-production.instructions.md` §10
- `.github/instructions/frontend.instructions.md`
- `.github/instructions/markdown.instructions.md`
