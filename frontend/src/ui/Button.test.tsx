import { render, screen } from '@testing-library/react';
import { ArrowRight } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { Button, IconButton } from './Button';

describe('Button', () => {
  it('renders a real button by default', () => {
    render(<Button>Save</Button>);
    const button = screen.getByRole('button', { name: 'Save' });
    // Defaulting to `submit` inside a form is a classic accidental-submit bug.
    expect(button).toHaveAttribute('type', 'button');
  });

  it('renders icons around the label', () => {
    render(
      <Button iconLeft={<ArrowRight data-testid="left" />} iconRight={<ArrowRight data-testid="right" />}>
        Go
      </Button>,
    );
    expect(screen.getByTestId('left')).toBeInTheDocument();
    expect(screen.getByTestId('right')).toBeInTheDocument();
  });

  it('blocks interaction while loading', () => {
    render(<Button loading>Saving</Button>);
    const button = screen.getByRole('button');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
  });

  /**
   * Regression: `asChild` previously rendered `{iconLeft}{children}{iconRight}`
   * into Radix `Slot`, handing it three children. `Children.only` threw, and
   * because these buttons sit on the dashboard and the 404 page it blanked the
   * whole route rather than degrading.
   */
  describe('asChild', () => {
    it('renders the child element and keeps the button styling', () => {
      render(
        <Button asChild variant="primary">
          <a href="/assets">
            Open <ArrowRight data-testid="chevron" />
          </a>
        </Button>,
      );

      const link = screen.getByRole('link', { name: /Open/ });
      expect(link).toHaveAttribute('href', '/assets');
      expect(link.className).toContain('inline-flex');
      // Content composed inside the child survives.
      expect(screen.getByTestId('chevron')).toBeInTheDocument();
      // It must not have become a <button>.
      expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('does not leak a type attribute onto a non-button element', () => {
      render(
        <Button asChild>
          <a href="/x">Link</a>
        </Button>,
      );
      expect(screen.getByRole('link')).not.toHaveAttribute('type');
    });
  });
});

describe('IconButton', () => {
  it('names itself for assistive technology', () => {
    render(
      <IconButton label="Close panel">
        <ArrowRight />
      </IconButton>,
    );
    expect(screen.getByRole('button', { name: 'Close panel' })).toBeInTheDocument();
  });
});
