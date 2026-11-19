# 1516. The frontends are themed with NCode Brand's Instrument design system, taken from a vendored release

- **Status:** Accepted
- **Date:** 2026-11-18
- **Deciders:** NCode Group

## Context

NCode Brand publishes Instrument, its design system, as data that a Material UI theme is built from:
`mui/instrument.json`, and `tokens/derive.mjs` with its `derive.d.mts` and `derive.vectors.json`. It leaves the theme's
code to the application
([`guidelines/12-applying.md`](https://github.com/NCodeGroup/brand/blob/v0.11.0/guidelines/12-applying.md)).

A theme built from Material UI's defaults passes a tenant's color through unchecked. A tenant that sets `#FACC15` gets
yellow text on a white page at a contrast of 1.5 to 1, where WCAG 2.2 AA asks for 4.5 to 1. A frontend build and the
host images read only `frontend/`
([ADR-1248](1248-the-host-console-and-directory-images-are-chiseled-non-root-multi-arch-digest-pinned-and-log-one-line-json.md)),
so a theme cannot read `brand/` at the repository root.

Instrument says that a page without a mode follows the reader's system setting, and that reduced motion stops all
motion.

The release also carries Instrument's two typefaces, Instrument Sans and Martian Mono, as `css/fonts.css` and four WOFF2
files under `fonts/`, each family with its SIL Open Font License 1.1 text, which asks every copy of a font to carry its
license. Every page the hosts serve starts from `default-src 'none'`
([ADR-1256](1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md)), so
a page loads a font only from a source its policy lists.

`brand/` vendors the logo kit from one NCodeGroup/brand release, under `brand/brand.lock.json`
([ADR-1492](1492-the-familys-id-logo-is-vendored-from-an-ncodegroup-brand-release-and-every-web-app-shows-its-icon.md)).

## Decision

**Every frontend is themed with NCode Brand's Instrument: its Material UI data, its tenant color derivation and its two
typefaces come from the NCodeGroup/brand release the logo kit pins, vendored into `frontend/packages/brand` and pinned
by SHA-256, and `createAppTheme` builds each surface's theme from them.**

### The vendored release

1. **The package.** `@ncodegroup/identity-brand` is private. Its files sit at their release paths under `vendor/`
   (`mui/instrument.json`, `tokens/derive.mjs`, `tokens/derive.d.mts`, `tokens/derive.vectors.json`, `brace/brace.json`,
   `css/fonts.css`, `fonts/*.woff2` and `fonts/OFL-*.txt`), and its `brand.lock.json` sits at its root. It holds no
   code, scripts or tests of its own, and its `exports` map names the files the frontend reads. Prettier and ESLint skip
   `vendor/` and the lock, because a reformatted byte fails its SHA-256.
2. **One release.** `build/brand-kit.ps1`, which the `brand kit` stage of `./build/dod.ps1` runs, checks both locks
   with `brand/vendor-brand.mjs --check` and fails when they name different releases. `pnpm run check:brand` runs in
   the frontend job after the format check, because a push that changes only `frontend/` runs only the frontend
   workflow. The frontend lock does not name `vendor-brand.mjs`.
3. **A chunk of its own.** The vendored files load in a `brand` chunk, because, like a package's chunk, they change only
   with a brand release. The typefaces' stylesheet is the chunk's `brand-<hash>.css`.

### The theme

4. **One derivation.** `createAppTheme(branding?, options?)` always calls `deriveBrand(primaryColor, secondaryColor)`.
   The result of a call without input is NCode's own colors, so NCode and a tenant share one mapping.
5. **The palette.** `primary` takes `primary`, `primary-hover` and `on-primary` from the derivation. `secondary` takes
   `screen`, `screen-raised` and `on-screen` in the light mode, and `screen-lit`, `on-screen-secondary` and `screen` in
   the dark mode. The status colors, the background, the text and the divider come from Instrument. `palette.instrument`
   holds Instrument's roles overlaid with the derived colors.
6. **The mode.** The mode is `branding.mode`, else the reader's `prefers-color-scheme`, else light. `CssBaseline` sets
   `enableColorScheme`.
7. **Reduced motion.** Under `prefers-reduced-motion: reduce` every duration is 0, `transitions.create` returns
   `none`, and `CssBaseline` stops every CSS animation and transition.
8. **Typography.** The typography comes from `instrument.typography`. `typography.fontFamilyMono` and the `code`
   variant set code, and a module augmentation types them. `variantMapping` maps `code` to `<code>`, and `label`,
   `field`, `value` and `readout` to `<span>`. Every code value is set in the `code` variant: `variant="code"` on a
   `Typography`, `typography: 'code'` in `sx`, or on a field's input through `inputBaseClasses.input`, so the field's
   label keeps its style; the playground's sensitive `CodeBlock` sits on `warning-subtle` with a `warning-border` edge.
9. **Measures.** The radius is 6 and the spacing is 8. The theme holds the mode's 25 shadows and
   `theme.instrument = { radius, space, size, focus }`.
10. **Motion.** The durations are `shortest` 70, `shorter` and `short` 120, `standard` 200, `complex` and
    `enteringScreen` 280, and `leavingScreen` 200. The easings are `easeOut` and `easeInOut`.
11. **Breakpoints.** `xs` is 0, `sm` 641, `md` 1089, `lg` 1281 and `xl` 1536, so `down('sm')` ends at 640 px.
12. **The parts.** The theme draws Instrument's parts with Material UI's components: a key stands on a lip of its edge
    color and travels down 2 px when pressed (a contained key, a plain outlined key on the control face, a ghost key as
    the text variant), with no ripple; a disabled key is sunken and unlit; a well is cut in with a shade under its top
    edge, and its field's label stands above it in sentence case, never inside it; a card is a plate with the module's
    radius, and a sheet takes no elevation overlay; a dialog dims the page with the scrim and is a sheet on a phone; a
    tooltip is a small screen; a table's head is engraved on a sunken ground; an alert has a signal bar in its role's
    mark and its title in the role's text color, and a caution's bar is hazard tape; a non-production tenant's banner is
    a hazard band, its text on the hazard color between two runs of tape (`HazardBand`). The focus ring is 2 px of
    `focus`, 2 px off the part, and the brand as text is `primary-text` (links, and text and outlined primary keys, with
    `primary-edge` as the outlined border); a key whose ink is `primary-text` is tinted with `primary-subtle` when
    touched, because `deriveBrand` holds `primary-text` at 4.5 to 1 on it.
13. **Storybook.** The toolbar has the globals `mode` and `tenant`.

### The brace, the watch and the fonts

14. **The brace.** `Brace` draws NCode Brand's stretching brace inline from `brace/brace.json`, so it needs no image
    source, and `TenantCard` sets the sign-in, consent and authorization error cards as raised plates between two
    braces in the tenant's `scope-tenant` ink, `brace-lg` and `brace-md` on a phone.
15. **The watch.** `build/brand-watch.ps1` and the weekly `brand-watch` workflow keep one `brand-update` issue open
    while a release that is at least three days old is newer than the one the locks pin, and close it once the locks
    pin that release.
16. **The fonts.** Each app imports `@ncodegroup/identity-brand/fonts.css` once, first in `src/main.tsx`. Its build
    emits the four font files into `assets/` under content-hashed names beside the stylesheet, whose link carries the
    page's nonce, and the preset's `emitFontLicenses()` emits `OFL-Instrument-Sans-<hash>.txt` and
    `OFL-Martian-Mono-<hash>.txt` beside them, resolved from the app's own dependency on the brand package, so a build
    of an app without it fails. The interactive, setup and management console pages list `font-src 'self'`, and their
    hosts serve the fonts as `font/woff2` and the licenses as `text/plain` from the bundle's assets route.
    `NCode.Identity.Frontend.EndToEnd.Tests` opens the login, setup and management console pages in Chromium, Firefox
    and WebKit and fails when a typeface does not load from the page's own assets, the page reports a policy violation,
    or its host does not serve both licenses from those assets as `text/plain`
    ([ADR-1430](1430-end-to-end-suites-run-the-bff-and-the-hosts-built-pages-in-real-browsers-in-their-own-gate.md)).

## Options considered

- **Values copied into `createAppTheme` by hand.** Rejected: they drift from Instrument, and no test vector proves
  them.
- **`tokens/tokens.css` custom properties.** Rejected: Material UI reads values in JavaScript, and CSS cannot raise a
  color that falls short of its contrast floor, which `derive.mjs` does.
- **A light theme for a branding without a mode.** Rejected: Instrument follows the reader's system setting.
- **Reading the repository's `brand/` from the frontend.** Rejected: the image builds' context holds only `frontend/`.
- **A release of its own for the theme.** Rejected: two versions of one brand drift apart.
- **The braces as CSS masks of the release's SVG files.** Rejected: a bundler inlines a small file as a `data:`
  address, which the pages' `img-src` refuses, and an inline SVG needs no image at all.
- **Fontsource packages from npm for the typefaces.** Rejected: a second pin of the same files, which can drift from the
  files NCode Brand measured its type with.
- **Fonts from a font CDN.** Rejected: every sign-in page would call a third party, and `font-src` would name a third
  origin.
- **The licenses only in `THIRD-PARTY-NOTICES.md`.** Rejected: the images ship the fonts, not that file.
- **The licenses under fixed names.** Rejected: the hosts serve every asset as immutable for a year, which holds only
  for a content-hashed name.
- **The chosen design.** One release feeds both the logo kit and the theme, the files are vendored byte for byte and
  checked by SHA-256, and one derivation builds every surface's palette, so a tenant's colors hold their contrast
  floors in both modes.

## Consequences

- Any stored tenant color renders at WCAG 2.2 AA contrast in both modes.
- Another look is an NCode Brand release, taken with `--update` for both locks and reviewed as a diff.
- A hand edit under `vendor/` fails `check:brand` and the Definition of Done.
- A release is taken once it is three days old
  ([project-template ADR-0009](project-template/0009-new-dependency-versions-wait-a-cooldown.md)).
- A value that Instrument does not export is added in NCode Brand first.
- Every app ships four font files, 139 kB in all, of which a browser fetches only those whose `unicode-range` a page
  uses, and two license texts.

## References

- [`frontend/packages/brand/brand.lock.json`](../../frontend/packages/brand/brand.lock.json) — the lock of the vendored
  theme files.
- [`frontend/packages/ui/src/theme/createAppTheme.ts`](../../frontend/packages/ui/src/theme/createAppTheme.ts) — the
  theme.
- [`frontend/packages/ui/src/components/TenantCard.tsx`](../../frontend/packages/ui/src/components/TenantCard.tsx) — the
  tenant's card between two braces.
- [`frontend/packages/ui/src/components/HazardBand.tsx`](../../frontend/packages/ui/src/components/HazardBand.tsx) — the
  hazard band.
- [`build/brand-kit.ps1`](../../build/brand-kit.ps1) and [`build/brand-watch.ps1`](../../build/brand-watch.ps1) — the
  check of both locks and the weekly watch.
- [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md) — the typefaces' notices.
- NCode Brand's
  [`guidelines/12-applying.md`](https://github.com/NCodeGroup/brand/blob/v0.11.0/guidelines/12-applying.md) — applying
  Instrument.
- Issue [#861](https://github.com/NCodeGroup/identity/issues/861)
- Issue [#862](https://github.com/NCodeGroup/identity/issues/862)
- [ADR-1064](1064-one-frontend-platform-of-shared-packages-and-thin-app-shells-serves-every-web-surface.md)
- [ADR-1151](1151-every-frontend-workspace-builds-and-tests-from-one-preset-that-fails-on-warnings-and-oversized-chunks.md)
- [ADR-1248](1248-the-host-console-and-directory-images-are-chiseled-non-root-multi-arch-digest-pinned-and-log-one-line-json.md)
- [ADR-1253](1253-a-tenant-brands-its-sign-in-pages-through-settings-and-the-bundle-mounts-over-the-hosts-page.md)
- [ADR-1256](1256-every-page-the-server-writes-has-a-strict-content-security-policy-and-is-framed-only-by-design.md)
- [ADR-1430](1430-end-to-end-suites-run-the-bff-and-the-hosts-built-pages-in-real-browsers-in-their-own-gate.md)
- [ADR-1492](1492-the-familys-id-logo-is-vendored-from-an-ncodegroup-brand-release-and-every-web-app-shows-its-icon.md)
- [project-template ADR-0008](project-template/0008-external-inputs-are-pinned-and-kept-current.md)
- [project-template ADR-0009](project-template/0009-new-dependency-versions-wait-a-cooldown.md)
