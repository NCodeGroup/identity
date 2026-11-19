import { brandFontsLoading } from '@ncodegroup/identity-test-support';
import { describe, expect, it } from 'vitest';
import entry from './main.tsx?raw';

describe("the brand's typefaces", () => {
  it('are loaded once, by the first import of the entry', () => {
    expect(brandFontsLoading(entry)).toEqual({ imports: 1, first: true });
  });
});
