import { PLATFORM_SCOPE, teamScope, tenantScope, type ManagementScope } from '@ncode/api';

/**
 * The console's URL is its navigation state. The path names the security boundary first and the surface
 * within it second, so the platform ↔ team ↔ tenant boundaries (ADR-1064) stay explicit in every address:
 *
 * - `/home` — the caller's home: the places it holds authority (ADR-1465);
 * - `/teams`, `/clients`, `/settings`, `/encryption-keys` — the platform scope's surfaces;
 * - `/teams/{teamId}/{surface}` — a team scope's surfaces (`tenants`, `teams`, `clients`,
 *   `settings`, `access`, `encryption-keys` and `usage`), the team id percent-encoded as one
 *   segment;
 * - `/tenants/{tenantId}/{surface}` — a tenant scope's surfaces (`clients`, `resource-servers`, `grants`, `secrets`,
 *   `encryption-keys`, `settings`, `access` and `usage`), the tenant id percent-encoded as one
 *   segment (ADR-1255);
 * - `/cells/{cell}/tenants/{tenantId}/{surface}` — the same tenant surfaces of a tenant that lives
 *   in a named cell, the cell percent-encoded as one segment; a tenant path without a cell
 *   addresses the control plane.
 *
 * This module is UI-free so the route grammar is unit-tested; `navigation.ts` binds it to the browser history.
 */

/** The surfaces of the platform scope, in tab order. */
export const PLATFORM_SURFACES = ['teams', 'clients', 'settings', 'encryption-keys'] as const;

/** The surfaces of a team scope, in tab order. */
export const TEAM_SURFACES = [
  'tenants',
  'teams',
  'clients',
  'settings',
  'access',
  'encryption-keys',
  'usage',
] as const;

/** The surfaces of a tenant scope, in tab order. */
export const TENANT_SURFACES = [
  'clients',
  'resource-servers',
  'grants',
  'secrets',
  'encryption-keys',
  'settings',
  'access',
  'usage',
] as const;

export type PlatformSurface = (typeof PLATFORM_SURFACES)[number];
export type TeamSurface = (typeof TEAM_SURFACES)[number];
export type TenantSurface = (typeof TENANT_SURFACES)[number];

/**
 * The tenant surfaces that show the tenant's own secrets, settings, access and usage rather than the resources it
 * holds.
 */
export type TenantOwnSurface = Extract<
  TenantSurface,
  'secrets' | 'encryption-keys' | 'settings' | 'access' | 'usage'
>;

export type ConsoleRoute =
  | { readonly kind: 'home' }
  | { readonly kind: 'platform'; readonly surface: PlatformSurface }
  | { readonly kind: 'team'; readonly teamId: string; readonly surface: TeamSurface }
  | {
      readonly kind: 'tenant';
      readonly tenantId: string;
      readonly surface: TenantSurface;
      readonly cell?: string;
    };

/** The route of the caller's home: the places it holds authority. */
export const HOME_ROUTE: ConsoleRoute = { kind: 'home' };

/** A route that addresses a management scope: every route except the caller's home. */
export type ScopedConsoleRoute = Exclude<ConsoleRoute, { readonly kind: 'home' }>;

/** Where the console lands when the path names no route: the caller's home. */
export const DEFAULT_ROUTE: ConsoleRoute = HOME_ROUTE;

/** The surface a team opens on when the path names the team but no surface. */
export const DEFAULT_TEAM_SURFACE: TeamSurface = 'tenants';

/** The surface a tenant opens on when the path names the tenant but no surface. */
export const DEFAULT_TENANT_SURFACE: TenantSurface = 'clients';

/** Builds the route of a platform surface. */
export function platformRoute(surface: PlatformSurface): ScopedConsoleRoute {
  return { kind: 'platform', surface };
}

/** Builds the route of a team surface, rejecting an empty/blank team id. */
export function teamRoute(
  teamId: string,
  surface: TeamSurface = DEFAULT_TEAM_SURFACE,
): ScopedConsoleRoute {
  teamScope(teamId);
  return { kind: 'team', teamId, surface };
}

/**
 * Builds the route of a tenant surface in `cell` (the control plane without one), rejecting an
 * empty/blank tenant id and a blank cell.
 */
export function tenantRoute(
  tenantId: string,
  surface: TenantSurface = DEFAULT_TENANT_SURFACE,
  cell?: string,
): ScopedConsoleRoute {
  tenantScope(tenantId);
  if (cell === undefined) {
    return { kind: 'tenant', tenantId, surface };
  }
  if (cell.trim().length === 0) {
    throw new Error('A cell must be a non-empty string.');
  }
  return { kind: 'tenant', tenantId, surface, cell };
}

