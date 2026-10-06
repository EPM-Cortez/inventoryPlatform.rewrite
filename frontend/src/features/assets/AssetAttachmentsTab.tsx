import { Download, FileText, Image as ImageIcon, Paperclip, Trash2, Upload } from 'lucide-react';
import { useState, type ChangeEvent } from 'react';

import {
  useArchiveAttachment,
  useAssetAttachments,
  useUploadAttachment,
} from '@/api/hooks';
import {
  ATTACHMENT_ACCEPT,
  ATTACHMENT_CATEGORIES,
  ATTACHMENT_MAX_BYTES,
  type AssetAttachment,
  type AttachmentCategory,
} from '@/api/operationsTypes';
import { ApiError, describeError } from '@/api/problem';
import { attachmentsApi } from '@/api/endpoints';
import type { Asset } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { downloadBlob } from '@/lib/download';
import { formatBytes, formatDateTime, formatRelative } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { CopyButton } from '@/ui/CopyButton';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Field, NativeSelect } from '@/ui/Field';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

const PAGE_SIZE = 25;

const CATEGORY_LABELS: Record<AttachmentCategory, string> = {
  invoice: 'Invoice',
  warrantyDocument: 'Warranty document',
  disposalCertificate: 'Disposal certificate',
  photo: 'Photo',
  other: 'Other',
};

/**
 * Files kept against an asset — invoices, warranty paperwork, disposal
 * certificates, photographs of damage.
 *
 * Nothing is ever hard-deleted: archiving hides a file from the working list but
 * keeps it retrievable, which is the behaviour an auditor needs from a disposal
 * certificate.
 */
