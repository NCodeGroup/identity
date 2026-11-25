import { describe, expect, it } from 'vitest';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig, OpenIdRuntimeConfig } from '@ncodegroup/identity-config';
import { CELL_CONSOLE_KIND, selectConsoleKind } from './consoleKind.js';

const bff: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

const openId: OpenIdRuntimeConfig = {
  kind: 'openid',
  apiBaseUrl: 'https://api.test',
  issuer: 'https://issuer.test',
  authorizationEndpoint: 'https://issuer.test/authorize',
  tokenEndpoint: 'https://issuer.test/token',
  scopes: ['openid'],
  features: {},
};

function kindOf(runtime: BffRuntimeConfig | OpenIdRuntimeConfig) {
  return selectConsoleKind(createAppStore({ config: { runtime } }).getState());
}

describe('selectConsoleKind', () => {
  it('reads cell from a BFF cell console', () => {
    expect(kindOf({ ...bff, console: CELL_CONSOLE_KIND, cell: 'eu-1' })).toBe(CELL_CONSOLE_KIND);
  });

  it('reads platform from a BFF platform console', () => {
    expect(kindOf({ ...bff, console: 'platform' })).toBe('platform');
  });

  it('reads nothing from a BFF runtime that names no console', () => {
    expect(kindOf(bff)).toBeUndefined();
  });

  it('reads nothing from a runtime that is not a BFF', () => {
    expect(kindOf(openId)).toBeUndefined();
  });
});
