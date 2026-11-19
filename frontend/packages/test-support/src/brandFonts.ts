/** The line that loads NCode Brand's two typefaces, which every web app's entry holds once, as its first import. */
export const BRAND_FONTS_IMPORT = "import '@ncodegroup/identity-brand/fonts.css';";

/** How an entry module loads NCode Brand's typefaces. */
export interface BrandFontsLoading {
  /** The number of the entry's lines that are {@link BRAND_FONTS_IMPORT}. */
  readonly imports: number;
  /** Whether the entry's first import line is {@link BRAND_FONTS_IMPORT}. */
  readonly first: boolean;
}

/**
 * Reads how an entry module's source loads NCode Brand's typefaces. Every web app imports them once, as the first
 * import of `src/main.tsx` (ADR-1516).
 */
export function brandFontsLoading(entry: string): BrandFontsLoading {
  const imports = entry.split(/\r?\n/).filter((line) => line.startsWith('import '));
  return {
    imports: imports.filter((line) => line === BRAND_FONTS_IMPORT).length,
    first: imports[0] === BRAND_FONTS_IMPORT,
  };
}
