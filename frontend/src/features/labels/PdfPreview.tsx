import { Download, ExternalLink, Printer } from 'lucide-react';
import { useRef } from 'react';

import type { LabelDocument } from '@/api/types';
import { formatNumber } from '@/lib/format';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';

export interface PdfPreviewProps {
  document: LabelDocument;
  /** Object URL for `document.blob`, owned and revoked by the parent. */
  url: string;
}

/**
 * Preview, print and download for a generated label PDF.
 *
 * Printing goes through a hidden iframe rather than the visible preview so the
 * browser prints the PDF alone, without the surrounding page. Scaling is the
 * thing that ruins label alignment, so the guidance to print at 100% is stated
 * where the print button is, not buried in documentation.
 */
export function PdfPreview({ document: label, url }: PdfPreviewProps) {
  const printFrame = useRef<HTMLIFrameElement>(null);

  function print() {
    const frame = printFrame.current;
    if (!frame) return;
    // The PDF viewer needs to be loaded before `print()` will do anything.
    frame.contentWindow?.focus();
    frame.contentWindow?.print();
  }

  function download() {
    const link = window.document.createElement('a');
    link.href = url;
    link.download = label.fileName;
    link.click();
  }

  return (
    <Card className="flex min-h-0 flex-col">
      <CardHeader
        title="Ready to print"
        description={
          <span className="flex flex-wrap items-center gap-2">
            <Badge tone="success">
              {formatNumber(label.labelCount)}{' '}
              {label.labelCount === 1 ? 'label' : 'labels'}
            </Badge>
            <Badge tone="neutral">
              {formatNumber(label.pageCount)} {label.pageCount === 1 ? 'page' : 'pages'}
            </Badge>
            <span className="font-mono text-2xs text-text-subtle">{label.fileName}</span>
          </span>
        }
        actions={
          <>
            <Button size="sm" iconLeft={<Download />} onClick={download}>
              Download
            </Button>
            <Button size="sm" asChild>
              <a href={url} target="_blank" rel="noreferrer">
                <ExternalLink className="mr-1.5 size-4" />
                New tab
              </a>
            </Button>
            <Button size="sm" variant="primary" iconLeft={<Printer />} onClick={print}>
              Print
            </Button>
          </>
        }
      />
      <CardBody className="flex min-h-0 flex-1 flex-col gap-3 p-3">
        <p className="rounded-md border border-warning-border bg-warning-soft px-3 py-2 text-xs text-warning">
          Print at <strong>Actual size</strong> (100%). &ldquo;Fit to page&rdquo; and
          &ldquo;Shrink oversized pages&rdquo; both move every label out of alignment
          with the die-cut sheet.
        </p>

        <iframe
          src={url}
          title={`Preview of ${label.fileName}`}
          className="h-140 w-full rounded-md border border-border bg-surface-sunken"
        />

        {/* Kept out of the layout purely so `print()` targets the PDF alone. */}
        <iframe ref={printFrame} src={url} title="Print frame" className="hidden" />
      </CardBody>
    </Card>
  );
}
