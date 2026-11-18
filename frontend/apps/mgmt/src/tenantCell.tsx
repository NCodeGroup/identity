import { createContext, use, type ReactNode } from 'react';

// The cell of the tenant route the console shows. A tenant lives in one cell, and every call its containers make
// addresses that cell; a platform or team route names no cell, so its calls reach the control plane.
const TenantCellContext = createContext<string | undefined>(undefined);

export type TenantCellProviderProps = {
  /** The cell of the tenant route being shown, or `undefined` for the control plane. */
  readonly cell: string | undefined;
  readonly children: ReactNode;
};

/** Provides the cell of the tenant route being shown to every container beneath it. */
export function TenantCellProvider({ cell, children }: TenantCellProviderProps) {
  return <TenantCellContext value={cell}>{children}</TenantCellContext>;
}

/**
 * The cell of the tenant route being shown; `undefined` outside one, so a call reaches the control plane.
 */
export function useTenantCell(): string | undefined {
  return use(TenantCellContext);
}
