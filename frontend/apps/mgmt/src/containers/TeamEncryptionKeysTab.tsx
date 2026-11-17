import { useMemo } from 'react';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import type { TeamRootKeyResource } from '@ncode/api';
import {
  useListCellsQuery,
  useListTeamRootKeysQuery,
  useRemoveTeamRootKeyMutation,
  useSetTeamRootKeyMutation,
  useValidateTeamRootKeyMutation,
} from '@ncode/store';
import { DataStateBoundary } from '@ncode/ui';
import { RootKeySection } from './RootKeySection.js';

export type TeamEncryptionKeysTabProps = {
  readonly teamId: string;
};

type TeamRegionRootKeySectionProps = {
  readonly teamId: string;
  readonly region: string;
  readonly rootKey: TeamRootKeyResource | undefined;
  readonly providers: readonly string[];
  readonly accessBinding: string | null;
};

/**
 * The root key a team brings for one region (ADR-1414): "Set root key" in a dialog that shows the server's error,
 * "Validate" and a confirmed "Remove". Validate and remove say what they did in a notice. The API returns no key
 * material, and neither does this view.
 */
function TeamRegionRootKeySection({
  teamId,
  region,
  rootKey,
  providers,
  accessBinding,
}: TeamRegionRootKeySectionProps) {
  const [setRootKey, { isLoading: isSetting }] = useSetTeamRootKeyMutation();
  const [removeRootKey, { isLoading: isRemoving }] = useRemoveTeamRootKeyMutation();
  const [validateRootKey, { isLoading: isValidating }] = useValidateTeamRootKeyMutation();

  return (
    <Stack spacing={1.5} data-testid={`team-root-key-region-${region}`}>
      <Typography variant="h6" component="h4" data-testid="team-root-key-region">
        {region}
      </Typography>
      <RootKeySection
        ownerLabel="team"
        region={region}
        rootKey={rootKey ?? null}
        providers={providers}
        accessBinding={accessBinding}
        isSetting={isSetting}
        isRemoving={isRemoving}
        isValidating={isValidating}
        onSet={(values) => setRootKey({ teamId, region, body: values }).unwrap()}
        onRemove={() => removeRootKey({ teamId, region }).unwrap()}
        onValidate={() => validateRootKey({ teamId, region }).unwrap()}
      />
    </Stack>
  );
}

/**
 * Container: the root keys of a team per region (ADR-1414). One row per distinct region of the platform's cells, sorted
 * by region, each showing that region's key, or saying that the deployment's root key wraps the team's keys there. A
 * region's key is set, validated and removed for that region only. The API returns no key material, and neither does
 * this view.
 */
export function TeamEncryptionKeysTab({ teamId }: TeamEncryptionKeysTabProps) {
  const cells = useListCellsQuery();
  const rootKeys = useListTeamRootKeysQuery({ teamId });

  const regions = useMemo(
    () =>
      [...new Set((cells.data ?? []).map((cell) => cell.region))].sort((a, b) =>
        a < b ? -1 : a > b ? 1 : 0,
      ),
    [cells.data],
  );

  const failed = cells.error || rootKeys.error;

  return (
    <Stack spacing={2}>
      <Typography variant="h6" component="h3">
        Encryption keys
      </Typography>
      <DataStateBoundary
        isLoading={cells.isLoading || rootKeys.isLoading}
        error={failed ? 'Failed to load the encryption keys of this team.' : undefined}
        isEmpty={regions.length === 0}
        emptyMessage="The platform has no regions, so this team has none to set a root key in."
      >
        {regions.map((region) => (
          <TeamRegionRootKeySection
            key={region}
            teamId={teamId}
            region={region}
            rootKey={rootKeys.data?.rootKeys.find((key) => key.region === region)}
            providers={rootKeys.data?.rootKeyProviders ?? []}
            accessBinding={rootKeys.data?.rootKeyAccessBinding ?? null}
          />
        ))}
      </DataStateBoundary>
    </Stack>
  );
}
