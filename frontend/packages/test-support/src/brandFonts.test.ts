import { describe, expect, it } from 'vitest';
import { BRAND_FONTS_IMPORT, brandFontsLoading } from './brandFonts.js';

describe('brandFontsLoading', () => {
  const lines = ['// Loads the typefaces.', BRAND_FONTS_IMPORT, '', "import { x } from 'y';"];

  it('reads an entry that loads the typefaces once, first', () => {
    const entry = lines.join('\n');

    expect(brandFontsLoading(entry)).toEqual({ imports: 1, first: true });
  });

  it('reads an entry whose first import is another module', () => {
    const entry = ["import { x } from 'y';", BRAND_FONTS_IMPORT].join('\n');

    expect(brandFontsLoading(entry)).toEqual({ imports: 1, first: false });
  });

  it('counts every import of the typefaces', () => {
    const entry = [BRAND_FONTS_IMPORT, BRAND_FONTS_IMPORT].join('\n');

    expect(brandFontsLoading(entry)).toEqual({ imports: 2, first: true });
  });

  it('reads an entry that never loads the typefaces', () => {
    const entry = "import { x } from 'y';";

    expect(brandFontsLoading(entry)).toEqual({ imports: 0, first: false });
  });

  it('reads an entry with Windows line endings', () => {
    const entry = lines.join('\r\n');

    expect(brandFontsLoading(entry)).toEqual({ imports: 1, first: true });
  });
});
