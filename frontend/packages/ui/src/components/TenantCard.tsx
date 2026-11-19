import type { ReactNode } from 'react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import brace from '@ncodegroup/identity-brand/brace.json';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import { Brace } from './Brace.js';

const GAP = instrument.space['3'];

const LEFT = { position: 'absolute', top: 0, bottom: 0, lineHeight: 0 } as const;

export type TenantCardProps = {
  /** The widest the card grows, in pixels, between its braces. */
  readonly maxWidth: number;
  /** The content of the card. */
  readonly children: ReactNode;
};

/**
 * The card of a page a tenant shows its user, such as sign-in, consent or an authorization error: a raised plate
 * between two braces in the tenant's scope ink, `brace-lg` and `brace-md` on a phone (NCode Brand's
 * guidelines/04-shape-and-depth.md).
 */
export function TenantCard({ maxWidth, children }: TenantCardProps) {
  const lg = { xs: 'none', sm: 'block' };
  const md = { xs: 'block', sm: 'none' };
  return (
    <Box
      sx={(theme) => ({
        position: 'relative',
        width: {
          xs: `calc(100% - ${String(2 * (brace.md.width + GAP))}px)`,
          sm: `calc(100% - ${String(2 * (brace.lg.width + GAP))}px)`,
        },
        maxWidth,
        color: theme.palette.instrument['scope-tenant'],
      })}
    >
      <Box sx={{ ...LEFT, right: `calc(100% + ${String(GAP)}px)`, display: lg }}>
        <Brace size="lg" />
      </Box>
      <Box sx={{ ...LEFT, right: `calc(100% + ${String(GAP)}px)`, display: md }}>
        <Brace size="md" />
      </Box>
      <Box
        sx={{
          ...LEFT,
          left: `calc(100% + ${String(GAP)}px)`,
          transform: 'scaleX(-1)',
          display: lg,
        }}
      >
        <Brace size="lg" />
      </Box>
      <Box
        sx={{
          ...LEFT,
          left: `calc(100% + ${String(GAP)}px)`,
          transform: 'scaleX(-1)',
          display: md,
        }}
      >
        <Brace size="md" />
      </Box>
      <Card elevation={4} sx={{ width: '100%' }}>
        {children}
      </Card>
    </Box>
  );
}
