import { describe, expect, it } from 'vitest';
import { isSameRegion } from './cellRegion.js';

describe('isSameRegion', () => {
  it('matches a region with itself', () => {
    expect(isSameRegion('eu', 'eu')).toBe(true);
  });

  it('does not match two different regions', () => {
    expect(isSameRegion('eu', 'us')).toBe(false);
  });

  it.each([
    ['null', null],
    ['undefined', undefined],
  ])('does not match a known region with %s on the other side', (_name, unknown) => {
    expect(isSameRegion('eu', unknown)).toBe(false);
    expect(isSameRegion(unknown, 'eu')).toBe(false);
  });

  it.each([
    ['null and null', null, null],
    ['undefined and undefined', undefined, undefined],
    ['null and undefined', null, undefined],
    ['undefined and null', undefined, null],
  ])('does not match two regions that are not known: %s', (_name, region, other) => {
    expect(isSameRegion(region, other)).toBe(false);
  });
});
