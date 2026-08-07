// @vitest-environment jsdom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CollectorFailure } from './CollectorFailure';

describe('CollectorFailure', () => {
  it('renders the backend failure code and message', () => {
    render(<CollectorFailure failureCode="collector.websocket" failureMessage="Socket closed." />);

    expect(screen.getByText('collector.websocket')).toBeTruthy();
    expect(screen.getByText('Socket closed.')).toBeTruthy();
  });

  it('renders null fields as dashes', () => {
    render(<CollectorFailure failureCode={null} failureMessage={null} />);

    expect(screen.getAllByText('-')).toHaveLength(2);
  });
});
