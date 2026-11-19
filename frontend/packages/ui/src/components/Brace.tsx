import { useId } from 'react';
import brace from '@ncodegroup/identity-brand/brace.json';

// The height of the clip rectangle that keeps the top stem below its hook; it only has to outrun any card's height.
const CLIP_HEIGHT = 100000;

/**
 * A stretching brace's size in NCode Brand's brace data: `sm` for a listing, `md` for a card on a phone, `lg` for a
 * card or a workspace.
 */
export type BraceSize = Exclude<keyof typeof brace, '$description' | 'mark'>;

export type BraceProps = {
  /** The size whose hooks, cusp and stems the brace draws. */
  readonly size: BraceSize;
};

/**
 * NCode Brand's left brace, drawn inline from its brace data at the full height of its container in `currentColor`. Its
 * hooks and cusp keep their size and its stems take the rest of the height; mirror it with `scaleX(-1)` for the right
 * side. It is decoration, hidden from assistive technology.
 */
export function Brace({ size }: BraceProps) {
  const { width, top, cusp, bottom, stem } = brace[size];
  const id = useId().replace(/[^A-Za-z0-9_-]/g, '');
  const hook = stem.hookReach - 0.5;
  const reach = stem.cuspReach - 0.5;
  return (
    <svg width={width} height="100%" fill="currentColor" aria-hidden="true" focusable="false">
      <defs>
        <clipPath id={`${id}-a`}>
          <rect x="0" y={hook} width={width} height={CLIP_HEIGHT} />
        </clipPath>
        <clipPath id={`${id}-b`}>
          <rect
            x="0"
            y="0"
            width={width}
            height="100%"
            transform={`translate(0 ${String(-hook)})`}
          />
        </clipPath>
      </defs>
      <path d={top} />
      <g clipPath={`url(#${id}-a)`}>
        <rect
          x={stem.x}
          y="0"
          width={stem.width}
          height="50%"
          transform={`translate(0 ${String(-reach)})`}
        />
      </g>
      <svg y="50%" overflow="visible">
        <path d={cusp} />
      </svg>
      <g clipPath={`url(#${id}-b)`}>
        <rect
          x={stem.x}
          y="50%"
          width={stem.width}
          height="50%"
          transform={`translate(0 ${String(reach)})`}
        />
      </g>
      <svg y="100%" overflow="visible">
        <path d={bottom} />
      </svg>
    </svg>
  );
}
