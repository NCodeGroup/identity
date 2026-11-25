import type { TenantAddressInput } from '@ncodegroup/identity-ui';

/** The address fields that a tenant address preview sends: the identifier, and the issuer and alias when entered. */
export type TenantAddressPreviewArgs = {
  readonly tenantId: string;
  readonly issuer?: string;
  readonly mtlsEndpointAliasAuthority?: string;
};

/** The preview arguments of an address as it is entered, leaving out an issuer or alias that is empty. */
export function tenantAddressPreviewArgs(
  address: TenantAddressInput | undefined,
): TenantAddressPreviewArgs {
  return {
    tenantId: address?.tenantId ?? '',
    ...(address?.issuer ? { issuer: address.issuer } : {}),
    ...(address?.mtlsEndpointAliasAuthority
      ? { mtlsEndpointAliasAuthority: address.mtlsEndpointAliasAuthority }
      : {}),
  };
}

/**
 * Whether a preview that was asked for with `args` still describes the address as it is entered now, so a preview of an
 * address the operator has since changed is never shown.
 */
export function isTenantAddressPreviewCurrent(
  args: TenantAddressPreviewArgs | undefined,
  address: TenantAddressInput | undefined,
): boolean {
  return (
    args !== undefined &&
    address !== undefined &&
    args.tenantId === address.tenantId &&
    (args.issuer ?? '') === address.issuer &&
    (args.mtlsEndpointAliasAuthority ?? '') === address.mtlsEndpointAliasAuthority
  );
}
