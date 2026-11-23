import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';

export type LoadMoreButtonProps = {
  readonly hasMore: boolean;
  readonly isLoading: boolean;
  readonly onLoadMore: () => void;
  readonly testId?: string;
};

/**
 * Stateless control below a paged list that fetches the next page. It renders nothing once every page is loaded
 * and is disabled, with a progress indicator, while a page is loading.
 */
export function LoadMoreButton({
  hasMore,
  isLoading,
  onLoadMore,
  testId = 'load-more',
}: LoadMoreButtonProps) {
  if (!hasMore) {
    return null;
  }
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', py: 1 }}>
      <Button
        variant="outlined"
        data-testid={testId}
        disabled={isLoading}
        startIcon={isLoading ? <CircularProgress size={16} /> : undefined}
        onClick={onLoadMore}
      >
        Load more
      </Button>
    </Box>
  );
}
