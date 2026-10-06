import {
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type Ref,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';

import { cn } from '@/lib/cn';

const CONTROL_BASE =
  'w-full rounded-md border bg-surface px-2.5 text-base text-text shadow-sm ' +
  'placeholder:text-text-subtle transition-colors ' +
  'disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-text-subtle ' +
  'read-only:bg-surface-sunken';

const CONTROL_TONE =
  'border-border-strong hover:border-text-subtle focus:border-accent';

const CONTROL_INVALID =
  'border-danger-border bg-danger-soft/40 hover:border-danger focus:border-danger';

export interface FieldProps {
  label: string;
  /** Marks the control required and shows the visual cue. */
  required?: boolean;
  /** Static guidance shown under the control while there is no error. */
  hint?: ReactNode;
  error?: string | null;
  /** Renders the label for screen readers only, for dense toolbars. */
  hideLabel?: boolean;
  className?: string;
  children: (controlProps: {
    id: string;
    'aria-describedby': string | undefined;
    'aria-invalid': true | undefined;
  }) => ReactNode;
}

/**
 * Wires a label, hint and error message to a control with the right ARIA
 * relationships. Always prefer this over hand-rolling a `<label>` so error text
 * is actually announced.
 */
export function Field({
  label,
  required,
  hint,
  error,
  hideLabel,
  className,
  children,
}: FieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = error ? errorId : hint ? hintId : undefined;

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label
        htmlFor={id}
        className={cn(
          'text-sm font-medium text-text',
          hideLabel && 'sr-only',
        )}
      >
        {label}
        {required && (
          <span className="ml-0.5 text-danger" aria-hidden="true">
            *
          </span>
        )}
      </label>

      {children({
        id,
        'aria-describedby': describedBy,
        'aria-invalid': error ? true : undefined,
      })}

      {error ? (
        <p id={errorId} className="text-xs text-danger" role="alert">
          {error}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-xs text-text-subtle">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean;
  /** Renders in the monospace face — use for asset tags, serials and codes. */
  mono?: boolean;
  /**
   * Declared explicitly because React 19 passes `ref` as an ordinary prop, so a
   * function component only receives it if its props type says so. Needed where
   * focus has to be driven — a scan field that refocuses itself after each entry.
   */
  ref?: Ref<HTMLInputElement>;
}

export function Input({ className, invalid, mono, ...props }: InputProps) {
  return (
    <input
      className={cn(
        CONTROL_BASE,
        'h-9',
        invalid ?? props['aria-invalid'] ? CONTROL_INVALID : CONTROL_TONE,
        mono && 'font-mono text-sm tracking-tight',
        className,
      )}
      {...props}
    />
  );
}

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean;
}

export function Textarea({ className, invalid, rows = 3, ...props }: TextareaProps) {
  return (
    <textarea
      rows={rows}
      className={cn(
        CONTROL_BASE,
        'resize-y py-2 leading-relaxed',
        invalid ?? props['aria-invalid'] ? CONTROL_INVALID : CONTROL_TONE,
        className,
      )}
      {...props}
    />
  );
}

export interface NativeSelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  invalid?: boolean;
}

/**
 * A plain `<select>`. Preferred for short, fixed option lists (status, condition,
 * person type) because it is faster to operate and works on touch without a
 * custom popover. Use `Combobox` when the list is long or needs search.
 */
export function NativeSelect({ className, invalid, children, ...props }: NativeSelectProps) {
  return (
    <div className="relative">
      <select
        className={cn(
          CONTROL_BASE,
          'h-9 appearance-none pr-8',
          invalid ?? props['aria-invalid'] ? CONTROL_INVALID : CONTROL_TONE,
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <svg
        className="pointer-events-none absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-text-subtle"
        viewBox="0 0 16 16"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="m4 6 4 4 4-4"
          stroke="currentColor"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </div>
  );
}
