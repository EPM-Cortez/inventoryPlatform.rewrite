import { Save, TriangleAlert } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import type { SchoolSettings } from '@/api/adminTypes';
import { useSchoolSettings, useUpdateSchoolSettings } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { ErrorState } from '@/ui/EmptyState';
import { Field, Input } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

import { AssetTagPolicyCard } from './AssetTagPolicyCard';

/**
 * Time zone ids offered as suggestions.
 *
 * The server resolves these with `TimeZoneInfo.FindSystemTimeZoneById`, so what
 * is valid depends on the host, not on this list — the input stays free text and
 * these are only shortcuts.
 */
const TIME_ZONE_SUGGESTIONS = [
  'Europe/London',
  'GMT Standard Time',
  'UTC',
  'Europe/Dublin',
  'Europe/Paris',
];

export default function SettingsPage() {
  const settings = useSchoolSettings();

  if (settings.isLoading) return <LoadingBlock label="Loading settings…" />;
  if (settings.isError) {
    return <ErrorState error={settings.error} onRetry={() => void settings.refetch()} />;
  }
  if (!settings.data) return null;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-5">
      <PageHeader
        title="School settings"
        description="Identity, time zone and the values the register derives from them."
      />

      {!settings.data.isPersisted && (
        <Alert tone="info" title="These are still the configured defaults">
          Nothing has been saved yet, so the app is running on the values in its
          configuration file. Saving here takes over from them permanently.
        </Alert>
      )}

      {/* Keyed on the version so a save — which issues a new version — remounts
          the form with the server's values rather than leaving stale state. */}
      <SettingsForm key={settings.data.version ?? 'unsaved'} settings={settings.data} />

      <AssetTagPolicyCard />
    </div>
  );
}

function SettingsForm({ settings }: { settings: SchoolSettings }) {
  const [name, setName] = useState(settings.name);
  const [code, setCode] = useState(settings.code);
  const [timeZone, setTimeZone] = useState(settings.timeZone);
  const [qrBaseUrl, setQrBaseUrl] = useState(settings.qrBaseUrl);
  const [warrantyWarningDays, setWarrantyWarningDays] = useState(
    String(settings.warrantyWarningDays),
  );

  const update = useUpdateSchoolSettings();
  const toast = useToast();

  const days = Number(warrantyWarningDays);
  const daysValid = Number.isInteger(days) && days >= 1 && days <= 365;
  // The server requires an absolute HTTPS URL; catching it here avoids a
  // round-trip that would otherwise reject a whole form for one field.
  const qrUrlValid = isHttpsUrl(qrBaseUrl.trim());
  const qrBaseChanged = qrBaseUrl.trim() !== settings.qrBaseUrl;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    update.mutate(
      {
        name: name.trim(),
        code: code.trim(),
        timeZone: timeZone.trim(),
        qrBaseUrl: qrBaseUrl.trim(),
        warrantyWarningDays: days,
        // The first save must not quote a version; the server rejects one when
        // nothing has been persisted yet.
        version: settings.isPersisted ? settings.version : null,
      },
      {
        onSuccess: () => toast.success('Settings saved'),
      },
    );
  }

  const error = update.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-5">
      {apiError?.isConcurrencyConflict ? (
        <Alert tone="danger" title="Someone else changed these settings" role="alert">
          Reload the page to pick up their version, then re-apply your change.
        </Alert>
      ) : error ? (
        <Alert tone="danger" title="Could not save" role="alert">
          {describeError(error)}
        </Alert>
      ) : null}

      <Card>
        <CardHeader
          title="Identity"
          description="Shown in the interface and written into exports."
        />
        <CardBody className="grid gap-4 sm:grid-cols-2">
          <Field label="School name" required>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={200}
                required
              />
            )}
          </Field>

          <Field label="Code" required hint="Short identifier used in exports.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={code}
                onChange={(event) => setCode(event.target.value)}
                maxLength={50}
                required
                mono
              />
            )}
          </Field>

          <Field
            label="Time zone"
            required
            hint="Must be an id the server's operating system recognises."
            error={
              apiError?.slug === 'invalid-time-zone'
                ? 'The server does not recognise that time zone.'
                : undefined
            }
          >
            {(controlProps) => (
              <>
                <Input
                  {...controlProps}
                  value={timeZone}
                  onChange={(event) => setTimeZone(event.target.value)}
                  maxLength={100}
                  required
                  list="time-zone-suggestions"
                />
                <datalist id="time-zone-suggestions">
                  {TIME_ZONE_SUGGESTIONS.map((zone) => (
                    <option key={zone} value={zone} />
                  ))}
                </datalist>
              </>
            )}
          </Field>

          <Field
            label="Warranty warning"
            required
            hint="Days before expiry that coverage starts being flagged. 1 to 365."
            error={
              warrantyWarningDays !== '' && !daysValid
                ? 'Enter a whole number between 1 and 365.'
                : undefined
            }
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="number"
                min={1}
                max={365}
                step={1}
                value={warrantyWarningDays}
                onChange={(event) => setWarrantyWarningDays(event.target.value)}
                required
              />
            )}
          </Field>
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          title="QR labels"
          description="The address a scanned label resolves to."
        />
        <CardBody className="flex flex-col gap-4">
          <Field
            label="QR base URL"
            required
            hint="Absolute HTTPS address. Each label encodes this followed by its token."
            error={
              qrBaseUrl.trim() !== '' && !qrUrlValid
                ? 'Must be an absolute https:// URL.'
                : undefined
            }
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="url"
                value={qrBaseUrl}
                onChange={(event) => setQrBaseUrl(event.target.value)}
                maxLength={500}
                required
                mono
                placeholder="https://inventory.school.internal/scan"
              />
            )}
          </Field>

          {qrBaseChanged && (
            // Labels are physically stuck to equipment; this is the one setting
            // whose change cannot be undone by editing it back.
            <Alert tone="warning" title="Labels already printed will point at the old address">
              <p className="flex items-start gap-2">
                <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
                <span>
                  Every QR code printed so far encodes{' '}
                  <code className="font-mono text-xs">{settings.qrBaseUrl}</code>. They
                  keep working only while that address still resolves. Change this only
                  when the site is genuinely moving, and plan to reprint.
                </span>
              </p>
            </Alert>
          )}
        </CardBody>
      </Card>

      <div className="flex justify-end">
        <Button
          type="submit"
          variant="primary"
          iconLeft={<Save />}
          loading={update.isPending}
          disabled={!daysValid || !qrUrlValid || !name.trim() || !code.trim()}
        >
          Save settings
        </Button>
      </div>
    </form>
  );
}

function isHttpsUrl(value: string): boolean {
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}
