import { describe, expect, it } from 'vitest';
import { cssRgb } from './cssColor.js';

describe('cssRgb', () => {
  it('reports a #rrggbb color in the rgb form a browser uses', () => {
    expect(cssRgb('#F4AD1D')).toBe('rgb(244, 173, 29)');
    expect(cssRgb('#0a0a08')).toBe('rgb(10, 10, 8)');
  });

  it('expands a #rgb color', () => {
    expect(cssRgb('#fa0')).toBe('rgb(255, 170, 0)');
  });

  it.each(['', 'F4AD1D', '#F4AD1', '#F4AD1DFF', '#GGGGGG', 'rgb(1, 2, 3)'])(
    'throws a RangeError naming %j',
    (input) => {
      expect(() => cssRgb(input)).toThrow(RangeError);
      expect(() => cssRgb(input)).toThrow(`'${input}'`);
    },
  );
});
