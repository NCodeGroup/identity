/**
 * Whether two regions are one region. A tenant moves, and a configuration is promoted, only within one region, and a
 * region that is not known matches no region, another unknown region included.
 */
export function isSameRegion(
  region: string | null | undefined,
  other: string | null | undefined,
): boolean {
  return region !== null && region !== undefined && region === other;
}