/** The management scope a route addresses — derived from the route, never held separately. */
export function routeScope(route: ScopedConsoleRoute): ManagementScope {
  switch (route.kind) {
    case 'platform':
      return PLATFORM_SCOPE;
    case 'team':
      return teamScope(route.teamId);
    case 'tenant':
      return tenantScope(route.tenantId);
  }
}

/** Formats a route as its canonical path. */
export function routePath(route: ConsoleRoute): string {
  switch (route.kind) {
    case 'home':
      return '/home';
    case 'platform':
      return `/${route.surface}`;
    case 'team':
      return `/teams/${encodeURIComponent(route.teamId)}/${route.surface}`;
    case 'tenant': {
      const tenantPath = `/tenants/${encodeURIComponent(route.tenantId)}/${route.surface}`;
      return route.cell === undefined
        ? tenantPath
        : `/cells/${encodeURIComponent(route.cell)}${tenantPath}`;
    }
  }
}

function isPlatformSurface(value: string): value is PlatformSurface {
  return (PLATFORM_SURFACES as readonly string[]).includes(value);
}

function isTeamSurface(value: string): value is TeamSurface {
  return (TEAM_SURFACES as readonly string[]).includes(value);
}

function isTenantSurface(value: string): value is TenantSurface {
  return (TENANT_SURFACES as readonly string[]).includes(value);
}

function decodeSegment(segment: string): string | null {
  try {
    return decodeURIComponent(segment);
  } catch {
    return null;
  }
}

/**
 * Parses a path into the route it names, or `null` when it names none. A single trailing slash is tolerated, and a
 * team or tenant path without a surface names that scope's default surface.
 */
export function parseRoute(pathname: string): ConsoleRoute | null {
  if (!pathname.startsWith('/')) {
    return null;
  }
  const trimmed = pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
  const segments = trimmed.slice(1).split('/');

  if (segments[0] === 'cells') {
    const [, encodedCell = '', ...scoped] = segments;
    const cell = decodeSegment(encodedCell);
    if (cell === null || cell.trim().length === 0 || scoped[0] !== 'tenants') {
      return null;
    }
    return parseTenantRoute(scoped, cell);
  }

  if (segments.length === 1) {
    const [surface = ''] = segments;
    if (surface === 'home') {
      return HOME_ROUTE;
    }
    return isPlatformSurface(surface) ? platformRoute(surface) : null;
  }

  const [root, encodedId = '', surface, ...rest] = segments;
  if (rest.length > 0) {
    return null;
  }
  const id = decodeSegment(encodedId);
  if (id === null || id.trim().length === 0) {
    return null;
  }
  if (root === 'teams') {
    const teamSurface = surface ?? DEFAULT_TEAM_SURFACE;
    return isTeamSurface(teamSurface) ? { kind: 'team', teamId: id, surface: teamSurface } : null;
  }
  if (root === 'tenants') {
    return parseTenantRoute(segments);
  }
  return null;
}

/**
 * Parses the segments of a tenant path (`tenants`, the id and an optional surface) in the given
 * cell, if any.
 */
function parseTenantRoute(
  segments: readonly string[],
  cell?: string,
): Extract<ConsoleRoute, { readonly kind: 'tenant' }> | null {
  const [, encodedId = '', surface, ...rest] = segments;
  if (rest.length > 0) {
    return null;
  }
  const tenantId = decodeSegment(encodedId);
  if (tenantId === null || tenantId.trim().length === 0) {
    return null;
  }
  const tenantSurface = surface ?? DEFAULT_TENANT_SURFACE;
  return isTenantSurface(tenantSurface)
    ? { kind: 'tenant', tenantId, surface: tenantSurface, ...(cell === undefined ? {} : { cell }) }
    : null;
}

/**
 * Resolves any path to the route the console shows for it and that route's canonical path: a path naming no route
 * resolves to {@link DEFAULT_ROUTE}. The canonical path is always built by {@link routePath}, so it is a same-origin
 * console path whatever the input — safe to restore after a redirect round trip.
 */
export function resolveRoute(pathname: string): {
  readonly route: ConsoleRoute;
  readonly path: string;
} {
  const route = parseRoute(pathname) ?? DEFAULT_ROUTE;
  return { route, path: routePath(route) };
}
