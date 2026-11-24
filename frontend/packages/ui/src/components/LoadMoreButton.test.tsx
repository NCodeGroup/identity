import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LoadMoreButton } from './LoadMoreButton.js';

describe('LoadMoreButton', () => {
  it('renders nothing when there are no more pages', () => {
    render(<LoadMoreButton hasMore={false} isLoading={false} onLoadMore={vi.fn()} />);

    expect(screen.queryByTestId('load-more')).not.toBeInTheDocument();
  });

  it('calls onLoadMore when clicked', () => {
    const onLoadMore = vi.fn();
    render(<LoadMoreButton hasMore isLoading={false} onLoadMore={onLoadMore} />);

    fireEvent.click(screen.getByTestId('load-more'));

    expect(onLoadMore).toHaveBeenCalledTimes(1);
  });

  it('is disabled and shows progress while loading', () => {
    const onLoadMore = vi.fn();
    render(<LoadMoreButton hasMore isLoading onLoadMore={onLoadMore} />);

    expect(screen.getByTestId('load-more')).toBeDisabled();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('load-more'));
    expect(onLoadMore).not.toHaveBeenCalled();
  });

  it('uses a custom test id', () => {
    render(<LoadMoreButton hasMore isLoading={false} onLoadMore={vi.fn()} testId="more-clients" />);

    expect(screen.getByTestId('more-clients')).toHaveTextContent('Load more');
  });
});
