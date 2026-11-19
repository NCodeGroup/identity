import { describe, expect, it } from 'vitest';
import { createTheme, type Theme } from '@mui/material/styles';
import { deriveBrand } from '@ncodegroup/identity-brand/derive';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import vectors from '@ncodegroup/identity-brand/derive.vectors.json';
import { createAppTheme } from './createAppTheme.js';

const MODES = ['light', 'dark'] as const;

const TEAL = { primaryColor: '#0F766E', secondaryColor: '#7C2D12', mode: 'light' } as const;

/** The `root` style of a component's `styleOverrides`, or the overrides themselves when they have no `root`. */
function overrides(component: { styleOverrides?: unknown } | undefined): unknown {
  const styleOverrides = component?.styleOverrides as Record<string, unknown> | undefined;
  return 'root' in (styleOverrides ?? {}) ? styleOverrides?.root : styleOverrides;
}

type ButtonVariant = { props: { variant?: string; color?: string }; style: unknown };

/** The `variants` of the MuiButton `root` style of a theme. */
function buttonVariants(theme: Theme): ButtonVariant[] {
  return (overrides(theme.components?.MuiButton) as { variants: ButtonVariant[] }).variants;
}

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

  it("times and eases motion with Instrument's durations and easings", () => {
    const { duration, easing } = createAppTheme().transitions;

    expect(duration).toMatchObject({
      shortest: 70,
      shorter: 120,
      short: 120,
      standard: 200,
      complex: 280,
      enteringScreen: 280,
      leavingScreen: 200,
    });
    expect(easing.easeOut).toBe(instrument.transitions.easing.out);
    expect(easing.easeInOut).toBe(instrument.transitions.easing['in-out']);
  });

  it('stops every animation and transition when the reader asks for reduced motion', () => {
    const { transitions, components } = createAppTheme({}, { reducedMotion: true });

    expect(transitions.create(['opacity'])).toBe('none');
    for (const value of Object.values(transitions.duration)) {
      expect(value).toBe(0);
    }
    expect(overrides(components?.MuiCssBaseline)).toMatchObject({
      '*, *::before, *::after': {
        animation: 'none !important',
        transition: 'none !important',
        scrollBehavior: 'auto !important',
      },
    });
  });

  it('keeps animations when the reader does not ask for reduced motion', () => {
    const { components } = createAppTheme();

    expect(overrides(components?.MuiCssBaseline)).not.toHaveProperty(['*, *::before, *::after']);
  });

  it('draws the focus ring in the focus color, focus wide and offset', () => {
    const { components } = createAppTheme(TEAL);
    const ring = { outline: '2px solid #006D65', outlineOffset: '2px' };

    expect(overrides(components?.MuiButtonBase)).toMatchObject({ '&.Mui-focusVisible': ring });
    expect(overrides(components?.MuiCssBaseline)).toMatchObject({
      ':focus-visible:not(.MuiInputBase-input)': ring,
    });
    expect(overrides(components?.MuiOutlinedInput)).toMatchObject({
      '&.Mui-focused:not(.Mui-error) .MuiOutlinedInput-notchedOutline': {
        borderColor: '#006D65',
        borderWidth: 2,
      },
    });
  });

  it('sets the brand as text in primary-text', () => {
    const { components } = createAppTheme(TEAL);

    expect(overrides(components?.MuiLink)).toMatchObject({
      variants: [{ props: { color: 'primary' }, style: { color: '#006D65' } }],
    });
    expect(overrides(components?.MuiButton)).toMatchObject({
      variants: expect.arrayContaining([
        { props: { variant: 'text', color: 'primary' }, style: { color: '#006D65' } },
        {
          props: { variant: 'outlined', color: 'primary' },
          style: { color: '#006D65', borderColor: '#07534D' },
        },
      ]),
    });
  });

  it('raises a contained key on its lip and presses it down', () => {
    const { light } = deriveBrand('#0F766E', '#7C2D12');
    const variants = buttonVariants(createAppTheme(TEAL));
    const primary = variants.find(
      (v) => v.props.variant === 'contained' && v.props.color === 'primary',
    );

    expect(primary?.style).toMatchObject({
      boxShadow: `inset 0 0 0 1px #07534D, 0 2px 0 ${light['primary-lip']}`,
      '&:active': {
        transform: 'translateY(2px)',
        backgroundColor: light['primary-pressed'],
      },
    });
  });

  it('draws a plain key on the control face', () => {
    const { control, 'control-pressed': pressed } = instrument.palette.light.instrument;
    const plain = buttonVariants(createAppTheme({ mode: 'light' })).find(
      (v) => v.props.variant === 'outlined' && v.props.color === undefined,
    );

    expect(plain?.style).toMatchObject({
      backgroundColor: control,
      '&:active': { backgroundColor: pressed },
    });
  });

  it('draws a ghost key as the text variant', () => {
    const { 'ghost-hover': hover, 'ghost-pressed': pressed } = instrument.palette.light.instrument;
    const ghost = buttonVariants(createAppTheme({ mode: 'light' })).find(
      (v) => v.props.variant === 'text' && v.props.color === undefined,
    );

    expect(ghost?.style).toMatchObject({
      '&:hover': { backgroundColor: hover },
      '&:active': { backgroundColor: pressed },
    });
  });

  it("times a key's travel with Instrument's press and quick durations", () => {
    const root = overrides(createAppTheme().components?.MuiButton) as { transition: string };

    expect(root.transition).toContain('transform 70ms');
    expect(root.transition).toContain('background-color 120ms');
  });

  it('cuts a well into the surface', () => {
    const { shade, well } = instrument.palette.light.instrument;

    expect(overrides(createAppTheme({ mode: 'light' }).components?.MuiOutlinedInput)).toMatchObject(
      {
        backgroundColor: well,
        boxShadow: `inset 0 2px 0 ${shade}`,
        '& .MuiOutlinedInput-notchedOutline': {
          borderColor: instrument.palette.light.instrument['border-strong'],
        },
      },
    );
  });

  it('squares the icon key to the control radius', () => {
    expect(overrides(createAppTheme().components?.MuiIconButton)).toMatchObject({
      borderRadius: 6,
    });
  });

  it('presses keys without a ripple', () => {
    expect(createAppTheme().components?.MuiButtonBase?.defaultProps).toEqual({
      disableRipple: true,
    });
  });

  it('takes no elevation overlay on a dark sheet', () => {
    const { components } = createAppTheme({ mode: 'dark' });

    expect(overrides(components?.MuiPaper)).toEqual({ backgroundImage: 'none' });
  });

  it('rounds a card to the module radius on a hairline edge', () => {
    expect(overrides(createAppTheme({ mode: 'light' }).components?.MuiCard)).toEqual({
      borderRadius: 14,
      border: `1px solid ${instrument.palette.light.instrument.border}`,
    });
  });

  it('dims the page under a dialog with the scrim', () => {
    const { scrim } = instrument.palette.light.instrument;

    expect(overrides(createAppTheme({ mode: 'light' }).components?.MuiBackdrop)).toEqual({
      variants: [{ props: { invisible: false }, style: { backgroundColor: scrim } }],
    });
  });

  it('makes a dialog a sheet on a phone', () => {
    const theme = createAppTheme();
    const phone = theme.breakpoints.down('sm');
    const dialog = theme.components?.MuiDialog?.styleOverrides as Record<string, unknown>;

    expect(dialog.paper).toMatchObject({
      borderRadius: 14,
      [phone]: {
        margin: 0,
        width: '100%',
        maxWidth: '100%',
        borderBottomLeftRadius: 0,
        borderBottomRightRadius: 0,
      },
    });
    expect(dialog.container).toEqual({ [phone]: { alignItems: 'flex-end' } });
  });

  it('draws a tooltip as a small screen', () => {
    const { light } = deriveBrand('#0F766E', '#7C2D12');
    const tooltip = createAppTheme(TEAL).components?.MuiTooltip?.styleOverrides as Record<
      string,
      unknown
    >;

    expect(tooltip.tooltip).toMatchObject({
      backgroundColor: light.screen,
      color: light['on-screen'],
      borderRadius: 4,
    });
    expect(tooltip.arrow).toEqual({ color: light.screen });
  });

  it("engraves a table's head on a sunken ground", () => {
    const { label } = instrument.typography.variants;
    const cell = createAppTheme({ mode: 'light' }).components?.MuiTableCell?.styleOverrides as {
      head: Record<string, unknown>;
    };

    expect(cell.head).toMatchObject({
      textTransform: label.textTransform,
      letterSpacing: label.letterSpacing,
      backgroundColor: instrument.palette.light.instrument['surface-sunken'],
    });
    expect(cell.head.textTransform).toBe('uppercase');
  });

  it.each([
    ['success', 'success'],
    ['info', 'info'],
    ['warning', 'warning'],
    ['error', 'danger'],
  ] as const)('marks each alert with its signal bar: %s', (severity, role) => {
    const colors = instrument.palette.light.instrument;
    const variants = (
      overrides(createAppTheme({ mode: 'light' }).components?.MuiAlert) as {
        variants: { props: { severity?: string }; style: Record<string, unknown> }[];
      }
    ).variants;
    const entry = variants.find((v) => v.props.severity === severity);

    expect(entry?.style).toMatchObject({
      backgroundColor: colors[`${role}-subtle`],
      border: `1px solid ${colors[`${role}-border`]}`,
      '&::before': { background: colors[`${role}-mark`] },
    });
  });

  it("tapes a caution's signal bar", () => {
    const { hazard } = instrument.palette.light.instrument;
    const variants = (
      overrides(createAppTheme({ mode: 'light' }).components?.MuiAlert) as {
        variants: { props: { severity?: string }; style: Record<string, unknown> }[];
      }
    ).variants;
    const tape = variants.filter((v) => v.props.severity === 'warning').at(-1);
    const bar = tape?.style['&::before'] as { background: string };

    expect(bar.background).toMatch(/^repeating-linear-gradient\(-45deg, /);
    expect(bar.background).toContain(hazard);
  });

  it('maps the code, label, field, value and readout variants to their elements', () => {
    expect(createAppTheme().components?.MuiTypography?.defaultProps?.variantMapping).toEqual({
      code: 'code',
      label: 'span',
      field: 'span',
      value: 'span',
      readout: 'span',
    });
  });
});
