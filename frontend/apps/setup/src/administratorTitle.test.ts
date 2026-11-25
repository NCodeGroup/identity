import { describe, expect, it } from 'vitest';
import { administratorTitle } from './administratorTitle.js';

describe('administratorTitle', () => {
  it('names a cell administrator for CellAdmin', () => {
    expect(administratorTitle('CellAdmin')).toBe('cell administrator');
  });

  it.each(['GlobalAdmin', 'Other'])('names a root administrator for %s', (role) => {
    expect(administratorTitle(role)).toBe('root administrator');
  });
});
