import { Check, Minus } from 'lucide-react';
import { Checkbox as RadixCheckbox } from 'radix-ui';
import { useId, type ReactNode } from 'react';

import { cn } from '@/lib/cn';

export interface CheckboxProps {
  checked: boolean | 'indeterminate';
  onCheckedChange: (checked: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
  className?: string;
}

export function Checkbox({
  checked,
  onCheckedChange,
  label,
  description,
  disabled,
  className,
}: CheckboxProps) {
  const id = useId();

  return (
    <div className={cn('flex items-start gap-2.5', className)}>
      <RadixCheckbox.Root
        id={id}
        checked={checked}
        onCheckedChange={(value) => onCheckedChange(value === true)}
        disabled={disabled}
        className={cn(
          'mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-xs border shadow-sm',
          'border-border-strong bg-surface transition-colors',
          'data-[state=checked]:border-accent data-[state=checked]:bg-accent',
          'data-[state=indeterminate]:border-accent data-[state=indeterminate]:bg-accent',
          'disabled:cursor-not-allowed disabled:opacity-50',
        )}
      >
        <RadixCheckbox.Indicator className="text-accent-fg">
          {checked === 'indeterminate' ? (
            <Minus className="size-3" strokeWidth={3} />
          ) : (
            <Check className="size-3" strokeWidth={3} />
          )}
        </RadixCheckbox.Indicator>
      </RadixCheckbox.Root>

      <div className="min-w-0">
        <label
          htmlFor={id}
          className={cn(
            'cursor-pointer select-none text-base text-text',
            disabled && 'cursor-not-allowed opacity-60',
          )}
        >
          {label}
        </label>
        {description && <p className="text-xs text-text-subtle">{description}</p>}
      </div>
    </div>
  );
}
