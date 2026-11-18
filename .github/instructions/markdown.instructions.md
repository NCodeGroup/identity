---
description: "NCode.Identity Markdown conventions — how the prose of the repository's Markdown files fills its lines."
applyTo: "**/*.md"
---

# Markdown

- 👁 **Prose fills its lines to 120 columns, as a doc comment does.** A paragraph, list item or blockquote fills each
  line with as many whole words as fit within 120 columns and keeps its prefix on every line: a list item's marker and
  continuation indent, a blockquote's `> `. A link `[text](target)` and a backtick code span each count as one word,
  together with any punctuation joined to them, so a link longer than the room a line has sits alone on its line. A
  heading, a table row, a fenced code block, the front matter and a hard break (two trailing spaces) keep their lines;
  outside them, only a line that holds such a link alone runs past 120 columns.
  [ADR-1515](../../docs/decisions/1515-doc-comments-fill-their-lines-to-120-columns-and-the-definition-of-done-checks-the-paragraphs-a-change-touches.md)
  decides this rule, which `./build/doc-comment-fill.ps1` checks in doc comments; no gate checks Markdown.
- 👁 **A change refills each paragraph it edits, and only those.** A name or a sentence that lengthens or shortens a
  line refills its whole paragraph, so no line keeps room for the next line's first word; a paragraph the change does
  not edit keeps its lines. The files the project template owns, which `AGENTS.md` lists, keep the template's layout.
