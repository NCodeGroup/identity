const HEX_COLOR = /^#(?:[0-9a-f]{6}|[0-9a-f]{3})$/i;

/**
 * The `rgb(r, g, b)` form a browser reports for a `#rrggbb` color, so a test compares a computed style with a theme
 * color.
 */
export function cssRgb(hex: string): string {
  if (!HEX_COLOR.test(hex)) {
    throw new RangeError(`Expected a #rrggbb or #rgb color, got '${hex}'.`);
  }
  const digits = hex.length === 4 ? [...hex.slice(1)].map((d) => d + d).join('') : hex.slice(1);
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(digits.slice(i, i + 2), 16));
  return `rgb(${String(r)}, ${String(g)}, ${String(b)})`;
}
