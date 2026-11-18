import { describe, it } from 'vitest';

import { managementApi } from './managementApi.js';

describe('managementApi cell-addressed endpoints', () => {
  it('require the cell as an argument', () => {
    // @ts-expect-error a cell-addressed call must name its cell, or `undefined` for the control plane
    managementApi.endpoints.getTenant.initiate({ tenantId: 'acme' });

    managementApi.endpoints.getTenant.initiate({ tenantId: 'acme', cell: undefined });
  });
});
