import { describe, expect, it, vi } from 'vitest';
import {
  createTenantMove,
  tenantMoveStageCount,
  tenantMoveStages,
  TenantPlacedElsewhereError,
  type TenantMoveOperations,
} from './tenantMove.js';

const jwk = { kty: 'EC', crv: 'P-256' };
const target = { tenantId: 'acme', sourceCell: 'eu-1', targetCell: 'eu-2' };

function createOperations(calls: string[] = []): TenantMoveOperations {
  return {
    createTransferKey: vi.fn(async (cell: string) => {
      calls.push(`transfer-key ${cell}`);
      return { kid: 'k-1', jwk };
    }),
    exportTenant: vi.fn(async (cell: string, tenantId: string) => {
      calls.push(`export ${cell} ${tenantId}`);
      return 'sealed';
    }),
    importTenant: vi.fn(async (cell: string) => {
      calls.push(`import ${cell}`);
    }),
    releaseMovedTenant: vi.fn(async (cell: string, tenantId: string) => {
      calls.push(`release ${cell} ${tenantId}`);
    }),
  };
}

/** Fails on its first call and succeeds after, so a retry of the stage finishes it. */
function failOnce<T>(calls: string[], name: string, result: T): () => Promise<T> {
  let failed = false;
  return async () => {
    if (!failed) {
      failed = true;
      calls.push(`${name} failed`);
      throw new Error(`${name} is unavailable.`);
    }
    calls.push(`${name} retried`);
    return result;
  };
}

