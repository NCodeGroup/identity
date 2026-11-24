/**
 * The principal types the control plane names in a `PrincipalSummaryResource.principalType`, mirroring
 * `PrincipalTypes`. The vocabulary is open: a package can register a describer of another type, so a summary may carry
 * a type this object does not list.
 */
export const PRINCIPAL_TYPES = {
  localAccount: 'local_account',
  client: 'client',
  federatedUser: 'federated_user',
} as const;
