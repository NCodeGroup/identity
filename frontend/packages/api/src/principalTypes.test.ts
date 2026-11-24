import { describe, expect, it } from 'vitest';
import { PRINCIPAL_TYPES } from './principalTypes.js';

describe('PRINCIPAL_TYPES', () => {
  it('names the principal types of the control plane', () => {
    expect(PRINCIPAL_TYPES).toEqual({
      localAccount: 'local_account',
      client: 'client',
      federatedUser: 'federated_user',
    });
  });
});
