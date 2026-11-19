import type { CSSProperties } from 'react';
import {
  createTheme,
  type CSSProperties as MuiCSSProperties,
  type Shadows,
  type Theme,
  type ThemeOptions,
} from '@mui/material/styles';
import { outlinedInputClasses } from '@mui/material/OutlinedInput';
import { deriveBrand, type DerivedColors } from '@ncodegroup/identity-brand/derive';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import type { BrandingConfig } from '@ncodegroup/identity-config';

/** A theme's color mode. */
export type ColorMode = 'light' | 'dark';

/** Every Instrument color role by its token name, as "#RRGGBB" or an `rgb()` value. */
export type InstrumentColors = Readonly<
  Record<keyof (typeof instrument)['palette']['light']['instrument'], string>
>;

/** The numbers Instrument measures a layout in, by name, in pixels. */
export type InstrumentMeasures = {
  /** The corner radii. */
  readonly radius: Readonly<Record<keyof (typeof instrument)['radius'], number>>;
  /** The spacing scale. */
  readonly space: Readonly<Record<keyof (typeof instrument)['space'], number>>;
  /** The control heights and the other fixed sizes. */
  readonly size: Readonly<Record<keyof (typeof instrument)['size'], number>>;
  /** The focus ring's width and its offset from the element. */
  readonly focus: { readonly width: number; readonly offset: number };
};

/** What the reader's browser says, which the theme cannot read from the branding. */
export type AppThemeOptions = {
  /** The reader's system color mode, used when the branding names none. */
  readonly systemMode?: ColorMode;
  /** Whether the reader asks for reduced motion; every transition is then instant. */
  readonly reducedMotion?: boolean;
};

// Material UI's theme types grow by declaration merging, which only an interface takes.
declare module '@mui/material/styles' {
  interface Palette {
    instrument: InstrumentColors;
  }
  interface PaletteOptions {
    instrument?: InstrumentColors;
  }
  interface TypographyVariants {
    fontFamilyMono: string;
    label: CSSProperties;
    field: CSSProperties;
    value: CSSProperties;
    code: CSSProperties;
    readout: CSSProperties;
  }
  interface TypographyVariantsOptions {
    fontFamilyMono?: string;
    label?: CSSProperties;
    field?: CSSProperties;
    value?: CSSProperties;
    code?: CSSProperties;
    readout?: CSSProperties;
  }
  interface Theme {
    instrument: InstrumentMeasures;
  }
  interface ThemeOptions {
    instrument?: InstrumentMeasures;
  }
}

declare module '@mui/material/Typography' {
  interface TypographyPropsVariantOverrides {
    label: true;
    field: true;
    value: true;
    code: true;
    readout: true;
  }
}

type VariantData =
  (typeof instrument)['typography']['variants'][keyof (typeof instrument)['typography']['variants']];

function style(variant: VariantData): MuiCSSProperties {
  const values: Record<string, string | number> = { ...variant };
  delete values.role;
  return values as MuiCSSProperties;
}

const { variants } = instrument.typography;

const TYPOGRAPHY: ThemeOptions['typography'] = {
  fontFamily: instrument.typography.fontFamily,
  fontFamilyMono: instrument.typography.fontFamilyMono,
  fontSize: instrument.typography.fontSize,
  fontWeightRegular: instrument.typography.fontWeightRegular,
  fontWeightMedium: instrument.typography.fontWeightMedium,
  fontWeightBold: instrument.typography.fontWeightBold,
  h1: style(variants.h1),
  h2: style(variants.h2),
  h3: style(variants.h3),
  h4: style(variants.h4),
  h5: style(variants.h5),
  h6: style(variants.h6),
  subtitle1: style(variants.subtitle1),
  subtitle2: style(variants.subtitle2),
  body1: style(variants.body1),
  body2: style(variants.body2),
  button: style(variants.button),
  overline: style(variants.overline),
  label: style(variants.label),
  field: style(variants.field),
  value: style(variants.value),
  code: style(variants.code),
  readout: style(variants.readout),
};

// Instrument names the widest width of the smaller layout; Material UI names where the larger one starts.
const BREAKPOINTS = {
  values: {
    xs: 0,
    sm: instrument.breakpoints.phone + 1,
    md: instrument.breakpoints.tablet + 1,
    lg: instrument.breakpoints.desk + 1,
    xl: 1536,
  },
};

const MEASURES: InstrumentMeasures = {
  radius: instrument.radius,
  space: instrument.space,
  size: instrument.size,
  focus: instrument.focus,
};

