import { RefreshCw } from 'lucide-react';

import { Button } from '@/ui/Button';
import { CopyButton } from '@/ui/CopyButton';
import { Field, Input } from '@/ui/Field';

export interface TemporaryPasswordFieldProps {
  value: string;
  /** Omit to render read-only, e.g. after the reset has already happened. */
  onRegenerate?: () => void;
  error?: string | null;
}

/**
 * Shows a generated single-use password with copy and regenerate.
 *
 * It is deliberately visible rather than masked: an administrator has to read it
 * out or paste it somewhere, and masking a value you must transcribe just invites
 * mistakes. It is never stored, and the account must change it at first sign-in.
 */
export function TemporaryPasswordField({
  value,
  onRegenerate,
  error,
}: TemporaryPasswordFieldProps) {
  return (
    <Field
      label="Temporary password"
      required
      error={error ?? null}
      hint="Generated in your browser. Hand it over in person or through a separate trusted channel — never in the same email as the username."
    >
      {(controlProps) => (
        <div className="flex items-center gap-2">
          <div className="relative flex-1">
            <Input
              {...controlProps}
              value={value}
              readOnly
              mono
              className="pr-9"
              // Selecting the whole value is almost always the next action.
              onFocus={(event) => event.currentTarget.select()}
            />
            <span className="absolute right-1.5 top-1/2 -translate-y-1/2">
              <CopyButton value={value} label="Copy temporary password" />
            </span>
          </div>
          {onRegenerate && (
            <Button size="sm" iconLeft={<RefreshCw />} onClick={onRegenerate}>
              New
            </Button>
          )}
        </div>
      )}
    </Field>
  );
}