export function AssetAttachmentsTab({ asset }: { asset: Asset }) {
  const { can } = useSession();
  const [includeArchived, setIncludeArchived] = useState(false);
  const [page, setPage] = useState(1);
  const [archiving, setArchiving] = useState<AssetAttachment | null>(null);

  const canManage = can(Permission.AttachmentsManage);
  const attachments = useAssetAttachments(asset.id, includeArchived, page, PAGE_SIZE);
  const archive = useArchiveAttachment(asset.id);
  const toast = useToast();

  async function download(attachment: AssetAttachment) {
    try {
      const file = await attachmentsApi.download(asset.id, attachment.id);
      downloadBlob(file.blob, file.fileName);
    } catch (error) {
      toast.fromError(error, 'Could not download that file');
    }
  }

  const columns: Column<AssetAttachment>[] = [
    {
      id: 'file',
      header: 'File',
      cell: (attachment) => (
        <div className="flex min-w-0 items-center gap-2.5">
          {attachment.contentType.startsWith('image/') ? (
            <ImageIcon className="size-4 shrink-0 text-text-subtle" aria-hidden />
          ) : (
            <FileText className="size-4 shrink-0 text-text-subtle" aria-hidden />
          )}
          <div className="min-w-0">
            <p className="truncate text-text">{attachment.fileName}</p>
            <p className="truncate text-2xs text-text-subtle">
              {formatBytes(attachment.sizeBytes)} · uploaded{' '}
              {formatRelative(attachment.createdAtUtc)} by{' '}
              {attachment.createdByDisplayName}
            </p>
          </div>
        </div>
      ),
    },
    {
      id: 'category',
      header: 'Kind',
      width: 'w-44',
      cell: (attachment) => (
        <Badge tone="neutral">{CATEGORY_LABELS[attachment.category]}</Badge>
      ),
    },
    {
      id: 'state',
      header: 'State',
      width: 'w-32',
      cell: (attachment) =>
        attachment.archivedAtUtc ? (
          <Tooltip
            content={`Archived ${formatDateTime(attachment.archivedAtUtc)} by ${
              attachment.archivedByDisplayName
            }. Still downloadable.`}
          >
            <span className="inline-block">
              <Badge tone="neutral" dot>
                Archived
              </Badge>
            </span>
          </Tooltip>
        ) : (
          <Badge tone="success" dot>
            Current
          </Badge>
        ),
    },
    {
      id: 'checksum',
      header: 'SHA-256',
      hideBelow: 'lg',
      width: 'w-40',
      cell: (attachment) => (
        <span className="flex items-center gap-1">
          <span className="truncate font-mono text-2xs text-text-subtle">
            {attachment.sha256.slice(0, 12)}…
          </span>
          <CopyButton value={attachment.sha256} label="Copy checksum" />
        </span>
      ),
    },
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-24',
      cell: (attachment) => (
        <div className="flex items-center justify-end gap-1">
          <IconButton
            label={`Download ${attachment.fileName}`}
            size="sm"
            onClick={() => void download(attachment)}
          >
            <Download />
          </IconButton>
          {canManage && !attachment.archivedAtUtc && (
            <IconButton
              label={`Archive ${attachment.fileName}`}
              size="sm"
              onClick={() => setArchiving(attachment)}
            >
              <Trash2 />
            </IconButton>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      {canManage && <UploadPanel assetId={asset.id} />}

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <Checkbox
            checked={includeArchived}
            onCheckedChange={(checked) => {
              setIncludeArchived(checked);
              setPage(1);
            }}
            label="Include archived"
          />
        </div>

        {attachments.isError ? (
          <ErrorState
            error={attachments.error}
            onRetry={() => void attachments.refetch()}
          />
        ) : (
          <DataTable
            columns={columns}
            rows={attachments.data?.items ?? []}
            rowKey={(attachment) => attachment.id}
            loading={attachments.isLoading}
            skeletonRows={3}
            rowClassName={(attachment) =>
              attachment.archivedAtUtc ? 'opacity-60' : undefined
            }
            empty={
              <EmptyState
                icon={<Paperclip />}
                title="No files attached"
                description="Keep the invoice, warranty paperwork or a photograph of damage here, alongside the asset."
              />
            }
          />
        )}
      </Card>

      {archiving && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setArchiving(null)}
          title="Archive this file?"
          description={
            <>
              <strong>{archiving.fileName}</strong> drops out of the working list but is
              not deleted — it stays downloadable, and an auditor can still find it.
            </>
          }
          confirmLabel="Archive it"
          loading={archive.isPending}
          onConfirm={() =>
            archive.mutate(
              { attachmentId: archiving.id, version: archiving.version },
              {
                onSuccess: () => {
                  toast.success('File archived', archiving.fileName);
                  setArchiving(null);
                },
                onError: (error) => {
                  toast.fromError(error, 'Could not archive it');
                  setArchiving(null);
                },
              },
            )
          }
        />
      )}
    </div>
  );
}

function UploadPanel({ assetId }: { assetId: string }) {
  const [category, setCategory] = useState<AttachmentCategory>('invoice');
  const [tooLarge, setTooLarge] = useState<string | null>(null);
  const upload = useUploadAttachment(assetId);
  const toast = useToast();

  function onFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset so choosing the same file twice still fires a change event.
    event.target.value = '';
    if (!file) return;

    // Checked here as well as server-side: a 10 MiB upload that is going to be
    // rejected is a slow way to learn it was too big, especially on school wifi.
    if (file.size > ATTACHMENT_MAX_BYTES) {
      setTooLarge(
        `${file.name} is ${formatBytes(file.size)}. The limit is ${formatBytes(
          ATTACHMENT_MAX_BYTES,
        )}.`,
      );
      return;
    }

    setTooLarge(null);
    upload.mutate(
      { category, file },
      { onSuccess: (saved) => toast.success('File attached', saved.fileName) },
    );
  }

  const error = upload.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Card>
      <div className="flex flex-col gap-3 p-4">
        {tooLarge && (
          <Alert tone="warning" title="That file is too big" role="alert">
            {tooLarge}
          </Alert>
        )}

        {apiError?.slug === 'attachment-malware-detected' ? (
          <Alert tone="danger" title="That file was rejected as unsafe" role="alert">
            The scanner flagged it, so nothing was stored. Do not try to work around
            this — check the file on the machine it came from.
          </Alert>
        ) : apiError?.slug === 'attachment-scan-unavailable' ? (
          <Alert tone="warning" title="The virus scanner is not responding" role="alert">
            Nothing was stored. Files are only accepted once they can be scanned, so try
            again shortly or tell whoever looks after the server.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not attach that file" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="flex flex-wrap items-end gap-3">
          <Field label="Kind of file" className="w-56">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={category}
                onChange={(event) =>
                  setCategory(event.target.value as AttachmentCategory)
                }
              >
                {ATTACHMENT_CATEGORIES.map((value) => (
                  <option key={value} value={value}>
                    {CATEGORY_LABELS[value]}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>

          <Button variant="primary" iconLeft={<Upload />} loading={upload.isPending} asChild>
            <label className="cursor-pointer">
              Attach a file
              <input
                type="file"
                accept={ATTACHMENT_ACCEPT}
                className="sr-only"
                onChange={onFileChange}
              />
            </label>
          </Button>

          <p className="text-xs text-text-subtle">
            PDF, JPEG or PNG · up to {formatBytes(ATTACHMENT_MAX_BYTES)}
          </p>
        </div>
      </div>
    </Card>
  );
}
