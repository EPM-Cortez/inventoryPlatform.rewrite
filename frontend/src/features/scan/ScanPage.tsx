import { ArrowRight, Camera, CameraOff, ScanLine, Search } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { useResolveQr } from '@/api/hooks';
import { ApiError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader, DataItem } from '@/ui/Card';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Field, Input } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { AssetConditionPill, AssetStatusPill, WarrantyStatePill } from '@/ui/StatusPill';

/**
 * Minimal shape of the Barcode Detection API. It is not in TypeScript's DOM
 * library and is unavailable in Safari and Firefox, so every use is behind a
 * feature check with a manual-entry fallback.
 */
interface DetectedBarcode {
  rawValue: string;
}
interface BarcodeDetectorLike {
  detect: (source: CanvasImageSource) => Promise<DetectedBarcode[]>;
}
declare global {
  interface Window {
    BarcodeDetector?: {
      new (options?: { formats?: string[] }): BarcodeDetectorLike;
      getSupportedFormats?: () => Promise<string[]>;
    };
  }
}

/**
 * A label encodes `{School:QrBaseUrl}/{token}`, and that base is configured to end
 * in `/scan`. Accept the whole URL, a bare token, or anything in between, and take
 * the last non-empty path segment.
 */
export function extractToken(input: string): string {
  const trimmed = input.trim();
  if (!trimmed) return '';

  try {
    const url = new URL(trimmed);
    const segments = url.pathname.split('/').filter(Boolean);
    return segments[segments.length - 1] ?? '';
  } catch {
    // Not a URL. Strip any path-like prefix a partial paste may have left.
    const segments = trimmed.split('/').filter(Boolean);
    return segments[segments.length - 1] ?? '';
  }
}

export default function ScanPage() {
  const { token } = useParams<{ token?: string }>();
  const navigate = useNavigate();
  const [manualEntry, setManualEntry] = useState('');

  const resolution = useResolveQr(token);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const extracted = extractToken(manualEntry);
    if (extracted) void navigate(`/scan/${encodeURIComponent(extracted)}`);
  }

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-5">
      <PageHeader
        title="Scan a label"
        description="Resolve a QR label to the asset it identifies. Scanning shows only summary details — open the asset for the full record."
      />

      {token ? (
        <>
          {resolution.isLoading ? (
            <LoadingBlock label="Resolving label…" />
          ) : resolution.isError ? (
            <ResolveError error={resolution.error} />
          ) : resolution.data ? (
            <Card>
              <CardHeader
                title={
                  <span className="font-mono tracking-tight">
                    {resolution.data.assetTag}
                  </span>
                }
                description={resolution.data.category.name}
                actions={
                  <Button variant="primary" asChild>
                    <Link to={`/assets/${resolution.data.id}`}>
                      Open asset <ArrowRight className="ml-1.5 size-4" />
                    </Link>
                  </Button>
                }
              />
              <CardBody>
                <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-4">
                  <DataItem label="Status">
                    <AssetStatusPill status={resolution.data.status} />
                  </DataItem>
                  <DataItem label="Condition">
                    <AssetConditionPill condition={resolution.data.condition} />
                  </DataItem>
                  <DataItem label="Location">{resolution.data.location?.name}</DataItem>
                  <DataItem label="Warranty">
                    <WarrantyStatePill state={resolution.data.warrantySummary.state} />
                  </DataItem>
                </dl>
              </CardBody>
            </Card>
          ) : null}

          <div>
            <Button onClick={() => void navigate('/scan')} iconLeft={<ScanLine />}>
              Scan another
            </Button>
          </div>
        </>
      ) : (
        <>
          <CameraScanner onToken={(value) => void navigate(`/scan/${encodeURIComponent(value)}`)} />

          <Card>
            <CardHeader
              title="Enter it by hand"
              description="Paste the label URL, or type the token printed beneath the code."
            />
            <CardBody>
              <form onSubmit={onSubmit} className="flex items-end gap-2">
                <Field label="Label URL or token" hideLabel className="flex-1">
                  {(controlProps) => (
                    <Input
                      {...controlProps}
                      value={manualEntry}
                      onChange={(event) => setManualEntry(event.target.value)}
                      placeholder="https://inventory.example.school/scan/…"
                      mono
                    />
                  )}
                </Field>
                <Button
                  type="submit"
                  variant="primary"
                  iconLeft={<Search />}
                  disabled={!extractToken(manualEntry)}
                >
                  Look up
                </Button>
              </form>
            </CardBody>
          </Card>
        </>
      )}
    </div>
  );
}

