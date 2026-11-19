import { useMemo, type ReactNode } from 'react';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import type { BrandingConfig } from '@ncodegroup/identity-config';
import { createAppTheme } from './createAppTheme.js';

export type AppThemeProviderProps = {
  readonly branding?: BrandingConfig;
  readonly children: ReactNode;
};

/**
 * Stateless theme provider: builds the Instrument theme from the runtime branding and the reader's system mode and
 * motion setting, and applies CssBaseline with the mode's color scheme.
 */
export function AppThemeProvider({ branding, children }: AppThemeProviderProps) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)', { noSsr: true });
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)', { noSsr: true });
  const theme = useMemo(
    () => createAppTheme(branding, { systemMode: prefersDark ? 'dark' : 'light', reducedMotion }),
    [branding, prefersDark, reducedMotion],
  );
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline enableColorScheme />
      {children}
    </ThemeProvider>
  );
}
