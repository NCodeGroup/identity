import type { EnvironmentConfig } from '@ncodegroup/identity-config';
import { HazardBand } from '@ncodegroup/identity-ui';
import { useMessages } from './i18n/LocaleContext.js';

export type EnvironmentBannerProps = {
  /** The tenant's non-production environment; the host embeds it only for a tenant that is not production. */
  readonly environment: EnvironmentConfig;
};

/** Warns that the tenant is a non-production environment, so no one mistakes it for the real thing. */
export function EnvironmentBanner({ environment }: EnvironmentBannerProps) {
  const messages = useMessages();
  const label = environment.name ?? environment.type;
  return <HazardBand>{messages.environmentBanner(label)}</HazardBand>;
}
