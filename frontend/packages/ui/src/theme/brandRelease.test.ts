import { describe, expect, it } from 'vitest';
import { deriveBrand, version } from '@ncodegroup/identity-brand/derive';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import vectors from '@ncodegroup/identity-brand/derive.vectors.json';

describe('the vendored NCode Brand release', () => {
  it.each(vectors.vectors)(
    'derives "$name" as the release does',
    ({ primary, secondary, expected }) => {
      expect(deriveBrand(primary, secondary)).toEqual(expected);
    },
  );

  it('is one release for the theme data, the derivation and its vectors', () => {
    expect(version).toBe(vectors.version);
    expect(instrument.version).toBe(vectors.version);
  });
});
