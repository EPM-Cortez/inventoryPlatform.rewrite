import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react';
import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';

import { describeError } from '@/api/problem';
import { cn } from '@/lib/cn';

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  tone: ToastTone;
  title: string;
  description?: string;
  /** Set while the exit animation plays, just before the toast is removed. */
  leaving?: boolean;
}

interface ToastContextValue {
  show: (toast: Omit<Toast, 'id'>) => void;
  success: (title: string, description?: string) => void;
  error: (title: string, description?: string) => void;
  /** Convenience for mutation `onError`: renders the API's own message. */
  fromError: (error: unknown, title?: string) => void;
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const value = useContext(ToastContext);
  if (!value) throw new Error('useToast must be used inside <ToastProvider>.');
  return value;
}

const TONE_STYLES: Record<ToastTone, { wrap: string; icon: ReactNode }> = {
  success: {
    wrap: 'border-success-border bg-success-soft text-success',
    icon: <CheckCircle2 />,
  },
  error: { wrap: 'border-danger-border bg-danger-soft text-danger', icon: <XCircle /> },
  warning: {
    wrap: 'border-warning-border bg-warning-soft text-warning',
    icon: <AlertTriangle />,
  },
  info: { wrap: 'border-info-border bg-info-soft text-info', icon: <Info /> },
};

const AUTO_DISMISS_MS = 6000;
/** Must stay in step with the exit animation in the markup below. */
const EXIT_MS = 130;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  /**
   * Dismissal is two-phase: mark the toast as leaving so its exit animation can
   * play, then remove it. Deleting the node outright would make it disappear on
   * the same frame, and a stack of toasts would jump as the gap closed.
   *
   * The removal is on a timer rather than `animationend` because a toast
   * dismissed while the tab is in the background never fires that event, and the
   * toast would then be stuck on screen forever.
   */
  const dismiss = useCallback((id: number) => {
    setToasts((current) =>
      current.map((toast) => (toast.id === id ? { ...toast, leaving: true } : toast)),
    );
    window.setTimeout(() => {
      setToasts((current) => current.filter((toast) => toast.id !== id));
    }, EXIT_MS);
  }, []);

  const show = useCallback(
    (toast: Omit<Toast, 'id'>) => {
      const id = nextId.current++;
      setToasts((current) => [...current, { ...toast, id }]);
      // Errors stay until dismissed: they usually need reading and acting on.
      if (toast.tone !== 'error') {
        window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
      }
    },
    [dismiss],
  );

  const value = useMemo<ToastContextValue>(
    () => ({
      show,
      dismiss,
      success: (title, description) => show({ tone: 'success', title, description }),
      error: (title, description) => show({ tone: 'error', title, description }),
      fromError: (error, title = 'That did not work') =>
        show({ tone: 'error', title, description: describeError(error) }),
    }),
    [dismiss, show],
  );

  return (
    <ToastContext value={value}>
      {children}
      <div
        // `polite` rather than `assertive`: these announcements should not
        // interrupt whatever the user is typing.
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed bottom-4 right-4 z-100 flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={cn(
              'pointer-events-auto flex items-start gap-2.5 rounded-lg border p-3 shadow-lg',
              'animate-[toast-in_var(--duration-base)_var(--ease-enter)]',
              // Leaving is driven by the `leaving` flag rather than unmounting
              // straight away, so a dismissed toast is not simply deleted.
              toast.leaving &&
                'animate-[toast-out_var(--duration-fast)_var(--ease-exit)_forwards]',
              TONE_STYLES[toast.tone].wrap,
            )}
          >
            <span className="mt-px shrink-0 [&_svg]:size-4">
              {TONE_STYLES[toast.tone].icon}
            </span>
            <div className="min-w-0 flex-1">
              <p className="text-base font-medium">{toast.title}</p>
              {toast.description && (
                <p className="mt-0.5 text-sm opacity-90">{toast.description}</p>
              )}
            </div>
            <button
              type="button"
              onClick={() => dismiss(toast.id)}
              className="-m-1 shrink-0 rounded-sm p-1 opacity-60 transition-opacity hover:opacity-100"
              aria-label="Dismiss"
            >
              <X className="size-3.5" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext>
  );
}
