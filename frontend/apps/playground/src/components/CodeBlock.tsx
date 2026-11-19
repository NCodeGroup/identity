import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

export type CodeBlockProps = {
  /** Optional caption shown above the block. */
  readonly label?: string;
  /** The text to display in the code style (a URL, token, or JSON document). */
  readonly value: string;
  /** Marks the content as sensitive so it renders on Instrument's warning wash. */
  readonly sensitive?: boolean;
};

/**
 * Stateless block in the theme's code style for a URL, token, or JSON document; a sensitive value takes Instrument's
 * warning wash. Wraps long values and preserves whitespace so the artifact is legible without truncation. Prop-driven
 * so it renders in isolation.
 */
export function CodeBlock({ label, value, sensitive = false }: CodeBlockProps) {
  return (
    <Box>
      {label ? (
        <Typography variant="caption" component="div" gutterBottom sx={{ color: 'text.secondary' }}>
          {label}
        </Typography>
      ) : null}
      <Box
        component="pre"
        data-testid="code-block"
        sx={{
          m: 0,
          p: 1.5,
          borderRadius: 1,
          typography: 'code',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all',
          overflowX: 'auto',
          bgcolor: sensitive ? 'instrument.warning-subtle' : 'action.hover',
          border: '1px solid',
          borderColor: sensitive ? 'instrument.warning-border' : 'divider',
        }}
      >
        {value}
      </Box>
    </Box>
  );
}
