/** The role of an administrator that belongs to one cell. */
const CELL_ADMIN_ROLE = 'CellAdmin';

/** Names the administrator the setup creates for `administratorRole`: a cell administrator or a root administrator. */
export function administratorTitle(administratorRole: string): string {
  return administratorRole === CELL_ADMIN_ROLE ? 'cell administrator' : 'root administrator';
}
