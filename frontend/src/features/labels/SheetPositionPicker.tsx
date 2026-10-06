import { cn } from '@/lib/cn';

export interface SheetPositionPickerProps {
  columns: number;
  rows: number;
  labelWidthMillimetres: number;
  labelHeightMillimetres: number;
  /** 1-based position of the first label to print on the first sheet. */
  startPosition: number;
  onStartPositionChange: (position: number) => void;
  /** Total labels queued, used to shade what this sheet will consume. */
  labelCount: number;
  disabled?: boolean;
}

/**
 * A to-scale map of the label sheet.
 *
 * Part-used sheets are the normal case in a school office — someone peels off
 * four labels, and the rest of the sheet sits in a drawer. Asking for a start
 * position as a number would mean counting cells by hand every time; showing the
 * sheet and letting them click the first empty label removes that entirely.
 *
 * Positions fill left-to-right, top-to-bottom, matching how the server lays the
 * PDF out.
 */
export function SheetPositionPicker({
  columns,
  rows,
  labelWidthMillimetres,
  labelHeightMillimetres,
  startPosition,
  onStartPositionChange,
  labelCount,
  disabled = false,
}: SheetPositionPickerProps) {
  const perPage = columns * rows;
  const positions = Array.from({ length: perPage }, (_, index) => index + 1);
  const lastUsedOnFirstSheet = Math.min(perPage, startPosition + labelCount - 1);

  return (
    <div className="flex flex-col gap-3">
      <div
        role="radiogroup"
        aria-label="First label position on the sheet"
        className="mx-auto grid w-full max-w-70 gap-1 rounded-md border border-border-strong bg-surface p-2 shadow-sm"
        style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
      >
        {positions.map((position) => {
          const alreadyUsed = position < startPosition;
          const willPrint = position >= startPosition && position <= lastUsedOnFirstSheet;

          return (
            <button
              key={position}
              type="button"
              role="radio"
              aria-checked={position === startPosition}
              aria-label={
                alreadyUsed
                  ? `Position ${position}, marked as already used`
                  : willPrint
                    ? `Position ${position}, will be printed`
                    : `Position ${position}, empty`
              }
              disabled={disabled}
              onClick={() => onStartPositionChange(position)}
              // The real label is wider than it is tall; keeping that ratio makes
              // the picker read as the sheet in your hand rather than a grid.
              style={{
                aspectRatio: `${labelWidthMillimetres} / ${labelHeightMillimetres}`,
              }}
              className={cn(
                'flex items-center justify-center rounded-xs border text-2xs tabular-nums transition-colors',
                'disabled:cursor-not-allowed disabled:opacity-50',
                position === startPosition &&
                  'border-accent bg-accent text-accent-fg font-semibold',
                position !== startPosition &&
                  willPrint &&
                  'border-accent-soft bg-accent-soft text-accent-soft-fg',
                alreadyUsed &&
                  // Diagonal hatch reads as "gone" without relying on colour.
                  'border-border bg-[repeating-linear-gradient(135deg,transparent,transparent_3px,var(--border)_3px,var(--border)_4px)] text-text-subtle',
                !alreadyUsed &&
                  !willPrint &&
                  'border-border text-text-subtle hover:border-border-strong hover:bg-surface-hover',
              )}
            >
              {position}
            </button>
          );
        })}
      </div>

      <div className="flex flex-wrap items-center justify-center gap-x-4 gap-y-1 text-2xs text-text-subtle">
        <Legend className="border-border bg-[repeating-linear-gradient(135deg,transparent,transparent_3px,var(--border)_3px,var(--border)_4px)]">
          Already used
        </Legend>
        <Legend className="border-accent bg-accent">Starts here</Legend>
        <Legend className="border-accent-soft bg-accent-soft">Will print</Legend>
        <Legend className="border-border bg-surface">Left empty</Legend>
      </div>

      <p className="text-center text-xs text-text-muted">
        Click the first empty label on your sheet. Positions before it are skipped so a
        part-used sheet is not wasted.
      </p>
    </div>
  );
}

function Legend({ className, children }: { className?: string; children: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={cn('size-3 shrink-0 rounded-xs border', className)} aria-hidden />
      {children}
    </span>
  );
}