describe('createTenantMove', () => {
  it('runs the four stages in order and reports each completed stage', async () => {
    const calls: string[] = [];
    const operations = createOperations(calls);
    const completed: number[] = [];

    const failure = await createTenantMove(operations, target).run(0, (count) =>
      completed.push(count),
    );

    expect(failure).toBeUndefined();
    expect(calls).toEqual([
      'transfer-key eu-2',
      'export eu-1 acme',
      'import eu-2',
      'release eu-1 acme',
    ]);
    expect(completed).toEqual([1, 2, 3, tenantMoveStageCount]);
    expect(operations.exportTenant).toHaveBeenCalledWith('eu-1', 'acme', 'eu-2', jwk);
    expect(operations.importTenant).toHaveBeenCalledWith('eu-2', 'k-1', 'sealed');
  });

  it('reports the transfer key stage as failed and stops', async () => {
    const calls: string[] = [];
    const operations: TenantMoveOperations = {
      ...createOperations(calls),
      createTransferKey: vi.fn(async () => {
        throw new Error('Unavailable.');
      }),
    };

    const failure = await createTenantMove(operations, target).run(0);

    expect(failure).toEqual({ stage: 0, resumeStage: 0, message: 'Unavailable.' });
    expect(calls).toEqual([]);
  });

  it('reports the message of a value that is not an Error', async () => {
    const operations: TenantMoveOperations = {
      ...createOperations(),
      createTransferKey: vi.fn(() => Promise.reject('refused')),
    };

    const failure = await createTenantMove(operations, target).run(0);

    expect(failure).toEqual({ stage: 0, resumeStage: 0, message: 'refused' });
  });

  it('resumes at the export and skips the transfer key', async () => {
    const calls: string[] = [];
    const operations: TenantMoveOperations = {
      ...createOperations(calls),
      exportTenant: vi.fn(failOnce(calls, 'export', 'sealed')),
    };
    const move = createTenantMove(operations, target);
    const first = await move.run(0);
    calls.length = 0;

    const second = await move.run(first?.resumeStage ?? -1);

    expect(first).toEqual({ stage: 1, resumeStage: 1, message: 'export is unavailable.' });
    expect(second).toBeUndefined();
    expect(calls).toEqual(['export retried', 'import eu-2', 'release eu-1 acme']);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(1);
    expect(operations.importTenant).toHaveBeenCalledWith('eu-2', 'k-1', 'sealed');
  });

  it('resumes from the transfer key after the import fails because the import spends the key', async () => {
    const calls: string[] = [];
    const newJwk = { kty: 'EC', crv: 'P-384' };
    let keys = 0;
    const operations: TenantMoveOperations = {
      ...createOperations(calls),
      createTransferKey: vi.fn(async (cell: string) => {
        keys += 1;
        calls.push(`transfer-key ${cell}`);
        return keys === 1 ? { kid: 'k-1', jwk } : { kid: 'k-2', jwk: newJwk };
      }),
      importTenant: vi.fn(failOnce(calls, 'import', undefined)),
    };
    const move = createTenantMove(operations, target);
    const first = await move.run(0);
    calls.length = 0;

    const second = await move.run(first?.resumeStage ?? -1);

    expect(first).toEqual({ stage: 2, resumeStage: 0, message: 'import is unavailable.' });
    expect(second).toBeUndefined();
    expect(calls).toEqual([
      'transfer-key eu-2',
      'export eu-1 acme',
      'import retried',
      'release eu-1 acme',
    ]);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(2);
    expect(operations.exportTenant).toHaveBeenCalledTimes(2);
    expect(operations.exportTenant).toHaveBeenLastCalledWith('eu-1', 'acme', 'eu-2', newJwk);
    expect(operations.importTenant).toHaveBeenCalledTimes(2);
    expect(operations.importTenant).toHaveBeenLastCalledWith('eu-2', 'k-2', 'sealed');
    expect(operations.releaseMovedTenant).toHaveBeenCalledTimes(1);
  });

  it('resumes at the release and runs only the release', async () => {
    const calls: string[] = [];
    const operations: TenantMoveOperations = {
      ...createOperations(calls),
      releaseMovedTenant: vi.fn(failOnce(calls, 'release', undefined)),
    };
    const move = createTenantMove(operations, target);
    const first = await move.run(0);
    calls.length = 0;
    const completed: number[] = [];

    const second = await move.run(first?.resumeStage ?? -1, (count) => completed.push(count));

    expect(first).toEqual({ stage: 3, resumeStage: 3, message: 'release is unavailable.' });
    expect(second).toBeUndefined();
    expect(calls).toEqual(['release retried']);
    expect(completed).toEqual([tenantMoveStageCount]);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(1);
    expect(operations.exportTenant).toHaveBeenCalledTimes(1);
    expect(operations.importTenant).toHaveBeenCalledTimes(1);
  });

  it('reports the failed stage again when the retry fails again', async () => {
    const operations: TenantMoveOperations = {
      ...createOperations(),
      releaseMovedTenant: vi.fn(async () => {
        throw new Error('Still unavailable.');
      }),
    };
    const move = createTenantMove(operations, target);
    await move.run(0);

    const failure = await move.run(3);

    expect(failure).toEqual({ stage: 3, resumeStage: 3, message: 'Still unavailable.' });
  });

  it('fails the export with the placed cell and resumes at the release when the directory places the tenant in another cell', async () => {
    const calls: string[] = [];
    const operations: TenantMoveOperations = {
      ...createOperations(calls),
      exportTenant: vi.fn(async () => {
        throw new TenantPlacedElsewhereError('The tenant is placed in eu-3.', 'eu-3');
      }),
    };
    const move = createTenantMove(operations, target);
    const first = await move.run(0);
    calls.length = 0;

    const second = await move.run(first?.resumeStage ?? -1);

    expect(first).toEqual({
      stage: 1,
      resumeStage: 3,
      message: 'The global directory already places the tenant in eu-3; release this copy at eu-1.',
      placedCell: 'eu-3',
    });
    expect(second).toBeUndefined();
    expect(calls).toEqual(['release eu-1 acme']);
    expect(operations.importTenant).not.toHaveBeenCalled();
  });

  it('reports a placement refused at a stage other than the export as an ordinary failure', async () => {
    const operations: TenantMoveOperations = {
      ...createOperations(),
      releaseMovedTenant: vi.fn(async () => {
        throw new TenantPlacedElsewhereError('Placed.', 'eu-3');
      }),
    };

    expect(await createTenantMove(operations, target).run(0)).toStrictEqual({
      stage: 3,
      resumeStage: 3,
      message: 'Placed.',
    });
  });

  it('fails the stage that needs a transfer key when the run starts after it', async () => {
    const operations = createOperations();

    const failure = await createTenantMove(operations, target).run(1);

    expect(failure).toEqual({
      stage: 1,
      resumeStage: 1,
      message: 'The transfer key was not issued.',
    });
    expect(operations.exportTenant).not.toHaveBeenCalled();
  });

  it('fails the import when the run starts after an export that never ran', async () => {
    const operations = createOperations();

    const failure = await createTenantMove(operations, target).run(2);

    expect(failure).toEqual({
      stage: 2,
      resumeStage: 0,
      message: 'The sealed package was not exported.',
    });
    expect(operations.importTenant).not.toHaveBeenCalled();
  });

  it('fails with the unknown stage and runs no operation when the run starts at a stage that does not exist', async () => {
    const operations = createOperations();

    const failure = await createTenantMove(operations, target).run(-1);

    expect(failure).toEqual({ stage: -1, resumeStage: -1, message: 'Unknown stage -1.' });
    expect(operations.createTransferKey).not.toHaveBeenCalled();
    expect(operations.exportTenant).not.toHaveBeenCalled();
    expect(operations.importTenant).not.toHaveBeenCalled();
    expect(operations.releaseMovedTenant).not.toHaveBeenCalled();
  });
});

describe('tenantMoveStages', () => {
  it('names every stage once in run order', () => {
    expect(Object.values(tenantMoveStages)).toEqual([0, 1, 2, 3]);
    expect(new Set(Object.values(tenantMoveStages)).size).toBe(tenantMoveStageCount);
  });
});
