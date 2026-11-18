---
description: "NCode.Identity frontend conventions — the TypeScript and TSX files under frontend/."
applyTo: "frontend/**/*.{ts,tsx}"
---

# Frontend conventions

- ✅ **A JSDoc comment fits within 120 columns.** Each paragraph fills its lines with as many whole words as fit, a
  `{@link …}` and a backtick code span counting as one word. Prettier's `printWidth` of 100 governs code and never
  rewraps comments, so the width is held by the author or by `./build/doc-comment-fill.ps1 -Fix`. The `doc comment fill`
  stage of [`build/dod.ps1`](../../build/dod.ps1) checks the paragraphs that a change adds or edits. See
  [ADR-1515](../../docs/decisions/1515-doc-comments-fill-their-lines-to-120-columns-and-the-definition-of-done-checks-the-paragraphs-a-change-touches.md).
- 👁 **A `//` comment fills its lines to 120 columns too.** A paragraph of `//` lines at one indent fills each line
  with as many whole words as fit within 120 columns, like a JSDoc paragraph. Prettier never rewraps a comment, and
  no stage of the Definition of Done checks a `//` comment, so the author or the reviewer holds the width.
