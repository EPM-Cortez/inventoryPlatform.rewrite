import { cn } from '@/lib/cn';

export function Spinner({ className }: { className?: string }) {
  return (
    <svg
      className={cn('size-4 animate-spin', className)}
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden="true"
      // Exempt from the reduced-motion cut-off: this rotation *is* the message.
      // A frozen spinner says "hung", which is worse than the movement.
      data-motion="essential"
    >
      <circle cx="8" cy="8" r="6.5" stroke="currentColor" strokeOpacity="0.25" strokeWidth="2" />
      <path
        d="M14.5 8A6.5 6.5 0 0 0 8 1.5"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

/** Centred spinner for a whole panel or route that is still loading. */
export function LoadingBlock({ label = 'Loading…' }: { label?: string }) {
  return (
    <div
      className="flex min-h-40 flex-col items-center justify-center gap-3 text-text-subtle"
      role="status"
    >
      <Spinner className="size-5" />
      <span className="text-sm">{label}</span>
    </div>
  );
}
