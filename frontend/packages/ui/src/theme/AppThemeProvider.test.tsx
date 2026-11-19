import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import { AppThemeProvider } from './AppThemeProvider.js';

/** Renders the theme values its descendants receive, so the test can observe what the provider supplies. */
function ThemeProbe() {
  const theme = useTheme();
  return (
    <dl>
      <dt>mode</dt>
      <dd data-testid="probe-mode">{theme.palette.mode}</dd>
      <dt>primary</dt>
      <dd data-testid="probe-primary">{theme.palette.primary.main}</dd>
      <dt>radius</dt>
      <dd data-testid="probe-radius">{String(theme.shape.borderRadius)}</dd>
      <dt>transition</dt>
      <dd data-testid="probe-transition">{theme.transitions.create('opacity')}</dd>
    </dl>
  );
}

/** Stubs `window.matchMedia` so a media query matches when `matches` says it does. */
function stubMatchMedia(matches: (query: string) => boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: matches(query),
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}

describe('AppThemeProvider', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders its children under the light Instrument theme by default', () => {
    render(
      <AppThemeProvider>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    expect(screen.getByTestId('probe-mode')).toHaveTextContent('light');
    expect(screen.getByTestId('probe-primary')).toHaveTextContent('#0033CC');
    expect(screen.getByTestId('probe-radius')).toHaveTextContent('6');
  });

  it('derives the theme from the runtime branding', () => {
    render(
      <AppThemeProvider branding={{ mode: 'dark', primaryColor: '#0F766E' }}>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    expect(screen.getByTestId('probe-mode')).toHaveTextContent('dark');
    expect(screen.getByTestId('probe-primary')).toHaveTextContent('#5DB6B2');
  });

  it('applies the CssBaseline global styles from the branded theme', () => {
    render(
      <AppThemeProvider branding={{ mode: 'dark' }}>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    // Instrument's dark background (#0A0A08), applied to <body> by CssBaseline.
    expect(getComputedStyle(document.body).backgroundColor).toBe('rgb(10, 10, 8)');
  });

  it("follows the reader's system mode when the branding names none", () => {
    stubMatchMedia((query) => query === '(prefers-color-scheme: dark)');

    render(
      <AppThemeProvider branding={{ displayName: 'Acme' }}>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    expect(screen.getByTestId('probe-mode')).toHaveTextContent('dark');
  });

  it("keeps the branding's mode over the reader's system mode", () => {
    stubMatchMedia((query) => query === '(prefers-color-scheme: dark)');

    render(
      <AppThemeProvider branding={{ mode: 'light' }}>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    expect(screen.getByTestId('probe-mode')).toHaveTextContent('light');
  });

  it('makes every transition instant when the reader asks for reduced motion', () => {
    stubMatchMedia((query) => query === '(prefers-reduced-motion: reduce)');

    render(
      <AppThemeProvider>
        <ThemeProbe />
      </AppThemeProvider>,
    );

    expect(screen.getByTestId('probe-transition')).toHaveTextContent('none');
  });

  it("sets a field's label above its well, with no notch in its outline", () => {
    const { container } = render(
      <AppThemeProvider>
        <TextField label="Name" />
        <TextField label="Date" slotProps={{ inputLabel: { shrink: true } }} />
      </AppThemeProvider>,
    );

    const labels = container.querySelectorAll('label');
    const legends = container.querySelectorAll('legend');
    expect(labels.length).toBeGreaterThan(0);
    expect(legends.length).toBeGreaterThan(0);
    for (const label of labels) {
      expect(label).toHaveAttribute('data-shrink', 'true');
      expect(getComputedStyle(label).position).toBe('static');
      expect(getComputedStyle(label).transform).toBe('none');
    }
    for (const legend of legends) {
      expect(getComputedStyle(legend).maxWidth).toBe('0.01px');
    }
    expect(screen.getByLabelText('Name')).toBeInTheDocument();
  });
});
