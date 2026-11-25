import { useAppSelector, type RootState } from '@ncodegroup/identity-store';

/**
 * Reads what the console manages from the runtime configuration: the `console` the host's bootstrap document publishes
 * for a BFF runtime, and `undefined` for any other runtime or when the host names none.
 */
export function selectConsoleKind(state: RootState): string | undefined {
  return state.config.runtime?.kind === 'bff' ? state.config.runtime.console : undefined;
}

/** The console kind the host publishes, such as `platform` or `cell`; `undefined` when the runtime names none. */
export function useConsoleKind(): string | undefined {
  return useAppSelector(selectConsoleKind);
}

/** Whether this console manages one cell, which shows no platform or team surface and addresses only its own API. */
export function useIsCellConsole(): boolean {
  return useConsoleKind() === 'cell';
}
