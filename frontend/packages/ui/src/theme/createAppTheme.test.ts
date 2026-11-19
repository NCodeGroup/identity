import { describe, expect, it } from 'vitest';
import { createTheme } from '@mui/material/styles';
import { deriveBrand } from '@ncodegroup/identity-brand/derive';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import vectors from '@ncodegroup/identity-brand/derive.vectors.json';
import { createAppTheme } from './createAppTheme.js';

const MODES = ['light', 'dark'] as const;

describe('createAppTheme', () => {
  it.each(MODES)("takes Instrument's %s palette for NCode's own brand", (mode) => {
    const theme = createAppTheme({ mode });
    const expected = instrument.palette[mode];

    expect(theme.palette.mode).toBe(mode);
    expect(theme.palette.primary).toMatchObject(expected.primary);
    expect(theme.palette.secondary).toMatchObject(expected.secondary);
    expect(theme.palette.error).toMatchObject(expected.error);
    expect(theme.palette.warning).toMatchObject(expected.warning);
    expect(theme.palette.info).toMatchObject(expected.info);
    expect(theme.palette.success).toMatchObject(expected.success);
    expect(theme.palette.text).toMatchObject(expected.text);
    expect(theme.palette.background).toEqual(expected.background);
    expect(theme.palette.divider).toBe(expected.divider);
    expect(theme.palette.instrument).toEqual(expected.instrument);
  });

  it('is light when neither the branding nor the system names a mode', () => {
    expect(createAppTheme().palette.mode).toBe('light');
    expect(createAppTheme({ displayName: 'Acme' }).palette.mode).toBe('light');
  });

  it('follows the system mode when the branding names none', () => {
    const theme = createAppTheme({ displayName: 'Acme' }, { systemMode: 'dark' });

    expect(theme.palette.mode).toBe('dark');
    expect(theme.palette.background.default).toBe(instrument.palette.dark.background.default);
  });

  it("keeps the branding's mode over the system's", () => {
    expect(createAppTheme({ mode: 'light' }, { systemMode: 'dark' }).palette.mode).toBe('light');
    expect(createAppTheme({ mode: 'dark' }, { systemMode: 'light' }).palette.mode).toBe('dark');
  });

  describe.each(vectors.vectors)('for the vector "$name"', ({ primary, secondary }) => {
    it.each(MODES)('maps the colors deriveBrand derives in the %s theme', (mode) => {
      const d = deriveBrand(primary, secondary)[mode];
      const theme = createAppTheme({
        mode,
        ...(primary === null ? {} : { primaryColor: primary }),
        ...(secondary === null ? {} : { secondaryColor: secondary }),
      });

      expect(theme.palette.primary).toMatchObject({
        main: d.primary,
        dark: d['primary-hover'],
        contrastText: d['on-primary'],
      });
      expect(theme.palette.secondary).toMatchObject(
        mode === 'light'
          ? { main: d.screen, dark: d['screen-raised'], contrastText: d['on-screen'] }
          : { main: d['screen-lit'], dark: d['on-screen-secondary'], contrastText: d.screen },
      );
      expect(theme.palette.instrument).toMatchObject(d);
      expect(theme.palette.error).toMatchObject(instrument.palette[mode].error);
    });
  });

  it('falls back to NCode Blue for colors it cannot read, and never throws', () => {
    const theme = createAppTheme({ primaryColor: 'not a color', secondaryColor: '#12' });

    expect(theme.palette.mode).toBe('light');
    expect(theme.palette.primary.main).toBe(instrument.palette.light.primary.main);
    expect(theme.palette.secondary.main).toBe(instrument.palette.light.secondary.main);
  });

  it('sets every typography variant in its Instrument type role', () => {
    const theme = createAppTheme();
    const { variants, fontFamily, fontSize, fontWeightRegular, fontWeightMedium, fontWeightBold } =
      instrument.typography;

    for (const [name, variant] of Object.entries(variants)) {
      const { role, ...values } = variant;

      expect(role).toBeTypeOf('string');
      expect(theme.typography[name as keyof typeof theme.typography]).toMatchObject(values);
    }
    expect(theme.typography.fontFamily).toBe(fontFamily);
    expect(theme.typography.fontSize).toBe(fontSize);
    expect(theme.typography.fontWeightRegular).toBe(fontWeightRegular);
    expect(theme.typography.fontWeightMedium).toBe(fontWeightMedium);
    expect(theme.typography.fontWeightBold).toBe(fontWeightBold);
  });

  it('reads the code font through the theme', () => {
    const theme = createAppTheme();

    expect(theme.typography.fontFamilyMono).toBe(instrument.typography.fontFamilyMono);
    expect(theme.typography.code.fontFamily).toBe(instrument.typography.fontFamilyMono);
  });

  it("keeps Material UI's size for a variant Instrument does not name", () => {
    const theme = createAppTheme();

    expect(theme.typography.caption.fontSize).toBe(createTheme().typography.caption.fontSize);
    expect(theme.typography.caption.fontFamily).toBe(instrument.typography.fontFamily);
  });

  it('takes the radius, spacing and measures', () => {
    const theme = createAppTheme();

    expect(theme.shape.borderRadius).toBe(6);
    expect(theme.spacing(1)).toBe('8px');
    expect(theme.instrument).toEqual({
      radius: instrument.radius,
      space: instrument.space,
      size: instrument.size,
      focus: instrument.focus,
    });
  });

  it.each(MODES)('takes the %s depths for the 25 elevations', (mode) => {
    const theme = createAppTheme({ mode });

    expect(theme.shadows).toHaveLength(25);
    expect(theme.shadows).toEqual(instrument.shadows[mode]);
  });

  it("changes layout at Instrument's breakpoints", () => {
    const theme = createAppTheme();

    expect(theme.breakpoints.values).toEqual({ xs: 0, sm: 641, md: 1089, lg: 1281, xl: 1536 });
    expect(theme.breakpoints.down('sm')).toBe('@media (max-width:640.95px)');
  });
});
