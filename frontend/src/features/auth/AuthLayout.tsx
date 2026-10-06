import type { ReactNode } from 'react';

const SCHOOL_NAME = import.meta.env.VITE_SCHOOL_NAME || 'Inventory';

export interface AuthLayoutProps {
  title: string;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}

/** Centred card used by every unauthenticated screen. */
export function AuthLayout({ title, description, children, footer }: AuthLayoutProps) {
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center bg-canvas px-4 py-10">
      <div className="animate-view w-full max-w-sm">
        <div className="mb-6 flex items-center gap-2.5">
          <div className="flex size-9 items-center justify-center rounded-lg bg-accent text-accent-fg">
            <svg viewBox="0 0 24 24" className="size-5" fill="none" aria-hidden="true">
              <path
                d="M12 3 3.5 7.5v9L12 21l8.5-4.5v-9L12 3Z"
                stroke="currentColor"
                strokeWidth="1.75"
                strokeLinejoin="round"
              />
              <path
                d="M3.5 7.5 12 12m0 0 8.5-4.5M12 12v9"
                stroke="currentColor"
                strokeWidth="1.75"
                strokeLinejoin="round"
              />
            </svg>
          </div>
          <div>
            <p className="text-lg font-semibold tracking-[-0.01em] text-text">
              {SCHOOL_NAME}
            </p>
            <p className="text-xs text-text-subtle">Inventory</p>
          </div>
        </div>

        <div className="rounded-xl border border-border bg-surface p-6 shadow-md">
          <h1 className="text-2xl font-semibold tracking-[-0.02em] text-text">{title}</h1>
          {description && <p className="mt-1.5 text-base text-text-muted">{description}</p>}
          <div className="mt-6">{children}</div>
        </div>

        {footer && <div className="mt-4 text-center text-sm text-text-muted">{footer}</div>}
      </div>
    </div>
  );
}