function ResolveError({ error }: { error: unknown }) {
  // The API deliberately returns the same non-sensitive 404 for an unknown token
  // and for one that has been rotated out, so the message covers both.
  if (error instanceof ApiError && error.isNotFound) {
    return (
      <EmptyState
        icon={<ScanLine />}
        title="That label is not recognised"
        description="Either the code does not belong to this inventory, or its identifier has been rotated and the label needs reprinting."
        action={
          <Button asChild>
            <Link to="/scan">Try another label</Link>
          </Button>
        }
      />
    );
  }

  if (error instanceof ApiError && error.isRateLimited) {
    return (
      <Alert tone="warning" title="Too many lookups">
        Label resolution is rate limited to protect the register. Wait a minute and try
        again.
      </Alert>
    );
  }

  return <ErrorState error={error} />;
}

function CameraScanner({ onToken }: { onToken: (token: string) => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [active, setActive] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const supported =
    typeof window !== 'undefined' &&
    'BarcodeDetector' in window &&
    typeof navigator !== 'undefined' &&
    Boolean(navigator.mediaDevices?.getUserMedia);

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    setActive(false);
  }, []);

  // Releasing the camera on unmount matters: the browser keeps the recording
  // indicator lit otherwise, which is alarming and looks like a bug.
  useEffect(() => stop, [stop]);

  async function start() {
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment' },
      });
      streamRef.current = stream;
      setActive(true);
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play();
      }
      void detectLoop();
    } catch {
      setError(
        'Could not open the camera. Check the browser permission, or enter the label by hand below.',
      );
      stop();
    }
  }

  async function detectLoop() {
    const Detector = window.BarcodeDetector;
    if (!Detector) return;
    const detector = new Detector({ formats: ['qr_code'] });

    while (streamRef.current && videoRef.current) {
      try {
        const codes = await detector.detect(videoRef.current);
        const value = codes[0]?.rawValue;
        if (value) {
          const token = extractToken(value);
          if (token) {
            stop();
            onToken(token);
            return;
          }
        }
      } catch {
        // A transient decode failure is normal between frames; keep looking.
      }
      await new Promise((resolve) => window.setTimeout(resolve, 250));
    }
  }

  if (!supported) {
    return (
      <Alert tone="info" title="Camera scanning is not available in this browser">
        Chrome and Edge on Android and desktop can scan directly. In Safari and Firefox,
        use your device&rsquo;s camera app to open the label, or paste the URL below.
      </Alert>
    );
  }

  return (
    <Card>
      <CardHeader
        title="Use the camera"
        description="Point the camera at the QR label on the asset."
        actions={
          active ? (
            <Button iconLeft={<CameraOff />} onClick={stop}>
              Stop
            </Button>
          ) : (
            <Button variant="primary" iconLeft={<Camera />} onClick={() => void start()}>
              Start camera
            </Button>
          )
        }
      />
      <CardBody>
        {error && (
          <Alert tone="danger" className="mb-3">
            {error}
          </Alert>
        )}
        <div className="relative mx-auto aspect-square w-full max-w-sm overflow-hidden rounded-lg border border-border bg-surface-sunken">
          <video
            ref={videoRef}
            playsInline
            muted
            className="size-full object-cover"
            aria-label="Camera preview"
          />
          {!active && (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-text-subtle">
              <Camera className="size-6" />
              <p className="text-sm">The camera is off</p>
            </div>
          )}
          {active && (
            <div
              className="pointer-events-none absolute inset-8 rounded-lg border-2 border-accent/70"
              aria-hidden="true"
            />
          )}
        </div>
      </CardBody>
    </Card>
  );
}
