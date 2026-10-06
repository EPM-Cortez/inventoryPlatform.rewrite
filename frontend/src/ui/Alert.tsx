import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

export type AlertTone = 'info' | 'success' | 'warning' | 'danger';

const TONES: Record<AlertTone, { wrap: string; icon: ReactNode }> = {
  info: { wrap: 'border-info-border bg-info-soft text-info', icon: <Info /> },
  success: {
    wrap: 'border-success-border bg-success-soft text-success',
    icon: <CheckCircle2 />,
  },
  warning: {
    wrap: 'border-warning-border bg-warning-soft text-warning',
    icon: <AlertTriangle />,
  },
  danger: { wrap: 'border-danger-border bg-danger-soft text-danger', icon: <XCircle /> },
};

export interface AlertProps {
  tone?: AlertTone;
  title?: ReactNode;
  children?: ReactNode;
  className?: string;
  /** `alert` interrupts a screen reader; use it for submit failures. */
  role?: 'alert' | 'status';
}

/** Inline message tied to a specific form or panel, not a floating toast. */
export function Alert({
  tone = 'info',
  title,
  children,
  className,
  role,
}: AlertProps) {
  return (
    <div
      role={role}
      className={cn(
        'flex items-start gap-2.5 rounded-md border p-3 text-sm',
        TONES[tone].wrap,
        className,
      )}
    >
      <span className="mt-px shrink-0 [&_svg]:size-4">{TONES[tone].icon}</span>
      <div className="min-w-0 flex-1">
        {title && <p className="text-base font-medium">{title}</p>}
        {children && <div className={cn(title && 'mt-0.5', 'opacity-95')}>{children}</div>}
      </div>
    </div>
  );
}
