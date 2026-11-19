import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import brace from '@ncodegroup/identity-brand/brace.json';
import { Brace } from './Brace.js';

describe('Brace', () => {
  it("draws the release's md brace", () => {
    const { container } = render(<Brace size="md" />);
    const svg = container.querySelector('svg');

    expect(svg).toHaveAttribute('width', '17');
    expect(svg).toHaveAttribute('aria-hidden', 'true');
    expect(Array.from(container.querySelectorAll('path'), (p) => p.getAttribute('d'))).toEqual([
      brace.md.top,
      brace.md.cusp,
      brace.md.bottom,
    ]);
    expect(container.querySelector('clipPath rect')).toHaveAttribute('y', '7.5');
    expect(
      Array.from(container.querySelectorAll('g > rect'), (r) => r.getAttribute('transform')),
    ).toEqual(['translate(0 -6.25)', 'translate(0 6.25)']);
  });

  it("draws the release's lg brace", () => {
    const { container } = render(<Brace size="lg" />);

    expect(container.querySelector('svg')).toHaveAttribute('width', '23');
    expect(container.querySelector('clipPath rect')).toHaveAttribute('y', '10.5');
    expect(
      Array.from(container.querySelectorAll('g > rect'), (r) => r.getAttribute('transform')),
    ).toEqual(['translate(0 -8.5)', 'translate(0 8.5)']);
  });

  it('gives each brace clip paths of its own', () => {
    const { container } = render(
      <>
        <Brace size="md" />
        <Brace size="lg" />
      </>,
    );
    const ids = Array.from(container.querySelectorAll('clipPath'), (c) => c.id);

    expect(ids).toHaveLength(4);
    expect(new Set(ids).size).toBe(4);
    for (const svg of Array.from(container.querySelectorAll(':scope > svg'))) {
      const own = Array.from(svg.querySelectorAll('clipPath'), (c) => c.id);
      const used = Array.from(svg.querySelectorAll('g[clip-path]'), (g) =>
        (g.getAttribute('clip-path') ?? '').replace(/^url\(#(.*)\)$/, '$1'),
      );
      expect(used).toHaveLength(2);
      expect(own).toEqual(expect.arrayContaining(used));
    }
  });
});
