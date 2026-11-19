import type { ReactNode } from 'react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import brace from '@ncodegroup/identity-brand/brace.json';
import instrument from '@ncodegroup/identity-brand/instrument.json';
import { Brace } from './Brace.js';

const GAP = instrument.space['3'];

// A brace's box spans the card's height, outside its edge.
const BRACE_BOX = { position: 'absolute', top: 0, bottom: 0, lineHeight: 0 } as const;

// brace-lg beside a card, brace-md beside a card on a phone (NCode Brand's guidelines/04-shape-and-depth.md).
const LARGE = { xs: 'none', sm: 'block' } as const;
const MEDIUM = { xs: 'block', sm: 'none' } as const;

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
      <Box sx={{ ...BRACE_BOX, right: `calc(100% + ${String(GAP)}px)`, display: LARGE }}>
        <Brace size="lg" />
      </Box>
      <Box sx={{ ...BRACE_BOX, right: `calc(100% + ${String(GAP)}px)`, display: MEDIUM }}>
        <Brace size="md" />
      </Box>
      <Box
        sx={{
          ...BRACE_BOX,
          left: `calc(100% + ${String(GAP)}px)`,
          transform: 'scaleX(-1)',
          display: LARGE,
        }}
      >
        <Brace size="lg" />
      </Box>
      <Box
        sx={{
          ...BRACE_BOX,
          left: `calc(100% + ${String(GAP)}px)`,
          transform: 'scaleX(-1)',
          display: MEDIUM,
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
