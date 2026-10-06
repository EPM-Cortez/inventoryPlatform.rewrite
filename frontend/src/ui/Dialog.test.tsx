import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { Dialog } from './Dialog';

function Harness({ dismissible }: { dismissible: boolean }) {
  const [open, setOpen] = useState(true);
  return (
    <Dialog
      open={open}
      onOpenChange={setOpen}
      title="Recovery token"
      dismissible={dismissible}
      footer={<button onClick={() => setOpen(false)}>I have copied it</button>}
    >
      <p>irk_secret</p>
    </Dialog>
  );
}

describe('Dialog', () => {
  it('closes on Escape when it is dismissible', async () => {
    const user = userEvent.setup();
    render(<Harness dismissible />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    // The exit animation is deferred, so wait for the unmount.
    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  /**
   * Regression guard for the one-time-secret dialogs.
   *
   * The enrollment token and the agent key-recovery token are shown exactly
   * once — the server keeps only a hash. If Escape could dismiss those, a reflex
   * keypress would destroy a credential with no way to get it back, so the only
   * way out has to be the explicit button.
   */
  describe('dismissible={false}', () => {
    it('does not close on Escape', async () => {
      const user = userEvent.setup();
      render(<Harness dismissible={false} />);

      await user.keyboard('{Escape}');
      await new Promise((resolve) => setTimeout(resolve, 200));
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByText('irk_secret')).toBeInTheDocument();
    });

    it('does not close on a click outside', async () => {
      render(<Harness dismissible={false} />);

      // Dispatched rather than driven through user-event: a modal Radix dialog
      // makes the rest of the page inert, so user-event refuses to click there
      // at all. Going straight at the handler is what actually exercises the
      // guard, and the event must be cancelable or `preventDefault` is a no-op.
      document.body.dispatchEvent(
        new MouseEvent('pointerdown', { bubbles: true, cancelable: true }),
      );
      await new Promise((resolve) => setTimeout(resolve, 200));
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });

    it('offers no close affordance except the explicit action', async () => {
      const user = userEvent.setup();
      render(<Harness dismissible={false} />);

      expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'I have copied it' }));
      await vi.waitFor(() =>
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
      );
    });
  });
});
