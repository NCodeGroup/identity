import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import { cssRgb } from '@ncodegroup/identity-test-support';
import { createAppTheme } from '../theme/createAppTheme.js';
import { HazardBand } from './HazardBand.js';

const MODES = ['light', 'dark'] as const;

describe('HazardBand', () => {
  it('shows its text on the hazard color', () => {
    const theme = createAppTheme();
    const { hazard, 'on-hazard': onHazard } = theme.palette.instrument;
    render(
      <ThemeProvider theme={theme}>
        <HazardBand>Staging</HazardBand>
      </ThemeProvider>,
    );

    const band = screen.getByText('Staging').parentElement;
    expect(band).not.toBeNull();
    if (!band) return;
    const style = getComputedStyle(band);
    expect(style.backgroundColor).toBe(cssRgb(hazard));
    expect(style.color).toBe(cssRgb(onHazard));
  });

  it.each(MODES)('runs hazard tape on both sides of its text in the %s theme', (mode) => {
    const theme = createAppTheme({ mode });
    const { hazard, 'on-hazard': onHazard } = theme.palette.instrument;
    render(
      <ThemeProvider theme={theme}>
        <HazardBand>Staging</HazardBand>
      </ThemeProvider>,
    );

    const css = Array.from(document.querySelectorAll('style'), (s) => s.textContent).join('');
    const tape = `repeating-linear-gradient(-45deg, ${onHazard} 0 8px, ${hazard} 8px 16px)`;
    expect(css.split(tape).length - 1).toBe(2);
  });
});
