// @vitest-environment jsdom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MarketTokens } from './MarketTokens';

describe('MarketTokens', () => {
  it('renders outcomes, indexes, and token ids without numeric conversion', () => {
    const longTokenId = '12345678901234567890123456789012345678901234567890';

    render(
      <MarketTokens
        tokens={[
          { outcome: 'Yes', outcomeIndex: 0, tokenId: longTokenId },
          { outcome: 'No', outcomeIndex: 1, tokenId: 'token-no' },
        ]}
      />,
    );

    expect(screen.getByText('Yes')).toBeTruthy();
    expect(screen.getByText('Outcome index: 0')).toBeTruthy();
    expect(screen.getByText(longTokenId).textContent).toBe(longTokenId);
    expect(screen.getByText('No')).toBeTruthy();
    expect(screen.getByText('Outcome index: 1')).toBeTruthy();
    expect(screen.getByText('token-no')).toBeTruthy();
  });

  it('renders a dedicated empty state', () => {
    render(<MarketTokens tokens={[]} />);

    expect(screen.getByText('У этого рынка нет токенов.')).toBeTruthy();
  });
});
