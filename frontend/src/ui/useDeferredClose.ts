import { useCallback, useState } from 'react';

/**
 * Lets an overlay finish its exit animation before the parent unmounts it.
 *
 * This app mounts dialogs and drawers only while they are open — that is what
 * keeps their state fresh on each open and avoids the focus race with a menu
 * item that opened them. The cost is that a close request unmounts the Radix
 * root on the same tick, so the content never reaches `data-state="closed"` and
 * whatever exit animation it declares never runs: the panel just disappears.
 *
 * So the close is held locally first. The content closes, Radix keeps it mounted
 * while the animation plays, and only then is the parent told — which is when it
 * unmounts the whole thing.
 *
 * The hand-off is on a timer rather than `animationend` because that event never
 * fires for an animation on a background tab, and the overlay would then be
 * stuck open until the user returned to it.
 */
export function useDeferredClose(
  onOpenChange: (open: boolean) => void,
  exitMs: number,
): { closing: boolean; requestClose: () => void } {
  const [closing, setClosing] = useState(false);

  const requestClose = useCallback(() => {
    setClosing(true);
    window.setTimeout(() => onOpenChange(false), exitMs);
  }, [exitMs, onOpenChange]);

  return { closing, requestClose };
}
