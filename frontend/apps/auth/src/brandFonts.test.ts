import { describe, expect, it } from 'vitest';
import entry from './main.tsx?raw';

describe("the brand's typefaces", () => {
  it('are loaded once, from the entry', () => {
    expect(entry.match(/^import '@ncodegroup\/identity-brand\/fonts\.css';$/gm)).toHaveLength(1);
  });
});
