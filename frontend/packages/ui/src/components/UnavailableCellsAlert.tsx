import Alert from '@mui/material/Alert';

export type UnavailableCellsAlertProps = {
  /** The cells that did not answer when their tenants were listed. */
  readonly cells: readonly string[];
};

/**
 * Stateless warning that a list of tenants is partial: it names each cell that did not answer, whose tenants are not
 * listed, and renders nothing when every cell answered.
 */
export function UnavailableCellsAlert({ cells }: UnavailableCellsAlertProps) {
  if (cells.length === 0) {
    return null;
  }
  return (
    <Alert severity="warning" sx={{ mb: 1 }} data-testid="unavailable-cells">
      {cells.map((cell) => (
        <div key={cell}>{`The cell ${cell} did not answer, so its tenants are not listed.`}</div>
      ))}
    </Alert>
  );
}
