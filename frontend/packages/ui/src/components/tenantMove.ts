/** The transfer key a receiving cell issues: its `kid` and its public JSON Web Key (decoupled from the API contract). */
export type TenantMoveTransferKey = {
  readonly kid: string;
  readonly jwk: Readonly<Record<string, unknown>>;
};

/**
 * The four calls of a move, each addressed to the cell it concerns (decoupled from the API contract). Each rejects
 * with an `Error` whose message says why it failed.
 */
export type TenantMoveOperations = {
  /** Asks `cell`, the receiving cell, for a one-time transfer key. */
  readonly createTransferKey: (cell: string) => Promise<TenantMoveTransferKey>;
  /**
   * Exports the disabled tenant from `cell`, the tenant's own cell, sealed to `jwk` and offered to `targetCell`, the
   * receiving cell, which alone imports the package; resolves with the package.
   */
  readonly exportTenant: (
    cell: string,
    tenantId: string,
    targetCell: string,
    jwk: Readonly<Record<string, unknown>>,
  ) => Promise<string>;
  /** Imports `sealedPackage` into `cell`, the receiving cell, with the transfer key named `kid`. */
  readonly importTenant: (cell: string, kid: string, sealedPackage: string) => Promise<void>;
  /** Releases what `cell`, the tenant's own cell, still holds of the tenant that moved. */
  readonly releaseMovedTenant: (cell: string, tenantId: string) => Promise<void>;
};

/** The tenant and the two cells of one move. */
export type TenantMoveTarget = {
  readonly tenantId: string;
  /** The cell the tenant lives in. */
  readonly sourceCell: string;
  /** The cell that receives the tenant. */
  readonly targetCell: string;
};

/** The stage that failed and why. */
export type TenantMoveFailure = {
  /** The index of the stage that failed. */
  readonly stage: number;
  /**
   * The stage a retry runs from: 0 when the import failed, because the import spends the transfer key, and the failed
   * stage otherwise.
   */
  readonly resumeStage: number;
  readonly message: string;
};

/** A move: runs its stages from a given stage and keeps what one stage hands the next. */
export type TenantMove = {
  /**
   * Runs the stages from `fromStage` (0 for a fresh move, the failure's `resumeStage` for a retry), so the stages
   * before it are skipped. Calls `onStageCompleted` with the number of stages finished after each stage. Resolves with
   * the stage that failed, or `undefined` when every stage finished.
   */
  readonly run: (
    fromStage: number,
    onStageCompleted?: (completedStages: number) => void,
  ) => Promise<TenantMoveFailure | undefined>;
};

/** The index of each stage of a move, in the order the stages run. */
export const tenantMoveStages = { transferKey: 0, export: 1, import: 2, release: 3 } as const;

/** The number of stages of a move: the transfer key, the export, the import and the release. */
export const tenantMoveStageCount = Object.keys(tenantMoveStages).length;

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Creates a move of a tenant between two cells. The move keeps the transfer key and the sealed package of the stages
 * that finished, so a run that resumes at a later stage reuses them instead of asking for them again.
 */
export function createTenantMove(
  operations: TenantMoveOperations,
  { tenantId, sourceCell, targetCell }: TenantMoveTarget,
): TenantMove {
  let transferKey: TenantMoveTransferKey | undefined;
  let sealedPackage: string | undefined;

  const runStage = async (stage: number): Promise<void> => {
    switch (stage) {
      case tenantMoveStages.transferKey:
        transferKey = await operations.createTransferKey(targetCell);
        return;
      case tenantMoveStages.export:
        if (transferKey === undefined) {
          throw new Error('The transfer key was not issued.');
        }
        sealedPackage = await operations.exportTenant(
          sourceCell,
          tenantId,
          targetCell,
          transferKey.jwk,
        );
        return;
      case tenantMoveStages.import:
        if (transferKey === undefined || sealedPackage === undefined) {
          throw new Error('The sealed package was not exported.');
        }
        await operations.importTenant(targetCell, transferKey.kid, sealedPackage);
        return;
      case tenantMoveStages.release:
        await operations.releaseMovedTenant(sourceCell, tenantId);
        return;
      default:
        throw new Error(`Unknown stage ${stage}.`);
    }
  };

  return {
    run: async (fromStage, onStageCompleted) => {
      // `stage` counts the stages that finished, so it is also the index of the stage that runs next.
      let stage = fromStage;
      try {
        while (stage < tenantMoveStageCount) {
          await runStage(stage);
          stage += 1;
          onStageCompleted?.(stage);
        }
        return undefined;
      } catch (error) {
        if (stage === tenantMoveStages.import) {
          // The import spends the transfer key, so a retry asks the receiving cell for another one.
          transferKey = undefined;
          sealedPackage = undefined;
          return { stage, resumeStage: tenantMoveStages.transferKey, message: errorMessage(error) };
        }
        return { stage, resumeStage: stage, message: errorMessage(error) };
      }
    },
  };
}
