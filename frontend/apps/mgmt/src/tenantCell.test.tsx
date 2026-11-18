import { describe, expect, it } from 'vitest';
import { render, renderHook, screen } from '@testing-library/react';
import { TenantCellProvider, useTenantCell } from './tenantCell.js';

function CellProbe() {
  return <span data-testid="cell">{useTenantCell() ?? 'control plane'}</span>;
}

describe('useTenantCell', () => {
  it('is undefined outside a tenant cell provider', () => {
    const { result } = renderHook(() => useTenantCell());

    expect(result.current).toBeUndefined();
  });

  it('is the cell the provider carries', () => {
    render(
      <TenantCellProvider cell="eu-1">
        <CellProbe />
      </TenantCellProvider>,
    );

    expect(screen.getByTestId('cell')).toHaveTextContent('eu-1');
  });

  it('is undefined under a provider that carries no cell', () => {
    render(
      <TenantCellProvider cell={undefined}>
        <CellProbe />
      </TenantCellProvider>,
    );

    expect(screen.getByTestId('cell')).toHaveTextContent('control plane');
  });

  it('is the cell of the nearest provider', () => {
    render(
      <TenantCellProvider cell="eu-1">
        <TenantCellProvider cell="us-1">
          <CellProbe />
        </TenantCellProvider>
      </TenantCellProvider>,
    );

    expect(screen.getByTestId('cell')).toHaveTextContent('us-1');
  });
});
