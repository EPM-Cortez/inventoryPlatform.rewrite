import { cn } from '@/lib/cn';

export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        'relative overflow-hidden rounded-sm bg-surface-sunken',
        'after:absolute after:inset-0 after:-translate-x-full',
        // The sweep is what says "loading" rather than "empty", so it keeps
        // running under reduced motion — it moves no layout and no text.
        'after:animate-[shimmer_1.6s_infinite]',
        'after:bg-linear-to-r after:from-transparent after:via-black/[0.04] after:to-transparent',
        'dark:after:via-white/[0.05]',
        className,
      )}
      aria-hidden="true"
      data-motion="essential"
    />
  );
}