function paletteOf(
  mode: ColorMode,
  derived: DerivedColors,
  colors: InstrumentColors,
): ThemeOptions['palette'] {
  const base = instrument.palette[mode];
  return {
    mode,
    primary: {
      main: derived.primary,
      dark: derived['primary-hover'],
      contrastText: derived['on-primary'],
    },
    // The colors a tenant's secondary drives, chosen for how Material UI paints them (guidelines/12-applying.md).
    secondary:
      mode === 'light'
        ? {
            main: derived.screen,
            dark: derived['screen-raised'],
            contrastText: derived['on-screen'],
          }
        : {
            main: derived['screen-lit'],
            dark: derived['on-screen-secondary'],
            contrastText: derived.screen,
          },
    error: base.error,
    warning: base.warning,
    info: base.info,
    success: base.success,
    background: base.background,
    text: base.text,
    divider: base.divider,
    instrument: colors,
  };
}

function transitionsOf(reducedMotion: boolean): ThemeOptions['transitions'] {
  const { duration, easing } = instrument.transitions;
  const still = reducedMotion ? 0 : undefined;
  return {
    duration: {
      shortest: still ?? duration.press,
      shorter: still ?? duration.quick,
      short: still ?? duration.quick,
      standard: still ?? duration.move,
      complex: still ?? duration.enter,
      enteringScreen: still ?? duration.enter,
      leavingScreen: still ?? duration.move,
    },
    easing: { easeOut: easing.out, easeInOut: easing['in-out'] },
    ...(reducedMotion ? { create: () => 'none' } : {}),
  };
}

function componentsOf(colors: InstrumentColors): ThemeOptions['components'] {
  const ring = {
    outline: `${String(instrument.focus.width)}px solid ${colors.focus}`,
    outlineOffset: `${String(instrument.focus.offset)}px`,
  };
  return {
    MuiCssBaseline: {
      styleOverrides: {
        // A text field shows its focus by its outline, so its input takes no ring of its own.
        ':focus-visible:not(.MuiInputBase-input)': ring,
      },
    },
    MuiButtonBase: {
      defaultProps: { disableRipple: true },
      styleOverrides: { root: { '&.Mui-focusVisible': ring } },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          [`&.Mui-focused:not(.Mui-error) .${outlinedInputClasses.notchedOutline}`]: {
            borderColor: colors.focus,
            borderWidth: instrument.focus.width,
          },
        },
      },
    },
    // The brand as text is primary-text, which holds 4.5 to 1 on the page; primary is a key's face.
    MuiLink: {
      styleOverrides: {
        root: {
          variants: [{ props: { color: 'primary' }, style: { color: colors['primary-text'] } }],
        },
      },
    },
    MuiButton: {
      styleOverrides: {
        root: {
          variants: [
            {
              props: { variant: 'text', color: 'primary' },
              style: { color: colors['primary-text'] },
            },
            {
              props: { variant: 'outlined', color: 'primary' },
              style: { color: colors['primary-text'], borderColor: colors['primary-edge'] },
            },
          ],
        },
      },
    },
    MuiTypography: {
      defaultProps: {
        variantMapping: {
          code: 'code',
          label: 'span',
          field: 'span',
          value: 'span',
          readout: 'span',
        },
      },
    },
  };
}

/**
 * Builds the application theme from NCode Brand's Instrument and the optional runtime {@link BrandingConfig}. A
 * tenant's colors pass through `deriveBrand`, which holds every pairing they touch at its contrast floor and reads a
 * color it cannot parse as NCode Blue, so the theme never throws. A branding without a mode follows the reader's system
 * mode. Branding is injected at runtime, never at build time, so one bundle themes every tenant.
 */
export function createAppTheme(branding?: BrandingConfig, options: AppThemeOptions = {}): Theme {
  const mode: ColorMode = branding?.mode ?? options.systemMode ?? 'light';
  const derived = deriveBrand(branding?.primaryColor, branding?.secondaryColor)[mode];
  const colors: InstrumentColors = { ...instrument.palette[mode].instrument, ...derived };
  return createTheme({
    palette: paletteOf(mode, derived, colors),
    typography: TYPOGRAPHY,
    shape: { borderRadius: instrument.shape.borderRadius },
    spacing: instrument.spacing,
    shadows: instrument.shadows[mode] as Shadows,
    breakpoints: BREAKPOINTS,
    transitions: transitionsOf(options.reducedMotion === true),
    components: componentsOf(colors),
    instrument: MEASURES,
  });
}
