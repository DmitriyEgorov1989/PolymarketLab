// @vitest-environment jsdom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CollectorStatusBadge } from './CollectorStatusBadge';

describe('CollectorStatusBadge', () => {
  it('renders the backend status as text', () => {
    render(<CollectorStatusBadge status="Running" />);

    expect(screen.getByText('Running')).toBeTruthy();
  });

  it('renders an unknown runtime status safely', () => {
    render(<CollectorStatusBadge status="Unexpected" />);

    expect(screen.getByText('Unknown')).toBeTruthy();
  });
});
