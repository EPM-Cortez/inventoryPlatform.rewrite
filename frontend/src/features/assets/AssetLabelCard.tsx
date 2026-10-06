import { useQuery } from '@tanstack/react-query';
import { Download, MoreHorizontal, Printer, QrCode, RefreshCw } from 'lucide-react';
import { useState } from 'react';

import { qrApi } from '@/api/endpoints';
import { useIssueQr, useRotateQr } from '@/api/hooks';
import { ApiError } from '@/api/problem';
import { keys } from '@/api/queryKeys';
import type { Asset, QrImageFormat } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime } from '@/lib/format';
import { Button, IconButton } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { ErrorState } from '@/ui/EmptyState';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '@/ui/Menu';
import { Skeleton } from '@/ui/Skeleton';
import { useToast } from '@/ui/Toast';

import { PrintLabelDialog } from '../labels/PrintLabelDialog';

/**
 * The asset's QR label, kept beside the record rather than on a tab of its own:
 * checking a label exists and printing it is a glance-and-click job.
 *
 * Only rendered for holders of `qr.generate`.
 */
export function AssetLabelCard({ asset }: { asset: Asset }) {
  const { can } = useSession();
  const toast = useToast();
  const [rotateOpen, setRotateOpen] = useState(false);
  const [printOpen, setPrintOpen] = useState(false);

  const issue = useIssueQr(asset.id);
  const rotate = useRotateQr(asset.id);

  // Rendering 404s until an identifier has been issued, which is how we know
  // whether this asset has a label at all — there is no metadata GET.
  //
  // The SVG is kept as text rather than a blob so the `src` below is a pure
  // derivation. An object URL would need creating in an effect and revoking on
  // cleanup, and a missed revoke pins the image in memory for the session.
  const image = useQuery({
    queryKey: [...keys.assets.qr(asset.id), 'svg'],
    queryFn: async ({ signal }) => (await qrApi.image(asset.id, 'svg', signal)).text(),
    retry: false,
    // The API sends `no-store`; caching the label in the client would defeat that.
    gcTime: 0,
    staleTime: 0,
  });

  // `<img src>` renders SVG in a sandbox: no scripts, no external fetches.
  const imageSrc = image.data
    ? `data:image/svg+xml;charset=utf-8,${encodeURIComponent(image.data)}`
    : null;

  const notIssued = image.error instanceof ApiError && image.error.isNotFound;
  const latest = rotate.data ?? issue.data;

  async function download(format: QrImageFormat) {
    try {
      const blob = await qrApi.image(asset.id, format);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${asset.assetTag}-qr.${format}`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      toast.fromError(error, 'Could not download the label');
    }
  }

  return (
    <Card>
      <CardHeader
        title="QR label"
        actions={
          imageSrc && (
            <Menu>
              <MenuTrigger asChild>
                <IconButton label="Label options" size="xs">
                  <MoreHorizontal />
                </IconButton>
              </MenuTrigger>
              <MenuContent>
                <MenuItem onSelect={() => void download('svg')}>
                  <Download />
                  Download SVG
                </MenuItem>
                <MenuItem onSelect={() => void download('png')}>
                  <Download />
                  Download PNG
                </MenuItem>
                {can(Permission.QrRotate) && (
                  <>
                    <MenuSeparator />
                    <MenuItem destructive onSelect={() => setRotateOpen(true)}>
                      <RefreshCw />
                      Rotate identifier…
                    </MenuItem>
                  </>
                )}
              </MenuContent>
            </Menu>
          )
        }
      />
      <CardBody>
        {image.isLoading ? (
          <div className="flex items-center gap-3">
            <Skeleton className="size-24 shrink-0 rounded-md" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-8 w-full" />
            </div>
          </div>
        ) : notIssued ? (
          <div className="flex flex-col gap-3">
            <p className="text-sm text-text-muted">
              No label has been issued yet. Issue one to print it and stick it on the
              equipment.
            </p>
            <Button
              size="sm"
              iconLeft={<QrCode />}
              loading={issue.isPending}
              onClick={() =>
                issue.mutate(undefined, {
                  onSuccess: () => {
                    toast.success('QR identifier issued');
                    void image.refetch();
                  },
                  onError: (error) => toast.fromError(error, 'Could not issue a QR code'),
                })
              }
            >
              Issue QR label
            </Button>
          </div>
        ) : image.isError ? (
          <ErrorState error={image.error} onRetry={() => void image.refetch()} />
        ) : (
          <div className="flex items-center gap-4">
            {/* The QR itself must stay dark-on-white in both themes to scan. */}
            <div className="shrink-0 rounded-md border border-border bg-white p-1.5">
              <img
                src={imageSrc ?? ''}
                alt={`QR label for ${asset.assetTag}`}
                className="size-20"
              />
            </div>
            <div className="flex min-w-0 flex-1 flex-col gap-2">
              <p className="truncate font-mono text-sm tracking-tight text-text">
                {asset.assetTag}
              </p>
              {latest && (
                <p className="text-2xs text-text-subtle">
                  Token v{latest.tokenVersion} · issued {formatDateTime(latest.issuedAtUtc)}
                </p>
              )}
              {can(Permission.QrPrint) && (
                <Button
                  size="sm"
                  iconLeft={<Printer />}
                  onClick={() => setPrintOpen(true)}
                  className="self-start"
                >
                  Print label
                </Button>
              )}
            </div>
          </div>
        )}

        <p className="mt-3 text-2xs text-text-subtle">
          A label encodes a private link to this asset. Keep it in the label workflow —
          never in exports, spreadsheets or logs.
        </p>
      </CardBody>

      <ConfirmDialog
        open={rotateOpen}
        onOpenChange={setRotateOpen}
        title="Rotate this QR identifier?"
        description={
          <>
            The current label for <strong>{asset.assetTag}</strong> will stop working
            straight away and scanning it will report the asset as unknown. Rotate when a
            label has been copied, photographed or otherwise exposed, then print and apply
            the new one.
          </>
        }
        confirmLabel="Rotate identifier"
        destructive
        loading={rotate.isPending}
        onConfirm={() =>
          rotate.mutate(undefined, {
            onSuccess: () => {
              toast.success('QR identifier rotated', 'Print and apply the new label.');
              setRotateOpen(false);
              void image.refetch();
            },
            onError: (error) => toast.fromError(error, 'Could not rotate'),
          })
        }
      />

      {printOpen && (
        <PrintLabelDialog
          onClose={() => setPrintOpen(false)}
          assetId={asset.id}
          assetTag={asset.assetTag}
        />
      )}
    </Card>
  );
}
