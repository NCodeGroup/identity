import type { ReactNode } from 'react';
import Box from '@mui/material/Box';
import instrument from '@ncodegroup/identity-brand/instrument.json';

export type HazardBandProps = {
  /** The band's text. */
  readonly children: ReactNode;
};

/**
 * NCode Brand's hazard band, the banner of a non-production tenant: its text on the hazard color between two runs of
 * hazard tape, which give way before the text wraps (NCode Brand's guidelines/04-shape-and-depth.md).
 */
export function HazardBand({ children }: HazardBandProps) {
  return (
    <Box
      sx={(theme) => {
        const { hazard, 'on-hazard': onHazard } = theme.palette.instrument;
        const tape = {
          content: '""',
          flex: '1 0 1.5rem',
          maxWidth: '6rem',
          background: `repeating-linear-gradient(-45deg, ${onHazard} 0 8px, ${hazard} 8px 16px)`,
        };
        return {
          display: 'flex',
          alignItems: 'stretch',
          justifyContent: 'space-between',
          minHeight: '2.25rem',
          backgroundColor: hazard,
          color: onHazard,
          typography: 'field',
          '&::before': tape,
          '&::after': tape,
        };
      }}
    >
      <Box
        sx={{
          flex: '0 1 auto',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: `${String(instrument.space['3'])}px`,
          py: '0.375rem',
          px: `${String(instrument.space['5'])}px`,
          textAlign: 'center',
        }}
      >
        {children}
      </Box>
    </Box>
  );
}
